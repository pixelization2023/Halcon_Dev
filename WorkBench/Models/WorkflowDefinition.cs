using WorkBench.Interfaces;

namespace WorkBench.Interfaces
{
    /// <summary>流程定义 — 描述一个完整的检测流程</summary>
    public class WorkflowDefinition
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public List<IWorkflowStep> Steps { get; set; } = new();
        public TimeSpan GlobalTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetries { get; set; } = 1;
    }
}
