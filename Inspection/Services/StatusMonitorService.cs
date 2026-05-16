using System.IO;
using System.Net.NetworkInformation;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 主界面状态监视。
    /// 迁移自 窗体.UI.FrmMian.timer1_Tick —— 原实现把 PLC 心跳、数据库、MES、复判、
    /// 存图盘、运行时长等检查全部塞在一个 Timer 回调里并直接操作控件。
    /// 这里改为后台轮询 + 事件推送，UI 通过数据绑定消费。
    /// </summary>
    public class StatusMonitorService : IDisposable
    {
        private readonly PlcIoService _plc;
        private readonly TraceSocketService _trace;
        private readonly InspectionResultStore _store;
        private readonly ImageArchiveService _archive;
        private readonly ILogger _logger;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private bool _disposed;

        public StatusMonitorService(PlcIoService plc, TraceSocketService trace, InspectionResultStore store,
            ImageArchiveService archive, ILogger logger)
        {
            _plc = plc;
            _trace = trace;
            _store = store;
            _archive = archive;
            _logger = logger.ForContext<StatusMonitorService>();
        }

        public StatusIndicator PlcStatus { get; } = new() { Name = "PLC", OkText = "PLC连接成功！", FailText = "PLC连接失败！" };
        public StatusIndicator DatabaseStatus { get; } = new() { Name = "数据库", OkText = "数据库连接成功！", FailText = "数据库连接失败！" };
        public StatusIndicator MesStatus { get; } = new() { Name = "MES", OkText = "MES连接成功！", FailText = "MES链接失败！" };
        public StatusIndicator TraceStatus { get; } = new() { Name = "复判", OkText = "复判连接成功！", FailText = "复判未连接！" };
        public StatusIndicator DiskStatus { get; } = new() { Name = "存图盘", OkText = "存图盘连接成功！", FailText = "存图盘连接失败！" };

        /// <summary>运行时长（对应原 LB_DateTime 的 "x天x小时x分钟x秒"）</summary>
        public TimeSpan Elapsed { get; private set; }

        /// <summary>状态刷新（每 <see cref="IntervalMs"/> 一次）</summary>
        public event EventHandler<StatusMonitorService>? Updated;

        /// <summary>需要检测的 MES 主机地址</summary>
        public string? MesHost { get; set; }

        /// <summary>需要检测的存图目录</summary>
        public string? ImageRoot { get; set; }

        /// <summary>存图清理间隔（分钟，0 表示不清理）</summary>
        public int CleanupIntervalMinutes { get; set; } = 30;

        public int IntervalMs { get; set; } = 1000;

        private DateTime _startedAt = DateTime.Now;
        private DateTime _lastCleanup = DateTime.MinValue;
        private ImageSaveLocation? _imageLocation;

        /// <summary>启动状态轮询</summary>
        public void Start()
        {
            if (_loop != null) return;

            _startedAt = DateTime.Now;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
            _logger.Information("状态监视已启动");
        }

        /// <summary>记录开始时刻（对应原 Stopwatch sw 的重启）</summary>
        public void ResetUptime() => _startedAt = DateTime.Now;

        /// <summary>设置存图路径与清理策略</summary>
        public void ConfigureImageSave(ImageSaveLocation location)
        {
            _imageLocation = location;
            ImageRoot = location?.NgPath;
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Elapsed = DateTime.Now - _startedAt;

                    PlcStatus.IsOnline = _plc.IsConnected;

                    if (DatabaseStatus.IsOnline)
                        DatabaseStatus.IsOnline = _store.TestConnection();

                    TraceStatus.IsOnline = _trace.IsClientConnected;

                    if (!string.IsNullOrWhiteSpace(MesHost))
                        MesStatus.IsOnline = await PingAsync(MesHost!).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(ImageRoot))
                        DiskStatus.IsOnline = Directory.Exists(ImageRoot);

                    // 定期清理过期图片（原 ClearFileTimer）
                    if (CleanupIntervalMinutes > 0 && _imageLocation != null &&
                        (DateTime.Now - _lastCleanup).TotalMinutes >= CleanupIntervalMinutes)
                    {
                        _lastCleanup = DateTime.Now;
                        _archive.CleanupAll(_imageLocation);
                    }

                    Updated?.Invoke(this, this);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "状态刷新异常");
                }

                try { await Task.Delay(IntervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>启用数据库连通性检测（原 SerLion.DatabaseBuilding）</summary>
        public void EnableDatabaseCheck(bool enabled) => DatabaseStatus.IsOnline = enabled;

        private static async Task<bool> PingAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 1500).ConfigureAwait(false);
                return reply.Status == IPStatus.Success;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts?.Cancel(); } catch { }
            try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }
    }
}
