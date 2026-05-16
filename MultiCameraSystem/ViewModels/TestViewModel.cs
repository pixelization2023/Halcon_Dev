using Halcon.Core;
using HalconDotNet;
using Microsoft.Win32;
using Serilog;
using System.IO;
using System.Windows.Input;

namespace MultiCameraSystem.ViewModels
{
    public class TestViewModel : BindableBase, INavigationAware
    {
        private readonly ILogger _logger;
        private HWindow? _halconWindow;

        public TestViewModel(ILogger logger)
        {
            _logger = logger.ForContext<TestViewModel>();
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
                var dialog = new OpenFileDialog
                {
                    Title = "选择要读取的图片文件",
                    Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.tif|所有文件 (*.*)|*.*",
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        HOperatorSet.ReadImage(out HObject img, dialog.FileName);
                        CurrentImage?.Dispose();
                        CurrentImage = img;
                        SelectedFile = dialog.FileName;
                        Status = $"已加载: {Path.GetFileName(dialog.FileName)}";
                        _logger.Information("TestView加载图像: {Path}", dialog.FileName);
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
                var dialog = new OpenFileDialog
                {
                    Title = "选择 HDevelop 程序文件 (.hdev)",
                    Filter = "HDevelop 文件|*.hdev|所有文件|*.*",
                    Multiselect = false
                };
                if (dialog.ShowDialog() != true) return;

                Status = "执行中...";
                try
                {
                    using var engine = new HalconEngine();
                    string procedureDir = @"C:\Program Files\MVTec\HALCON-25.11-Progress\procedures";
                    engine.InitEngine(procedureDir);

                    // 获取本地函数名
                    var program = new HDevProgram(dialog.FileName);
                    var localProcs = program.GetLocalProcedureNames();
                    if (localProcs.Length == 0)
                    {
                        Status = "未找到本地函数";
                        _logger.Warning("hdev文件中未找到本地函数");
                        return;
                    }

                    _logger.Information("hdev本地函数: {Procs}", string.Join(", ", (string[])localProcs));
                    engine.LoadHdevProcedure(dialog.FileName, localProcs[0]);

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
                var dialog = new OpenFileDialog
                {
                    Title = "选择 HDVP 程序文件",
                    Filter = "HDVP 文件|*.hdvp|所有文件|*.*",
                    Multiselect = false
                };
                if (dialog.ShowDialog() != true) return;

                Status = "执行中...";
                try
                {
                    using var engine = new HalconEngine();
                    string procedureDir = @"C:\Program Files\MVTec\HALCON-25.11-Progress\procedures";
                    engine.InitEngine(procedureDir);

                    if (!engine.LoadHdvpProgram(dialog.FileName))
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
}
