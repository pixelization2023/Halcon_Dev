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

            // 事件订阅统一放到 Subscribe()/Unsubscribe() 里成对管理（见那两个方法）。
            // 之前这里是构造函数直接 += 、从不退订，而这个 ViewModel 的
            // IsNavigationTarget => true（Prism 会复用同一实例），
            // 于是每次重新导航都会再订阅一遍 —— 处理器被重复调用、旧实例也回收不掉。
            Subscribe();

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

            // 图表画笔必须由代码构造，主题变化时重建（颜色取自共享内核，保证与全站同源）。
            // 保存成字段而不是内联 lambda：内联 lambda 无法退订，
            // 而 ThemePalette.Changed 是**静态事件** —— 不退订就是永久泄漏。
            ThemePalette.Changed += OnThemePaletteChanged;
            RebuildCharts();
        }

        /// <summary>是否已订阅（防止重复订阅）</summary>
        private bool _subscribed;

        /// <summary>订阅编排器 / 状态监视 / 主题事件</summary>
        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;

            _orchestrator.ProgressChanged += OnProgressChanged;
            _orchestrator.PcsCompleted += OnPcsCompleted;
            _orchestrator.StatusChanged += OnStatusChanged;
            _orchestrator.SheetUploaded += OnSheetUploaded;
            // 产品加载/切换后按新方案的显示设置重建窗口
            _orchestrator.ProductLoaded += OnProductLoaded;
            // 在「检测配置」页改完显示设置并保存后，立刻重建窗口（不必等下次加载产品）
            _orchestrator.ConfigurationChanged += OnProductLoaded;
            _status.Updated += OnStatusUpdated;
        }

        /// <summary>退订全部事件（与 <see cref="Subscribe"/> 严格配对）</summary>
        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;

            _orchestrator.ProgressChanged -= OnProgressChanged;
            _orchestrator.PcsCompleted -= OnPcsCompleted;
            _orchestrator.StatusChanged -= OnStatusChanged;
            _orchestrator.SheetUploaded -= OnSheetUploaded;
            _orchestrator.ProductLoaded -= OnProductLoaded;
            _orchestrator.ConfigurationChanged -= OnProductLoaded;
            _status.Updated -= OnStatusUpdated;
        }

        /// <summary>主题变化时重建图表（静态事件必须用字段引用才能退订）</summary>
        private void OnThemePaletteChanged(object? sender, EventArgs e) => RunOnUi(RebuildCharts);

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

        // ================= 多窗口显示（见 Docs/多窗口显示与检测流程方案.md 第 2 节） =================

        /// <summary>
        /// 多窗口显示的窗口集合。窗口个数、每行列数、每个窗口绑定哪个 PCS
        /// 都来自产品方案的 <see cref="ProductConfiguration.Display"/>。
        /// </summary>
        public ObservableCollection<PcsDisplayWindowViewModel> DisplayWindows { get; } = new();

        /// <summary>UniformGrid 的列数（绑定到视图）</summary>
        public int DisplayColumns => _orchestrator.Configuration?.Display.ResolveColumns() ?? 2;

        /// <summary>单格高度（按宽高比推算，避免每格被拉成异形）</summary>
        public double DisplayTileHeight => 200;

        /// <summary>是否已有可见的显示窗口</summary>
        public bool HasDisplayWindows => DisplayWindows.Count > 0;

        /// <summary>是否没有配置任何显示窗口（视图用它显示兜底提示）</summary>
        public bool HasNoDisplayWindows => DisplayWindows.Count == 0;

        /// <summary>显示窗口数（卡片标题里显示）</summary>
        public int DisplayWindowCount => DisplayWindows.Count;

        /// <summary>
        /// 按当前产品方案的显示设置重建窗口集合。
        /// 窗口数变化时只做"先加后减"，避免一次性清空导致的界面抖动与句柄集中释放。
        /// </summary>
        public void RebuildDisplayWindows()
        {
            var settings = _orchestrator.Configuration?.Display;
            if (settings == null)
            {
                DisplayWindows.Clear();
            }
            else
            {
                settings.Normalize();
                var target = settings.Windows.Take(DisplaySettings.MaxWindowCount).ToList();

                // 裁掉多余的（从尾部开始）
                while (DisplayWindows.Count > target.Count)
                    DisplayWindows.RemoveAt(DisplayWindows.Count - 1);

                // 补齐缺的
                for (int i = DisplayWindows.Count; i < target.Count; i++)
                    DisplayWindows.Add(new PcsDisplayWindowViewModel(target[i]));

                // 已存在的窗口：**必须把新规则灌进去**，否则用户在配置页改的
                // 绑定方式 / 图像来源 / NG 框样式不会生效（窗口还拿着构造时的旧 spec）
                for (int i = 0; i < DisplayWindows.Count && i < target.Count; i++)
                    DisplayWindows[i].SetSpec(target[i]);
            }

            RaisePropertyChanged(nameof(DisplayColumns));
            RaisePropertyChanged(nameof(HasDisplayWindows));
            RaisePropertyChanged(nameof(HasNoDisplayWindows));
            RaisePropertyChanged(nameof(DisplayWindowCount));
        }

        /// <summary>把一条 PCS 结果分发到所有匹配的窗口</summary>
        private void DispatchToDisplayWindows(PcsInspectionResult result)
        {
            foreach (var window in DisplayWindows)
            {
                if (window.Match(result)) window.Attach(result);
            }
        }

        /// <summary>清空所有显示窗口（换料 / 清除数据时）</summary>
        private void ClearDisplayWindows()
        {
            foreach (var window in DisplayWindows) window.Clear();
        }

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
        /// <summary>
        /// 当前显示的图像（HalconView 的 HImage 绑定目标）。
        ///
        /// 生命周期约定（重要）：
        /// 这里存的是 <see cref="PcsInspectionResult.OutputImage"/> 的**引用**，所有权属于
        /// <see cref="InspectionOrchestrator"/>，由它的 ClearSheet() 统一释放。
        /// 本类**不得**释放它 —— 改成多窗口显示后，同一张图会被多个窗口同时引用，
        /// 谁释放谁就把别人的显示打断（旧实现就是在这里 Dispose 上一张，
        /// 导致 PcsResults 表里留下已释放句柄）。
        /// </summary>
        public HObject? CurrentImage
        {
            get => _currentImage;
            private set
            {
                if (SetProperty(ref _currentImage, value))
                    RaisePropertyChanged(nameof(HasImage));
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
            ClearDisplayWindows();
            CurrentImage = null;
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
            ClearDisplayWindows();
            CurrentImage = null;

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

                if (summary.ProcessedPcs == 0)
                {
                    _elapsedTrend.Clear();
                    // 新的一张料开始：清空所有显示窗口，避免上一张的图与判定残留在窗口里
                    ClearDisplayWindows();
                }

                RebuildCharts();
            });
        }

        private void OnProductLoaded(object? sender, ProductConfiguration configuration)
            => RunOnUi(() =>
            {
                RebuildDisplayWindows();
                ClearDisplayWindows();
            });

        private void OnPcsCompleted(object? sender, PcsInspectionResult result)
        {
            RunOnUi(() =>
            {
                PcsResults.Insert(0, result);
                while (PcsResults.Count > 200) PcsResults.RemoveAt(PcsResults.Count - 1);

                // 多窗口分发：每个窗口按自己的绑定规则决定要不要这张图
                DispatchToDisplayWindows(result);

                // 单窗口兼容路径：HalconView 只持引用、不释放（见 CurrentImage 的注释）
                if (result.OutputImage != null && result.OutputImage.IsInitialized())
                    CurrentImage = result.OutputImage;
                else if (result.SourceImage != null && result.SourceImage.IsInitialized())
                    CurrentImage = result.SourceImage;

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

        #endregion

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 注意：**不要**在这里无条件 Start()。
            // 旧实现在每次进入页面时都调 _orchestrator.Start()，含义是"一进监控台就开始消费图像队列"，
            // 操作员还没点「开始运行」设备就已经在跑了；而且 Start() 内部有 `if (_worker != null) return;`
            // 的守卫，所以点「停止」之后再回到这一页又会把它偷偷启动起来 —— 停止按钮等于失效。
            // 正确行为：运行的启停只由「开始运行 / 停止」按钮控制，这里只同步显示状态。
            // 重新进入页面时恢复订阅（OnNavigatedFrom 里退订了，这里成对补回）。
            // Subscribe() 自带幂等保护，首次进入不会重复订阅。
            Subscribe();

            IsRunning = _orchestrator.IsRunning;

            // 进入页面时按当前方案重建显示窗口（首次进入时 ProductLoaded 可能已经错过）
            RebuildDisplayWindows();

            RaisePropertyChanged(nameof(ProductName));
            RaisePropertyChanged(nameof(SolutionName));
            RaisePropertyChanged(nameof(CurrentUserText));
            RefreshDetectionItems();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        /// <summary>
        /// 离开页面时退订。
        ///
        /// 为什么必须做：本类 <see cref="IsNavigationTarget"/> 返回 true，Prism 会**复用同一实例**，
        /// 但"离开页面"并不等于"实例被销毁"。旧实现在构造函数里订阅、从不退订，
        /// 于是每进出一次就多一份订阅 —— 日志会重复、图表白重建，实例也永远回收不掉。
        /// 与 <see cref="Subscribe"/> 严格配对。
        /// </summary>
        public void OnNavigatedFrom(NavigationContext navigationContext) => Unsubscribe();

        #endregion
    }
}
