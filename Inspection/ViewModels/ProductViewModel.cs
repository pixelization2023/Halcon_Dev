using System.Collections.ObjectModel;
using System.IO;
using Inspection.Models;
using Inspection.Services;
using Prism.Commands;
using Prism.Navigation;
using Serilog;

namespace Inspection.ViewModels
{
    /// <summary>
    /// 产品与方案管理。
    /// 迁移自 窗体.UI.FrmMian 的 "新建 / 加载产品" 分支 + 窗体.序列化类.SolModel，
    /// 以及 FrmSetUp 的 "保存产品" 按钮。
    /// </summary>
    public class ProductViewModel : BindableBase, INavigationAware
    {
        private readonly ProductRepository _repository;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly UserSessionService _session;
        private readonly ILogger _logger;

        public ProductViewModel(ProductRepository repository, InspectionOrchestrator orchestrator,
            UserSessionService session, ILogger logger)
        {
            _repository = repository;
            _orchestrator = orchestrator;
            _session = session;
            _logger = logger.ForContext<ProductViewModel>();

            CreateProductCommand = new DelegateCommand(ExecuteCreateProduct);
            RefreshCommand = new DelegateCommand(LoadProducts);
            LoadProductCommand = new DelegateCommand(async () => await ExecuteLoadProductAsync());
            SaveConfigCommand = new DelegateCommand(ExecuteSaveConfig);
            AddCameraCommand = new DelegateCommand(ExecuteAddCamera);
            RemoveCameraCommand = new DelegateCommand<CameraBinding>(ExecuteRemoveCamera);
            ApplyScaleCommand = new DelegateCommand(ExecuteApplyScale);
        }

        #region 集合

        public ObservableCollection<string> Products { get; } = new();
        public ObservableCollection<string> Solutions { get; } = new();
        public ObservableCollection<CameraBinding> Cameras { get; } = new();
        public ObservableCollection<string> Issues { get; } = new();

        /// <summary>相机绑定表「种类」列的下拉项。
        /// 之前这一列是自由文本，手打错一个字符（例如写成"海康像机"）就会在运行期匹配不到设备，
        /// 且没有任何提示 —— 改为枚举下拉从根上避免。</summary>
        public IReadOnlyList<DeviceKind> DeviceKinds { get; } = new[]
        {
            DeviceKind.HikCamera,
            DeviceKind.DahengCamera,
            DeviceKind.HikCodeReader
        };

        #endregion

        #region 绑定属性

        private string? _selectedProduct;
        public string? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                    OnProductSelected();
            }
        }

        private string? _selectedSolution;
        public string? SelectedSolution
        {
            get => _selectedSolution;
            set => SetProperty(ref _selectedSolution, value);
        }

        private string _newProductName = string.Empty;
        public string NewProductName
        {
            get => _newProductName;
            set => SetProperty(ref _newProductName, value);
        }

        private string _cameraKindText = "海康相机";
        public string CameraKindText
        {
            get => _cameraKindText;
            set => SetProperty(ref _cameraKindText, value);
        }

        public IReadOnlyList<string> CameraKinds { get; } = new[] { "海康相机", "大恒相机", "海康扫码枪" };

        // ---- 制品规模（原 PicNum / UploadNum / CodeNum）----
        // 这三个字段决定配方校验与上传顺序，以前界面上完全没有入口，只能手改 产品名.json。

        /// <summary>相机拍摄图片总数（原 PicNum）</summary>
        public int ImageTotal
        {
            get => _orchestrator.Configuration?.ImageTotal ?? 1;
            set
            {
                var cfg = _orchestrator.Configuration;
                if (cfg == null) return;
                if (cfg.ImageTotal == value) return;
                cfg.ImageTotal = Math.Max(1, value);
                RaisePropertyChanged();
            }
        }

        /// <summary>整张 PCS 总数（原 UploadNum）</summary>
        public int SheetPcsTotal
        {
            get => _orchestrator.Configuration?.SheetPcsTotal ?? 1;
            set
            {
                var cfg = _orchestrator.Configuration;
                if (cfg == null) return;
                if (cfg.SheetPcsTotal == value) return;
                cfg.SheetPcsTotal = Math.Max(1, value);
                RaisePropertyChanged();
            }
        }

        /// <summary>二维码个数（原 CodeNum）</summary>
        public int CodeCount
        {
            get => _orchestrator.Configuration?.CodeCount ?? 1;
            set
            {
                var cfg = _orchestrator.Configuration;
                if (cfg == null) return;
                if (cfg.CodeCount == value) return;
                cfg.CodeCount = Math.Max(1, value);
                RaisePropertyChanged();
            }
        }

        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public bool CanEdit => _session.IsEngineer;

        public ProductConfiguration? Configuration => _orchestrator.Configuration;

        #endregion

        #region 命令

        public DelegateCommand CreateProductCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand LoadProductCommand { get; }
        public DelegateCommand SaveConfigCommand { get; }
        public DelegateCommand AddCameraCommand { get; }
        public DelegateCommand<CameraBinding> RemoveCameraCommand { get; }
        public DelegateCommand ApplyScaleCommand { get; }

        private void ExecuteCreateProduct()
        {
            if (string.IsNullOrWhiteSpace(NewProductName))
            {
                StatusText = "请输入新产品名称";
                return;
            }

            if (_repository.CreateProduct(NewProductName.Trim()))
            {
                var cfg = _repository.CreateDefault(NewProductName.Trim());
                _repository.Save(cfg);

                StatusText = $"新建产品成功：{cfg.ProductName}";
                _logger.Information("新建产品成功: {Product}", cfg.ProductName);
                NewProductName = string.Empty;
                LoadProducts();
            }
            else
            {
                StatusText = "新建产品失败";
            }
        }

        private void OnProductSelected()
        {
            Solutions.Clear();

            if (string.IsNullOrWhiteSpace(SelectedProduct)) return;

            foreach (var file in _repository.GetVisionProgramFiles(SelectedProduct))
                Solutions.Add(file);

            if (Solutions.Count > 0 && string.IsNullOrEmpty(SelectedSolution))
                SelectedSolution = Solutions[0];

            // 注意：这里只是"预览已选中产品的配置"，并不加载到编排器（那是「加载产品」按钮的职责），
            // 所以制品规模三个输入框此时是不可编辑的空值状态。
            var cfg = _repository.Load(SelectedProduct);
            Cameras.Clear();
            if (cfg != null)
            {
                foreach (var cam in cfg.Cameras) Cameras.Add(cam);
                RefreshIssues(cfg);
            }
            RaiseScaleChanged();
        }

        private async Task ExecuteLoadProductAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedProduct))
            {
                StatusText = "请先选择产品";
                return;
            }

            IsBusy = true;
            try
            {
                var cfg = _repository.Load(SelectedProduct) ?? _repository.CreateDefault(SelectedProduct);
                if (!string.IsNullOrWhiteSpace(SelectedSolution))
                {
                    cfg.SolutionName = SelectedSolution!;
                    _repository.LocateVisionProgram(cfg);
                }

                await _orchestrator.LoadProductAsync(SelectedProduct);

                Cameras.Clear();
                foreach (var cam in cfg.Cameras) Cameras.Add(cam);
                RefreshIssues(cfg);
                RaiseScaleChanged();

                StatusText = $"产品已加载：{SelectedProduct}";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载产品失败");
                StatusText = "加载产品失败: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteSaveConfig()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                StatusText = "请先加载产品";
                return;
            }

            ValidateUploadOrder(cfg);

            if (_repository.Save(cfg))
            {
                StatusText = "保存成功";
                RefreshIssues(cfg);
            }
            else
            {
                StatusText = "保存失败";
            }
        }

        private void ExecuteAddCamera()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                StatusText = "请先加载产品";
                return;
            }

            var kind = CameraKindText switch
            {
                "大恒相机" => DeviceKind.DahengCamera,
                "海康扫码枪" => DeviceKind.HikCodeReader,
                _ => DeviceKind.HikCamera
            };

            var prefix = kind == DeviceKind.HikCodeReader ? "海康扫码枪" : CameraKindText;
            var index = cfg.Cameras.Count(c => c.Kind == kind);
            var binding = new CameraBinding { Name = prefix + index, Order = index, Kind = kind };

            cfg.Cameras.Add(binding);
            Cameras.Add(binding);

            _logger.Information("新增相机绑定: {Name}", binding.Name);
            StatusText = $"已添加 {binding.Name}，请填写 SN 后保存";
        }

        private void ExecuteRemoveCamera(CameraBinding? binding)
        {
            if (binding == null) return;

            var cfg = _orchestrator.Configuration;
            cfg?.Cameras.Remove(binding);
            Cameras.Remove(binding);
            StatusText = $"已移除 {binding.Name}";
        }

        /// <summary>
        /// 应用制品规模并立刻校验。
        /// 上传顺序（UploadOrder）的长度必须与整张 PCS 数一致，否则上传阶段的绑定会错位，
        /// 这里在改完规模后按 PCS 总数把上传顺序补全，并给出校验清单。
        /// </summary>
        private void ExecuteApplyScale()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                StatusText = "请先加载产品";
                return;
            }

            if (cfg.UploadOrder.Count != cfg.SheetPcsTotal)
            {
                var previous = cfg.UploadOrder.Count;
                cfg.UploadOrder = Enumerable.Range(1, Math.Max(1, cfg.SheetPcsTotal))
                    .Select(i => i.ToString()).ToList();
                _logger.Information("上传顺序按 PCS 总数补全: {Old} -> {New}", previous, cfg.UploadOrder.Count);
            }

            RefreshIssues(cfg);
            StatusText = $"制品规模已应用：图片 {cfg.ImageTotal} 张 / PCS {cfg.SheetPcsTotal} 个 / 二维码 {cfg.CodeCount} 个" +
                         (Issues.Count > 0 ? $"（{Issues.Count} 项待处理）" : "（校验通过）");
        }

        #endregion

        #region 辅助

        /// <summary>按 Ctrl 时的上传顺序解析（原 SerLion.Savesol 中的 UploadOrderBuild 解析）</summary>
        private void ValidateUploadOrder(ProductConfiguration cfg)
        {
            if (cfg.UploadOrder.Count == 0)
            {
                cfg.UploadOrder = Enumerable.Range(1, Math.Max(1, cfg.SheetPcsTotal))
                    .Select(i => i.ToString()).ToList();
                return;
            }

            if (cfg.UploadOrder.Count != cfg.SheetPcsTotal)
                StatusText = $"警告：上传顺序个数({cfg.UploadOrder.Count})与整张 PCS 数({cfg.SheetPcsTotal})不一致";
        }

        private void RefreshIssues(ProductConfiguration cfg)
        {
            Issues.Clear();
            foreach (var issue in cfg.Validate()) Issues.Add(issue);
        }

        /// <summary>制品规模三个输入框的显示值来自 Configuration，配置变化后要通知一次</summary>
        private void RaiseScaleChanged()
        {
            RaisePropertyChanged(nameof(ImageTotal));
            RaisePropertyChanged(nameof(SheetPcsTotal));
            RaisePropertyChanged(nameof(CodeCount));
        }

        public void LoadProducts()
        {
            var current = SelectedProduct;

            Products.Clear();
            foreach (var product in _repository.GetProducts()) Products.Add(product);

            SelectedProduct = Products.Contains(current ?? string.Empty) ? current : Products.FirstOrDefault();
            StatusText = $"共 {Products.Count} 个产品";
        }

        #endregion

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext) => LoadProducts();

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        #endregion
    }
}
