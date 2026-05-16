using PLCModule.Interfaces;
using Serilog;
using System.Diagnostics;

namespace PLCModule.Services
{
    /// <summary>
    /// PLC心跳服务 — 定期向PLC发送心跳信号，检测连接状态
    /// </summary>
    public class PLCHeartbeatService : IDisposable
    {
        private readonly IPLCCommunicator _communicator;
        private readonly ILogger _logger;
        private readonly string _heartbeatAddress;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly int _intervalMs;

        public bool IsRunning { get; private set; }

        public event EventHandler<bool>? ConnectionStatusChanged; // true=在线 false=离线

        public PLCHeartbeatService(IPLCCommunicator communicator, ILogger logger,
            string heartbeatAddress = "D0", int intervalMs = 2000)
        {
            _communicator = communicator;
            _logger = logger.ForContext<PLCHeartbeatService>();
            _heartbeatAddress = heartbeatAddress;
            _intervalMs = intervalMs;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _loopTask = HeartbeatLoop(_cts.Token);
            IsRunning = true;
            _logger.Information("PLC心跳服务启动，地址={Addr} 间隔={Interval}ms",
                _heartbeatAddress, _intervalMs);
        }

        public async Task StopAsync()
        {
            IsRunning = false;
            _cts?.Cancel();
            if (_loopTask != null)
            {
                try { await _loopTask; }
                catch (OperationCanceledException) { }
            }
            _logger.Information("PLC心跳服务已停止");
        }

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            bool wasConnected = false;
            var toggle = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 翻转心跳值（0→1→0→1）
                    toggle = !toggle;
                    var result = await _communicator.WriteAsync(_heartbeatAddress, toggle, ct);

                    if (!wasConnected && result)
                    {
                        wasConnected = true;
                        ConnectionStatusChanged?.Invoke(this, true);
                        _logger.Information("PLC心跳恢复");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (wasConnected)
                    {
                        wasConnected = false;
                        ConnectionStatusChanged?.Invoke(this, false);
                        _logger.Warning("PLC心跳丢失: {Error}", ex.Message);
                    }
                }

                try { await Task.Delay(_intervalMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
