using System.Diagnostics;
using System.Text.Json;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>一次整张上传所需的上下文</summary>
    public sealed class UploadContext
    {
        public string Product { get; set; } = string.Empty;
        public string ProductModel { get; set; } = string.Empty;
        public string LotNo { get; set; } = string.Empty;
        public List<string> PaperCodes { get; init; } = new();
        public List<string> LaserCodes { get; init; } = new();
        public Dictionary<string, PcsInspectionResult> Results { get; init; } = new();
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime EndedAt { get; set; } = DateTime.Now;
    }

    /// <summary>上传结果</summary>
    public sealed class UploadOutcome
    {
        public bool Success { get; set; }
        public long ElapsedMs { get; set; }
        public string? Error { get; set; }
        public List<string> Steps { get; } = new();
    }

    /// <summary>
    /// 检测数据上传编排。
    /// 迁移自 窗体.Mysql.UploadMethod.WholeUpload（含 FrmMian.Run 中的工序重复检查）。
    /// 原实现把 MES / MySQL / Socket / PLC 四类副作用揉在一个方法里，这里保持调用顺序不变，
    /// 但拆成独立步骤方法，便于单测与日志追踪。
    /// </summary>
    public class UploadOrchestrator
    {
        private readonly MesApiClient _mes;
        private readonly TraceSocketService _trace;
        private readonly InspectionResultStore _store;
        private readonly PlcIoService _plc;
        private readonly UserSessionService _session;
        private readonly ILogger _logger;

        public UploadOrchestrator(MesApiClient mes, TraceSocketService trace, InspectionResultStore store,
            PlcIoService plc, UserSessionService session, ILogger logger)
        {
            _mes = mes;
            _trace = trace;
            _store = store;
            _plc = plc;
            _session = session;
            _logger = logger.ForContext<UploadOrchestrator>();
        }

        /// <summary>配置数据库与 MES 连接参数</summary>
        public void Configure(ProductConfiguration cfg)
        {
            _store.Configure(cfg.MySql);
            _mes.BaseUrl = string.IsNullOrWhiteSpace(cfg.Mes.BaseUrl) ? _mes.BaseUrl : cfg.Mes.BaseUrl;
        }

        /// <summary>
        /// 工序重复检查（迁移自 FrmMian.Run 中扫码分支里的 Repeat 判断）。
        /// 返回 true 表示该制品已检测过（应拒绝）。
        /// </summary>
        public async Task<bool> CheckDuplicateAsync(ProductConfiguration cfg, string paperCode, string lotNo, CancellationToken ct = default)
        {
            if (!cfg.GetToggles(SettingsGroups.General).Repeat)
                return false;

            if (string.IsNullOrEmpty(paperCode))
                return false;

            try
            {
                var response = await _mes.CheckProcessAsync(cfg.Mes, paperCode, lotNo, ct).ConfigureAwait(false);
                var duplicate = response.Contains("工序管控数据不存在", StringComparison.Ordinal)
                                || response.Contains("测试次数超过", StringComparison.Ordinal);

                if (duplicate)
                    _logger.Warning("当前制品已检测过，请勿重复过 CCD: {Code}", paperCode);

                return duplicate;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "工序重复检查失败: {Code}", paperCode);
                return false;
            }
        }

        /// <summary>整张数据上传（对应原 WholeUpload）</summary>
        public async Task<UploadOutcome> UploadAsync(ProductConfiguration cfg, UploadContext ctx, CancellationToken ct = default)
        {
            var outcome = new UploadOutcome();
            var sw = Stopwatch.StartNew();

            try
            {
                await SampleUploadAsync(cfg, ctx, outcome, ct).ConfigureAwait(false);
                await SaveToDatabaseAsync(cfg, ctx, outcome).ConfigureAwait(false);
                await SaveConveyorCodeAsync(cfg, ctx, outcome).ConfigureAwait(false);
                await NotifyTraceClientAsync(cfg, ctx, outcome, ct).ConfigureAwait(false);

                outcome.Success = true;
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.Error = ex.Message;
                _logger.Error(ex, "数据上传失败");
            }
            finally
            {
                sw.Stop();
                outcome.ElapsedMs = sw.ElapsedMilliseconds;

                // 无论成功失败都要给 PLC 一个完成信号，避免产线卡死（与原实现一致）
                await SignalPlcUploadCompletedAsync().ConfigureAwait(false);
                _logger.Information("数据上传结束: {Result} ({Ms}ms)", outcome.Success ? "成功" : "失败", outcome.ElapsedMs);
            }

            return outcome;
        }

        #region 步骤

        /// <summary>样品板数据上传（原 OpenCoderComm 分支）</summary>
        private async Task SampleUploadAsync(ProductConfiguration cfg, UploadContext ctx, UploadOutcome outcome, CancellationToken ct)
        {
            if (!cfg.GetToggles(SettingsGroups.General).OpenCoderComm)
                return;

            if (ctx.PaperCodes.Count == 0)
                return;

            if (!string.Equals(ctx.PaperCodes[0], cfg.Sample.SampleCode, StringComparison.Ordinal))
                return;

            var defects = new List<SampleDefect>();
            foreach (var kv in ctx.Results)
            {
                if (string.IsNullOrEmpty(kv.Value.ResultText))
                    defects.Add(new SampleDefect { PcsNo = SafeInt(kv.Value.PcsIndex), ComponentName = string.Empty, ErrorType = string.Empty });
            }

            var request = new SampleUploadRequest
            {
                ProductModel = cfg.Mes.ProductModel,
                Product = ctx.Product,
                LineName = cfg.Mes.LineName,
                EngineerId = cfg.Mes.EngineerId,
                SubEngineerId = cfg.Mes.SubEngineerId,
                EquipmentType = cfg.Mes.EquipmentType,
                EquipmentId = cfg.Mes.Memo,
                Barcode = ctx.PaperCodes[0],
                UseType = 1,
                Defects = defects
            };

            var response = await _mes.UploadSampleAsync(request, ct).ConfigureAwait(false);
            outcome.Steps.Add("样品板上传: " + Truncate(response));
            _logger.Information("样品板上传成功");
        }

        /// <summary>MySQL 结果写入（原 VM设置 的 Single / Many 分支）</summary>
        private Task SaveToDatabaseAsync(ProductConfiguration cfg, UploadContext ctx, UploadOutcome outcome)
        {
            var vision = cfg.GetToggles(SettingsGroups.Vision);
            var operatorId = _session.UserName;

            if (vision.Single)
            {
                foreach (var kv in ctx.Results)
                {
                    var record = BuildRecord(kv.Value, ctx, cfg, operatorId);
                    record.PcsNumber = kv.Key;
                    if (_store.InsertResult(record))
                        outcome.Steps.Add($"数据库写入 PCS {kv.Key}");
                }
            }
            else if (vision.Many)
            {
                if (vision.OneImageOneCode)
                {
                    foreach (var kv in ctx.Results)
                    {
                        var record = BuildRecord(kv.Value, ctx, cfg, operatorId);
                        record.PcsNumber = kv.Value.PcsIndex;

                        var index = SafeInt(kv.Key) - 1;
                        record.PaperCode = index >= 0 && index < ctx.PaperCodes.Count ? ctx.PaperCodes[index] : kv.Value.PaperCode;
                        record.LaserCode = index >= 0 && index < ctx.LaserCodes.Count ? ctx.LaserCodes[index] : kv.Value.LaserCode;

                        if (_store.InsertResult(record))
                            outcome.Steps.Add($"数据库写入 PCS {record.PcsNumber}");
                    }
                }
                else if (vision.ManyImageOneCode)
                {
                    foreach (var paper in ctx.PaperCodes)
                    {
                        foreach (var kv in ctx.Results)
                        {
                            if (!string.Equals(paper, kv.Value.PaperCode, StringComparison.Ordinal))
                                continue;

                            var record = BuildRecord(kv.Value, ctx, cfg, operatorId);
                            record.PcsNumber = kv.Value.PcsIndex;
                            record.PaperCode = paper;
                            record.LaserCode = paper;

                            if (_store.InsertResult(record))
                                outcome.Steps.Add($"数据库写入 PCS {record.PcsNumber}");
                        }
                    }
                }
            }

            return Task.CompletedTask;
        }

        private InspectionRecord BuildRecord(PcsInspectionResult pcs, UploadContext ctx, ProductConfiguration cfg, string operatorId)
            => new()
            {
                RecordTime = DateTime.Now,
                PhotoName = pcs.PhotoName,
                PcsNumber = pcs.PcsIndex,
                PaperCode = pcs.PaperCode,
                LaserCode = pcs.LaserCode,
                Lot = ctx.LotNo,
                UserId = operatorId,
                Item = ctx.Product,
                Model = ctx.ProductModel,
                PointSet = SerializePointSets(pcs.PointSets),
                Result = pcs.ResultText
            };

        /// <summary>流道 / 单张模式的条码记录（原 Runners / Leaflets 分支）</summary>
        private Task SaveConveyorCodeAsync(ProductConfiguration cfg, UploadContext ctx, UploadOutcome outcome)
        {
            var general = cfg.GetToggles(SettingsGroups.General);
            var vision = cfg.GetToggles(SettingsGroups.Vision);

            if (general.Runners)
            {
                var code = ctx.LaserCodes.Count > 0
                    ? ctx.LaserCodes[0]
                    : "未扫描到二维码！" + DateTime.Now.ToString("yyyyMMddHHmmss");

                if (_store.InsertCode(code))
                    outcome.Steps.Add("流道模式条码已写入: " + code);
            }
            else if (general.Leaflets && ctx.LaserCodes.Count > 0)
            {
                if (_store.InsertCode(ctx.LaserCodes[0]))
                    outcome.Steps.Add("单张模式条码已写入: " + ctx.LaserCodes[0]);
            }

            return Task.CompletedTask;
        }

        /// <summary>把条码推送给复判程序（原 Choose_DataUploadsCheck 分支）</summary>
        private async Task NotifyTraceClientAsync(ProductConfiguration cfg, UploadContext ctx, UploadOutcome outcome, CancellationToken ct)
        {
            if (!cfg.GetToggles(SettingsGroups.General).ChooseDataUploadsCheck)
                return;

            if (ctx.LaserCodes.Count == 0)
                return;

            foreach (var code in ctx.LaserCodes)
            {
                if (await _trace.SendCodeAsync(code, ct).ConfigureAwait(false))
                    outcome.Steps.Add("条码已发送复判: " + code);
            }
        }

        /// <summary>上传完成信号（原 ["其他点位发送"]["9"] = 1）</summary>
        private async Task SignalPlcUploadCompletedAsync()
        {
            try
            {
                if (_plc.IsConnected &&
                    await _plc.SignalUploadCompletedAsync().ConfigureAwait(false))
                {
                    _logger.Information("数据上传完成！PLC 信号已发送！");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "发送 PLC 上传完成信号失败");
            }
        }

        #endregion

        #region 辅助

        /// <summary>把 NG 框集合序列化为 JSON（对应原 UploadMethod.JsonPointFs 列表）</summary>
        public static string SerializePointSets(List<List<BoxRegion>> pointSets)
        {
            var boxes = pointSets.SelectMany(p => p).ToList();
            if (boxes.Count == 0) return "OK";

            var payload = boxes.Select(b => new
            {
                PointX = b.CenterX.ToString("F2"),
                PointY = b.CenterY.ToString("F2"),
                Wide = b.Width.ToString("F2"),
                High = b.Height.ToString("F2")
            }).ToList();

            return JsonSerializer.Serialize(payload);
        }

        private static int SafeInt(string? text) => int.TryParse(text, out var v) ? v : 0;

        private static string Truncate(string text, int max = 200)
            => string.IsNullOrEmpty(text) ? string.Empty : (text.Length <= max ? text : text[..max] + "...");

        #endregion
    }
}
