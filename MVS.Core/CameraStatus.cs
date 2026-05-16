using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVS.Core
{
    /// <summary>
    /// 相机状态枚举
    /// </summary>
    public enum CameraStatus
    {
        /// <summary>
        /// 相机已断开连接
        /// </summary>
        Disconnected,

        /// <summary>
        /// 相机已连接但未采集
        /// </summary>
        Connected,

        /// <summary>
        /// 相机正在采集图像
        /// </summary>
        Grabbing,

        /// <summary>
        /// 相机采集停止
        /// </summary>
        StopGrabbing


    }
}
