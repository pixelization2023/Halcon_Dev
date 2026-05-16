using System.Net;
using System.Net.Sockets;
using System.Text;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 复判 Socket 服务端。
    /// 迁移自 窗体.Socket通讯.SocketCommunication —— 原实现用裸 Thread + Thread.Abort（.NET 9 已移除），
    /// 这里改为 async TcpListener + CancellationToken，行为保持一致：
    /// 接受一个复判客户端连接、每秒发送心跳、把条码推送给客户端。
    /// </summary>
    public class TraceSocketService : IDisposable
    {
        private readonly ILogger _logger;
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private bool _disposed;

        public TraceSocketService(ILogger logger)
        {
            _logger = logger.ForContext<TraceSocketService>();
        }

        /// <summary>服务端是否已启动</summary>
        public bool IsListening { get; private set; }

        /// <summary>复判客户端是否已连接（原 SerLion.isClient）</summary>
        public bool IsClientConnected => _client?.Connected == true;

        /// <summary>客户端连接状态变化</summary>
        public event EventHandler<bool>? ClientStateChanged;

        /// <summary>启动监听（原 SocketCommunication.link）</summary>
        public bool Start(SocketParameter parameter)
        {
            if (IsListening)
            {
                Stop();
                return false;
            }

            try
            {
                var ip = IPAddress.Parse(parameter.RejudicationIp);
                _listener = new TcpListener(ip, parameter.RejudicationPort);
                _listener.Start(10);

                _cts = new CancellationTokenSource();
                _acceptTask = Task.Run(() => AcceptLoop(_cts.Token));

                IsListening = true;
                _logger.Information("复判 Socket 服务已启动: {Ip}:{Port}", parameter.RejudicationIp, parameter.RejudicationPort);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "复判 Socket 服务启动失败: {Ip}:{Port}", parameter.RejudicationIp, parameter.RejudicationPort);
                IsListening = false;
                return false;
            }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    _client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    _stream = _client.GetStream();
                    _logger.Information("复判客户端连接成功: {Remote}", _client.Client.RemoteEndPoint);
                    ClientStateChanged?.Invoke(this, true);

                    _ = Task.Run(() => ReceiveLoop(ct), ct);
                    _ = Task.Run(() => HeartbeatLoop(ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "复判 Socket 接受连接异常");
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (!ct.IsCancellationRequested && _stream != null)
                {
                    var count = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (count <= 0) break;

                    _logger.Information("复判客户端消息: {Message}", Encoding.Default.GetString(buffer, 0, count));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Warning(ex, "复判 Socket 接收异常");
            }
        }

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);

                    if (_stream == null || _client?.Connected != true)
                    {
                        HandleDisconnect();
                        return;
                    }

                    await _sendLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await _stream.WriteAsync(new byte[] { 0 }, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "复判 Socket 心跳失败，判定客户端断开");
                    HandleDisconnect();
                    return;
                }
            }
        }

        private void HandleDisconnect()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }

            _stream = null;
            _client = null;
            ClientStateChanged?.Invoke(this, false);
            _logger.Information("复判客户端已断开连接");
        }

        /// <summary>把条码发送给复判客户端（原 SocketCommunication.Read）</summary>
        public async Task<bool> SendCodeAsync(string code, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(code)) return false;

            if (_stream == null || _client?.Connected != true)
            {
                _logger.Warning("Socket 服务器或复判客户端未连接，条码未发送: {Code}", code);
                return false;
            }

            try
            {
                var payload = Encoding.Default.GetBytes(code);
                await _sendLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
                }
                finally
                {
                    _sendLock.Release();
                }

                _logger.Information("条码已发送复判: {Code}", code);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "向复判发送条码失败: {Code}", code);
                HandleDisconnect();
                return false;
            }
        }

        /// <summary>关闭服务（原 SocketCommunication.Close）</summary>
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            HandleDisconnect();

            _listener = null;
            IsListening = false;
            _cts?.Dispose();
            _cts = null;

            _logger.Information("复判 Socket 服务已关闭");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _sendLock.Dispose();
        }
    }
}
