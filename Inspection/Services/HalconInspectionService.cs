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

            try
            {
                lock (engine)
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
                    {
                        result.Error = $"过程执行失败: {recipe.ProcedureName}";
                        return result;
                    }

                    if (recipe.IsCodeRecipe)
                        result.Code = ExtractCode(engine, recipe);
                    else
                        CollectPcsResults(engine, recipe, result);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _logger.Error(ex, "执行配方失败: {Recipe}", recipe.ProcedureName);
            }
            finally
            {
                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        private void CollectPcsResults(HalconEngine engine, InspectionRecipe recipe, RecipeRunResult result)
        {
            // 需要的结果输出个数 = PcsPerImage × ItemCount；与方案声明的 out{i} 数量不一致时，
            // 说明"图片内 PCS 数 / 检测项数"配错了 —— 这里给出一次明确提示，避免只看到一堆"未取到输出"。
            var requiredCount = recipe.PcsPerImage * recipe.ItemCount;
            var availableOutputs = engine.GetOutputCtrlParamNames();
            var reportedMismatch = false;

            for (int pcs = 0; pcs < recipe.PcsPerImage; pcs++)
            {
                var pcsResult = new PcsRunResult { PcsInImage = pcs };

                for (int item = 0; item < recipe.ItemCount; item++)
                {
                    int flatIndex = pcs * recipe.ItemCount + item;

                    // ---- 检测项结果 ----
                    var resultName = string.Format(recipe.ResultOutputPattern, flatIndex);
                    if (engine.GetOutputCtrlParam(resultName, out var tuple) && tuple.Length > 0)
                    {
                        for (int i = 0; i < tuple.Length; i++)
                            pcsResult.ItemResults.Add(NormalizeResult(tuple[i].S));
                    }
                    else
                    {
                        if (!reportedMismatch)
                        {
                            reportedMismatch = true;
                            _logger.Warning(
                                "配方 {Recipe} 需要 {Required} 个结果输出（PCS {Pcs} × 检测项 {Items}），" +
                                "但方案只声明了 [{Available}] —— 请检查配方的 PCS个数/检测项目数，或补齐方案里的 out{{i}}",
                                recipe.ProcedureName, requiredCount, recipe.PcsPerImage, recipe.ItemCount,
                                availableOutputs.Length > 0 ? string.Join(",", availableOutputs) : "无");
                        }
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
                if (!string.IsNullOrWhiteSpace(recipe.ImageOutputPattern))
                {
                    var imageName = string.Format(recipe.ImageOutputPattern, pcs);
                    if (engine.GetOutputIconicParam(imageName, out var outImage))
                        pcsResult.OutputImage = outImage;
                }

                result.PcsResults.Add(pcsResult);
            }
        }

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
