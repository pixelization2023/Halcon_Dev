using System.Text.Json.Serialization;

namespace Inspection.Models
{
    /// <summary>通用 MES 列表响应（迁移自 窗体.WebApi.List_json&lt;T&gt;）</summary>
    public class MesListResponse<T>
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("msg")] public string? Message { get; set; }
        [JsonPropertyName("obj")] public List<T>? Items { get; set; }
    }

    /// <summary>按长码（蚀刻码）获取 LOT / 机种 / 品目（迁移自 LongCode_Obtain_lotNumber_Method）</summary>
    public class LongCodeInfo
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("msg")] public string? Message { get; set; }
        [JsonPropertyName("obj")] public LongCodePayload? Payload { get; set; }
    }

    public class LongCodePayload
    {
        [JsonPropertyName("LotNo")] public string? LotNo { get; set; }
        [JsonPropertyName("Product")] public string? Product { get; set; }
        [JsonPropertyName("ProductModel")] public string? ProductModel { get; set; }
    }

    /// <summary>指定工程 BM 管控项目（迁移自 BM_List_json）</summary>
    public class BmControlItem
    {
        [JsonPropertyName("FuncName")] public string? FunctionName { get; set; }
        [JsonPropertyName("LinkFlowID")] public string? LinkFlowId { get; set; }
    }

    /// <summary>前工程不良数据（迁移自 FrontSection_list_json）</summary>
    public class FrontSectionItem
    {
        [JsonPropertyName("SHEETSN")] public string? SheetSn { get; set; }
        [JsonPropertyName("PCSNO")] public string? PcsNo { get; set; }
        [JsonPropertyName("PARTNO")] public string? PartNo { get; set; }
        [JsonPropertyName("WRITEDATE")] public string? WriteDate { get; set; }
        [JsonPropertyName("UPDATETIME")] public string? UpdateTime { get; set; }
        [JsonPropertyName("DeletedFlag")] public string? DeletedFlag { get; set; }
        [JsonPropertyName("Dsp")] public string? Dsp { get; set; }
        [JsonPropertyName("PCSNOS")] public string? PcsNos { get; set; }
        [JsonPropertyName("Delete")] public string? Delete { get; set; }
    }

    /// <summary>短码换长码（迁移自 ShortCode_Obtain_LongCode）</summary>
    public class ShortCodeMapping
    {
        [JsonPropertyName("Barcode")] public string? Barcode { get; set; }
        [JsonPropertyName("ShtBarcode")] public string? SheetBarcode { get; set; }
        [JsonPropertyName("PcsIndex")] public string? PcsIndex { get; set; }
    }

    /// <summary>整张测试结果写入的单条数据（迁移自 Sheet_data_json）</summary>
    public class SheetTestData
    {
        [JsonPropertyName("PcsIndex")] public string PcsIndex { get; set; } = string.Empty;
        [JsonPropertyName("TestResult")] public string TestResult { get; set; } = string.Empty;
    }

    /// <summary>PCS 批量结果写入的数据项（迁移自 json_post_piliang_pcs_result_TestResult）</summary>
    public class BatchTestItem
    {
        [JsonPropertyName("PcsBarcode")] public string PcsBarcode { get; set; } = string.Empty;
        [JsonPropertyName("TestResult")] public string TestResult { get; set; } = string.Empty;
        [JsonPropertyName("Extend1")] public string Extend1 { get; set; } = string.Empty;
    }

    /// <summary>PCS 批量结果写入请求体（迁移自 json_post_piliang_pcs_result）</summary>
    public class BatchTestRequest
    {
        [JsonPropertyName("ProductModel")] public string ProductModel { get; set; } = string.Empty;
        [JsonPropertyName("FlowID")] public string FlowId { get; set; } = string.Empty;
        [JsonPropertyName("TestData")] public List<BatchTestItem> TestData { get; set; } = new();
        [JsonPropertyName("MachineID")] public string MachineId { get; set; } = string.Empty;
        [JsonPropertyName("LotNo")] public string LotNo { get; set; } = string.Empty;
        [JsonPropertyName("CreateUser")] public string CreateUser { get; set; } = string.Empty;
        [JsonPropertyName("CreateDate")] public string CreateDate { get; set; } = string.Empty;
    }

    /// <summary>实时良率的 PCS 明细（迁移自 YieldParameter_One）</summary>
    public class YieldPcsInfo
    {
        [JsonPropertyName("PcsIndex")] public int PcsIndex { get; set; }
        [JsonPropertyName("TestResult")] public int TestResult { get; set; }
    }

    /// <summary>实时良率写入请求体（迁移自 YieldParameter_Two）</summary>
    public class YieldUploadRequest
    {
        [JsonPropertyName("SheetBar")] public string SheetBarcode { get; set; } = string.Empty;
        [JsonPropertyName("PINFO")] public List<YieldPcsInfo> PcsInfo { get; set; } = new();
        [JsonPropertyName("ProductModel")] public string ProductModel { get; set; } = string.Empty;
        [JsonPropertyName("Product")] public string Product { get; set; } = string.Empty;
        [JsonPropertyName("LotNo")] public string LotNo { get; set; } = string.Empty;
        [JsonPropertyName("LineName")] public string LineName { get; set; } = string.Empty;
        [JsonPropertyName("EngineerId")] public string EngineerId { get; set; } = string.Empty;
        [JsonPropertyName("SubEngineerId")] public string SubEngineerId { get; set; } = string.Empty;
        [JsonPropertyName("EquipmentId")] public string EquipmentId { get; set; } = string.Empty;
        [JsonPropertyName("TestType")] public string TestType { get; set; } = string.Empty;
        [JsonPropertyName("Ext1")] public string Ext1 { get; set; } = string.Empty;
        [JsonPropertyName("Ext2")] public string Ext2 { get; set; } = string.Empty;
        [JsonPropertyName("StartTime")] public string StartTime { get; set; } = string.Empty;
        [JsonPropertyName("EndTime")] public string EndTime { get; set; } = string.Empty;
        [JsonPropertyName("DeviceId")] public int DeviceId { get; set; }
        [JsonPropertyName("EquipmentType")] public string EquipmentType { get; set; } = "CCD";
    }

    /// <summary>日报明细（迁移自 DailyUpload_Two）</summary>
    public class DailyResultItem
    {
        [JsonPropertyName("DSP")] public string? Dsp { get; set; }
        [JsonPropertyName("ErrClassID")] public int? ErrorClassId { get; set; }
        [JsonPropertyName("PCSResult")] public string PcsResult { get; set; } = string.Empty;
        [JsonPropertyName("PcsIndex")] public int PcsIndex { get; set; }
    }

    /// <summary>日报写入请求体（迁移自 DailyUpload_One）</summary>
    public class DailyUploadRequest
    {
        [JsonPropertyName("CreateUser")] public string CreateUser { get; set; } = string.Empty;
        [JsonPropertyName("EngineerID")] public string EngineerId { get; set; } = string.Empty;
        [JsonPropertyName("EquipmentID")] public string EquipmentId { get; set; } = string.Empty;
        [JsonPropertyName("EquipmentType")] public string EquipmentType { get; set; } = "CCD";
        [JsonPropertyName("LineName")] public string LineName { get; set; } = string.Empty;
        [JsonPropertyName("LotNo")] public string LotNo { get; set; } = string.Empty;
        [JsonPropertyName("ProductModel")] public string ProductModel { get; set; } = string.Empty;
        [JsonPropertyName("Results")] public List<DailyResultItem> Results { get; set; } = new();
        [JsonPropertyName("ShtBarcode")] public string SheetBarcode { get; set; } = string.Empty;
        [JsonPropertyName("SubEngineerID")] public string SubEngineerId { get; set; } = string.Empty;
    }

    /// <summary>样品板不良项（迁移自 Samples_Two）</summary>
    public class SampleDefect
    {
        [JsonPropertyName("PCSNO")] public int PcsNo { get; set; }
        [JsonPropertyName("ComponentName")] public string ComponentName { get; set; } = string.Empty;
        [JsonPropertyName("ErrType")] public string ErrorType { get; set; } = string.Empty;
    }

    /// <summary>样品板数据上传请求体（迁移自 Samples_One）</summary>
    public class SampleUploadRequest
    {
        [JsonPropertyName("ProductModel")] public string ProductModel { get; set; } = string.Empty;
        [JsonPropertyName("Product")] public string Product { get; set; } = string.Empty;
        [JsonPropertyName("LineName")] public string LineName { get; set; } = string.Empty;
        [JsonPropertyName("EngineerID")] public string EngineerId { get; set; } = string.Empty;
        [JsonPropertyName("SubEngineerID")] public string SubEngineerId { get; set; } = string.Empty;
        [JsonPropertyName("EquipmentType")] public string EquipmentType { get; set; } = "CCD";
        [JsonPropertyName("EquipmentID")] public string EquipmentId { get; set; } = string.Empty;
        [JsonPropertyName("Barcode")] public string Barcode { get; set; } = string.Empty;
        [JsonPropertyName("UseType")] public int UseType { get; set; } = 1;
        [JsonPropertyName("DefectResults")] public List<SampleDefect> Defects { get; set; } = new();
    }
}
