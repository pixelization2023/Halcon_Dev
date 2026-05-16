using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MVS.Core;
using Serilog;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Windows.Media;
using WorkBench.Interfaces;

namespace WorkBench.ViewModels
{
    /// <summary>
    /// 结果仪表盘。
    ///
    /// 图表用 LiveCharts2：良率环形图 + 检测耗时折线。
    /// 画笔颜色取自共享内核 <see cref="ThemePalette"/>（由外壳的 ThemeService 登记），
    /// 这样图表颜色与全站主题同源，切换主题/自定义主题时会自动重建。
    /// </summary>
    public class ResultDashboardViewModel : BindableBase, INavigationAware
    {
        private readonly ILogger _logger;

        /// <summary>趋势最多保留的点数</summary>
        private const int MaxTrendPoints = 60;

        private readonly List<double> _elapsedTrend = new();

        public ResultDashboardViewModel(ILogger logger)
        {
            _logger = logger.ForContext<ResultDashboardViewModel>();

            ThemePalette.Changed += (_, _) => RebuildCharts();
            RebuildCharts();
        }

        public ObservableCollection<AggregatedResult> Results { get; } = new();

        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private int _passCount;
        public int PassCount { get => _passCount; set => SetProperty(ref _passCount, value); }

        private int _failCount;
        public int FailCount { get => _failCount; set => SetProperty(ref _failCount, value); }

        private string _passRate = "--";
        public string PassRate { get => _passRate; set => SetProperty(ref _passRate, value); }

        #region 图表

        private ISeries[] _yieldSeries = Array.Empty<ISeries>();
        /// <summary>良率环形图（OK / NG）</summary>
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

        public int TrendCount => _elapsedTrend.Count;

        private void RebuildCharts()
        {
            var ok = ToSk(ThemePalette.Get("SuccessBrush"));
            var ng = ToSk(ThemePalette.Get("ErrorBrush"));
            var line = ToSk(ThemePalette.Get("PrimaryBrush"));
            var track = ToSk(ThemePalette.Get("DataTrackBrush"));
            var card = ToSk(ThemePalette.Get("CardBrush"));

            if (PassCount + FailCount == 0)
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
                        Values = new double[] { PassCount },
                        InnerRadius = 62,
                        MaxRadialColumnWidth = 16,
                        Fill = new SolidColorPaint(ok),
                        Stroke = null,
                        Name = "合格"
                    },
                    new PieSeries<double>
                    {
                        Values = new double[] { FailCount },
                        InnerRadius = 62,
                        MaxRadialColumnWidth = 16,
                        Fill = new SolidColorPaint(ng),
                        Stroke = null,
                        Name = "不合格"
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
                    GeometryFill = new SolidColorPaint(card),
                    LineSmoothness = 0.3
                }
            };

            RaisePropertyChanged(nameof(TrendCount));
        }

        private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

        #endregion

        public void AddResult(AggregatedResult result)
        {
            Results.Insert(0, result);
            TotalCount++;
            if (result.OverallSuccess) PassCount++;
            else FailCount++;
            PassRate = TotalCount > 0
                ? $"{(double)PassCount / TotalCount * 100:F1}%"
                : "--";

            // 趋势：按时间顺序追加
            _elapsedTrend.Add(result.TotalElapsedMs);
            while (_elapsedTrend.Count > MaxTrendPoints) _elapsedTrend.RemoveAt(0);

            RebuildCharts();
        }

        public void Clear()
        {
            Results.Clear();
            TotalCount = 0;
            PassCount = 0;
            FailCount = 0;
            PassRate = "--";
            _elapsedTrend.Clear();
            RebuildCharts();
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入结果仪表盘");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _logger.Debug("离开结果仪表盘");
        }
    }
}
