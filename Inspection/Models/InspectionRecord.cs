namespace Inspection.Models
{
    /// <summary>
    /// 检测结果数据库记录。
    /// 迁移自 窗体.Mysql.data1 / data2 —— 保留原表结构与列名（含中文列 PCS号），
    /// 以保证现场已有 MySQL 库无需迁移即可继续写入。
    /// </summary>
    public class InspectionRecord
    {
        /// <summary>自增主键（原 indenx）</summary>
        public int Index { get; set; }

        /// <summary>记录时间（原 DetaTime）</summary>
        public DateTime RecordTime { get; set; } = DateTime.Now;

        /// <summary>图片名称</summary>
        public string PhotoName { get; set; } = string.Empty;

        /// <summary>检测 PCS 号</summary>
        public string PcsNumber { get; set; } = string.Empty;

        /// <summary>纸质码</summary>
        public string PaperCode { get; set; } = string.Empty;

        /// <summary>镭射码</summary>
        public string LaserCode { get; set; } = string.Empty;

        /// <summary>LOT 号</summary>
        public string Lot { get; set; } = string.Empty;

        /// <summary>工号</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>品目</summary>
        public string Item { get; set; } = string.Empty;

        /// <summary>机种</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>NG 点位点集（JSON）</summary>
        public string PointSet { get; set; } = string.Empty;

        /// <summary>结果串，"0"=OK / "1"=NG</summary>
        public string Result { get; set; } = string.Empty;

        public bool IsNg => Result.Contains('1');
    }

    /// <summary>条码记录（迁移自 窗体.Mysql.data2）</summary>
    public class CodeRecord
    {
        public int Index { get; set; }
        public string Code { get; set; } = string.Empty;
        public DateTime RecordTime { get; set; } = DateTime.Now;
    }
}
