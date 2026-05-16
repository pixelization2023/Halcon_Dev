namespace MESModule.Models
{
    /// <summary>检测判定</summary>
    public enum InspectionJudgment
    {
        OK,
        NG,
        Retry,
        Skip
    }

    /// <summary>检测结果（上报MES用）</summary>
    public class InspectionResult
    {
        /// <summary>产品条码</summary>
        public string Barcode { get; set; } = "";

        /// <summary>工单号</summary>
        public string WorkOrderNo { get; set; } = "";

        /// <summary>检测判定</summary>
        public InspectionJudgment Judgment { get; set; } = InspectionJudgment.OK;

        /// <summary>NG原因（判定NG时）</summary>
        public string? NgReason { get; set; }

        /// <summary>检测项明细（如: {"划痕":"OK","缺角":"NG"}）</summary>
        public Dictionary<string, string> Details { get; set; } = new();

        /// <summary>检测耗时(ms)</summary>
        public long ElapsedMs { get; set; }

        /// <summary>检测时间</summary>
        public DateTime InspectedAt { get; set; } = DateTime.Now;

        /// <summary>相机编号</summary>
        public string? CameraId { get; set; }

        /// <summary>产线/工站</summary>
        public string LineId { get; set; } = "";
        public string StationId { get; set; } = "";

        /// <summary>图像保存路径（可选）</summary>
        public string? ImagePath { get; set; }

        public override string ToString()
            => $"[{Judgment}] {Barcode} @ {InspectedAt:HH:mm:ss}";
    }
}
