using MVS.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiCameraSystem.Services.Interfaces
{
    public interface ICameraService
    {
        /// <summary>
        /// 获取相机列表信息
        /// </summary>
        IEnumerable<CameraInfo> AvailableCameras { get; }

        /// <summary>
        /// 获取相机设备对象
        /// </summary>
        /// <param name="serialNumber"></param>
        /// <returns></returns>
        CameraDevice GetCamera(string serialNumber);
        /// <summary>
        /// 初始化相机服务
        /// </summary>
        void Initialize();
      
        /// <summary>
        ///  刷新相机列表
        /// </summary>

        void RefreshCameras();
        /// <summary>
        /// 关闭相机服务并释放资源
        /// </summary>
        void Shutdown();
    }
}
