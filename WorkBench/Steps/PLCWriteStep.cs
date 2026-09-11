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

        /// <summary>
        /// 注入的 PLC 通信器（**必需**依赖）。
        ///
        /// 解耦要点：依赖通过构造函数显式传入。旧实现在 ExecuteAsync 里走
        /// <c>MVS.Core.AppContainer</c> 服务定位器取 —— 那样依赖关系编译期不可见、
        /// 只能等执行到这一步才发现缺失，也无法在测试里替换。
        /// 改成必需参数后，注册期就能发现缺失（容器解析失败会直接报错，而不是静默跳过步骤）。
        /// </summary>
        private readonly IPLCCommunicator _plcCommunicator;

        public PLCWriteStep(Dictionary<string, object> signals, TimeSpan timeout, IPLCCommunicator plcCommunicator)
            : base("PLC写入", StepType.PLCWrite, timeout)
        {
            _signals = signals;
            _plcCommunicator = plcCommunicator
                ?? throw new ArgumentNullException(nameof(plcCommunicator),
                    "PLCWriteStep 需要 IPLCCommunicator；请由组合根注入（WorkBenchViewModel 会从容器取）。");
            _logger = Serilog.Log.Logger.ForContext<PLCWriteStep>();
        }

        public override Task<bool> ValidateAsync(IInspectionContext context)
        {
            return Task.FromResult(_signals.Count > 0);
        }

        public override async Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var plc = _plcCommunicator;

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
