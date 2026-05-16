using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace MultiCameraSystem.Views
{
    /// <summary>
    /// 主窗口。
    ///
    /// MVVM 说明：这里只保留「窗口自身」的职责 —— 无边框窗口拖动、最小化/最大化/关闭按钮、
    /// 以及最大化后切换标题栏图标。这些能力 WPF 没有可绑定的等价物（Window.DragMove / WindowState），
    /// Prism 官方示例同样把它们放在代码后置里。
    /// 导航、主题切换、日志面板开关等**应用逻辑**全部在
    /// <see cref="ViewModels.MainWindowViewModel"/> 中，通过 Command/绑定驱动。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            StateChanged += (_, _) => UpdateRestoreIcon();
            UpdateRestoreIcon();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                ToggleWindowState();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
            => ToggleWindowState();

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleWindowState()
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateRestoreIcon()
            => WindowRestoreIcon.Kind = WindowState == WindowState.Maximized
                ? PackIconKind.WindowRestore
                : PackIconKind.WindowMaximize;
    }
}
