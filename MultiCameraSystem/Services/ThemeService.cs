using MaterialDesignThemes.Wpf;
using MultiCameraSystem.Models;
using MultiCameraSystem.Themes;
using Serilog;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace MultiCameraSystem.Services
{
    /// <summary>
    /// 动态主题服务。
    ///
    /// 参考 MaterialDesignInXamlToolkit 源码（<c>PaletteHelper</c> / <c>ITheme</c> / <c>ThemeExtensions</c>）的做法：
    /// <list type="number">
    /// <item>用 <see cref="PaletteHelper"/> 取出当前 <see cref="ITheme"/>，
    /// 通过 <c>SetBaseTheme</c> + <c>SetPrimaryColor</c> + <c>SetSecondaryColor</c> 改色后 <c>SetTheme</c> 回去，
    /// 这样所有 MaterialDesign 控件（按钮、输入框、DataGrid、波纹…）会即时换色。</item>
    /// <item>同时按主色推导出一整套「语义画刷」（SurfaceBrush / CardBrush / TextBrush / TintBrush…），
    /// 就地修改 <see cref="Application.Current"/> 资源里画刷对象的 <c>Color</c>，
    /// 因为画刷实例没变，界面上所有 StaticResource 引用都会实时刷新，无需重启或重建视图。</item>
    /// </list>
    ///
    /// 原实现把所有颜色硬编码在 App.xaml 中（23 处），既不能换肤也不能跟随明暗主题，这里统一接管。
    /// </summary>
    public class ThemeService
    {
        private readonly ILogger _logger;
        private readonly PaletteHelper _paletteHelper = new();

        /// <summary>当前主题的语义色数量（用于日志核对）</summary>
        private int _appliedBrushCount;

        /// <summary>上次成功应用的「主题/明暗」键，用于跳过重复应用</summary>
        private string? _lastAppliedKey;

        public ThemeService(ILogger logger)
        {
            _logger = logger.ForContext<ThemeService>();
        }

        /// <summary>自定义主题（来自 appsettings.json，键为 custom-xxxxxxxx）</summary>
        private readonly List<ThemeDefinition> _customThemes = new();

        /// <summary>全部可选主题 = 内置主题 + 自定义主题</summary>
        public IReadOnlyList<ThemeDefinition> Themes
            => ThemeCatalog.All.Concat(_customThemes).ToList();

        /// <summary>内置主题</summary>
        public IReadOnlyList<ThemeDefinition> BuiltInThemes => ThemeCatalog.All;

        /// <summary>全部语义画刷键（「高级覆盖」界面用）</summary>
        public IReadOnlyList<string> SemanticBrushKeys => ThemeCatalog.SemanticBrushKeys;

        /// <summary>当前主题</summary>
        public ThemeDefinition Current { get; private set; } = ThemeCatalog.GetOrDefault(ThemeCatalog.DefaultKey);

        /// <summary>当前基础主题（Dark / Light）</summary>
        public bool IsDark { get; private set; } = true;

        /// <summary>主题变化通知</summary>
        public event EventHandler<ThemeDefinition>? ThemeChanged;

        /// <summary>可用主题列表发生变化（新增/删除自定义主题）</summary>
        public event EventHandler? ThemesChanged;

        /// <summary>
        /// 启动时初始化：从配置恢复自定义主题并应用当前主题。
        /// 必须早于 Shell 与各视图创建，否则 StaticResource 会先取到 App.xaml 的兜底色。
        /// </summary>
        public void Initialize(UISettings settings)
        {
            SetCustomThemes(settings.CustomThemes);

            // 兼容旧配置：以前 Theme 字段里存的其实是 "Dark"/"Light"
            var themeKey = settings.Theme;
            var baseTheme = settings.BaseTheme;

            if (IsBaseThemeName(themeKey))
            {
                baseTheme = themeKey;
                themeKey = ThemeCatalog.DefaultKey;
            }

            Apply(themeKey, baseTheme, persist: false);
        }

        /// <summary>重新载入自定义主题列表（新增/删除/改名后调用）</summary>
        public void SetCustomThemes(IEnumerable<CustomThemeSetting>? settings)
        {
            _customThemes.Clear();

            if (settings != null)
            {
                foreach (var setting in settings)
                {
                    if (setting == null) continue;
                    if (string.IsNullOrWhiteSpace(setting.Key)) setting.Key = ThemeCatalog.NewCustomKey();
                    _customThemes.Add(ThemeCatalog.FromSetting(setting));
                }
            }

            ThemesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>按 Key 查找主题（内置 + 自定义），找不到返回 null</summary>
        public ThemeDefinition? Find(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return ThemeCatalog.FindBuiltIn(key)
                   ?? _customThemes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 直接应用一个临时主题定义（用于外观设置页的「实时预览」，不写入配置）。
        /// </summary>
        public void ApplyTransient(ThemeDefinition definition, string? baseTheme)
        {
            if (definition == null) return;

            // 换掉 _lastAppliedKey，保证同样的键在颜色变化后也能重新应用
            _lastAppliedKey = null;
            ApplyDefinition(definition, baseTheme, "ApplyTransient");
        }

        /// <summary>用主色/次色 + 覆盖项构造一个自定义主题定义（不写入配置）</summary>
        public static ThemeDefinition BuildCustomDefinition(string? key, string displayName,
            string primary, string secondary, IReadOnlyDictionary<string, string>? overrides)
            => ThemeCatalog.FromSetting(new CustomThemeSetting
            {
                Key = string.IsNullOrWhiteSpace(key) ? ThemeCatalog.NewCustomKey() : key!,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "我的主题" : displayName,
                Primary = primary,
                Secondary = secondary,
                ColorOverrides = overrides is { Count: > 0 }
                    ? new Dictionary<string, string>(overrides, StringComparer.Ordinal)
                    : new Dictionary<string, string>()
            });

        /// <summary>切换主题</summary>
        /// <param name="themeKey">主题键</param>
        /// <param name="baseTheme">Dark / Light / System</param>
        /// <param name="persist">是否写回配置</param>
        public void Apply(string? themeKey, string? baseTheme, bool persist)
        {
            var definition = Find(themeKey) ?? ThemeCatalog.GetOrDefault(ThemeCatalog.DefaultKey);
            ApplyDefinition(definition, baseTheme, "Apply");
        }

        private void ApplyDefinition(ThemeDefinition definition, string? baseTheme, string caller)
        {
            var resolvedBase = ResolveBaseTheme(ParseBaseTheme(baseTheme));

            // 同一套主题 + 同一明暗重复应用时直接跳过，避免无谓的重建刷子与界面抖动
            var appliedKey = definition.Key + "/" + resolvedBase;
            if (appliedKey == _lastAppliedKey)
            {
                _logger.Debug("主题未变化，跳过重复应用: {Key}（调用方 {Caller}）", appliedKey, caller);
                return;
            }

            _lastAppliedKey = appliedKey;
            _logger.Debug("应用主题: {Key}（调用方 {Caller}）", appliedKey, caller);

            Current = definition;
            IsDark = resolvedBase == BaseTheme.Dark;

            try
            {
                ApplyMaterialDesignTheme(definition, resolvedBase);
                ApplySemanticBrushes(definition, IsDark);

                _logger.Information("主题已应用: {Theme} / {Base}（{Count} 个语义画刷）{Custom}",
                    definition.DisplayName, IsDark ? "Dark" : "Light", _appliedBrushCount,
                    definition.IsCustom ? "[自定义]" : string.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "应用主题失败: {Theme}", definition.DisplayName);

                // 兜底：默认主题画刷已从 App.xaml 移除，界面颜色完全依赖这里生成的资源，
                // 因此失败时必须回退一次默认主题，避免出现「整屏没有主题色」。
                if (!string.Equals(definition.Key, ThemeCatalog.DefaultKey, StringComparison.Ordinal))
                {
                    _lastAppliedKey = null;
                    ApplyDefinition(ThemeCatalog.GetOrDefault(ThemeCatalog.DefaultKey), baseTheme, "Fallback");
                    return;
                }
            }

            ThemeChanged?.Invoke(this, definition);
            MVS.Core.ThemePalette.NotifyChanged();
        }

        /// <summary>只切换明暗，不换主色</summary>
        public void ApplyBaseTheme(string? baseTheme, bool persist) => Apply(Current.Key, baseTheme, persist);

        #region MaterialDesign 主题

        private void ApplyMaterialDesignTheme(ThemeDefinition definition, BaseTheme baseTheme)
        {
            var theme = _paletteHelper.GetTheme();

            // MaterialDesignThemes 5.x 的官方写法：SetDarkTheme / SetLightTheme 扩展方法
            if (baseTheme == BaseTheme.Dark)
                theme.SetDarkTheme();
            else
                theme.SetLightTheme();

            var primary = ParseColor(definition.Primary, MediaColor.FromRgb(0x00, 0xBC, 0xD4));
            var secondary = ParseColor(definition.Secondary, MediaColor.FromRgb(0x26, 0xC6, 0xDA));

            // 浅色底上主色需要压暗一点，否则文字对比度不够
            theme.SetPrimaryColor(baseTheme == BaseTheme.Light ? Darken(primary, 0.10) : primary);
            theme.SetSecondaryColor(baseTheme == BaseTheme.Light ? Darken(secondary, 0.10) : secondary);

            _paletteHelper.SetTheme(theme);
        }

        #endregion

        #region 语义画刷

        private void ApplySemanticBrushes(ThemeDefinition definition, bool dark)
        {
            var palette = ComputePalette(definition, dark);

            // 把「语义键 → 颜色」登记到共享内核，供 LiveCharts 等需要代码构造画笔的场景取色
            MVS.Core.ThemePalette.Resolver = key =>
                palette.TryGetValue(key, out var c) ? c : Colors.Gray;

            _appliedBrushCount = 0;
            foreach (var kv in palette)
            {
                if (SetBrush(kv.Key, kv.Value))
                    _appliedBrushCount++;
            }

            // 渐变画刷（参考 Wally 的渐变卡片 / CTA 按钮）
            foreach (var (key, from, to, angle) in ComputeGradients(palette, definition))
            {
                if (SetGradientBrush(key, from, to, angle))
                    _appliedBrushCount++;
            }
        }

        /// <summary>
        /// 由已算好的语义色派生渐变色。
        /// 端点取主色/次色/功能色，保证换主题时渐变跟着变。
        /// 支持用 ColorOverrides 覆写某个渐变的起始色（终点自动提亮）。
        /// </summary>
        private static List<(string Key, MediaColor From, MediaColor To, double Angle)> ComputeGradients(
            IReadOnlyDictionary<string, MediaColor> map, ThemeDefinition definition)
        {
            MediaColor Get(string key) => map.TryGetValue(key, out var c) ? c : Colors.Gray;

            MediaColor End(MediaColor baseColor, double lift) => Lighten(baseColor, lift);

            MediaColor From(string key, MediaColor fallback)
            {
                if (definition.ColorOverrides.TryGetValue(key, out var hex) && TryParseHex(NormalizeHex(hex)) is { } c)
                    return c;
                return fallback;
            }

            return new List<(string, MediaColor, MediaColor, double)>
            {
                ("AccentGradientBrush",  From("AccentGradientBrush", Get("PrimaryLightBrush")),  Get("AccentBrush"), 45),
                ("PrimaryGradientBrush", From("PrimaryGradientBrush", Get("PrimaryLightBrush")), Get("PrimaryDarkBrush"), 45),
                ("SuccessGradientBrush", From("SuccessGradientBrush", Get("SuccessBrush")),      End(Get("SuccessBrush"), 0.14), 45),
                ("WarningGradientBrush", From("WarningGradientBrush", Get("WarningBrush")),      End(Get("WarningBrush"), 0.14), 45),
                ("DangerGradientBrush",  From("DangerGradientBrush", Get("ErrorBrush")),         End(Get("ErrorBrush"), 0.14), 45)
            };
        }

        private static string NormalizeHex(string hex)
            => hex.StartsWith('#') ? hex : "#" + hex;

        /// <summary>
        /// 写入渐变画刷。
        /// 渐变无法"就地改色"，所以这里直接替换实例；
        /// 界面上用 DynamicResource 引用，替换后会自动重新解析。
        /// </summary>
        private bool SetGradientBrush(string key, MediaColor from, MediaColor to, double angle)
        {
            var resources = Application.Current?.Resources;
            if (resources == null) return false;

            resources[key] = new LinearGradientBrush(from, to, angle);
            return true;
        }

        /// <summary>
        /// 生成预览色板（外观设置页用，纯计算、不触碰 UI），键名与语义画刷一致。
        /// </summary>
        public IReadOnlyDictionary<string, string> BuildPreview(ThemeDefinition definition, bool dark)
            => ComputePalette(definition, dark).ToDictionary(kv => kv.Key, kv => ToHex(kv.Value));

        /// <summary>把颜色转成 #AARRGGBB 字符串</summary>
        public static string ToHex(MediaColor color)
            => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>
        /// 由「主色 + 次色 + 明暗」推导出全部语义色。
        /// 这里集中了配色规则，界面与预览共享同一份结果，避免两处不一致。
        /// </summary>
        private static Dictionary<string, MediaColor> ComputePalette(ThemeDefinition definition, bool dark)
        {
            var primary = ParseColor(definition.Primary, MediaColor.FromRgb(0x00, 0xBC, 0xD4));
            var secondary = ParseColor(definition.Secondary, MediaColor.FromRgb(0x26, 0xC6, 0xDA));

            // 主色在浅色主题下压暗，保证在白色面板上的可读性
            var primaryOnSurface = dark ? primary : Darken(primary, 0.10);
            var secondaryOnSurface = dark ? secondary : Darken(secondary, 0.10);

            var success = dark ? MediaColor.FromRgb(0x00, 0xE6, 0x76) : MediaColor.FromRgb(0x2E, 0x7D, 0x32);
            var warning = dark ? MediaColor.FromRgb(0xFF, 0xAB, 0x40) : MediaColor.FromRgb(0xEF, 0x6C, 0x00);
            var error = dark ? MediaColor.FromRgb(0xFF, 0x52, 0x52) : MediaColor.FromRgb(0xC6, 0x28, 0x28);

            var map = new Dictionary<string, MediaColor>(StringComparer.Ordinal);

            // ---- 主色系 ----
            map["PrimaryBrush"] = primaryOnSurface;
            map["PrimaryDarkBrush"] = Darken(primaryOnSurface, 0.15);
            map["PrimaryLightBrush"] = Lighten(primaryOnSurface, dark ? 0.18 : 0.08);
            map["AccentBrush"] = secondaryOnSurface;
            map["AccentDarkBrush"] = Darken(secondaryOnSurface, 0.15);
            map["TextAccentBrush"] = dark ? Lighten(primary, 0.25) : primaryOnSurface;

            // ---- 面：画布 → 主体 → 卡片 → 悬浮（参考 Radiograph：近黑底 + 稍亮卡片 + 1px 弱边框）----
            var cardBorder = dark ? Hsl(primary, 0.14, 0.22) : Hsl(primary, 0.12, 0.87);

            if (dark)
            {
                map["CanvasBrush"] = Hsl(primary, 0.45, 0.045);
                map["SurfaceBrush"] = Hsl(primary, 0.30, 0.105);
                map["SurfaceDarkBrush"] = Hsl(primary, 0.40, 0.055);
                map["CardBrush"] = Hsl(primary, 0.26, 0.135);
                map["SurfaceLightBrush"] = Hsl(primary, 0.28, 0.155);
                map["SurfaceElevatedBrush"] = Hsl(primary, 0.26, 0.18);
                map["CardBorderBrush"] = cardBorder;
                map["ChipBrush"] = Hsl(primary, 0.24, 0.19);
            }
            else
            {
                map["CanvasBrush"] = Hsl(primary, 0.14, 0.955);
                map["SurfaceBrush"] = Hsl(primary, 0.18, 0.975);
                map["SurfaceDarkBrush"] = Hsl(primary, 0.12, 0.93);
                map["CardBrush"] = Colors.White;
                map["SurfaceLightBrush"] = Colors.White;
                map["SurfaceElevatedBrush"] = Colors.White;
                map["CardBorderBrush"] = cardBorder;
                map["ChipBrush"] = Hsl(primary, 0.16, 0.955);
            }

            map["BorderSubtleBrush"] = WithAlpha(cardBorder, 0x99);

            // ---- 左侧导航栏：深浅主题下都保持深色（参考 Wally / ZR）----
            map["NavRailBrush"] = dark ? Hsl(primary, 0.45, 0.055) : Hsl(primary, 0.40, 0.11);
            map["NavRailActiveBrush"] = WithAlpha(primary, dark ? (byte)0x2E : (byte)0x3D);
            map["NavRailTextBrush"] = dark ? Hsl(primary, 0.10, 0.72) : Hsl(primary, 0.08, 0.78);

            // ---- 数据可视化 ----
            map["DataTrackBrush"] = WithAlpha(primaryOnSurface, 0x24);
            map["DataBarBrush"] = dark ? Lighten(primary, 0.06) : primaryOnSurface;

            // ---- 文字色系 ----
            if (dark)
            {
                map["TextPrimaryBrush"] = Colors.White;
                map["TextSecondaryBrush"] = Hsl(primary, 0.10, 0.76);
                map["TextMutedBrush"] = Hsl(primary, 0.08, 0.52);
            }
            else
            {
                map["TextPrimaryBrush"] = Hsl(primary, 0.30, 0.13);
                map["TextSecondaryBrush"] = Hsl(primary, 0.12, 0.35);
                map["TextMutedBrush"] = Hsl(primary, 0.10, 0.55);
            }

            // ---- 强调色上的文字：按主色亮度自动取深/浅 ----
            map["TextOnAccentBrush"] = (0.299 * primary.R + 0.587 * primary.G + 0.114 * primary.B) / 255.0 > 0.62
                ? MediaColor.FromRgb(0x14, 0x18, 0x20)
                : Colors.White;

            // ---- 功能色 ----
            map["SuccessBrush"] = success;
            map["WarningBrush"] = warning;
            map["ErrorBrush"] = error;
            map["ConnectedBrush"] = success;
            map["DisconnectedBrush"] = error;
            map["GrabbingBrush"] = Lighten(primary, dark ? 0.20 : 0.05);

            // ---- 半透明色（图标底纹 / 遮罩 / 光晕 / 阴影）----
            map["PrimaryTintBrush"] = WithAlpha(primaryOnSurface, 0x1A);
            map["SecondaryTintBrush"] = WithAlpha(secondaryOnSurface, 0x1A);
            map["SuccessTintBrush"] = WithAlpha(success, 0x22);
            map["WarningTintBrush"] = WithAlpha(warning, 0x22);
            map["ErrorTintBrush"] = WithAlpha(error, 0x22);
            map["ErrorTintStrongBrush"] = WithAlpha(error, 0x33);
            map["GlowBrush"] = WithAlpha(primary, 0x1A);
            map["GlowStrongBrush"] = WithAlpha(primary, 0x33);
            map["ScrimBrush"] = WithAlpha(Colors.Black, dark ? (byte)0x99 : (byte)0x33);
            map["ShadowBrush"] = WithAlpha(Colors.Black, dark ? (byte)0xCC : (byte)0x40);

            // 自定义主题的高级覆盖：逐项替换上面推导出的颜色
            foreach (var kv in definition.ColorOverrides)
                ApplyOverride(map, kv.Key, kv.Value);

            return map;
        }

        /// <summary>
        /// 应用单项颜色覆盖。
        /// 支持 #RRGGBB（保留原透明度）与 #AARRGGBB（连透明度一起覆盖，便于定制 Tint/Scrim）。
        /// </summary>
        private static void ApplyOverride(Dictionary<string, MediaColor> map, string key, string? hex)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(hex)) return;

            var value = hex!.Trim();
            if (!value.StartsWith('#')) value = "#" + value;

            var parsed = TryParseHex(value);
            if (parsed == null) return;

            var color = parsed.Value;

            // 6 位十六进制：沿用推导出的透明度
            if (value.Length == 7 && map.TryGetValue(key, out var existing))
                color = MediaColor.FromArgb(existing.A, color.R, color.G, color.B);

            map[key] = color;
        }

        private static MediaColor? TryParseHex(string value)
        {
            try
            {
                return (MediaColor)ColorConverter.ConvertFromString(value);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 写入 Application 资源。
        /// 画刷未冻结时**就地改 Color**（实例不变 → 已加载的界面实时刷新）；
        /// 冻结或不存在时才替换实例。
        /// </summary>
        private bool SetBrush(string key, MediaColor color)
        {
            var resources = Application.Current?.Resources;
            if (resources == null) return false;

            if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                if (brush.Color != color) brush.Color = color;
                return true;
            }

            resources[key] = new SolidColorBrush(color);
            return true;
        }

        #endregion

        #region 颜色推导

        private static BaseTheme ParseBaseTheme(string? value)
            => value switch
            {
                null => BaseTheme.Dark,
                _ when value.Equals("Light", StringComparison.OrdinalIgnoreCase) => BaseTheme.Light,
                _ when value.Equals("System", StringComparison.OrdinalIgnoreCase) => BaseTheme.Inherit,
                _ when value.Equals("Inherit", StringComparison.OrdinalIgnoreCase) => BaseTheme.Inherit,
                _ => BaseTheme.Dark
            };

        private static bool IsBaseThemeName(string? value)
            => value != null && (value.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                                 || value.Equals("Light", StringComparison.OrdinalIgnoreCase)
                                 || value.Equals("System", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 把 Dark/Light/System 解析为实际的 Dark 或 Light。
        /// System 通过注册表读取 Windows「应用模式」（0 = 深色，1 = 浅色），
        /// 等价于 MaterialDesign 内部 GetSystemTheme 的行为，但不依赖其内部类型。
        /// </summary>
        private static BaseTheme ResolveBaseTheme(BaseTheme requested)
        {
            if (requested != BaseTheme.Inherit) return requested;

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                if (key?.GetValue("AppsUseLightTheme") is int light)
                    return light == 0 ? BaseTheme.Dark : BaseTheme.Light;
            }
            catch
            {
                // 读注册表失败（受限环境）时按深色处理，与产品默认一致
            }

            return BaseTheme.Dark;
        }

        private static MediaColor ParseColor(string hex, MediaColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;

            try
            {
                return (MediaColor)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private static MediaColor WithAlpha(MediaColor color, byte alpha)
            => MediaColor.FromArgb(alpha, color.R, color.G, color.B);

        /// <summary>按 H/S/L 生成颜色，H 与 S 取自主题主色，保证整体色相统一</summary>
        private static MediaColor Hsl(MediaColor basedOn, double saturationFactor, double lightness)
        {
            var (h, s, _) = ToHsl(basedOn);
            return FromHsl(h, Math.Clamp(s * saturationFactor, 0, 1), Math.Clamp(lightness, 0, 1));
        }

        private static MediaColor Lighten(MediaColor color, double amount)
        {
            var (h, s, l) = ToHsl(color);
            return FromHsl(h, s, Math.Clamp(l + amount, 0, 1));
        }

        private static MediaColor Darken(MediaColor color, double amount)
        {
            var (h, s, l) = ToHsl(color);
            return FromHsl(h, s, Math.Clamp(l - amount, 0, 1));
        }

        private static (double H, double S, double L) ToHsl(MediaColor color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double h = 0, s = 0;

            if (Math.Abs(max - min) > double.Epsilon)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;

                h /= 6.0;
            }

            return (h, s, l);
        }

        private static MediaColor FromHsl(double h, double s, double l)
        {
            if (s <= double.Epsilon)
            {
                var gray = (byte)Math.Round(l * 255);
                return MediaColor.FromRgb(gray, gray, gray);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            return MediaColor.FromRgb(
                (byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255),
                (byte)Math.Round(HueToRgb(p, q, h) * 255),
                (byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        #endregion
    }
}
