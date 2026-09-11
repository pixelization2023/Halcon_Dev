using Microsoft.Win32;
using MVS.Core;
using Serilog;

namespace MultiCameraSystem.Services
{
    /// <summary>
    /// <see cref="IFileDialogService"/> 的 WPF 实现（外壳层）。
    ///
    /// 放在外壳程序集：它需要使用 <c>Microsoft.Win32</c> 的对话框类型，
    /// 而业务模块只依赖 <see cref="IFileDialogService"/> 契约，不碰 UI 类型。
    /// </summary>
    public class WpfFileDialogService : IFileDialogService
    {
        private readonly ILogger _logger;

        public WpfFileDialogService(ILogger logger)
        {
            _logger = logger.ForContext<WpfFileDialogService>();
        }

        public string? OpenFile(string title, string filter, string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            var ok = dialog.ShowDialog() == true;
            if (!ok)
            {
                _logger.Debug("用户取消了打开文件对话框: {Title}", title);
                return null;
            }

            _logger.Debug("已选择文件: {Path}", dialog.FileName);
            return dialog.FileName;
        }

        public string? SaveFile(string title, string filter, string? defaultFileName = null)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter
            };

            if (!string.IsNullOrWhiteSpace(defaultFileName))
                dialog.FileName = defaultFileName;

            var ok = dialog.ShowDialog() == true;
            if (!ok)
            {
                _logger.Debug("用户取消了保存文件对话框: {Title}", title);
                return null;
            }

            _logger.Debug("已选择保存路径: {Path}", dialog.FileName);
            return dialog.FileName;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            var ok = dialog.ShowDialog() == true;
            if (!ok)
            {
                _logger.Debug("用户取消了选择文件夹对话框: {Title}", title);
                return null;
            }

            _logger.Debug("已选择文件夹: {Path}", dialog.FolderName);
            return dialog.FolderName;
        }
    }
}
