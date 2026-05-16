namespace WorkBench.Interfaces
{
    /// <summary>步骤执行结果</summary>
    public class StepResult
    {
        public string StepName { get; init; } = "";
        public StepType StepType { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public long ElapsedMs { get; init; }
        public object? Output { get; init; }
        public DateTime CompletedAt { get; init; } = DateTime.Now;

        public static StepResult Ok(string name, StepType type, long elapsed, object? output = null)
            => new() { StepName = name, StepType = type, Success = true, ElapsedMs = elapsed, Output = output };

        public static StepResult Fail(string name, StepType type, string error, long elapsed)
            => new() { StepName = name, StepType = type, Success = false, ErrorMessage = error, ElapsedMs = elapsed };
    }
}
