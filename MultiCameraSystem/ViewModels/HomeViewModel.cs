using MultiCameraSystem.Services.Interfaces;
using MVS.Core;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    public class HomeViewModel : BindableBase, INavigationAware
    {
        private readonly IRegionManager _regionManager;
        private readonly ICameraService _cameraService;
        private readonly ILogger _logger;

        public HomeViewModel(IRegionManager regionManager, ICameraService cameraService, ILogger logger)
        {
            _regionManager = regionManager;
            _cameraService = cameraService;
            _logger = logger.ForContext<HomeViewModel>();
        }

        private int _cameraCount;
        public int CameraCount
        {
            get => _cameraCount;
            set => SetProperty(ref _cameraCount, value);
        }

        private int _connectedCount;
        public int ConnectedCount
        {
            get => _connectedCount;
            set => SetProperty(ref _connectedCount, value);
        }

        private string _systemStatus = "正常运行";
        public string SystemStatus
        {
            get => _systemStatus;
            set => SetProperty(ref _systemStatus, value);
        }

        private bool _isError;
        /// <summary>
        /// 是否处于异常状态。
        /// 原来这里返回硬编码的 "#00E676"/"#FF5252"，颜色属于主题职责，
        /// 现在只暴露状态，具体颜色由 XAML 绑定主题画刷（切换主题即时生效）。
        /// </summary>
        public bool IsError
        {
            get => _isError;
            set => SetProperty(ref _isError, value);
        }

        private string _imagePath = "D:\\Captures\\";
        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        private string _systemTime = "";
        public string SystemTime
        {
            get => _systemTime;
            set => SetProperty(ref _systemTime, value);
        }

        private string _plcStatus = "未连接";
        public string PlcStatus
        {
            get => _plcStatus;
            set => SetProperty(ref _plcStatus, value);
        }

        private string _mesStatus = "未连接";
        public string MesStatus
        {
            get => _mesStatus;
            set => SetProperty(ref _mesStatus, value);
        }

        private void RefreshStatus()
        {
            try
            {
                var cameras = _cameraService.AvailableCameras?.ToList() ?? new();
                CameraCount = cameras.Count;
                ConnectedCount = cameras.Count(c => c.Status);
                SystemStatus = "正常运行";
                IsError = false;
                SystemTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _logger.Debug("首页状态已刷新: 相机 {Count} 台", CameraCount);
            }
            catch (Exception ex)
            {
                SystemStatus = $"异常: {ex.Message}";
                IsError = true;
                _logger.Error(ex, "刷新首页状态失败");
            }
        }

        #region 导航命令

        private DelegateCommand? _navigateToCameraManagerCommand;
        public DelegateCommand NavigateToCameraManagerCommand =>
            _navigateToCameraManagerCommand ??= new DelegateCommand(() =>
            {
                _logger.Information("从首页导航到相机管理");
                _regionManager.RequestNavigate("ContentRegion", "CameraManagerView");
            });

        private DelegateCommand? _navigateTestViewCommand;
        public DelegateCommand NavigateTestViewCommand =>
            _navigateTestViewCommand ??= new DelegateCommand(() =>
            {
                _logger.Information("从首页导航到流程测试");
                _regionManager.RequestNavigate("ContentRegion", "TestView");
            });

        private DelegateCommand? _navigateRunViewCommand;
        public DelegateCommand NavigateRunViewCommand =>
            _navigateRunViewCommand ??= new DelegateCommand(() =>
            {
                _logger.Information("从首页导航到运行界面");
                _regionManager.RequestNavigate("ContentRegion", "RunView");
            });

        private DelegateCommand? _refreshCommand;
        public DelegateCommand RefreshCommand =>
            _refreshCommand ??= new DelegateCommand(() =>
            {
                _cameraService.RefreshCameras();
                RefreshStatus();
            });

        #endregion

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            RefreshStatus();
            _logger.Information("进入首页");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
