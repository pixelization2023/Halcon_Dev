using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace MultiCameraSystem.Convert
{
    public class BooleanAndConverter : IMultiValueConverter

    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return false;
            bool result = true;
             foreach(object value in values)
            {
               if(value is bool b)
                {
                    result = result && b; // 如果是布尔值，进行与操作
                }
                else
                {
                    return false; // 如果有非布尔值，直接返回false
                }
              
            }
            return result; // 返回最终结果
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // 多值绑定的反向转换在语义上无唯一解（true 无法反推出各分量），
            // 因此返回 Binding.DoNothing 表示"不写回源"，而不是抛 NotImplementedException ——
            // 后者一旦被 WPF 调用到就是界面白屏/运行期异常，且极难定位。
            var result = new object[targetTypes?.Length ?? 0];
            for (int i = 0; i < result.Length; i++)
                result[i] = Binding.DoNothing;
            return result;
        }
    }
}
