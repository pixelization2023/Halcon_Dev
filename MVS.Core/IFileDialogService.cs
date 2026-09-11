namespace MVS.Core
{
    /// <summary>
    /// 文件 / 文件夹选择对话框服务。
    ///
    /// 存在的理由（解耦 + 可测试性）：
    /// 各 ViewModel 原本直接 <c>new OpenFileDialog()</c> / <c>new OpenFolderDialog()</c>，
    /// 这会带来两个问题：
    /// <list type="number">
    /// <item><b>ViewModel 直接依赖 UI 类型</b>（<c>Microsoft.Win32</c>），破坏了 MVVM 的分层，
    /// 也无法在单元测试里跑（会弹真实窗口、需要 STA 线程）；</item>
    /// <item>各模块（Inspection / 外壳）都要自己写一遍对话框代码。</item>
    /// </list>
    ///
    /// 契约放在共享内核 <c>MVS.Core</c>：由外壳（MultiCameraSystem）提供实现，
    /// 业务模块只依赖这个接口 —— 依赖方向保持单向，与
    /// <see cref="IVisionInterfaceProvider"/> 的做法一致。
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>
        /// 选择单个文件。
        /// </summary>
        /// <param name="title">对话框标题</param>
        /// <param name="filter">过滤器，Win32 格式，例如 <c>"图像文件|*.png;*.jpg|所有文件|*.*"</c></param>
        /// <param name="initialDirectory">初始目录（可空）</param>
        /// <returns>选中的完整路径；用户取消时返回 null</returns>
        string? OpenFile(string title, string filter, string? initialDirectory = null);

        /// <summary>
        /// 选择保存路径。
        /// </summary>
        /// <param name="title">对话框标题</param>
        /// <param name="filter">过滤器（同上）</param>
        /// <param name="defaultFileName">默认文件名（可空）</param>
        /// <returns>选中的完整路径；用户取消时返回 null</returns>
        string? SaveFile(string title, string filter, string? defaultFileName = null);

        /// <summary>
        /// 选择文件夹。
        /// </summary>
        /// <param name="title">对话框标题</param>
        /// <param name="initialDirectory">初始目录（可空）</param>
        /// <returns>选中的目录路径；用户取消时返回 null</returns>
        string? PickFolder(string title, string? initialDirectory = null);
    }
}
