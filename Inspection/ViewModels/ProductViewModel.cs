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
        }

        #region 集合

        public ObservableCollection<string> Products { get; } = new();
        public ObservableCollection<string> Solutions { get; } = new();
        public ObservableCollection<CameraBinding> Cameras { get; } = new();
        public ObservableCollection<string> Issues { get; } = new();

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

            var cfg = _repository.Load(SelectedProduct);
            Cameras.Clear();
            if (cfg != null)
            {
                foreach (var cam in cfg.Cameras) Cameras.Add(cam);
                RefreshIssues(cfg);
            }
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
