using MvCameraControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{
    /// <summary>
    /// 相机管理器类，用于管理和控制多个相机
    /// </summary>
    public class CameraManager : IDisposable
    {

        // 相机控制器列表
        private readonly List<ICameraController> _cameras = new List<ICameraController>();

        // 相机事件
        public event CameraEventHandler CameraEvent;

        // 只读相机列表
        public IReadOnlyList<ICameraController> Cameras => _cameras.AsReadOnly();

        /// <summary>
        /// 初始化所有相机
        /// </summary>          

        public CameraManager()
        {
          
        }
        public void InitializeAll()
        {
            try
            {
                int ret = SDKSystem.Initialize();
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"SDK初始化失败，错误代码: {ret}");

                List<IDeviceInfo> deviceInfos;
                ret = DeviceEnumerator.EnumDevices(
                    DeviceTLayerType.MvGigEDevice |
                    DeviceTLayerType.MvUsbDevice |
                    DeviceTLayerType.MvGenTLCXPDevice |
                    DeviceTLayerType.MvGenTLXoFDevice,
                    out deviceInfos
                );

                if (ret != MvError.MV_OK || deviceInfos == null || deviceInfos.Count == 0)
                    throw new ApplicationException("未找到可用相机设备");

                // 创建相机控制器
                foreach (var deviceInfo in deviceInfos)
                {
                    var camera = new CameraController(deviceInfo);
                    camera.CameraEvent += OnCameraEvent;
                    _cameras.Add(camera);
                }

     
                OnCameraEvent(sender: null, e: new CameraEventArgs( cameraId: null, message: $"相机系统初始化完成，发现 {_cameras.Count} 台相机"));

            }
            catch (Exception ex)
            {

                OnCameraEvent(sender: null, e: new CameraEventArgs(cameraId: null, message: $"相机系统初始化失败: {ex.Message}"));
                throw;
            }
        }

        /// <summary>
        /// 通过相机ID获取相机控制器
        /// </summary>
        /// <param name="cameraId">相机ID</param>
        /// <returns>相机控制器</returns>
        public ICameraController GetCameraById(string cameraId)
        {
            return _cameras.FirstOrDefault(c => c.CameraId == cameraId);
        }

        /// <summary>
        /// 打开所有相机
        /// </summary>
        public void OpenAllCameras()
        {
            Parallel.ForEach(_cameras, camera =>
            {
                try
                {
                    
                    camera.Open();
                }
                catch
                {
                    // 错误处理已在相机内部完成
                }
            });
        }

        /// <summary>
        /// 配置所有相机
        /// </summary>
        /// <param name="config">相机配置</param>
        public void ConfigureAllCameras(CameraConfiguration config = null)
        {
            Parallel.ForEach(_cameras, camera =>
            {
                try
                {
                    if (config==null)
                    {
                       
                    }

                    camera.Configure(config);
                }
                catch
                {
                    // 错误处理已在相机内部完成
                }
            });
        }

        /// <summary>
        /// 启动所有相机采集
        /// </summary>
        public void StartGrabbingAll()
        {
            Parallel.ForEach(_cameras, camera =>
            {
                try
                {
                    camera.StartGrabbing();
                }
                catch
                {
                    // 错误处理已在相机内部完成
                }
            });
        }

        /// <summary>
        /// 所有相机采集指定数量的图片
        /// </summary>
        /// <param name="countPerCamera">每台相机采集的图片数量</param>
        public void CaptureFromAllCameras(int countPerCamera)
        {
            Parallel.ForEach(_cameras, camera =>
            {
                try
                {
                    camera.CaptureImages(countPerCamera);
                }
                catch
                {
                    // 错误处理已在相机内部完成
                }
            });
        }

        /// <summary>
        /// 关闭所有相机
        /// </summary>
        public void CloseAllCameras()
        {
            Parallel.ForEach(_cameras, camera =>
            {
                try
                {
                    camera.Close();
                }
                catch
                {
                    // 错误处理已在相机内部完成
                }
            });
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            CloseAllCameras();
            SDKSystem.Finalize();
        }

        /// <summary>
        /// 相机事件回调
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="e">事件参数</param>
        private void OnCameraEvent(object sender, CameraEventArgs e)
        {
            CameraEvent?.Invoke(sender, e);
        }

    }
}
