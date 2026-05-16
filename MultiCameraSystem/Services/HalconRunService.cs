using Halcon.Core;
using HalconDotNet;
using Serilog;

namespace MultiCameraSystem.Services
{
    /// <summary>一次 Halcon 过程执行的结果</summary>
    public sealed class HalconRunResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public long ElapsedMs { get; init; }
        public bool Ok { get; init; } = true;
        public double Score { get; init; }
        public string? ResultText { get; init; }
    }

    /// <summary>
    /// 单张图像的 Halcon 过程执行服务。
    ///
    /// MVVM 分离：原来「运行界面」的 ViewModel 里直接 new HalconEngine()、加载过程、设参、执行、取结果，
    /// 既把算法细节混进了 ViewModel，也没法在别处复用。
    /// 这里把它抽成服务，ViewModel 只负责编排状态与命令。
    /// </summary>
    public class HalconRunService
    {
        private readonly ILogger _logger;

        public HalconRunService(ILogger logger)
        {
            _logger = logger.ForContext<HalconRunService>();
        }

        /// <summary>
        /// 执行一次 Halcon 外部过程。
        /// </summary>
        /// <param name="programFile">.hdev / .hdvp 文件路径</param>
        /// <param name="procedureName">过程名（本地函数）</param>
        /// <param name="inputImageParam">输入图像参数名</param>
        /// <param name="image">输入图像（不会被释放）</param>
        public HalconRunResult Run(string programFile, string procedureName, string inputImageParam, HObject? image)
        {
            if (image == null || !image.IsInitialized())
                return new HalconRunResult { Success = false, Error = "输入图像无效" };

            if (string.IsNullOrWhiteSpace(programFile))
                return new HalconRunResult { Success = false, Error = "未指定 Halcon 程序文件" };

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var engine = new HalconEngine(_logger);
                if (!engine.InitEngine())
                    return new HalconRunResult { Success = false, Error = "Halcon 引擎初始化失败" };

                // 优先按外部过程加载，失败时回退为主程序模式
                var loaded = !string.IsNullOrWhiteSpace(procedureName) && engine.LoadProcedure(programFile, procedureName);
                if (!loaded && !engine.LoadProgram(programFile))
                    return new HalconRunResult { Success = false, Error = "加载 Halcon 程序/过程失败" };

                if (engine.IsProcedureMode)
                    engine.SetInputIconicParam(string.IsNullOrWhiteSpace(inputImageParam) ? "Image" : inputImageParam, image);

                if (!engine.Execute())
                    return new HalconRunResult { Success = false, Error = "Halcon 执行失败" };

                var ok = true;
                var score = 0.0;
                var text = "";

                if (engine.GetOutputCtrlParam("OK", out var okTuple) && okTuple.Length > 0)
                    ok = okTuple[0].I != 0 || string.Equals(okTuple[0].S, "OK", StringComparison.OrdinalIgnoreCase);

                if (engine.GetOutputCtrlParam("Score", out var scoreTuple) && scoreTuple.Length > 0)
                {
                    score = scoreTuple[0].D;
                    text = $"Score: {score:F2}";
                }

                sw.Stop();
                return new HalconRunResult
                {
                    Success = true,
                    Ok = ok,
                    Score = score,
                    ResultText = text,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.Error(ex, "Halcon 过程执行失败: {Procedure}", procedureName);
                return new HalconRunResult { Success = false, Error = ex.Message, ElapsedMs = sw.ElapsedMilliseconds };
            }
        }
    }
}
