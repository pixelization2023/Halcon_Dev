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
            ValidateInterfaceCommand = new DelegateCommand(async () => await ExecuteValidateInterfaceAsync());
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

        /// <summary>
        /// 「配方 ↔ 方案接口」一致性校验结果（一条一行）。
        /// 为什么要有：配方模板名与 .hdev 实际输出名对不上时，程序"检测成功、日志正常"，
        /// 但界面上没有图、结果串为空 —— 没有任何报错，只能靠人肉比对。这里把它显式列出来。
        /// </summary>
        public ObservableCollection<string> InterfaceIssues { get; } = new();

        /// <summary>多窗口显示：每个窗口的绑定规则（与 ProductConfiguration.Display.Windows 同步）</summary>
        public ObservableCollection<DisplayWindowSpec> DisplayWindows { get; } = new();

        /// <summary>窗口绑定方式下拉项</summary>
        public IReadOnlyList<DisplayBindMode> BindModes { get; } = new[]
        {
            DisplayBindMode.ByPcs,
            DisplayBindMode.ByKey,
            DisplayBindMode.ByImage,
            DisplayBindMode.Follow
        };

        /// <summary>窗口图像来源下拉项</summary>
        public IReadOnlyList<DisplayImageSource> ImageSources { get; } = new[]
        {
            DisplayImageSource.Auto,
            DisplayImageSource.ResultImage,
            DisplayImageSource.OriginalImage
        };

        /// <summary>窗口判定过滤下拉项</summary>
        public IReadOnlyList<DisplayJudgmentFilter> JudgmentFilters { get; } = new[]
        {
            DisplayJudgmentFilter.Any,
            DisplayJudgmentFilter.OkOnly,
            DisplayJudgmentFilter.NgOnly
        };

        /// <summary>窗口缩放方式下拉项</summary>
        public IReadOnlyList<DisplayScaleMode> ScaleModes { get; } = new[]
        {
            DisplayScaleMode.Fit,
            DisplayScaleMode.None
        };

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

        // ---- 多窗口显示设置（对应 ProductConfiguration.Display）----

        private bool _displayEnabled = true;
        /// <summary>是否启用多窗口显示</summary>
        public bool DisplayEnabled
        {
            get => _displayEnabled;
            set => SetProperty(ref _displayEnabled, value);
        }

        private int _displayWindowCount = 4;
        /// <summary>显示窗口个数（1~16）</summary>
        public int DisplayWindowCount
        {
            get => _displayWindowCount;
            set
            {
                var clamped = Math.Clamp(value, 1, DisplaySettings.MaxWindowCount);
                if (SetProperty(ref _displayWindowCount, clamped))
                    SyncDisplayWindowList();
            }
        }

        private int _displayColumns = 2;
        /// <summary>每行窗口数（0 = 自动）</summary>
        public int DisplayColumns
        {
            get => _displayColumns;
            set => SetProperty(ref _displayColumns, Math.Clamp(value, 0, DisplaySettings.MaxWindowCount));
        }

        private EmptyWindowMode _emptyWindowMode = EmptyWindowMode.Empty;
        /// <summary>没有结果时窗口里显示什么</summary>
        public EmptyWindowMode EmptyWindowMode
        {
            get => _emptyWindowMode;
            set => SetProperty(ref _emptyWindowMode, value);
        }

        /// <summary>空窗显示模式下拉项</summary>
        public IReadOnlyList<EmptyWindowMode> EmptyModes { get; } = new[]
        {
            EmptyWindowMode.Empty,
            EmptyWindowMode.OriginalImage
        };

        public bool CanEdit => _session.IsEngineer;

        #endregion

        #region 命令

        public DelegateCommand AddRecipeCommand { get; }
        public DelegateCommand<InspectionRecipe> RemoveRecipeCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand QueryInterfaceCommand { get; }
        public DelegateCommand ValidateInterfaceCommand { get; }
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

            // 多窗口显示设置（v3 新增：窗口个数 / 每行列数 / 每格绑定哪个 PCS）
            ApplyDisplaySettings(cfg);

            if (_repository.Save(cfg))
            {
                _logger.Information("检测配置已保存");
                StatusText = "保存成功";

                // 通知主监控台立刻按新的显示设置重建窗口。
                // 不通知的话，用户在配置页改完绑定方式 / NG 框样式，
                // 切回主监控台看到的还是旧规则（要等下次加载产品才生效）。
                _orchestrator.NotifyConfigurationChanged();

                RunInterfaceValidation(cfg);
            }
            else
            {
                StatusText = "保存失败";
            }
        }

        /// <summary>
        /// 保存后同步跑一次接口一致性校验（引擎已缓存，开销很小），
        /// 把「配方与方案不一致」直接显示出来 —— 这是 P0-1 的现场防护。
        /// </summary>
        private void RunInterfaceValidation(ProductConfiguration cfg)
        {
            InterfaceIssues.Clear();

            foreach (var recipe in cfg.Recipes.ToList())
            {
                List<string> issues;
                try
                {
                    issues = _halcon.ValidateRecipeAgainstInterface(cfg, recipe);
                }
                catch (Exception ex)
                {
                    issues = new List<string> { $"校验异常: {ex.Message}" };
                }

                if (issues.Count == 0)
                {
                    InterfaceIssues.Add($"✔ {recipe.ProcedureName}：配方与方案接口一致");
                }
                else
                {
                    foreach (var issue in issues)
                        InterfaceIssues.Add($"✘ {recipe.ProcedureName}：{issue}");
                }
            }

            var problemCount = InterfaceIssues.Count(i => i.StartsWith("✘", StringComparison.Ordinal));
            if (problemCount > 0)
            {
                StatusText = $"保存成功，但发现 {problemCount} 处配方与方案接口不一致（见下方清单）";
                _logger.Warning("保存后一致性校验发现 {Count} 处问题", problemCount);
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

        /// <summary>
        /// 配方 ↔ 方案接口 一致性校验。
        /// 逐条配方比对「模板名」与「.hdev 实际声明的参数名」，把不一致的地方列出来。
        /// 这是 P0-1 的界面入口：以前"带图不出来"只能靠人肉查 .hdev。
        /// </summary>
        private async Task ExecuteValidateInterfaceAsync()
        {
            var cfg = _orchestrator.Configuration;
            if (cfg == null)
            {
                StatusText = "请先加载产品";
                return;
            }

            if (cfg.Recipes.Count == 0)
            {
                StatusText = "请先添加配方";
                return;
            }

            InterfaceIssues.Clear();
            StatusText = "正在校验配方与方案接口…";

            var recipes = cfg.Recipes.ToList();
            await Task.Run(() =>
            {
                foreach (var recipe in recipes)
                {
                    List<string> issues;
                    try
                    {
                        issues = _halcon.ValidateRecipeAgainstInterface(cfg, recipe);
                    }
                    catch (Exception ex)
                    {
                        issues = new List<string> { $"校验异常: {ex.Message}" };
                    }

                    if (issues.Count == 0)
                    {
                        InterfaceIssues.Add($"✔ {recipe.ProcedureName}：配方与方案接口一致");
                    }
                    else
                    {
                        foreach (var issue in issues)
                            InterfaceIssues.Add($"✘ {recipe.ProcedureName}：{issue}");
                    }
                }
            });

            var problemCount = InterfaceIssues.Count(i => i.StartsWith("✘", StringComparison.Ordinal));
            StatusText = problemCount == 0
                ? $"校验通过：{recipes.Count} 个配方与方案接口一致"
                : $"发现 {problemCount} 处不一致（见下方清单）；运行时会按顺序兜底，但建议改齐";
        }

        private void ExecuteAddBinding()
        {
            CodeBindings.Add(new ImageCodeBindingItem { CodeIndex = CodeBindings.Count.ToString(), ImageIndexes = string.Empty });
        }

        /// <summary>
        /// 把「窗口个数」同步到窗口列表：多了裁掉、少了补默认绑定。
        /// 默认绑定为"第 i 个窗口看 PCS i"，现场改起来最直观。
        /// </summary>
        private void SyncDisplayWindowList()
        {
            // 裁掉多余的（从尾部）
            while (DisplayWindows.Count > _displayWindowCount)
                DisplayWindows.RemoveAt(DisplayWindows.Count - 1);

            // 补齐缺的
            for (int i = DisplayWindows.Count; i < _displayWindowCount; i++)
            {
                DisplayWindows.Add(new DisplayWindowSpec
                {
                    Index = i + 1,
                    Bind = DisplayBindMode.ByPcs,
                    Target = (i + 1).ToString(),
                    Source = DisplayImageSource.Auto,
                    ShowNgBoxes = true,
                    ShowItemText = true
                });
            }

            // 重排序号，保证与界面行号一致
            for (int i = 0; i < DisplayWindows.Count; i++)
                DisplayWindows[i].Index = i + 1;
        }

        /// <summary>把界面上的显示设置写回产品配置</summary>
        private void ApplyDisplaySettings(ProductConfiguration cfg)
        {
            cfg.Display ??= new DisplaySettings();

            cfg.Display.Enabled = DisplayEnabled;
            cfg.Display.WindowCount = Math.Clamp(_displayWindowCount, 1, DisplaySettings.MaxWindowCount);
            cfg.Display.Columns = Math.Clamp(_displayColumns, 0, cfg.Display.WindowCount);
            cfg.Display.EmptyMode = EmptyWindowMode;

            cfg.Display.Windows = DisplayWindows.Take(cfg.Display.WindowCount)
                .Select(w => new DisplayWindowSpec
                {
                    Index = w.Index,
                    Title = w.Title,
                    Bind = w.Bind,
                    Target = w.Target,
                    Source = w.Source,
                    ShowNgBoxes = w.ShowNgBoxes,
                    ShowItemText = w.ShowItemText,
                    Filter = w.Filter,
                    BoxColor = w.BoxColor,
                    BoxLineWidth = w.BoxLineWidth,
                    Scale = w.Scale
                })
                .ToList();

            cfg.Display.Normalize();
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
            DisplayWindows.Clear();

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

            // 多窗口显示设置：先把配置规整化（老配置可能没有窗口定义），再灌进界面集合
            cfg.NormalizeDisplay();
            _displayEnabled = cfg.Display.Enabled;
            _displayWindowCount = cfg.Display.WindowCount;
            _displayColumns = cfg.Display.Columns;
            _emptyWindowMode = cfg.Display.EmptyMode;

            foreach (var spec in cfg.Display.Windows)
                DisplayWindows.Add(spec);

            SyncDisplayWindowList();

            RaisePropertyChanged(nameof(DisplayEnabled));
            RaisePropertyChanged(nameof(DisplayWindowCount));
            RaisePropertyChanged(nameof(DisplayColumns));
            RaisePropertyChanged(nameof(EmptyWindowMode));

            UploadOrderText = string.Join(",", cfg.UploadOrder);
            StatusText = $"已加载 {cfg.ProductName} 的检测配置（{cfg.Recipes.Count} 个配方，" +
                         $"{cfg.Display.WindowCount} 个显示窗口）";
        }

        #endregion
    }
}
