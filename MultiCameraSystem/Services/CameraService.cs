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

        /// <summary>相机 SDK 是否已成功初始化</summary>
        public bool IsInitialized => _initialized;

        /// <summary>SDK 初始化失败原因（成功时为 null）</summary>
        public string? InitializationError { get; private set; }

        public CameraDevice GetCamera(string serialNumber)
        {
            if (!_initialized)
                throw new InvalidOperationException(
                    InitializationError == null
                        ? "相机服务未初始化，请先调用Initialize方法。"
                        : $"相机服务未初始化：{InitializationError}");

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

        /// <summary>
        /// 初始化相机服务。
        /// 失败时**不再向外抛**：相机不可用属于可降级情形（整机仍可运行、可从文件跑检测），
        /// 但必须把原因记录下来，由界面的 SDK 状态区显示，而不是像以前那样"静默失败 +
        /// 之后 RefreshCameras 抛 InvalidOperationException"。
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                _cameraManager.InitializeSDK();
                AvailableCameras = _cameraManager.EnumerateCameras().ToList();
                _initialized = true;
                InitializationError = null;
                _logger.Information("相机服务初始化完成，发现 {Count} 台相机", AvailableCameras.Count());
            }
            catch (Exception ex)
            {
                _initialized = false;
                InitializationError = $"{ex.GetType().Name}: {ex.Message}";
                _logger.Error(ex, "相机 SDK 初始化失败（相机功能不可用，其余功能不受影响）");
            }
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

        /// <summary>
        /// 刷新相机列表。
        /// 未初始化时返回 false 并保留上次的列表，而不是抛异常 ——
        /// 抛异常会让"刷新"按钮看起来毫无反应（异常被 UI 层吞掉）。
        /// </summary>
        public bool TryRefreshCameras()
        {
            if (!_initialized)
            {
                _logger.Warning("相机 SDK 未初始化，无法刷新设备列表。原因: {Error}", InitializationError ?? "未知");
                return false;
            }

            try
            {
                AvailableCameras = _cameraManager.EnumerateCameras().ToList();
                _logger.Debug("相机列表已刷新，当前 {Count} 台", AvailableCameras.Count());
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "刷新相机列表失败");
                InitializationError = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        /// <summary>刷新相机列表（兼容旧调用点；未初始化时会抛异常，新代码请用 TryRefreshCameras）</summary>
        public void RefreshCameras()
        {
            if (!TryRefreshCameras())
                throw new InvalidOperationException(InitializationError ?? "相机服务未初始化，请先调用Initialize方法。");
        }
    }
}
