using MVS.Core;
using MultiCameraSystem.Services.Interfaces;

namespace MultiCameraSystem.Services
{
    /// <summary>
    /// 把外壳的 <see cref="ICameraService"/> 适配成共享内核的只读契约 <see cref="ICameraInventory"/>，
    /// 供 Inspection 模块的"自检"读取相机状态（Inspection 不能引用本工程）。
    ///
    /// 这里只做字段投影，不复制任何设备对象：<see cref="CameraInfo"/> 本身就是内核类型。
    /// </summary>
    internal sealed class CameraInventoryAdapter : ICameraInventory
    {
        private readonly ICameraService _cameras;

        public CameraInventoryAdapter(ICameraService cameras)
            => _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));

        public bool IsInitialized => _cameras.IsInitialized;

        public string? InitializationError => _cameras.InitializationError;

        public IReadOnlyList<CameraInfo> Devices
            => _cameras.AvailableCameras?.ToList() ?? new List<CameraInfo>();
    }
}
