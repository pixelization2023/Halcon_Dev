using System.Collections.ObjectModel;
using MultiCameraSystem.Models;
using MultiCameraSystem.Services;
using MultiCameraSystem.Themes;
using Prism.Commands;
using Prism.Navigation;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    /// <summary>外观设置页里的一个主题卡片</summary>
    public class ThemeOption : BindableBase
    {
        public ThemeOption(ThemeDefinition definition)
        {
            Definition = definition;
        }

        public ThemeDefinition Definition { get; }

        public string DisplayName => Definition.DisplayName;
        public string Description => Definition.Description;
        public string Primary => Definition.Primary;
        public string Secondary => Definition.Secondary;

        /// <summary>是否为自定义主题（界面上加角标、可删除）</summary>
        public bool IsCustom => Definition.IsCustom;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    /// <summary>「高级覆盖」里的一行：语义画刷键 → 颜色</summary>
    public class ColorOverrideItem : BindableBase
    {
        public ColorOverrideItem(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }

        private string _value;
        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }

    /// <summary>
    /// 外观主题设置。
    ///
    /// 迁移前颜色全部写死在 App.xaml（23 处硬编码），这里统一由 <see cref="ThemeService"/>
    /// 按 MaterialDesignInXamlToolkit 的 PaletteHelper 机制在运行期生成：
    /// 支持 12 套内置主题、深/浅/跟随系统，以及**用户自定义主题**（自选主色/次色 + 逐项覆盖）。
    /// </summary>
    public class ThemeSettingsViewModel : BindableBase, INavigationAware
    {
        private readonly ThemeService _themeService;
        private readonly SettingsService _settings;
        private readonly ILogger _logger;

        private bool _syncing;

        public ThemeSettingsViewModel(ThemeService themeService, SettingsService settings, ILogger logger)
        {
            _themeService = themeService;
            _settings = settings;
            _logger = logger.ForContext<ThemeSettingsViewModel>();

            BaseThemeOptions = new[] { "Dark", "Light", "System" };
            BrushKeys = _themeService.SemanticBrushKeys;

            ApplyCommand = new DelegateCommand<ThemeOption>(ExecuteApply);
            ResetCommand = new DelegateCommand(ExecuteReset);
            SaveCommand = new DelegateCommand(ExecuteSave);
            ToggleBaseThemeCommand = new DelegateCommand(ExecuteToggleBaseTheme);

            NewCustomThemeCommand = new DelegateCommand(ExecuteNewCustomTheme);
            SaveCustomThemeCommand = new DelegateCommand(ExecuteSaveCustomTheme);
            DeleteCustomThemeCommand = new DelegateCommand(ExecuteDeleteCustomTheme);
            AddOverrideCommand = new DelegateCommand(ExecuteAddOverride);
            RemoveOverrideCommand = new DelegateCommand<ColorOverrideItem>(ExecuteRemoveOverride);
            ApplyPresetColorCommand = new DelegateCommand<string>(ExecuteApplyPresetPrimary);

            _themeService.ThemeChanged += (_, _) => SyncFromService();
            _themeService.ThemesChanged += (_, _) => ReloadOptions();

            ReloadOptions();
            SyncFromService();
            StartNewCustomTheme();
        }

        #region 数据

        public ObservableCollection<ThemeOption> Options { get; } = new();

        /// <summary>自定义主题的高级覆盖编辑行</summary>
        public ObservableCollection<ColorOverrideItem> EditOverrides { get; } = new();

        public IReadOnlyList<string> BaseThemeOptions { get; }

        /// <summary>可供覆盖的语义画刷键</summary>
        public IReadOnlyList<string> BrushKeys { get; }

        /// <summary>常用主色快捷选择</summary>
        public IReadOnlyList<string> PresetColors { get; } = new[]
        {
            "#00BCD4", "#2196F3", "#3F51B5", "#009688", "#43A047", "#8BC34A",
            "#FB8C00", "#F4511E", "#E91E63", "#9C27B0", "#546E7A", "#E53935"
        };

        private string _selectedBaseTheme = "Dark";
        public string SelectedBaseTheme
        {
            get => _selectedBaseTheme;
            set
            {
                if (!SetProperty(ref _selectedBaseTheme, value)) return;
                if (_syncing) return;

                ApplyTheme(_current?.Definition.Key, value);
                RefreshPreview();
            }
        }

        private ThemeOption? _current;

        private ThemeOption? _selectedOption;
        public ThemeOption? SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (!SetProperty(ref _selectedOption, value)) return;
                if (_syncing || value == null) return;

                ApplyTheme(value.Definition.Key, SelectedBaseTheme);
                RefreshPreview();

                if (value.IsCustom) LoadCustomIntoEditor(value.Definition);
            }
        }

        /// <summary>当前选中主题 + 明暗的完整色板（用于右侧实时预览）</summary>
        private IReadOnlyDictionary<string, string> _preview = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> Preview
        {
            get => _preview;
            private set => SetProperty(ref _preview, value);
        }

        private string _statusText = "选择一套主题即可立即生效；也可以自定义主色，无需重启";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        #endregion

        #region 自定义主题编辑

        /// <summary>正在编辑的自定义主题键（null 表示新建）</summary>
        private string? _editingKey;

        private bool _isEditingCustom;
        /// <summary>是否正在编辑自定义主题</summary>
        public bool IsEditingCustom
        {
            get => _isEditingCustom;
            private set => SetProperty(ref _isEditingCustom, value);
        }

        private string _editName = "我的主题";
        public string EditName
        {
            get => _editName;
            set
            {
                if (SetProperty(ref _editName, value)) RefreshCustomPreview();
            }
        }

        private string _editPrimary = "#00BCD4";
        public string EditPrimary
        {
            get => _editPrimary;
            set
            {
                if (SetProperty(ref _editPrimary, value)) RefreshCustomPreview();
            }
        }

        private string _editSecondary = "#26C6DA";
        public string EditSecondary
        {
            get => _editSecondary;
            set
            {
                if (SetProperty(ref _editSecondary, value)) RefreshCustomPreview();
            }
        }

        private string _selectedBrushKey = "SuccessBrush";
        public string SelectedBrushKey
        {
            get => _selectedBrushKey;
            set => SetProperty(ref _selectedBrushKey, value);
        }

        private string _newOverrideValue = "#00E676";
        public string NewOverrideValue
        {
            get => _newOverrideValue;
            set => SetProperty(ref _newOverrideValue, value);
        }

        /// <summary>自定义主题的实时预览色板（跟随编辑框变化）</summary>
        private IReadOnlyDictionary<string, string> _customPreview = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> CustomPreview
        {
            get => _customPreview;
            private set => SetProperty(ref _customPreview, value);
        }

        public bool CanDeleteCustomTheme => _selectedOption?.IsCustom == true;

        #endregion

        #region 命令

        public DelegateCommand<ThemeOption> ApplyCommand { get; }
        public DelegateCommand ResetCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand ToggleBaseThemeCommand { get; }

        public DelegateCommand NewCustomThemeCommand { get; }
        public DelegateCommand SaveCustomThemeCommand { get; }
        public DelegateCommand DeleteCustomThemeCommand { get; }
        public DelegateCommand AddOverrideCommand { get; }
        public DelegateCommand<ColorOverrideItem> RemoveOverrideCommand { get; }
        public DelegateCommand<string> ApplyPresetColorCommand { get; }

        private void ExecuteApply(ThemeOption? option)
        {
            if (option == null) return;
            SelectedOption = option;
        }

        private void ExecuteReset()
        {
            ApplyTheme(ThemeCatalog.DefaultKey, "Dark");
            SyncFromService();
            StatusText = "已恢复默认外观（科技青 / 深色）";
        }

        private void ExecuteSave()
        {
            PersistThemeSelection();
            StatusText = "外观设置已保存";
        }

        private void ExecuteToggleBaseTheme()
            => SelectedBaseTheme = string.Equals(SelectedBaseTheme, "Dark", StringComparison.OrdinalIgnoreCase)
                ? "Light" : "Dark";

        private void ExecuteNewCustomTheme() => StartNewCustomTheme();

        private void ExecuteSaveCustomTheme()
        {
            if (string.IsNullOrWhiteSpace(_editPrimary) || string.IsNullOrWhiteSpace(_editSecondary))
            {
                StatusText = "请先选择主色与次色";
                return;
            }

            var ui = _settings.Current.UI;
            var key = _editingKey ?? ThemeCatalog.NewCustomKey();

            var setting = ui.CustomThemes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
            if (setting == null)
            {
                setting = new CustomThemeSetting { Key = key };
                ui.CustomThemes.Add(setting);
            }

            setting.DisplayName = string.IsNullOrWhiteSpace(EditName) ? "我的主题" : EditName.Trim();
            setting.Primary = EditPrimary;
            setting.Secondary = EditSecondary;
            setting.ColorOverrides = EditOverrides
                .Where(o => !string.IsNullOrWhiteSpace(o.Key) && !string.IsNullOrWhiteSpace(o.Value))
                .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);

            _settings.Update(s =>
            {
                s.UI.Theme = key;
                s.UI.BaseTheme = SelectedBaseTheme;
            });

            _themeService.SetCustomThemes(ui.CustomThemes);
            _editingKey = key;

            ApplyTheme(key, SelectedBaseTheme);
            SyncFromService();
            LoadCustomIntoEditor(_themeService.Find(key)!);

            StatusText = $"自定义主题「{setting.DisplayName}」已保存并应用";
            _logger.Information("自定义主题已保存: {Key} {Name} {Primary}/{Secondary} 覆盖 {Count} 项",
                key, setting.DisplayName, setting.Primary, setting.Secondary, setting.ColorOverrides.Count);
        }

        private void ExecuteDeleteCustomTheme()
        {
            if (_selectedOption?.IsCustom != true) return;

            var key = _selectedOption.Definition.Key;
            var name = _selectedOption.Definition.DisplayName;

            _settings.Update(s => s.UI.CustomThemes.RemoveAll(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)));
            _themeService.SetCustomThemes(_settings.Current.UI.CustomThemes);

            _editingKey = null;
            _settings.Update(s => s.UI.Theme = ThemeCatalog.DefaultKey);
            ApplyTheme(ThemeCatalog.DefaultKey, SelectedBaseTheme);
            SyncFromService();
            StartNewCustomTheme();

            StatusText = $"已删除自定义主题「{name}」，回到默认主题";
        }

        private void ExecuteAddOverride()
        {
            var key = SelectedBrushKey;
            if (string.IsNullOrWhiteSpace(key)) return;
            if (EditOverrides.Any(o => string.Equals(o.Key, key, StringComparison.Ordinal))) return;

            EditOverrides.Add(new ColorOverrideItem(key, NewOverrideValue));
            RefreshCustomPreview();
        }

        private void ExecuteRemoveOverride(ColorOverrideItem? item)
        {
            if (item == null) return;
            EditOverrides.Remove(item);
            RefreshCustomPreview();
        }

        private void ExecuteApplyPresetPrimary(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            EditPrimary = hex;
            EditSecondary = hex;
        }

        #endregion

        #region 内部

        private void StartNewCustomTheme()
        {
            _editingKey = null;
            IsEditingCustom = true;
            EditName = "我的主题";
            EditPrimary = "#00BCD4";
            EditSecondary = "#26C6DA";
            EditOverrides.Clear();
            RefreshCustomPreview();
        }

        private void LoadCustomIntoEditor(ThemeDefinition definition)
        {
            _editingKey = definition.IsCustom ? definition.Key : null;
            IsEditingCustom = definition.IsCustom;
            EditName = definition.DisplayName;
            EditPrimary = definition.Primary;
            EditSecondary = definition.Secondary;

            EditOverrides.Clear();
            foreach (var kv in definition.ColorOverrides)
                EditOverrides.Add(new ColorOverrideItem(kv.Key, kv.Value));

            RefreshCustomPreview();
        }

        /// <summary>自定义编辑区的实时预览（不落到界面，只算色）</summary>
        private void RefreshCustomPreview()
        {
            var definition = ThemeService.BuildCustomDefinition(
                _editingKey, EditName, EditPrimary, EditSecondary,
                EditOverrides.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal));

            CustomPreview = _themeService.BuildPreview(definition, _themeService.IsDark);

            // 编辑中的自定义主题如果正在使用，顺手刷新全局预览
            if (_editingKey != null && string.Equals(_current?.Definition.Key, _editingKey, StringComparison.OrdinalIgnoreCase))
                Preview = CustomPreview;
        }

        private void ReloadOptions()
        {
            var selectedKey = _selectedOption?.Definition.Key ?? _themeService.Current.Key;

            _syncing = true;
            try
            {
                Options.Clear();
                foreach (var definition in _themeService.Themes)
                    Options.Add(new ThemeOption(definition));

                _current = Options.FirstOrDefault(o => o.Definition.Key == _themeService.Current.Key);
                _selectedOption = Options.FirstOrDefault(o => o.Definition.Key == selectedKey)
                                  ?? Options.FirstOrDefault(o => o.Definition.Key == _themeService.Current.Key);

                if (_current != null)
                    foreach (var option in Options)
                        option.IsSelected = ReferenceEquals(option, _current);

                SetProperty(ref _selectedOption, _selectedOption, nameof(SelectedOption));
            }
            finally
            {
                _syncing = false;
            }

            RaisePropertyChanged(nameof(CanDeleteCustomTheme));
        }

        private void PersistThemeSelection()
        {
            _settings.Update(s =>
            {
                s.UI.Theme = _current?.Definition.Key ?? ThemeCatalog.DefaultKey;
                s.UI.BaseTheme = SelectedBaseTheme;
            });

            _logger.Information("外观设置已保存: {Theme}/{Base}", _current?.Definition.Key, SelectedBaseTheme);
        }

        private void ApplyTheme(string? themeKey, string? baseTheme)
        {
            _themeService.Apply(themeKey, baseTheme, persist: false);
            _logger.Information("外观预览切换到: {Theme}/{Base}", themeKey, baseTheme);
        }

        private void SyncFromService()
        {
            _syncing = true;
            try
            {
                _current = Options.FirstOrDefault(o => o.Definition.Key == _themeService.Current.Key);

                foreach (var option in Options)
                    option.IsSelected = ReferenceEquals(option, _current);

                SetProperty(ref _selectedOption, _current, nameof(SelectedOption));
                SetProperty(ref _selectedBaseTheme,
                    _themeService.IsDark ? "Dark" : "Light",
                    nameof(SelectedBaseTheme));

                RefreshPreview();
            }
            finally
            {
                _syncing = false;
            }

            RaisePropertyChanged(nameof(CanDeleteCustomTheme));
        }

        private void RefreshPreview()
        {
            if (_current == null) return;
            Preview = _themeService.BuildPreview(_current.Definition, _themeService.IsDark);
        }

        #endregion

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            ReloadOptions();
            SyncFromService();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        #endregion
    }
}
