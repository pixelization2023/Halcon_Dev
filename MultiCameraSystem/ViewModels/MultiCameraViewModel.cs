using System.Collections.ObjectModel;
using MultiCameraSystem.Services.Interfaces;
using MVS.Core;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    /// <summary>
    /// 多相机列表。
    ///
    /// MVVM 修正：
    /// <list type="bullet">
    /// <item>删掉从未使用的 <c>_ameraCount</c>（拼写错误的死字段）与一堆无用 using；</item>
    /// <item>命令缓存为只读字段，避免每次绑定都新建命令；</item>
    /// <item>实现 <see cref="INavigationAware"/>，进入页面时自动刷新列表。</item>
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

        private void ExecuteSelectCommand(CameraInfo? selectedCamera)
        {
            if (selectedCamera == null) return;

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
            _cameraService.RefreshCameras();
            Reload();
            _logger.Information("相机列表已刷新，当前可用相机数量: {Count}", Cameras.Count);
        }

        private void Reload()
        {
            Cameras.Clear();
            foreach (var camera in _cameraService.AvailableCameras)
                Cameras.Add(camera);
        }

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext) => Reload();

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        #endregion
    }
}
