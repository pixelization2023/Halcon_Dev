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

        /// <summary>
        /// 创建 Shell。
        ///
        /// 这里返回的其实是**启动界面**：真正的服务初始化必须等 Prism 把所有模块加载完
        ///（Inspection 模块的服务是条件注册的，CreateShell 阶段还解析不到），
        /// 所以主窗口在 OnInitialized() 里等启动编排结束后再显示。
        /// 这样做的好处是：启动界面一出现就开始显示真实进度，而不是"先黑一下、再出主界面"。
        /// </summary>
        protected override Window CreateShell()
        {
            _splash = new SplashWindow();
            return _splash;
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 注入 LogBroadcaster（单例，供 LogViewer 订阅）
            containerRegistry.RegisterInstance<LogBroadcaster>(_logBroadcaster);

            // 注入服务
            containerRegistry.RegisterSingleton<ICameraManager, CameraManager>();
            containerRegistry.RegisterSingleton<ICameraService, CameraService>();

            // 相机状态的只读视图：Inspection 的"自检"要靠它检查相机（它不能反向引用本工程）
            containerRegistry.RegisterSingleton<MVS.Core.ICameraInventory, CameraInventoryAdapter>();

            // 剪贴板：自检报告的"复制"按钮用（VM 不直连 System.Windows.Clipboard）
            containerRegistry.RegisterSingleton<MVS.Core.IClipboardService, WpfClipboardService>();

            // Shell 的导航能力：模块（自检页的「去处理」）通过它请求导航，
            // 以保证与侧栏选中态同步。注册成单例后 ViewModelLocator 拿到的也是同一个实例。
            containerRegistry.RegisterSingleton<MultiCameraSystem.ViewModels.MainWindowViewModel>();
            containerRegistry.RegisterSingleton<MVS.Core.INavigationRequestSink>(
                c => c.Resolve<MultiCameraSystem.ViewModels.MainWindowViewModel>());
            containerRegistry.RegisterSingleton<SettingsService>();

            // 动态多主题服务（参考 MaterialDesignInXamlToolkit 的 PaletteHelper 用法）
            containerRegistry.RegisterSingleton<ThemeService>();

            // 单张图像的 Halcon 过程执行服务（供「运行界面」使用，算法逻辑不在 ViewModel 里）
            containerRegistry.RegisterSingleton<HalconRunService>();

            // 检测宿主：把相机采集链路接到迁移过来的 Inspection 检测模块
            containerRegistry.RegisterSingleton<InspectionHostService>();

            // 启动编排：按步骤做配置/主题/数据库/相机 SDK/Halcon 预热，并向启动窗口报告真实进度
            containerRegistry.RegisterSingleton<AppStartupService>();

            // 文件/文件夹选择对话框：业务模块只依赖 MVS.Core.IFileDialogService 契约，
            // ViewModel 不再直接 new OpenFileDialog（那会让 ViewModel 依赖 UI 类型、也无法测试）
            containerRegistry.RegisterSingleton<MVS.Core.IFileDialogService, WpfFileDialogService>();

            // 注入 Serilog ILogger — 同时写文件 + 广播到UI
            containerRegistry.RegisterSingleton<ILogger>(_ =>
            {
                var logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        path: @"Logs/log.txt",
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 50 * 1024 * 1024,
                        flushToDiskInterval: TimeSpan.FromMilliseconds(5))
                    .WriteTo.Sink(_logBroadcaster)   // ← LogBroadcaster 实现了 ILogEventSink
                    .CreateLogger();

                // 同时提升为 **Serilog 全局静态 logger**。
                //
                // 为什么必须做：本工程是通过容器提供 ILogger 的，从来没有给 Serilog.Log.Logger 赋过值 ——
                // 于是那些"拿不到容器、退回 Serilog.Log.Logger"的代码路径（HalconEngine / HalconView
                // 的兜底分支，以及不属于任何容器的工具类）实际拿到的是 Serilog 的**默认静默 logger**，
                // 日志会无声消失。赋这一次之后：
                //   1) 全局静态 logger 与容器 logger 是同一个实例（写同一份文件、同一个 UI 广播）；
                //   2) Halcon 侧可以彻底不再依赖 MVS.Core.AppContainer 这个静态容器定位器。
                Serilog.Log.Logger = logger;
                return logger;
            });

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


        /// <summary>启动界面（由 CreateShell 创建，OnInitialized 里用完即关）</summary>
        private SplashWindow? _splash;

        /// <summary>主窗口（OnInitialized 里等启动编排结束后显示）</summary>
        private Window? _shell;

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

            // 兜底：万一 OnInitialized 中途异常、主窗口没显示出来，
            // 也不能让用户面对"启动界面关不掉、进程还在"的死界面。
            if (_shell == null)
            {
                WriteCrashHint("启动流程未正常完成，强制显示主窗口（服务可能未完全初始化）");
                try
                {
                    ShowMainWindow(Container.Resolve<MainWindow>(), _splash);
                }
                catch (Exception ex)
                {
                    WriteCrashHint(DescribeException(ex));
                }
            }

            var logger = Container.Resolve<ILogger>();
            logger.Information("程序启动");
        }

        protected override void Initialize()
        {
            base.Initialize();

            // 1) 说明：这里以前会接一个全局容器定位器（MVS.Core.AppContainer）。
            //    它只为"给 HalconView / HalconEngine 取 ILogger"而存在，代价是引入了
            //    "容器必须先于任何控件就绪"这一隐式时序契约（踩过启动期白屏的坑）。
            //    现在日志走 Serilog 全局静态 logger（在 RegisterTypes 里已把容器 logger 提升过去），
            //    定位器连同 AppContainer 类一并删除。

            // 2) 配置加载 + 主题应用。必须早于 Shell 与各视图创建，
            //    否则视图里的资源会先取到兜底色（首屏主题闪一下再变）。
            //    这两步是纯内存/注册表操作，没有 IO 阻塞，不需要放进带进度条的启动编排里，
            //    也不需要依赖任何 Prism 模块服务。
            var settings = Container.Resolve<SettingsService>();
            settings.Load();

            var themeService = Container.Resolve<ThemeService>();
            themeService.Initialize(settings.Current.UI);

            settings.Update(s =>
            {
                s.UI.Theme = themeService.Current.Key;
                s.UI.BaseTheme = themeService.IsDark ? "Dark" : "Light";
            });
        }

        /// <summary>
        /// 在启动界面上按步骤执行启动期初始化，完成后切换到主窗口。
        /// 若存在致命失败，停在启动界面等待用户「重试」或「仍要继续（降级启动）」。
        /// </summary>
        /// <param name="mainWindow">已构造但尚未显示的主窗口</param>
        private void RunStartupWithSplash(Window mainWindow)
        {
            var splash = _splash;

            // 拿不到启动服务（依赖解析失败）时不能卡在启动界面 —— 直接进主界面并留下原因
            AppStartupService startup;
            try
            {
                startup = Container.Resolve<AppStartupService>();
            }
            catch (Exception ex)
            {
                // 只看 ex.Message 会得到毫无信息量的 "An unexpected error occurred while resolving ..."，
                // 必须展开异常链才能看到"到底哪个依赖解析不了"。
                WriteCrashHint("AppStartupService 解析失败，跳过启动编排:" + Environment.NewLine +
                               DescribeException(ex));
                ShowMainWindow(mainWindow, splash);
                return;
            }

            if (splash == null)
            {
                // 极端情况（Shell 被换掉）：没有启动界面可用，直接同步跑完
                try { startup.RunAll(); }
                catch (Exception ex) { WriteCrashHint("启动编排异常:" + Environment.NewLine + DescribeException(ex)); }
                ShowMainWindow(mainWindow, splash);
                return;
            }

            splash.DataContext = startup.Progress;

            void OnRetry(object? sender, EventArgs e) =>
                splash.Dispatcher.BeginInvoke(new Action(() =>
                {
                    splash.ShowFailurePanel(false);
                    if (startup.RunAll(() => splash.TotalProgress.Value = startup.Progress.ProgressPercent))
                    {
                        ShowMainWindow(mainWindow, splash);
                    }
                    else
                    {
                        splash.ShowFailurePanel(true);
                        splash.TotalProgress.Value = startup.Progress.ProgressPercent;
                    }
                }));

            void OnContinue(object? sender, EventArgs e) =>
                splash.Dispatcher.BeginInvoke(new Action(() => ShowMainWindow(mainWindow, splash)));

            splash.RetryRequested += OnRetry;
            splash.ContinueRequested += OnContinue;

            bool completed;
            try
            {
                completed = startup.RunAll(() =>
                {
                    // 进度回调可能来自任意线程，统一切回 UI 线程
                    if (splash.Dispatcher.CheckAccess())
                        splash.TotalProgress.Value = startup.Progress.ProgressPercent;
                    else
                        splash.Dispatcher.BeginInvoke(
                            new Action(() => splash.TotalProgress.Value = startup.Progress.ProgressPercent));
                });
            }
            catch (Exception ex)
            {
                WriteCrashHint("启动编排异常:" + Environment.NewLine + DescribeException(ex));
                completed = false;
            }

            if (completed)
            {
                splash.RetryRequested -= OnRetry;
                splash.ContinueRequested -= OnContinue;

                if (startup.DegradedStepCount > 0)
                    WriteCrashHint($"启动完成，但有 {startup.DegradedStepCount} 个步骤被降级跳过（详见日志）");

                ShowMainWindow(mainWindow, splash);
                return;
            }

            // 有致命失败：停在启动界面，让用户选择重试或降级继续。
            // 不能在这里 ShowDialog —— Application.Run 还没开始，模态消息循环会抛
            // "Cannot perform requested operation because the application is shutting down"。
            // 因此由按钮事件驱动后续流程。
            splash.ShowFailurePanel(true);
            splash.TotalProgress.Value = startup.Progress.ProgressPercent;
        }

        /// <summary>关闭启动界面、显示主窗口并完成首次导航</summary>
        private void ShowMainWindow(Window? mainWindow, Window? splash)
        {
            if (_shell != null) return;   // 防止重试/继续被点两次

            // 用 Prism 基类的 InitializeShell(shell)：它会把 MainWindow 登记好，
            // 并且不会触发 PrismApplication 默认的"CreateShell 返回的就是 Shell"逻辑。
            if (mainWindow != null)
                base.InitializeShell(mainWindow);

            _shell = mainWindow ?? _shell;
            if (_shell == null)
            {
                WriteCrashHint("主窗口为空，无法显示");
                return;
            }

            try
            {
                splash?.Close();
                _splash = null;
            }
            catch (Exception ex)
            {
                WriteCrashHint("关闭启动界面失败: " + ex.Message);
            }

            _shell.Show();

            // ⚠️ 关键一步：主窗口必须补做 Prism 的"区域挂载"。
            //
            // 反汇编 Prism.Wpf 9.0.537 的 PrismApplicationBase.Initialize() 得到的事实：
            //   IL_0073: CreateShell() -> shell
            //   IL_007c: AutowireViewModel(shell)
            //   IL_0082: RegionManager.SetRegionManager(shell, Resolve<IRegionManager>())   ← 区域挂载
            //   IL_0093: RegionManager.UpdateRegions()                                     ← 扫描视觉树注册区域
            //   IL_0098: InitializeShell(shell)   ← 这个方法只做一件事：Application.set_MainWindow(shell)
            //
            // 也就是说，**区域挂载只对 CreateShell() 返回的那个窗口做**。
            // 我们为了先显示启动界面，让 CreateShell() 返回了 SplashWindow，
            // 于是区域全被挂到启动窗口身上；真正的主窗口从没走过 IL_0082/IL_0093 ——
            // 它的 ContentRegion / LogRegion 根本没注册。
            //
            // 症状极具迷惑性：导航日志完全正常（RequestNavigate 找得到视图、也把视图加进了"区域"），
            // 但那个区域从未进入 RegionManager，内容区永远空白，且日志里没有任何错误。
            // 实测日志为证（修复前）：
            //   [区域快照 导航前]    当前没有任何已注册区域（RegionManager 里是空的）
            //   [区域快照 首次导航后] 当前没有任何已注册区域
            //
            // 这里按 Prism 原样补上那两步即可（都是公开 API）。
            if (mainWindow != null)
                AttachRegionsToShell(mainWindow);

            var regionManager = Container.Resolve<IRegionManager>();
            LogRegionState(regionManager, "主窗口显示并注册区域后");

            // 首次导航必须在区域注册完成之后（否则视图无处安放）。
            // 走 MainWindowViewModel 的命令（顶部/侧栏选中态与标题栏会一并同步）。
            try
            {
                Container.Resolve<MultiCameraSystem.ViewModels.MainWindowViewModel>()
                         .NavigateByNameCommand.Execute("DashboardView");

                LogRegionState(regionManager, "首次导航后");

                Navigate(regionManager, "LogRegion", nameof(LogViewerView));
            }
            catch (Exception ex)
            {
                WriteCrashHint("首次导航失败:" + Environment.NewLine + DescribeException(ex));
            }
        }

        /// <summary>
        /// 把 Prism 的区域机制挂到指定窗口上。
        ///
        /// 与 Prism 的 <c>PrismApplicationBase.Initialize()</c> 中那两行完全等价
        /// （见 <see cref="ShowMainWindow"/> 里的反汇编说明）：
        /// 先把 RegionManager 设成窗口的附加属性，再扫描窗口视觉树、把带
        /// <c>RegionName</c> 的控件注册成区域。
        ///
        /// 因为我们的 Shell 是启动窗口，这两步没作用在主窗口上，所以在这里补做一次。
        /// </summary>
        private void AttachRegionsToShell(Window shell)
        {
            // 只挂一次：重试 / "降级继续" 都可能再走到这里，
            // 重复 SetRegionManager + UpdateRegions 会把区域重扫一遍、视图被重复创建。
            if (_shellRegionsAttached) return;

            try
            {
                var regionManager = Container.Resolve<IRegionManager>();

                // 让视图能在该窗口的区域内被自动装配（Prism 对 Shell 也会做这一步）
                Prism.Common.MvvmHelpers.AutowireViewModel(shell);

                RegionManager.SetRegionManager(shell, regionManager);
                RegionManager.UpdateRegions();

                _shellRegionsAttached = true;

                Container.Resolve<ILogger>().Information(
                    "已为主窗口挂载区域管理器并完成区域注册（区域: {Regions}）",
                    string.Join(", ", regionManager.Regions.Select(r => r.Name)));
            }
            catch (Exception ex)
            {
                WriteCrashHint("为主窗口挂载区域失败:" + Environment.NewLine + DescribeException(ex));
            }
        }

        /// <summary>主窗口的区域是否已挂载（防止重复挂载）</summary>
        private bool _shellRegionsAttached;

        /// <summary>
        /// 把 Prism 各区域的状态写进日志（区域是否存在、有几个视图、当前活动视图）。
        /// 内容区空白这类问题只有看这个才定位得到：导航日志说"成功"，但区域可能压根没注册。
        /// </summary>
        private void LogRegionState(IRegionManager regionManager, string phase)
        {
            try
            {
                var logger = Container.Resolve<ILogger>();

                if (regionManager.Regions.Count() == 0)
                {
                    logger.Warning("[区域快照 {Phase}] 当前**没有任何已注册区域**（RegionManager 里是空的）", phase);
                    return;
                }

                foreach (var region in regionManager.Regions)
                {
                    var views = region.Views.ToList();
                    var active = region.ActiveViews.FirstOrDefault();
                    logger.Information("[区域快照 {Phase}] {Region}: 视图 {Count} 个，活动 {Active}",
                        phase, region.Name, views.Count, active?.GetType().Name ?? "（无）");
                }
            }
            catch (Exception ex)
            {
                WriteCrashHint("区域快照失败: " + ex.Message);
            }
        }

        /// <summary>把一条启动期提示追加到 crash_log.txt（与崩溃日志同文件，方便现场一次性取回）</summary>
        private static void WriteCrashHint(string message)
        {
            try
            {
                File.AppendAllText("crash_log.txt", $"[启动] {DateTime.Now} {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
                // 记录失败不再抛，避免掩盖原始问题
            }
        }

        /// <summary>
        /// 展开异常链（含 Prism 的 ResolutionFailedException 内层"无法解析 XXX"）。
        /// 只看 ex.Message 会得到毫无信息量的 "An unexpected error occurred while resolving ..."。
        /// </summary>
        private static string DescribeException(Exception? ex)
        {
            var sb = new StringBuilder();
            var depth = 0;
            while (ex != null && depth < 8)
            {
                sb.AppendLine($"  [{depth}] {ex.GetType().Name}: {ex.Message}");
                ex = ex.InnerException;
                depth++;
            }
            return sb.ToString();
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

            // 到这里为止 Prism 已经：适配器初始化 → 各模块 RegisterTypes → 各模块 OnInitialized → CreateShell。
            // 也就是说 Inspection 模块的服务（InspectionOrchestrator 等条件注册项）现在才能解析，
            // 这正是"启动界面加载服务"必须在此时做的原因（放在 CreateShell 里会解析失败，已实测）。
            //
            // 主窗口在这里构造但不显示：界面上先显示启动界面 + 真实进度，
            // 服务全部就绪后再切到主窗口并完成首次导航。
            Window? mainWindow = null;
            try
            {
                mainWindow = Container.Resolve<MainWindow>();
            }
            catch (Exception ex)
            {
                WriteCrashHint("主窗口构造失败:" + Environment.NewLine + DescribeException(ex));
            }

            RunStartupWithSplash(mainWindow!);
        }

        /// <summary>
        /// 应用退出：按依赖倒序释放容器里的长生命周期资源。
        ///
        /// 为什么必须逐个释放（而不是只关相机）：这些服务都在后台跑着线程/持有 IO，
        /// 不释放会有实际后果 ——
        /// <list type="bullet">
        /// <item><c>InspectionOrchestrator</c>：检测消费线程 + 内部队列；</item>
        /// <item><c>ImageArchiveService</c>：存图线程，**队列里可能还有没写完的图**
        ///（直接退出会丢图，这对视觉检测系统是数据完整性问题）；</item>
        /// <item><c>StatusMonitorService</c>：状态轮询循环；</item>
        /// <item><c>TraceSocketService</c>：复判 Socket 监听（不关会让快速重启绑定端口失败）。</item>
        /// </list>
        /// 每个都单独 try/catch：一个释放失败不能阻断后面的清理。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            var logger = SafeResolve<ILogger>();
            logger?.Information("程序开始退出，正在释放资源…");

            // 1) 检测编排：先停消费线程，并排空队列
            SafeDispose<Inspection.Services.InspectionOrchestrator>("检测编排器");

            // 2) 存图：停止并等待队列里剩余的图写完（这一步会阻塞到写完或超时）
            SafeDispose<Inspection.Services.ImageArchiveService>("存图服务");

            // 3) 状态监视 / 复判 Socket
            SafeDispose<Inspection.Services.StatusMonitorService>("状态监视服务");
            SafeDispose<Inspection.Services.TraceSocketService>("复判 Socket 服务");

            // 4) PLC / MES 连接
            SafeDispose<Inspection.Services.PlcIoService>("PLC 服务");
            SafeDispose<Inspection.Services.MesApiClient>("MES 客户端");

            // 5) 相机 SDK（与原有行为一致；放在最后确保取流已经停了）
            try
            {
                Container.Resolve<ICameraService>().Shutdown();
            }
            catch (Exception ex)
            {
                WriteCrashHint("关闭相机服务失败: " + ex.Message);
            }

            logger?.Information("程序退出");
            _logBroadcaster.Dispose();
            base.OnExit(e);
        }

        /// <summary>安全解析容器服务：未注册 / 解析失败时返回 null，不抛</summary>
        private T? SafeResolve<T>() where T : class
        {
            try
            {
                return Container.GetContainer().Resolve(typeof(T)) as T;
            }
            catch
            {
                // 未注册或解析失败（例如模块未加载）——退出清理阶段不应因此中断
                return null;
            }
        }

        /// <summary>安全释放容器里的 IDisposable 单例（未注册时按钮式跳过）</summary>
        private void SafeDispose<T>(string displayName) where T : class
        {
            var service = SafeResolve<T>();
            if (service is not IDisposable disposable) return;

            try
            {
                disposable.Dispose();
                SafeResolve<ILogger>()?.Debug("已释放: {Service}", displayName);
            }
            catch (Exception ex)
            {
                WriteCrashHint($"释放 {displayName} 失败:" + Environment.NewLine + DescribeException(ex));
            }
        }
    }
}
