namespace WorkBench.Interfaces
{
    /// <summary>步骤类型</summary>
    public enum StepType
    {
        Trigger,      // 等待触发
        Acquisition,  // 图像采集
        Inspection,   // 视觉检测
        Decision,     // 判定分支
        PLCWrite,     // PLC写入
        MESUpload     // MES上传
    }

    /// <summary>
    /// 流程步骤接口 — 检测流程的最小执行单元
    /// </summary>
    public interface IWorkflowStep
    {
        string Name { get; }
        StepType Type { get; }

        /// <summary>执行步骤</summary>
        Task<StepResult> ExecuteAsync(IInspectionContext context, CancellationToken ct);

        /// <summary>执行前校验</summary>
        Task<bool> ValidateAsync(IInspectionContext context);

        /// <summary>超时时间</summary>
        TimeSpan Timeout { get; }
    }
}
