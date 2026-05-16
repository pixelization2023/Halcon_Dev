namespace MVS.Core
{
    /// <summary>
    /// 相机状态变更事件参数
    /// </summary>
    public class CameraStatusEventArgs : EventArgs
    {
        /// <summary>
        /// 相机唯一标识符
        /// </summary>
        public string CameraId { get; set; }

        /// <summary>
        /// 相机当前状态
        /// </summary>
        public CameraStatus Status { get; set; }
    }
}