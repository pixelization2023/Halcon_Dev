using MvCameraControl;

namespace MVS.Core
{
    public class CameraInfo 
    {
        /// <summary>
        /// 原始设备信息对象（关键修正）
        /// </summary>
        public IDeviceInfo DeviceInfo { get; set; }
        /// <summary>
        /// 设备接口类型
        /// </summary>
        ///
        public string TLayerType { get; }
        /// <summary>
        /// 设备型号
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// 序列号
        /// </summary>
        public string SerialNumber { get; set; }

        /// <summary>
        /// 用户自定义名称
        /// </summary>
        public string UserDefinedName { get; set; }

        /// <summary>
        /// IP地址（仅GigE相机）
        /// </summary>
        public string IpAddress { get; set; }
        /// <summary>
        /// IP地址（仅GigE相机）
        /// </summary>
        public string MacAddress { get; set; }

        /// <summary>
        /// 掩码地址（仅GigE相机）
        /// </summary>
        public string SubNetMask { get; set; }
        /// <summary>
        /// 设备类型
        /// </summary>
        public string DeviceType { get; set; }

        /// <summary>
        /// 制造商名称
        /// </summary>
        public string ManufacturerName { get; set; }

        /// <summary>
        /// 相机连接状态
        /// </summary>
        public bool Status { get; set; }

    }
}