using MESModule.Interfaces;
using MESModule.Models;
using Serilog;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MESModule.Services
{
    /// <summary>
    /// MES连接器 — HTTP REST 通信实现，支持离线队列和自动重试
    /// </summary>
    public class MESConnector : IMESConnector
    {
        private readonly ILogger _logger;
        private HttpClient? _httpClient;
        private MESConfig? _config;
        private bool _isConnected;
        private readonly Channel<InspectionResult> _offlineQueue;
        private CancellationTokenSource? _uploadCts;

        public bool IsConnected => _isConnected;

        public event EventHandler<int>? QueueCountChanged;

        public MESConnector(ILogger logger)
        {
            _logger = logger.ForContext<MESConnector>();
            _offlineQueue = Channel.CreateBounded<InspectionResult>(
                new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
        }

        public Task<bool> ConnectAsync(MESConfig config, CancellationToken ct)
        {
            _config = config;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
            };
            if (!string.IsNullOrEmpty(config.ApiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);

            _isConnected = true;
            _logger.Information("MES连接成功: {Url}", config.BaseUrl);

            // 启动离线队列上传
            _uploadCts = new CancellationTokenSource();
            _ = ProcessOfflineQueue(_uploadCts.Token);

            return Task.FromResult(true);
        }

        public async Task DisconnectAsync()
        {
            _isConnected = false;
            _uploadCts?.Cancel();
            _httpClient?.Dispose();
            _httpClient = null;
            await Task.CompletedTask;
        }

        public async Task<WorkOrderInfo?> GetWorkOrderAsync(string barcode, CancellationToken ct)
        {
            try
            {
                var response = await _httpClient!.GetAsync($"workorder/by-barcode/{barcode}", ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<WorkOrderInfo>(cancellationToken: ct);
                _logger.Warning("获取工单失败: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取工单异常");
                return null;
            }
        }

        public async Task<bool> UploadInspectionResultAsync(InspectionResult result, CancellationToken ct)
        {
            if (!_isConnected || _httpClient == null)
            {
                // 离线模式 → 入队
                await EnqueueOffline(result);
                return false;
            }

            return await UploadWithRetryAsync(result, ct);
        }

        public async Task<ProductInfo?> GetProductInfoAsync(string barcode, CancellationToken ct)
        {
            try
            {
                var response = await _httpClient!.GetAsync($"product/{barcode}", ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<ProductInfo>(cancellationToken: ct);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取产品信息异常");
                return null;
            }
        }

        public async Task<bool> UpdateProductStatusAsync(string barcode, string status, CancellationToken ct)
        {
            try
            {
                var payload = new { barcode, status };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient!.PutAsync($"product/{barcode}/status", content, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "更新产品状态异常");
                return false;
            }
        }

        public async Task<bool> HealthCheckAsync(CancellationToken ct)
        {
            try
            {
                var response = await _httpClient!.GetAsync("health", ct);
                _isConnected = response.IsSuccessStatusCode;
                return _isConnected;
            }
            catch
            {
                _isConnected = false;
                return false;
            }
        }

        // ==================== 离线队列 ====================

        private async Task EnqueueOffline(InspectionResult result)
        {
            await _offlineQueue.Writer.WriteAsync(result);
            _logger.Warning("MES离线，结果入队: {Barcode} (队列: {Count})",
                result.Barcode, _offlineQueue.Reader.Count);
            QueueCountChanged?.Invoke(this, _offlineQueue.Reader.Count);
        }

        private async Task ProcessOfflineQueue(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_isConnected && await _offlineQueue.Reader.WaitToReadAsync(ct))
                    {
                        while (_offlineQueue.Reader.TryRead(out var result))
                        {
                            if (await UploadWithRetryAsync(result, ct))
                            {
                                _logger.Information("离线数据上传成功: {Barcode}", result.Barcode);
                            }
                            else
                            {
                                // 上传失败，放回队列
                                await _offlineQueue.Writer.WriteAsync(result, ct);
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.Error(ex, "离线队列处理异常"); }

                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task<bool> UploadWithRetryAsync(InspectionResult result, CancellationToken ct)
        {
            int maxRetries = _config?.MaxRetries ?? 3;
            int retryDelay = _config?.RetryDelayMs ?? 1000;

            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    var json = JsonSerializer.Serialize(result);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient!.PostAsync("inspection/result", content, ct);

                    if (response.IsSuccessStatusCode)
                        return true;

                    _logger.Warning("MES上传失败(尝试 {Retry}): {StatusCode}", i + 1, response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "MES上传异常(尝试 {Retry})", i + 1);
                }

                if (i < maxRetries)
                    await Task.Delay(retryDelay * (i + 1), ct); // 递增退避
            }
            return false;
        }

        public void Dispose()
        {
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            _httpClient?.Dispose();
        }
    }
}
