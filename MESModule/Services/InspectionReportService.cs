using MESModule.Interfaces;
using MESModule.Models;
using Serilog;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MESModule.Services
{
    /// <summary>
    /// 检测报告服务 — 批量上传检测结果到MES，支持离线缓存和自动重试
    /// </summary>
    public class InspectionReportService
    {
        private readonly IMESConnector _mesConnector;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<InspectionResult> _pendingResults = new();
        private readonly SemaphoreSlim _uploadLock = new(1, 1);
        private CancellationTokenSource? _flushCts;
        private int _maxBatchSize = 50;
        private int _flushIntervalMs = 5000;

        public InspectionReportService(IMESConnector mesConnector, ILogger logger)
        {
            _mesConnector = mesConnector;
            _logger = logger.ForContext<InspectionReportService>();
        }

        public int PendingCount => _pendingResults.Count;

        public event EventHandler<int>? BatchUploaded;

        public void Enqueue(InspectionResult result)
        {
            _pendingResults.Enqueue(result);
            _logger.Debug("检测结果入队: {Barcode} (队列: {Count})", result.Barcode, _pendingResults.Count);
        }

        public void StartAutoFlush(int flushIntervalMs = 5000, int maxBatchSize = 50)
        {
            _flushIntervalMs = flushIntervalMs;
            _maxBatchSize = maxBatchSize;
            _flushCts?.Cancel();
            _flushCts = new CancellationTokenSource();
            _ = AutoFlushLoop(_flushCts.Token);
        }

        public async Task<int> FlushAsync(CancellationToken ct = default)
        {
            if (_pendingResults.IsEmpty) return 0;

            await _uploadLock.WaitAsync(ct);
            try
            {
                var batch = new List<InspectionResult>();
                while (batch.Count < _maxBatchSize && _pendingResults.TryDequeue(out var r))
                    batch.Add(r);

                if (batch.Count == 0) return 0;

                int successCount = 0;
                foreach (var result in batch)
                {
                    if (await _mesConnector.UploadInspectionResultAsync(result, ct))
                        successCount++;
                    else
                        _pendingResults.Enqueue(result); // 失败的放回队列
                }

                if (batch.Count > 0)
                {
                    _logger.Information("批量上传完成: {Success}/{Total}", successCount, batch.Count);
                    BatchUploaded?.Invoke(this, batch.Count);
                }

                return successCount;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "批量上传异常");
                return 0;
            }
            finally
            {
                _uploadLock.Release();
            }
        }

        public InspectionResult[] GetPendingResults()
            => _pendingResults.ToArray();

        public async Task<string> ExportReportJsonAsync()
        {
            var results = GetPendingResults();
            using var ms = new MemoryStream();
            await JsonSerializer.SerializeAsync(ms, results, new JsonSerializerOptions { WriteIndented = true });
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private async Task AutoFlushLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_flushIntervalMs, ct);
                    if (!_pendingResults.IsEmpty)
                        await FlushAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.Error(ex, "自动刷新异常"); }
            }
        }

        public void Stop()
        {
            _flushCts?.Cancel();
            _flushCts?.Dispose();
            _uploadLock.Dispose();
        }
    }
}
