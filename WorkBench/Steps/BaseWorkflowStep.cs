using WorkBench.Interfaces;

namespace WorkBench.Steps
{
    /// <summary>步骤基类</summary>
    public abstract class BaseWorkflowStep : IWorkflowStep
    {
        public string Name { get; }
        public StepType Type { get; }
        public TimeSpan Timeout { get; }

        protected BaseWorkflowStep(string name, StepType type, TimeSpan timeout)
        {
            Name = name;
            Type = type;
            Timeout = timeout;
        }

        public abstract Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct);

        public virtual Task<bool> ValidateAsync(IInspectionContext context) => Task.FromResult(true);
    }
}
