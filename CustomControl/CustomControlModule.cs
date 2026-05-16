using CustomControl.Views;
using Prism.Ioc;
using Prism.Modularity;
using Serilog;

namespace CustomControl
{
    public class CustomControlModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var logger = containerProvider.Resolve<ILogger>();
            logger.Information("CustomControl 模块已初始化");
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterDialogWindow<CustomDialogWindow>();
            containerRegistry.RegisterDialog<LoadingUserControl>();
        }
    }
}
