using System.Collections.ObjectModel;
using System.IO;
using Inspection.Models;
using Inspection.Services;
using Prism.Commands;
using Prism.Navigation;
using Serilog;

namespace Inspection.ViewModels
{
    /// <summary>二维码与图片绑定的一行（迁移自 窗体.ImageCodeBuild 字典项）</summary>
    public class ImageCodeBindingItem : BindableBase
    {
        private string _codeIndex = "0";
        public string CodeIndex
        {
            get => _codeIndex;
            set => SetProperty(ref _codeIndex, value);
        }

        private string _imageIndexes = string.Empty;
        public string ImageIndexes
        {
            get => _imageIndexes;
            set => SetProperty(ref _imageIndexes, value);
        }
    }

    /// <summary>
    /// 检测配方与流程绑定配置。
    /// 迁移自 窗体.UI.FrmSetUp 的 "VM设置" 页（AddProPicBuild / LoadVM / 检测位置绑定 / 上传顺序绑定 /
    /// 二维码和图片绑定 / 主界面检测项）以及 FrmBuildPicPro、FrmBuildUploadOrder、FrmBuildCodeImage 三个弹窗。
    /// 原弹窗式绑定交互改为表格内联编辑。
    /// </summary>
    public class InspectionConfigViewModel : BindableBase, INavigationAware
    {
        private readonly ProductRepository _repository;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly HalconInspectionService _halcon;
        private readonly UserSessionService _session;
        private readonly ILogger _logger;

        public InspectionConfigViewModel(ProductRepository repository, InspectionOrchestrator orchestrator,
            HalconInspectionService halcon, UserSessionService session, ILogger logger)
        {
            _repository = repository;
            _orchestrator = orchestrator;
            _halcon = halcon;
            _session = session;
            _logger = logger.ForContext<InspectionConfigViewModel>();

            AddRecipeCommand = new DelegateCommand(ExecuteAddRecipe);
            RemoveRecipeCommand = new DelegateCommand<InspectionRecipe>(ExecuteRemoveRecipe);
            SaveCommand = new DelegateCommand(ExecuteSave);
            QueryInterfaceCommand = new DelegateCommand(async () => await ExecuteQueryInterfaceAsync());
            AddBindingCommand = new DelegateCommand(ExecuteAddBinding);
            RemoveBindingCommand = new DelegateCommand<ImageCodeBindingItem>(ExecuteRemoveBinding);
            AddDetectionItemCommand = new DelegateCommand(ExecuteAddDetectionItem);
            RemoveDetectionItemCommand = new DelegateCommand<string>(ExecuteRemoveDetectionItem);
            DiscoverProceduresCommand = new DelegateCommand(ExecuteDiscoverProcedures);
        }

        #region 集合

        public ObservableCollection<InspectionRecipe> Recipes { get; } = new();
        public ObservableCollection<ImageCodeBindingItem> CodeBindings { get; } = new();
        public ObservableCollection<string> DetectionItems { get; } = new();
        public ObservableCollection<string> DiscoveredProcedures { get; } = new();

        #endregion

        #region 绑定属性

        private string _newProcedureName = string.Empty;
        public string NewProcedureName
        {
            get => _newProcedureName;
            set => SetProperty(ref _newProcedureName, value);
        }

        private string _newImageIndexes = string.Empty;
        public string NewImageIndexes
        {
            get => _newImageIndexes;
            set => SetProperty(ref _newImageIndexes, value);
        }

        private int _newPcsPerImage = 1;
        public int NewPcsPerImage
        {
            get => _newPcsPerImage;
            set => SetProperty(ref _newPcsPerImage, value);
        }

        private int _newItemCount = 1;
        public int NewItemCount
        {
            get => _newItemCount;
            set => SetProperty(ref _newItemCount, value);
        }

        private string _newDetectionItem = string.Empty;
        public string NewDetectionItem
        {
            get => _newDetectionItem;
            set => SetProperty(ref _newDetectionItem, value);
        }

        private string _uploadOrderText = string.Empty;
        public string UploadOrderText
        {
            get => _uploadOrderText;
            set => SetProperty(ref _uploadOrderText, value);
        }

        private string _interfaceInfo = "尚未查询过程接口";
        public string InterfaceInfo
        {
            get => _interfaceInfo;
            set => SetProperty(ref _interfaceInfo, value);
        }

        private string _statusText = "请先在「产品方案」页面加载产品";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool CanEdit => _session.IsEngineer;

        #endregion

        #region 命令

        public DelegateCommand AddRecipeCommand { get; }
        public DelegateCommand<InspectionRecipe> RemoveRecipeCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand QueryInterfaceCommand { get; }
        public DelegateCommand AddBindingCommand { get; }
        public DelegateCommand<ImageCodeBindingItem> RemoveBindingCommand { get; }
        public DelegateCommand AddDetectionItemCommand { get; }
        public DelegateCommand<string> RemoveDetectionItemCommand { get; }
        public DelegateCommand DiscoverProceduresCommand { get; }

        private void ExecuteAddRecipe()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            if (string.IsNullOrWhiteSpace(NewProcedureName))
            {
                StatusText = "请填写 Halcon 过程名（对应原 VisionMaster 流程名，扫码流程需包含「扫码」）";
                return;
            }

            var recipe = new InspectionRecipe
            {
                ProcedureName = NewProcedureName.Trim(),
                ImageIndexes = NewImageIndexes.Trim(),
                PcsPerImage = Math.Max(1, NewPcsPerImage),
                ItemCount = Math.Max(1, NewItemCount),
                ProcedureFile = cfg.SolutionName
            };

            cfg.Recipes.Add(recipe);
            Recipes.Add(recipe);

            StatusText = $"已添加配方 {recipe.ProcedureName}";
            NewProcedureName = string.Empty;
            NewImageIndexes = string.Empty;
        }

        private void ExecuteRemoveRecipe(InspectionRecipe? recipe)
        {
            if (recipe == null) return;

            _orchestrator.Configuration?.Recipes.Remove(recipe);
            Recipes.Remove(recipe);
            StatusText = $"已移除配方 {recipe.ProcedureName}";
        }

        private void ExecuteSave()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            // 上传顺序（原 FrmMian.Savesol 中的 UploadOrderBuild 解析）
            cfg.UploadOrder = UploadOrderText
                .Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (cfg.UploadOrder.Count == 0)
            {
                cfg.UploadOrder = Enumerable.Range(1, Math.Max(1, cfg.SheetPcsTotal))
                    .Select(i => i.ToString()).ToList();
            }

            // 二维码与图片绑定（原 ImageCodeBuild，值以 "。" 分隔）
            cfg.ImageCodeBinding = CodeBindings
                .Where(b => !string.IsNullOrWhiteSpace(b.CodeIndex))
                .GroupBy(b => b.CodeIndex.Trim())
                .ToDictionary(g => g.Key, g => string.Join("。", g.Select(x => x.ImageIndexes)) + "。");

            // 主界面检测项（原 DetectionItems）
            cfg.DetectionItems = DetectionItems
                .Select((text, index) => (text, index))
                .ToDictionary(x => (x.index + 1).ToString(), x => x.text);

            if (_repository.Save(cfg))
            {
                _logger.Information("检测配置已保存");
                StatusText = "保存成功";
            }
            else
            {
                StatusText = "保存失败";
            }
        }

        private async Task ExecuteQueryInterfaceAsync()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null || Recipes.Count == 0)
            {
                StatusText = "请先添加配方";
                return;
            }

            var recipe = Recipes.FirstOrDefault();
            if (recipe == null) return;

            InterfaceInfo = "查询中…";
            await Task.Run(() =>
            {
                var iface = _halcon.QueryInterface(cfg, recipe);
                if (iface == null)
                {
                    InterfaceInfo = "查询失败：请确认 .hdev 文件中存在该本地函数（procedure）";
                    return;
                }

                InterfaceInfo =
                    $"过程 {iface.Name}\n" +
                    $"输入图像: {string.Join(", ", iface.InputImageParams.Select(p => p.Name))}\n" +
                    $"输出图像: {string.Join(", ", iface.OutputImageParams.Select(p => p.Name))}\n" +
                    $"输入控制: {string.Join(", ", iface.InputControlParams.Select(p => p.Name))}\n" +
                    $"输出控制: {string.Join(", ", iface.OutputControlParams.Select(p => p.Name))}";
            });
        }

        private void ExecuteAddBinding()
        {
            CodeBindings.Add(new ImageCodeBindingItem { CodeIndex = CodeBindings.Count.ToString(), ImageIndexes = string.Empty });
        }

        private void ExecuteRemoveBinding(ImageCodeBindingItem? item)
        {
            if (item != null) CodeBindings.Remove(item);
        }

        private void ExecuteAddDetectionItem()
        {
            if (string.IsNullOrWhiteSpace(NewDetectionItem)) return;

            DetectionItems.Add(NewDetectionItem.Trim());
            NewDetectionItem = string.Empty;
        }

        private void ExecuteRemoveDetectionItem(string? item)
        {
            if (!string.IsNullOrWhiteSpace(item)) DetectionItems.Remove(item);
        }

        private void ExecuteDiscoverProcedures()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            DiscoveredProcedures.Clear();
            foreach (var file in _repository.GetVisionProgramFiles(cfg.ProductName))
                DiscoveredProcedures.Add(Path.GetFileNameWithoutExtension(file));

            StatusText = $"发现 {DiscoveredProcedures.Count} 个候选方案文件";
        }

        #endregion

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext) => Reload();

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private void Reload()
        {
            Recipes.Clear();
            CodeBindings.Clear();
            DetectionItems.Clear();
            DiscoveredProcedures.Clear();

            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                StatusText = "请先在「产品方案」页面加载产品";
                return;
            }

            foreach (var recipe in cfg.Recipes) Recipes.Add(recipe);
            foreach (var kv in cfg.ImageCodeBinding)
                CodeBindings.Add(new ImageCodeBindingItem { CodeIndex = kv.Key, ImageIndexes = kv.Value });

            foreach (var kv in cfg.DetectionItems) DetectionItems.Add(kv.Value);

            UploadOrderText = string.Join(",", cfg.UploadOrder);
            StatusText = $"已加载 {cfg.ProductName} 的检测配置（{cfg.Recipes.Count} 个配方）";
        }

        #endregion
    }
}
