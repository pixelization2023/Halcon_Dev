using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaiKangMVS
{
    /// <summary>
    /// 相机控制器接口，定义了相机的基本操作和事件。
    /// </summary>
    public interface ICameraController : IDisposable
    {
        /// <summary>
        /// 相机唯一标识符。
        /// </summary>
        string CameraId { get; }

        /// <summary>
        /// 相机当前状态。
        /// </summary>
        CameraStatus Status { get; }

        /// <summary>
        /// 相机信息。
        /// </summary>
        CameraInfo Info { get; }

        /// <summary>
        /// 相机事件（如采集完成、错误等）。
        /// </summary>
        event CameraEventHandler CameraEvent;

        /// <summary>
        /// 初始化相机。
        /// </summary>
        void Initialize();

        /// <summary>
        /// 打开相机连接。
        /// </summary>
        void Open();

        /// <summary>
        /// 配置相机参数。
        /// </summary>
        /// <param name="config">相机配置参数，可选。</param>
        void Configure(CameraConfiguration config = null);
        /// <summary>
        /// 获取相机配置参数。
        /// </summary>

        void Getfigure(out CameraConfiguration config);

        /// <summary>
        /// 开始连续采集图像。
        /// </summary>
        void StartGrabbing();

        /// <summary>
        /// 停止连续采集图像。
        /// </summary>
        void StopGrabbing();

        /// <summary>
        /// 采集单帧图像。
        /// </summary>
        /// <returns>采集到的图像。</returns>
        Bitmap GrabSingleFrame();

        /// <summary>
        /// 异步采集单帧图像。
        /// </summary>
        /// <returns>采集到的图像任务。</returns>
        Task<Bitmap> GrabSingleFrameAsync();

        /// <summary>
        /// 连续采集指定数量的图像。
        /// </summary>
        /// <param name="count">采集的图像数量。</param>
        void CaptureImages(int count);

        /// <summary>
        /// 关闭相机连接。
        /// </summary>
        void Close();
        void ForceStop();
    }
}
