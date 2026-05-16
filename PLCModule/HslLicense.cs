using Serilog;

namespace PLCModule
{
    /// <summary>
    /// HslCommunication 组件授权注册。
    ///
    /// 背景：HslCommunication 是**商业组件**，不注册授权码会跑在试用模式（连接数/功能受限），
    /// 商用项目**必须**在创建任何通信对象之前完成注册：
    /// <code>HslCommunication.Authorization.SetAuthorizationCode("...")</code>
    ///
    /// 取值优先级（高 → 低）：
    /// <list type="number">
    /// <item>调用方传入的授权码（来自 appsettings.json 的 <c>Plc.HslAuthorizationCode</c>）</item>
    /// <item>环境变量 <c>HSL_AUTH_CODE</c>（便于把密钥放在源码/仓库之外）</item>
    /// <item>内置默认授权码（现场开箱即用）</item>
    /// </list>
    ///
    /// 该调用是幂等的：重复注册同一授权码直接返回，不会重复打日志。
    /// </summary>
    public static class HslLicense
    {
        /// <summary>内置默认授权码（可在 appsettings.json 或环境变量里覆盖）</summary>
        public const string DefaultAuthorizationCode = "2b3b2d73-01ff-4f68-b39f-fcc1bfb82b54";

        /// <summary>环境变量名（优先级高于内置默认值）</summary>
        public const string EnvironmentVariableName = "HSL_AUTH_CODE";

        private static readonly object Sync = new();
        private static bool _registered;
        private static string? _registeredCode;

        /// <summary>是否已成功注册</summary>
        public static bool IsRegistered
        {
            get { lock (Sync) return _registered; }
        }

        /// <summary>最近一次注册结果说明（界面/日志用）</summary>
        public static string LastMessage { get; private set; } = "尚未注册";

        /// <summary>
        /// 首次真实注册的结果（"授权成功" / "授权失败" / "未提供授权码"）。
        /// 由于注册发生在日志系统就绪之前，这里把结果留出来，供模块初始化时输出到正式日志。
        /// </summary>
        public static string? RegistrationDetail { get; private set; }

        /// <summary>
        /// 注册授权码。可在程序启动的任意早期阶段调用（幂等，内部加锁）。
        /// </summary>
        /// <param name="authorizationCode">授权码；为空则回退到环境变量/内置默认值</param>
        /// <param name="logger">可选日志</param>
        /// <returns>是否已处于"已注册"状态</returns>
        public static bool Initialize(string? authorizationCode = null, ILogger? logger = null)
        {
            lock (Sync)
            {
                var code = ResolveCode(authorizationCode);

                if (string.IsNullOrWhiteSpace(code))
                {
                    RegistrationDetail ??= "未提供授权码";
                    LastMessage = "未提供授权码，HslCommunication 将以试用模式运行";
                    (logger ?? Log.Logger).Warning("{Message}", LastMessage);
                    return false;
                }

                if (_registered && string.Equals(_registeredCode, code, StringComparison.Ordinal))
                {
                    // 幂等：同一授权码不重复注册，但仍记录一次状态（便于启动日志核对）
                    LastMessage = "HslCommunication 授权已注册";
                    var log = logger ?? Log.Logger;
                    log.Information("{Message}（授权码未变化，跳过重复注册）", LastMessage);
                    return true;
                }

                try
                {
                    var ok = HslCommunication.Authorization.SetAuthorizationCode(code);

                    _registered = ok;
                    _registeredCode = code;
                    RegistrationDetail = ok
                        ? "授权成功"
                        : "授权失败（请检查授权码是否正确、是否与本机绑定）";
                    LastMessage = ok
                        ? "HslCommunication 授权成功"
                        : "HslCommunication 授权失败（请检查授权码是否正确、是否与本机绑定）";

                    if (ok)
                        (logger ?? Log.Logger).Information("{Message}", LastMessage);
                    else
                        (logger ?? Log.Logger).Error("{Message}", LastMessage);

                    return ok;
                }
                catch (Exception ex)
                {
                    _registered = false;
                    LastMessage = $"HslCommunication 授权异常: {ex.Message}";
                    (logger ?? Log.Logger).Error(ex, "HslCommunication 授权异常");
                    return false;
                }
            }
        }

        private static string? ResolveCode(string? explicitCode)
        {
            if (!string.IsNullOrWhiteSpace(explicitCode)) return explicitCode!.Trim();

            var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            return DefaultAuthorizationCode;
        }
    }
}
