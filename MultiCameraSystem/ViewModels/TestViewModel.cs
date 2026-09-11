using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Halcon.Core;
using HalconDotNet;
using Inspection.Services;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    public class TestViewModel : BindableBase, INavigationAware
    {
        private readonly ILogger _logger;
        private HWindow? _halconWindow;

        /// <summary>
        /// 文件选择服务。
        /// 解耦要点：不再直接 <c>new OpenFileDialog()</c> —— ViewModel 不依赖 UI 类型，测试可替换。
        /// </summary>
        private readonly MVS.Core.IFileDialogService _fileDialogs;

        /// <summary>
        /// 检测服务 + 编排器：用于「整张检测」—— 按产品配方把**一张整图**跑成多个 PCS 结果。
        /// 单过程调试（Hdev / Hdvp 按钮）仍走本地的 HalconEngine，两条路互不影响。
        /// </summary>
        private readonly HalconInspectionService _halconInspection;
        private readonly InspectionOrchestrator _orchestrator;

        public TestViewModel(MVS.Core.IFileDialogService fileDialogs, ILogger logger,
            HalconInspectionService halconInspection, InspectionOrchestrator orchestrator)
        {
            _fileDialogs = fileDialogs;
            _logger = logger.ForContext<TestViewModel>();
            _halconInspection = halconInspection;
            _orchestrator = orchestrator;
        }

        private HObject? _currentImage;
        public HObject? CurrentImage
        {
            get => _currentImage;
            set => SetProperty(ref _currentImage, value);
        }

        private string _status = "就绪";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _selectedFile = "";
        public string SelectedFile
        {
            get => _selectedFile;
            set => SetProperty(ref _selectedFile, value);
        }

        private string _resultInfo = "";
        public string ResultInfo
        {
            get => _resultInfo;
            set => SetProperty(ref _resultInfo, value);
        }

        #region 加载图像

        private DelegateCommand? _loadImageCommand;
        public DelegateCommand LoadImageCommand =>
            _loadImageCommand ??= new DelegateCommand(() =>
            {
                var file = _fileDialogs.OpenFile("选择要读取的图片文件",
                    "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.tif|所有文件 (*.*)|*.*");
                if (!string.IsNullOrEmpty(file))
                {
                    try
                    {
                        HOperatorSet.ReadImage(out HObject img, file);
                        CurrentImage?.Dispose();
                        CurrentImage = img;
                        SelectedFile = file;
                        Status = $"已加载: {Path.GetFileName(file)}";
                        _logger.Information("TestView加载图像: {Path}", file);
                    }
                    catch (Exception ex)
                    {
                        Status = $"加载失败: {ex.Message}";
                        _logger.Error(ex, "TestView加载图像失败");
                    }
                }
            });

        #endregion

        #region 加载Hdev

        private DelegateCommand? _loadHdevCommand;
        public DelegateCommand Load_Hdev =>
            _loadHdevCommand ??= new DelegateCommand(() =>
            {
                var file = _fileDialogs.OpenFile("选择 HDevelop 程序文件 (.hdev)",
                    "HDevelop 文件|*.hdev|所有文件|*.*");
                if (string.IsNullOrEmpty(file)) return;

                Status = "执行中...";
                try
                {
                    using var engine = new HalconEngine();
                    string procedureDir = @"C:\Program Files\MVTec\HALCON-25.11-Progress\procedures";
                    engine.InitEngine(procedureDir);

                    // 获取本地函数名
                    var program = new HDevProgram(file);
                    var localProcs = program.GetLocalProcedureNames();
                    if (localProcs.Length == 0)
                    {
                        Status = "未找到本地函数";
                        _logger.Warning("hdev文件中未找到本地函数");
                        return;
                    }

                    _logger.Information("hdev本地函数: {Procs}", string.Join(", ", (string[])localProcs));
                    engine.LoadHdevProcedure(file, localProcs[0]);

                    if (CurrentImage != null && CurrentImage.IsInitialized())
                        engine.SetInputIconicParam("Image", CurrentImage);

                    if (_halconWindow != null)
                        engine.SetInputCtrlParam("WindowHandle", _halconWindow);

                    engine.Execute();

                    engine.GetOutputCtrlParam("OK", out HTuple ok);
                    engine.GetOutputIconicParam("NGRegion", out HObject ngRegion);

                    Status = ok ? "检测完成: OK" : "检测完成: NG";
                    ResultInfo = ok ? "Pass" : $"NG - Region: {ngRegion}";
                    _logger.Information("hdev执行完成: {Result}", ok ? "OK" : "NG");
                }
                catch (Exception ex)
                {
                    Status = $"执行失败: {ex.Message}";
                    _logger.Error(ex, "hdev执行失败");
                }
            });

        #endregion

        #region 加载Hdvp

        private DelegateCommand? _loadHdvpCommand;
        public DelegateCommand Load_Hdvp =>
            _loadHdvpCommand ??= new DelegateCommand(() =>
            {
                var file = _fileDialogs.OpenFile("选择 HDVP 程序文件",
                    "HDVP 文件|*.hdvp|所有文件|*.*");
                if (string.IsNullOrEmpty(file)) return;

                Status = "执行中...";
                try
                {
                    using var engine = new HalconEngine();
                    string procedureDir = @"C:\Program Files\MVTec\HALCON-25.11-Progress\procedures";
                    engine.InitEngine(procedureDir);

                    if (!engine.LoadHdvpProgram(file))
                    {
                        Status = "加载HDVP程序失败";
                        return;
                    }

                    if (CurrentImage != null && CurrentImage.IsInitialized())
                        engine.SetInputIconicParam("Image", CurrentImage);

                    engine.Execute();

                    engine.GetOutputCtrlParam("OK", out HTuple ok);
                    Status = ok ? "HDVP执行: OK" : "HDVP执行: NG";
                    _logger.Information("hdvp执行完成: {Result}", ok ? "OK" : "NG");
                }
                catch (Exception ex)
                {
                    Status = $"HDVP执行失败: {ex.Message}";
                    _logger.Error(ex, "hdvp执行失败");
                }
            });

        #endregion

        #region 运行Halcon脚本 (旧版兼容)

        private DelegateCommand? _runHalconProcessCommand;
        public DelegateCommand RunHalconProcessCommand =>
            _runHalconProcessCommand ??= new DelegateCommand(() =>
            {
                // 委托到Load_Hdev
                if (Load_Hdev.CanExecute())
                    Load_Hdev.Execute();
            });

        #endregion

        #region 整张检测（一张整图 → 多个 PCS 逐个出结果）

        /// <summary>整张检测的 PCS 结果行</summary>
        public ObservableCollection<PcsRow> PcsRows { get; } = new();

        private string _sheetSummary = "尚未执行整张检测";
        /// <summary>整张汇总（PCS 数 · NG 数 · 整张耗时）</summary>
        public string SheetSummary
        {
            get => _sheetSummary;
            set => SetProperty(ref _sheetSummary, value);
        }

        private bool _hasPcsResult;
        public bool HasPcsResult
        {
            get => _hasPcsResult;
            set
            {
                if (SetProperty(ref _hasPcsResult, value))
                    RaisePropertyChanged(nameof(HasNoPcsResult));
            }
        }

        /// <summary>
        /// 空态提示的可见性。界面只有正向的 <c>BoolToVisibility</c> 转换器，
        /// 所以反向条件由 VM 提供 —— 与 <c>DashboardViewModel.HasNoDisplayWindows</c> 同一做法。
        /// </summary>
        public bool HasNoPcsResult => !HasPcsResult;

        private bool _isRunningSheet;
        public bool IsRunningSheet
        {
            get => _isRunningSheet;
            set
            {
                if (SetProperty(ref _isRunningSheet, value))
                    RunWholeSheetCommand.RaiseCanExecuteChanged();
            }
        }

        private DelegateCommand? _runWholeSheetCommand;
        /// <summary>
        /// 整张检测：用当前已加载产品的**检测配方**跑这张图，按 PCS 拆开逐个出结果。
        ///
        /// 与「加载 Hdev / Hdvp」的区别：那两个是"单图 + 单过程"的裸调试；
        /// 这一条走的是生产链路的同一套服务（<see cref="HalconInspectionService.RunRecipe"/>），
        /// 所以它同时验证了配方、接口映射、PCS 拆分与判定。
        /// </summary>
        public DelegateCommand RunWholeSheetCommand =>
            _runWholeSheetCommand ??= new DelegateCommand(ExecuteRunWholeSheet, () => !IsRunningSheet);

        private DelegateCommand? _useSampleImageCommand;
        /// <summary>
        /// 载入一张**合成样张**（无需外部图片文件）。
        ///
        /// 存在的理由：现场常常手边没有可用的图片，而"配方能不能跑、能拆出几个 PCS、判定对不对"
        /// 这件事本身与图片从哪来无关。样张由 <see cref="SelfTestService.CreateSampleImage"/> 生成
        /// （与自检页的仿真投图同一份逻辑），所以走的是同一条真实链路。
        /// </summary>
        public DelegateCommand UseSampleImageCommand =>
            _useSampleImageCommand ??= new DelegateCommand(() =>
            {
                try
                {
                    CurrentImage?.Dispose();
                    CurrentImage = SelfTestService.CreateSampleImage(ng: false);
                    SelectedFile = "(合成样张 800×600)";
                    Status = "已载入合成样张，可直接点「▦ 整张检测」";
                    _logger.Information("流程测试载入合成样张");
                }
                catch (Exception ex)
                {
                    Status = $"生成合成样张失败：{ex.Message}";
                    _logger.Error(ex, "生成合成样张失败");
                }
            });

        private void ExecuteRunWholeSheet()
        {
            if (CurrentImage == null || !CurrentImage.IsInitialized())
            {
                Status = "请先「加载图像」，再执行整张检测";
                return;
            }

            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                Status = "整张检测需要产品配置：请先到「产品与方案」加载产品（自检页可一键生成演示产品）";
                return;
            }

            var recipe = cfg.Recipes.FirstOrDefault(r => !r.IsCodeRecipe) ?? cfg.Recipes.FirstOrDefault();
            if (recipe == null)
            {
                Status = "当前产品没有可用的检测配方";
                return;
            }

            IsRunningSheet = true;
            Status = $"整张检测中：{recipe.ProcedureName}（一张图拆多个 PCS）…";
            try
            {
                var result = _halconInspection.RunRecipe(cfg, recipe, CurrentImage, 1);

                PcsRows.Clear();
                foreach (var pcs in result.PcsResults.OrderBy(p => p.PcsInImage))
                {
                    PcsRows.Add(new PcsRow
                    {
                        Index = pcs.PcsInImage + 1,
                        IsNg = pcs.HasNg,
                        Items = string.Join(" ", pcs.ItemResults),
                        NgBoxCount = pcs.PointSets.Sum(boxes => boxes.Count)
                    });
                }

                var ngCount = result.PcsResults.Count(pcs => pcs.HasNg);
                SheetSummary = $"整张 {result.PcsResults.Count} 个 PCS · NG {ngCount} 个 · 耗时 {result.ElapsedMs} ms";
                HasPcsResult = PcsRows.Count > 0;
                Status = result.Success
                    ? $"整张检测完成：{SheetSummary}"
                    : $"整张检测失败：{result.Error}";

                // 本页只展示结果表格、不显示结果图 —— 及时释放，避免 HObject 句柄堆积
                result.DisposeImages();
                _logger.Information("流程测试-整张检测: 配方={Recipe} PCS={Pcs} NG={Ng} 耗时={Ms}ms",
                    recipe.ProcedureName, result.PcsResults.Count, ngCount, result.ElapsedMs);
            }
            catch (Exception ex)
            {
                Status = $"整张检测异常：{ex.Message}";
                _logger.Error(ex, "流程测试-整张检测失败");
            }
            finally
            {
                IsRunningSheet = false;
            }
        }

        #endregion

        #region 窗口就绪命令

        private ICommand? _windowReadyCommand;
        public ICommand WindowReadyCommand =>
            _windowReadyCommand ??= new DelegateCommand<HWindow>(h =>
            {
                _halconWindow = h;
                _logger.Information("TestView获取Halcon窗口句柄: {Handle}", h.ToString());
            });

        #endregion

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入流程测试界面");
            Status = "就绪 - 请加载图像和Halcon程序";
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }

    /// <summary>
    /// 「整张检测」结果里的一个 PCS 行。
    ///
    /// 说明：Halcon 一次执行会同时产出整张的所有 PCS，所以**单 PCS 耗时无法单独测量**；
    /// 耗时统一放在整张汇总里（<see cref="TestViewModel.SheetSummary"/>），
    /// 这里不虚构每行的耗时数字。
    /// </summary>
    public sealed class PcsRow
    {
        /// <summary>PCS 序号（1 开始，界面上给人看的）</summary>
        public int Index { get; init; }

        public bool IsNg { get; init; }

        public string Judgment => IsNg ? "NG" : "OK";

        public string Color => IsNg ? "#FF5252" : "#00E676";

        /// <summary>各检测项结果原样拼接（"0"=OK，"1"=NG，与原项目一致）</summary>
        public string Items { get; init; } = string.Empty;

        /// <summary>NG 框数量（各检测项的框数之和）</summary>
        public int NgBoxCount { get; init; }

        public string BoxText => NgBoxCount > 0 ? NgBoxCount.ToString() : "—";
    }
}
