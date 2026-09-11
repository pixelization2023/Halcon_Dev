using MESModule.Interfaces;
using MESModule.Models;
using Serilog;
using System.Diagnostics;
using WorkBench.Interfaces;

namespace WorkBench.Steps
{
    public class MESUploadStep : BaseWorkflowStep
    {
        private readonly ILogger _logger;

        /// <summary>
        /// 注入的 MES 连接器（**必需**依赖）。
        ///
        /// 解耦要点：与 <see cref="PLCWriteStep"/> 同理 —— 依赖显式传入，
        /// 不再在 ExecuteAsync 里走 <c>MVS.Core.AppContainer</c> 服务定位器。
        /// </summary>
        private readonly IMESConnector _mesConnector;

        public MESUploadStep(TimeSpan timeout, IMESConnector mesConnector)
            : base("MES上传", StepType.MESUpload, timeout)
        {
            _mesConnector = mesConnector
                ?? throw new ArgumentNullException(nameof(mesConnector),
                    "MESUploadStep 需要 IMESConnector；请由组合根注入（WorkBenchViewModel 会从容器取）。");
            _logger = Serilog.Log.Logger.ForContext<MESUploadStep>();
        }

        public override Task<bool> ValidateAsync(IInspectionContext context)
        {
            return Task.FromResult(!string.IsNullOrEmpty(context.ProductBarcode));
        }

        public override async Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var mes = _mesConnector;

                if (!mes.IsConnected)
                {
                    _logger.Warning("MES离线，结果进入离线队列");
                    return StepResult.Ok(Name, StepType.MESUpload, sw.ElapsedMilliseconds, "offline_queued");
                }

                var result = new InspectionResult
                {
                    Barcode = context.ProductBarcode,
                    WorkOrderNo = context.Metadata.GetValueOrDefault("WorkOrderNo", ""),
                    Judgment = context.GetResult<string>("FinalJudgment") == "OK"
                        ? InspectionJudgment.OK : InspectionJudgment.NG,
                    NgReason = context.GetResult<string>("NGReason"),
                    ElapsedMs = sw.ElapsedMilliseconds,
                    InspectedAt = DateTime.Now,
                    LineId = context.Metadata.GetValueOrDefault("LineId", ""),
                    StationId = context.Metadata.GetValueOrDefault("StationId", "")
                };

                _logger.Debug("上传检测结果: {Barcode} [{Judgment}]", result.Barcode, result.Judgment);
                var success = await mes.UploadInspectionResultAsync(result, ct);

                if (success)
                    _logger.Information("MES上传成功: {Barcode} ({Ms}ms)", result.Barcode, sw.ElapsedMilliseconds);
                else
                    _logger.Warning("MES上传失败: {Barcode}", result.Barcode);

                return success
                    ? StepResult.Ok(Name, StepType.MESUpload, sw.ElapsedMilliseconds)
                    : StepResult.Fail(Name, StepType.MESUpload, "MES上传失败", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MES上传异常");
                return StepResult.Fail(Name, StepType.MESUpload, ex.Message, sw.ElapsedMilliseconds);
            }
        }
    }
}
