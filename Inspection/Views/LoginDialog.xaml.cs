using System.Windows;
using System.Windows.Controls;
using Inspection.ViewModels;

namespace Inspection.Views
{
    /// <summary>
    /// 权限登录对话框的交互逻辑。
    ///
    /// 这里刻意保留少量代码后置，原因有两条且都属于视图自身职责：
    /// <list type="number">
    /// <item><c>PasswordBox.Password</c> 是**不可绑定**的（WPF 出于安全考虑没有把它做成依赖属性），
    /// 想在 XAML 里直接 TwoWay 绑定密码是做不到的；</item>
    /// <item>按钮用 <c>Click</c> 而不是 Command，是为了在点击瞬间把 PasswordBox 的明文
    /// 交给 ViewModel 后**立刻清空**，避免密码在界面上多停留一个绑定周期。</item>
    /// </list>
    /// 业务判断（账号/口令校验、角色、失败提示）与对话框生命周期全部在
    /// <see cref="LoginDialogViewModel"/> 里（它实现 Prism 的 IDialogAware，
    /// 与本项目另一个对话框 LoadingUserControlViewModel 保持同一种写法）。
    /// </summary>
    public partial class LoginDialog : UserControl
    {
        public LoginDialog()
        {
            InitializeComponent();
        }

        private LoginDialogViewModel? ViewModel => DataContext as LoginDialogViewModel;

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is { } vm && sender is PasswordBox box)
                vm.Password = box.Password;
        }

        private void OnLoginClick(object sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            vm.Password = PasswordInput.Password;
            vm.LoginCommand.Execute();

            // 登录成功即关闭对话框；失败则留在对话框里让用户改密码
            if (vm.IsLoggedIn)
                vm.Close(Prism.Dialogs.ButtonResult.OK);

            PasswordInput.Clear();
        }

        private void OnLogoutClick(object sender, RoutedEventArgs e)
            => ViewModel?.LogoutCommand.Execute();

        private void OnCancelClick(object sender, RoutedEventArgs e)
            => ViewModel?.Close(Prism.Dialogs.ButtonResult.Cancel);
    }
}
