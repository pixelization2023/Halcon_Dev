using System.Net.Http;
using System.Text;
using System.Text.Json;
using Inspection.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// MES WebAPI 客户端。
    /// 迁移自 窗体.WebApi.webapi 静态类 —— 原实现全部是 HttpClient 同步阻塞调用（.Result），
    /// 这里统一改为 async + CancellationToken，并把 URL 拼接收敛到一处。
    /// 接口路径与参数名保持不变，现场 MES 无需改动。
    /// </summary>
    public class MesApiClient : IDisposable
    {
        private readonly ILogger _logger;
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public MesApiClient(ILogger logger)
        {
            _logger = logger.ForContext<MesApiClient>();
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>MES 服务根地址，如 http://suzapi:9201</summary>
        public string BaseUrl { get; set; } = "http://suzapi:9201";

        private string Url(string path) => BaseUrl.TrimEnd('/') + path;

        #region 通用请求

        public async Task<string> GetAsync(string absoluteOrRelative, CancellationToken ct = default)
        {
            var url = absoluteOrRelative.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? absoluteOrRelative
                : Url(absoluteOrRelative);

            _logger.Debug("MES GET {Url}", url);
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                _logger.Warning("MES GET 失败 {Status}: {Url}", (int)response.StatusCode, url);

            return text;
        }

        public async Task<string> PostJsonAsync(string absoluteOrRelative, object payload, CancellationToken ct = default)
        {
            var url = absoluteOrRelative.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? absoluteOrRelative
                : Url(absoluteOrRelative);

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Debug("MES POST {Url}", url);
            using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                _logger.Warning("MES POST 失败 {Status}: {Url}", (int)response.StatusCode, url);

            return text;
        }

        private async Task<T?> GetJsonAsync<T>(string relative, CancellationToken ct)
        {
            try
            {
                var text = await GetAsync(relative, ct).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MES 请求失败: {Path}", relative);
                return default;
            }
        }

        #endregion

        #region 接口（与 窗体.WebApi 一一对应）

        /// <summary>根据蚀刻码获取 LOT / 品目 / 机种（原 LongCode_Obtain_lotNumber）</summary>
        public Task<LongCodeInfo?> GetLongCodeInfoAsync(string etchingCode, string workType, CancellationToken ct = default)
            => GetJsonAsync<LongCodeInfo>(
                $"/apimmcs040/GetProductModelBySht?SheetBarcode={Uri.EscapeDataString(etchingCode)}&WorkType={Uri.EscapeDataString(workType)}", ct);

        /// <summary>工序检查（原 ProcessInquiry）</summary>
        public Task<string> CheckProcessAsync(MesParameter mes, string etchingCode, string lotNumber, CancellationToken ct = default)
            => GetAsync($"/apimmcs040/CheckproductionInfo?ProductModel={mes.ProductModel}&EngineerID={mes.EngineerId}" +
                        $"&SubEngineerID={mes.SubEngineerId}&Barcode={Uri.EscapeDataString(etchingCode)}&LineName={mes.LineName}" +
                        $"&Memo={mes.Memo}&LotNo={Uri.EscapeDataString(lotNumber)}&EquipmentID={mes.EquipmentId}", ct);

        /// <summary>人员资格 / 设备点检（原 EquipmentMaintenance）</summary>
        public Task<string> CheckBasicInfoAsync(MesParameter mes, string instrumentId, CancellationToken ct = default)
            => GetAsync($"/apimmcs040/CheckBasicInfo?EquipmentID={mes.EquipmentId}&Memo={mes.Memo}&LineName={mes.LineName}" +
                        $"&EngineerID={mes.EngineerId}&SubEngineerID={mes.SubEngineerId}&InstrumentID={instrumentId}", ct);

        /// <summary>取前段整张不良位置（原 FrontSection_NG）</summary>
        public async Task<List<string>> GetFrontSectionNgAsync(string etchingCode, CancellationToken ct = default, params string[] partNumbers)
        {
            var parts = string.Concat(partNumbers.Select(p => "&PARTNO=" + Uri.EscapeDataString(p)));
            var response = await GetJsonAsync<MesListResponse<FrontSectionItem>>(
                $"/apimmcs017/GetAutoPunchDataDetail?sheetsn={Uri.EscapeDataString(etchingCode)}{parts}", ct).ConfigureAwait(false);

            return response?.Items?.Select(i => i.PcsNo ?? string.Empty).Where(s => s.Length > 0).ToList() ?? new List<string>();
        }

        /// <summary>工序写入（原 ProductionProcesses）</summary>
        public Task<string> WriteProductionProcessAsync(MesParameter mes, string product, string lotNo, string etchingCode,
            string operatorId, string status, CancellationToken ct = default)
        {
            var productionInfo = JsonSerializer.Serialize(new
            {
                ProductModel = mes.ProductModel,
                LotNo = lotNo,
                Product = product,
                Barcode = etchingCode,
                LineName = mes.LineName,
                Operator = operatorId,
                Machine = mes.Memo,
                EngineerID = mes.EngineerId,
                SubEngineerID = mes.SubEngineerId,
                Ext1 = "",
                Ext2 = "",
                Status = status
            }, JsonOptions);

            return GetAsync($"/apimmcs040/engineer/WriteEngineerProductionData?Memo={mes.Memo}" +
                            $"&ProductionInfo={Uri.EscapeDataString(productionInfo)}", ct);
        }

        /// <summary>短码（PCS）换长码（原 ShortCode_Obtain_LongCode）</summary>
        public async Task<string?> GetSheetBarcodeByPcsAsync(string productModel, string barcode, string flowId, CancellationToken ct = default)
        {
            var response = await GetJsonAsync<MesListResponse<ShortCodeMapping>>(
                $"/apimmcs017/GetSheetBarcodeByPcs?ProductModel={productModel}&Barcode={Uri.EscapeDataString(barcode)}&Flowid={flowId}", ct)
                .ConfigureAwait(false);

            return response?.Items?.FirstOrDefault()?.SheetBarcode;
        }

        /// <summary>整张条码测试结果写入（原 ShortCode_ResultPosition）</summary>
        public Task<string> WriteSheetTestResultAsync(string etchingCode, Dictionary<string, string> positionResults,
            string flowId, string productModel, string machineId, string computerNumber, string createTime, CancellationToken ct = default)
        {
            var payload = new
            {
                ShtBarcode = etchingCode,
                TestData = JsonSerializer.Serialize(
                    positionResults.Select(kv => new SheetTestData { PcsIndex = kv.Key, TestResult = kv.Value }).ToList(), JsonOptions),
                FlowID = flowId,
                ProductModel = productModel,
                MachineID = machineId,
                CreateUser = computerNumber,
                CreateDate = createTime
            };

            return PostJsonAsync("/apimmcs017/WriteTestSpecialComponent?TestSpecialComponent=", payload, ct);
        }

        /// <summary>实时良率写入（原 RealTimeYieldRate）</summary>
        public Task<string> WriteRealtimeYieldAsync(YieldUploadRequest request, CancellationToken ct = default)
            => PostJsonAsync("/apimmcs010/WriteRotResultRaw", new[] { request }, ct);

        /// <summary>日报写入（原 Daily）</summary>
        public Task<string> WriteDailyAsync(DailyUploadRequest request, CancellationToken ct = default)
            => PostJsonAsync("/apimmcs017/WriteFirstProcessResult", request, ct);

        /// <summary>样品板数据上传（原 UploadSamples）</summary>
        public Task<string> UploadSampleAsync(SampleUploadRequest request, CancellationToken ct = default)
            => PostJsonAsync("/apimmcs010/CheckSampleResultBySheet", request, ct);

        /// <summary>PCS 条码批量结果写入（原 BatchResultWriting）</summary>
        public Task<string> WriteBatchTestResultAsync(BatchTestRequest request, CancellationToken ct = default)
            => PostJsonAsync("/apimmcs017/WriteBatchTestComponent", request, ct);

        /// <summary>反查样品板二维码（原 SampleBoardInformation）</summary>
        public Task<string> GetSampleBoardBarcodeAsync(string equipmentId, CancellationToken ct = default)
            => GetAsync($"/apimmcs010/GetTestSampleBarcode?EquipmentID={equipmentId}", ct);

        /// <summary>取指定工程 BM 管控项目（原 BM_ControlProject）</summary>
        public async Task<List<string>> GetBmControlProjectsAsync(string service, string productModel, string engineerId,
            string subEngineerId, string flag, CancellationToken ct = default)
        {
            var response = await GetJsonAsync<MesListResponse<BmControlItem>>(
                $"/apimmcs036/DATA?SERVICE={service}&ProductModel={productModel}&EngineerID={engineerId}&SubEngineerID={subEngineerId}&Flag={flag}", ct)
                .ConfigureAwait(false);

            return response?.Items?.Select(i => i.FunctionName ?? string.Empty).Where(s => s.Length > 0).ToList() ?? new List<string>();
        }

        #endregion

        public void Dispose() => _http.Dispose();
    }
}
