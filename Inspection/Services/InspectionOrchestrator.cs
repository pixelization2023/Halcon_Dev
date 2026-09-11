using System.Diagnostics;
using HalconDotNet;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 检测编排器 —— 整个迁移的核心。
    ///
    /// 迁移自 窗体.UI.FrmMian 的 CheckMethod / Run / ClearDate / LoadProduct / PlcReposition：
    /// <list type="bullet">
    /// <item>图像队列消费线程（原 SerLion.Image_Union + CheckMethod 死循环）</item>
    /// <item>按图片索引匹配检测配方并执行视觉流程（原 Run 中的 RunImage 绑定判断）</item>
    /// <item>PCS 结果汇聚与上传顺序绑定（原 Resultsdict / UploadOrderBuild / ImageCodeBuild）</item>
    /// <item>OK/NG 存图（原 SetBmpZip 调用）</item>
    /// <item>整张/单 PCS 触发数据上传（原 RunPcsIndex == UploadNum 判断）</item>
    /// </list>
    /// </summary>
    public class InspectionOrchestrator : IDisposable
    {
        private readonly ProductRepository _repository;
        private readonly HalconInspectionService _halcon;
        private readonly ImageArchiveService _archive;
        private readonly UploadOrchestrator _upload;
        private readonly PlcIoService _plc;
        private readonly MesApiClient _mes;
        private readonly StatusMonitorService _status;
        private readonly TraceSocketService _trace;
        private readonly InspectionResultStore _store;
        private readonly ILogger _logger;

        // 待检测图像队列：容量、丢最旧策略、丢弃计数与节流告警都在 FrameQueue 内（见该类注释）
        private readonly FrameQueue _frameQueue;
        private CancellationTokenSource? _cts;
        private Task? _worker;
        private bool _disposed;

        // ---- 单张料运行状态（对应原 SerLion 中一堆 [NonSerialized] 运行期字段） ----
        private readonly Dictionary<string, PcsInspectionResult> _results = new(StringComparer.Ordinal);
        private readonly List<string> _paperCodes = new();
        private readonly List<string> _laserCodes = new();
        private int _runIndex;
        private int _runImage;
        private int _runPcsIndex;
        private LongCodePayload? _lotInfo;
        private DateTime _sheetStartedAt = DateTime.Now;
        private bool _allImagesProcessed;

        public InspectionOrchestrator(ProductRepository repository, HalconInspectionService halcon,
            ImageArchiveService archive, UploadOrchestrator upload, PlcIoService plc,
            MesApiClient mes, StatusMonitorService status, TraceSocketService trace,
            InspectionResultStore store, ILogger logger)
        {
            _repository = repository;
            _halcon = halcon;
            _archive = archive;
            _upload = upload;
            _plc = plc;
            _mes = mes;
            _status = status;
            _trace = trace;
            _store = store;
            _logger = logger.ForContext<InspectionOrchestrator>();

            _frameQueue = new FrameQueue(OnFramesDropped);

            _plc.CameraTriggerRequested += (_, e) => CameraTriggerRequested?.Invoke(this, e);
            _plc.RepositionRequested += (_, e) => RepositionRequested?.Invoke(this, e);
        }

        #region 属性与事件

        /// <summary>当前加载的产品配置</summary>
        public ProductConfiguration? Configuration { get; private set; }

        /// <summary>是否已加载产品</summary>
        public bool IsProductLoaded => Configuration != null;

        /// <summary>是否正在运行检测</summary>
        public bool IsRunning => _worker != null;

        /// <summary>进度汇总</summary>
        public InspectionSummary Summary { get; } = new();

        /// <summary>当前 LOT / 机种信息（原 LB_Model / LB_Lot / LB_Item）</summary>
        public LongCodePayload? LotInfo => _lotInfo;

        /// <summary>视觉流程累计运行次数（原 SerLion.RunIndex）</summary>
        public int RunIndex => _runIndex;

        /// <summary>当前运行到第几张图片（原 SerLion.RunImage）</summary>
        public int RunImage => _runImage;

        /// <summary>已采集的纸质码（原 paperCode）</summary>
        public IReadOnlyList<string> PaperCodes => _paperCodes;

        /// <summary>已采集的镭射码（原 laserCode）</summary>
        public IReadOnlyList<string> LaserCodes => _laserCodes;

        /// <summary>当前本张已产出的 PCS 结果（原 Resultsdict）</summary>
        public IReadOnlyDictionary<string, PcsInspectionResult> Results => _results;

        /// <summary>进度变化</summary>
        public event EventHandler<InspectionSummary>? ProgressChanged;

        /// <summary>单个 PCS 检测完成</summary>
        public event EventHandler<PcsInspectionResult>? PcsCompleted;

        /// <summary>日志/状态文本</summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>整张上传完成</summary>
        public event EventHandler<UploadOutcome>? SheetUploaded;

        /// <summary>产品加载完成（外壳据此绑定相机）</summary>
        public event EventHandler<ProductConfiguration>? ProductLoaded;

        /// <summary>
        /// 产品配置被修改（例如在「检测配置」页保存了显示设置）。
        /// 界面据此刷新依赖配置的显示内容 —— 否则用户改了窗口绑定 / NG 框样式后，
        /// 主监控台不会立刻跟着变，要等下次加载产品。
        /// </summary>
        public event EventHandler<ProductConfiguration>? ConfigurationChanged;

        /// <summary>通知外部"配置已变"（由写配置的界面在保存成功后调用）</summary>
        public void NotifyConfigurationChanged()
        {
            var cfg = Configuration;
            if (cfg == null) return;

            // 统一在这里做一次规整化，避免各处调用点漏掉（窗口列表与窗口数必须始终一致）
            cfg.NormalizeDisplay();
            ConfigurationChanged?.Invoke(this, cfg);
        }

        /// <summary>PLC 请求触发相机（由外壳转发给相机服务）</summary>
        public event EventHandler<PlcTriggerEventArgs>? CameraTriggerRequested;

        /// <summary>PLC 请求复位</summary>
        public event EventHandler<PlcTriggerEventArgs>? RepositionRequested;

        /// <summary>请求外壳执行相机软触发（相机对象属于外壳程序集）</summary>
        public event EventHandler? SoftwareTriggerRequested;

        /// <summary>发起一次软触发请求</summary>
        public void RequestSoftwareTrigger() => SoftwareTriggerRequested?.Invoke(this, EventArgs.Empty);

        #endregion

        #region 产品加载 / 卸载

        /// <summary>
        /// 加载产品（迁移自 FrmMian.LoadProduct + BT_Click 的 "加载产品" 分支）。
        /// 原实现会依次：关闭相机 → 清数据 → 反序列化 .asol → 加载 VM 方案 → 连接 PLC → 开启 Socket。
        /// 相机部分由外壳负责，这里只处理配置、Halcon、PLC、Socket、数据库。
        /// </summary>
        public async Task<bool> LoadProductAsync(string productName, CancellationToken ct = default)
        {
            await UnloadAsync().ConfigureAwait(false);

            var cfg = _repository.Load(productName) ?? _repository.CreateDefault(productName);
            _repository.LocateVisionProgram(cfg);

            Configuration = cfg;
            _upload.Configure(cfg);

            ClearSheet();

            // 视觉流程：清空缓存，强制重新加载 .hdev
            _halcon.UnloadAll();

            if (string.IsNullOrWhiteSpace(cfg.VisionProgramFile))
            {
                Report($"产品 [{productName}] 未找到 Halcon 方案文件，请把 .hdev 放入 {_repository.GetProductDirectory(productName)}");
            }

            // 存图线程
            _archive.Start();
            _status.ConfigureImageSave(cfg.GetImageLocation());

            // PLC
            if (cfg.PlcDevices.Count > 0)
            {
                var connected = await _plc.ConnectAsync(cfg, ct).ConfigureAwait(false);
                if (connected)
                {
                    _plc.StartMonitor();
                    Report("PLC连接成功！");
                }
                else
                {
                    Report("PLC连接失败！");
                }
            }

            // 复判 Socket
            if (!_status.TraceStatus.IsOnline)
            {
                if (_trace.Start(cfg.Socket))
                    Report("Socket服务器已开启！");
            }

            // 数据库
            _status.EnableDatabaseCheck(true);
            var dbOk = await Task.Run(() => _store.TestConnection() && _store.EnsureSchema(), ct).ConfigureAwait(false);
            Report(dbOk ? "数据库连接成功！" : "数据库连接失败！");

            Summary.ProductName = cfg.ProductName;
            Summary.ImageTotal = cfg.ImageTotal;
            Summary.SheetPcsTotal = cfg.SheetPcsTotal;
            _status.MesHost = cfg.Mes.HostIp;
            _status.ResetUptime();

            Report($"产品 [{productName}] 加载完成，共 {cfg.Recipes.Count} 个检测配方");
            ProductLoaded?.Invoke(this, cfg);
            return true;
        }

        /// <summary>卸载当前产品（释放相机/PLC/Socket 之外的资源）</summary>
        public async Task UnloadAsync()
        {
            await StopAsync().ConfigureAwait(false);

            _halcon.UnloadAll();
            await _plc.DisconnectAsync().ConfigureAwait(false);
            await _archive.StopAsync().ConfigureAwait(false);

            ClearSheet();
            Configuration = null;
            _logger.Information("产品已卸载");
        }

        #endregion

        #region 运行控制

        /// <summary>启动检测消费线程（对应原 BT_Click "加载产品" 中 CheckThread.Start()）</summary>
        public void Start()
        {
            if (_worker != null) return;

            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => ConsumeLoopAsync(_cts.Token));
            _logger.Information("检测编排线程已启动");
        }

        /// <summary>停止检测消费线程并丢弃排队图像</summary>
        public async Task StopAsync()
        {
            if (_worker == null) return;

            _cts?.Cancel();
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.Warning(ex, "检测编排线程退出异常"); }

            _worker = null;
            _cts?.Dispose();
            _cts = null;

            _frameQueue.Drain();
            _logger.Information("检测编排线程已停止");
        }

        /// <summary>仿真投图计数（无相机时用于轮换图片序号）</summary>
        private int _simulatedIndex;

        /// <summary>
        /// 投入一张**合成样张**（无相机也能跑通整条检测链路）。
        /// 用于自检：Halcon 执行 → PCS 结果梳理 → 存图 → 上传 → 图表/日志。
        /// </summary>
        /// <param name="ng">true = 生成"有大块亮区"的不良样张；false = 良品样张</param>
        public bool SubmitSimulatedFrame(bool ng)
        {
            var cfg = Configuration;
            if (cfg == null)
            {
                _logger.Warning("尚未加载产品，无法投入仿真图像");
                return false;
            }

            var imageTotal = Math.Max(1, cfg.ImageTotal);
            var index = _simulatedIndex % imageTotal + 1;
            _simulatedIndex++;

            var frame = new QueuedFrame
            {
                ImageIndex = index,
                Image = SelfTestService.CreateSampleImage(ng),
                PhotoName = $"SIM-{(ng ? "NG" : "OK")}-{DateTime.Now:HHmmssfff}",
                CameraName = "SIM"
            };

            Report($"投入仿真样张：{(ng ? "NG" : "OK")} 样张（第 {index}/{imageTotal} 张）");
            return SubmitFrame(frame);
        }

        /// <summary>提交一帧待检测图像（对应原 SerLion.Instance.Image_Union.Enqueue）</summary>
        public bool SubmitFrame(QueuedFrame frame)
        {
            if (Configuration == null)
            {
                frame.Dispose();
                _logger.Warning("尚未加载产品，丢弃图像");
                return false;
            }

            // 队列满时的取舍（丢最旧、计数、按秒节流）全部由 FrameQueue 负责，见其类注释
            return _frameQueue.TryEnqueue(frame);
        }

        /// <summary>队列溢出告警：FrameQueue 已完成节流，这里只决定"报给谁"</summary>
        private void OnFramesDropped(int total)
        {
            _logger.Warning("待检测图像队列已满（上限 {Max} 张），已丢弃最旧的图像；累计丢弃 {Total} 帧。" +
                            "这说明检测节拍跟不上采集节拍，请检查配方耗时或相机触发频率",
                FrameQueue.Capacity, total);
            Report($"警告：图像积压，已丢弃最旧的图像（累计 {total} 帧），检测节拍可能跟不上采集");
        }

        /// <summary>累计丢弃的帧数（自检/诊断用）</summary>
        public int DroppedFrameCount => _frameQueue.DroppedCount;

        private async Task ConsumeLoopAsync(CancellationToken ct)
        {
            try
            {
                foreach (var frame in _frameQueue.Consume(ct))
                {
                    try
                    {
                        await ProcessFrameAsync(frame, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "处理图像失败: {Photo}", frame.PhotoName);
                        Summary.LastError = ex.Message;
                    }
                    finally
                    {
                        frame.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        #endregion

        #region 单帧处理

        private async Task ProcessFrameAsync(QueuedFrame frame, CancellationToken ct)
        {
            var cfg = Configuration;
            if (cfg == null) return;

            Summary.ImageTotal = cfg.ImageTotal;

            foreach (var recipe in cfg.Recipes)
            {
                ct.ThrowIfCancellationRequested();

                if (!recipe.ParseImageIndexes().Contains(frame.ImageIndex))
                    continue;

                var run = _halcon.RunRecipe(cfg, recipe, frame.Image, frame.ImageIndex);
                Summary.VisionElapsedMs += run.ElapsedMs;

                if (!run.Success)
                {
                    _logger.Warning("配方 {Recipe} 执行失败: {Error}", recipe.ProcedureName, run.Error);
                    Report($"流程 {recipe.ProcedureName} 执行失败: {run.Error}");
                    continue;
                }

                if (recipe.IsCodeRecipe)
                {
                    await HandleCodeAsync(cfg, run.Code, frame, ct).ConfigureAwait(false);
                    continue;
                }

                foreach (var pcs in run.PcsResults)
                    await CommitPcsAsync(cfg, pcs, frame, run.ElapsedMs, ct).ConfigureAwait(false);
            }

            Summary.ProcessedImages++;
            RaiseProgress();

            if (cfg.ImageTotal > 0 && Summary.ProcessedImages >= cfg.ImageTotal && !_allImagesProcessed)
            {
                _allImagesProcessed = true;
                await FinalizeSheetAsync(cfg, ct).ConfigureAwait(false);
            }
        }

        /// <summary>处理扫码流程结果（迁移自 FrmMian.Run 的 "else" 分支）</summary>
        private async Task HandleCodeAsync(ProductConfiguration cfg, string? code, QueuedFrame frame, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                Report("未扫描到二维码！");
                await _plc.SignalNoCodeAsync().ConfigureAwait(false);
                return;
            }

            var general = cfg.GetToggles(SettingsGroups.General);

            // 长码（>20 字符）视为镭射码；短码视为纸质码（与原实现的长度判断一致）
            if (code.Length > 20)
            {
                if (general.Repeat)
                {
                    _lotInfo = await FetchLotInfoAsync(cfg, code, ct).ConfigureAwait(false);

                    var paper = _paperCodes.Count > 0 ? _paperCodes[0] : string.Empty;
                    var duplicate = await _upload.CheckDuplicateAsync(cfg, paper, _lotInfo?.LotNo ?? string.Empty, ct)
                        .ConfigureAwait(false);

                    if (duplicate)
                    {
                        Report("当前制品已检测过！请勿重复过CCD！");
                        await _plc.SignalDuplicateAsync(2).ConfigureAwait(false);
                        return;
                    }
                }

                await _plc.SignalDuplicateAsync(1).ConfigureAwait(false);
                _laserCodes.Add(code);
                Report($"镭射码：{code}");
            }
            else
            {
                _paperCodes.Add(code);
                Report($"纸质码：{code}");
            }

            await _plc.SignalCameraReadyAsync("扫码1").ConfigureAwait(false);
            _logger.Information("扫码结果已记录: {Code}", code);
        }

        private async Task<LongCodePayload?> FetchLotInfoAsync(ProductConfiguration cfg, string laserCode, CancellationToken ct)
        {
            try
            {
                var info = await _mes.GetLongCodeInfoAsync(laserCode, "2", ct).ConfigureAwait(false);
                return info?.Payload;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取 LOT 信息失败: {Code}", laserCode);
                return null;
            }
        }

        /// <summary>汇聚单个 PCS 结果（迁移自 FrmMian.Run 中的 PCS 结果写入段）</summary>
        private async Task CommitPcsAsync(ProductConfiguration cfg, PcsRunResult pcs, QueuedFrame frame,
            long recipeElapsedMs, CancellationToken ct)
        {
            var result = new PcsInspectionResult
            {
                ItemResults = pcs.ItemResults,
                PointSets = pcs.PointSets,
                OutputImage = pcs.OutputImage,
                PhotoName = frame.PhotoName,
                ElapsedMs = recipeElapsedMs,
                Judgment = pcs.HasNg ? PcsJudgment.Ng : PcsJudgment.Ok,
                SourceImageIndex = frame.ImageIndex,
                // 原图副本：留给界面显示（消费循环随后会 Dispose 掉 frame.Image）。
                // 现场很多 .hdev 不输出结果图，界面必须有原图可显示。
                SourceImage = TryCloneImage(frame.Image)
            };

            // ---- 上传顺序绑定（原 UploadOrderBuild[RunPcsIndex]） ----
            result.PcsKey = _runPcsIndex < cfg.UploadOrder.Count
                ? cfg.UploadOrder[_runPcsIndex]
                : (_runPcsIndex + 1).ToString();

            // ---- 二维码与图片绑定（原 ImageCodeBuild） ----
            foreach (var kv in cfg.ImageCodeBinding)
            {
                var indices = kv.Value.Split(new[] { '。', '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (!indices.Contains(_runPcsIndex.ToString()))
                    continue;

                if (int.TryParse(kv.Key, out var codeIndex))
                {
                    result.PcsIndex = (codeIndex + 1).ToString();
                    if (codeIndex >= 0 && codeIndex < _paperCodes.Count)
                        result.PaperCode = _paperCodes[codeIndex];
                    if (codeIndex >= 0 && codeIndex < _laserCodes.Count)
                        result.LaserCode = _laserCodes[codeIndex];
                }
                break;
            }

            if (string.IsNullOrEmpty(result.PcsIndex))
                result.PcsIndex = result.PcsKey;

            // ---- 记录 & 计数 ----
            _results[result.PcsKey] = result;
            _runPcsIndex++;
            _runIndex++;

            if (result.Judgment == PcsJudgment.Ng) Summary.NgCount++;
            else Summary.OkCount++;
            Summary.ProcessedPcs = _runPcsIndex;

            // ---- 存图（原 pictrueLocation["存图设置"] 分支） ----
            SavePcsImage(cfg, result, frame);

            PcsCompleted?.Invoke(this, result);
            Report($"PCS {result.PcsIndex} 检测完成: {result.ResultText}");

            // ---- 单 PCS 即时上传（原 SinglePCS 分支） ----
            if (cfg.GetToggles(SettingsGroups.Vision).SinglePcs)
            {
                var outcome = await UploadSheetAsync(cfg, ct).ConfigureAwait(false);
                _results.Clear();
                SheetUploaded?.Invoke(this, outcome);
            }

            RaiseProgress();
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private void SavePcsImage(ProductConfiguration cfg, PcsInspectionResult result, QueuedFrame frame)
        {
            var toggles = cfg.GetToggles(SettingsGroups.ImageSave);
            if (!toggles.NgImage && !toggles.OriginalImage && !toggles.ReCheckImage)
                return;

            // 结果图优先使用 Halcon 输出图；"原始图"分支必须用真正的原图副本
            //（result.SourceImage），不能沿用 image —— 那在 .hdev 有结果图时其实是结果图，
            // 会把结果图当成原始图存进 OriginalPath。旧实现有这个隐患。
            var resultImage = result.OutputImage ?? frame.Image;
            var originalImage = result.SourceImage ?? frame.Image;

            var location = cfg.GetImageLocation();
            var laser = result.LaserCode.Length > 0
                ? result.LaserCode
                : (_laserCodes.Count > 0 ? _laserCodes[0] : "未扫到码");

            try
            {
                if (toggles.OriginalImage && !string.IsNullOrWhiteSpace(location.OriginalPath))
                {
                    if (originalImage != null && originalImage.IsInitialized())
                    {
                        var path = ImageArchiveService.BuildOriginalPath(location.OriginalPath, result.PhotoName, laser);
                        _archive.Enqueue(originalImage.Clone(), path, useJpeg: false);
                    }
                }

                if (toggles.NgImage || toggles.ReCheckImage)
                {
                    if (resultImage == null || !resultImage.IsInitialized()) return;

                    var root = result.Judgment == PcsJudgment.Ng ? location.NgPath
                        : (toggles.ReCheckImage ? location.ReCheckPath : location.OkPath);

                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        var path = ImageArchiveService.BuildResultPath(root, result.Judgment == PcsJudgment.Ng,
                            result.PhotoName, laser, result.PcsKey);
                        _archive.Enqueue(resultImage.Clone(), path, useJpeg: true, quality: location.ZipQuality);
                        result.PhotoName = path;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "存图任务创建失败: PCS {Pcs}", result.PcsIndex);
            }
        }

        /// <summary>尝试克隆一份图像（失败返回 null，不抛）</summary>
        private HObject? TryCloneImage(HObject? image)
        {
            if (image == null || !image.IsInitialized()) return null;

            try
            {
                return image.Clone();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "克隆原图失败（界面将只能显示结果图）");
                return null;
            }
        }

        /// <summary>整张处理结束（迁移自 CheckMethod 中 RunPcsIndex == UploadNum 判断）</summary>
        private async Task FinalizeSheetAsync(ProductConfiguration cfg, CancellationToken ct)
        {
            var vision = cfg.GetToggles(SettingsGroups.Vision);

            if (vision.SinglePcs)
                return;   // 已在每个 PCS 处理时上传

            if (!vision.WholePcs && cfg.SheetPcsTotal > 0 && _runPcsIndex < cfg.SheetPcsTotal)
                return;

            var outcome = await UploadSheetAsync(cfg, ct).ConfigureAwait(false);
            SheetUploaded?.Invoke(this, outcome);

            Summary.ElapsedMs = (long)(DateTime.Now - _sheetStartedAt).TotalMilliseconds;
            Report(outcome.Success
                ? $"数据上传完成，耗时 {outcome.ElapsedMs}ms"
                : $"数据上传失败: {outcome.Error}");
        }

        private async Task<UploadOutcome> UploadSheetAsync(ProductConfiguration cfg, CancellationToken ct)
        {
            var context = new UploadContext
            {
                Product = _lotInfo?.Product ?? string.Empty,
                ProductModel = _lotInfo?.ProductModel ?? cfg.Mes.ProductModel,
                LotNo = _lotInfo?.LotNo ?? string.Empty,
                PaperCodes = new List<string>(_paperCodes),
                LaserCodes = new List<string>(_laserCodes),
                Results = new Dictionary<string, PcsInspectionResult>(_results),
                StartedAt = _sheetStartedAt,
                EndedAt = DateTime.Now
            };

            var outcome = await _upload.UploadAsync(cfg, context, ct).ConfigureAwait(false);
            Summary.UploadElapsedMs += outcome.ElapsedMs;
            return outcome;
        }

        /// <summary>清除当前料数据（迁移自 FrmMian.ClearDate）</summary>
        public void ClearSheet()
        {
            // PCS 结果图的**所有权在这一层**：每个 PcsInspectionResult.OutputImage 是 Halcon 过程输出的
            // HObject（由 CollectPcsResults 取得所有权）。这里必须显式释放，否则每张料的所有结果图
            // 都会一直挂在 _results 里 —— 换料/长时间运行就是内存泄漏。
            // 界面（DashboardViewModel）只持有引用，不负责释放。
            foreach (var result in _results.Values)
                result.DisposeImage();

            _results.Clear();
            _paperCodes.Clear();
            _laserCodes.Clear();
            _runIndex = 0;
            _runImage = 0;
            _runPcsIndex = 0;
            _allImagesProcessed = false;
            _sheetStartedAt = DateTime.Now;

            Summary.ProcessedImages = 0;
            Summary.ProcessedPcs = 0;
            Summary.OkCount = 0;
            Summary.NgCount = 0;
            Summary.ElapsedMs = 0;
            Summary.VisionElapsedMs = 0;
            Summary.UploadElapsedMs = 0;
            Summary.LastError = null;

            RaiseProgress();
        }

        private void RaiseProgress() => ProgressChanged?.Invoke(this, Summary);

        private void Report(string message)
        {
            _logger.Information("{Message}", message);
            StatusChanged?.Invoke(this, message);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { StopAsync().GetAwaiter().GetResult(); } catch { }
            _frameQueue.Dispose();
        }
    }
}
