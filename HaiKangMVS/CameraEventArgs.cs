using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{
    /// <summary>
    /// 相机事件参数类
    /// </summary>
    public class CameraEventArgs : EventArgs
    {
        public CameraEventArgs(string cameraId, Bitmap image = null, string message = null)
        {
            CameraId = cameraId;
            Image = image;
            Message = message;
        }

        /// <summary>
        /// 相机ID
        /// </summary>
        public string CameraId { get; }

        /// <summary>
        /// 采集图像
        /// </summary>
        public Bitmap Image { get; }

        /// <summary>
        /// 传递消息
        /// </summary>
        public string Message { get; }

    }



}
