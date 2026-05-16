using MESModule.Interfaces;
using MESModule.Services;
using MESModule.Views;
using MESModule.ViewModels;
using Prism.Ioc;
using Prism.Modularity;
using Serilog;

namespace MESModule
{
    public class MESModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var logger = containerProvider.Resolve<ILogger>();
            logger.Information("MES 模块已初始化");
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<MESStatusView>();
            containerRegistry.RegisterSingleton<IMESConnector, MESConnector>();
            containerRegistry.RegisterSingleton<IProductDataService, ProductTraceService>();
            containerRegistry.RegisterSingleton<InspectionReportService>();
        }
    }
}
