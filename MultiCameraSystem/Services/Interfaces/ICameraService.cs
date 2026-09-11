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
        /// 相机 SDK 是否已成功初始化。
        /// 为什么要暴露：Initialize() 失败（MVS 运行时缺失 / 版本不匹配）以前是被吞掉的，
        /// 之后 RefreshCameras() 会抛 InvalidOperationException，界面上却只表现为"点刷新没反应"。
        /// 有了这个标志，界面可以直接把"SDK 不可用"和原因显示出来。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>SDK 初始化失败时的原因（成功时为 null）</summary>
        string? InitializationError { get; }

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
        /// 刷新相机列表（不抛异常版本；返回 false 时请看 <see cref="InitializationError"/>）
        /// </summary>
        bool TryRefreshCameras();

        /// <summary>
        /// 关闭相机服务并释放资源
        /// </summary>
        void Shutdown();
    }
}
