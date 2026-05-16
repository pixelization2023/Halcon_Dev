using System.Windows.Media;

namespace MVS.Core
{
    /// <summary>
    /// 主题色共享通道（共享内核）。
    ///
    /// 背景：主题色由 MultiCameraSystem 的 <c>ThemeService</c> 在运行期推导并写入
    /// Application.Resources，界面用 DynamicResource 自动跟随。但**图表库**（LiveCharts）
    /// 的画笔必须由代码构造（SolidColorPaint），而各个模块（Inspection / WorkBench / MES /
    /// PLC）都不能反向引用外壳工程 MultiCameraSystem。
    ///
    /// 所以这里放一个极薄的桥：ThemeService 每次应用主题时把「语义键 → 颜色」的解析器登记进来，
    /// 任何模块都能取到**当前主题的颜色**，并在主题变化时收到通知重建图表。
    /// 与既有的 <see cref="AppContainer"/> 一样，属于共享内核里约定的桥接点。
    /// </summary>
    public static class ThemePalette
    {
        /// <summary>颜色解析器，由 ThemeService 在应用主题时设置</summary>
        public static Func<string, Color>? Resolver { get; set; }

        /// <summary>主题变化通知（订阅方据此重建图表画笔）</summary>
        public static event EventHandler? Changed;

        /// <summary>取当前主题的语义色；解析器未就绪时回落到灰色，保证不崩</summary>
        public static Color Get(string key)
            => Resolver?.Invoke(key) ?? Colors.Gray;

        /// <summary>主题已变化（由 ThemeService 调用）</summary>
        public static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);
    }
}
