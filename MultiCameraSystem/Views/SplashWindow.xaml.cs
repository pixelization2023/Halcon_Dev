using System.Windows;

namespace MultiCameraSystem.Views
{
    /// <summary>
    /// 启动窗口的交互逻辑。
    ///
    /// 职责边界（刻意保持极薄）：
    /// <list type="bullet">
    /// <item>只做"把 ViewModel 挂上去"和"把两个按钮的意图回传给 App"；</item>
    /// <item>所有初始化逻辑都在 <see cref="Services.AppStartupService"/> 里，窗口自身不碰任何服务。</item>
    /// </list>
    /// 这属于视图自身职责（与 MainWindow 的窗口拖动/最小化同类），不是业务逻辑外泄。
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        /// <summary>「重试」被点击：由 App 重新跑一遍失败的步骤</summary>
        public event EventHandler? RetryRequested;

        /// <summary>「仍要继续」被点击：由 App 带着降级状态继续创建主界面</summary>
        public event EventHandler? ContinueRequested;

        /// <summary>显示/隐藏失败操作区</summary>
        public void ShowFailurePanel(bool visible)
        {
            FailurePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);

        private void OnContinueClick(object sender, RoutedEventArgs e) => ContinueRequested?.Invoke(this, EventArgs.Empty);
    }
}
