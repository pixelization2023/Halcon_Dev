using System.Collections.Concurrent;
using System.IO;
using HalconDotNet;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 存图服务。
    /// 迁移自 窗体.序列化类.SerLion 的 SaveBMP / SetBmpZip，以及 窗体.文件操作类.FileOperations.CleanFile。
    /// 原实现用 GDI+ 编码（EncoderParameters + ImageCodecInfo）保存 Bitmap；
    /// 这里直接用 Halcon 的 write_image 写盘，省掉 Halcon→Bitmap→磁盘 的一次转换。
    /// </summary>
    public class ImageArchiveService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly BlockingCollection<ImageArchiveTask> _queue = new(new ConcurrentQueue<ImageArchiveTask>());
        private Task? _worker;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public ImageArchiveService(ILogger logger)
        {
            _logger = logger.ForContext<ImageArchiveService>();
        }

        /// <summary>已入队待保存的图片数量</summary>
        public int PendingCount => _queue.Count;

        /// <summary>累计保存成功数量</summary>
        public long SavedCount { get; private set; }

        /// <summary>启动后台存图线程</summary>
        public void Start()
        {
            if (_worker != null) return;

            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => WorkerLoop(_cts.Token));
            _logger.Information("存图线程已启动");
        }

        /// <summary>
        /// 停止后台存图线程（等待已入队的图片保存完）。
        ///
        /// 关键修复：**不能调用 <c>CompleteAdding()</c>** —— 那会把队列永久关闭，
        /// 而产品切换流程是「Unload（停止存图）→ Load（再启动存图）」，
        /// 一旦关掉队列，后续所有存图都会以"存图队列已关闭"被丢弃（实测踩过的坑）。
        /// 这里改为只取消消费循环，队列保持可用，Start() 可以随时再启动。
        /// </summary>
        public async Task StopAsync()
        {
            if (_worker == null) return;

            _cts?.Cancel();

            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.Error(ex, "存图线程退出异常"); }

            _worker = null;
            _cts?.Dispose();
            _cts = null;
            _logger.Information("存图线程已停止，累计保存 {Count} 张", SavedCount);
        }

        /// <summary>入队一张图片（所有权转移给本服务，保存后自动释放）</summary>
        public bool Enqueue(HObject image, string filePath, bool useJpeg = true, int quality = 80)
        {
            if (image == null || !image.IsInitialized() || string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                return _queue.TryAdd(new ImageArchiveTask
                {
                    Image = image,
                    FilePath = filePath,
                    UseJpeg = useJpeg,
                    Quality = quality
                });
            }
            catch (InvalidOperationException)
            {
                image.Dispose();
                _logger.Warning("存图队列已关闭，丢弃图片: {Path}", filePath);
                return false;
            }
        }

        private void WorkerLoop(CancellationToken ct)
        {
            try
            {
                foreach (var task in _queue.GetConsumingEnumerable(ct))
                {
                    try
                    {
                        SaveImage(task);
                        SavedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "保存图片失败: {Path}", task.FilePath);
                    }
                    finally
                    {
                        task.Image?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>同步保存单张图片</summary>
        public void SaveImage(ImageArchiveTask task) => SaveHObject(task.Image, task.FilePath, task.UseJpeg, task.Quality);

        /// <summary>把 HObject 写到磁盘（Halcon write_image）</summary>
        public static void SaveHObject(HObject image, string filePath, bool useJpeg = true, int quality = 80)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var format = useJpeg ? "jpeg" : "bmp";

            if (useJpeg)
            {
                // JPEG 压缩质量通过系统参数控制，部分版本不支持时忽略即可
                try { HOperatorSet.SetSystem("jpeg_quality", Math.Clamp(quality, 1, 100)); }
                catch { /* 忽略：并非所有 HALCON 版本都暴露该参数 */ }
            }

            HOperatorSet.WriteImage(image, format, 0, filePath);
        }

        /// <summary>
        /// 生成结果图路径，布局与原项目一致：
        /// {根目录}\{yyyyMMdd}\{OK|NG}\{图片名}-{条码}-{PCS}.jpeg
        /// 说明：原项目在 OK 分支误用了 NGImageSavePath，这里改为按判定选择正确的根目录。
        /// </summary>
        public static string BuildResultPath(string root, bool isNg, string photoName, string code, string pcsKey)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("存图根目录为空", nameof(root));

            var dir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd"), isNg ? "NG" : "OK");
            Directory.CreateDirectory(dir);

            var name = Sanitize($"{photoName}-{code}-{pcsKey}.jpeg");
            return Path.Combine(dir, name);
        }

        /// <summary>清理过期图片（原 FileOperations.CleanFile）</summary>
        public int CleanupExpired(string root, int retentionDays)
        {
            if (string.IsNullOrWhiteSpace(root) || retentionDays <= 0)
                return 0;

            if (!Directory.Exists(root))
            {
                _logger.Warning("未找到图片存储地址: {Root}", root);
                return 0;
            }

            var threshold = DateTime.Now.AddDays(-retentionDays);
            var removed = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < threshold)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "删除过期图片失败: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "清理过期图片失败: {Root}", root);
            }

            if (removed > 0)
                _logger.Information("已清理 {Count} 张过期图片（保留 {Days} 天）", removed, retentionDays);

            return removed;
        }

        /// <summary>按存图设置一次性清理三个目录</summary>
        public void CleanupAll(ImageSaveLocation location)
        {
            if (location == null) return;

            if (location.OriginalRetentionDays is { Length: > 0 } d1 && int.TryParse(d1, out var days1))
                CleanupExpired(location.OriginalPath, days1);
            if (location.NgRetentionDays is { Length: > 0 } d2 && int.TryParse(d2, out var days2))
                CleanupExpired(location.NgPath, days2);
            if (location.ReCheckRetentionDays is { Length: > 0 } d3 && int.TryParse(d3, out var days3))
                CleanupExpired(location.ReCheckPath, days3);
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { StopAsync().GetAwaiter().GetResult(); }
            catch { /* 释放阶段忽略 */ }

            // 只有真正释放时才关闭队列
            try { _queue.CompleteAdding(); } catch { }
            _queue.Dispose();
        }
    }
}
