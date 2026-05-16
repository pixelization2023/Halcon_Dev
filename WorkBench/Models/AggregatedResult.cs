namespace WorkBench.Interfaces
{
    /// <summary>聚合检测结果</summary>
    public class AggregatedResult
    {
        public bool OverallSuccess { get; set; }
        public List<StepResult> StepResults { get; set; } = new();
        public long TotalElapsedMs { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.Now;
        public string? FinalJudgment { get; set; }
        public string? ErrorSummary { get; set; }

        /// <summary>是否通过（颜色由视图按主题画刷决定，模型不再持有颜色）</summary>
        public bool IsPass => string.Equals(FinalJudgment, "PASS", StringComparison.Ordinal);

        public override string ToString()
            => $"[{(OverallSuccess ? "PASS" : "FAIL")}] {StepResults.Count} steps, {TotalElapsedMs}ms";
    }
}
