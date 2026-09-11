using System.Collections.ObjectModel;
using Serilog;

namespace MultiCameraSystem.Features
{
    /// <summary>
    /// 功能注册表 —— 汇总所有 <see cref="IFeatureProvider"/> 声明的导航功能。
    ///
    /// 设计目的（为什么不直接改 MainWindowViewModel 的硬编码数组）：
    /// <list type="bullet">
    /// <item>现在每加一个页面都要改 <c>BuildNavigation</c> 那一段中心代码，
    /// 多分支合并时最容易在这里被覆盖 —— 这正是"界面丢了"的典型成因；</item>
    /// <item>把"有哪些页面"变成各模块自己声明、注册表汇总的数据，
    /// 于是补回来的界面只要注册一个 provider 就能出现，**不需要碰任何现有文件**；</item>
    /// <item>注册表还能回答"哪些功能是预留但没实现的"，供「预留功能清单」显示。</item>
    /// </list>
    ///
    /// 兼容性：如果**一个 provider 都没注册**，<see cref="GetDeclared"/> 返回空集合，
    /// 主界面完全按原来的硬编码导航工作 —— 也就是说这个类不改变任何现有行为。
    /// </summary>
    public class FeatureRegistry
    {
        private readonly ILogger _logger;
        private readonly List<IFeatureProvider> _providers;

        public FeatureRegistry(ILogger logger, IEnumerable<IFeatureProvider>? providers = null)
        {
            _logger = logger.ForContext<FeatureRegistry>();
            _providers = providers?.Where(p => p != null).ToList() ?? new List<IFeatureProvider>();
        }

        /// <summary>
        /// 全部声明（含预留未实现的）。
        /// 每次调用重新汇总，便于以后支持"运行期增删模块"。
        /// </summary>
        public IReadOnlyList<INavigationFeature> GetAll()
        {
            var result = new List<INavigationFeature>();

            foreach (var provider in _providers)
            {
                try
                {
                    foreach (var feature in provider.GetFeatures() ?? Enumerable.Empty<INavigationFeature>())
                    {
                        if (feature == null || string.IsNullOrWhiteSpace(feature.ViewName)) continue;

                        // 同一个 ViewName 以第一个声明为准，并留下痕迹便于排查重复
                        if (result.Any(f => string.Equals(f.ViewName, feature.ViewName, StringComparison.Ordinal)))
                        {
                            _logger.Warning("导航功能重复声明，已忽略后一个: {ViewName}", feature.ViewName);
                            continue;
                        }

                        result.Add(feature);
                    }
                }
                catch (Exception ex)
                {
                    // 单个 provider 出错不能影响整个导航构造
                    _logger.Error(ex, "汇总功能声明失败: {Provider}", provider.GetType().Name);
                }
            }

            return result;
        }

        /// <summary>
        /// 当前角色**可见**的已实现功能。
        /// 注意：只返回 <see cref="FeatureStatusKind.Declared"/>，
        /// 预留项不会出现在导航里（避免"点进去是空白页"）。
        /// </summary>
        public IReadOnlyList<INavigationFeature> GetVisible(int currentRole)
            => GetAll()
                .Where(f => f.Status == FeatureStatusKind.Declared && f.RequiredRole <= currentRole)
                .ToList();

        /// <summary>已实现的功能（不分角色）</summary>
        public IReadOnlyList<INavigationFeature> GetDeclared()
            => GetAll().Where(f => f.Status == FeatureStatusKind.Declared).ToList();

        /// <summary>
        /// 预留但尚未实现的功能。
        /// 供「预留功能清单」显示 —— 让"缺了什么"是可查询的事实，而不是散落在文档里的待办。
        /// </summary>
        public IReadOnlyList<INavigationFeature> GetReserved()
            => GetAll().Where(f => f.Status == FeatureStatusKind.Reserved).ToList();

        /// <summary>按分组取当前角色可见的功能</summary>
        public IReadOnlyList<INavigationFeature> GetVisibleByGroup(FeatureGroup group, int currentRole)
            => GetVisible(currentRole).Where(f => f.Group == group).ToList();

        /// <summary>
        /// 预留功能清单的可读文本（一行一条）。
        /// 适合直接丢进日志或「自检」页，现场可以据此确认"这个功能是故意没有，还是漏搬了"。
        /// </summary>
        public ReadOnlyCollection<string> BuildReservedFeatureReport()
        {
            var reserved = GetReserved();
            if (reserved.Count == 0)
                return new ReadOnlyCollection<string>(new List<string> { "（没有预留未实现的功能）" });

            var lines = new List<string> { $"预留未实现功能共 {reserved.Count} 项：" };
            foreach (var f in reserved.OrderBy(f => f.Title, StringComparer.Ordinal))
            {
                var line = $"  · {f.Title}（{f.ViewName}，需角色 ≥{f.RequiredRole}）";
                if (!string.IsNullOrWhiteSpace(f.Description)) line += " — " + f.Description;
                lines.Add(line);
            }
            return new ReadOnlyCollection<string>(lines);
        }
    }
}
