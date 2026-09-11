using Inspection.Models;
using Serilog;

namespace MultiCameraSystem.Services
{
    /// <summary>
    /// 相机图片序号规划器。
    ///
    /// 解决的问题（原实现是一处会静默出错的缺陷）：
    /// 编排器靠 <c>QueuedFrame.ImageIndex</c> 去匹配配方的「图片索引」（<see cref="InspectionRecipe.ImageIndexes"/>）。
    /// 旧实现用一个**全局自增**计数器给所有相机发号，于是：
    /// <code>
    /// 相机A 第1张 -> 1     相机B 第1张 -> 2
    /// 相机A 第2张 -> 3     相机B 第2张 -> 4
    /// </code>
    /// 而配方期望的是「相机A 出第 1、2 张，相机B 出第 3、4 张」这类按位置划分的序号。
    /// 结果就是"检测跑过了，但结果对不上"，而且**没有任何报错** —— 现场极难排查。
    ///
    /// 现在：
    /// <list type="bullet">
    /// <item>每台相机在自己负责的序号列表里独立推进；</item>
    /// <item>序号可由相机绑定显式配置（<see cref="CameraBinding.ImageIndexes"/>），
    /// 未配置时按绑定顺序自动分配独占槽位；</item>
    /// <item>某台相机把本张料的序号用完后再来帧 → 报"新的一张料"，由上层做一次 ClearSheet。</item>
    /// </list>
    ///
    /// 之所以抽成独立类：这段逻辑决定"哪张图喂给哪个配方"，属于会静默出错的核心逻辑，
    /// 必须能脱离相机硬件与 WPF 被单独验证。
    /// </summary>
    public class CameraFrameIndexPlanner
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, int[]> _plans = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _cursors = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>自上次开始新一张料以来收到的帧数（判定"凑满一张料"用）</summary>
        private int _framesSinceReset;

        public CameraFrameIndexPlanner(ILogger logger)
        {
            _logger = logger.ForContext<CameraFrameIndexPlanner>();
        }

        /// <summary>每台相机（序列号）负责的图片序号，供日志与界面展示</summary>
        public IReadOnlyDictionary<string, int[]> Plans => _plans;

        /// <summary>按产品配置重新规划并清空游标</summary>
        public void Configure(ProductConfiguration cfg)
        {
            _plans.Clear();
            _cursors.Clear();
            _framesSinceReset = 0;

            if (cfg == null) return;

            var imageTotal = Math.Max(1, cfg.ImageTotal);
            var bindings = cfg.Cameras
                .Where(c => !string.IsNullOrWhiteSpace(c.SerialNumber))
                .OrderBy(c => c.Order)
                .ToList();

            if (bindings.Count == 0) return;

            // 1) 显式配置优先
            foreach (var b in bindings)
            {
                var explicitIndexes = b.ParseImageIndexes();
                if (explicitIndexes.Count > 0)
                    _plans[b.SerialNumber] = explicitIndexes.ToArray();
            }

            // 2) 其余按顺序占用尚未分配的槽位
            var used = new HashSet<int>();
            foreach (var indexes in _plans.Values)
            {
                foreach (var i in indexes) used.Add(i);
            }

            var autoCameras = bindings.Where(b => !_plans.ContainsKey(b.SerialNumber)).ToList();

            // 只剩一台相机需要自动规划时，它必须拿到**完整**序号区间。
            // 否则单相机（ImageTotal=3）只会拿到 [1]，一张料的第 2、3 张图永远不被采集 ——
            // 这正是"配方绑了图片索引 2/3，却永远等不到图"的成因。
            if (autoCameras.Count == 1)
            {
                var only = autoCameras[0];
                if (used.Count == 0)
                {
                    _plans[only.SerialNumber] = Enumerable.Range(1, imageTotal).ToArray();
                    _logger.Information("相机 {Name} 是唯一需要自动规划的相机，分配完整图片序号 1..{Total}",
                        only.Name, imageTotal);
                    goto LogPlans;
                }

                var freeSlots = Enumerable.Range(1, imageTotal).Where(i => !used.Contains(i)).ToArray();
                _plans[only.SerialNumber] = freeSlots.Length > 0 ? freeSlots : Enumerable.Range(1, imageTotal).ToArray();
                _logger.Information("相机 {Name} 自动分配剩余图片序号 [{Plan}]",
                    only.Name, string.Join(",", _plans[only.SerialNumber]));
                goto LogPlans;
            }

            foreach (var b in autoCameras)
            {
                var next = Enumerable.Range(1, imageTotal).FirstOrDefault(i => !used.Contains(i));
                if (next == 0)
                {
                    // 槽位不够分：退化为"全部序号"轮转（与旧行为一致，不会比原来更差）
                    _plans[b.SerialNumber] = Enumerable.Range(1, imageTotal).ToArray();
                    _logger.Warning(
                        "相机 {Name} 没有可用的独占图片序号（图片总数 {Total} 不够分），已退化为与其他相机共用 1..{Total}；" +
                        "建议在相机绑定里显式填写「图片序号」",
                        b.Name, imageTotal, imageTotal);
                }
                else
                {
                    used.Add(next);
                    _plans[b.SerialNumber] = new[] { next };
                }
            }

        LogPlans:

            foreach (var b in bindings)
            {
                _logger.Information("相机图片序号规划: {Name} ({Sn}) -> [{Plan}]",
                    b.Name, b.SerialNumber, string.Join(",", _plans[b.SerialNumber]));
            }
        }

        /// <summary>
        /// 取某台相机本次帧应使用的图片序号。
        /// </summary>
        /// <param name="serialNumber">相机序列号</param>
        /// <param name="imageTotal">一张料的图片总数</param>
        /// <param name="startedNewSheet">
        /// 本次调用是否**刚好凑满一张料的图片数**（即这一帧之后应该开始新的一张料）。
        /// 判定依据是"自上次清零以来累计收到的帧数"，因此与相机台数、每台拍几张都无关：
        /// 单相机 ImageTotal=3 时正好每 3 帧报一次；两台相机 ImageTotal=4 时正好每 4 帧报一次。
        /// 这样既不会每帧都报（那会把刚收到的结果反复清掉），也不会永远不报（自动开新料失效）。
        /// </param>
        public int Next(string serialNumber, int imageTotal, out bool startedNewSheet)
        {
            startedNewSheet = false;
            if (imageTotal <= 0) imageTotal = 1;

            if (string.IsNullOrWhiteSpace(serialNumber))
                serialNumber = "__unknown__";

            if (!_plans.TryGetValue(serialNumber, out var plan) || plan.Length == 0)
            {
                // 没规划过（例如相机不在配置里）：退化为按帧轮转，至少保证序号落在 1..ImageTotal 内
                plan = Enumerable.Range(1, imageTotal).ToArray();
                _plans[serialNumber] = plan;
            }

            int index;

            // 说明：这里用 lock 而不是 Interlocked。因为需要"读游标 → 可能清零 → 写回"这一组动作
            // 与"累积帧数 → 可能清零"保持一致的视图；相机回调本来就在各自线程上，
            // 一把锁换来的是可推理的行为，代价可以忽略（每帧一次，纳秒级）。
            lock (_cursors)
            {
                var cursor = _cursors.TryGetValue(serialNumber, out var c) ? c : 0;

                if (cursor >= plan.Length)
                    cursor = 0;      // 本相机本轮序号用完：回到自己序列的第一个

                index = plan[cursor];
                _cursors[serialNumber] = cursor + 1;

                _framesSinceReset++;
                if (_framesSinceReset >= imageTotal)
                {
                    // 一张料的图片已经全部收到（或收到足够多），开始新的一张：
                    // 帧计数清零，并且所有相机都回到自己序列的起点。
                    _framesSinceReset = 0;
                    _cursors.Clear();
                    startedNewSheet = true;
                }
            }

            return index;
        }

        /// <summary>
        /// 重置游标（解绑相机时用），保留已规划的序号表。
        /// 同时清掉帧计数，让下一次绑定后的第一张料从头开始计。
        /// </summary>
        public void ResetCursors()
        {
            lock (_cursors)
            {
                _cursors.Clear();
                _framesSinceReset = 0;
            }
        }
    }
}
