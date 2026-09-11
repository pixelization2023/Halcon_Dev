using System.Collections.ObjectModel;
using MultiCameraSystem.Services.Interfaces;
using MVS.Core;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    /// <summary>
    /// 多相机概览（设备列表）。
    ///
    /// MVVM 修正记录：
    /// <list type="bullet">
    /// <item>删掉从未使用的 <c>_ameraCount</c>（拼写错误的死字段）与一堆无用 using；</item>
    /// <item>命令缓存为只读字段，避免每次绑定都新建命令；</item>
    /// <item>实现 <see cref="INavigationAware"/>，进入页面时自动刷新列表；</item>
    /// <item>新增 <see cref="SdkStatusText"/> / <see cref="IsSdkReady"/>：把"SDK 到底有没有起来"
    /// 直接显示在界面上。以前 Initialize() 失败被吞掉，刷新按钮点了没反应，现场无从判断。</item>
    /// </list>
    /// </summary>
    public class MultiCameraViewModel : BindableBase, INavigationAware
    {
        private readonly ICameraService _cameraService;
        private readonly IRegionManager _regionManager;
        private readonly ILogger _logger;

        public MultiCameraViewModel(ICameraService cameraService, IRegionManager regionManager, ILogger logger)
        {
            _cameraService = cameraService;
            _regionManager = regionManager;
            _logger = logger.ForContext<MultiCameraViewModel>();

            SelectCameraCommand = new DelegateCommand<CameraInfo>(ExecuteSelectCommand);
            RefreshCommand = new DelegateCommand(ExecuteRefreshCommand);

            Reload();
        }

        public ObservableCollection<CameraInfo> Cameras { get; } = new();

        public DelegateCommand<CameraInfo> SelectCameraCommand { get; }
        public DelegateCommand RefreshCommand { get; }

        /// <summary>相机 SDK 是否已成功初始化</summary>
        public bool IsSdkReady => _cameraService.IsInitialized;

        /// <summary>
        /// SDK 状态文本。直接把失败原因带出来，
        /// 例如 "相机 SDK 初始化失败：CameraException: SDK初始化失败: 0x80000006"。
        /// </summary>
        public string SdkStatusText => _cameraService.IsInitialized
            ? $"相机 SDK 已就绪 · 发现 {Cameras.Count} 台设备"
            : $"相机 SDK 不可用：{_cameraService.InitializationError ?? "尚未初始化"}";

        private void ExecuteSelectCommand(CameraInfo? selectedCamera)
        {
            if (selectedCamera == null) return;

            if (!_cameraService.IsInitialized)
            {
                _logger.Warning("相机 SDK 未就绪，无法选择设备: {Error}", _cameraService.InitializationError);
                Reload();
                return;
            }

            var parameters = new NavigationParameters
            {
                { "serialNumber", selectedCamera.SerialNumber }
            };

            _regionManager.RequestNavigate("CameraMainRegion", "CameraControlView", parameters);
            _logger.Information("已选择相机: {CameraName} (序列号: {SerialNumber})",
                selectedCamera.UserDefinedName, selectedCamera.SerialNumber);
        }

        private void ExecuteRefreshCommand()
        {
            // 用不抛异常的版本：失败时保留上次列表并把原因显示到 SDK 状态文本上，
            // 而不是抛 InvalidOperationException 被 UI 静默吞掉（旧行为的实际表现是"点了没反应"）。
            var ok = _cameraService.TryRefreshCameras();
            Reload();

            if (!ok)
                _logger.Warning("刷新相机列表失败：{Error}", _cameraService.InitializationError);
            else
                _logger.Information("相机列表已刷新，当前可用相机数量: {Count}", Cameras.Count);
        }

        private void Reload()
        {
            Cameras.Clear();
            foreach (var camera in _cameraService.AvailableCameras)
                Cameras.Add(camera);

            RaisePropertyChanged(nameof(IsSdkReady));
            RaisePropertyChanged(nameof(SdkStatusText));
        }

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext) => Reload();

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        #endregion
    }
}
