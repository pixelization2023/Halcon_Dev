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

        // 图片序号规划：每台相机各走各的序号，避免多相机共用一个全局自增计数器而串号。
        // 具体规则与"为什么会静默出错"见 CameraFrameIndexPlanner 的类注释。
        private readonly CameraFrameIndexPlanner _frameIndexPlanner;

        private bool _disposed;

        public InspectionHostService(ICameraService cameraService, InspectionOrchestrator orchestrator,
            PlcIoService plc, ILogger logger)
        {
            _cameraService = cameraService;
            _orchestrator = orchestrator;
            _plc = plc;
            _logger = logger.ForContext<InspectionHostService>();
            _frameIndexPlanner = new CameraFrameIndexPlanner(logger);

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
            _frameIndexPlanner.Configure(cfg);

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

                    var plan = _frameIndexPlanner.Plans.TryGetValue(binding.SerialNumber, out var p)
                        ? string.Join(",", p)
                        : "（未规划）";
                    _logger.Information("相机已绑定并开始采集: {Name} ({Sn})，图片序号 [{Plan}]",
                        binding.Name, binding.SerialNumber, plan);
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
                _frameIndexPlanner.ResetCursors();
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

                var camera = sender as CameraDevice;
                var serialNumber = camera?.Info.SerialNumber ?? string.Empty;

                // 每台相机走自己的图片序号（旧实现是全局自增，多相机会串号，见 CameraFrameIndexPlanner）
                var index = _frameIndexPlanner.Next(serialNumber, Math.Max(1, cfg.ImageTotal), out var startedNewSheet);

                if (startedNewSheet)
                {
                    // 上一张料已拍满：清掉它的汇总数据，从新的一张料重新计数
                    _orchestrator.ClearSheet();
                    _logger.Information("相机 {Camera} 已拍满本张料（每张 {Total} 张图），自动开始新的一张",
                        ResolveLogicalName(serialNumber), cfg.ImageTotal);
                }

                var hobject = HalconImageConvert.BitmapToHObject(image.ToBitmap());

                var frame = new QueuedFrame
                {
                    ImageIndex = index,
                    Image = hobject,
                    PhotoName = DateTime.Now.ToString("yyyy-MM-dd-H-mm-ss-ffff"),
                    // 逻辑名优先（相机绑定里的 Name），取不到再退化为序列号 ——
                    // 界面与日志里显示序列号对现场没什么意义。
                    CameraName = ResolveLogicalName(serialNumber)
                };

                if (!_orchestrator.SubmitFrame(frame))
                    _logger.Warning("图像入队失败: 相机 {Camera} 图片序号 {Index}", frame.CameraName, index);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理相机取流帧失败");
            }
        }

        /// <summary>序列号 → 相机绑定里的逻辑名</summary>
        private string ResolveLogicalName(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber)) return string.Empty;

            lock (_sync)
            {
                foreach (var kv in _boundCameras)
                {
                    if (string.Equals(kv.Value.Info.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase))
                        return kv.Key;
                }
            }

            return serialNumber;
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
