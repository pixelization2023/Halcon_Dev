using System.Collections.ObjectModel;
using Inspection.Models;
using Inspection.Services;
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
        private readonly ILogger _logger;

        public SelfTestViewModel(SelfTestService selfTest, InspectionOrchestrator orchestrator,
            ProductRepository repository, ILogger logger)
        {
            _selfTest = selfTest;
            _orchestrator = orchestrator;
            _repository = repository;
            _logger = logger.ForContext<SelfTestViewModel>();

            RunCommand = new DelegateCommand(async () => await RunAsync());
            CreateDemoCommand = new DelegateCommand(ExecuteCreateDemo);
            LoadDemoCommand = new DelegateCommand(async () => await ExecuteLoadDemoAsync());
            SubmitOkCommand = new DelegateCommand(() => Submit(false));
            SubmitNgCommand = new DelegateCommand(() => Submit(true));
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

        private async Task RunAsync()
        {
            IsBusy = true;
            StatusText = "自检中…";
            try
            {
                var report = await _selfTest.RunAsync();

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
