using Halcon.Core;
using HalconDotNet;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>单个图片内某个 PCS 的原始视觉结果</summary>
    public sealed class PcsRunResult
    {
        /// <summary>图片内 PCS 下标（0 开始）</summary>
        public int PcsInImage { get; init; }

        /// <summary>检测项结果，"0"=OK，"1"=NG（与原项目一致）</summary>
        public List<string> ItemResults { get; init; } = new();

        /// <summary>各检测项对应的 NG 框</summary>
        public List<List<BoxRegion>> PointSets { get; init; } = new();

        /// <summary>是否有 NG 项（与 <see cref="PcsInspectionResult.HasNg"/> 同一语义，避免调用方散落字面量 "1"）</summary>
        public bool HasNg => ItemResults.Any(r => r == "1");

        /// <summary>PCS 结果图（所有权转移给调用方）</summary>
        public HObject? OutputImage { get; set; }
    }

    /// <summary>一次配方执行的完整结果（对应原项目 Run(imageInfo) 中一个流程的运行结果）</summary>
    public sealed class RecipeRunResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public long ElapsedMs { get; set; }
        public bool IsCodeRecipe { get; set; }

        /// <summary>扫码结果（扫码流程）</summary>
        public string? Code { get; set; }

        /// <summary>各 PCS 结果（检测流程）</summary>
        public List<PcsRunResult> PcsResults { get; } = new();

        public void DisposeImages()
        {
            foreach (var p in PcsResults)
            {
                p.OutputImage?.Dispose();
                p.OutputImage = null;
            }
        }
    }

    /// <summary>
    /// Halcon 检测执行服务。
    ///
    /// 迁移自 窗体.UI.FrmMian.Run(ImageInfo) —— 原实现通过 VmSolution / VmProcedure / ImageSourceModuleTool
    /// 加载 VisionMaster 流程并读取 "out{i}" / "box{i}" / "image{i}" 输出；
    /// 这里改为调用 HDevelop 外部过程，输出命名约定保持完全一致，方便直接搬运现场 .hdev 流程。
    /// </summary>
    public class HalconInspectionService : IDisposable
    {
        private readonly ProductRepository _repository;
        private readonly ILogger _logger;
        private readonly Dictionary<string, HalconEngine> _engines = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 已被标记为"不可信"的引擎键（执行超时或异常之后）。
        /// 下次取用时会重建，避免复用一个状态未知的引擎继续跑出错误结果。
        /// </summary>
        private readonly HashSet<string> _poisonedEngines = new(StringComparer.OrdinalIgnoreCase);

        private readonly object _sync = new();
        private bool _disposed;

        public HalconInspectionService(ProductRepository repository, ILogger logger)
        {
            _repository = repository;
            _logger = logger.ForContext<HalconInspectionService>();
        }

        #region 引擎缓存

        /// <summary>取得（或加载）指定过程对应的引擎实例</summary>
        public HalconEngine? GetEngine(string filePath, string procedureName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(procedureName))
                return null;

            var key = filePath + "::" + procedureName;

            lock (_sync)
            {
                // 被标记污染的引擎（上次执行超时/异常）不能复用：状态未知，继续用可能算出错的结果。
                // 这里顺手把它丢弃，下面会重新加载。
                if (_poisonedEngines.Remove(key) && _engines.TryGetValue(key, out var poisoned))
                {
                    _engines.Remove(key);
                    _logger.Warning("过程 [{Procedure}] 的上一个引擎已被标记为不可信，正在重建", procedureName);
                    try { poisoned.Dispose(); } catch (Exception ex) { _logger.Warning(ex, "释放旧引擎失败"); }
                }

                if (_engines.TryGetValue(key, out var cached) && cached.IsContentLoaded)
                    return cached;

                var engine = new HalconEngine(_logger);
                if (!engine.InitEngine())
                {
                    engine.Dispose();
                    return null;
                }

                if (!engine.LoadProcedure(filePath, procedureName))
                {
                    // 回退：把整份文件当作主程序加载（历史 .hdvp 流程）
                    _logger.Warning("按外部过程加载失败，回退为主程序模式: {File}", filePath);
                    if (!engine.LoadProgram(filePath))
                    {
                        engine.Dispose();
                        return null;
                    }
                }

                if (_engines.TryGetValue(key, out var old))
                    old.Dispose();

                _engines[key] = engine;
                return engine;
            }
        }

        /// <summary>卸载全部已加载的过程（切换产品/方案时调用）</summary>
        public void UnloadAll()
        {
            lock (_sync)
            {
                foreach (var engine in _engines.Values)
                    engine.Dispose();
                _engines.Clear();
                _poisonedEngines.Clear();
            }
            _logger.Information("已卸载全部 Halcon 过程");
        }

        /// <summary>查询配方对应过程的参数接口（供设置界面展示）</summary>
        public ProcedureInterface? QueryInterface(ProductConfiguration cfg, InspectionRecipe recipe)
        {
            var file = _repository.ResolveRecipeFile(cfg, recipe);
            var engine = GetEngine(file, recipe.ProcedureName);
            return engine?.QueryProcedureInterface();
        }

        #endregion

        #region 执行

        /// <summary>
        /// 执行一个配方。
        /// </summary>
        /// <param name="cfg">产品配置</param>
        /// <param name="recipe">检测配方</param>
        /// <param name="image">输入图像</param>
        /// <param name="imageIndex">图片序号（1 开始），用于参数化流程</param>
        public RecipeRunResult RunRecipe(ProductConfiguration cfg, InspectionRecipe recipe, HObject image, int imageIndex)
        {
            var result = new RecipeRunResult { IsCodeRecipe = recipe.IsCodeRecipe };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (image == null || !image.IsInitialized())
            {
                result.Error = "输入图像无效";
                return result;
            }

            var file = _repository.ResolveRecipeFile(cfg, recipe);
            if (string.IsNullOrWhiteSpace(file))
            {
                result.Error = "未找到配方对应的 Halcon 方案文件（.hdev）";
                _logger.Error("{Error}", result.Error);
                return result;
            }

            var engine = GetEngine(file, recipe.ProcedureName);
            if (engine == null)
            {
                result.Error = $"加载 Halcon 过程失败: {recipe.ProcedureName}";
                _logger.Error("{Error}", result.Error);
                return result;
            }

            // 是否拿到了引擎锁。放在 try 外面是为了在 finally 里正确释放。
            var lockTaken = false;

            try
            {
                // 用 TryEnter 而不是 lock：某个过程卡住时（死循环、等外部信号、Halcon 内部阻塞），
                // 旧实现会让消费线程永远等在这把锁上 —— 表现就是**整线静默停摆**，
                // 日志里什么都没有，现场只能看到"没有新结果了"。
                if (!Monitor.TryEnter(engine, EngineLockTimeoutMs))
                {
                    result.Error = $"等待 Halcon 过程 [{recipe.ProcedureName}] 可用超时（{EngineLockTimeoutMs}ms）—— " +
                                   "上一次执行可能卡住了";
                    _logger.Error("获取引擎锁超时: {Recipe}（上一次执行疑似卡死）", recipe.ProcedureName);
                    return result;
                }

                lockTaken = true;

                // 真正的执行放进独立任务，这样可以对它做超时等待。
                // 注意：Halcon 的执行本身不可取消，超时后那个任务会继续跑完；
                // 我们能做的是把它标记为"污染引擎"，下次执行前重建，避免复用一个状态未知的引擎。
                var body = Task.Run(() =>
                {
                    // 1) 输入图像
                    if (!engine.SetInputIconicParam(recipe.InputImageParam, image))
                        _logger.Warning("设置输入图像参数失败: {Param}（过程可能未声明该参数）", recipe.InputImageParam);

                    // 2) 附加控制参数 + 图片序号
                    foreach (var kv in recipe.InputParameters)
                    {
                        var tuple = double.TryParse(kv.Value, out var number)
                            ? new HTuple(number)
                            : new HTuple(kv.Value);
                        engine.SetInputCtrlParam(kv.Key, tuple);
                    }
                    engine.SetInputCtrlParam("ImageIndex", new HTuple(imageIndex));

                    // 3) 执行
                    if (!engine.Execute())
                        return $"过程执行失败: {recipe.ProcedureName}";

                    if (recipe.IsCodeRecipe)
                        result.Code = ExtractCode(engine, recipe);
                    else
                        CollectPcsResults(engine, recipe, result);

                    return null;   // null = 成功
                });

                if (!body.Wait(recipe.EffectiveTimeoutMs))
                {
                    result.Error = $"过程执行超时（超过 {recipe.EffectiveTimeoutMs}ms）: {recipe.ProcedureName}";
                    _logger.Error("Halcon 过程执行超时: {Recipe} 限时 {Timeout}ms；该引擎已标记为不可信，下次执行会重建",
                        recipe.ProcedureName, recipe.EffectiveTimeoutMs);

                    // 不能 Dispose（那个任务可能还在用），只标记为污染，下次 GetEngine 会重建
                    MarkEnginePoisoned(file, recipe.ProcedureName);
                    return result;
                }

                var failure = body.Result;
                if (!string.IsNullOrEmpty(failure))
                {
                    result.Error = failure;
                    return result;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _logger.Error(ex, "执行配方失败: {Recipe}", recipe.ProcedureName);

                // 出现异常说明引擎状态可能已不可信（例如 Halcon 内部错误），下次重建
                MarkEnginePoisoned(file, recipe.ProcedureName);
            }
            finally
            {
                if (lockTaken) Monitor.Exit(engine);

                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>
        /// 单次配方的默认超时（毫秒）。
        /// 现场可通过配方上的值覆盖；这里是兜底，防止"没配就永远不限时"。
        /// 取 60 秒：正常检测在几十到几百毫秒，超过 60 秒几乎一定是卡死而不是算得慢。
        /// </summary>
        public const int DefaultRecipeTimeoutMs = 60_000;

        /// <summary>等待引擎锁可用的超时（毫秒）。超过它就认为上一次执行卡住了。</summary>
        public const int EngineLockTimeoutMs = 5_000;

        /// <summary>
        /// 把某个过程对应的引擎标记为"不可信"，下次执行前重建。
        ///
        /// 为什么不在这里直接 Dispose：执行超时的情况下那个任务可能仍在用这个引擎对象，
        /// 立刻释放会造成访问已释放资源。标记 + 下次重建既安全又能恢复。
        /// </summary>
        private void MarkEnginePoisoned(string filePath, string procedureName)
        {
            var key = filePath + "::" + procedureName;
            lock (_sync)
            {
                _poisonedEngines.Add(key);
            }
        }

        private void CollectPcsResults(HalconEngine engine, InspectionRecipe recipe, RecipeRunResult result)
        {
            // 需要的结果输出个数 = PcsPerImage × ItemCount；与方案声明的 out{i} 数量不一致时，
            // 说明"图片内 PCS 数 / 检测项数"配错了 —— 这里给出一次明确提示，避免只看到一堆"未取到输出"。
            var requiredCount = recipe.PcsPerImage * recipe.ItemCount;
            var availableOutputs = engine.GetOutputCtrlParamNames();
            var reportedMismatch = false;

            // 方案实际声明的输出参数名。
            // 为什么需要：配方的模板（默认 out{i}/box{i}/image{i}）与现场 .hdev 的实际输出名
            // 经常对不上（例如演示方案声明的是 ResultImage / out0 / box0）。
            // 一旦对不上，按模板取值会全部落空 —— 表现就是"检测成功但界面没图、结果串为空"，
            // 极难排查。这里在按模板取不到时，回退到"按接口顺序取第 i 个"，
            // 并打印一次明确的警告告诉现场真实的名字。
            var declaredCtrlOutputs = engine.GetOutputCtrlParamNames() ?? Array.Empty<string>();

            var declaredImageOutputs = engine.QueryProcedureInterface()?.OutputImageParams
                                           ?.Select(p => p.Name)
                                           .Where(n => !string.IsNullOrWhiteSpace(n))
                                           .ToArray()
                                       ?? Array.Empty<string>();
            var reportedImageMismatch = false;

            for (int pcs = 0; pcs < recipe.PcsPerImage; pcs++)
            {
                var pcsResult = new PcsRunResult { PcsInImage = pcs };

                for (int item = 0; item < recipe.ItemCount; item++)
                {
                    int flatIndex = pcs * recipe.ItemCount + item;

                    // ---- 检测项结果（按模板名，取不到则按声明顺序回退） ----
                    var resultName = string.Format(recipe.ResultOutputPattern, flatIndex);
                    if (GetOutputCtrlByNameOrIndex(engine, declaredCtrlOutputs, resultName, flatIndex, out var tuple)
                        && tuple.Length > 0)
                    {
                        for (int i = 0; i < tuple.Length; i++)
                            pcsResult.ItemResults.Add(NormalizeResult(tuple[i].S));
                    }
                    else if (!reportedMismatch)
                    {
                        reportedMismatch = true;
                        _logger.Warning(
                            "配方 {Recipe} 需要 {Required} 个结果输出（PCS {Pcs} × 检测项 {Items}），" +
                            "但方案只声明了 [{Available}] —— 请检查配方的 PCS个数/检测项目数，或补齐方案里的 out{{i}}",
                            recipe.ProcedureName, requiredCount, recipe.PcsPerImage, recipe.ItemCount,
                            availableOutputs.Length > 0 ? string.Join(",", availableOutputs) : "无");
                    }

                    // ---- NG 框 ----
                    var boxName = string.Format(recipe.BoxOutputPattern, flatIndex);
                    if (engine.GetOutputIconicParam(boxName, out var boxObject))
                    {
                        try
                        {
                            pcsResult.PointSets.Add(ExtractBoxes(boxObject));
                        }
                        finally
                        {
                            boxObject?.Dispose();
                        }
                    }
                    else
                    {
                        pcsResult.PointSets.Add(new List<BoxRegion>());
                    }
                }

                // ---- PCS 结果图 ----
                CollectPcsImage(engine, recipe, pcs, pcsResult, declaredImageOutputs, ref reportedImageMismatch);

                result.PcsResults.Add(pcsResult);
            }
        }

        /// <summary>
        /// 取 PCS 结果图。
        /// 优先按配方的 <see cref="InspectionRecipe.ImageOutputPattern"/> 取名；
        /// 取不到时回退到「方案声明的第 pcs 个输出图标参数」——这一条是让界面能出图的关键：
        /// 现场 .hdev 的输出名五花八门（ResultImage / ImageOut / ...），
        /// 要求现场必须改名才能出图是不现实的。
        /// </summary>
        private void CollectPcsImage(HalconEngine engine, InspectionRecipe recipe, int pcs,
            PcsRunResult pcsResult, string[] declaredImageOutputs, ref bool reportedMismatch)
        {
            // 1) 按模板名
            if (!string.IsNullOrWhiteSpace(recipe.ImageOutputPattern))
            {
                var imageName = string.Format(recipe.ImageOutputPattern, pcs);
                if (engine.GetOutputIconicParam(imageName, out var outImage))
                {
                    pcsResult.OutputImage = outImage;
                    return;
                }
            }

            // 2) 回退：按声明顺序取第 pcs 个输出图标
            if (pcs < declaredImageOutputs.Length)
            {
                var fallbackName = declaredImageOutputs[pcs];
                if (engine.GetOutputIconicParam(fallbackName, out var fallbackImage))
                {
                    pcsResult.OutputImage = fallbackImage;

                    if (!reportedMismatch)
                    {
                        reportedMismatch = true;
                        _logger.Warning(
                            "配方 {Recipe} 配置的结果图输出名 [{Pattern}] 在方案里不存在，" +
                            "已改用方案实际声明的输出 [{Actual}]；建议把配方里的「结果图输出模板」改成实际名字",
                            recipe.ProcedureName,
                            string.IsNullOrWhiteSpace(recipe.ImageOutputPattern)
                                ? "（未配置）"
                                : string.Format(recipe.ImageOutputPattern, pcs),
                            fallbackName);
                    }

                    return;
                }
            }

            // 3) 都没有：只提示一次，不报错（很多现场流程本来就不输出结果图）
            if (!reportedMismatch && declaredImageOutputs.Length == 0)
            {
                reportedMismatch = true;
                _logger.Information(
                    "配方 {Recipe} 未输出结果图（方案未声明任何输出图标参数），界面将回退显示原始输入图",
                    recipe.ProcedureName);
            }
        }

        /// <summary>
        /// 按名字取输出控制参数；名字不在方案声明的参数表里时，回退到「按声明顺序取第 index 个」。
        /// </summary>
        private static bool GetOutputCtrlByNameOrIndex(HalconEngine engine, string[] declaredNames,
            string name, int index, out HTuple tuple)
        {
            tuple = new HTuple();

            if (engine.GetOutputCtrlParam(name, out var direct) && direct.Length > 0)
            {
                tuple = direct;
                return true;
            }

            if (index < 0 || index >= declaredNames.Length)
                return false;

            var fallbackName = declaredNames[index];
            if (string.Equals(fallbackName, name, StringComparison.Ordinal))
                return false;

            if (engine.GetOutputCtrlParam(fallbackName, out var fallback) && fallback.Length > 0)
            {
                tuple = fallback;
                return true;
            }

            return false;
        }

        #endregion

        #region 配方与方案接口的一致性校验

        /// <summary>
        /// 校验配方的输出名模板与方案实际声明的接口是否对得上。
        ///
        /// 迁移自现场教训：配方里写 <c>image{0}</c> 而方案声明 <c>ResultImage</c> 时，
        /// 程序"检测成功、日志正常"，但界面上永远没有图、结果串也是空 —— 没有任何报错，
        /// 只能靠人肉比对 .hdev。这里在保存配置/查询接口时给出明确的问题清单。
        /// </summary>
        /// <returns>问题描述列表；空列表表示一致</returns>
        public List<string> ValidateRecipeAgainstInterface(ProductConfiguration cfg, InspectionRecipe recipe)
        {
            var issues = new List<string>();

            ProcedureInterface? iface;
            try
            {
                iface = QueryInterface(cfg, recipe);
            }
            catch (Exception ex)
            {
                issues.Add($"查询过程接口失败: {ex.Message}");
                return issues;
            }

            if (iface == null)
            {
                issues.Add($"无法查询过程接口（方案文件或过程名不对）: {recipe.ProcedureName}");
                return issues;
            }

            // ---- 输入图像参数名 ----
            var inputImages = iface.InputImageParams?.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                              ?? new List<string>();
            if (inputImages.Count > 0 && !inputImages.Contains(recipe.InputImageParam, StringComparer.Ordinal))
            {
                issues.Add($"输入图像参数名 [{recipe.InputImageParam}] 在方案中不存在，" +
                           $"方案实际声明的是 [{string.Join(", ", inputImages)}]");
            }

            // ---- 扫码流程只需要一个条码输出，不校验结果/框/图 ----
            if (recipe.IsCodeRecipe)
            {
                var codeOutputs = iface.OutputControlParams?.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                                  ?? new List<string>();
                if (codeOutputs.Count > 0 && !codeOutputs.Contains(recipe.CodeOutputParam, StringComparer.Ordinal))
                {
                    issues.Add($"扫码结果参数名 [{recipe.CodeOutputParam}] 在方案中不存在，" +
                               $"方案实际声明的是 [{string.Join(", ", codeOutputs)}]（为空时会自动兜底取第一个非空输出）");
                }
                return issues;
            }

            // ---- 结果输出个数 ----
            var ctrlOutputNames = iface.OutputControlParams?.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                                  ?? new List<string>();
            var required = recipe.PcsPerImage * recipe.ItemCount;
            if (ctrlOutputNames.Count < required)
            {
                issues.Add($"检测项结果输出不足：配方需要 {required} 个" +
                           $"（PCS {recipe.PcsPerImage} × 检测项 {recipe.ItemCount}），" +
                           $"方案只声明了 {ctrlOutputNames.Count} 个 [{string.Join(", ", ctrlOutputNames)}]");
            }

            // ---- 结果输出名模板 ----
            var missingResults = new List<string>();
            for (int i = 0; i < required; i++)
            {
                var name = string.Format(recipe.ResultOutputPattern, i);
                if (!ctrlOutputNames.Contains(name, StringComparer.Ordinal))
                    missingResults.Add(name);
            }
            if (missingResults.Count > 0 && ctrlOutputNames.Count > 0)
            {
                issues.Add($"结果输出名对不上：模板 [{recipe.ResultOutputPattern}] 生成的名字 " +
                           $"[{string.Join(", ", missingResults)}] 在方案里不存在，" +
                           $"方案声明的是 [{string.Join(", ", ctrlOutputNames)}]（运行时会按顺序兜底，但建议改齐）");
            }

            // ---- PCS 结果图输出名模板 ----
            if (!string.IsNullOrWhiteSpace(recipe.ImageOutputPattern))
            {
                var imageNames = iface.OutputImageParams?.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                                 ?? new List<string>();
                var missingImages = new List<string>();
                for (int pcs = 0; pcs < recipe.PcsPerImage; pcs++)
                {
                    var name = string.Format(recipe.ImageOutputPattern, pcs);
                    if (!imageNames.Contains(name, StringComparer.Ordinal))
                        missingImages.Add(name);
                }

                if (missingImages.Count > 0)
                {
                    issues.Add(imageNames.Count > 0
                        ? $"结果图输出名对不上：模板 [{recipe.ImageOutputPattern}] 生成的名字 " +
                          $"[{string.Join(", ", missingImages)}] 在方案里不存在，" +
                          $"方案声明的是 [{string.Join(", ", imageNames)}]（运行时会按顺序兜底）"
                        : $"配方配置了结果图输出模板 [{recipe.ImageOutputPattern}]，但方案没有声明任何输出图标参数，" +
                          "界面将只能显示原始输入图");
                }
            }

            return issues;
        }

        /// <summary>校验整个产品配置的所有配方，返回「配方名 -> 问题列表」</summary>
        public Dictionary<string, List<string>> ValidateConfiguration(ProductConfiguration cfg)
        {
            var report = new Dictionary<string, List<string>>();
            foreach (var recipe in cfg.Recipes)
            {
                var issues = ValidateRecipeAgainstInterface(cfg, recipe);
                if (issues.Count > 0)
                    report[recipe.ProcedureName] = issues;
            }
            return report;
        }

        #endregion

        #region 私有辅助

        private string? ExtractCode(HalconEngine engine, InspectionRecipe recipe)
        {
            var name = recipe.CodeOutputParam;

            if (!string.IsNullOrWhiteSpace(name) &&
                engine.GetOutputCtrlParam(name, out var tuple) && tuple.Length > 0)
            {
                var code = tuple[0].S;
                if (!string.IsNullOrEmpty(code)) return code;
            }

            // 兜底：取第一个非空的控制输出
            foreach (var candidate in engine.GetOutputCtrlParamNames())
            {
                if (engine.GetOutputCtrlParam(candidate, out var t) && t.Length > 0 && !string.IsNullOrEmpty(t[0].S))
                    return t[0].S;
            }

            _logger.Warning("扫码流程 {Recipe} 未输出任何条码字符串", recipe.ProcedureName);
            return null;
        }

        /// <summary>
        /// 结果字符串归一化。
        /// 与原项目保持一致：空串按 NG("1") 处理，仅 "OK"（忽略大小写）视为 OK("0")。
        /// </summary>
        public static string NormalizeResult(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "1";
            return raw.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "0" : "1";
        }

        #endregion

        #region 区域 -> 框

        /// <summary>从 Halcon 区域 / XLD 中提取矩形框集合</summary>
        public static List<BoxRegion> ExtractBoxes(HObject? obj)
        {
            var boxes = new List<BoxRegion>();
            if (obj == null || !obj.IsInitialized()) return boxes;

            // 1) 轮廓（XLD）
            try
            {
                HOperatorSet.SmallestRectangle1Xld(obj, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
                AppendBoxes(boxes, r1, c1, r2, c2);
                if (boxes.Count > 0) return boxes;
            }
            catch
            {
                // 不是 XLD，继续按区域处理
            }

            // 2) 区域
            try
            {
                HOperatorSet.SmallestRectangle1(obj, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
                AppendBoxes(boxes, r1, c1, r2, c2);
            }
            catch
            {
                // 既不是区域也不是 XLD，返回空集合
            }

            return boxes;
        }

        private static void AppendBoxes(List<BoxRegion> boxes, HTuple r1, HTuple c1, HTuple r2, HTuple c2)
        {
            if (r1 == null || c1 == null || r2 == null || c2 == null) return;

            var count = Math.Min(Math.Min(r1.Length, c1.Length), Math.Min(r2.Length, c2.Length));
            for (int i = 0; i < count; i++)
                boxes.Add(BoxRegion.FromRectangle1(r1[i].D, c1[i].D, r2[i].D, c2[i].D));
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnloadAll();
        }
    }
}
