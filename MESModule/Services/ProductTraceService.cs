using MESModule.Interfaces;
using MESModule.Models;
using Serilog;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MESModule.Services
{
    public class ProductTraceService : IProductDataService
    {
        private readonly IMESConnector _mesConnector;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, ProductInfo> _localCache = new();
        private readonly Channel<InspectionResult> _pendingResults;
        private CancellationTokenSource? _uploadCts;

        public ProductTraceService(IMESConnector mesConnector, ILogger logger)
        {
            _mesConnector = mesConnector;
            _logger = logger.ForContext<ProductTraceService>();
            _pendingResults = Channel.CreateUnbounded<InspectionResult>();
        }

        public ProductInfo? GetLocalProductInfo(string barcode)
        {
            var found = _localCache.TryGetValue(barcode, out var info);
            _logger.Debug("查询本地产品缓存: {Barcode} -> {Found}", barcode, found ? "命中" : "未命中");
            return found ? info : null;
        }

        public void CacheProductInfo(ProductInfo info)
        {
            _localCache[info.Barcode] = info;
            _logger.Debug("缓存产品信息: {Barcode}", info.Barcode);
        }

        public async Task EnqueueResultAsync(InspectionResult result)
        {
            await _pendingResults.Writer.WriteAsync(result);
            _logger.Debug("检测结果入队: {Barcode} [{Judgment}]", result.Barcode, result.Judgment);
        }

        public event EventHandler<int>? QueueCountChanged;

        public void StartBackgroundUpload(CancellationToken ct = default)
        {
            _uploadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = UploadLoop(_uploadCts.Token);
            _logger.Information("后台上传服务已启动");
        }

        private async Task UploadLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await foreach (var result in _pendingResults.Reader.ReadAllAsync(ct))
                    {
                        await _mesConnector.UploadInspectionResultAsync(result, ct);
                        QueueCountChanged?.Invoke(this, _pendingResults.Reader.Count);
                        _logger.Information("上传成功: {Barcode}", result.Barcode);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Error(ex, "后台上传异常，1秒后重试");
                    await Task.Delay(1000, ct);
                }
            }
        }

        public void Stop()
        {
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            _logger.Information("后台上传服务已停止");
        }
    }
}
