using Serilog;
using System.Collections.ObjectModel;

namespace MultiCameraSystem.ViewModels
{
    /// <summary>
    /// 日志查看器。
    ///
    /// MVVM / 生命周期修正：原实现在构造函数里订阅日志、却在 <c>OnNavigatedFrom</c> 里退订，
    /// 而 <c>IsNavigationTarget => true</c> 会让 Prism 复用同一个实例，
    /// 于是「离开日志页再回来」之后就再也收不到新日志了。
    /// 现在订阅/退订与导航生命周期严格配对。
    /// 日志级别的颜色也不再硬编码在 ViewModel 里，改由视图绑定主题画刷（切换主题即时生效）。
    /// </summary>
    public class LogViewerViewModel : BindableBase, INavigationAware
    {
        /// <summary>内存中保留的最大日志条数（避免长时间运行内存无限增长）</summary>
        private const int MaxEntries = 5000;

        private readonly Services.LogBroadcaster _logBroadcaster;
        private readonly ILogger _logger;
        private IDisposable? _subscription;

        public LogViewerViewModel(Services.LogBroadcaster logBroadcaster, ILogger logger)
        {
            _logBroadcaster = logBroadcaster;
            _logger = logger.ForContext<LogViewerViewModel>();

            foreach (var entry in _logBroadcaster.RecentLogs)
                LogEntries.Add(ToLogItem(entry));

            FilteredEntries = new ObservableCollection<LogItem>(LogEntries);

            ClearCommand = new DelegateCommand(ExecuteClear);
        }

        public ObservableCollection<LogItem> LogEntries { get; } = new();

        #region 过滤

        private string _filterLevel = "ALL";
        public string FilterLevel
        {
            get => _filterLevel;
            set
            {
                if (!SetProperty(ref _filterLevel, value)) return;

                RaisePropertyChanged(nameof(IsFilterAll));
                RaisePropertyChanged(nameof(IsFilterError));
                RaisePropertyChanged(nameof(IsFilterWarn));
                RaisePropertyChanged(nameof(IsFilterInfo));
                RaisePropertyChanged(nameof(IsFilterDebug));
                ApplyFilter();
            }
        }

        public bool IsFilterAll { get => FilterLevel == "ALL"; set { if (value) FilterLevel = "ALL"; } }
        public bool IsFilterError { get => FilterLevel == "ERROR"; set { if (value) FilterLevel = "ERROR"; } }
        public bool IsFilterWarn { get => FilterLevel == "WARN"; set { if (value) FilterLevel = "WARN"; } }
        public bool IsFilterInfo { get => FilterLevel == "INFO"; set { if (value) FilterLevel = "INFO"; } }
        public bool IsFilterDebug { get => FilterLevel == "DEBUG"; set { if (value) FilterLevel = "DEBUG"; } }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        private ObservableCollection<LogItem> _filteredEntries = new();
        public ObservableCollection<LogItem> FilteredEntries
        {
            get => _filteredEntries;
            private set => SetProperty(ref _filteredEntries, value);
        }

        private LogItem? _selectedEntry;
        public LogItem? SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
        }

        public DelegateCommand ClearCommand { get; }

        #endregion

        private static LogItem ToLogItem(Services.LogEntry entry) => new()
        {
            Timestamp = entry.Timestamp,
            Level = entry.Level,
            Message = entry.Message,
            Source = entry.Source
        };

        private void ExecuteClear()
        {
            LogEntries.Clear();
            FilteredEntries.Clear();
            _logBroadcaster.Clear();
            _logger.Information("日志已清空");
        }

        private bool PassesFilter(LogItem entry)
        {
            if (FilterLevel != "ALL" &&
                !string.Equals(entry.Level, FilterLevel, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(SearchText) &&
                entry.Message != null &&
                !entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void ApplyFilter()
            => FilteredEntries = new ObservableCollection<LogItem>(LogEntries.Where(PassesFilter));

        private void OnNewLog(Services.LogEntry entry)
        {
            var item = ToLogItem(entry);
            LogEntries.Add(item);

            while (LogEntries.Count > MaxEntries)
                LogEntries.RemoveAt(0);

            if (PassesFilter(item))
                FilteredEntries.Add(item);
        }

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 与 OnNavigatedFrom 配对，避免实例被复用后订阅丢失
            _subscription ??= _logBroadcaster.Subscribe(OnNewLog);
            ApplyFilter();
            _logger.Information("进入日志查看器");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        #endregion
    }

    /// <summary>
    /// 日志行。只承载数据，颜色交给视图按 <see cref="Level"/> 绑定主题画刷。
    /// </summary>
    public class LogItem
    {
        public DateTime Timestamp { get; init; }
        public string Level { get; init; } = "INFO";
        public string Message { get; init; } = "";
        public string? Source { get; init; }
    }
}
