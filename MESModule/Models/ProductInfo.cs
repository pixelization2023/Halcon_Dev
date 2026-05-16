namespace MESModule.Models
{
    /// <summary>产品信息</summary>
    public class ProductInfo
    {
        /// <summary>产品条码（唯一标识）</summary>
        public string Barcode { get; set; } = "";

        /// <summary>产品型号</summary>
        public string ModelName { get; set; } = "";

        /// <summary>工单号</summary>
        public string WorkOrderNo { get; set; } = "";

        /// <summary>产品状态</summary>
        public string Status { get; set; } = "待检测";

        /// <summary>生产线</summary>
        public string LineId { get; set; } = "";

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>更新时间</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
