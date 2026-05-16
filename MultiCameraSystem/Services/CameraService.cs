using MultiCameraSystem.Services.Interfaces;
using MVS.Core;
using Serilog;

namespace MultiCameraSystem.Services
{
    public class CameraService : ICameraService, IDisposable
    {
        private readonly ICameraManager _cameraManager;
        private readonly ILogger _logger;
        private readonly Dictionary<string, CameraDevice> _cameras = new();
        private bool _initialized;

        public IEnumerable<CameraInfo> AvailableCameras { get; private set; } =
            Enumerable.Empty<CameraInfo>();

        public CameraService(ICameraManager cameraManager, ILogger logger)
        {
            _cameraManager = cameraManager;
            _logger = logger.ForContext<CameraService>();
        }

        public CameraDevice GetCamera(string serialNumber)
        {
            if (!_initialized)
                throw new InvalidOperationException("相机服务未初始化，请先调用Initialize方法。");

            if (_cameras.TryGetValue(serialNumber, out var camera))
                return camera;

            var cameraInfo = AvailableCameras.FirstOrDefault(c => c.SerialNumber == serialNumber);
            if (cameraInfo == null)
            {
                _logger.Warning("未找到序列号为 {SerialNumber} 的相机", serialNumber);
                throw new CameraException($"未找到序列号为 {serialNumber} 的相机。");
            }

            var newCamera = new CameraDevice(cameraInfo);
            _cameras.Add(serialNumber, newCamera);
            _logger.Information("创建相机实例: {Model} (S/N: {SerialNumber})", cameraInfo.ModelName, serialNumber);

            return newCamera;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _cameraManager.InitializeSDK();
            AvailableCameras = _cameraManager.EnumerateCameras().ToList();
            _initialized = true;
            _logger.Information("相机服务初始化完成，发现 {Count} 台相机", AvailableCameras.Count());
        }

        public void Shutdown()
        {
            if (!_initialized) return;

            foreach (var camera in _cameras.Values)
            {
                camera.Disconnect();
                camera.Dispose();
            }
            _cameras.Clear();
            _cameraManager.FinalizeSDK();
            _initialized = false;
            _logger.Information("相机服务已关闭");
        }

        public void Dispose() => Shutdown();

        public void RefreshCameras()
        {
            if (!_initialized)
                throw new InvalidOperationException("相机服务未初始化，请先调用Initialize方法。");
            AvailableCameras = _cameraManager.EnumerateCameras().ToList();
            _logger.Debug("相机列表已刷新，当前 {Count} 台", AvailableCameras.Count());
        }
    }
}
