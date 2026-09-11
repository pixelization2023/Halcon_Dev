namespace MultiCameraSystem.Features
{
    /// <summary>
    /// 预留功能清单。
    ///
    /// <b>为什么有这个文件</b>：与旧版本（GitHub 上的 Halcon_Dev 旧版）相比，当前合并后的版本
    /// 少了一部分界面与功能。这些功能**不是被删掉了，而是没有合并进来** ——
    /// 但如果不记下来，下次再合并时又会重复丢一遍，或者花时间去查"这是漏搬还是故意没有"。
    ///
    /// 这里把每一条都声明成 <see cref="FeatureStatusKind.Reserved"/>：
    /// <list type="bullet">
    /// <item>它们**不会出现在导航里**（避免点进去是空白页）；</item>
    /// <item>会被 <see cref="FeatureRegistry.BuildReservedFeatureReport"/> 汇总成清单，
    /// 可以在日志或自检页直接看到"哪些功能是预留的、为什么"；</item>
    /// <item>以后补回某个界面时，只要把这条的 <c>Status</c> 改成 <c>Declared</c>
    /// 并提供真正的 View + ViewModel，**不需要改动其它任何文件**。</item>
    /// </list>
    ///
    /// <b>重要说明</b>：下面的条目一部分来自代码与文档里的明确证据（例如属性面板明确写着
    /// "迁移自 窗体.UI.FrmPower"却只做了登录、没有用户管理界面），另一部分来自对旧版本的
    /// 已知差异。凡是我无法从本机任何资料确认的，都在 Description 里写明"待确认"，
    /// 不假装它一定存在。等你把旧版的界面清单给我，我再逐条对齐。
    /// </summary>
    public sealed class ReservedFeatureProvider : IFeatureProvider
    {
        /// <summary>预留项的占位视图名统一前缀，便于一眼认出"这不是真页面"</summary>
        private const string ReservedPrefix = "Reserved_";

        public IEnumerable<INavigationFeature> GetFeatures() => new[]
        {
            // ---------------------------------------------------------------
            // 证据：Services/UserSessionService.cs 的属性说明写着
            //       "迁移自 窗体.UI.FrmPower ... 管理员：全部权限 + 用户管理"，
            //       而当前只做了登录对话框，没有任何"用户管理"界面。
            // ---------------------------------------------------------------
            Reserved("用户与权限管理", "UserManagement", "用户管理", "AccountCog", 3,
                "旧版 FrmPower 含账号/口令/角色维护（UserSessionService 的属性说明里也写了「管理员：全部权限 + 用户管理」），" +
                "当前只有登录对话框，账号表仍硬编码在 UserSessionService 构造函数里。待补：账号增删改、口令修改、角色分配。"),

            // ---------------------------------------------------------------
            // 证据：Docs/迁移说明.md 第 3 节明确写了
            //       "历史 .asol 文件无法自动读取，需要在现场重新配置一次"。
            //       这是一个"已知且明确"的缺口，不是猜测。
            // ---------------------------------------------------------------
            Reserved("旧配置(.asol)导入工具", "LegacyAsolImport", "旧配置导入", "DatabaseImport", 2,
                "旧版配置是 BinaryFormatter 的 .asol（二进制）。Docs/迁移说明.md 明确写了「无法自动读取，需要现场重新配置一次」。" +
                "待补：一次性导入工具（需在 .NET Framework 侧解析后转 JSON，.NET 9 已移除 BinaryFormatter）。"),

            // ---------------------------------------------------------------
            // 证据：参考项目 DO IT!!\Halcon 的 MachineVision.templateMach 有
            //       ShapeMatchingView / DrawShapeView（手动绘制 ROI、创建模板、匹配、
            //       显示中心点/文本/匹配轮廓）。当前 AI 侧只有 WorkBench 的
            //       「从过程接口导入端口」，没有任何交互式绘形/建模工具界面。
            // ---------------------------------------------------------------
            Reserved("模板匹配与交互式建模", "TemplateMatchTool", "模板匹配", "ShapeOutline", 2,
                "参考项目 MachineVision.templateMach 提供交互式 ROI 绘制（矩形/椭圆/圆/区域）+ 创建模板 + 匹配结果叠加显示" +
                "（中心点/文本/轮廓，即 ImageEdeitView 的 MatchResult 渲染）。当前版本没有对应界面。待补：绘制与建模工具页。"),

            // ---------------------------------------------------------------
            // 待确认项：结果只写入 MySQL（InspectionResultStore），当前没有查询/导出界面。
            // 旧版是否有独立的历史查询界面，我无法从本机资料确认。
            // ---------------------------------------------------------------
            Reserved("历史数据查询与导出", "HistoryQuery", "历史查询", "TableSearch", 1,
                "当前检测结果只写入 MySQL（InspectionResultStore），界面侧只有实时表格（PcsResults，内存中最近 200 条）。" +
                "待确认：旧版是否有独立的历史查询/导出界面；确认后再决定是补界面还是给出 SQL 脚本即可。"),

            // ---------------------------------------------------------------
            // 待确认项：参考项目有 Assets/zh_CN.xaml + en_US.xaml + LanguageConverter，
            // 说明旧版有中英切换。当前 AI 侧全部为中文硬编码。
            // ---------------------------------------------------------------
            Reserved("多语言（中/英）切换", "LanguageSettings", "语言", "Translate", 2,
                "参考项目含 Assets/zh_CN.xaml、en_US.xaml 与 LanguageConverter/Extension/LanguageHelp。" +
                "当前版本所有文案都是中文硬编码。待确认：旧版是否真的做了语言切换，还是只是模板残留。")
        };

        private static INavigationFeature Reserved(string title, string viewSuffix, string caption,
            string icon, int requiredRole, string description)
            => new NavigationFeature(
                ReservedPrefix + viewSuffix,
                title,
                caption,
                icon,
                FeatureGroup.SideBar,
                requiredRole,
                FeatureStatusKind.Reserved,
                description);
    }
}
