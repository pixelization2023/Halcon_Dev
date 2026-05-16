using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{

    /// <summary>
    /// 相机配置类，用于设置相机的各项参数
    /// </summary>
    public class CameraConfiguration
    {
        /// <summary>
        /// 是否优化数据包大小
        /// </summary>
        public bool OptimizePacketSize { get; set; } = true;

        /// <summary>
        /// 触发模式（0为关闭，1为开启）
        /// </summary>
        public uint TriggerMode { get; set; } = 0;

        /// <summary>
        /// 抓取超时时间（毫秒）
        /// </summary>
        public int GrabTimeout { get; set; } = 1000;

        /// <summary>
        /// 缓冲区数量
        /// </summary>
        public int BufferCount { get; set; } = 5;

        /// <summary>
        /// 图像格式
        /// </summary>
        public ImageFormat ImageFormat { get; set; } = ImageFormat.Bmp;

        /// <summary>
        /// 图像文件名前缀
        /// </summary>
        public string ImagePrefix { get; set; } = "capture";



        /// <summary>
        /// 曝光时间
        /// </summary>
        public float ExposureTime { get; set; } = 5000f; // 默认值为10000微秒（10毫秒）


        /// <summary>
        /// 增益值
        /// </summary>
        public float GainValue { get; set; } = 20f; // 默认值为100


        /// <summary>
        /// 帧率
        /// </summary>
        public float FrameRate { get; set; }

    }
}
