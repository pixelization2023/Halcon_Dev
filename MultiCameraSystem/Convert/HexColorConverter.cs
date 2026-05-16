using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiCameraSystem.Convert
{
    /// <summary>
    /// 「#RRGGBB 字符串」与 <see cref="Color"/> 之间互转。
    /// 主题配置里颜色以字符串持久化（便于手写 appsettings.json），
    /// 而 MaterialDesign 的 ColorPicker 绑定的是 Color，用它做桥接。
    /// </summary>
    public class HexColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try { return (Color)ColorConverter.ConvertFromString(hex); }
                catch { /* 非法值回落到透明 */ }
            }

            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            return Binding.DoNothing;
        }
    }
}
