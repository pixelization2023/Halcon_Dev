using DryIoc;
using Halcon.Core;
using Microsoft.Extensions.DependencyInjection;
using MultiCameraSystem.Services;
using MultiCameraSystem.Services.Interfaces;
using MultiCameraSystem.Views;
using MVS.Core;
using Prism.Ioc;
using Serilog;
using Serilog.Core;
using System.Configuration;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MultiCameraSystem
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        // 日志广播器 — 先创建实例，再注入 Serilog Sink
        private readonly LogBroadcaster _logBroadcaster = new LogBroadcaster(capacity: 5000);

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 注入 LogBroadcaster（单例，供 LogViewer 订阅）
            containerRegistry.RegisterInstance<LogBroadcaster>(_logBroadcaster);

            // 注入服务
            containerRegistry.RegisterSingleton<ICameraManager, CameraManager>();
            containerRegistry.RegisterSingleton<ICameraService, CameraService>();
            containerRegistry.RegisterSingleton<SettingsService>();

            // 动态多主题服务（参考 MaterialDesignInXamlToolkit 的 PaletteHelper 用法）
            containerRegistry.RegisterSingleton<ThemeService>();

            // 单张图像的 Halcon 过程执行服务（供「运行界面」使用，算法逻辑不在 ViewModel 里）
            containerRegistry.RegisterSingleton<HalconRunService>();

            // 检测宿主：把相机采集链路接到迁移过来的 Inspection 检测模块
            containerRegistry.RegisterSingleton<InspectionHostService>();

            // 注入 Serilog ILogger — 同时写文件 + 广播到UI
            containerRegistry.RegisterSingleton<ILogger>(_ =>
                new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        path: @"Logs/log.txt",
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 50 * 1024 * 1024,
                        flushToDiskInterval: TimeSpan.FromMilliseconds(5))
                    .WriteTo.Sink(_logBroadcaster)   // ← LogBroadcaster 实现了 ILogEventSink
                    .CreateLogger());

            // 注入视图导航
            // 主窗口 ViewModel 注册为单例：否则 DryIoc 会为「外壳绑定」和「外部 Resolve」各建一个实例，
            // 表现就是「程序里改了选中态、界面上没变」（实测踩过的坑）。
            containerRegistry.RegisterSingleton<MultiCameraSystem.ViewModels.MainWindowViewModel>();

            containerRegistry.RegisterForNavigation<MultiCameraView>();
            containerRegistry.RegisterForNavigation<CameraControlView>();
            containerRegistry.RegisterForNavigation<CameraManagerView>();
            containerRegistry.RegisterForNavigation<Home>();
            containerRegistry.RegisterForNavigation<TestView>();
            containerRegistry.RegisterForNavigation<RunView>();
            containerRegistry.RegisterForNavigation<LogViewerView>();
            containerRegistry.RegisterForNavigation<SettingsView>();
            containerRegistry.RegisterForNavigation<ThemeSettingsView>();
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            base.ConfigureModuleCatalog(moduleCatalog);
            moduleCatalog.AddModule<CustomControl.CustomControlModule>();
            moduleCatalog.AddModule<PLCModule.PLCModule>();
            moduleCatalog.AddModule<MESModule.MESModule>();
            moduleCatalog.AddModule<WorkBench.WorkBenchModule>();
            // 窗体（WinForms）项目迁移过来的业务模块
            moduleCatalog.AddModule<Inspection.InspectionModule>();
        }


        protected override void OnStartup(StartupEventArgs e)
        {
            // 注册全局异常处理
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // 商业组件（HslCommunication）授权：必须在任何通信对象创建之前注册，
            // 所以放在容器初始化之前、用环境变量/内置默认值先兜一次。
            PLCModule.HslLicense.Initialize();

            base.OnStartup(e);
            var logger = Container.Resolve<ILogger>();
            logger.Information("程序启动");
        }

        protected override void Initialize()
        {
            base.Initialize();

            // 配置与主题必须在 Shell/各视图创建之前就绪，
            // 否则视图里的 StaticResource 会先取到 App.xaml 里的兜底色。
            var settings = Container.Resolve<SettingsService>();
            settings.Load();

            // 配置就绪后再确认一次授权码（appsettings.json 的 Plc.HslAuthorizationCode 可覆盖内置值）
            PLCModule.HslLicense.Initialize(settings.Current.Plc.HslAuthorizationCode, Container.Resolve<ILogger>());

            var themeService = Container.Resolve<ThemeService>();
            themeService.Initialize(settings.Current.UI);

            // 把解析后的主题写回配置，顺带兼容旧配置里 Theme="Dark"/"Light" 的写法
            settings.Update(s =>
            {
                s.UI.Theme = themeService.Current.Key;
                s.UI.BaseTheme = themeService.IsDark ? "Dark" : "Light";
            });
        }

        /// <summary>
        /// 区域导航 + 结果校验。
        ///
        /// 说明：Prism 的区域导航会把视图创建过程中抛出的异常内部消化掉，
        /// 结果是「页面静默不显示、也不写 crash_log」，非常难排查。
        /// 这里在导航后检查区域是否真的有视图加载成功，失败就明确落日志。
        /// </summary>
        private void Navigate(IRegionManager regionManager, string regionName, string viewName)
        {
            var logger = Container.Resolve<ILogger>();

            try
            {
                regionManager.RequestNavigate(regionName, viewName);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "导航异常: {Region} -> {View}", regionName, viewName);
                return;
            }

            if (regionManager.Regions.ContainsRegionWithName(regionName) &&
                !regionManager.Regions[regionName].Views.Any())
            {
                logger.Error("导航后区域 [{Region}] 没有任何视图，{View} 加载失败（检查 XAML 绑定/资源）",
                    regionName, viewName);
            }
        }

        #region 异常处理

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // 立即处理崩溃，不等待异步完成
            HandleCrash(e.ExceptionObject as Exception);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            HandleCrash(e.Exception);
            e.Handled = true;  // 标记为已处理
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleCrash(e.Exception);
            e.SetObserved(); // 标记为已观察
        }

        private void HandleCrash(Exception ex)
        {
            try
            {
                var crashInfo = $"[崩溃时间] {DateTime.Now}\n" +
                               $"[异常类型] {ex?.GetType().Name}\n" +
                               $"[异常信息] {ex?.Message}\n" +
                               $"[堆栈跟踪] {ex?.StackTrace}\n\n";

                File.AppendAllText("crash_log.txt", crashInfo, Encoding.UTF8);
                // 启动异步资源释放，但不等待完成

            }
            finally
            {
                try
                {
                    // 先执行资源清理
                    var cameraService = Container.Resolve<ICameraService>();
                    cameraService.Shutdown();
                }
                catch (Exception cleanupEx)
                {
                    File.AppendAllText("crash_log.txt",
                        $"[清理失败] {cleanupEx.Message}\n", Encoding.UTF8);
                }
                finally
                {
                    Application.Current?.Dispatcher.InvokeShutdown();
                    Environment.Exit(1);
                }
            }
        }
        #endregion

        protected override void OnInitialized()
        {
            base.OnInitialized();

            // 设置全局容器定位器
            AppContainer.Resolver = t => Container.GetContainer().Resolve(t, IfUnresolved.ReturnDefault);

            // 相机初始化服务
            var cameraService = Container.Resolve<ICameraService>();
            cameraService.Initialize();

            // 检测宿主服务：订阅 PLC 触发、相机取流，并提交图像给检测编排器
            Container.Resolve<InspectionHostService>();

            // 导航到检测主监控台（迁移后的主界面）
            // 走 MainWindowViewModel 的导航命令：顶部/侧栏选中态与标题栏会一并同步；
            // 直接 RequestNavigate 会漏掉选中态更新（实测踩过的坑）。
            IRegionManager regionManager = Container.Resolve<IRegionManager>();
            Container.Resolve<MultiCameraSystem.ViewModels.MainWindowViewModel>()
                     .NavigateByNameCommand.Execute("DashboardView");


            // 导航日志视图到日志面板区域
            Navigate(regionManager, "LogRegion", nameof(LogViewerView));




        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 清理资源
            var cameraService = Container.Resolve<ICameraService>();
            cameraService.Shutdown();
            var logger = Container.Resolve<ILogger>();
            logger.Information("程序退出");
            _logBroadcaster.Dispose();
            base.OnExit(e);
        }
    }
}
