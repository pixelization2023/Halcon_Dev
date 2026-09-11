
using System.Collections.ObjectModel;
using System.Windows;
using Inspection.Services;
using MultiCameraSystem.Services;
using MultiCameraSystem.Themes;
using Prism.Commands;
using Prism.Dialogs;
using Serilog;


namespace MultiCameraSystem.ViewModels
{
    /// <summary>
    /// 导航项。
    /// 原来的 MainWindowViewModel 为每个页面写了一个 bool 属性 + 一大段 switch，
    /// 新增页面必须同时改 XAML、属性、两个 switch。这里改为数据驱动的导航项集合，
    /// 新增页面只需在 <see cref="MainWindowViewModel.BuildNavigation"/> 里加一行。
    /// </summary>
    public class NavigationItem : BindableBase
    {
        public NavigationItem(string viewName, string title, string caption, string icon, bool showInTopBar,
            int requiredRole = 0)
        {
            ViewName = viewName;
            Title = title;
            Caption = caption;
            Icon = icon;
            ShowInTopBar = showInTopBar;
            RequiredRole = requiredRole;
        }

        /// <summary>侧栏图标下方的小字（短，避免被截断）</summary>
        public string Caption { get; }

        /// <summary>Prism 导航目标（视图名）</summary>
        public string ViewName { get; }

        /// <summary>菜单标题</summary>
        public string Title { get; }

        /// <summary>MaterialDesign PackIconKind 名称</summary>
        public string Icon { get; }

        /// <summary>是否在顶部主菜单显示（其余只在侧栏显示）</summary>
        public bool ShowInTopBar { get; }

        /// <summary>
        /// 访问该页面需要的最低角色序号（0=任何人可看）。
        /// 取值来自 <see cref="UserSessionService.CurrentUserRole"/>：
        /// 未登录=0、操作员=1、工程师=2、管理员=3。
        /// 这是"角色权限"真正落到界面上的地方 —— 旧实现里 CanEditGroup 全仓没有调用点，
        /// 结果操作员照样能点进系统设置。
        /// </summary>
        public int RequiredRole { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private bool _isLockedForCurrentRole;
        /// <summary>
        /// 当前角色是否**进不去**本页（未达到 <see cref="RequiredRole"/>）。
        /// 界面据此在侧栏图标上显示一个锁角标。
        ///
        /// 说明：这只是提示，不是禁用 —— 点上去会提示需要什么权限并弹出登录对话框。
        /// 之前是直接把页面从导航里删掉，现场表现就是"很多界面不见了"。
        /// </summary>
        public bool IsLockedForCurrentRole
        {
            get => _isLockedForCurrentRole;
            set => SetProperty(ref _isLockedForCurrentRole, value);
        }

        public override string ToString() => Title;
    }

    /// <summary>
    /// 主窗口 ViewModel：顶部导航、侧栏导航、主题即时切换、日志面板开关。
    /// 视图（MainWindow.xaml）只负责布局，导航项与主题都是数据驱动的。
    /// </summary>
    public class MainWindowViewModel : BindableBase, MVS.Core.INavigationRequestSink
    {
        /// <summary>权限登录对话框的注册名（由 Inspection 模块注册，见 InspectionModule.RegisterTypes）</summary>
        private const string LoginDialogName = "LoginDialog";

        private readonly IRegionManager _regionManager;
        private readonly ThemeService _themeService;
        private readonly SettingsService _settings;
        private readonly UserSessionService _session;
        private readonly IDialogService _dialogService;
        private readonly ILogger _logger;

        public MainWindowViewModel(IRegionManager regionManager, ThemeService themeService,
            SettingsService settings, UserSessionService session, IDialogService dialogService, ILogger logger)
        {
            _regionManager = regionManager;
            _themeService = themeService;
            _settings = settings;
            _session = session;
            _dialogService = dialogService;
            _logger = logger.ForContext<MainWindowViewModel>();

            BuildNavigation();
            NavigateCommand = new DelegateCommand<NavigationItem>(ExecuteNavigate);
            NavigateByNameCommand = new DelegateCommand<string>(name => ExecuteNavigateByName(name));

            ApplyThemeCommand = new DelegateCommand<ThemeDefinition>(ExecuteApplyTheme);
            ToggleBaseThemeCommand = new DelegateCommand(ExecuteToggleBaseTheme);
            ToggleLogPanelCommand = new DelegateCommand(() => IsLogPanelVisible = !IsLogPanelVisible);

            // 状态栏「用户胶囊」：点击弹出登录对话框（不再占用一个导航页）
            LoginCommand = new DelegateCommand(ExecuteLogin);
            LogoutCommand = new DelegateCommand(ExecuteLogout);

            // 角色变化时重建导航（按 RequiredRole 过滤）并刷新状态栏显示
            _session.RoleChanged += (_, _) => RefreshRoleDependentState();

            RefreshThemes();
            _themeService.ThemesChanged += (_, _) => RefreshThemes();

            _selectedTheme = _themeService.Current;
            _isDarkTheme = _themeService.IsDark;

            // 其它入口（例如外观设置页）切换主题时，标题栏的快速切换也要同步。
            // 这里直接改字段 + 通知，不经过会再次调用 Apply 的属性 setter，避免重入。
            _themeService.ThemeChanged += (_, theme) =>
            {
                _suppressThemeApply = true;
                try
                {
                    SetProperty(ref _selectedTheme, theme, nameof(SelectedTheme));
                    _isDarkTheme = _themeService.IsDark;
                    RaisePropertyChanged(nameof(IsDarkTheme));
                    RaisePropertyChanged(nameof(ThemeDisplayName));
                }
                finally
                {
                    _suppressThemeApply = false;
                }
            };

            RefreshRoleDependentState();
            SelectByViewName("DashboardView");
        }

        #region 导航

        /// <summary>顶部主菜单</summary>
        public ObservableCollection<NavigationItem> TopNavigation { get; } = new();

        /// <summary>侧栏导航</summary>
        public ObservableCollection<NavigationItem> SideNavigation { get; } = new();

        public DelegateCommand<NavigationItem> NavigateCommand { get; }

        public DelegateCommand<string> NavigateByNameCommand { get; }

        private void BuildNavigation()
        {
            // viewName, 页面标题, 侧栏小字, 图标, 是否置顶, 最低角色（0=任何人 / 1=操作员 / 2=工程师）
            //
            // 关于 RequiredRole：这是"权限真正生效"的落点。
            // 未登录（0）时只保留监控台、首页、日志这些"看"的页面；
            // 涉及配方/相机/系统设置/自检的动作页需要工程师及以上。
            // 注意：这是**界面层**的可见性控制；服务层是否放行由各 ViewModel 的 CanEdit 决定。
            var items = new[]
            {
                new NavigationItem("DashboardView", "检测主监控台", "监控台", "MonitorDashboard", true, 0),
                new NavigationItem("Home", "首页", "首页", "HomeVariant", false, 0),
                new NavigationItem("LogViewerView", "系统日志", "日志", "Text", false, 0),
                new NavigationItem("ResultDashboardView", "结果仪表盘", "仪表盘", "ChartBar", false, 0),
                new NavigationItem("PLCMonitorView", "PLC 监控", "PLC", "Sitemap", false, 0),
                new NavigationItem("MESStatusView", "MES 状态", "MES", "Database", false, 0),

                new NavigationItem("ProductView", "产品与方案", "产品", "PackageVariant", true, 2),
                new NavigationItem("InspectionConfigView", "检测配置", "配置", "Tune", true, 2),
                new NavigationItem("SystemSettingsView", "检测与通讯设置", "检测设置", "TuneVariant", false, 2),
                new NavigationItem("CameraManagerView", "相机管理", "相机", "Camera", false, 2),
                new NavigationItem("MultiCameraView", "多相机概览", "概览", "ViewDashboard", false, 1),
                new NavigationItem("TestView", "流程测试", "测试", "FlaskOutline", false, 2),
                new NavigationItem("RunView", "运行界面", "运行", "PlayCircle", false, 1),
                new NavigationItem("WorkBenchView", "工作台", "工作台", "Cog", false, 2),
                new NavigationItem("SelfTestView", "自检与仿真", "自检", "ClipboardCheckOutline", false, 2),
                new NavigationItem("SettingsView", "系统设置", "设置", "Settings", false, 2),
                new NavigationItem("ThemeSettingsView", "外观主题", "外观", "Palette", true, 0)
            };

            _allNavigation = items.ToList();
            ApplyRoleFilter();
        }

        /// <summary>
        /// 全部导航项。
        ///
        /// 注意：**导航不再按角色过滤**（见 <see cref="ApplyRoleFilter"/>）。
        /// 之前这里是"未登录只显示 6 项"，结果是未登录状态下侧栏少了一大半，
        /// 现场看着就是"界面/功能丢了"。现在导航恒为全量，权限改为**进入时**校验
        ///（<see cref="ExecuteNavigateByName"/> → 需要登录就弹登录对话框）。
        /// </summary>
        private List<NavigationItem> _allNavigation = new();

        /// <summary>
        /// 刷新导航集合与"锁"标记。
        ///
        /// 策略（2026-09 调整）：**导航全显，进入时校验**。
        ///
        /// 曾经的错误做法：这里用 <c>_allNavigation.Where(i =&gt; i.RequiredRole &lt;= role)</c> 过滤，
        /// 意图是"未登录时把要权限的页面藏起来"。但 `RequiredRole` 为 0 的页面只有 7 个，
        /// 于是未登录时侧栏**只剩 7 项**，另外 10 项（产品/检测配置/相机/自检…）全部消失，
        /// 现场看到的就是"合并之后界面少了一大半、功能像丢了"。
        ///
        /// 现在：导航恒为全部项（任何登录状态下结构一致），权限在
        /// <see cref="ExecuteNavigateByName"/> 里按需校验 —— 未登录点受限页会提示并弹登录对话框。
        /// </summary>
        private void ApplyRoleFilter()
        {
            var role = _session.CurrentUserRole;

            // 锁标记：当前角色进不去的页面显示锁角标（只是提示，点上去会引导登录）
            foreach (var item in _allNavigation)
                item.IsLockedForCurrentRole = item.RequiredRole > role;

            // 导航始终是全量。只在集合内容与全量不一致时才重建（首次、或换了产品级导航插件之后）
            if (SideNavigation.Count == _allNavigation.Count)
                return;

            RebuildNavigationCollections(_allNavigation);
        }

        private void RebuildNavigationCollections(List<NavigationItem> items)
        {
            SideNavigation.Clear();
            TopNavigation.Clear();
            foreach (var item in items)
            {
                SideNavigation.Add(item);
                if (item.ShowInTopBar) TopNavigation.Add(item);
            }
        }

        /// <summary>
        /// 当前角色是否允许进入某页面。
        /// 与 <see cref="ApplyRoleFilter"/> 用同一套 RequiredRole 规则，保证"能不能点"与
        /// "显不显示锁"永远一致。
        /// </summary>
        public bool CanAccess(string viewName, out int requiredRole)
        {
            requiredRole = 0;
            var item = _allNavigation.FirstOrDefault(i =>
                string.Equals(i.ViewName, viewName, StringComparison.Ordinal));

            if (item == null) return true;   // 不在导航里的页面不拦（例如登录对话框、外部跳转）
            requiredRole = item.RequiredRole;
            return _session.CurrentUserRole >= item.RequiredRole;
        }

        /// <summary>角色变化后刷新导航与状态栏</summary>
        private void RefreshRoleDependentState()
        {
            ApplyRoleFilter();
            RaiseAccessStateChanged();

            // 当前页若因为角色变化而不可见（目前不会发生，保留兼容），退回监控台
            var current = SideNavigation.FirstOrDefault(i => i.IsSelected);
            if (current == null)
                SelectByViewName("DashboardView");
        }

        /// <summary>刷新与"登录状态 / 权限"相关的界面显示</summary>
        private void RaiseAccessStateChanged()
        {
            RaisePropertyChanged(nameof(CurrentUserText));
            RaisePropertyChanged(nameof(RoleText));
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(CanLogout));
            RaisePropertyChanged(nameof(StatusNotice));
        }

        /// <summary>
        /// 状态栏（底部）显示的提示，用于告诉操作员"刚才那次点击为什么没进去"。
        /// 这是新增的可见反馈：以前点了受限页是**静默无反应**，现场只会觉得"点不动/坏了"。
        /// </summary>
        private string _statusNotice = string.Empty;
        public string StatusNotice
        {
            get => _statusNotice;
            private set
            {
                if (SetProperty(ref _statusNotice, value))
                    RaisePropertyChanged(nameof(IsNoticeVisible));
            }
        }

        /// <summary>状态栏是否显示操作提示</summary>
        public bool IsNoticeVisible => !string.IsNullOrWhiteSpace(_statusNotice);

        /// <summary>取导航项的标题（用于提示文案）</summary>
        private string GetTitle(string viewName)
            => _allNavigation.FirstOrDefault(i =>
                   string.Equals(i.ViewName, viewName, StringComparison.Ordinal))?.Title ?? viewName;

        /// <summary>角色名（用于提示文案）</summary>
        private static string RoleName(int role) => role switch
        {
            1 => "操作员",
            2 => "工程师",
            3 => "管理员",
            _ => "登录用户"
        };

        void ExecuteNavigate(NavigationItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.ViewName)) return;
            ExecuteNavigateByName(item.ViewName);
        }

        /// <summary>
        /// 导航到某个页面。
        ///
        /// 权限策略（2026-09 第二次调整，以现场反馈为准）：
        /// **导航永远可用；未登录 = 只读状态，而不是进不去。**
        ///
        /// 演进过程（两次都踩了坑，记下来避免再犯）：
        /// <list type="number">
        /// <item>最初：导航按角色过滤 —— 未登录时侧栏只剩 7 项，现场看成"界面丢了一大半"；</item>
        /// <item>然后：改成"点受限页弹登录" —— 登录对话框是**模态阻塞**的，
        /// 点一下界面就被对话框挡住，操作员感受到的是"点了没反应/卡住"；</item>
        /// <item>现在：导航不拦，页面自己按 <c>CanEdit</c> 决定可编辑性（未登录即只读）。
        /// 这与"不登录就是能运行查看、但没有修改权限"的现场预期一致。</item>
        /// </list>
        /// </summary>
        /// <summary>
        /// 来自模块（例如自检页的「去处理」按钮）的导航请求。
        ///
        /// 刻意复用 <see cref="ExecuteNavigateByName"/>：这样**侧栏选中态同步、页面标题、权限校验**
        /// 一个都不会漏。模块若直接调 <c>IRegionManager.RequestNavigate</c>，
        /// 页面会切过去而侧栏高亮仍停在旧项上。
        ///
        /// 未知视图名由 <see cref="ExecuteNavigateByName"/> 内部按"找不到导航项"处理，不抛异常。
        /// </summary>
        public void RequestNavigate(string viewName) => ExecuteNavigateByName(viewName);

        void ExecuteNavigateByName(string viewName)
        {
            if (string.IsNullOrEmpty(viewName)) return;

            // 只读提示：不需要登录就能进，但要让人知道"现在改不了东西"
            if (!_session.IsLoggedIn)
            {
                if (CanAccess(viewName, out var need))
                    StatusNotice = "当前未登录：可以查看，但无法保存/修改（状态栏右侧点「未登录」可登录）";
                else
                    StatusNotice = $"当前未登录：「{GetTitle(viewName)}」只能查看，" +
                                   $"修改需要{RoleName(need)}权限（点状态栏右侧「未登录」登录）";
            }
            else
            {
                StatusNotice = string.Empty;
            }

            _logger.Information("导航到视图: {ViewName}（角色 {Role}）", viewName, _session.RoleText);

            SelectByViewName(viewName);
            _regionManager.RequestNavigate("ContentRegion", viewName);
        }

        private void SelectByViewName(string viewName)
        {
            foreach (var item in SideNavigation)
                item.IsSelected = string.Equals(item.ViewName, viewName, StringComparison.Ordinal);

            var current = SideNavigation.FirstOrDefault(i => i.IsSelected);
            Title = current?.Title ?? viewName;
        }

        #endregion

        #region 用户与权限（状态栏用户胶囊）

        /// <summary>点击状态栏用户胶囊 → 弹出登录对话框</summary>
        public DelegateCommand LoginCommand { get; }

        /// <summary>注销当前用户</summary>
        public DelegateCommand LogoutCommand { get; }

        /// <summary>状态栏显示文本：未登录 / 用户名（角色）</summary>
        public string CurrentUserText => _session.IsLoggedIn
            ? $"{_session.UserName} · {_session.RoleText}"
            : "未登录";

        /// <summary>角色文本（未登录时为"只读模式"）</summary>
        public string RoleText => _session.IsLoggedIn ? _session.RoleText : "只读模式";

        public bool IsLoggedIn => _session.IsLoggedIn;

        /// <summary>未登录时"注销"按钮无意义</summary>
        public bool CanLogout => _session.IsLoggedIn;

        private void ExecuteLogin()
            => ShowLoginDialog(requiredRole: 0, pendingViewName: null);

        /// <summary>
        /// 弹出登录对话框。
        /// </summary>
        /// <param name="requiredRole">本次登录是为了进入什么权限的页面（0 = 只是用户自己点胶囊登录）</param>
        /// <param name="pendingViewName">登录成功且权限足够后要自动进入的页面（null = 不跳转）</param>
        private void ShowLoginDialog(int requiredRole, string? pendingViewName)
        {
            try
            {
                // 用 Prism 的对话框服务拉起已注册的登录对话框。
                // 这是项目里之前**完全没有用到**的能力：CustomControl 早就注册好了
                // CustomDialogWindow（IDialogWindow），但没有任何一处调用 ShowDialog。
                _dialogService.ShowDialog(LoginDialogName, result =>
                {
                    if (result?.Result != ButtonResult.OK)
                    {
                        // 用户取消：把提示留清楚，不要让"点了没反应"成为唯一反馈
                        if (pendingViewName != null)
                            StatusNotice = $"未登录，「{GetTitle(pendingViewName)}」保持锁定";
                        return;
                    }

                    if (pendingViewName == null)
                    {
                        _logger.Information("已打开权限登录对话框，当前用户: {User}", CurrentUserText);
                        return;
                    }

                    // 登录成功：再核一次权限，够了才进去（可能登了个权限更低的账号）
                    if (!CanAccess(pendingViewName, out var need))
                    {
                        StatusNotice = $"已登录为「{_session.RoleText}」，但「{GetTitle(pendingViewName)}」" +
                                       $"需要{RoleName(need)}及以上权限";
                        return;
                    }

                    // 对话框刚关闭，把跳转放到下一次 UI 循环执行：
                    // 避免在对话框关闭回调里直接操作区域导航（可能仍在嵌套消息循环中）。
                    var target = pendingViewName;
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null)
                        ExecuteNavigateByName(target);
                    else
                        dispatcher.BeginInvoke(new Action(() => ExecuteNavigateByName(target)));
                });

                _logger.Information("已打开权限登录对话框（目标页面 {Target}），当前用户: {User}",
                    pendingViewName ?? "（无）", CurrentUserText);
            }
            catch (Exception ex)
            {
                // 对话框注册名写错/模块没加载时，这里必须留下明确线索，
                // 否则表现只是"点了没反应"。
                StatusNotice = "打开登录对话框失败，请查看日志";
                _logger.Error(ex, "打开登录对话框失败（注册名 {Name}）", LoginDialogName);
            }
        }

        private void ExecuteLogout()
        {
            _session.Logout();
            StatusNotice = "已注销，受限页面需要重新登录";
            _logger.Information("用户已注销");
        }

        #endregion

        #region 主题

        /// <summary>可选主题（内置 + 用户自定义），随自定义主题增删自动刷新</summary>
        public ObservableCollection<ThemeDefinition> Themes { get; } = new();

        private ThemeDefinition _selectedTheme;
        public ThemeDefinition SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (!SetProperty(ref _selectedTheme, value)) return;
                if (_suppressThemeApply || value == null) return;
                ExecuteApplyTheme(value);
            }
        }

        /// <summary>为 true 时属性变更不回头调用 ThemeService（防止事件回推造成重入）</summary>
        private bool _suppressThemeApply;

        private bool _isDarkTheme;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (!SetProperty(ref _isDarkTheme, value)) return;
                if (!_suppressBaseThemeApply)
                    ExecuteApplyBaseTheme(value ? "Dark" : "Light");
            }
        }

        /// <summary>避免 ThemeService 回推时形成递归</summary>
        private bool _suppressBaseThemeApply;

        public DelegateCommand<ThemeDefinition> ApplyThemeCommand { get; }
        public DelegateCommand ToggleBaseThemeCommand { get; }

        private void RefreshThemes()
        {
            Themes.Clear();
            foreach (var definition in _themeService.Themes)
                Themes.Add(definition);
        }

        private void ExecuteApplyTheme(ThemeDefinition? theme)
        {
            if (theme == null) return;

            // 与当前主题「同键同色」时不必重复应用（自定义主题重建实例后引用会变，因此按内容比较）
            var current = _themeService.Current;
            if (string.Equals(current.Key, theme.Key, StringComparison.Ordinal)
                && current.Primary == theme.Primary
                && current.Secondary == theme.Secondary)
                return;

            _themeService.Apply(theme.Key, _settings.Current.UI.BaseTheme, persist: true);
            _settings.Update(s => s.UI.Theme = theme.Key);

            _suppressBaseThemeApply = true;
            IsDarkTheme = _themeService.IsDark;
            _suppressBaseThemeApply = false;

            if (!ReferenceEquals(_selectedTheme, theme))
                SetProperty(ref _selectedTheme, theme, nameof(SelectedTheme));
        }

        private void ExecuteToggleBaseTheme() => ExecuteApplyBaseTheme(IsDarkTheme ? "Light" : "Dark");

        private void ExecuteApplyBaseTheme(string baseTheme)
        {
            _themeService.Apply(_themeService.Current.Key, baseTheme, persist: true);
            _settings.Update(s => s.UI.BaseTheme = baseTheme);

            _suppressBaseThemeApply = true;
            IsDarkTheme = _themeService.IsDark;
            _suppressBaseThemeApply = false;
        }

        #endregion

        #region 日志面板

        private bool _isLogPanelVisible;
        /// <summary>底部实时日志面板是否展开（原来在代码后置里直接改控件 Visibility）</summary>
        public bool IsLogPanelVisible
        {
            get => _isLogPanelVisible;
            set => SetProperty(ref _isLogPanelVisible, value);
        }

        public DelegateCommand ToggleLogPanelCommand { get; }

        #endregion

        private string _title = "CCD 视觉检测系统";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>当前主题的展示名，状态栏用</summary>
        public string ThemeDisplayName => _themeService.Current.DisplayName;
    }
}
