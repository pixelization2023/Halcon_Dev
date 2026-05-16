using MvCameraControl;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVS.Core
{
    /// <summary>
    /// 相机设备 - 封装单个相机的操作
    /// </summary>
    public class CameraDevice : IDisposable, IConnectManage, IParameterManage, IGrabControl
    {
        private readonly ILogger _logger;
        private bool _isSingleFrameMode = false;
        private IDevice _device;
        private bool _isDisposed = false;

        /// <summary>
        /// 相机唯一标识符
        /// </summary>
        public string CameraId { get; }

       /// <summary>
       /// 相机状态
       /// </summary>
        public CameraStatus Status { get; private set; } = CameraStatus.Disconnected;

        /// <summary>
        /// 相机信息
        /// </summary>
        public CameraInfo Info { get; }

       /// <summary>
       /// 设备参数接口
       /// </summary>
        private IParameters Parameters => _device.Parameters;

        /// <summary>
        /// 相机是否已连接
        /// </summary>
        public bool IsConnected => _device != null && _device.IsConnected;

        /// <summary>
        /// 相机是否正在采集图像
        /// </summary>
        public bool IsGrabbing;

        /// <summary>
        /// 图像到达事件
        /// </summary>

        public event EventHandler<FrameGrabbedEventArgs> FrameGrabbed;

        /// <summary>
        /// 相机状态变更事件
        /// </summary>
        public event EventHandler<CameraStatusEventArgs> StatusChanged;

        /// <summary>
        /// 创建相机实例
        /// </summary>
        /// <param name="cameraInfo">相机信息</param>
        /// <param name="logger">可选日志记录器</param>
        public CameraDevice(CameraInfo cameraInfo, ILogger? logger = null)
        {
            Info = cameraInfo;
            CameraId = $"{cameraInfo.ModelName}_{cameraInfo.SerialNumber}";
            _logger = logger ?? Serilog.Log.Logger.ForContext<CameraDevice>();
        }

        /// <summary>
        /// 连接到相机设备
        /// </summary>
        public void Connect()
        {
            if (IsConnected) return;

            // 直接使用枚举时保存的原始设备信息
            if (Info.DeviceInfo == null)
            {
                throw new CameraException($"相机设备信息无效: {Info.ModelName} ({Info.SerialNumber})");
            }

            _device = DeviceFactory.CreateDevice(Info.DeviceInfo);
            int ret = _device.Open();
            if (ret != MvError.MV_OK)
            {
                _device = null;
                throw new CameraException($"设备连接失败: 0x{ret:X8}");
            }

            // 优化GigE相机网络包大小
            if (_device is IGigEDevice gigeDevice)
            {
                ret = gigeDevice.GetOptimalPacketSize(out int packetSize);
                if (ret == MvError.MV_OK && packetSize > 0)
                {
                    _device.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
                }
            }

            // 注册图像回调
            _device.StreamGrabber.FrameGrabedEvent += OnFrameGrabbed;

            OnStatusChanged(CameraStatus.Connected);
            _logger.Information("相机 {CameraId} 已连接", CameraId);
        }





        /// <summary>
        /// 采集单帧图像
        /// </summary>
        /// <exception cref="CameraException"></exception>
        public void GrabSingleFrame()
        {
            if (Status != CameraStatus.Connected) throw new CameraException("设备未连接");

            // 设置单帧模式
            int ret = _device.Parameters.SetEnumValue("AcquisitionMode", 0);
            if (ret != MvError.MV_OK)
                throw new CameraException($"设置单帧模式失败: 0x{ret:X8}");

            // 关闭触发
            ret = _device.Parameters.SetEnumValue("TriggerMode", 0);
            if (ret != MvError.MV_OK)
                throw new CameraException($"关闭触发模式失败: 0x{ret:X8}");

            // 使用推荐缓冲区数量(3)
            StartGrabbing(3);
            _isSingleFrameMode = true;
        }








        /// <summary>
        /// 硬件触发拍照
        /// </summary>
        /// <exception cref="CameraException"></exception>
        /// 

        public enum TriggerSource
        {
            /// <summary>
            /// 外部触发输入1
            /// </summary>
            Line1 = 1,
            /// <summary>
            /// 外部触发输入2
            /// </summary>
            Line2 = 2,
            /// <summary>
            /// 软件触发
            /// </summary>
            Software = 7
        }










        /// <summary>
        /// 开始采集图像
        /// </summary>
        /// <param name="bufferCount">图像缓冲区数量</param>
        ///
        public void StartGrabbing(uint bufferCount = 5)
        {
            if (!IsConnected) throw new CameraException("设备未连接");
            if (IsGrabbing) return;

            _device.StreamGrabber.SetImageNodeNum(bufferCount);
            var ret = _device.StreamGrabber.StartGrabbing();

            if (ret != MvError.MV_OK)
            {
                _logger.Error("开始采集失败: 0x{Ret:X8}", ret);
                throw new CameraException($"开始采集失败: 0x{ret:X8}");
            }
            IsGrabbing = true;
            _logger.Information("相机 {CameraId} 开始采集 (缓冲区: {Buffer})", CameraId, bufferCount);
            OnStatusChanged(CameraStatus.Grabbing);
           
        }

        /// <summary>
        /// 停止采集图像
        /// </summary>
        public void StopGrabbing()
        {
            if (!IsGrabbing) return;

            int ret = _device.StreamGrabber.StopGrabbing();
            if (ret != MvError.MV_OK)
            {
                _logger.Error("停止采集失败: 0x{Ret:X8}", ret);
                throw new CameraException($"停止采集失败: 0x{ret:X8}");
            }
            IsGrabbing = false;
            _logger.Information("相机 {CameraId} 已停止采集", CameraId);
            OnStatusChanged(CameraStatus.Connected);
          
        }


        /// <summary>
        /// 执行软触发（需先设置软触发模式）
        /// </summary>
        public void ExecuteSoftwareTrigger()
        {
            if (!IsConnected) throw new CameraException("设备未连接");
            if (!IsGrabbing) throw new CameraException("未开始采集");
            //检查当前是否为软触发模式
            IEnumValue triggerSource;
            _device.Parameters.GetEnumValue("TriggerSource", out triggerSource);

            if (triggerSource.CurEnumEntry.Symbolic != "Software")
                throw new CameraException("未配置软触发模式");

            int ret = _device.Parameters.SetCommandValue("TriggerSoftware");
            if (ret != MvError.MV_OK)
                throw new CameraException($"软触发失败: 0x{ret:X8}");
            _logger.Debug("相机 {CameraId} 软触发已执行", CameraId);
        }






      
         /// <summary>
         /// 设置连续采集模式
         /// </summary>
        public void SetContinuousMode()
        {
            if (!IsConnected) throw new CameraException("设备未连接");
            if (IsGrabbing)
                throw new CameraException("请先停止采集再修改模式");

            // 1. 关闭触发模式
            int ret = _device.Parameters.SetEnumValue("TriggerMode", 0);
            if (ret != MvError.MV_OK)
                throw new CameraException($"关闭触发模式失败: 0x{ret:X8}");
            // 2. 设置连续采集模式
            ret = _device.Parameters.SetEnumValue("AcquisitionMode", 2u); // Continuous
            if (ret != MvError.MV_OK)
                throw new CameraException($"设置连续模式失败: 0x{ret:X8}");
            _logger.Information("相机 {CameraId} 设置为连续采集模式", CameraId);
        }


        /// <summary>
        /// 设置软触发模式
        /// </summary>
        /// <exception cref="CameraException"></exception>
        public void SetSoftwareTriggerMode()
        {
            if (!IsConnected) throw new CameraException("设备未连接");
            if (IsGrabbing) throw new CameraException("请先停止采集再修改模式");
            // 1. 启用触发模式
            int ret = _device.Parameters.SetEnumValue("TriggerMode", 1u);
            if (ret != MvError.MV_OK)
                throw new CameraException($"启用触发模式失败: 0x{ret:X8}");

            // 2. 设置触发源为软件
            ret = _device.Parameters.SetEnumValue("TriggerSource", (uint)TriggerSource.Software);
            if (ret != MvError.MV_OK)
                throw new CameraException($"设置软触发源失败: 0x{ret:X8}");
            _logger.Information("相机 {CameraId} 设置为软触发模式", CameraId);
        }


      /// <summary>
      /// 设置硬件触发模式
      /// </summary>
      /// <param name="source"></param>
      /// <exception cref="CameraException"></exception>
        public void SetHardwareTriggerMode(TriggerSource source)
        {
            if (!IsConnected) throw new CameraException("设备未连接");
            if (IsGrabbing) throw new CameraException("请先停止采集再修改模式");

            if (source == TriggerSource.Software)
                throw new ArgumentException("硬件触发不能选择Software源", nameof(source));
            // 1. 启用触发模式
            int ret = _device.Parameters.SetEnumValue("TriggerMode", 1u);
            if (ret != MvError.MV_OK)
                throw new CameraException($"启用触发模式失败: 0x{ret:X8}");

            // 2. 设置指定触发源
            ret = _device.Parameters.SetEnumValue("TriggerSource", (uint)source);
            if (ret != MvError.MV_OK)
                throw new CameraException($"设置触发源失败: 0x{ret:X8}");
            _logger.Information("相机 {CameraId} 设置为硬触发模式 (Source: {Source})", CameraId, source);
        }




        /// <summary>
        /// 断开相机连接
        /// </summary>
        public void Disconnect()
        {
           if (!IsConnected) return;

            try
            {
                StopGrabbing();
            }
            catch
            {
                // 忽略停止采集时的错误
            }

            // 注销回调
            if (_device.StreamGrabber != null)
            {
                _device.StreamGrabber.FrameGrabedEvent -= OnFrameGrabbed;
            }

            int ret = _device.Close();
            if (ret != MvError.MV_OK)
            {
                throw new CameraException   ($"关闭设备失败: 0x{ret:X8}");
            }
          
            _device.Dispose();
            _device = null;

            _logger.Information("相机 {CameraId} 已断开", CameraId);
            OnStatusChanged(CameraStatus.Disconnected);
           
        }

        /// <summary>
        /// 设置相机参数
        /// </summary>
        public void SetParameter(string key, object value)
        {
            if (!IsConnected) throw new CameraException("设备未连接");

            switch (value)
            {
                case int intValue:
                    _device.Parameters.SetIntValue(key, intValue);
                    break;
                case float floatValue:
                    _device.Parameters.SetFloatValue(key, floatValue);
                    break;
                case bool boolValue:
                    _device.Parameters.SetBoolValue(key, boolValue);
                    break;
                case string stringValue:
                    _device.Parameters.SetStringValue(key, stringValue);
                    break;
                case Enum enumValue:
                    _device.Parameters.SetEnumValue(key, Convert.ToUInt32(enumValue));
                    break;
                default:
                    throw new CameraException($"不支持的参数类型: {value.GetType().Name}");
            }
        }

        /// <summary>
        /// 获取相机参数
        /// </summary>
        public T GetParameter<T>(string key)
        {
            if (!IsConnected) throw new CameraException("设备未连接");

            if (typeof(T) == typeof(int))
            {
                MvCameraControl.IIntValue value;
                _device.Parameters.GetIntValue(key, out value);
                return (T)(object)value.CurValue;
            }
            else if (typeof(T) == typeof(float))
            {
                MvCameraControl.IFloatValue value;
                _device.Parameters.GetFloatValue(key, out value);
                return (T)(object)value.CurValue;
            }
            else if (typeof(T) == typeof(bool))
            {
                bool value;
                _device.Parameters.GetBoolValue(key, out value);
                return (T)(object)value;
            }
            else if (typeof(T) == typeof(string))
            {
                MvCameraControl.IStringValue value;
                _device.Parameters.GetStringValue(key, out value);
                return (T)(object)value.CurValue;
            }
            else if (typeof(T).IsEnum)
            {
                MvCameraControl.IEnumValue value;
                _device.Parameters.GetEnumValue(key, out value);
                return (T)Enum.ToObject(typeof(T), value);
            }

            throw new CameraException($"不支持的参数类型: {typeof(T).Name}");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
          
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    Disconnect();
                }
                _isDisposed = true;
            }
        }

        // 图像回调处理
        private void OnFrameGrabbed(object sender, FrameGrabbedEventArgs e)
        {
            try
            {
                FrameGrabbed?.Invoke(this, e);
            }
            finally
            {
                if (_isSingleFrameMode)
                {
                    StopGrabbing();  // 收到帧后自动停止
                    _isSingleFrameMode = false;
           
                }
            }
        }

        // 状态变更通知
        private void OnStatusChanged(CameraStatus newStatus)
        {
            Status = newStatus;
            StatusChanged?.Invoke(this, new CameraStatusEventArgs
            {
                CameraId = CameraId,
                Status = newStatus
            });
        }
    }
}
