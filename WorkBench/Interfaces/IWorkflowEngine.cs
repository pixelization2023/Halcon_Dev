namespace WorkBench.Interfaces
{
    /// <summary>
    /// 流程引擎 — 编排检测流程的执行
    /// </summary>
    public interface IWorkflowEngine
    {
        /// <summary>执行流程</summary>
        Task<AggregatedResult> ExecuteAsync(WorkflowDefinition workflow, IInspectionContext context, CancellationToken ct);

        /// <summary>停止当前流程</summary>
        Task StopAsync();

        bool IsRunning { get; }
        IInspectionContext? CurrentContext { get; }

        event EventHandler<StepResult>? StepCompleted;
        event EventHandler<AggregatedResult>? WorkflowCompleted;
        event EventHandler<string>? WorkflowError;
    }
}
