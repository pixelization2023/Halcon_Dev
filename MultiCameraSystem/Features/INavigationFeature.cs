namespace MultiCameraSystem.Features
{
    /// <summary>
    /// 导航分组。对应主界面顶部主菜单 / 侧栏的归类。
    /// 放在这里是为了让"以后补回来的界面"能自己声明归到哪一组，
    /// 而不需要回来改 <c>MainWindowViewModel.BuildNavigation</c> 里那一段硬编码。
    /// </summary>
    public enum FeatureGroup
    {
        /// <summary>顶部主菜单（重要页面）</summary>
        TopBar = 0,

        /// <summary>侧栏</summary>
        SideBar = 1
    }

    /// <summary>
    /// 功能状态。
    ///
    /// 这个枚举是"预留接口层"的核心：它把"这个功能现在能不能用"变成**数据**而不是注释，
    /// 于是界面可以据此决定是显示入口、显示灰色占位，还是根本不显示。
    /// </summary>
    public enum FeatureStatusKind
    {
        /// <summary>已经能用的功能</summary>
        Declared = 0,

        /// <summary>
        /// 已预留、还没实现的功能。
        /// 来源：与旧版本（GitHub 上的 Halcon_Dev 旧版）对比后确认存在、但当前版本缺失的界面/功能。
        /// 这些**不会出现在导航里**，只会在「预留功能清单」中被列出来，
        /// 避免出现"点进去是个空白页"这种更糟的体验。
        /// </summary>
        Reserved = 1
    }

    /// <summary>
    /// 一个导航功能的声明。
    ///
    /// 用途：新页面（包括以后从旧版本搬回来的界面）只要提供 <see cref="IFeatureProvider"/>，
    /// 就能把自己注册进主界面导航，**不需要修改 MainWindowViewModel**。
    /// 这解决了当前 <c>BuildNavigation</c> 里那段"items 数组 + 手写 RequiredRole"的问题：
    /// 每加一个页面都要改一处中心代码，合并分支时最容易被覆盖掉。
    /// </summary>
    public interface INavigationFeature
    {
        /// <summary>Prism 导航目标（视图名），例如 "ProductView"</summary>
        string ViewName { get; }

        /// <summary>菜单标题</summary>
        string Title { get; }

        /// <summary>侧栏图标下方的小字（尽量短，避免被截断）</summary>
        string Caption { get; }

        /// <summary>MaterialDesign PackIconKind 名称</summary>
        string Icon { get; }

        /// <summary>归到哪一组</summary>
        FeatureGroup Group { get; }

        /// <summary>
        /// 访问该页面需要的最低角色序号。
        /// 与 <c>Inspection.Models.UserRole</c> 的取值保持一致：
        /// 0=未登录也可看、1=操作员、2=工程师、3=管理员。
        ///
        /// 这里刻意用 int 而不是直接引用那个枚举 —— 契约要留在共享层，
        /// 不能反向依赖业务模块（同一个理由见 <c>MVS.Core.IVisionInterfaceProvider</c>）。
        /// </summary>
        int RequiredRole { get; }

        /// <summary>功能状态。<see cref="FeatureStatusKind.Reserved"/> 的条目不会进导航。</summary>
        FeatureStatusKind Status { get; }

        /// <summary>说明（用于「预留功能清单」里展示：这个功能是什么、来自哪里、为什么现在没有）</summary>
        string Description { get; }
    }

    /// <summary>
    /// 导航功能提供者。
    ///
    /// 实现它并按约定注册到容器即可（外壳会汇总所有实现）：
    /// <code>
    /// containerRegistry.RegisterSingleton&lt;IFeatureProvider, YourFeatureProvider&gt;();
    /// </code>
    /// 建议每个业务模块各自实现一个（Inspection 一个、PLCModule 一个…），
    /// 这样"某个模块新增页面"不会碰到其它模块的文件，合并冲突面最小。
    /// </summary>
    public interface IFeatureProvider
    {
        /// <summary>本提供者声明的全部导航功能（含已预留未实现的）</summary>
        IEnumerable<INavigationFeature> GetFeatures();
    }

    /// <summary>功能声明的默认实现（绝大多数功能没有特殊逻辑，用这个就够了）</summary>
    public sealed class NavigationFeature : INavigationFeature
    {
        public NavigationFeature(string viewName, string title, string caption, string icon,
            FeatureGroup group, int requiredRole = 0,
            FeatureStatusKind status = FeatureStatusKind.Declared, string description = "")
        {
            ViewName = viewName ?? string.Empty;
            Title = title ?? string.Empty;
            Caption = caption ?? string.Empty;
            Icon = string.IsNullOrWhiteSpace(icon) ? "FileQuestionOutline" : icon;
            Group = group;
            RequiredRole = requiredRole;
            Status = status;
            Description = description ?? string.Empty;
        }

        public string ViewName { get; }
        public string Title { get; }
        public string Caption { get; }
        public string Icon { get; }
        public FeatureGroup Group { get; }
        public int RequiredRole { get; }
        public FeatureStatusKind Status { get; }
        public string Description { get; }

        public override string ToString() => $"{Title} ({ViewName}) [{Status}]";
    }
}
