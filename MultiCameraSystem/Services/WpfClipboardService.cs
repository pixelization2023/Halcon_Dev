using System.Windows;
using MVS.Core;

namespace MultiCameraSystem.Services
{
    /// <summary>
    /// <see cref="IClipboardService"/> 的 WPF 实现。
    ///
    /// 剪贴板是**全局共享资源**：别的进程正占用时 <see cref="Clipboard.SetText(string)"/>
    /// 会抛 COMException。这是常见且正常的竞争，所以这里最多重试两次，失败就返回 false，
    /// 绝不把异常抛给调用方（自检报告复制失败不该影响自检本身）。
    /// </summary>
    internal sealed class WpfClipboardService : IClipboardService
    {
        public bool TrySetText(string text)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // SetDataObject + copy:true 让内容在进程退出后仍留在剪贴板
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch
                {
                    // 被占用：短暂让出线程再重试
                    Thread.Sleep(60);
                }
            }

            return false;
        }
    }
}
