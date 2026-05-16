using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{
    /// <summary>
    /// 相机状态枚举
    /// </summary>
    public enum CameraStatus
    {
        Disconnected,
        Connected,
        Grabbing,
        Error
    }
}
