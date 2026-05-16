namespace MESModule.Models
{
    /// <summary>工单信息</summary>
    public class WorkOrderInfo
    {
        /// <summary>工单号</summary>
        public string WorkOrderNo { get; set; } = "";

        /// <summary>产品型号</summary>
        public string ModelName { get; set; } = "";

        /// <summary>计划数量</summary>
        public int PlannedQty { get; set; }

        /// <summary>已生产数量</summary>
        public int ProducedQty { get; set; }

        /// <summary>OK数量</summary>
        public int OkQty { get; set; }

        /// <summary>NG数量</summary>
        public int NgQty { get; set; }

        /// <summary>工单状态</summary>
        public string Status { get; set; } = "进行中";

        /// <summary>开始时间</summary>
        public DateTime StartTime { get; set; }

        /// <summary>结束时间</summary>
        public DateTime? EndTime { get; set; }
    }
}
