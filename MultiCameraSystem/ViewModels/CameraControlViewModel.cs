using Halcon.Core;
using HalconDotNet;
using MultiCameraSystem.Models;
using MultiCameraSystem.Convert;
using MultiCameraSystem.Services;
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

        // 应用设置（用于按序列号持久化曝光/增益）
        private readonly SettingsService _settings;

        private readonly ILogger logger;

        // 当前相机对象（未选择时为 null —— 所有命令都必须能容忍）
        private CameraDevice? _camera;

        // 用于计算帧率的计时器
        private Stopwatch _frameStopwatch;
        private double _frameRate;
        private bool _isGrabbing;

        /// <summary>
        /// 构造函数，注入相机服务
        /// </summary>
        /// <param name="cameraService"></param>
        public CameraControlViewModel(ICameraService cameraService, SettingsService settings, ILogger logger)
        {
            _cameraService = cameraService;
            _settings = settings;
            _frameStopwatch = new Stopwatch();
            this.logger = logger;

            // HalconView 窗口就绪后回传句柄（XAML 里通过附加属性 WindowReadyCommand 触发）
            WindowReadyCommand = new DelegateCommand<HWindow>(h =>
            {
                _halconWindow = h;
                logger.Debug("相机控制：Halcon 窗口句柄已就绪");
            });
        }

        /// <summary>
        /// 统一刷新所有命令的可用状态。
        /// 这样"未选相机 / 未连接"时按钮自动置灰，而不是点下去抛异常。
        /// </summary>
        private void RaiseCanExecuteChanged()
        {
            _connectCommand?.RaiseCanExecuteChanged();
            _disconnectCommand?.RaiseCanExecuteChanged();
            _startGrabbingCommand?.RaiseCanExecuteChanged();
            _stopGrabbingCommand?.RaiseCanExecuteChanged();
            _setContinuousModeCommand?.RaiseCanExecuteChanged();
            _setSoftwareTriggerModeCommand?.RaiseCanExecuteChanged();
            _setHardwareTriggerModeCommand?.RaiseCanExecuteChanged();
            _executeSoftwareTriggerCommand?.RaiseCanExecuteChanged();
            _captureCommand?.RaiseCanExecuteChanged();
            _saveconfigCommand?.RaiseCanExecuteChanged();
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
        /// 当前相机属性。
        ///
        /// 关键修正：<c>_camera</c> 只在 OnNavigatedTo 且导航参数带 serialNumber 时才被赋值。
        /// 旧实现的所有命令都直接写 <c>Camera.xxx</c>（如 Camera.StartGrabbing()），
        /// 没选相机时点任何按钮都是 NullReferenceException。现在：
        /// 1) setter 里同步 <see cref="HasCamera"/> 并刷新命令可用性；
        /// 2) 每个命令都带 CanExecute 门控 + 方法内空判兜底。
        /// </summary>
        public CameraDevice? Camera
        {
            get => _camera;
            set
            {
                if (SetProperty(ref _camera, value))
                {
                    RaisePropertyChanged(nameof(HasCamera));
                    RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>是否已经选择了相机（决定所有相机命令能否执行）</summary>
        public bool HasCamera => _camera != null;

        /// <summary>句柄回传（HalconView 窗口就绪时把 HWindow 交给本 VM）</summary>
        public DelegateCommand<HWindow> WindowReadyCommand { get; }

        /// <summary>Halcon 窗口句柄（后续画框/量测用；未就绪为 null）</summary>
        private HWindow? _halconWindow;

        private bool _isConnected;
        /// <summary>
        /// 相机是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                    RaiseCanExecuteChanged();
            }
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
            _startGrabbingCommand ?? (_startGrabbingCommand = new DelegateCommand(ExecuteStartGrabbingCommand, () => _camera is { IsConnected: true }));

        /// <summary>
        /// 执行开始采集
        /// </summary>
        void ExecuteStartGrabbingCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

            try
            {
                _camera.StartGrabbing();
                IsGrabbing = true;
                Status = "开始采集图像...";
                _frameStopwatch.Restart();
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
            _stopGrabbingCommand ?? (_stopGrabbingCommand = new DelegateCommand(ExecuteStopGrabbingCommand, () => _camera != null && IsGrabbing));

        /// <summary>
        /// 执行停止采集
        /// </summary>
        void ExecuteStopGrabbingCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

            try
            {
                _camera.StopGrabbing();
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
            _setContinuousModeCommand ?? (_setContinuousModeCommand = new DelegateCommand(ExecuteSetContinuousModeCommand, () => _camera is { IsConnected: true }));

        /// <summary>
        /// 执行设置连续采集模式
        /// </summary>
        void ExecuteSetContinuousModeCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

            try
            {
                _camera.SetContinuousMode();
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
            _setSoftwareTriggerModeCommand ?? (_setSoftwareTriggerModeCommand = new DelegateCommand(ExecuteSetSoftwareTriggerModeCommand, () => _camera is { IsConnected: true }));

        /// <summary>
        /// 执行设置软件触发模式
        /// </summary>
        void ExecuteSetSoftwareTriggerModeCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

            try
            {
                _camera.SetSoftwareTriggerMode();
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
            _setHardwareTriggerModeCommand ?? (_setHardwareTriggerModeCommand = new DelegateCommand(ExecuteSetHardwareTriggerModeCommand, () => _camera is { IsConnected: true }));

        /// <summary>
        /// 执行设置硬件触发模式
        /// </summary>
        void ExecuteSetHardwareTriggerModeCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

            try
            {
                // 默认使用Line1作为硬件触发源
                _camera.SetHardwareTriggerMode(CameraDevice.TriggerSource.Line1);
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
            _connectCommand ?? (_connectCommand = new DelegateCommand(ExecuteConnectCommand, () => _camera is { IsConnected: false }));

        /// <summary>
        /// 执行连接相机
        /// </summary>
        void ExecuteConnectCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

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
            _disconnectCommand ?? (_disconnectCommand = new DelegateCommand(ExecuteDisconnectCommand, () => _camera != null));

        /// <summary>
        /// 执行断开相机
        /// </summary>
        void ExecuteDisconnectCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

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
            _executeSoftwareTriggerCommand ?? (_executeSoftwareTriggerCommand = new DelegateCommand(ExecuteSoftwareTrigger, () => _camera is { IsConnected: true }));

        void ExecuteSoftwareTrigger()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

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
            _captureCommand ?? (_captureCommand = new DelegateCommand(ExecuteCaptureCommand, () => _camera != null && IsGrabbing));

        /// <summary>
        /// 执行拍照保存
        /// </summary>
        void ExecuteCaptureCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

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
            _saveconfigCommand ?? (_saveconfigCommand = new DelegateCommand(ExecuteSaveconfigCommand, () => _camera is { IsConnected: true }));
        /// <summary>
        /// 执行保存参数配置命令
        /// </summary>
        void ExecuteSaveconfigCommand()
        {
            if (_camera == null) { Status = "请先选择相机"; return; }

            try
            {
                // 1) 写进相机寄存器（立即生效）
                _camera.SetParameter("ExposureTime", ExposureTime);
                _camera.SetParameter("Gain", Gain);

                // 2) 按序列号持久化到 appsettings.json（重启/换相机后能自动恢复）
                //    旧实现只做第 1 步，重启即回默认值，现场每次开机都要重调一次。
                PersistCameraParameters();

                Status = "参数配置已保存（含持久化）";
                logger.Information("相机参数已保存: S/N={Serial} 曝光={Exposure} 增益={Gain}",
                    _camera.Info.SerialNumber, ExposureTime, Gain);
            }
            catch (Exception ex)
            {
                Status = ex.Message;
                logger.Error(ex, "保存参数配置失败");
            }
        }

        /// <summary>把当前曝光/增益按序列号写入 appsettings.json</summary>
        private void PersistCameraParameters()
        {
            if (_camera == null) return;

            var serial = _camera.Info.SerialNumber;
            if (string.IsNullOrWhiteSpace(serial)) return;

            try
            {
                _settings.Update(s =>
                {
                    s.CameraDefaults.PerCamera[serial] = new CameraParameterSettings
                    {
                        ExposureTime = ExposureTime,
                        Gain = Gain,
                        SavedAt = DateTime.Now
                    };
                });
            }
            catch (Exception ex)
            {
                // 持久化失败不影响相机已生效的参数，但必须留痕
                logger.Warning(ex, "相机参数持久化失败（相机内参数已生效）: S/N={Serial}", serial);
            }
        }

        /// <summary>从持久化配置恢复本相机的曝光/增益；没有记录时用全局默认值</summary>
        private void RestoreSavedParameters()
        {
            if (_camera == null) return;

            var serial = _camera.Info.SerialNumber;
            var defaults = _settings.Current.CameraDefaults;

            if (!string.IsNullOrWhiteSpace(serial)
                && defaults.PerCamera.TryGetValue(serial, out var saved))
            {
                ExposureTime = saved.ExposureTime;
                Gain = saved.Gain;
                logger.Information("已恢复相机保存的参数: S/N={Serial} 曝光={Exposure} 增益={Gain}",
                    serial, ExposureTime, Gain);
            }
            else
            {
                ExposureTime = defaults.ExposureTime;
                Gain = defaults.GainValue;
            }
        }

        /// <summary>
        /// 导航到该视图时的处理
        /// </summary>
        /// <param name="navigationContext"></param>
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var serialNumber = navigationContext.Parameters.GetValue<string>("serialNumber");
            if (string.IsNullOrEmpty(serialNumber))
            {
                // 没有带序列号就进来（例如直接从侧栏点击「相机管理」再进来）：
                // 保持"未选择相机"状态，所有命令自动禁用，而不是后面各处空引用。
                Status = "未选择相机，请先在「多相机概览」里选择一台设备";
                return;
            }

            try
            {
                var camera = _cameraService.GetCamera(serialNumber);

                // 先退订旧相机：本页 IsNavigationTarget => false，但 DryIoc 仍可能复用实例，
                // 反复订阅会让一帧回调触发多次（帧率虚高、图像重复处理）。
                DetachCameraEvents();

                _camera = camera;
                RaisePropertyChanged(nameof(Camera));
                RaisePropertyChanged(nameof(HasCamera));

                _camera.StatusChanged += OnCameraStatusChanged;
                _camera.FrameGrabbed += OnFrameGrabbed;

                Status = $"已选择相机: {_camera.Info.ModelName}";
                IsConnected = _camera.IsConnected;

                // 恢复该相机上次保存的曝光/增益（未连接时先显示持久化值，连接后以设备值为准）
                if (!IsConnected)
                    RestoreSavedParameters();

                RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                Status = $"选择相机失败: {ex.Message}";
                logger.Error(ex, "选择相机失败: {Serial}", serialNumber);
            }
        }

        /// <summary>退订当前相机的事件（防止重复订阅）</summary>
        private void DetachCameraEvents()
        {
            if (_camera == null) return;

            try
            {
                _camera.StatusChanged -= OnCameraStatusChanged;
                _camera.FrameGrabbed -= OnFrameGrabbed;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "退订相机事件失败");
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
