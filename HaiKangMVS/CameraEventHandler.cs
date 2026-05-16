using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{
    /// <summary>
    /// 相机事件处理器委托
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"> 相机事件参数类</param>
    public delegate void CameraEventHandler(object sender, CameraEventArgs e);
}
