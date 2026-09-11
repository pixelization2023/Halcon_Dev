using System.Collections.ObjectModel;
using Inspection.Services;
using Prism.Commands;
using Prism.Dialogs;
using Serilog;

namespace Inspection.ViewModels
{
    /// <summary>
    /// 权限登录对话框的视图模型。
    ///
    /// 迁移自 窗体.UI.FrmPower —— 原实现把用户名与口令硬编码在按钮事件里。
    /// 现在账号清单来自 <see cref="UserSessionService"/>（唯一真源），
    /// 默认口令仍然是 工程师 666666 / 操作员 5588 / 管理员 admin888，现场可平滑过渡。
    ///
    /// 实现 Prism 的 IDialogAware：关闭通道用 <see cref="DialogCloseListener"/> 属性
    ///（Prism 9 的写法，与本项目 CustomControl 的 LoadingUserControlViewModel 一致；
    /// 旧版的 <c>event Action&lt;IDialogResult&gt; RequestClose</c> 已不被该接口接受）。
    /// </summary>
    public class LoginDialogViewModel : BindableBase, IDialogAware
    {
        private readonly UserSessionService _session;
        private readonly ILogger _logger;

        public LoginDialogViewModel(UserSessionService session, ILogger logger)
        {
            _session = session;
            _logger = logger.ForContext<LoginDialogViewModel>();

            LoginCommand = new DelegateCommand(ExecuteLogin);
            LogoutCommand = new DelegateCommand(ExecuteLogout);

            _userName = Accounts.FirstOrDefault() ?? "工程师";
        }

        /// <summary>可选账号（来自会话服务的账号表）</summary>
        public IReadOnlyList<string> Accounts => _session.AccountNames;

        private string _userName;
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        private string _password = string.Empty;
        /// <summary>密码明文。由视图的 PasswordBox 在点击登录时填入后立刻清空。</summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        /// <summary>提示信息清单（成功/失败原因）</summary>
        public ObservableCollection<string> Messages { get; } = new();

        public bool IsLoggedIn => _session.IsLoggedIn;

        public string CurrentUserText => _session.IsLoggedIn
            ? $"当前用户：{_session.UserName}（{_session.RoleText}）"
            : "当前未登录（只读模式）";

        public DelegateCommand LoginCommand { get; }
        public DelegateCommand LogoutCommand { get; }

        private void ExecuteLogin()
        {
            Messages.Clear();

            try
            {
                if (_session.Login(UserName, Password))
                {
                    Messages.Add($"登录成功：{_session.UserName}（{_session.RoleText}）");
                    Password = string.Empty;
                }
                else
                {
                    Messages.Add(_session.LastError ?? "登录失败：用户名或密码错误");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "登录异常");
                Messages.Add("登录异常：" + ex.Message);
            }

            Refresh();
        }

        private void ExecuteLogout()
        {
            _session.Logout();
            Password = string.Empty;
            Messages.Clear();
            Messages.Add("已注销，当前为只读模式");
            Refresh();
        }

        /// <summary>刷新角色相关显示</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(CurrentUserText));
        }

        #region IDialogAware

        public string Title => "权限登录";

        public DialogCloseListener RequestClose { get; } = new();

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            Password = string.Empty;
            _logger.Debug("权限登录对话框已关闭");
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            Messages.Clear();
            Refresh();
        }

        /// <summary>供视图调用关闭对话框</summary>
        public void Close(ButtonResult result) => RequestClose.Invoke(new DialogResult(result));

        #endregion
    }
}
