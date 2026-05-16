using MvCameraControl;
using System.Drawing;
using System.Drawing.Imaging;

namespace HaiKangMVS
{

    /// <summary>
    /// 相机控制器实现类，封装了相机的初始化、打开、配置、采集、关闭等操作
    /// </summary>
    public class CameraController : ICameraController
    {
        /// <summary>
        /// 设备信息
        /// </summary>
        private readonly IDeviceInfo _deviceInfo;

        /// <summary>
        /// 设备实例
        /// </summary>
        private IDevice _device;

        /// <summary>
        /// 相机配置
        /// </summary>
        private CameraConfiguration _config=new CameraConfiguration();

        /// <summary>
        /// SDK是否已初始化
        /// </summary>
        private bool _isInitialized;

        /// <summary>
        /// 是否正在采集
        /// </summary>
        private bool _isGrabbing;

        /// <summary>
        /// 相机ID
        /// </summary>
        public string CameraId { get; }

        /// <summary>
        /// 相机状态
        /// </summary>
        public CameraStatus Status { get; private set; }

        /// <summary>
        /// 相机信息
        /// </summary>
        public CameraInfo Info { get; }

        /// <summary>
        /// 相机事件
        /// </summary>
        public event CameraEventHandler CameraEvent;

        /// <summary>
        /// 构造函数，初始化相机控制器
        /// </summary>
        /// <param name="deviceInfo">设备信息</param>
        public CameraController(IDeviceInfo deviceInfo)
        {
            _deviceInfo = deviceInfo;
            Info = new CameraInfo(deviceInfo);
            CameraId = Info.Id;
            Status = CameraStatus.Disconnected;
            _isInitialized = false;
            _isGrabbing = false;
        }

        /// <summary>
        /// 触发相机事件
        /// </summary>
        /// <param name="message">事件消息</param>
        /// <param name="image">相关图像</param>
        private void OnCameraEvent(string message, Bitmap image = null)
        {
            CameraEvent?.Invoke(this, new CameraEventArgs(CameraId, image, message));
        }

        /// <summary>
        /// 初始化SDK
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                int ret = SDKSystem.Initialize();
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"SDK初始化失败，错误代码: {ret}");

                _isInitialized = true;
                OnCameraEvent($"相机 {CameraId} 初始化成功");
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 打开相机
        /// </summary>
        public void Open()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("请先初始化相机");

            if (Status == CameraStatus.Connected) return;
            try
            {
                _device = DeviceFactory.CreateDevice(_deviceInfo);
                int ret = _device.Open();
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"打开相机失败，错误代码: {ret}");

                Status = CameraStatus.Connected;
                OnCameraEvent($"相机 {CameraId} 已连接");
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 连接失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 配置相机参数
        /// </summary>
        /// <param name="config">相机配置</param>
        public void Configure(CameraConfiguration config = null)
        {
            if (Status != CameraStatus.Connected)
                throw new InvalidOperationException("请先连接相机");

            _config = config ?? new CameraConfiguration() { ExposureTime =1000f};

            try
            {
                // 设置触发模式
                int ret = _device.Parameters.SetEnumValue("TriggerMode", _config.TriggerMode);
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"设置触发模式失败，错误代码: {ret}");

                // 优化GigE相机包大小
                if (_config.OptimizePacketSize && _device is IGigEDevice gigEDevice)
                {
                    int optimalPacketSize;
                    gigEDevice.GetOptimalPacketSize(out optimalPacketSize);

                    IIntValue currentPacketSize;
                    _device.Parameters.GetIntValue("GevSCPSPacketSize", out currentPacketSize);

                    if (currentPacketSize.CurValue != optimalPacketSize)
                    {
                        ret = gigEDevice.Parameters.SetIntValue("GevSCPSPacketSize", optimalPacketSize);
                        if (ret != MvError.MV_OK)
                            throw new ApplicationException($"设置包大小失败，错误代码: {ret}");
                    }
                }


               

              ret = _device.Parameters.SetFloatValue("ExposureTime", (float) _config.ExposureTime);
                if (ret != MvError.MV_OK)
                {
                
                    throw new ApplicationException($"设置曝光时间失败，错误代码: {ret}");
                }


                float gainValue = _config.GainValue > 0 ? _config.GainValue : 1.0f;
                ret = _device.Parameters.SetFloatValue("Gain", gainValue);
                if (ret != MvError.MV_OK)
                {
                    throw new ApplicationException($"设置增益失败，错误代码: {ret}");
                }





                OnCameraEvent($"相机 {CameraId} 配置完成");
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 配置失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 开始采集图像
        /// </summary>
        public void StartGrabbing()
        {
            if (Status != CameraStatus.Connected )
                throw new InvalidOperationException("请先连接相机");

            if (_isGrabbing) return;

            try
            {
                _device.StreamGrabber.SetImageNodeNum((uint)_config.BufferCount);
                int ret = _device.StreamGrabber.StartGrabbing();
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"开始采集失败，错误代码: {ret}");

                _isGrabbing = true;
                Status = CameraStatus.Grabbing;
                OnCameraEvent($"相机 {CameraId} 开始采集");
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 开始采集失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 停止采集图像
        /// </summary>
        public void StopGrabbing()
        {
            if (!_isGrabbing) return;

            try
            {
                int ret = _device.StreamGrabber.StopGrabbing();
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"停止采集失败，错误代码: {ret}");

                _isGrabbing = false;
                Status = CameraStatus.Connected;
                OnCameraEvent($"相机 {CameraId} 停止采集");
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 停止采集失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 抓取单帧图像
        /// </summary>
        /// <returns>抓取到的图像</returns>
        public Bitmap GrabSingleFrame()
        {
            if (Status != CameraStatus.Grabbing)
            {
                OnCameraEvent("请先开始采集");
                return null;
            }
            try
            {
                IFrameOut frame;
                int ret = _device.StreamGrabber.GetImageBuffer((uint)_config.GrabTimeout, out frame);
                if (ret != MvError.MV_OK)
                    throw new ApplicationException($"获取图像失败，错误代码: {ret}");

                using (frame)
                {
                    var bitmap = frame.Image.ToBitmap();
                    OnCameraEvent($"相机 {CameraId} 捕获单帧图像", bitmap);
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 捕获图像失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 异步抓取单帧图像
        /// </summary>
        /// <returns>抓取到的图像</returns>
        public async Task<Bitmap> GrabSingleFrameAsync()
        {
            return await Task.Run(() => GrabSingleFrame());
        }

        /// <summary>
        /// 连续抓取多帧并保存图像
        /// </summary>
        /// <param name="count">抓取帧数</param>
        public void CaptureImages(int count)
        {
            if (Status != CameraStatus.Grabbing)
                throw new InvalidOperationException("请先开始采集");

            try
            {
                for (int i = 0; i < count; i++)
                {
                    using (var bmp = GrabSingleFrame())
                    {
                        string extension = GetImageExtension(_config.ImageFormat);
                        string fileName = $"{_config.ImagePrefix}_{CameraId}_{DateTime.Now:yyyyMMdd_HHmmss}_{i}{extension}";
                        bmp.Save(fileName, _config.ImageFormat);
                        OnCameraEvent($"相机 {CameraId} 保存图像: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 捕获图像失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 关闭相机
        /// </summary>
        public void Close()
        {
            if (Status == CameraStatus.Disconnected) return;

            try
            {
                StopGrabbing();

                if (_device != null && _device.IsConnected)
                {
                    int ret = _device.Close();
                    if (ret != MvError.MV_OK)
                        throw new ApplicationException($"关闭相机失败，错误代码: {ret}");
                }

                Status = CameraStatus.Disconnected;
                OnCameraEvent($"相机 {CameraId} 已断开");
            }
            catch (Exception ex)
            {
                Status = CameraStatus.Error;
                OnCameraEvent($"相机 {CameraId} 断开失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                Close();
                if (_isInitialized)
                {
                    SDKSystem.Finalize();
                    _isInitialized = false;
                }
            }
            catch
            {
                // 确保资源释放
            }
        }

        /// <summary>
        /// 获取图像格式对应的文件扩展名
        /// </summary>
        /// <param name="format">图像格式</param>
        /// <returns>扩展名</returns>
        private string GetImageExtension(ImageFormat format)
        {
            if (format.Equals(ImageFormat.Bmp)) return ".bmp";
            if (format.Equals(ImageFormat.Jpeg)) return ".jpg";
            if (format.Equals(ImageFormat.Png)) return ".png";
            if (format.Equals(ImageFormat.Tiff)) return ".tiff";
            return ".img";
        }

        /// <summary>
        /// 获取相机配置参数
        /// </summary>
        /// <param name="config"></param>
        public void Getfigure(out CameraConfiguration config)
        {
            // 初始化输出参数
            config = new CameraConfiguration();

            // 检查设备是否有效
            if (_device == null || _device.Parameters == null)
            {
                throw new InvalidOperationException("相机设备未初始化或参数接口无效");
            }

            // 用于接收相机参数的变量
            IFloatValue floatValue;

            // 获取曝光时间
            int result = _device.Parameters.GetFloatValue("ExposureTime", out floatValue);
            if (result == MvError.MV_OK && floatValue != null)
            {
                config.ExposureTime = floatValue.CurValue; // 赋值给配置对象
            }
            else
            {
                // 记录错误或使用默认值
                Console.WriteLine($"获取曝光时间失败，错误码: {result}");

                throw new ApplicationException($"获取曝光时间失败，错误码: {result}");
            }

            // 获取增益值
            result = _device.Parameters.GetFloatValue("Gain", out floatValue);
            if (result == MvError.MV_OK && floatValue != null)
            {
                config.GainValue = floatValue.CurValue; // 赋值给配置对象
            }
            else
            {
                Console.WriteLine($"获取增益值失败，错误码: {result}");
                throw new ApplicationException($"获取增益值失败，错误码: {result}");
            }

            // 获取帧率（注意：您的配置类中还没有帧率属性）
            result = _device.Parameters.GetFloatValue("ResultingFrameRate", out floatValue);
            if (result == MvError.MV_OK && floatValue != null)
            {
                // 如果需要在配置中存储帧率，可以添加FrameRate属性
                config.FrameRate = floatValue.CurValue;
            }
            else
            {
                Console.WriteLine($"获取帧率失败，错误码: {result}");
                throw new ApplicationException($"获取帧率失败，错误码: {result}");
            }
        }
        /// <summary>
        /// 强制停止相机
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void ForceStop()
        {
            _device.Dispose();
            _device = null;

        }
    }
}
