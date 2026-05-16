using HalconDotNet;

namespace WorkBench.Interfaces
{
    /// <summary>检测状态</summary>
    public enum InspectionStatus
    {
        Idle,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 检测上下文 — 贯穿整个检测流程的数据载体，线程安全
    /// </summary>
    public interface IInspectionContext
    {
        /// <summary>产品条码</summary>
        string ProductBarcode { get; set; }

        /// <summary>检测开始时间</summary>
        DateTime StartTime { get; }

        /// <summary>全局检测状态</summary>
        InspectionStatus Status { get; set; }

        /// <summary>存储相机采集的图像</summary>
        void SetImage(string cameraName, HObject image);
        HObject? GetImage(string cameraName);

        /// <summary>存储步骤结果</summary>
        void SetResult(string stepName, object result);
        T? GetResult<T>(string stepName);

        /// <summary>PLC数据交互</summary>
        void SetPLCData(string signalName, object value);
        T? GetPLCData<T>(string signalName);

        /// <summary>元数据（扩展字段）</summary>
        Dictionary<string, string> Metadata { get; }
    }
}
