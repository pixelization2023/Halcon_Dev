using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Inspection.Models;
using Inspection.Services;
using MVS.Core;
using Prism.Commands;
using Prism.Navigation;
using Serilog;

namespace Inspection.ViewModels
{
    /// <summary>
    /// 自检与仿真。
    ///
    /// 解决"很多功能没法测试"的问题：
    ///   - 一键生成演示产品（含可用的 XML .hdev 方案 + 配方 + 存图路径）
    ///   - 一键运行依赖自检（Halcon / 方案文件 / PLC 授权 / 存图 / 数据库 / MES / PLC）
    ///   - 一键投入 OK / NG 仿真样张，走**真实编排器**跑通检测→结果→存图→上传→图表→日志
    /// 全部无需相机、PLC、MES、数据库即可验证。
    /// </summary>
    public class SelfTestViewModel : BindableBase, INavigationAware
    {
        private readonly SelfTestService _selfTest;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly ProductRepository _repository;
        private readonly IFileDialogService _fileDialogs;
        private readonly IClipboardService _clipboard;
        private readonly INavigationRequestSink _navigation;
        private readonly ILogger _logger;

        /// <summary>最近一次自检结果（复制/导出报告用；为空说明还没跑过）</summary>
        private SelfTestReport? _lastReport;

        public SelfTestViewModel(SelfTestService selfTest, InspectionOrchestrator orchestrator,
            ProductRepository repository, IFileDialogService fileDialogs,
            IClipboardService clipboard, INavigationRequestSink navigation, ILogger logger)
        {
            _selfTest = selfTest;
            _orchestrator = orchestrator;
            _repository = repository;
            _fileDialogs = fileDialogs;
            _clipboard = clipboard;
            _navigation = navigation;
            _logger = logger.ForContext<SelfTestViewModel>();

            RunCommand = new DelegateCommand(async () => await RunAsync());
            CreateDemoCommand = new DelegateCommand(ExecuteCreateDemo);
            LoadDemoCommand = new DelegateCommand(async () => await ExecuteLoadDemoAsync());
            SubmitOkCommand = new DelegateCommand(() => Submit(false));
            SubmitNgCommand = new DelegateCommand(() => Submit(true));
            CopyReportCommand = new DelegateCommand(ExecuteCopyReport);
            ExportReportCommand = new DelegateCommand(ExecuteExportReport);
            GoToTargetCommand = new DelegateCommand<SelfTestItem>(ExecuteGoToTarget);
        }

        #region 数据

        public ObservableCollection<SelfTestItem> Items { get; } = new();

        private string _statusText = "点「运行自检」检查当前环境；点「生成演示产品」可在无硬件条件下跑通全流程。";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _summaryText = "尚未自检";
        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string ProductText => _orchestrator.Configuration == null
            ? "未加载产品"
            : $"当前产品：{_orchestrator.Configuration.ProductName}（{_orchestrator.Configuration.Recipes.Count} 个配方）";

        #endregion

        public DelegateCommand RunCommand { get; }
        public DelegateCommand CreateDemoCommand { get; }
        public DelegateCommand LoadDemoCommand { get; }
        public DelegateCommand SubmitOkCommand { get; }
        public DelegateCommand SubmitNgCommand { get; }
        public DelegateCommand CopyReportCommand { get; }
        public DelegateCommand ExportReportCommand { get; }
        public DelegateCommand<SelfTestItem> GoToTargetCommand { get; }

        private async Task RunAsync()
        {
            IsBusy = true;
            StatusText = "自检中…";
            try
            {
                // Progress<T> 会回到创建它的同步上下文（UI 线程），所以直接改 StatusText 是安全的。
                // 自检里 Halcon 那几项首次要十几秒，没有这个回调，界面就只是一句干巴巴的"自检中…"。
                var progress = new Progress<string>(text => StatusText = text);
                var report = await _selfTest.RunAsync(progress);

                _lastReport = report;
                Items.Clear();
                // 异常项排在最前，方便一眼看到要处理什么
                foreach (var item in report.Items.OrderBy(i => i.SortOrder)) Items.Add(item);

                SummaryText = report.Summary;
                StatusText = report.FailCount == 0
                    ? "核心依赖均可用（未配置项为可选项，无对应硬件时属正常）"
                    : "存在异常项：按每行下方显示的「建议」处理后重试";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteCreateDemo()
        {
            try
            {
                var cfg = _selfTest.CreateDemoProduct();
                StatusText = $"已生成演示产品：{cfg.ProductName}（{_repository.GetProductDirectory(cfg.ProductName)}）";
                RaisePropertyChanged(nameof(ProductText));
            }
            catch (Exception ex)
            {
                StatusText = $"生成演示产品失败：{ex.Message}";
                _logger.Error(ex, "生成演示产品失败");
            }
        }

        private async Task ExecuteLoadDemoAsync()
        {
            IsBusy = true;
            try
            {
                if (!_selfTest.DemoProductExists())
                    _selfTest.CreateDemoProduct();

                await _orchestrator.LoadProductAsync(SelfTestService.DemoProductName);
                _orchestrator.Start();

                RaisePropertyChanged(nameof(ProductText));
                StatusText = "演示产品已加载并启动编排器，可点「投入 OK/NG 样张」验证检测链路";
            }
            catch (Exception ex)
            {
                StatusText = $"加载演示产品失败：{ex.Message}";
                _logger.Error(ex, "加载演示产品失败");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Submit(bool ng)
        {
            if (_orchestrator.Configuration == null)
            {
                StatusText = "请先「加载演示产品」或到「产品与方案」加载产品";
                return;
            }

            _orchestrator.Start();
            var ok = _orchestrator.SubmitSimulatedFrame(ng);
            StatusText = ok
                ? $"已投入{(ng ? " NG" : " OK")} 仿真样张，请看「检测主监控台」的结果/图表/日志"
                : "投入失败（产品未加载或队列已关闭）";
        }

        /// <summary>把用户带到该项对应的设置页（交给外壳执行，保证侧栏选中态同步）</summary>
        private void ExecuteGoToTarget(SelfTestItem? item)
        {
            if (item?.TargetView is not { Length: > 0 } target) return;

            try
            {
                _navigation.RequestNavigate(target);
                StatusText = $"已跳转到「{item.Name}」对应的设置页";
            }
            catch (Exception ex)
            {
                StatusText = $"跳转失败：{ex.Message}";
                _logger.Warning(ex, "自检项跳转失败: {Target}", target);
            }
        }

        /// <summary>把最近一次自检报告复制到剪贴板（现场直接粘给工程师，比截图强）</summary>
        private void ExecuteCopyReport()
        {
            if (_lastReport == null)
            {
                StatusText = "请先「运行自检」，再复制报告";
                return;
            }

            var text = _lastReport.ToText(_orchestrator.Configuration?.ProductName);
            StatusText = _clipboard.TrySetText(text)
                ? "自检报告已复制到剪贴板，可直接粘贴发送"
                : "复制失败：剪贴板正被其他程序占用，请重试";
        }

        /// <summary>把最近一次自检报告另存为 txt</summary>
        private void ExecuteExportReport()
        {
            if (_lastReport == null)
            {
                StatusText = "请先「运行自检」，再导出报告";
                return;
            }

            try
            {
                var defaultName = $"自检报告_{_lastReport.RunAt:yyyyMMdd_HHmmss}.txt";
                var path = _fileDialogs.SaveFile("导出自检报告", "文本文件|*.txt", defaultName);
                if (string.IsNullOrWhiteSpace(path)) return;   // 用户取消

                File.WriteAllText(path, _lastReport.ToText(_orchestrator.Configuration?.ProductName), Encoding.UTF8);
                StatusText = $"自检报告已保存：{path}";
            }
            catch (Exception ex)
            {
                StatusText = $"导出失败：{ex.Message}";
                _logger.Warning(ex, "导出自检报告失败");
            }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            RaisePropertyChanged(nameof(ProductText));

            // 第一次进入自动跑一次自检，省得用户不知道要先点「运行自检」
            if (Items.Count == 0 && !IsBusy)
                _ = RunAsync();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
