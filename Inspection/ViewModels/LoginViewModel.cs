using Inspection.Models;
using Inspection.Services;
using Prism.Commands;
using Serilog;

namespace Inspection.ViewModels
{
    /// <summary>
    /// 权限登录视图模型。
    /// 迁移自 窗体.UI.FrmPower —— 原窗体把用户名和口令硬编码在按钮事件里，
    /// 这里改为 ViewModel + 账号服务，并保留原有默认口令。
    /// </summary>
    public class LoginViewModel : BindableBase
    {
        private readonly UserSessionService _session;
        private readonly ILogger _logger;

        public LoginViewModel(UserSessionService session, ILogger logger)
        {
            _session = session;
            _logger = logger.ForContext<LoginViewModel>();

            _userName = "工程师";
            LoginCommand = new DelegateCommand(ExecuteLogin);
            LogoutCommand = new DelegateCommand(ExecuteLogout);
        }

        /// <summary>可选账号（与 UserSessionService 的默认账号一致）</summary>
        public IReadOnlyList<string> Accounts { get; } = new[] { "工程师", "操作员", "管理员" };

        private string _userName;
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private string? _message;
        public string? Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public UserRole Role => _session.Role;
        public bool IsLoggedIn => _session.IsLoggedIn;
        public string CurrentUserText => _session.IsLoggedIn ? $"{_session.UserName}（{_session.Role}）" : "未登录";

        public DelegateCommand LoginCommand { get; }
        public DelegateCommand LogoutCommand { get; }

        private void ExecuteLogin()
        {
            try
            {
                if (_session.Login(UserName, Password))
                {
                    Message = "登录成功";
                    Password = string.Empty;
                }
                else
                {
                    Message = _session.LastError ?? "登录失败";
                }

                RaisePropertyChanged(nameof(Role));
                RaisePropertyChanged(nameof(IsLoggedIn));
                RaisePropertyChanged(nameof(CurrentUserText));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "登录异常");
                Message = ex.Message;
            }
        }

        private void ExecuteLogout()
        {
            _session.Logout();
            Password = string.Empty;
            Message = "已注销";

            RaisePropertyChanged(nameof(Role));
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(CurrentUserText));
        }

        /// <summary>供外部（如模块初始化）同步权限显示</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(Role));
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(CurrentUserText));
        }
    }
}
