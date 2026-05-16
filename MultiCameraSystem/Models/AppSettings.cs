using System.Text.Json.Serialization;

namespace MultiCameraSystem.Models
{
    public class AppSettings
    {
        public GeneralSettings General { get; set; } = new();
        public CameraDefaultsSettings CameraDefaults { get; set; } = new();
        public PLCSettings Plc { get; set; } = new();
        public MESSettings Mes { get; set; } = new();
        public UISettings UI { get; set; } = new();
    }

    public class GeneralSettings
    {
        public string ImageSavePath { get; set; } = @"D:\Captures";
        public string LogPath { get; set; } = @"Logs";
        public int LogRetentionDays { get; set; } = 30;
        public bool AutoStartCamera { get; set; } = true;
    }

    public class CameraDefaultsSettings
    {
        public float ExposureTime { get; set; } = 5000f;
        public float GainValue { get; set; } = 20f;
        public int GrabTimeout { get; set; } = 1000;
        public int BufferCount { get; set; } = 5;
        public string ImageFormat { get; set; } = "Bmp";
    }

    public class PLCSettings
    {
        /// <summary>
        /// HslCommunication 授权码。
        /// 留空 = 使用 PLCModule.HslLicense 的内置默认值（也可用环境变量 HSL_AUTH_CODE 覆盖，
        /// 便于把密钥放在源码/仓库之外）。
        /// </summary>
        public string HslAuthorizationCode { get; set; } = "";

        public string IpAddress { get; set; } = "192.168.1.100";
        public int Port { get; set; } = 502;
        public string Protocol { get; set; } = "ModbusTcp";
        public int ConnectTimeoutMs { get; set; } = 3000;
        public int ReadWriteTimeoutMs { get; set; } = 2000;
        public int ReconnectIntervalMs { get; set; } = 5000;
        public int HeartbeatIntervalMs { get; set; } = 2000;
        public string HeartbeatAddress { get; set; } = "D0";
    }

    public class MESSettings
    {
        public string BaseUrl { get; set; } = "http://localhost:5000/api";
        public string ApiKey { get; set; } = "";
        public string LineId { get; set; } = "LINE-01";
        public string StationId { get; set; } = "CCD-01";
        public int TimeoutSeconds { get; set; } = 10;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
    }

    public class UISettings
    {
        public bool AutoScrollLogs { get; set; } = true;

        /// <summary>
        /// 主题键（见 <see cref="Themes.ThemeCatalog"/>）：内置主题用 Cyan / Blue / Amber…，
        /// 自定义主题用 <c>custom-xxxxxxxx</c>（见 <see cref="CustomThemes"/>）。
        /// 兼容旧配置：若填的是 Dark/Light/System，会被当作 BaseTheme 处理并使用默认主题。
        /// </summary>
        public string Theme { get; set; } = "Cyan";

        /// <summary>基础明暗主题：Dark / Light / System</summary>
        public string BaseTheme { get; set; } = "Dark";

        public string Language { get; set; } = "zh-CN";

        /// <summary>用户自定义主题（主色/次色 + 可选的逐项颜色覆盖）</summary>
        public List<CustomThemeSetting> CustomThemes { get; set; } = new();
    }

    /// <summary>
    /// 用户自定义主题。
    /// 只需要给「主色 + 次色」，其余 31 个语义色仍由 ThemeService 按 Material Design 规则推导；
    /// 想微调某几个语义色时，用 <see cref="ColorOverrides"/> 按画刷键覆盖即可。
    /// </summary>
    public class CustomThemeSetting
    {
        /// <summary>主题键，形如 custom-3f7a1b2c</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>显示名（界面与切换菜单里用）</summary>
        public string DisplayName { get; set; } = "我的主题";

        /// <summary>主色（#RRGGBB）</summary>
        public string Primary { get; set; } = "#00BCD4";

        /// <summary>次色 / 强调色（#RRGGBB）</summary>
        public string Secondary { get; set; } = "#26C6DA";

        /// <summary>
        /// 高级覆盖：语义画刷键 → 颜色（#RRGGBB 或 #AARRGGBB）。
        /// 支持 #AARRGGBB 以便直接控制透明度（如 PrimaryTintBrush）。
        /// </summary>
        public Dictionary<string, string> ColorOverrides { get; set; } = new();

        public CustomThemeSetting Clone() => new()
        {
            Key = Key,
            DisplayName = DisplayName,
            Primary = Primary,
            Secondary = Secondary,
            ColorOverrides = new Dictionary<string, string>(ColorOverrides)
        };
    }
}
