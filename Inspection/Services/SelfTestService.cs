using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using Halcon.Core;
using HalconDotNet;
using Inspection.Models;
using MVS.Core;
using PLCModule;
using PLCModule.Interfaces;
using Serilog;

namespace Inspection.Services
{
    /// <summary>自检项</summary>
    public sealed class SelfTestItem
    {
        public string Name { get; init; } = string.Empty;
        public bool Ok { get; init; }
        /// <summary>是否只是"可选/未配置"（不计入失败）</summary>
        public bool Optional { get; init; }
        public string Detail { get; init; } = string.Empty;
        public string? Hint { get; init; }

        /// <summary>
        /// 该项对应的设置页（视图名）。为空表示"没有对应页可去"（例如纯环境问题）。
        /// 有了它，界面上的「去处理」按钮就能把用户直接带到该改的地方，
        /// 而不是让人对着"到「检测设置 → 数据库设置」检查 IP"自己找。
        /// </summary>
        public string? TargetView { get; init; }

        public bool HasTarget => !string.IsNullOrWhiteSpace(TargetView);

        public string StatusText => Ok ? "正常" : Optional ? "未配置" : "异常";
        public string Color => Ok ? "#00E676" : Optional ? "#FFAB40" : "#FF5252";

        /// <summary>是否有可显示的处理建议（界面行内展示，不依赖鼠标悬停）</summary>
        public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

        /// <summary>行内展示的建议文本</summary>
        public string HintText => HasHint ? "建议：" + Hint : string.Empty;

        /// <summary>异常项的优先级（用于排序：异常在前、未配置居中、正常在后）</summary>
        public int SortOrder => Ok ? 2 : Optional ? 1 : 0;
    }

    /// <summary>自检报告</summary>
    public sealed class SelfTestReport
    {
        public List<SelfTestItem> Items { get; } = new();
        public DateTime RunAt { get; } = DateTime.Now;

        public int FailCount => Items.Count(i => !i.Ok && !i.Optional);
        public int OkCount => Items.Count(i => i.Ok);
        public int OptionalCount => Items.Count(i => !i.Ok && i.Optional);
        public bool AllPassed => FailCount == 0;

        /// <summary>摘要文本（把"未配置"的可选项单独说明，避免误以为全绿）</summary>
        public string Summary
        {
            get
            {
                var text = FailCount > 0
                    ? $"{FailCount} 项异常"
                    : "无异常项";
                text += $"，{OkCount} 项正常";
                if (OptionalCount > 0) text += $"，{OptionalCount} 项未配置（可选项）";
                return text;
            }
        }

        /// <summary>
        /// 可直接复制 / 存档的纯文本报告（现场把它发给工程师，比截图强）。
        /// 只读取 <see cref="SelfTestItem"/> 的叶子字段，不序列化任何运行时对象。
        /// </summary>
        public string ToText(string? productName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CCD 视觉检测系统 — 环境自检报告");
            sb.AppendLine($"时间：{RunAt:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrWhiteSpace(productName))
                sb.AppendLine($"产品：{productName}");
            sb.AppendLine($"结果：{Summary}");
            sb.AppendLine(new string('-', 64));

            // 与界面同序：异常 → 未配置 → 正常
            foreach (var item in Items.OrderBy(i => i.SortOrder))
            {
                sb.Append('[').Append(item.StatusText).Append("] ").AppendLine(item.Name);
                if (!string.IsNullOrWhiteSpace(item.Detail))
                    sb.Append("    ").AppendLine(item.Detail);
                if (item.HasHint)
                    sb.Append("    ").AppendLine(item.HintText);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 演示数据与自检服务。
    ///
    /// 解决的问题：没有相机 / PLC / MES / 数据库 / 现场方案文件时，
    /// 检测流程、存图、结果梳理、图表、日志这些功能**根本没法验证**。
    /// 这里提供：
    ///   1) 一键生成演示产品（含可用的 XML .hdev 方案文件 + 完整配方配置）
    ///   2) 合成 OK / NG 样张（真实 Halcon 图像），可投入编排器跑完整链路
    ///   3) 依赖自检（Halcon / 方案文件 / PLC 授权 / 存图 / 数据库 / MES / 相机）
    /// </summary>
    public class SelfTestService
    {
        /// <summary>演示产品名称</summary>
        public const string DemoProductName = "演示产品";

        private readonly ProductRepository _repository;
        private readonly HalconInspectionService _halcon;
        private readonly ImageArchiveService _archive;
        private readonly InspectionResultStore _store;
        private readonly IPLCCommunicator _plc;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly ICameraInventory _cameras;
        private readonly ILogger _logger;

        /// <summary>
        /// 进度上报：界面用它显示"正在检查哪一项"。
        /// Halcon 那几项首次合计十几秒，没有它界面就只是一句干巴巴的"自检中…"。
        /// </summary>
        private IProgress<string>? _progress;

        public SelfTestService(ProductRepository repository, HalconInspectionService halcon,
            ImageArchiveService archive, InspectionResultStore store, IPLCCommunicator plc,
            InspectionOrchestrator orchestrator, ICameraInventory cameras, ILogger logger)
        {
            _repository = repository;
            _halcon = halcon;
            _archive = archive;
            _store = store;
            _plc = plc;
            _orchestrator = orchestrator;
            _cameras = cameras;
            _logger = logger.ForContext<SelfTestService>();
        }

        #region 演示数据

        /// <summary>演示产品目录是否已就绪</summary>
        public bool DemoProductExists()
            => File.Exists(Path.Combine(_repository.GetProductDirectory(DemoProductName), DemoVisionProgram.FileName));

        /// <summary>
        /// 一键生成演示产品：方案文件（XML .hdev）+ 完整配置（扫码流程 + 检测流程 + 存图路径）。
        /// </summary>
        public ProductConfiguration CreateDemoProduct()
        {
            _repository.CreateProduct(DemoProductName);

            var dir = _repository.GetProductDirectory(DemoProductName);
            var hdevPath = Path.Combine(dir, DemoVisionProgram.FileName);
            File.WriteAllText(hdevPath, DemoVisionProgram.Xml);

            var cfg = new ProductConfiguration
            {
                ProductName = DemoProductName,
                SolutionName = DemoVisionProgram.FileName,
                ImageTotal = 1,
                // 1 张图 1 个 PCS；每个 PCS 的检测项数由配方的 ItemCount 决定，
                // 需要的输出个数 = PcsPerImage × ItemCount（必须与 .hdev 里声明的 out{i} 数量一致）
                SheetPcsTotal = 1,
                CodeCount = 1,
                UploadOrder = new List<string> { "1" },
                VisionProgramFile = hdevPath
            };

            // 两个配方：一个扫码、一个检测（图片索引都绑到第 1 张图，模拟"一张图两 PCS"）
            cfg.Recipes.Add(new InspectionRecipe
            {
                ProcedureName = DemoVisionProgram.ScanProcedure,
                ProcedureFile = DemoVisionProgram.FileName,
                ImageIndexes = "1",
                PcsPerImage = 1,
                ItemCount = 1,
                IsScannerRecipe = true,          // 显式声明，避免被当成检测流程
                CodeOutputParam = "code"
            });

            cfg.Recipes.Add(new InspectionRecipe
            {
                ProcedureName = DemoVisionProgram.InspectProcedure,
                ProcedureFile = DemoVisionProgram.FileName,
                ImageIndexes = "1",
                PcsPerImage = 1,                 // 1 个 PCS
                ItemCount = 2,                   // 2 个检测项 → 需要 out0/out1（与演示方案一致）
                ResultOutputPattern = "out{0}",
                BoxOutputPattern = "box{0}",
                ImageOutputPattern = "image{0}"
            });

            // 演示用：不依赖 MES / 数据库也能跑（默认关掉上传，避免自检时全是失败）
            cfg.GetToggles(SettingsGroups.General).MesProcess = false;
            cfg.GetToggles(SettingsGroups.General).MesData = false;
            cfg.GetToggles(SettingsGroups.Vision).Single = true;
            cfg.GetToggles(SettingsGroups.Vision).WholePcs = true;
            cfg.GetToggles(SettingsGroups.ImageSave).NgImage = true;
            cfg.GetToggles(SettingsGroups.ImageSave).OriginalImage = false;

            var save = cfg.GetImageLocation();
            var root = Path.Combine(AppContext.BaseDirectory, "DemoImages");
            save.NgPath = Path.Combine(root, "NG");
            save.OkPath = Path.Combine(root, "OK");
            save.OriginalPath = Path.Combine(root, "Original");
            save.ReCheckPath = Path.Combine(root, "ReCheck");

            _repository.Save(cfg);
            _logger.Information("演示产品已生成: {Dir}", dir);
            return cfg;
        }

        /// <summary>合成样张：ng = true 生成"有大块亮区"的不良样张，否则良品样张</summary>
        public static HObject CreateSampleImage(bool ng)
        {
            HOperatorSet.GenImageConst(out HObject image, "byte", 800, 600);

            if (ng)
            {
                // 大块亮区 → 面积 >= 500 → out0 = NG
                HOperatorSet.GenRectangle1(out HObject big, 100, 100, 300, 500);
                HOperatorSet.PaintRegion(big, image, out HObject painted, 220, "fill");
                big.Dispose();
                return painted;
            }

            // 良品：只有一个很小的亮点（面积 < 500）
            HOperatorSet.GenRectangle1(out HObject small, 10, 10, 20, 30);
            HOperatorSet.PaintRegion(small, image, out HObject ok, 220, "fill");
            small.Dispose();
            return ok;
        }

        #endregion

        #region 依赖自检

        private SelfTestReport? _pendingReport;

        /// <summary>
        /// Halcon 相关的两项检查（引擎初始化 / 方案文件与过程可加载）。
        /// 首次执行会加载 HALCON 运行时，可能耗时十几秒，因此**只在后台线程调用**，
        /// 否则进入自检页时界面会白屏卡住（实测踩过的坑）。
        /// </summary>
        private void RunHalconChecks(CancellationToken ct)
        {
            var report = new SelfTestReport();
            _pendingReport = report;

            var cfg = _orchestrator.Configuration;

            // 1) Halcon 引擎
            _progress?.Report("正在检查：Halcon 引擎…");
            try
            {
                using var engine = new HalconEngine(_logger);
                var ok = engine.InitEngine();
                report.Items.Add(new SelfTestItem
                {
                    Name = "Halcon 引擎",
                    Ok = ok,
                    Detail = ok ? "HDevEngine 初始化成功" : "初始化失败",
                    Hint = ok ? null : "检查 HALCON 是否正确安装、License 是否可用"
                });
            }
            catch (Exception ex)
            {
                report.Items.Add(new SelfTestItem { Name = "Halcon 引擎", Ok = false, Detail = ex.Message });
            }

            // 3) 方案文件 + 过程可加载
            _progress?.Report("正在检查：Halcon 方案文件与过程可加载性…");
            var productName = cfg?.ProductName ?? DemoProductName;
            var programFile = cfg?.VisionProgramFile;
            if (string.IsNullOrWhiteSpace(programFile))
                programFile = Path.Combine(_repository.GetProductDirectory(productName), DemoVisionProgram.FileName);

            var fileExists = File.Exists(programFile);
            var procedures = cfg?.Recipes.Select(r => r.ProcedureName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                             ?? new List<string> { DemoVisionProgram.InspectProcedure };

            var loadDetail = string.Empty;
            var loadOk = false;

            if (fileExists && procedures.Count > 0)
            {
                try
                {
                    using var engine = new HalconEngine(_logger);
                    if (engine.InitEngine())
                    {
                        loadOk = true;
                        foreach (var p in procedures)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (!engine.LoadProcedure(programFile, p))
                            {
                                loadOk = false;
                                loadDetail = $"过程 [{p}] 加载失败";
                                break;
                            }
                        }

                        if (loadOk)
                            loadDetail = $"{Path.GetFileName(programFile)} 的 {procedures.Count} 个过程均可加载";
                    }
                }
                catch (Exception ex)
                {
                    loadDetail = ex.Message;
                }
            }
            else if (!fileExists)
            {
                loadDetail = "未找到方案文件（.hdev）";
            }

            var programOk = fileExists && loadOk;
            report.Items.Add(new SelfTestItem
            {
                Name = "Halcon 方案文件",
                Ok = programOk,
                Detail = fileExists ? loadDetail : $"{programFile} 不存在",
                // 通过时不显示建议，避免噪音
                Hint = programOk
                    ? null
                    : "HALCON 25 的 .hdev 必须是 XML 格式（用 HDevelop 另存为）；可用「生成演示产品」得到一份可用示例"
            });

            // 4) 配方与方案**接口**的一致性 —— 本项目最高发的配置错误区。
            //    典型症状：结果图/NG 框的输出名写错、检测项个数与方案实际输出不符。
            //    这类问题不会让过程"加载失败"，只会在运行时静默回退（日志里一条 Warning），
            //    所以必须在自检里显式查出来。只在方案确实能加载时才查，否则会重复报同一个失败。
            if (programOk && cfg is { Recipes.Count: > 0 })
            {
                _progress?.Report("正在检查：配方与方案接口一致性…");
                var mismatches = new List<string>();
                foreach (var recipe in cfg.Recipes)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        mismatches.AddRange(_halcon.ValidateRecipeAgainstInterface(cfg, recipe)
                            .Select(issue => $"{recipe.ProcedureName}: {issue}"));
                    }
                    catch (Exception ex)
                    {
                        mismatches.Add($"{recipe.ProcedureName}: 校验异常 {ex.Message}");
                    }
                }

                report.Items.Add(new SelfTestItem
                {
                    Name = "配方与方案接口",
                    TargetView = "InspectionConfigView",
                    Ok = mismatches.Count == 0,
                    Detail = mismatches.Count == 0
                        ? $"{cfg.Recipes.Count} 个配方的输入/输出与方案声明一致"
                        : string.Join("；", mismatches.Take(3)) +
                          (mismatches.Count > 3 ? $" …等共 {mismatches.Count} 项" : string.Empty),
                    Hint = mismatches.Count == 0
                        ? null
                        : "到「检测配置」点「校验接口」看明细，核对结果图/NG 框的输出名与检测项个数"
                });

                // 节拍体检：前置条件与上面完全相同（方案可加载 + 有配方），顺带做掉
                _progress?.Report("正在检查：检测节拍（会实际执行 3 次配方）…");
                AddThroughputCheck(report, cfg);
            }
        }

        /// <summary>
        /// 检测节拍体检：用合成样张跑真实配方，量化"单张处理耗时"。
        ///
        /// 为什么只给数字、不判"快慢"：配置里没有"采集节拍"字段，无法自动判定跟不跟得上。
        /// 但**单张耗时**本身就是关键数字 —— 与 <see cref="FrameQueue.Capacity"/> 一起换算，
        /// 现场就能知道"采集间隔快于多少毫秒会开始积压、进而丢帧"。
        /// 唯一的硬判定是"已经跑到配方超时"，那说明这个配方根本跑不完。
        /// </summary>
        private void AddThroughputCheck(SelfTestReport report, ProductConfiguration cfg)
        {
            var recipe = cfg.Recipes.FirstOrDefault(r => !r.IsCodeRecipe) ?? cfg.Recipes[0];
            const int measuredRuns = 2;

            try
            {
                using var image = CreateSampleImage(ng: false);

                // 预热一次：首次会把过程加载进引擎，那段耗时不属于"单张处理节拍"
                _halcon.RunRecipe(cfg, recipe, image, 1).DisposeImages();

                var sw = Stopwatch.StartNew();
                for (var i = 0; i < measuredRuns; i++)
                    _halcon.RunRecipe(cfg, recipe, image, 1).DisposeImages();
                sw.Stop();

                var avgMs = sw.Elapsed.TotalMilliseconds / measuredRuns;
                var timeout = recipe.EffectiveTimeoutMs;
                var slow = avgMs >= timeout;

                report.Items.Add(new SelfTestItem
                {
                    Name = "检测节拍",
                    TargetView = "InspectionConfigView",
                    Ok = !slow,
                    Detail = slow
                        ? $"配方 {recipe.ProcedureName} 单张平均 {avgMs:F0} ms，已达/超过配方超时 {timeout} ms"
                        : $"配方 {recipe.ProcedureName} 单张平均 {avgMs:F0} ms；" +
                          $"队列容量 {FrameQueue.Capacity} 张，采集间隔快于 {avgMs:F0} ms 就会开始积压并触发丢帧",
                    Hint = slow
                        ? "检查方案里的算子复杂度，或在「检测配置」放宽配方超时"
                        : null
                });
            }
            catch (Exception ex)
            {
                report.Items.Add(new SelfTestItem
                {
                    Name = "检测节拍",
                    TargetView = "InspectionConfigView",
                    Ok = false,
                    Detail = $"节拍体检失败：{ex.Message}",
                    Hint = "先在「流程测试」里手动跑一次该配方，确认方案可执行"
                });
            }
        }

        /// <summary>运行全部自检项（不抛异常，逐项给出结论与建议）</summary>
        /// <param name="progress">进度回调（用于显示"正在检查哪一项"），可空</param>
        public async Task<SelfTestReport> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            _progress = progress;

            // 让调用方（UI 线程）先拿到控制权：下面的 Halcon 检查会加载 HALCON 运行时，首次可能耗时十几秒，
            // 必须放到后台线程，否则界面会白屏卡住（实测踩过的坑）。
            await Task.Yield();

            _progress?.Report("正在检查：Halcon 引擎与方案文件（首次启动较慢，请稍候）…");
            await Task.Run(() => RunHalconChecks(ct), ct).ConfigureAwait(true);

            var report = _pendingReport!;
            _progress?.Report("正在检查：相机、产品配置与图像索引…");
            var cfg = _orchestrator.Configuration;

            // 2) 产品配置
            if (cfg == null)
            {
                report.Items.Add(new SelfTestItem
                {
                    Name = "产品配置",
                    TargetView = "ProductView",
                    Ok = false,
                    Optional = true,
                    Detail = "未加载产品",
                    Hint = "到「产品与方案」加载，或在「自检与仿真」里一键生成演示产品"
                });
            }
            else
            {
                var issues = cfg.Validate();
                report.Items.Add(new SelfTestItem
                {
                    Name = "产品配置",
                    TargetView = "ProductView",
                    Ok = issues.Count == 0,
                    Detail = issues.Count == 0 ? $"{cfg.ProductName}：校验通过" : string.Join("；", issues),
                    Hint = issues.Count == 0 ? null : "到「检测配置」补齐配方/绑定"
                });
            }

            // 相机：视觉链路的核心，旧版自检完全没覆盖。而"换过相机 / 插错网口导致
            // 配置里的序列号对不上"是现场最常见的开机故障之一。
            AddCameraChecks(report, cfg);

            // 图像索引自洽：相机产出的图与配方消费的图必须对得上，
            // 否则表现为"检测跑过了但结果对不上"，且没有任何报错。
            AddImageIndexChecks(report, cfg);

            // 4) HslCommunication 授权（商业组件）
            report.Items.Add(new SelfTestItem
            {
                Name = "PLC 组件授权",
                Ok = HslLicense.IsRegistered,
                Detail = HslLicense.LastMessage,
                Hint = HslLicense.IsRegistered ? null : "在 appsettings.json 的 Plc.HslAuthorizationCode 或环境变量 HSL_AUTH_CODE 填入授权码"
            });

            // 5) PLC 连接（可选）
            report.Items.Add(new SelfTestItem
            {
                Name = "PLC 连接",
                TargetView = "PLCMonitorView",
                Ok = _plc.IsConnected,
                Optional = true,
                Detail = _plc.IsConnected ? "已连接" : "未连接",
                Hint = _plc.IsConnected ? null : "「PLC 监控」里填 IP/端口后连接（无 PLC 可跳过）"
            });

            // 6) 存图目录可写 + 磁盘余量
            _progress?.Report("正在检查：存图目录、日志目录与磁盘空间…");
            var location = cfg?.GetImageLocation();
            var imageRoot = location?.NgPath;
            if (string.IsNullOrWhiteSpace(imageRoot))
                imageRoot = Path.Combine(AppContext.BaseDirectory, "DemoImages", "NG");

            var writeOk = TryProbeWrite(imageRoot, out var writeDetail);
            report.Items.Add(new SelfTestItem
            {
                Name = "存图目录",
                TargetView = "SystemSettingsView",
                Ok = writeOk,
                Detail = writeOk ? imageRoot : writeDetail,
                Hint = writeOk ? null : "到「检测设置 → 存图设置」改成有写权限的路径"
            });

            // 日志目录可写：写不进去等于整个程序"瞎跑"，现场排查会完全没有依据
            var logRoot = Path.Combine(AppContext.BaseDirectory, "Logs");
            var logOk = TryProbeWrite(logRoot, out var logDetail);
            report.Items.Add(new SelfTestItem
            {
                Name = "日志目录",
                Ok = logOk,
                Detail = logOk ? logRoot : logDetail,
                Hint = logOk ? null : "检查程序目录是否有写权限（安装到 Program Files 且未提权时最容易遇到）"
            });

            // 磁盘余量：产线连续存图最容易把盘写满，写满之后所有检测都会静默失败
            AddDiskSpaceCheck(report, imageRoot);

            // 配置版本：旧结构的产品配置会让新字段取默认值，表现为"配了但不生效"
            if (cfg != null)
            {
                var schemaOk = cfg.SchemaVersion >= ProductConfiguration.CurrentSchemaVersion;
                report.Items.Add(new SelfTestItem
                {
                    Name = "配置版本",
                    TargetView = "ProductView",
                    Ok = schemaOk,
                    Optional = true,
                    Detail = schemaOk
                        ? $"v{cfg.SchemaVersion}（当前版本）"
                        : $"配置为 v{cfg.SchemaVersion}，程序要求 v{ProductConfiguration.CurrentSchemaVersion}",
                    Hint = schemaOk ? null : "到「产品与方案」打开该产品并保存一次，即可写入新版结构"
                });
            }

            // 7) 数据库（可选）
            _progress?.Report("正在检查：数据库连接…");
            await Task.Run(() =>
            {
                var ok = false;
                var detail = "未配置";
                try
                {
                    if (cfg != null)
                    {
                        _store.Configure(cfg.MySql);
                        ok = _store.TestConnection();
                        detail = ok ? $"{cfg.MySql.Ip}:{cfg.MySql.Port}/{cfg.MySql.DatabaseName} 连接成功" : "连接失败";
                    }
                }
                catch (Exception ex) { detail = ex.Message; }

                report.Items.Add(new SelfTestItem
                {
                    Name = "数据库",
                    TargetView = "SystemSettingsView",
                    Ok = ok,
                    Optional = true,
                    Detail = detail,
                    Hint = ok ? null : "「检测设置 → 数据库设置」检查 IP/账号；无数据库可跳过"
                });
            }, ct).ConfigureAwait(false);

            // 8) MES 可达（可选）
            _progress?.Report("正在检查：MES 主机可达性…");
            var mesHost = cfg?.Mes.HostIp;
            if (!string.IsNullOrWhiteSpace(mesHost))
            {
                var reachable = false;
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(mesHost, 1500).ConfigureAwait(false);
                    reachable = reply.Status == IPStatus.Success;
                }
                catch { /* 不可达 */ }

                report.Items.Add(new SelfTestItem
                {
                    Name = "MES 主机",
                    TargetView = "SystemSettingsView",
                    Ok = reachable,
                    Optional = true,
                    Detail = reachable ? $"{mesHost} 可达" : $"{mesHost} 不可达",
                    Hint = reachable ? null : "「检测设置 → MES设置」检查主机地址；无 MES 可跳过"
                });
            }

            // 9) 演示方案是否就绪
            report.Items.Add(new SelfTestItem
            {
                Name = "演示方案",
                Ok = DemoProductExists(),
                Optional = true,
                Detail = DemoProductExists() ? $"{DemoProductName}\\{DemoVisionProgram.FileName} 已就绪" : "尚未生成",
                Hint = DemoProductExists() ? null : "点「生成演示产品」即可得到一份可用的示例方案"
            });

            _logger.Information("自检完成：{Ok} 项正常，{Fail} 项异常", report.OkCount, report.FailCount);
            return report;
        }

        #endregion

        #region 视觉链路检查（相机 / 图像索引）

        /// <summary>
        /// 相机 SDK 状态 + 配置里绑定的序列号是否真的存在于设备列表。
        /// 这是旧版自检完全缺失的一块：程序的核心就是相机，自检却从不检查它。
        /// </summary>
        private void AddCameraChecks(SelfTestReport report, ProductConfiguration? cfg)
        {
            var deviceCount = 0;
            string detail;
            try
            {
                deviceCount = _cameras.Devices.Count;
                detail = _cameras.IsInitialized
                    ? $"MVS SDK 已初始化，枚举到 {deviceCount} 台设备"
                    : $"MVS SDK 未就绪：{_cameras.InitializationError ?? "原因未知"}";
            }
            catch (Exception ex)
            {
                detail = $"读取相机状态失败：{ex.Message}";
            }

            report.Items.Add(new SelfTestItem
            {
                Name = "相机 SDK",
                TargetView = "CameraManagerView",
                Ok = _cameras.IsInitialized,
                Detail = detail,
                Hint = _cameras.IsInitialized
                    ? null
                    : "检查 MVS 运行库是否安装、版本是否匹配；「相机管理」页会显示具体错误"
            });

            var bindings = cfg?.Cameras ?? new List<CameraBinding>();
            if (bindings.Count == 0)
            {
                report.Items.Add(new SelfTestItem
                {
                    Name = "相机绑定",
                    TargetView = "InspectionConfigView",
                    Ok = false,
                    Optional = true,
                    Detail = cfg == null ? "未加载产品" : "当前产品未配置相机绑定",
                    Hint = "到「检测配置 → 相机绑定」指定序列号与图片序号；无相机可跳过"
                });
                return;
            }

            var actual = _cameras.Devices
                .Select(d => d.SerialNumber)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var emptySerial = bindings.Where(b => string.IsNullOrWhiteSpace(b.SerialNumber))
                                      .Select(b => b.Name).ToList();
            var missing = bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.SerialNumber) && !actual.Contains(b.SerialNumber))
                .Select(b => $"{b.Name}({b.SerialNumber})")
                .ToList();

            var problems = new List<string>();
            if (emptySerial.Count > 0) problems.Add($"未填序列号：{string.Join("、", emptySerial)}");
            if (missing.Count > 0) problems.Add($"设备列表中找不到：{string.Join("、", missing)}");

            report.Items.Add(new SelfTestItem
            {
                Name = "相机绑定",
                TargetView = "InspectionConfigView",
                Ok = problems.Count == 0,
                Detail = problems.Count == 0
                    ? $"{bindings.Count} 个绑定均匹配到实际设备（共枚举 {actual.Count} 台）"
                    : string.Join("；", problems),
                Hint = problems.Count == 0
                    ? null
                    : "到「相机管理」核对序列号：换过相机、插错网口、或相机未上电都会导致序列号对不上"
            });
        }

        /// <summary>
        /// 图像索引自洽：相机产出的图片序号与配方消费的图片序号必须对得上。
        /// 对不上时表现为"检测跑过了但结果对不上"，而且**没有任何报错**，必须查出来。
        /// </summary>
        private void AddImageIndexChecks(SelfTestReport report, ProductConfiguration? cfg)
        {
            if (cfg == null) return;   // "未加载产品"已由「产品配置」项报出，不重复

            var issues = new List<string>();
            var total = cfg.ImageTotal;

            var recipeIndexes = cfg.Recipes.SelectMany(r => r.ParseImageIndexes()).ToList();
            var overRecipe = recipeIndexes.Where(i => i > total).Distinct().OrderBy(i => i).ToList();
            if (overRecipe.Count > 0)
                issues.Add($"配方引用了超出图片总数的索引 {string.Join(",", overRecipe)}（总数 {total}）");

            var explicitIndexes = cfg.Cameras
                .Where(c => !string.IsNullOrWhiteSpace(c.ImageIndexes))
                .SelectMany(c => c.ParseImageIndexes())
                .ToList();

            var duplicated = explicitIndexes.GroupBy(i => i).Where(g => g.Count() > 1)
                                            .Select(g => g.Key).OrderBy(i => i).ToList();
            if (duplicated.Count > 0)
                issues.Add($"多个相机绑定到同一图片序号 {string.Join(",", duplicated)}（会互相覆盖）");

            var overCamera = explicitIndexes.Where(i => i > total).Distinct().OrderBy(i => i).ToList();
            if (overCamera.Count > 0)
                issues.Add($"相机产出的索引超出图片总数 {string.Join(",", overCamera)}（总数 {total}）");

            // 交叉检查只在"所有相机都显式配了索引"时才有意义：
            // 留空代表按 Order 自动分配区间，静态判断会误报。
            var allExplicit = cfg.Cameras.Count > 0
                              && cfg.Cameras.All(c => !string.IsNullOrWhiteSpace(c.ImageIndexes));

            string detail;
            if (issues.Count > 0)
            {
                detail = string.Join("；", issues);
            }
            else if (!allExplicit)
            {
                detail = $"配方侧 {recipeIndexes.Distinct().Count()}/{total} 张已绑定；" +
                         "相机侧存在自动分配（留空），跳过交叉检查";
            }
            else
            {
                var cameraSet = explicitIndexes.ToHashSet();
                var recipeSet = recipeIndexes.ToHashSet();
                var noCamera = recipeSet.Where(i => !cameraSet.Contains(i)).OrderBy(i => i).ToList();
                var noRecipe = cameraSet.Where(i => !recipeSet.Contains(i)).OrderBy(i => i).ToList();

                if (noCamera.Count > 0)
                    issues.Add($"配方需要但没有相机产出的图片序号 {string.Join(",", noCamera)}（会一直等不到图）");
                if (noRecipe.Count > 0)
                    issues.Add($"相机产出但没有配方接收的图片序号 {string.Join(",", noRecipe)}（白采图）");

                detail = issues.Count == 0
                    ? $"配方与相机的图片序号完全对应（共 {total} 张）"
                    : string.Join("；", issues);
            }

            report.Items.Add(new SelfTestItem
            {
                Name = "图像索引",
                TargetView = "InspectionConfigView",
                Ok = issues.Count == 0,
                Detail = detail,
                Hint = issues.Count == 0
                    ? null
                    : "到「检测配置」核对配方图片索引、到「检测配置 → 相机绑定」核对每台相机的图片序号"
            });
        }

        #endregion

        #region 环境资源检查（磁盘 / 目录写权限）

        /// <summary>剩余空间告警阈值（GB）—— 产线连续存图，低于这个量就该安排清盘了</summary>
        private const double MinFreeDiskGb = 2;

        /// <summary>
        /// 目录写探针：建目录 → 写临时文件 → 删掉。
        /// 返回是否可写；<paramref name="detail"/> 成功时是目录路径、失败时是原因。
        /// （存图目录与日志目录共用这一处，避免同一段探针逻辑写两遍。）
        /// </summary>
        private static bool TryProbeWrite(string dir, out string detail)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, $".probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                detail = dir;
                return true;
            }
            catch (Exception ex)
            {
                detail = $"{dir} 不可写：{ex.Message}";
                return false;
            }
        }

        /// <summary>存图盘与日志盘的剩余空间：只要有一个低于阈值就报异常（写满后检测会静默失败）</summary>
        private static void AddDiskSpaceCheck(SelfTestReport report, string imageRoot)
        {
            var parts = new List<string>();
            var low = new List<string>();

            foreach (var (label, path) in new[]
                     {
                         ("存图", imageRoot),
                         ("日志", Path.Combine(AppContext.BaseDirectory, "Logs"))
                     })
            {
                try
                {
                    var root = Path.GetPathRoot(Path.GetFullPath(path));
                    if (string.IsNullOrEmpty(root))
                    {
                        parts.Add($"{label}盘无法解析");
                        low.Add(label);
                        continue;
                    }

                    var freeGb = new DriveInfo(root).AvailableFreeSpace / 1024d / 1024d / 1024d;
                    parts.Add($"{label}盘 {root} 剩余 {freeGb:F1} GB");
                    if (freeGb < MinFreeDiskGb) low.Add(label);
                }
                catch (Exception ex)
                {
                    parts.Add($"{label}盘读取失败：{ex.Message}");
                    low.Add(label);
                }
            }

            report.Items.Add(new SelfTestItem
            {
                Name = "磁盘空间",
                TargetView = "SystemSettingsView",
                Ok = low.Count == 0,
                Detail = low.Count == 0
                    ? string.Join("；", parts)
                    : string.Join("；", parts) + $"（{string.Join("、", low)}低于 {MinFreeDiskGb:F0} GB 阈值）",
                Hint = low.Count == 0
                    ? null
                    : "清理存图目录，或到「检测设置 → 存图设置」换到剩余空间更大的盘"
            });
        }

        #endregion
    }
}
