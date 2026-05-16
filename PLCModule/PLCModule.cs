using PLCModule.Interfaces;
using PLCModule.Services;
using PLCModule.Views;
using PLCModule.ViewModels;
using Prism.Ioc;
using Prism.Modularity;
using Serilog;

namespace PLCModule
{
    public class PLCModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var logger = containerProvider.Resolve<ILogger>();

            // 商业组件授权必须在创建任何通信对象之前注册。
            // 这里先用「环境变量 / 内置默认值」兜底，外壳加载配置后会用 appsettings.json 里的值再确认一次。
            HslLicense.Initialize(null, logger);

            // 输出首次真实注册结果（注册发生在日志系统就绪之前，所以要在这里补一行）
            logger.Information("PLC 模块已初始化（HslCommunication {Detail}）",
                HslLicense.RegistrationDetail ?? (HslLicense.IsRegistered ? "授权：已注册" : "授权：试用模式"));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<PLCMonitorView>();
            containerRegistry.RegisterSingleton<IPLCCommunicator, PLCCommunicator>();
            containerRegistry.RegisterSingleton<IPLCDataHandler, PLCSignalManager>();
        }
    }
}
