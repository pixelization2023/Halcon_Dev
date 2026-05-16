using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using HalconDotNet;
using Inspection.Models;
using Inspection.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MVS.Core;
using Prism.Commands;
using Prism.Navigation;
using Serilog;
using SkiaSharp;

namespace Inspection.ViewModels
{
    /// <summary>
    /// 主监控台。
    /// 迁移自 窗体.UI.FrmMian 主界面：状态灯（PLC / 数据库 / MES / 复判 / 存图盘）、
    /// 检测项显示、检测进度环、检测 CT、结果表格、实时日志，以及
    /// 开始运行 / 清除数据 / 初始化出板 / 相机软触发 等按钮。
    /// </summary>
    public class DashboardViewModel : BindableBase, INavigationAware
    {
        private readonly InspectionOrchestrator _orchestrator;
        private readonly StatusMonitorService _status;
        private readonly PlcIoService _plc;
        private readonly UserSessionService _session;
        private readonly ILogger _logger;

        public DashboardViewModel(InspectionOrchestrator orchestrator, StatusMonitorService status,
            PlcIoService plc, UserSessionService session, ILogger logger)
        {
            _orchestrator = orchestrator;
            _status = status;
            _plc = plc;
            _session = session;
            _logger = logger.ForContext<DashboardViewModel>();

            _orchestrator.ProgressChanged += OnProgressChanged;
            _orchestrator.PcsCompleted += OnPcsCompleted;
            _orchestrator.StatusChanged += OnStatusChanged;
            _orchestrator.SheetUploaded += OnSheetUploaded;
            _status.Updated += OnStatusUpdated;

            StartCommand = new DelegateCommand(ExecuteStart);
            StopCommand = new DelegateCommand(async () => await ExecuteStopAsync());
            ClearCommand = new DelegateCommand(ExecuteClear);
            TriggerCameraCommand = new DelegateCommand(() => _orchestrator.RequestSoftwareTrigger());
            InitializeBoardCommand = new DelegateCommand(async () => await ExecuteInitializeBoardAsync());
            ClearEventsCommand = new DelegateCommand(() => Events.Clear());

            foreach (var indicator in new[]
                     {
                         _status.PlcStatus, _status.DatabaseStatus, _status.MesStatus,
                         _status.TraceStatus, _status.DiskStatus
                     })
                Indicators.Add(indicator);

            // 图表画笔必须由代码构造，主题变化时重建（颜色取自共享内核，保证与全站同源）
            ThemePalette.Changed += (_, _) => RunOnUi(RebuildCharts);
            RebuildCharts();
        }

        #region 图表（LiveCharts2）

        /// <summary>单次检测耗时趋势，最多保留最近 60 个 PCS</summary>
        private const int MaxTrendPoints = 60;

        private readonly List<double> _elapsedTrend = new();

        private ISeries[] _yieldSeries = Array.Empty<ISeries>();
        /// <summary>良率环形图：OK / NG 两段</summary>
        public ISeries[] YieldSeries
        {
            get => _yieldSeries;
            private set => SetProperty(ref _yieldSeries, value);
        }

        private ISeries[] _trendSeries = Array.Empty<ISeries>();
        /// <summary>检测耗时折线</summary>
        public ISeries[] TrendSeries
        {
            get => _trendSeries;
            private set => SetProperty(ref _trendSeries, value);
        }

        private Axis[] _hiddenXAxes = { new Axis { IsVisible = false, ShowSeparatorLines = false } };
        public Axis[] HiddenXAxes
        {
            get => _hiddenXAxes;
            private set => SetProperty(ref _hiddenXAxes, value);
        }

        private Axis[] _hiddenYAxes = { new Axis { IsVisible = false, ShowSeparatorLines = false } };
        public Axis[] HiddenYAxes
        {
            get => _hiddenYAxes;
            private set => SetProperty(ref _hiddenYAxes, value);
        }

        /// <summary>本次已检测的 PCS 总数（图表中心显示用）</summary>
        public int ProcessedPcs => _elapsedTrend.Count;

        public string YieldText => OkCount + NgCount == 0
            ? "--"
            : (OkCount * 100.0 / (OkCount + NgCount)).ToString("F1") + "%";

        private void RebuildCharts()
        {
            var ok = ToSk(ThemePalette.Get("SuccessBrush"));
            var ng = ToSk(ThemePalette.Get("ErrorBrush"));
            var line = ToSk(ThemePalette.Get("PrimaryBrush"));
            var track = ToSk(ThemePalette.Get("DataTrackBrush"));

            // 环形：良品段 + 不良段（无数据时用轨道色占位，保持圆环可见）
            var okValue = OkCount;
            var ngValue = NgCount;
            if (okValue + ngValue == 0)
            {
                YieldSeries = new ISeries[]
                {
                    new PieSeries<double>
                    {
                        Values = new double[] { 1 },
                        InnerRadius = 62,
                        MaxRadialColumnWidth = 16,
                        Fill = new SolidColorPaint(track),
                        Stroke = null,
                        Name = "暂无数据"
                    }
                };
            }
            else
            {
                YieldSeries = new ISeries[]
                {
                    new PieSeries<double>
                    {
                        Values = new double[] { okValue },
                        InnerRadius = 62,
                        MaxRadialColumnWidth = 16,
                        Fill = new SolidColorPaint(ok),
                        Stroke = null,
                        Name = "OK"
                    },
                    new PieSeries<double>
                    {
                        Values = new double[] { ngValue },
                        InnerRadius = 62,
                        MaxRadialColumnWidth = 16,
                        Fill = new SolidColorPaint(ng),
                        Stroke = null,
                        Name = "NG"
                    }
                };
            }

            TrendSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _elapsedTrend.ToArray(),
                    Fill = null,
                    Stroke = new SolidColorPaint(line, 2),
                    GeometrySize = _elapsedTrend.Count <= 30 ? 5 : 0,
                    GeometryStroke = new SolidColorPaint(line, 2),
                    GeometryFill = new SolidColorPaint(ToSk(ThemePalette.Get("CardBrush"))),
                    LineSmoothness = 0.3
                }
            };

            RaisePropertyChanged(nameof(YieldText));
            RaisePropertyChanged(nameof(ProcessedPcs));
        }

        private static SKColor ToSk(System.Windows.Media.Color c) => new(c.R, c.G, c.B, c.A);

        #endregion

        #region 集合

        public ObservableCollection<StatusIndicator> Indicators { get; } = new();
        public ObservableCollection<string> DetectionItems { get; } = new();
        public ObservableCollection<PcsInspectionResult> PcsResults { get; } = new();
        public ObservableCollection<string> Events { get; } = new();

        #endregion

        #region 绑定属性

        public string ProductName => _orchestrator.Configuration?.ProductName ?? "未加载产品";

        public string SolutionName => _orchestrator.Configuration?.SolutionName ?? "-";

        public string CurrentUserText => _session.IsLoggedIn ? $"{_session.UserName}（{_session.Role}）" : "未登录";

        private int _processedImages;
        public int ProcessedImages
        {
            get => _processedImages;
            private set => SetProperty(ref _processedImages, value);
        }

        private int _imageTotal;
        public int ImageTotal
        {
            get => _imageTotal;
            private set => SetProperty(ref _imageTotal, value);
        }

        private int _okCount;
        public int OkCount
        {
            get => _okCount;
            private set => SetProperty(ref _okCount, value);
        }

        private int _ngCount;
        public int NgCount
        {
            get => _ngCount;
            private set => SetProperty(ref _ngCount, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        private string _visionElapsed = "0ms";
        public string VisionElapsed
        {
            get => _visionElapsed;
            private set => SetProperty(ref _visionElapsed, value);
        }

        private string _uploadElapsed = "0ms";
        public string UploadElapsed
        {
            get => _uploadElapsed;
            private set => SetProperty(ref _uploadElapsed, value);
        }

        private string _elapsed = "0天0小时0分钟0秒";
        public string Elapsed
        {
            get => _elapsed;
            private set => SetProperty(ref _elapsed, value);
        }

        private string _lotNo = "-";
        public string LotNo
        {
            get => _lotNo;
            private set => SetProperty(ref _lotNo, value);
        }

        private string _productModel = "-";
        public string ProductModel
        {
            get => _productModel;
            private set => SetProperty(ref _productModel, value);
        }

        private string _laserCode = "-";
        public string LaserCode
        {
            get => _laserCode;
            private set => SetProperty(ref _laserCode, value);
        }

        private string _paperCode = "-";
        public string PaperCode
        {
            get => _paperCode;
            private set => SetProperty(ref _paperCode, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        private HObject? _currentImage;
        /// <summary>当前显示图像（HalconView 的 HImage 绑定目标）</summary>
        public HObject? CurrentImage
        {
            get => _currentImage;
            private set
            {
                var previous = _currentImage;
                if (SetProperty(ref _currentImage, value))
                {
                    RaisePropertyChanged(nameof(HasImage));

                    if (previous != null && !ReferenceEquals(previous, value))
                        DisposeLater(previous);
                }
            }
        }

        public bool HasImage => _currentImage != null && _currentImage.IsInitialized();

        #endregion

        #region 命令

        public DelegateCommand StartCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand ClearCommand { get; }
        public DelegateCommand TriggerCameraCommand { get; }
        public DelegateCommand InitializeBoardCommand { get; }
        public DelegateCommand ClearEventsCommand { get; }

        private void ExecuteStart()
        {
            if (!_orchestrator.IsProductLoaded)
            {
                AppendEvent("请先在「产品方案」页面加载产品");
                return;
            }

            foreach (var recipe in _orchestrator.Configuration!.Recipes)
            {
                if (_orchestrator.Configuration.ImageTotal > 0 && recipe.ParseImageIndexes().Count == 0)
                    AppendEvent($"配方 {recipe.ProcedureName} 未绑定图片索引");
            }

            _orchestrator.Start();
            IsRunning = true;
            AppendEvent("开始运行，等待相机图像…");
        }

        private async Task ExecuteStopAsync()
        {
            await _orchestrator.StopAsync();
            IsRunning = false;
            AppendEvent("已停止运行");
        }

        private void ExecuteClear()
        {
            _orchestrator.ClearSheet();
            PcsResults.Clear();
            AppendEvent("数据已清除");
        }

        private async Task ExecuteInitializeBoardAsync()
        {
            if (!_plc.IsConnected)
            {
                AppendEvent("PLC 未连接，无法发送初始化出板信号");
                return;
            }

            _orchestrator.ClearSheet();
            PcsResults.Clear();

            var ok = await _plc.SignalUploadCompletedAsync();
            AppendEvent(ok ? "PLC 初始化出板信号已发送！" : "PLC 信号发送失败");
        }

        #endregion

        #region 事件回调

        private void OnProgressChanged(object? sender, InspectionSummary summary)
        {
            RunOnUi(() =>
            {
                ProcessedImages = summary.ProcessedImages;
                ImageTotal = summary.ImageTotal;
                OkCount = summary.OkCount;
                NgCount = summary.NgCount;
                Progress = summary.Progress;
                VisionElapsed = summary.VisionElapsedMs + "ms";
                UploadElapsed = summary.UploadElapsedMs + "ms";

                if (summary.ProcessedPcs == 0) _elapsedTrend.Clear();
                RebuildCharts();
            });
        }

        private void OnPcsCompleted(object? sender, PcsInspectionResult result)
        {
            RunOnUi(() =>
            {
                PcsResults.Insert(0, result);
                while (PcsResults.Count > 200) PcsResults.RemoveAt(PcsResults.Count - 1);

                if (result.OutputImage != null && result.OutputImage.IsInitialized())
                    CurrentImage = result.OutputImage;

                // 耗时趋势（按时间顺序追加，只保留最近 N 点）
                _elapsedTrend.Add(result.ElapsedMs);
                while (_elapsedTrend.Count > MaxTrendPoints) _elapsedTrend.RemoveAt(0);

                RebuildCharts();
            });
        }

        private void OnStatusChanged(object? sender, string message) => AppendEvent(message);

        private void OnSheetUploaded(object? sender, UploadOutcome outcome)
        {
            RunOnUi(() =>
            {
                AppendEvent(outcome.Success
                    ? $"整张数据上传完成（{outcome.ElapsedMs}ms）"
                    : $"整张数据上传失败：{outcome.Error}");

                RefreshDetectionItems();
            });
        }

        private void OnStatusUpdated(object? sender, StatusMonitorService service)
        {
            RunOnUi(() =>
            {
                Elapsed = string.Format("{0}天{1}小时{2}分钟{3}秒",
                    (int)service.Elapsed.TotalDays, service.Elapsed.Hours, service.Elapsed.Minutes, service.Elapsed.Seconds);

                foreach (var indicator in Indicators)
                    indicator.RaiseTextChanged();

                var lot = _orchestrator.LotInfo;
                if (lot != null)
                {
                    LotNo = lot.LotNo ?? "-";
                    ProductModel = lot.ProductModel ?? "-";
                }

                LaserCode = _orchestrator.LaserCodes.LastOrDefault() ?? "-";
                PaperCode = _orchestrator.PaperCodes.FirstOrDefault() ?? "-";
            });
        }

        #endregion

        #region 辅助

        private void RefreshDetectionItems()
        {
            DetectionItems.Clear();
            var items = _orchestrator.Configuration?.DetectionItems;
            if (items == null) return;

            foreach (var kv in items)
                DetectionItems.Add($"{kv.Key}: {kv.Value}");
        }

        private void AppendEvent(string message)
        {
            RunOnUi(() =>
            {
                Events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
                while (Events.Count > 500) Events.RemoveAt(Events.Count - 1);
            });
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }

        private static void DisposeLater(HObject image)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                image.Dispose();
                return;
            }

            dispatcher.BeginInvoke(() => image.Dispose(), DispatcherPriority.ApplicationIdle);
        }

        #endregion

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _orchestrator.Start();
            IsRunning = _orchestrator.IsRunning;

            RaisePropertyChanged(nameof(ProductName));
            RaisePropertyChanged(nameof(SolutionName));
            RaisePropertyChanged(nameof(CurrentUserText));
            RefreshDetectionItems();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        #endregion
    }
}
