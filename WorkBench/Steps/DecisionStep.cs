using Serilog;
using System.Diagnostics;
using WorkBench.Interfaces;

namespace WorkBench.Steps
{
    public class DecisionStep : BaseWorkflowStep
    {
        private readonly Func<IInspectionContext, bool>? _condition;
        private readonly ILogger _logger;

        public DecisionStep(Func<IInspectionContext, bool>? condition = null)
            : base("判定", StepType.Decision, TimeSpan.FromSeconds(1))
        {
            _condition = condition;
            _logger = Serilog.Log.Logger.ForContext<DecisionStep>();
        }

        public override async Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                bool passed;
                if (_condition != null)
                    passed = _condition(context);
                else
                    passed = context.GetResult<bool>("Passed");

                context.SetResult("FinalJudgment", passed ? "OK" : "NG");

                _logger.Information("判定结果: {Result} ({Ms}ms)", passed ? "OK" : "NG", sw.ElapsedMilliseconds);

                return passed
                    ? StepResult.Ok(Name, StepType.Decision, sw.ElapsedMilliseconds, "OK")
                    : StepResult.Fail(Name, StepType.Decision,
                        context.GetResult<string>("NGReason") ?? "检测不通过", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "判定步骤异常");
                return StepResult.Fail(Name, StepType.Decision, ex.Message, sw.ElapsedMilliseconds);
            }
        }
    }
}
