
using System.Collections.ObjectModel;
using MultiCameraSystem.Services;
using MultiCameraSystem.Themes;
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
        public NavigationItem(string viewName, string title, string caption, string icon, bool showInTopBar)
        {
            ViewName = viewName;
            Title = title;
            Caption = caption;
            Icon = icon;
            ShowInTopBar = showInTopBar;
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

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public override string ToString() => Title;
    }

    /// <summary>
    /// 主窗口 ViewModel：顶部导航、侧栏导航、主题即时切换、日志面板开关。
    /// 视图（MainWindow.xaml）只负责布局，导航项与主题都是数据驱动的。
    /// </summary>
    public class MainWindowViewModel : BindableBase
    {
        private readonly IRegionManager _regionManager;
        private readonly ThemeService _themeService;
        private readonly SettingsService _settings;
        private readonly ILogger _logger;

        public MainWindowViewModel(IRegionManager regionManager, ThemeService themeService,
            SettingsService settings, ILogger logger)
        {
            _regionManager = regionManager;
            _themeService = themeService;
            _settings = settings;
            _logger = logger.ForContext<MainWindowViewModel>();

            BuildNavigation();
            NavigateCommand = new DelegateCommand<NavigationItem>(ExecuteNavigate);
            NavigateByNameCommand = new DelegateCommand<string>(name => ExecuteNavigateByName(name));

            ApplyThemeCommand = new DelegateCommand<ThemeDefinition>(ExecuteApplyTheme);
            ToggleBaseThemeCommand = new DelegateCommand(ExecuteToggleBaseTheme);
            ToggleLogPanelCommand = new DelegateCommand(() => IsLogPanelVisible = !IsLogPanelVisible);

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
            // viewName, 标题, 图标, 是否在顶部主菜单
            // viewName, 页面标题, 侧栏小字, 图标, 是否置顶
            var items = new[]
            {
                new NavigationItem("DashboardView", "检测主监控台", "监控台", "MonitorDashboard", true),
                new NavigationItem("ProductView", "产品与方案", "产品", "PackageVariant", true),
                new NavigationItem("InspectionConfigView", "检测配置", "配置", "Tune", true),
                new NavigationItem("LoginView", "权限登录", "权限", "AccountKey", true),
                new NavigationItem("Home", "首页", "首页", "HomeVariant", false),
                new NavigationItem("CameraManagerView", "相机管理", "相机", "Camera", false),
                new NavigationItem("MultiCameraView", "多相机概览", "概览", "ViewDashboard", false),
                new NavigationItem("PLCMonitorView", "PLC 监控", "PLC", "Sitemap", false),
                new NavigationItem("MESStatusView", "MES 状态", "MES", "Database", false),
                new NavigationItem("TestView", "流程测试", "测试", "FlaskOutline", false),
                new NavigationItem("RunView", "运行界面", "运行", "PlayCircle", false),
                new NavigationItem("WorkBenchView", "工作台", "工作台", "Cog", false),
                new NavigationItem("ResultDashboardView", "结果仪表盘", "仪表盘", "ChartBar", false),
                new NavigationItem("LogViewerView", "系统日志", "日志", "Text", false),
                new NavigationItem("SettingsView", "系统设置", "设置", "Settings", false),
                new NavigationItem("SystemSettingsView", "检测与通讯设置", "检测设置", "TuneVariant", false),
                new NavigationItem("SelfTestView", "自检与仿真", "自检", "ClipboardCheckOutline", false),
                new NavigationItem("ThemeSettingsView", "外观主题", "外观", "Palette", true)
            };

            foreach (var item in items)
            {
                SideNavigation.Add(item);
                if (item.ShowInTopBar) TopNavigation.Add(item);
            }
        }

        void ExecuteNavigate(NavigationItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.ViewName)) return;
            ExecuteNavigateByName(item.ViewName);
        }

        void ExecuteNavigateByName(string viewName)
        {
            if (string.IsNullOrEmpty(viewName)) return;

            _logger.Information("导航到视图: {ViewName}", viewName);

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
