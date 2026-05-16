using MultiCameraSystem.Views;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    public class CameraManagerViewModel : BindableBase, INavigationAware
    {
        private readonly IRegionManager _regionManager;
        private readonly ILogger _logger;

        public CameraManagerViewModel(IRegionManager regionManager, ILogger logger)
        {
            _regionManager = regionManager;
            _logger = logger.ForContext<CameraManagerViewModel>();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _logger.Debug("离开相机管理界面");
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入相机管理界面");
        }

        private DelegateCommand? _loadedCommand;
        public DelegateCommand LoadedCommand =>
            _loadedCommand ??= new DelegateCommand(ExecuteLoadedCommand);

        void ExecuteLoadedCommand()
        {
            _logger.Debug("相机管理界面加载完成，导航到多相机列表");
            _regionManager.RequestNavigate("CameraListRegion", nameof(MultiCameraView));
        }
    }
}
