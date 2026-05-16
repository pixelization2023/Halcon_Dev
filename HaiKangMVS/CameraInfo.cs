using MvCameraControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{
    /// <summary>
    /// 相机信息类
    /// </summary>
    public class CameraInfo
    {
        /// <summary>
        /// 相机唯一标识符
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 相机制造商
        /// </summary>
        public string Manufacturer { get; }

        /// <summary>
        /// 相机型号
        /// </summary>
        public string Model { get; }

        /// <summary>
        /// 相机序列号
        /// </summary>
        public string SerialNumber { get; }

        /// <summary>
        /// 相机接口类型
        /// </summary>
        public DeviceTLayerType InterfaceType { get; }

        public CameraInfo(IDeviceInfo deviceInfo)
        {
            Id = $"{deviceInfo.ManufacturerName}_{deviceInfo.ModelName}_{deviceInfo.SerialNumber}";
            Manufacturer = deviceInfo.ManufacturerName;
            Model = deviceInfo.ModelName;
            SerialNumber = deviceInfo.SerialNumber;
            InterfaceType = deviceInfo.TLayerType;
        }
    }
}
