using HalconDotNet;
using Serilog;
using System.Diagnostics;
using WorkBench.Interfaces;

namespace WorkBench.Steps
{
    public class AcquisitionStep : BaseWorkflowStep
    {
        private readonly string _cameraName;
        private readonly ILogger _logger;

        public AcquisitionStep(string cameraName, TimeSpan timeout)
            : base($"采集-{cameraName}", StepType.Acquisition, timeout)
        {
            _cameraName = cameraName;
            _logger = Serilog.Log.Logger.ForContext<AcquisitionStep>();
        }

        public override Task<bool> ValidateAsync(IInspectionContext context)
        {
            return Task.FromResult(!string.IsNullOrEmpty(_cameraName));
        }

        public override Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var image = context.GetImage(_cameraName);
                if (image == null || !image.IsInitialized())
                {
                    _logger.Warning("相机 {Camera} 图像为空", _cameraName);
                    return Task.FromResult(StepResult.Fail(Name, StepType.Acquisition, $"相机 {_cameraName} 图像为空", sw.ElapsedMilliseconds));
                }

                var clone = image.Clone();
                context.SetImage(_cameraName, clone);
                _logger.Debug("相机 {Camera} 采集完成 ({Ms}ms)", _cameraName, sw.ElapsedMilliseconds);

                return Task.FromResult(StepResult.Ok(Name, StepType.Acquisition, sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "相机 {Camera} 采集失败", _cameraName);
                return Task.FromResult(StepResult.Fail(Name, StepType.Acquisition, ex.Message, sw.ElapsedMilliseconds));
            }
        }
    }
}
