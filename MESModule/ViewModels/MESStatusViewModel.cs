using MESModule.Interfaces;
using MESModule.Models;
using MESModule.Services;
using Serilog;

namespace MESModule.ViewModels
{
    public class MESStatusViewModel : BindableBase, INavigationAware
    {
        private readonly ILogger _logger;
        private readonly IMESConnector _mesConnector;

        public MESStatusViewModel(ILogger logger, IMESConnector mesConnector)
        {
            _logger = logger.ForContext<MESStatusViewModel>();
            _mesConnector = mesConnector;
        }

        private string _baseUrl = "http://localhost:5000/api";
        public string BaseUrl
        {
            get => _baseUrl;
            set => SetProperty(ref _baseUrl, value);
        }

        private string _apiKey = "";
        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                SetProperty(ref _isConnected, value);
                RaisePropertyChanged(nameof(ConnectionStatus));
            }
        }

        public string ConnectionStatus => IsConnected ? "已连接" : "未连接";

        private string _status = "未连接 - 配置MES服务器后点击连接";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _lineId = "LINE-01";
        public string LineId
        {
            get => _lineId;
            set => SetProperty(ref _lineId, value);
        }

        private string _stationId = "CCD-01";
        public string StationId
        {
            get => _stationId;
            set => SetProperty(ref _stationId, value);
        }

        private int _queueCount;
        public int QueueCount
        {
            get => _queueCount;
            set
            {
                SetProperty(ref _queueCount, value);
                RaisePropertyChanged(nameof(QueueStatus));
            }
        }

        public string QueueStatus => QueueCount > 0 ? $"离线队列: {QueueCount} 条待上传" : "队列为空";

        private int _totalUploaded;
        public int TotalUploaded
        {
            get => _totalUploaded;
            set => SetProperty(ref _totalUploaded, value);
        }

        private int _totalFailed;
        public int TotalFailed
        {
            get => _totalFailed;
            set => SetProperty(ref _totalFailed, value);
        }

        private string _lastBarcode = "";
        public string LastBarcode
        {
            get => _lastBarcode;
            set => SetProperty(ref _lastBarcode, value);
        }

        private DelegateCommand? _connectCommand;
        public DelegateCommand ConnectCommand =>
            _connectCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    var config = new MESConfig
                    {
                        BaseUrl = BaseUrl,
                        ApiKey = ApiKey,
                        TimeoutSeconds = 10,
                        LineId = LineId,
                        StationId = StationId
                    };
                    Status = "连接中...";
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var result = await _mesConnector.ConnectAsync(config, cts.Token);
                    IsConnected = result;
                    Status = result ? "连接成功" : "连接失败";
                    _logger.Information("MES连接: {Result}", result ? "成功" : "失败");
                }
                catch (Exception ex)
                {
                    Status = $"连接失败: {ex.Message}";
                    _logger.Error(ex, "MES连接异常");
                }
            });

        private DelegateCommand? _disconnectCommand;
        public DelegateCommand DisconnectCommand =>
            _disconnectCommand ??= new DelegateCommand(async () =>
            {
                await _mesConnector.DisconnectAsync();
                IsConnected = false;
                Status = "已断开连接";
            });

        private DelegateCommand? _healthCheckCommand;
        public DelegateCommand HealthCheckCommand =>
            _healthCheckCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    Status = "健康检查中...";
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var ok = await _mesConnector.HealthCheckAsync(cts.Token);
                    IsConnected = ok;
                    Status = ok ? "服务正常" : "服务不可达";
                }
                catch (Exception ex)
                {
                    Status = $"检查失败: {ex.Message}";
                }
            });

        private DelegateCommand? _lookupCommand;
        public DelegateCommand LookupCommand =>
            _lookupCommand ??= new DelegateCommand(async () =>
            {
                if (string.IsNullOrEmpty(LastBarcode))
                {
                    Status = "请输入产品条码";
                    return;
                }
                try
                {
                    Status = "查询中...";
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var product = await _mesConnector.GetProductInfoAsync(LastBarcode, cts.Token);
                    Status = product != null ? $"查询成功: {product}" : "未找到产品";
                }
                catch (Exception ex)
                {
                    Status = $"查询失败: {ex.Message}";
                }
            });

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入MES状态界面");
            _isConnected = _mesConnector.IsConnected;
            RaisePropertyChanged(nameof(IsConnected));
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
