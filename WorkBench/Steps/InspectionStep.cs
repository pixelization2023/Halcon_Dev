using Halcon.Core;
using HalconDotNet;
using Serilog;
using System.Diagnostics;
using WorkBench.Interfaces;

namespace WorkBench.Steps
{
    public class InspectionStep : BaseWorkflowStep
    {
        private readonly string _programName;
        private readonly Dictionary<string, object>? _parameters;
        private readonly ILogger _logger;
        private static readonly object _engineLock = new();

        public InspectionStep(string programName, Dictionary<string, object>? parameters = null, TimeSpan? timeout = null)
            : base($"检测-{programName}", StepType.Inspection, timeout ?? TimeSpan.FromSeconds(10))
        {
            _programName = programName;
            _parameters = parameters;
            _logger = Serilog.Log.Logger.ForContext<InspectionStep>();
        }

        public override Task<bool> ValidateAsync(IInspectionContext context)
        {
            return Task.FromResult(!string.IsNullOrEmpty(_programName) &&
                                   context.GetImage("Camera1") != null);
        }

        public override async Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            _logger.Information("开始执行检测: {Program}", _programName);

            try
            {
                var result = await Task.Run(() =>
                {
                    lock (_engineLock)
                    {
                        using var engine = new HalconEngine();
                        engine.LoadHdvpProgram(_programName);

                        if (_parameters != null)
                        {
                            foreach (var kv in _parameters)
                            {
                                if (kv.Value is HTuple tuple)
                                    engine.SetInputCtrlParam(kv.Key, tuple);
                            }
                        }

                        var image = context.GetImage("Camera1");
                        if (image != null && image.IsInitialized())
                            engine.SetInputIconicParam("Image", image);

                        engine.Execute();

                        engine.GetOutputCtrlParam("OK", out var ok);
                        engine.GetOutputIconicParam("NGRegion", out var ngRegion);
                        engine.GetOutputCtrlParam("Score", out var score);

                        return (ok, ngRegion, score);
                    }
                }, ct);

                context.SetResult("Passed", result.ok);
                context.SetResult("NGReason", result.ngRegion);
                context.SetResult("Score", result.score);

                if (result.ok)
                {
                    _logger.Information("检测通过: {Program} Score:{Score:F2} ({Ms}ms)",
                        _programName, result.score, sw.ElapsedMilliseconds);
                    return StepResult.Ok(Name, StepType.Inspection, sw.ElapsedMilliseconds, result.score);
                }
                else
                {
                    _logger.Warning("检测NG: {Program} ({Ms}ms)", _programName, sw.ElapsedMilliseconds);
                    return StepResult.Fail(Name, StepType.Inspection,
                        result.ngRegion?.ToString() ?? "NG", sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "检测异常: {Program}", _programName);
                return StepResult.Fail(Name, StepType.Inspection, ex.Message, sw.ElapsedMilliseconds);
            }
        }
    }
}
