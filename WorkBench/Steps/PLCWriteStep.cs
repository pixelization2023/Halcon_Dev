using PLCModule.Interfaces;
using Serilog;
using System.Diagnostics;
using WorkBench.Interfaces;

namespace WorkBench.Steps
{
    public class PLCWriteStep : BaseWorkflowStep
    {
        private readonly Dictionary<string, object> _signals;
        private readonly ILogger _logger;

        public PLCWriteStep(Dictionary<string, object> signals, TimeSpan timeout)
            : base("PLC写入", StepType.PLCWrite, timeout)
        {
            _signals = signals;
            _logger = Serilog.Log.Logger.ForContext<PLCWriteStep>();
        }

        public override async Task<bool> ValidateAsync(IInspectionContext context)
        {
            return _signals.Count > 0;
        }

        public override async Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var plc = MVS.Core.AppContainer.Resolve<IPLCCommunicator>();

                if (!plc.IsConnected)
                {
                    _logger.Warning("PLC未连接，写入跳过");
                    return StepResult.Fail(Name, StepType.PLCWrite, "PLC未连接", sw.ElapsedMilliseconds);
                }

                var resolvedSignals = new Dictionary<string, object>();
                foreach (var kv in _signals)
                {
                    resolvedSignals[kv.Key] = kv.Value switch
                    {
                        Func<object> func => func(),
                        _ => kv.Value
                    };
                }

                _logger.Debug("向PLC写入 {Count} 个信号", resolvedSignals.Count);
                var success = await plc.WriteBatchAsync(resolvedSignals, ct);

                if (success)
                    _logger.Information("PLC写入成功 ({Ms}ms)", sw.ElapsedMilliseconds);
                else
                    _logger.Warning("PLC写入失败");

                return success
                    ? StepResult.Ok(Name, StepType.PLCWrite, sw.ElapsedMilliseconds)
                    : StepResult.Fail(Name, StepType.PLCWrite, "PLC写入失败", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "PLC写入异常");
                return StepResult.Fail(Name, StepType.PLCWrite, ex.Message, sw.ElapsedMilliseconds);
            }
        }
    }
}
