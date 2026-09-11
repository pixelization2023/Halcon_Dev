using Inspection.Models;
using Prism.Mvvm;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 权限与登录会话。
    /// 迁移自 窗体.UI.FrmPower —— 原实现把用户名硬编码在窗体里，这里改为可配置账号表，
    /// 并保留原默认口令（工程师 666666 / 操作员 5588）以便现场平滑过渡。
    /// </summary>
    public class UserSessionService : BindableBase
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, (string Password, UserRole Role)> _accounts = new(StringComparer.OrdinalIgnoreCase);

        public UserSessionService(ILogger logger)
        {
            _logger = logger.ForContext<UserSessionService>();

            // 默认账号（与 窗体.FrmPower 一致）
            _accounts["工程师"] = ("666666", UserRole.Engineer);
            _accounts["操作员"] = ("5588", UserRole.Operator);
            _accounts["管理员"] = ("admin888", UserRole.Administrator);
        }

        private UserRole _role = UserRole.None;
        /// <summary>当前角色</summary>
        public UserRole Role
        {
            get => _role;
            private set
            {
                if (SetProperty(ref _role, value))
                {
                    RaisePropertyChanged(nameof(IsLoggedIn));
                    RaisePropertyChanged(nameof(IsEngineer));
                    RaisePropertyChanged(nameof(IsOperator));
                    RaisePropertyChanged(nameof(CurrentUserRole));
                    RaisePropertyChanged(nameof(RoleText));
                    RoleChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// 可选账号名（供登录界面下拉）。
        /// 说明：旧实现里登录页有一个账号下拉，但它绑定的属性在 ViewModel 上**并不存在**
        ///（只有 LoginViewModel 里一个硬编码数组），换个界面就退化成"空白下拉"。
        /// 账号清单的唯一真源放在这里，界面只消费。
        /// </summary>
        public IReadOnlyList<string> AccountNames => _accounts.Keys.ToList();

        /// <summary>角色序号，供导航项按角色过滤使用（None=0 &lt; Operator &lt; Engineer &lt; Administrator）</summary>
        public int CurrentUserRole => (int)Role;

        /// <summary>角色显示文本</summary>
        public string RoleText => Role switch
        {
            UserRole.Operator => "操作员",
            UserRole.Engineer => "工程师",
            UserRole.Administrator => "管理员",
            _ => "未登录"
        };

        private string _userName = string.Empty;
        /// <summary>当前登录用户名</summary>
        public string UserName
        {
            get => _userName;
            private set => SetProperty(ref _userName, value);
        }

        public bool IsLoggedIn => Role != UserRole.None;
        public bool IsEngineer => Role >= UserRole.Engineer;
        public bool IsOperator => Role >= UserRole.Operator;

        public event EventHandler<UserRole>? RoleChanged;

        /// <summary>登录，成功返回 true</summary>
        public bool Login(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                _logger.Warning("登录失败：用户名为空");
                return false;
            }

            if (!_accounts.TryGetValue(userName.Trim(), out var account) || account.Password != password)
            {
                _logger.Warning("登录失败：用户名或密码错误 ({User})", userName);
                LastError = "密码错误";
                return false;
            }

            Role = account.Role;
            UserName = userName.Trim();
            LastError = null;
            _logger.Information("用户 [{User}] 登录成功，角色 {Role}", UserName, Role);
            return true;
        }

        /// <summary>注销</summary>
        public void Logout()
        {
            _logger.Information("用户 [{User}] 已注销", UserName);
            UserName = string.Empty;
            Role = UserRole.None;
        }

        /// <summary>最近一次失败原因</summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// 判断角色能否编辑指定设置分组。
        /// 迁移自 窗体.FrmSetUp.FormSetUp_Load 中的页签过滤逻辑：
        /// 操作员只能看到「通用设置 / MES设置 / 存图设置」。
        /// </summary>
        public bool CanEditGroup(string group)
        {
            group = SettingsGroups.Normalize(group);

            if (Role == UserRole.Administrator || Role == UserRole.Engineer)
                return true;

            if (Role == UserRole.Operator)
            {
                return group == SettingsGroups.General
                    || group == SettingsGroups.Mes
                    || group == SettingsGroups.ImageSave;
            }

            return false;
        }
    }
}
