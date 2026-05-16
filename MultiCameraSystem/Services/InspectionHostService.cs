using Halcon.Core;
using Inspection.Models;
using Inspection.Services;
using MultiCameraSystem.Services.Interfaces;
using MvCameraControl;
using MVS.Core;
using Serilog;

namespace MultiCameraSystem.Services
{
    /// <summary>
    /// 检测宿主服务 —— 把相机采集链路接到迁移过来的 Inspection 模块上。
    ///
    /// 迁移自 窗体 的 <c>FrmMian</c> + 相机类（HIKCam.SoftWare / 回调存图）：
    /// <list type="bullet">
    /// <item>监听 <see cref="InspectionOrchestrator.CameraTriggerRequested"/>（PLC 上升沿）→ 触发对应相机</item>
    /// <item>监听相机的 <see cref="CameraDevice.FrameGrabbed"/> → 转成 Halcon 图像 → 提交给检测编排器</item>
    /// <item>提供主界面「相机软触发」按钮的批量触发能力</item>
    /// </list>
    /// 之所以放在外壳程序集：相机服务属于 UI 层，Inspection 模块保持对相机的零依赖。
    /// </summary>
    public class InspectionHostService : IDisposable
    {
        private readonly ICameraService _cameraService;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly PlcIoService _plc;
        private readonly ILogger _logger;
        private readonly Dictionary<string, CameraDevice> _boundCameras = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        private int _frameIndex;
        private bool _disposed;

        public InspectionHostService(ICameraService cameraService, InspectionOrchestrator orchestrator,
            PlcIoService plc, ILogger logger)
        {
            _cameraService = cameraService;
            _orchestrator = orchestrator;
            _plc = plc;
            _logger = logger.ForContext<InspectionHostService>();

            _orchestrator.CameraTriggerRequested += OnCameraTriggerRequested;
            _orchestrator.SoftwareTriggerRequested += OnSoftwareTriggerRequested;
            _orchestrator.ProductLoaded += OnProductLoaded;
        }

        private void OnProductLoaded(object? sender, ProductConfiguration e) => BindCameras();

        /// <summary>当前是否已绑定相机</summary>
        public bool HasBoundCameras
        {
            get { lock (_sync) return _boundCameras.Count > 0; }
        }

        /// <summary>
        /// 按产品配置绑定（打开）相机。
        /// 对应原 FrmMian.LoadProduct 之后由用户在设置界面手工开相机的步骤，这里改为自动完成：
        /// 打开设备 → 设置软触发 → 开始采集 → 订阅取流回调。
        /// </summary>
        public void BindCameras()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                _logger.Warning("尚未加载产品，无法绑定相机");
                return;
            }

            UnbindCameras();
            _frameIndex = 0;

            foreach (var binding in cfg.Cameras)
            {
                if (string.IsNullOrWhiteSpace(binding.SerialNumber))
                {
                    _logger.Warning("相机 {Name} 未绑定序列号，已跳过", binding.Name);
                    continue;
                }

                try
                {
                    var camera = _cameraService.GetCamera(binding.SerialNumber);

                    if (!camera.IsConnected)
                        camera.Connect();

                    camera.FrameGrabbed += OnFrameGrabbed;

                    camera.SetSoftwareTriggerMode();
                    camera.StartGrabbing();

                    lock (_sync)
                        _boundCameras[binding.Name] = camera;

                    _logger.Information("相机已绑定并开始采集: {Name} ({Sn})", binding.Name, binding.SerialNumber);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "绑定相机失败: {Name} ({Sn})", binding.Name, binding.SerialNumber);
                }
            }
        }

        /// <summary>解绑并停止所有相机</summary>
        public void UnbindCameras()
        {
            lock (_sync)
            {
                foreach (var camera in _boundCameras.Values)
                {
                    try
                    {
                        camera.FrameGrabbed -= OnFrameGrabbed;
                        if (camera.IsGrabbing) camera.StopGrabbing();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "解绑相机失败");
                    }
                }

                _boundCameras.Clear();
            }
        }

        /// <summary>批量软触发（主界面「相机软触发」按钮）</summary>
        public async Task TriggerAllAsync()
        {
            KeyValuePair<string, CameraDevice>[] cameras;
            lock (_sync) cameras = _boundCameras.ToArray();

            if (cameras.Length == 0)
            {
                _logger.Warning("没有已绑定的相机，无法触发");
                return;
            }

            foreach (var (name, camera) in cameras)
            {
                try
                {
                    await Task.Run(() => camera.ExecuteSoftwareTrigger()).ConfigureAwait(false);
                    _logger.Debug("已软触发相机 {Name}", name);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "软触发相机失败: {Name}", name);
                }
            }
        }

        /// <summary>按逻辑名触发单个相机（PLC 上升沿触发）</summary>
        public async Task TriggerAsync(string cameraName)
        {
            CameraDevice? camera;
            lock (_sync) _boundCameras.TryGetValue(cameraName, out camera);

            if (camera == null)
            {
                _logger.Warning("未找到相机绑定: {Name}", cameraName);
                return;
            }

            try
            {
                // 扫码位的触发名可能是 "扫码1"，而绑定名可能是 "海康扫码枪0"，这里做一次宽松匹配
                await Task.Run(() => camera.ExecuteSoftwareTrigger()).ConfigureAwait(false);
                _logger.Information("已触发相机 {Name}", cameraName);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "触发相机失败: {Name}", cameraName);
            }
        }

        private void OnCameraTriggerRequested(object? sender, PlcTriggerEventArgs e)
        {
            // 原实现在触发后立刻回写「相机就绪」，这里同样处理（扫码位在扫码流程内部回写）
            _ = Task.Run(async () =>
            {
                await TriggerAsync(e.PointName).ConfigureAwait(false);

                if (!e.PointName.Contains("扫码", StringComparison.Ordinal))
                    await _plc.SignalCameraReadyAsync(e.PointName).ConfigureAwait(false);
            });
        }

        private void OnSoftwareTriggerRequested(object? sender, EventArgs e)
        {
            _ = Task.Run(TriggerAllAsync);
        }

        private void OnFrameGrabbed(object? sender, FrameGrabbedEventArgs e)
        {
            try
            {
                var cfg = _orchestrator.Configuration;
                if (cfg == null) return;

                var image = e.FrameOut?.Image;
                if (image == null) return;

                var hobject = HalconImageConvert.BitmapToHObject(image.ToBitmap());

                var index = Interlocked.Increment(ref _frameIndex);
                if (cfg.ImageTotal > 0 && index > cfg.ImageTotal)
                {
                    // 超过一张料的图片总数：自动开新的一张（原实现依赖 PLC 复位信号清数据）
                    Interlocked.Exchange(ref _frameIndex, 1);
                    index = 1;
                    _orchestrator.ClearSheet();
                    _logger.Information("图片数超过 {Total}，自动开始新一张", cfg.ImageTotal);
                }

                var frame = new QueuedFrame
                {
                    ImageIndex = index,
                    Image = hobject,
                    PhotoName = DateTime.Now.ToString("yyyy-MM-dd-H-mm-ss-ffff"),
                    CameraName = (sender as CameraDevice)?.Info.SerialNumber ?? string.Empty
                };

                if (!_orchestrator.SubmitFrame(frame))
                    _logger.Warning("图像入队失败: 序号 {Index}", index);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理相机取流帧失败");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _orchestrator.CameraTriggerRequested -= OnCameraTriggerRequested;
            _orchestrator.SoftwareTriggerRequested -= OnSoftwareTriggerRequested;
            _orchestrator.ProductLoaded -= OnProductLoaded;
            UnbindCameras();
        }
    }
}
