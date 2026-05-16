using MultiCameraSystem.Models;

namespace MultiCameraSystem.Themes
{
    /// <summary>
    /// 一套主题的静态描述。
    /// 参考 MaterialDesignInXamlToolkit 的 BundledTheme/Palette 做法：
    /// 只声明「主色 + 次色」，具体的背景/纸面/文字/描边色在运行期由
    /// <see cref="Services.ThemeService"/> 按 Material Design 的明暗规则推导，
    /// 这样新增一套主题只需要在这里加一行，也不必手写一整套画刷。
    ///
    /// 自定义主题（用户自己填主色/次色）与此结构一致，额外多一个
    /// <see cref="ColorOverrides"/> 用于逐项微调语义色。
    /// </summary>
    public sealed class ThemeDefinition
    {
        public ThemeDefinition(
            string key,
            string displayName,
            string description,
            string primary,
            string secondary,
            bool isCustom = false,
            IReadOnlyDictionary<string, string>? colorOverrides = null)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
            Primary = primary;
            Secondary = secondary;
            IsCustom = isCustom;
            ColorOverrides = colorOverrides ?? new Dictionary<string, string>();
        }

        /// <summary>主题键（写入 appsettings.json）</summary>
        public string Key { get; }

        /// <summary>显示名</summary>
        public string DisplayName { get; }

        /// <summary>一句话说明（界面用）</summary>
        public string Description { get; }

        /// <summary>主色（#RRGGBB）</summary>
        public string Primary { get; }

        /// <summary>次色 / 强调色（#RRGGBB）</summary>
        public string Secondary { get; }

        /// <summary>是否为用户自定义主题</summary>
        public bool IsCustom { get; }

        /// <summary>逐项颜色覆盖：语义画刷键 → #RRGGBB / #AARRGGBB</summary>
        public IReadOnlyDictionary<string, string> ColorOverrides { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>内置主题目录 + 自定义主题工厂</summary>
    public static class ThemeCatalog
    {
        /// <summary>默认主题键</summary>
        public const string DefaultKey = "Cyan";

        /// <summary>自定义主题键前缀</summary>
        public const string CustomKeyPrefix = "custom-";

        /// <summary>
        /// 全部语义画刷键（「高级覆盖」界面据此提供下拉选项）。
        /// 与 <c>ThemeService.ComputePalette</c> 产出的键保持一致。
        /// </summary>
        public static IReadOnlyList<string> SemanticBrushKeys { get; } = new[]
        {
            // 主色 / 强调
            "PrimaryBrush", "PrimaryDarkBrush", "PrimaryLightBrush", "AccentBrush", "AccentDarkBrush", "TextAccentBrush",
            // 面：画布 → 主体 → 卡片 → 悬浮，四级层次（参考 Radiograph / ZR 的深浅分层）
            "CanvasBrush", "SurfaceBrush", "SurfaceDarkBrush", "CardBrush", "SurfaceElevatedBrush", "CardBorderBrush",
            "BorderSubtleBrush", "ChipBrush",
            // 左侧导航栏专用（深浅主题下都保持深色，参考 Wally / ZR）
            "NavRailBrush", "NavRailActiveBrush", "NavRailTextBrush",
            // 文字
            "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "TextOnAccentBrush",
            // 功能色
            "SuccessBrush", "WarningBrush", "ErrorBrush", "ConnectedBrush", "DisconnectedBrush", "GrabbingBrush",
            // 数据可视化
            "DataTrackBrush", "DataBarBrush",
            // 底纹 / 光晕 / 遮罩
            "PrimaryTintBrush", "SecondaryTintBrush", "SuccessTintBrush", "WarningTintBrush", "ErrorTintBrush",
            "ErrorTintStrongBrush", "GlowBrush", "GlowStrongBrush", "ScrimBrush", "ShadowBrush",
            // 渐变（参考 Wally 的渐变卡片与 CTA 按钮）
            "AccentGradientBrush", "PrimaryGradientBrush", "SuccessGradientBrush", "WarningGradientBrush", "DangerGradientBrush"
        };

        private static readonly ThemeDefinition[] Definitions =
        {
            new("Cyan",      "科技青",   "经典工业视觉配色，长时间盯屏不易疲劳", "#00BCD4", "#26C6DA"),
            new("Blue",      "深海蓝",   "沉稳冷静，适合与蓝色设备外壳搭配",     "#2196F3", "#03A9F4"),
            new("Indigo",    "靛青",     "偏冷的深色调，暗光车间观感更好",       "#3F51B5", "#536DFE"),
            new("Teal",      "青碧",     "青绿过渡色，兼顾科技感与柔和度",       "#009688", "#26A69A"),
            new("Green",     "极光绿",   "高对比绿色，快速识别 OK/NG 状态",      "#43A047", "#66BB6A"),
            new("Amber",     "琥珀",     "暖色调，适合明亮车间环境",             "#FB8C00", "#FFA726"),
            new("DeepOrange","焰橙",     "强提示感，适合报警信息较多的产线",     "#F4511E", "#FF7043"),
            new("Pink",      "品红",     "高辨识度，用于多机台区分",             "#E91E63", "#F06292"),
            new("Purple",    "紫罗兰",   "低饱和紫，视觉上更「现代」",           "#9C27B0", "#AB47BC"),
            new("BlueGrey",  "石墨灰",   "几乎中性的工业灰，干扰最小",           "#546E7A", "#78909C"),
            new("Red",       "警示红",   "强警示主色，适合安全等级高的工站",     "#E53935", "#EF5350"),
            new("Brown",     "青铜",     "复古工业风，弱光环境下对比适中",       "#6D4C41", "#8D6E63")
        };

        /// <summary>全部内置主题</summary>
        public static IReadOnlyList<ThemeDefinition> All => Definitions;

        /// <summary>按 Key 查找内置主题，找不到返回 null</summary>
        public static ThemeDefinition? FindBuiltIn(string? key)
            => string.IsNullOrWhiteSpace(key)
                ? null
                : Definitions.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>是否内置主题键</summary>
        public static bool IsBuiltInKey(string? key) => FindBuiltIn(key) != null;

        /// <summary>是否自定义主题键</summary>
        public static bool IsCustomKey(string? key)
            => !string.IsNullOrWhiteSpace(key) &&
               key.StartsWith(CustomKeyPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>生成一个新的自定义主题键</summary>
        public static string NewCustomKey()
            => CustomKeyPrefix + Guid.NewGuid().ToString("N")[..8];

        /// <summary>把用户配置转换为主题定义</summary>
        public static ThemeDefinition FromSetting(CustomThemeSetting setting)
        {
            var overrides = setting.ColorOverrides is { Count: > 0 }
                ? new Dictionary<string, string>(setting.ColorOverrides, StringComparer.Ordinal)
                : new Dictionary<string, string>();

            return new ThemeDefinition(
                string.IsNullOrWhiteSpace(setting.Key) ? NewCustomKey() : setting.Key!,
                string.IsNullOrWhiteSpace(setting.DisplayName) ? "我的主题" : setting.DisplayName!,
                "自定义主题",
                string.IsNullOrWhiteSpace(setting.Primary) ? "#00BCD4" : setting.Primary!,
                string.IsNullOrWhiteSpace(setting.Secondary) ? "#26C6DA" : setting.Secondary!,
                isCustom: true,
                colorOverrides: overrides);
        }

        /// <summary>取主题，找不到时回退到默认主题</summary>
        public static ThemeDefinition GetOrDefault(string? key) => FindBuiltIn(key) ?? Definitions[0];
    }
}
