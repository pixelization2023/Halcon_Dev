namespace MVS.Core
{
    /// <summary>
    /// "请外壳帮我导航到某个页面"的契约。
    ///
    /// 为什么需要它：模块（例如 Inspection 的自检页）想让用户"点一下建议就跳到对应设置页"，
    /// 但导航的**选中态同步**逻辑在 Shell 的 <c>MainWindowViewModel</c> 里。
    /// 如果模块直接调 <c>IRegionManager.RequestNavigate</c>，页面会切过去、侧栏高亮却还停在旧项上，
    /// 用户会以为"点了没反应 / 点错了"。
    ///
    /// 所以把导航请求做成契约下沉到内核、由 Shell 实现 —— 与
    /// <see cref="ICameraInventory"/>、<see cref="IVisionInterfaceProvider"/> 同一思路：
    /// **契约在内核，实现在使用方**，依赖方向保持单向。
    /// </summary>
    public interface INavigationRequestSink
    {
        /// <summary>
        /// 请求导航到指定视图。
        /// 实现方应把未知视图名当作无操作处理，而不是抛异常（页面名会随版本变化）。
        /// </summary>
        void RequestNavigate(string viewName);
    }
}
