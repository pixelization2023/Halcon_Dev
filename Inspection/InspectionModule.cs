using Inspection.Services;
using Inspection.Views;
using Prism.Ioc;
using Prism.Modularity;
using Serilog;

namespace Inspection
{
    /// <summary>
    /// 检测模块。
    /// 承载从 WinForms 「窗体」项目迁移过来的全部业务逻辑：
    /// 产品方案、检测配方、Halcon 流程执行、存图、PLC 点位与握手、MES 上传、复判 Socket、权限与结果库。
    /// </summary>
    public class InspectionModule : IModule
    {
        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // ---- 基础服务 ----
            containerRegistry.RegisterSingleton<UserSessionService>();
            containerRegistry.RegisterSingleton<ProductRepository>();
            containerRegistry.RegisterSingleton<ImageArchiveService>();
            containerRegistry.RegisterSingleton<HalconInspectionService>();
            containerRegistry.RegisterSingleton<MesApiClient>();
            containerRegistry.RegisterSingleton<TraceSocketService>();
            containerRegistry.RegisterSingleton<InspectionResultStore>();
            containerRegistry.RegisterSingleton<PlcIoService>();
            containerRegistry.RegisterSingleton<UploadOrchestrator>();
            containerRegistry.RegisterSingleton<StatusMonitorService>();
            containerRegistry.RegisterSingleton<InspectionOrchestrator>();
            containerRegistry.RegisterSingleton<SelfTestService>();

            // ---- 视图导航 ----
            containerRegistry.RegisterForNavigation<DashboardView>();
            containerRegistry.RegisterForNavigation<ProductView>();
            containerRegistry.RegisterForNavigation<InspectionConfigView>();
            containerRegistry.RegisterForNavigation<SystemSettingsView>();
            containerRegistry.RegisterForNavigation<LoginView>();
            containerRegistry.RegisterForNavigation<SelfTestView>();
        }

        public void OnInitialized(IContainerProvider containerProvider)
        {
            var logger = containerProvider.Resolve<ILogger>();
            var status = containerProvider.Resolve<StatusMonitorService>();
            var archive = containerProvider.Resolve<ImageArchiveService>();

            // 存图线程与状态轮询在模块初始化阶段就开始工作，
            // 与原 WinForms 中 timer1_Tick / 存图线程常驻的行为一致。
            archive.Start();
            status.Start();

            logger.Information("Inspection 检测模块已初始化（窗体项目逻辑迁移完成）");
        }
    }
}
