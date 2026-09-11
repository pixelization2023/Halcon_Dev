namespace MVS.Core
{
    /// <summary>
    /// 剪贴板写入契约。
    ///
    /// 为什么单独抽一个接口：ViewModel 直连 <c>System.Windows.Clipboard</c> 会让 VM 依赖 UI 类型，
    /// 也没法在测试里断言"到底复制了什么" —— 与 <see cref="IFileDialogService"/> 同一个理由。
    /// 实现在外壳（MultiCameraSystem），由它处理剪贴板被其他进程占用之类的 UI 线程细节。
    /// </summary>
    public interface IClipboardService
    {
        /// <summary>写入文本；失败返回 false（剪贴板被其他进程占用时会失败，不应抛出去）</summary>
        bool TrySetText(string text);
    }
}
