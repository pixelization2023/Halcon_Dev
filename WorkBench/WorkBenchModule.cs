using Prism.Ioc;
using Prism.Modularity;
using Serilog;
using WorkBench.Core;
using WorkBench.Interfaces;
using WorkBench.Views;

namespace WorkBench
{
    public class WorkBenchModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var logger = containerProvider.Resolve<ILogger>();
            logger.Information("WorkBench 模块已初始化");
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<WorkBenchView>();
            containerRegistry.RegisterForNavigation<ResultDashboardView>();
            containerRegistry.RegisterSingleton<IWorkflowEngine, WorkflowEngine>();
        }
    }
}
