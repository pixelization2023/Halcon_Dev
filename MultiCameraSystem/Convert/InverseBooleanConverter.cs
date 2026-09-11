using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace MultiCameraSystem.Convert
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            else
            {
                return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 取反是自反的，直接复用 Convert 即可。
            // 旧实现抛 NotImplementedException —— 只要这个转换器被用在 TwoWay 绑定上，
            // 运行期就会异常。这里给出正确实现而不是留个定时炸弹。
            return Convert(value, targetType, parameter, culture);
        }
    }
}
