using Halcon.Core;
using HalconDotNet;
using MultiCameraSystem.Convert;
using MultiCameraSystem.Services.Interfaces;
using MvCameraControl;
using MVS.Core;
using Serilog;
using System.Diagnostics;
using System.Drawing;
using System.Reflection.PortableExecutable;
using System.Windows.Media.Imaging;

namespace MultiCameraSystem.ViewModels
{
    /// <summary>
    /// 相机控制视图模型，负责相机的连接、采集、模式切换等操作
    /// </summary>
    public class CameraControlViewModel : BindableBase, INavigationAware
    {
        // 相机服务接口
        private readonly ICameraService _cameraService;

        private readonly ILogger logger;

        // 当前相机对象
        private CameraDevice _camera;

        // 用于计算帧率的计时器
        private Stopwatch _frameStopwatch;
        private int _frameCount;
        private double _frameRate;
        private bool _isGrabbing;
        private bool _isDisposed;

        /// <summary>
        /// 构造函数，注入相机服务
        /// </summary>
        /// <param name="cameraService"></param>
        public CameraControlViewModel(ICameraService cameraService, ILogger logger)
        {
            _cameraService = cameraService;
            _frameStopwatch = new Stopwatch();
            this.logger = logger;
        }

        private float _exposureTime;
        public float ExposureTime
        {
            get { return _exposureTime; }
            set { SetProperty(ref _exposureTime, value); }
        }

        private float _gain;
        public float Gain
        {
            get { return _gain; }
            set { SetProperty(ref _gain, value); }
        }


        /// <summary>
        /// 当前相机属性
        /// </summary>
        public CameraDevice Camera
        {
            get => _camera;
            set => SetProperty(ref _camera, value);
        }

        private bool _isConnected;
        /// <summary>
        /// 相机是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }



        private string _status = "准备就绪";
        /// <summary>
        /// 状态描述
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 是否正在采集图像
        /// </summary>
        public bool IsGrabbing
        {
            get => _isGrabbing;
            set
            {
                SetProperty(ref _isGrabbing, value);
            }
        }

        private bool _hasImage;
        public bool HasImage
        {
            get => _hasImage;
            set => SetProperty(ref _hasImage, value);
        }



        /// <summary>
        /// 当前显示控件
        /// </summary>

        private HObject _hobjeimage;
        public HObject Hobjeimage
        {
            get { return _hobjeimage; }
            set
            {
                SetProperty(ref _hobjeimage, value);
                // 图像赋值成功后更新 HasImage
                HasImage = value != null && value.IsInitialized();
            }
        }
        





        /// <summary>
        /// 当前帧率
        /// </summary>
        public double FrameRate
        {
            get => _frameRate;
            set => SetProperty(ref _frameRate, value);
        }

        private DelegateCommand _startGrabbingCommand;
        /// <summary>
        /// 开始采集命令
        /// </summary>
        public DelegateCommand StartGrabbingCommand =>
            _startGrabbingCommand ?? (_startGrabbingCommand = new DelegateCommand(ExecuteStartGrabbingCommand));

        /// <summary>
        /// 执行开始采集
        /// </summary>
        void ExecuteStartGrabbingCommand()
        {
            try
            {
                Camera.StartGrabbing();
                IsGrabbing = true;
                Status = "开始采集图像...";
                _frameStopwatch.Restart();
                _frameCount = 0;
                logger.Information($"相机{Camera.Info.ModelName}开始采集");
            }
            catch (CameraException ex)
            {
                Status = $"开始采集失败: {ex.Message}";
            }
        }

        private DelegateCommand _stopGrabbingCommand;
        /// <summary>
        /// 停止采集命令
        /// </summary>
        public DelegateCommand StopGrabbingCommand =>
            _stopGrabbingCommand ?? (_stopGrabbingCommand = new DelegateCommand(ExecuteStopGrabbingCommand));

        /// <summary>
        /// 执行停止采集
        /// </summary>
        void ExecuteStopGrabbingCommand()
        {
            try
            {
                Camera.StopGrabbing();
                IsGrabbing = false;
                Status = "已停止采集";
                logger.Information($"相机{Camera.Info.ModelName}停止采集");
                _frameStopwatch.Stop();
            }
            catch (CameraException ex)
            {
                Status = $"停止采集失败: {ex.Message}";
            }
        }

        private DelegateCommand _setContinuousModeCommand;
        /// <summary>
        /// 设置连续采集模式命令
        /// </summary>
        public DelegateCommand SetContinuousModeCommand =>
            _setContinuousModeCommand ?? (_setContinuousModeCommand = new DelegateCommand(ExecuteSetContinuousModeCommand));

        /// <summary>
        /// 执行设置连续采集模式
        /// </summary>
        void ExecuteSetContinuousModeCommand()
        {
            try
            {
                Camera.SetContinuousMode();
                Status = "已设置为连续采集模式";
                logger.Information($"相机{Camera.Info.ModelName}已设置为连续采集模式");
            }
            catch (CameraException ex)
            {
                Status = $"设置连续模式失败: {ex.Message}";
            }
        }

        private DelegateCommand _setSoftwareTriggerModeCommand;
        /// <summary>
        /// 设置软件触发模式命令
        /// </summary>
        public DelegateCommand SetSoftwareTriggerModeCommand =>
            _setSoftwareTriggerModeCommand ?? (_setSoftwareTriggerModeCommand = new DelegateCommand(ExecuteSetSoftwareTriggerModeCommand));

        /// <summary>
        /// 执行设置软件触发模式
        /// </summary>
        void ExecuteSetSoftwareTriggerModeCommand()
        {
            try
            {
                Camera.SetSoftwareTriggerMode();
                Status = "已设置为软件触发模式";
                logger.Information($"相机{Camera.Info.ModelName}已设置为软件触发模式");
            }
            catch (CameraException ex)
            {
                Status = $"设置软触发模式失败: {ex.Message}";
            }
        }

        private DelegateCommand _setHardwareTriggerModeCommand;
        /// <summary>
        /// 设置硬件触发模式命令
        /// </summary>
        public DelegateCommand SetHardwareTriggerModeCommand =>
            _setHardwareTriggerModeCommand ?? (_setHardwareTriggerModeCommand = new DelegateCommand(ExecuteSetHardwareTriggerModeCommand));

        /// <summary>
        /// 执行设置硬件触发模式
        /// </summary>
        void ExecuteSetHardwareTriggerModeCommand()
        {
            try
            {
                // 默认使用Line1作为硬件触发源
                Camera.SetHardwareTriggerMode(CameraDevice.TriggerSource.Line1);
                Status = "已设置为硬件触发模式 (Line1)";
                logger.Information($"相机{Camera.Info.ModelName}已设置为硬件触发模式 (Line1)");
            }
            catch (CameraException ex)
            {
                Status = $"设置硬触发模式失败: {ex.Message}";
            }
        }

        private DelegateCommand _connectCommand;
        /// <summary>
        /// 连接相机命令
        /// </summary>
        public DelegateCommand ConnectCommand =>
            _connectCommand ?? (_connectCommand = new DelegateCommand(ExecuteConnectCommand));

        /// <summary>
        /// 执行连接相机
        /// </summary>
        void ExecuteConnectCommand()
        {
            try
            {
                _camera.Connect();
                IsConnected = true;
                logger.Information($"相机{Camera.Info.ModelName}已经连接");
                ExposureTime = _camera.GetParameter<float>("ExposureTime"); // 获取曝光时间参数，确保相机已连接
                Gain = _camera.GetParameter<float>("Gain"); // 获取增益参数，确保相机已连接

            }
            catch (Exception ex)
            {
                Status = $"连接失败: {ex.Message}";
            }
        }

        private DelegateCommand _disconnectCommand;
        /// <summary>
        /// 断开相机命令
        /// </summary>
        public DelegateCommand DisconnectCommand =>
            _disconnectCommand ?? (_disconnectCommand = new DelegateCommand(ExecuteDisconnectCommand));

        /// <summary>
        /// 执行断开相机
        /// </summary>
        void ExecuteDisconnectCommand()
        {
            try
            {
                if (IsGrabbing)
                {
                    Camera.StopGrabbing(); // 先停止采集
                    IsGrabbing = false;
                }

                _camera.Disconnect();
                IsConnected = false; // 必须更新状态
                Status = "已断开连接";
                logger.Information($"相机{Camera.Info.ModelName}断开连接");

                Hobjeimage?.Dispose();
             
                FrameRate = 0;

            }
            catch (CameraException ex)
            {
                Status = $"断开连接失败: {ex.Message}";
                logger.Error(ex, "断开连接失败");
            }
        }

        private DelegateCommand _executeSoftwareTriggerCommand;
        /// <summary>
        /// 执行软触发命令
        /// </summary>
        public DelegateCommand ExecuteSoftwareTriggerCommand =>
            _executeSoftwareTriggerCommand ?? (_executeSoftwareTriggerCommand = new DelegateCommand(ExecuteSoftwareTrigger));

        void ExecuteSoftwareTrigger()
        {
            try
            {
                _camera.ExecuteSoftwareTrigger();
                Status = "软触发已执行";
                logger.Information($"相机{Camera.Info.ModelName}执行软触发");
            }
            catch (CameraException ex)
            {
                Status = $"软触发失败: {ex.Message}";
                logger.Error(ex, "软触发失败");
            }
        }

        private DelegateCommand _captureCommand;
        /// <summary>
        /// 采集命令（拍照保存）
        /// </summary>
        public DelegateCommand CaptureCommand =>
            _captureCommand ?? (_captureCommand = new DelegateCommand(ExecuteCaptureCommand));

        /// <summary>
        /// 执行拍照保存
        /// </summary>
        void ExecuteCaptureCommand()
        {
            try
            {
                if (!IsGrabbing)
                {
                    Status = "请先开始采集";
                    return;
                }
                Status = "拍照保存中...";
                logger.Information($"相机{Camera.Info.ModelName}拍照保存");
            }
            catch (CameraException ex)
            {
                Status = $"拍照失败: {ex.Message}";
                logger.Error(ex, "拍照失败");
            }
        }


        /// <summary>
        /// 保存参数配置命令
        /// </summary>
        private DelegateCommand _saveconfigCommand;
        public DelegateCommand SaveconfigCommand =>
            _saveconfigCommand ?? (_saveconfigCommand = new DelegateCommand(ExecuteSaveconfigCommand));
        /// <summary>
        /// 执行保存参数配置命令
        /// </summary>
        void ExecuteSaveconfigCommand()
        {
            try
            {
                //保存曝光
                _camera.SetParameter("ExposureTime", ExposureTime);
                //保存增益
                _camera.SetParameter("Gain", Gain);

                Status = "参数配置已保存";
                logger.Information("参数置已保存");
            }
            catch (Exception ex)
            {

                Status = ex.Message;
                logger.Error(ex, "保存参数配置失败");

            }

        }

        /// <summary>
        /// 导航到该视图时的处理
        /// </summary>
        /// <param name="navigationContext"></param>
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var serialNumber = navigationContext.Parameters.GetValue<string>("serialNumber");
            if (!string.IsNullOrEmpty(serialNumber))
            {
                _camera = _cameraService.GetCamera(serialNumber);
                _camera.StatusChanged += OnCameraStatusChanged;
                _camera.FrameGrabbed += OnFrameGrabbed;
                Status = $"已选择相机: {_camera.Info.ModelName}";
            }
        }

        /// <summary>
        /// 图像采集回调
        /// </summary>
        private void OnFrameGrabbed(object? sender, FrameGrabbedEventArgs e)
        {
            if (e.FrameOut.Image != null)
            {



                Hobjeimage = HalconImageConvert.BitmapToHObject(e.FrameOut.Image.ToBitmap());
                //HOperatorSet.WriteImage(Hobjeimage, "bmp", 0, DateTime.Now.ToString("yyyy-MM-dd-H-mm-ss-sf") + ".bmp");

                Status = $"已采集图像: {e.FrameOut.Image.Width}x{e.FrameOut.Image.Height}";
                FrameRate = e.FrameOut.FrameNum / _frameStopwatch.Elapsed.TotalSeconds;
            }
            else
            {
                Status = "采集图像失败，图像为空。";
            }


        }

        /// <summary>
        /// 相机状态变更回调
        /// </summary>
        private void OnCameraStatusChanged(object? sender, CameraStatusEventArgs e)
        {
            Status = $"{_camera.Info.ModelName}: {e.Status}";
        }

        /// <summary>
        /// 是否控制实例重用
        /// </summary>
        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return false;
        }

        /// <summary>
        /// 导航离开时的处理
        /// </summary>
        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            
            //离开导航的时候断开相机连接
            ExecuteDisconnectCommand();
        }
    }
}
