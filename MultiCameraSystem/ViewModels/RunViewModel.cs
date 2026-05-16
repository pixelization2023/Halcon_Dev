using Halcon.Core;
using HalconDotNet;
using Microsoft.Win32;
using MultiCameraSystem.Services;
using Serilog;
using System.Collections.ObjectModel;
using System.IO;

namespace MultiCameraSystem.ViewModels
{
    /// <summary>
    /// 单张图像流程测试。
    ///
    /// MVVM 修正：
    /// <list type="bullet">
    /// <item><c>WindowReadyCommand</c> 原来每次取值都 new 一个新命令（绑定会反复执行/失效），
    /// 现在缓存为只读字段。</item>
    /// <item>Halcon 的加载/设参/执行/取结果搬进 <see cref="HalconRunService"/>，
    /// ViewModel 只保留状态与命令。</item>
    /// <item>过程名不再写死 <c>user_findcircle</c>，改为可配置。</item>
    /// </list>
    /// </summary>
    public class RunViewModel : BindableBase, INavigationAware
    {
        private readonly HalconRunService _halcon;
        private readonly ILogger _logger;
        private HWindow? _halconWindow;

        public RunViewModel(HalconRunService halcon, ILogger logger)
        {
            _halcon = halcon;
            _logger = logger.ForContext<RunViewModel>();

            WindowReadyCommand = new DelegateCommand<HWindow>(h => _halconWindow = h);
            LoadImageCommand = new DelegateCommand(ExecuteLoadImage);
            RunDetectionCommand = new DelegateCommand(ExecuteRunDetection);
            SelectProgramCommand = new DelegateCommand(ExecuteSelectProgram);
            ClearRecordsCommand = new DelegateCommand(() => { Records.Clear(); Status = "记录已清空"; });
        }

        #region 状态

        private HObject? _currentImage;
        public HObject? CurrentImage
        {
            get => _currentImage;
            set
            {
                var previous = _currentImage;
                if (SetProperty(ref _currentImage, value))
                    previous?.Dispose();
            }
        }

        private string _status = "就绪";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _elapsedTime = "";
        public string ElapsedTime
        {
            get => _elapsedTime;
            set => SetProperty(ref _elapsedTime, value);
        }

        private string _detectionResult = "";
        public string DetectionResult
        {
            get => _detectionResult;
            set => SetProperty(ref _detectionResult, value);
        }

        private bool _isOk;
        public bool IsOk
        {
            get => _isOk;
            set => SetProperty(ref _isOk, value);
        }

        private bool _isNg;
        public bool IsNg
        {
            get => _isNg;
            set => SetProperty(ref _isNg, value);
        }

        private ObservableCollection<DetectionRecord> _records = new();
        public ObservableCollection<DetectionRecord> Records
        {
            get => _records;
            set => SetProperty(ref _records, value);
        }

        private string _selectedProgram = "";
        /// <summary>选中的 .hdev / .hdvp 文件</summary>
        public string SelectedProgram
        {
            get => _selectedProgram;
            set => SetProperty(ref _selectedProgram, value);
        }

        private string _selectedProcedure = "user_findcircle";
        /// <summary>要执行的过程名（HDevelop 本地函数）</summary>
        public string SelectedProcedure
        {
            get => _selectedProcedure;
            set => SetProperty(ref _selectedProcedure, value);
        }

        private string _inputImageParam = "Image";
        /// <summary>输入图像参数名</summary>
        public string InputImageParam
        {
            get => _inputImageParam;
            set => SetProperty(ref _inputImageParam, value);
        }

        #endregion

        #region 命令

        /// <summary>HalconView 附加属性回传的窗口句柄（必须缓存，否则每次绑定都会新建命令）</summary>
        public DelegateCommand<HWindow> WindowReadyCommand { get; }

        public DelegateCommand LoadImageCommand { get; }
        public DelegateCommand RunDetectionCommand { get; }
        public DelegateCommand SelectProgramCommand { get; }
        public DelegateCommand ClearRecordsCommand { get; }

        private void ExecuteLoadImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择检测图像",
                Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                HOperatorSet.ReadImage(out HObject img, dialog.FileName);
                CurrentImage = img;
                Status = $"已加载: {Path.GetFileName(dialog.FileName)}";
                _logger.Information("加载图像: {Path}", dialog.FileName);
            }
            catch (Exception ex)
            {
                Status = $"加载失败: {ex.Message}";
                _logger.Error(ex, "加载图像失败");
            }
        }

        private void ExecuteRunDetection()
        {
            if (CurrentImage == null || !CurrentImage.IsInitialized())
            {
                Status = "请先加载图像";
                return;
            }

            if (string.IsNullOrEmpty(SelectedProgram))
            {
                Status = "请选择检测程序(.hdev)";
                return;
            }

            Status = "检测中...";
            IsOk = false;
            IsNg = false;

            var result = _halcon.Run(SelectedProgram, SelectedProcedure, InputImageParam, CurrentImage);

            if (!result.Success)
            {
                Status = $"检测失败: {result.Error}";
                return;
            }

            ElapsedTime = $"{result.ElapsedMs} ms";
            IsOk = result.Ok;
            IsNg = !result.Ok;
            DetectionResult = result.Ok ? $"OK ({result.ResultText})" : $"NG ({result.ResultText})";
            Status = result.Ok ? "检测通过" : "检测不通过";

            Records.Insert(0, new DetectionRecord
            {
                Time = DateTime.Now,
                Result = result.Ok ? "OK" : "NG",
                Score = result.Score,
                ElapsedMs = result.ElapsedMs
            });

            _logger.Information("检测完成: {Result}, 耗时 {Ms}ms", result.Ok ? "OK" : "NG", result.ElapsedMs);
        }

        private void ExecuteSelectProgram()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 HDevelop 程序",
                Filter = "HDevelop 文件|*.hdev;*.hdvp|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            SelectedProgram = dialog.FileName;
            Status = $"已选择: {Path.GetFileName(dialog.FileName)}";
            _logger.Information("选择检测程序: {Path}", dialog.FileName);
        }

        #endregion

        public void OnNavigatedTo(NavigationContext navigationContext)
            => _logger.Information("进入运行界面");

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }

    /// <summary>
    /// 检测记录。结果颜色不再由 ViewModel 决定（那是主题的职责），
    /// 视图根据 <see cref="Result"/> 绑定 SuccessBrush / ErrorBrush。
    /// </summary>
    public class DetectionRecord
    {
        public DateTime Time { get; set; }
        public string Result { get; set; } = "";
        public double Score { get; set; }
        public long ElapsedMs { get; set; }
        public bool IsOk => string.Equals(Result, "OK", StringComparison.OrdinalIgnoreCase);
        public string Display => $"[{Time:HH:mm:ss}] {Result}  Score:{Score:F2}  {ElapsedMs}ms";
    }
}
