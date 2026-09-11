# 主题系统与 MVVM/Prism 规范化说明

本次改动两部分：

1. **动态多主题**：参考工作区下 `MaterialDesignInXamlToolkit主题资源` 的源码思路
   （`PaletteHelper` / `ITheme` / `ThemeExtensions` / `BundledTheme`），把原来写死在
   `App.xaml` 里的 23 处颜色改为运行期按主题推导，支持 12 套主色 + 深/浅/跟随系统。
2. **MVVM / Prism 规范复查**：逐文件检查视图与 ViewModel 的职责边界并修正若干问题。

---

## 1. 动态多主题

### 1.1 结构

| 文件 | 职责 |
| --- | --- |
| `MultiCameraSystem/Themes/ThemeCatalog.cs` | 主题目录：每套主题只声明「主色 + 次色」，新增主题加一行即可 |
| `MultiCameraSystem/Services/ThemeService.cs` | 应用主题：MDIX 调色板 + 自定义语义画刷推导 |
| `MultiCameraSystem/ViewModels/ThemeSettingsViewModel.cs` | 外观设置页 ViewModel（主题卡片、明暗、实时预览、保存） |
| `MultiCameraSystem/Views/ThemeSettingsView.xaml` | 外观设置页（顶部导航「外观主题」/ 调色板按钮进入） |
| `MultiCameraSystem/Models/AppSettings.cs` | `UISettings.Theme`（主题键）+ `UISettings.BaseTheme`（Dark/Light/System） |

### 1.2 XAML 侧必须使用 DynamicResource（重要）

**单一真源**：`App.xaml` 里**不再声明任何颜色**，全部 31 个语义画刷由
`Themes/ThemeCatalog.cs`（声明主色/次色）+ `Services/ThemeService.cs`（推导背景/纸面/文字/功能色/底纹）
在运行期计算后写入 `Application.Resources`。

**因此 XAML 引用主题色一律用 `{DynamicResource XxxBrush}`，不能用 `StaticResource`：**

| | 行为 |
| --- | --- |
| `{StaticResource XxxBrush}` | ① 只在元素加载时解析**一次**，之后资源被替换也不管；② App.xaml 里的画刷被 WPF **冻结**（`IsFrozen=true`），连就地改 `Color` 都做不到。结果：**切换主题后已加载的界面完全不变**（这正是实测踩到的坑） |
| `{DynamicResource XxxBrush}` | 资源键被替换时会**自动重新解析**，已加载的界面实时变色 ✅ |

本次已把全部 **396 处**画刷引用从 `StaticResource` 改为 `DynamicResource`（键名都以 `Brush` 结尾，
不会误伤 `Style`/`Converter` 这类必须静态解析的资源）。

运行时机：`App.Initialize()`（Shell 与所有视图创建之前）先应用一次主题，所以不会出现"没有颜色"的空窗期；
即使主题应用抛异常，`ThemeService` 也会自动回退到默认主题。

> 代价：VS 设计器不跑 `ThemeService`，设计器里看不到主题配色（运行时一切正常）。

### 1.3 生效机制（为什么切主题不用重启）

`ThemeService.Apply` 做两件事：

1. **MaterialDesign 侧**：`PaletteHelper.GetTheme()` → `SetDarkTheme()/SetLightTheme()` +
   `SetPrimaryColor()/SetSecondaryColor()` → `SetTheme()`。所有 MDIX 控件（按钮、输入框、DataGrid、
   含水波纹、Chip 等）即时换色，不需要重载页面。
2. **自定义语义画刷侧**：按主色用 HSL 推导出 31 个语义色（Surface / Card / Border / Text /
   Success / Warning / Error / Tint / Scrim …），**就地修改** `Application.Current.Resources` 里
   `SolidColorBrush` 的 `Color`。画刷实例不变 ⇒ 界面上所有 `StaticResource` 引用实时刷新。

> 只在首次应用（或画刷被冻结）时才替换实例，之后一律就地改色。

### 1.4 可选主题

科技青（默认）、深海蓝、靛青、青碧、极光绿、琥珀、焰橙、品红、紫罗兰、石墨灰、警示红、青铜；
每种都可叠加 深色 / 浅色 / 跟随系统（读注册表 `AppsUseLightTheme`）。

### 1.5 自定义主题（可自定义）

除 12 套内置主题外，界面提供**完整的自定义主题编辑**：

| 能力 | 说明 |
| --- | --- |
| 自选主色 / 次色 | 用 MaterialDesignInXamlToolkit 自带的 `ColorPicker` 取色；另有 12 个常用主色快捷色块 |
| 主题命名 | 例如「三号机台」，用于多机台区分 |
| 高级逐项覆盖 | 31 个语义画刷任意挑、逐个指定颜色。支持 `#RRGGBB`（沿用推导出的透明度）与 `#AARRGGBB`（连透明度一起覆盖，用于 `PrimaryTintBrush`/`ScrimBrush` 这类半透明色） |
| 实时预览 | 编辑区左侧两块取色器 + 右侧「自定义预览」面板，改色即时反映到预览（未保存不影响全局） |
| 保存并应用 / 删除 | 保存后立即全局生效并写入配置；删除后回退默认主题 |
| 快速切换同步 | 标题栏调色板弹窗的主题列表会自动包含自定义主题（`ThemeService.ThemesChanged` → `MainWindowViewModel` 刷新列表） |

持久化结构（`appsettings.json`）：

```json
{
  "UI": {
    "Theme": "custom-test01",
    "BaseTheme": "Dark",
    "CustomThemes": [
      {
        "Key": "custom-test01",
        "DisplayName": "三号机台",
        "Primary": "#7C4DFF",
        "Secondary": "#B388FF",
        "ColorOverrides": {
          "SuccessBrush": "#00E5A0",
          "ErrorBrush": "#FF3D71",
          "PrimaryTintBrush": "#337C4DFF"
        }
      }
    ]
  }
}
```

新增/改动文件：`Themes/ThemeCatalog.cs`（`ThemeDefinition.IsCustom`/`ColorOverrides`、`FromSetting`、`NewCustomKey`、
`SemanticBrushKeys`）、`Services/ThemeService.cs`（`SetCustomThemes`/`ThemesChanged`/`BuildCustomDefinition`/`ApplyOverride`）、
`Convert/HexColorConverter.cs`（`Color` ↔ `#RRGGBB` 桥接）、`Models/AppSettings.cs`（`CustomThemeSetting`）。

**实测**：把 `custom-test01 / 三号机台 / #7C4DFF / #B388FF` 连同 3 项颜色覆盖写入配置后启动，
日志输出 `主题已应用: 三号机台 / Dark（31 个语义画刷）[自定义]`，配置回写完整保留自定义主题定义。

### 1.6 入口

- 标题栏 **调色板按钮** → 弹出主题列表（色块预览，点选即生效）+ 深/浅切换 + 跳转外观设置。
- 顶部导航 **外观主题** 页 → 主题卡片、明暗下拉、实时预览色板、保存 / 恢复默认。
- 选择会写回 `appsettings.json`；启动时在 **Shell 创建之前** 应用，避免视图先取到兜底色。

### 1.7 硬编码颜色清理

原来 14 个 XAML 文件里有 40+ 处硬编码色，现已收敛：

| 原硬编码 | 现用资源 |
| --- | --- |
| `#1A00BCD4` | `PrimaryTintBrush` |
| `#1A9C27B0` / `#9C27B0` | `SecondaryTintBrush` / `AccentBrush` |
| `#1A00E676` | `SuccessTintBrush` |
| `#1AFFAB40` / `#1AFF9800` | `WarningTintBrush` |
| `#1AFF5252` | `ErrorTintBrush` |
| `#99000000` / `#80000000` | `ScrimBrush` |
| `#FF7043` | `WarningBrush` |
| `Value="#00E676"` / `"#FF5252"`（视图模型里的状态色） | 改为 `bool` 状态 + XAML `DataTrigger` 绑定 `SuccessBrush`/`ErrorBrush` |
| `HalconView/Themes/Generic.xaml` 的 `#FF0D1117`/`#2A2A3E` | `DynamicResource CanvasBrush / CardBorderBrush` |

`App.xaml` 里的 23 处颜色字面量**已全部删除**（原来既像"唯一真源"、又因画刷被冻结而根本改不动），
现在它只保留样式与转换器；所有颜色只有一个来源：`ThemeCatalog` + `ThemeService`。
仅剩的字面量是 `DropShadowEffect Color="#000000"`（阴影恒为黑，不属于主题色）。

---

## 2. MVVM / Prism 复查结果

### 2.1 结论（合规项）

- 所有 `View` 与 `ViewModel` 通过 `prism:ViewModelLocator.AutoWireViewModel` 自动装配，
  没有一处 `DataContext = new XxxViewModel()`。
- 20 个 code-behind 文件中，此前只有 `MainWindow.xaml.cs` 有事件处理器：
  **窗口拖动 / 最小化 / 最大化 / 关闭** —— 这类能力 WPF 没有可绑定等价物，
  Prism 官方示例同样放在代码后置，属于视图自身职责，予以保留（并在文件头注明）。
  本轮把其中的 **日志面板开关** 挪到了 ViewModel（`ToggleLogPanelCommand` + 绑定 `Visibility`）。
- 依赖通过构造函数注入（Prism/DryIoc），没有服务定位器式 `Container.Resolve` 散落在 ViewModel 中
  （仅 `App.xaml.cs` 组合根与 `MVS.Core.AppContainer` 桥接点使用）。
- 导航全部走 `IRegionManager.RequestNavigate`；`INavigationAware` 的
  `OnNavigatedTo/OnNavigatedFrom` 与资源订阅配对。

### 2.2 本轮修正的问题

| 位置 | 问题 | 修正 |
| --- | --- | --- |
| `MainWindowViewModel` + `MainWindow.xaml` | 每个页面一个 `bool IsXxxSelected` 属性 + 两段 `switch`，新增页面要改三处；顶部菜单图标用 `Tag="HomeVariant"` 传给 `ContentPresenter`，**实际显示成文字而不是图标** | 改为数据驱动的 `NavigationItem` 集合（`TopNavigation` / `SideNavigation`），菜单/侧栏用 `ItemsControl` 生成；图标用 `PackIcon Kind="{Binding Icon}"` |
| `MainWindowViewModel` | 「工作台」与「系统设置」共用同一个选中标志 | 拆分为独立导航项，选中态由集合统一维护 |
| `LogViewerViewModel` | 构造函数里订阅日志、`OnNavigatedFrom` 里退订，而 `IsNavigationTarget => true` 会复用实例 ⇒ **离开日志页再回来就收不到新日志** | 订阅/退订与 `OnNavigatedTo/OnNavigatedFrom` 严格配对；并限制内存中日志条数（5000） |
| `LogViewerViewModel` | `LevelBrush/LevelBackground` 在 VM 里 new 硬编码 `SolidColorBrush`；`AutoScroll` 属性没人用（死属性） | 颜色改由视图 `DataTrigger` 绑定主题画刷；`AutoScroll` 接上新增附加行为 `Behaviors/ListBoxBehaviors.AutoScrollToEnd` |
| `LogViewerViewModel` / `SettingsViewModel` / `HomeViewModel` 等 | 命令写成 `=> new DelegateCommand(...)`，每次绑定都新建命令实例 | 统一改为只读字段缓存 |
| `RunViewModel` | `WindowReadyCommand` 每次取值都新建命令（HalconView 回传句柄会反复执行/失效）；过程名写死 `user_findcircle`；Halcon 加载/设参/执行/取结果全写在 VM 里 | 命令缓存；过程名/图像参数名可配置；算法搬进 `Services/HalconRunService.cs` |
| `HomeViewModel`、`PLCMonitorViewModel`、`MESStatusViewModel` | 暴露 `StatusColor => "#00E676" : "#FF5252"` 之类的颜色字符串 —— 颜色是视图/主题职责 | 改为只暴露 `IsError` / 复用 `IsConnected`，颜色由 XAML `DataTrigger` 取主题画刷 |
| `AggregatedResult`、`HistoryRecord`、`DetectionRecord` | 模型/VM 里带 `ResultColor` 颜色字符串 | 移除，改为 `IsPass`/`IsOk` 布尔 + 视图 `DataTrigger` |
| `MultiCameraViewModel` | `_ameraCount`（拼写错误的死字段）、未使用的 `_cameras`、一堆无用 using；未实现导航接口，列表不刷新 | 清理死代码；实现 `INavigationAware` 在进入页面时刷新；命令缓存 |
| `MultiCameraView.xaml.cs` | 无用的 `using Prism.Ioc;` | 移除 |
| `SettingsViewModel` | `_dirty` 只写不读（死字段） | 改为公开只读属性 `HasUnsavedChanges` |
| `ThemeService` | 同一主题被重复应用、并多写一次配置文件 | 增加 `_lastAppliedKey` 幂等守卫；`ThemeChanged` 回推不再走会再次 Apply 的属性 setter |
| `Inspection/Views/DashboardView.xaml` | `ProgressBar.Value` 绑到只读属性 `DashboardViewModel.Progress` —— **`RangeBase.Value` 默认是 TwoWay 绑定，XAML 解析直接抛 `XamlParseException`** | 显式 `Mode=OneWay` |
| `Inspection/Views/DashboardView.xaml` / `SystemSettingsView.xaml` | 纯展示的 `DataGrid` 未设 `IsReadOnly`，且列绑定了只读属性（如 `PcsInspectionResult.ResultText`）—— 列绑定默认同样按 TwoWay 处理，行出现时会抛同类异常 | 加 `IsReadOnly="True"`，只读属性列显式 `Mode=OneWay` |
| `App.xaml.cs` | Prism 区域导航会把视图创建时的异常内部消化，表现为「页面静默不显示、也不写 crash_log」，极难排查 | 新增 `Navigate(...)` 包装：导航后校验区域是否真的加载出视图，失败即明确落 `ERR` 日志 |
| 全部 21 个 XAML（396 处） | 主题色用 `{StaticResource}` 引用 + 资源画刷被 WPF 冻结，导致**切换主题（含自定义主题）后已加载界面完全不变** | 全部改为 `{DynamicResource}`；`App.xaml` 删除颜色字面量，颜色改由 `ThemeService` 单一来源生成；主题应用失败时自动回退默认主题 |

### 2.3 WPF 绑定模式踩坑备忘（本次教训）

WPF 有一批**默认 TwoWay** 的依赖属性，绑定到只读属性会直接抛
`InvalidOperationException: 无法对只读属性进行 TwoWay 或 OneWayToSource 绑定`
（包在 `XamlParseException` 里）：

| 默认 TwoWay 的属性 | 用到的地方 |
| --- | --- |
| `RangeBase.Value` | `ProgressBar` / `Slider` / `ScrollBar` |
| `TextBox.Text` | 所有输入框 |
| `ToggleButton.IsChecked`（含 `CheckBox`/`RadioButton`） | 所有开关 |
| `Selector.SelectedItem` / `SelectedIndex` / `SelectedValue` | `ComboBox` / `ListBox` / `TabControl` |
| `DataGridBoundColumn.Binding` | `DataGrid` 列（未设 `IsReadOnly` 时） |

规避方式：源属性给可写 setter，或绑定上显式写 `Mode=OneWay`；
纯展示的 `DataGrid` 一律加 `IsReadOnly="True"`。

### 2.4 仍可继续改进（未做，供决策）



- 各模块 ViewModel 仍在 ViewModel 中直接使用 `Microsoft.Win32.OpenFileDialog` /
  `OpenFolderDialog`。若要彻底可测试，可抽象成 `IFileDialogService` 注入。
- `MainWindow` 的无边框窗口 chrome 可用附加行为（`WindowChrome`）进一步零代码后置。
- 模块内部（`PLCModule`/`MESModule`/`WorkBench`）的少量文案与配色仍可继续向主题资源收敛。

---

## 3. 界面重设计（参考 主题参考 目录里的三张图）

### 3.1 从参考图提取的设计语言

| 参考图 | 提取到的要点 |
| --- | --- |
| 主界面1.png（Wally 金融） | 深色底 + **渐变主色卡**、大圆角、**胶囊渐变 CTA**、侧栏选中为"深色胶囊" |
| 主界面2.png（ZR 后台） | **深色侧栏 + 浅色内容**的双层结构、白卡 + 细边框、指标小卡、数据表格 |
| 主界面3.png（Radiograph 监控） | **近黑画布 + 稍亮卡片 + 1px 弱边框**、**窄图标栏（图标+小字+选中竖条）**、**左标签/右数值 + 细数据条**、可折叠分区 |

综合方向：工业视觉监控场景 → 以 Radiograph 的深色密度为主，借 Wally 的渐变提升质感，
用 ZR 的"深色侧栏 + 浅色内容"覆盖浅色主题。

### 3.2 新增设计令牌（全部由主题推导，跟着主题变）

原有 31 个语义画刷之外，ThemeService 又推导出 14 个：

| 令牌 | 用途 |
| --- | --- |
| CanvasBrush / SurfaceBrush / CardBrush / SurfaceElevatedBrush | 四级"面"层次（画布 → 主体 → 卡片 → 悬浮） |
| BorderSubtleBrush | 分隔线 / 表格横线（半透明，取代重阴影） |
| NavRailBrush / NavRailActiveBrush / NavRailTextBrush | 左侧导航栏专用（**深浅主题下都保持深色**，对应参考图 2/3） |
| TextOnAccentBrush | 强调色上的文字（按主色亮度自动取深/浅） |
| DataTrackBrush / DataBarBrush | 数据条（参考图 3 的细进度条） |
| ChipBrush | 状态胶囊底色 |
| AccentGradientBrush / PrimaryGradientBrush / SuccessGradientBrush / WarningGradientBrush / DangerGradientBrush | **渐变画刷**（参考图 1 的渐变卡片与 CTA） |

### 3.3 全局样式（App.xaml）

- **按钮**：自定义 PillButtonTemplate（圆角 10 + hover/pressed 透明度反馈）
  - PrimaryButton = 主色渐变胶囊；SuccessButton / DangerButton = 功能色渐变；
  - ControlButton = 幽灵按钮（透明底 + 弱边框，hover 转主色）；WindowButton / WindowCloseButton = 标题栏窗控。
- **卡片**：CardBorder（圆角 12 + 1px 弱边框，**去掉重阴影**）、InnerCard、MetricCard、MetricCardGradient。
- **文字**：PageTitle(24 Bold) / SectionTitle(14 SemiBold) / MutedText / MetricValue(28 Bold) / DataLabel + DataValue。
- **输入**：DarkTextBox 自定义模板（圆角 9，聚焦/悬停边框转主色）。
- **数据**：DataGrid + DataGridColumnHeader 统一为"透明底 + 细横线 + 弱化表头"；ThinProgressBar 细数据条。
- **状态**：StatusChip 胶囊 + StatusDot 圆点。

### 3.4 主窗口（MainWindow.xaml）

    ┌────────────────────────────────────────────────────────────────┐
    │ ◆ CCD 视觉检测系统 │ 当前页      [日志][主题][—][□][✕]          │ 44px 标题栏
    ├──────┬─────────────────────────────────────────────────────────┤
    │ 76px │                                                         │
    │ 图标 │   内容区（ContentRegion）                               │
    │ +小字│                                                         │
    │ ▌选中│                                                         │
    ├──────┴─────────────────────────────────────────────────────────┤
    │ ● 系统就绪  ▣ 科技青              CCD 视觉检测系统 v1.2.0 · 页名 │ 34px 状态栏
    └────────────────────────────────────────────────────────────────┘

- 侧栏由 NavigationItem 集合数据驱动（图标 + 小字 + **选中竖条/淡色底**），底部固定"外观"入口。
- 标题栏：渐变 Logo 徽标 + 应用名 + 当前页名；右侧日志开关、主题快速切换（自定义模板，不再被 MaterialDesign 画成"开关"）、窗控按钮。
- 状态栏：状态胶囊组 + 版本/页名。

### 3.5 检测主监控台（DashboardView.xaml）

- **指标卡行（4 张）**：图片进度（渐变卡 + 细进度条）/ OK 数 / NG 数 / 耗时（两行数据行）。
- **主区**：实时图像卡 | 右栏"设备状态"（状态点 + 名称 + 状态文本右对齐）、"制品信息"（左标签右数值）、"检测项目"。
- **底部**：PCS 检测结果表（只读 DataGrid）| 实时日志卡。
- 页面头：标题 + 产品/方案/操作员上下文 + 运行时长胶囊 + 操作按钮组。

### 3.6 验证

- **像素采样**（客观取色，非肉眼判断缩略图）：
  - 深色（科技青/Dark）：画布 `#060F11`、卡片 `#19292B`、标题栏/侧栏 `#081214` —— 近黑 + 两级卡片，符合参考图 3。
  - 浅色（深海蓝/Light）：卡片 `#FFFFFF`、标题栏/状态栏 `#EBEDEF`、**侧栏 `#121D26`（保持深色）** —— 符合参考图 2 的"深栏浅内容"。
- **全视图烟测**：遍历 16 个视图全部加载成功，ERR/FTL 计数 0。
- 主题与自定义主题的**运行期实时切换**依旧有效（画刷统一走 DynamicResource）。

> 说明：本轮**没有改动内页版式**。其余页面（产品 / 检测配置 / 系统设置 / 相机 / MES / PLC 等）
> 自动继承新的卡片、按钮、输入框、表格、文字样式，整体观感统一。
> 如果要把某几页也按参考图重排（例如系统设置改成分区折叠、产品页改成卡片网格），指定页面即可。

### 3.7 第二轮：全站风格统一

第一轮只改了外壳 + 主监控台，本轮把**其余页面**拉到同一套规范上：

| 页面 | 统一动作 |
| --- | --- |
| TestView / RunView | 卡片里的"图标+标题"改为**标准页面头**（PageTitle + 说明 + 右侧操作），去掉按钮上与幽灵样式打架的 `SurfaceDarkBrush` 覆盖 |
| SettingsView / ResultDashboardView | 同上；设置页的操作按钮移到页面头右侧 |
| WorkBenchView | 拆成「页面头（标题+状态+运行控制）」+「流程参数卡片」+ 主体，行结构改为 3 行 |
| CameraManagerView | 增加页面头（DockPanel 包裹原有三栏布局） |
| CameraControlView | 状态点改为 `IsConnected`/`IsGrabbing` + 主题画刷的 `DataTrigger`，并把 ViewModel 里的 `StatusColor`（返回硬编码 Color 的"颜色泄漏"）删除 |
| MultiCameraView | 顶部栏改为页面头（标题 + 已发现设备数 + 刷新按钮用 `IconButton`） |
| Home | 四张信息卡统一为 `MetricCard`（等宽 212、图标+标签一行、`MetricValue` 大数字），刷新按钮并入页面头 |
| PLCMonitorView / MESStatusView | **删掉 5 处"卡片底色改成 SurfaceDarkBrush"的覆盖**（那是为了区分卡片套卡片，会让卡片比画布更暗、层级反转）；整页重排为「页面头 + 左表单卡 + 右状态/指标卡」，表单控件统一 `DarkTextBox`、按钮统一 Primary/Controls/幽灵、`ThinSeparator` 分隔 |
| LogViewerView | 它是嵌在底部日志区域的**面板**而非页面，保持紧凑面板形态（仅沿用主题画刷与卡片样式） |

一致性规则（新增页面请照此写）：

1. 页面 = `PageTitle` + `MutedText` 说明 + 右侧操作按钮（不放在卡片里）；
2. 卡片一律 `CardBorder`（不要覆盖 `Background`）；嵌套分区用 `InnerCard`；
3. 关键数字用 `MetricCard` 或 `MetricCardGradient` + `MetricValue`；
4. 表单标签用 `DataLabel`、值用 `DataValue`；输入框一律 `DarkTextBox`；
5. 按钮：主操作 `PrimaryButton`、成功 `SuccessButton`、危险 `DangerButton`、次要 `ControlButton`、纯图标 `IconButton`；
6. 分隔线用 `ThinSeparator`、状态用 `StatusChip` + `StatusDot`、数据条用 `ThinProgressBar`；
7. **颜色只用 `{DynamicResource XxxBrush}`**，不写十六进制、不在 ViewModel 里存颜色。

**验证**：全 16 视图烟测通过（ERR/FTL 计数 0），多页截图巡检确认观感一致；程序启动无异常。

---

## 4. 图表（LiveCharts2）与随之修掉的既有 Bug

### 4.1 为什么用 LiveCharts

对比过三种做法：

| 方案 | 结论 |
| --- | --- |
| LiveCharts2（`LiveChartsCore.SkiaSharpView.WPF` 2.0.5） | **采用**。环形/折线/柱状开箱即用，动画与命中测试免费；代价是引入 SkiaSharp（原生库），已验证在本机可离线还原 + 运行期正常渲染 |
| 自己画（FrameworkElement.OnRender） | 环形+折线大约 150 行，零依赖；适合只做极少量图形。后续若只想保留一两个小图可用 |
| 其它图表库 | 本地 NuGet 缓存里没有，未考虑 |

### 4.2 依赖处理（容易踩坑）

`LiveChartsCore.SkiaSharpView.WPF 2.0.5` 的依赖图会同时拉入 **SkiaSharp 2.88.9** 与
`SkiaSharp.Views.WPF 3.119.0`（后者带回 **SkiaSharp 3.119.0**），出现**同一主版本混用**。
处理方式：在用到图表的 4 个工程里显式钉住 `SkiaSharp 3.119.0`：

```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.5" />
<PackageReference Include="SkiaSharp" Version="3.119.0" />
```

> 注意：这些包**已不在本地缓存里**（本机需要能访问 nuget.org 才能还原）。

### 4.3 图表颜色如何跟随主题

图表画笔必须由代码构造（`SolidColorPaint`），而**各模块不能反向引用外壳工程** MultiCameraSystem。
因此在共享内核 `MVS.Core` 里加了极薄的桥 `ThemePalette`：

- `ThemeService` 每次应用主题时登记 `Resolver`（语义键 → 颜色）并 `NotifyChanged()`；
- 任何模块都能 `ThemePalette.Get("SuccessBrush")` 取到**当前主题色**，并订阅 `Changed` 重建图表。

这样图表颜色与全站同源，切换主题 / 自定义主题时图表自动换色。

### 4.4 已接入的图表

| 页面 | 图表 |
| --- | --- |
| 检测主监控台（Inspection） | **良率环形图**（Pipeline：OK 绿 / NG 红，无数据时用轨道色占位，中心显示良率与 PCS 数）+ **检测耗时趋势折线**（最近 60 个 PCS，关闭坐标轴与提示） |
| 结果仪表盘（WorkBench） | 良率环形图 + 检测耗时趋势折线；并新增侧栏入口「结果仪表盘」 |
| MES 状态 | 仍为数值指标卡，未加图（数据结构只有两个计数，加图意义不大，可后续按需加柱状） |

图表数据来源：`InspectionOrchestrator` 现在会把**配方执行耗时**写到每个 PCS 结果（`PcsInspectionResult.ElapsedMs`），
趋势折线据此绘制。

### 4.5 顺带修掉的既有 Bug（重要）

**WorkBench 模块的两个 ViewModel 命名空间是 `WorkBench.ViewModel`（单数）**，而 Prism 约定要求
`{Views→ViewModels}`，也就是 `WorkBench.ViewModels`。后果是：

- `WorkBenchView`（工作台）**从来没有拿到 DataContext** —— 流程名称/超时/重试、步骤列表、运行/停止按钮全是死的；
- `ResultDashboardView`（结果仪表盘）同样，指标数字与图表全是空。

修复：把 `WorkBench\ViewModel\*.cs` 移到 `WorkBench\ViewModels\` 并把命名空间改为 `WorkBench.ViewModels`
（其它模块本来就是复数，只有 WorkBench 写错了）。

> 这类问题不会抛异常，只会"界面看起来正常但全是空值"，属于最难查的一类。
> 建议：新增模块时统一用 `XXX.ViewModels` 命名空间 + `ViewModels` 文件夹。

---

## 5. PLC 读取值修复（显示 System.Byte[] / 触发静默失效）

### 5.1 现象

PLC 监控页连接成功、提示"读取成功"，但「数值」显示为 **`System.Byte[]`**（Modbus Slave 里寄存器 0 = 2321）。

### 5.2 根因

HslCommunication 的 `Read(address, length)` 返回的是 **`byte[]`**：

- 监控页把 `PLCReadResult.Value`（byte[]）直接 `.ToString()` → `"System.Byte[]"`；
- 更严重的是**产线路径**：`PlcIoService.ReadAsync(point)` 调 `ReadAsync<bool>`，
  内部 `Convert.ChangeType(byte[], bool)` **抛异常被吞掉** → 返回 null
  → **PLC 上升沿触发（相机触发/复位）静默失效**。

### 5.3 修复内容

| 位置 | 改动 |
| --- | --- |
| `PLCCommunicator.ExtractValue` | `byte[]` 按长度归一化：1 字节 → `byte`；**2 字节 → `ushort`（大端寄存器值）**；4/8 字节保留 `byte[]` 交给上层按类型转换 |
| `PLCCommunicator.ConvertRaw`（新增） | 统一的原始值 → 目标类型转换：`byte[]` → bool/整数/浮点/字符串（大端处理、长度保护），数值 → `bool` 走"非零即真" |
| `IPLCCommunicator.ReadTypedAsync<T>`（新增） | **把类型下推到协议层**：Float/Double/Int32 按 2/4 个寄存器读取（`ReadFloat/ReadInt32`），而不是只读 1 个寄存器硬转 |
| `ReadTypedAsync<T>` 的 Bool 回退 | 先按线圈（功能码 1）读；从站只有保持寄存器时报"不支持的功能码"，此时**自动退回"读寄存器、非零即真"** |
| PLC 监控页 | 新增**数据类型**下拉（Bool/UInt16/Int16/Int32/Float/String），读取后按类型格式化（`bool` 显示 `1 (ON)/0 (OFF)`，`float` 保留 3 位），失败时清空并显示原因 |

### 5.4 实测（直连本机 Modbus Slave，寄存器 0 = 2321）

| 类型 | 结果 |
| --- | --- |
| UInt16 | **2321** ✅ |
| Int32 | **2321** ✅（按 2 个寄存器读：2321 + 0） |
| Float | **3.252E-42** ✅（0x09110000 的正确浮点解释，说明确实是 2 寄存器读取） |
| Bool | **true** ✅（线圈不支持 → 自动回退寄存器非零） |
| 地址 1 / 2 | 0 / false ✅（与从站一致） |

> 结论：**产线路径（PLC 上升沿触发相机）也随之修好了** —— 之前它一定读不到值。

---

## 6. 按「类型 + 个数」读写 PLC（不同数据类型 → 不同寄存器个数）

### 6.1 问题

同一批寄存器，按不同类型读需要读**不同个数**：

| 类型 | 占用寄存器（16 位字） |
| --- | --- |
| Bool（位） | 1 位（Modbus 为线圈，功能码 1） |
| Byte / Int16 / UInt16 | 1 个 |
| Int32 / UInt32 / Float | **2 个** |
| Int64 / Double | **4 个** |
| String（如从寄存器读条码） | N 个字（按长度） |

之前只有"读 1 个寄存器"这一条路，既读不出 Float/Int32，也没法一次读一组。

### 6.2 改动

| 层 | 改动 |
| --- | --- |
| `SignalInfo` | 新增 `Count`（0 = 按类型自动）与 `StringByteLength`；新增 `RegisterCount(Type)` / `EffectiveCount(Type)` 明确"类型 → 寄存器个数"的规则 |
| 类型化读取 | `ReadTypedByAddress(..., bytesOrLength)`：Float/Int32 交给 `ReadFloat/ReadInt32` 自行读 2 个寄存器，String/byte[] 按长度读 |
| **新增数组读取** | `ReadTypedArrayByAddress` + `ReadSiemensArray/ReadMelsecArray/ReadModbusArray`，用 HslCommunication 的 `ReadXxx(address, length)` 重载一次读 N 个 |
| `IPLCCommunicator` | 新增 `ReadTypedAsync<T>(addr, count, ct)` 与 `ReadArrayAsync<T>(addr, count, ct)` |
| Bool 回退 | 单个/数组都支持：线圈（功能码 1）不支持时自动退回"读寄存器、非零即真" |
| `PlcIoPoint` | 新增 `DataType` 字段 —— 每个点位自己声明类型（Bool/Int16…/String），个数用已有的 `Count` |
| `PlcIoService` | 读写按 `DataType` 派发（位→bool、字→对应整型/浮点、String→文本），并新增 `ReadStringAsync` / `ReadArrayAsync<T>` |
| `PLCSignal` | 新增 `Count` |
| 监控页 | 新增「个数」输入；个数值 >1 时走数组读取，下方列表逐值显示 |
| **设置页点位表** | 「通讯设置」页新增 **PLC 点位表**：分组/点位/**区域/偏移/数据类型/个数/方向**/实际地址/说明 全部可编辑 |
| 入口 | 补上缺失的导航项「检测设置」（迁移过来的通用/存图/MES/通讯/数据库设置页之前**没有任何入口**） |

### 6.3 实测（连本机 Modbus Slave，寄存器 0 = 2321，后续 9 个为 0）

| 调用 | 结果 |
| --- | --- |
| `ushort[10]` | `2321,0,0,0,0,0,0,0,0,0` ← **与 Modbus Slave 里那 10 行完全一致** |
| `int[3]` | `2321,0,0`（3 个 int = 读 6 个寄存器） |
| `float[2]` | `3.252E-42,0`（2 个 float = 读 4 个寄存器，0x09110000 的正确解释） |
| `bool[5]` | `True,False,False,False,False`（线圈不支持 → 回退为寄存器非零） |
| 单个 `Int16` | `2321` |
| 单个 `UInt16` | `2321` |

> 说明：String 的"个数"在 HslCommunication 里按**字数**解释（Modbus 下 N → 2N 字节），
> 读条码时按目标长度填即可（例如 20 个字 = 40 字节，够放 20 个 ASCII 字符的条码）。

---

## 7. HslCommunication 商业组件授权注册（必须）

### 7.1 为什么必须做

HslCommunication 是**商业组件**：不注册授权码会运行在**试用模式**（连接数/功能受限），
商用项目必须在**创建任何通信对象之前**完成注册：

```csharp
HslCommunication.Authorization.SetAuthorizationCode("<授权码>");   // 返回 bool
```

### 7.2 实现：`PLCModule/HslLicense.cs`

- 授权码取值优先级（高 → 低）：
  1. 调用方传入（来自 `appsettings.json` 的 `Plc.HslAuthorizationCode`）
  2. 环境变量 **`HSL_AUTH_CODE`**（便于把密钥放在源码/仓库之外）
  3. 内置默认授权码（现场开箱即用）
- **幂等**：同一授权码重复调用直接返回，不重复注册；状态与结果通过
  `IsRegistered` / `RegistrationDetail` / `LastMessage` 对外暴露。
- 授权异常不会让程序崩溃，只记录日志并回落试用模式。

### 7.3 注册时机（两处，缺一不可）

| 位置 | 作用 |
| --- | --- |
| `App.OnStartup` 最开头（容器初始化之前、`base.OnStartup` 之前） | 用「环境变量 / 内置默认值」**抢在一切之前**注册，保证任何早期代码都不会用到未授权的组件 |
| `App.Initialize()` 里 `settings.Load()` 之后 | 用 `appsettings.json` 的值**再确认一次**（可覆盖内置值） |
| `PLCModule.OnInitialized` | 兜底注册；并把**首次真实注册结果**输出到正式日志（注册发生在日志系统就绪之前，需要在这里补一行） |

### 7.4 可观测性

- 启动日志会明确输出：`PLC 模块已初始化（HslCommunication 授权成功）`
  或 `…授权失败（请检查授权码是否正确、是否与本机绑定）`；
- **PLC 监控页**「连接状态」卡片下新增一行授权状态（绿点 = 已注册，橙点 = 试用模式）。

### 7.5 配置方式

`appsettings.json`：

```json
{
  "Plc": {
    "HslAuthorizationCode": "",     // 留空 = 用内置默认值；也可用环境变量 HSL_AUTH_CODE 覆盖
    "IpAddress": "192.168.1.100",
    "Port": 502
  }
}
```

> 实测：启动日志输出 `PLC 模块已初始化（HslCommunication 授权成功）`，即授权码被组件接受。

---

## 8. PLC 写入（按类型 + 个数，含 0.0）

### 8.1 之前的缺口

- PLC 监控页**只有读取，没有写入**；
- 底层写入还有个和读取同源的坑：**往"只有保持寄存器"的 Modbus 从站写位（0/1）会走线圈功能码（写线圈）被拒 → 写入静默失败**；
- 无法一次写多个（整段写配方/条码）。

### 8.2 改动

| 位置 | 改动 |
| --- | --- |
| `PLCCommunicator.WriteByAddress` | **位写入带回退**：先写线圈/位，失败则改写寄存器 `0/1` |
| `WriteArrayByAddress`（新增） | 数组写入：`short[]/ushort[]/int[]/uint[]/float[]/double[]/byte[]`；位数组同样支持"线圈 → 寄存器"回退 |
| `IPLCCommunicator` | 新增 `WriteTypedAsync<T>(addr, value, ct)`、`WriteArrayAsync<T>(addr, values, ct)` |
| PLC 监控页 | 新增 **写入值** 输入 + **写入 PLC** 按钮；个数 = 1 写单值，个数 > 1 用逗号分隔一次写多个；**写完自动回读确认**（避免"以为写进去了"） |

### 8.3 实测（连本机 Modbus Slave，station 1）

| 操作 | 回读结果 |
| --- | --- |
| 初始 | `2321,0,0` |
| 写 `1234`（UInt16，地址 0） | `1234,0,0` ✅ |
| **写 `0.0`（Float）** | `0,0,0` ✅（Float 写 2 个寄存器；0.0 正常写入，不存在"跳过 0"的逻辑） |
| 写 `0`（UInt16） | `0,0,0` ✅ |
| 写数组 `[111,222,333]`（UInt16，地址 0） | `111,222,333` ✅ |
| 写 `true`（Bool） | `1,222,333` ✅（从站无线圈区 → 自动回退写寄存器 1） |

> 测试后已把从站的 10 个寄存器复原为 `2321,0,0,0,0,0,0,0,0,0`。

### 8.4 现场用法

PLC 监控页：地址 `0` + 数据类型 `UInt16` + 个数 `1` + 写入值 `1234` → 点「写入 PLC」→ 自动回读显示 `1234`。
一次写多个：个数填 `3`、写入值填 `111,222,333`。
产线路径走 `PlcIoService.WriteAsync(point, value)`，按点位的 `DataType` 派发写入负载。

---

## 9. 让功能"可测试"：自检与仿真（含 3 个真实 Bug 修复）

### 9.1 问题

没有相机 / PLC / MES / 数据库 / 现场方案文件时，检测流程、存图、上传、图表这些功能**根本跑不起来**，
自然"不知道功能是否正常"。此外踩到 **HALCON 25 的 .hdev 格式陷阱**。

### 9.2 HALCON 25 的 .hdev 是 XML（重要）

纯文本 .hdev 会直接报错：

```
Reading of file '...hdev' failed:
The file is not a valid XML file.
Parser error: Start tag expected, '<' not found
```

HALCON 25 的 HDevelop 方案文件是 **XML**，接口标签含义：

| 标签 | 含义 |
| --- | --- |
| `<io>` | 输入图标（iconic input） |
| `<ic>` | 输入控制（control input） |
| `<oo>` | 输出图标（iconic output） |
| `<oc>` | 输出控制（control output） |
| `<c>` / `<l>` | 注释行 / 代码行（代码里的 `<`、`>` 需 XML 转义） |

示例（可用）：

```xml
<?xml version="1.0" encoding="UTF-8"?>
<hdevelop file_version="1.2" halcon_version="25.11.0.0">
<procedure name="inspect_demo">
<interface>
<io><par name="Image" base_type="iconic" dimension="0"/></io>
<ic><par name="ImageIndex" base_type="ctrl" dimension="0"/></ic>
<oo>
<par name="ResultImage" base_type="iconic" dimension="0"/>
<par name="box0" base_type="iconic" dimension="0"/>
</oo>
<oc><par name="out0" base_type="ctrl" dimension="0"/></oc>
</interface>
<body>
<l>threshold (Image, Bright, 128, 255)</l>
...
</body>
</procedure>
</hdevelop>
```

> 结论：现场必须用 **HDevelop 25 另存**方案文件；手写/旧版本的文本 .hdev 无法被引擎加载。
> 项目内置了一份可用的演示方案：`Inspection/Docs/SampleProduct/演示流程.hdev`。

### 9.3 新增「自检与仿真」页面（侧栏 → 自检）

| 能力 | 说明 |
| --- | --- |
| **生成演示产品** | 一键写入演示方案（XML .hdev）+ 完整配方（扫码 + 检测）+ 存图路径到 `Product/演示产品/` |
| **加载演示产品** | 直接加载并启动编排器 |
| **投入 OK / NG 样张** | 生成合成图像（良品 / 有大块亮区的不良品），走**真实编排器**：Halcon 执行 → PCS 结果 → OK/NG 存图 → 上传 → 图表与日志 |
| **运行自检** | 逐项检查：Halcon 引擎 / 产品配置 / **方案文件与过程可加载** / PLC 组件授权 / PLC 连接 / 存图目录可写 / 数据库 / MES 主机 / 演示方案，并给出"建议" |

### 9.4 本次修掉的 3 个真实 Bug（都在仿真中被暴露）

| # | 现象 | 根因 | 修复 |
| --- | --- | --- | --- |
| 1 | **存图全部被丢弃**：日志 `存图队列已关闭，丢弃图片…` | `LoadProductAsync` 先 `UnloadAsync()` → `ImageArchiveService.StopAsync()` 里调用了 `CompleteAdding()`，把队列**永久关闭**；之后 `Start()` 也无效 | `StopAsync` 只取消消费循环，**不再关闭队列**；只有 `Dispose()` 才 `CompleteAdding()`。服务变为"可停止/可重启" |
| 2 | 扫码流程被当成检测流程，**凭空多出一个空 PCS** | `InspectionRecipe.IsCodeRecipe` 只认「扫码」/`ScanCode`，认不出 `scan_code` | 新增显式声明 **`IsScannerRecipe`**（推荐现场使用），名字约定扩展为 含「扫码」/scan/code |
| 3 | `未取到检测项输出[out2]/[out3]`，PCS 结果为空 | 配方写成 PcsPerImage=2 × ItemCount=2（需要 out0..out3），但方案只声明了 out0/out1 | 演示配置改为 1 图 1 PCS × 2 检测项；并在输出缺失时给出**一次性明确提示**：需要 N 个输出 vs 方案实际声明了哪些 |

### 9.5 仿真实测结果（无任何硬件）

```
投 OK 样张 → 处理图片=1 PCS=1 OK=1 NG=0  视觉耗时=20ms
投 NG 样张 → 处理图片=2 PCS=2 OK=1 NG=1  视觉耗时=43ms
NG 存图 1 张：SIM-NG-...-DEMO-800X600-0000000001-2.jpeg
PCS 1 结果=00（两个检测项均 OK）  PCS 2 结果=10（检测项0=NG）
```

> 说明：合成样张分别对应"只有小亮点"（OK）与"存在大块亮区"（NG），
> 与演示方案里的阈值/面积判定逻辑一致，因此可用于回归验证 Halcon 链路。

### 9.6 现场怎么用

1. 打开「自检与仿真」→ **运行自检** → 看哪些项异常（"未配置"是可选项，无对应硬件属正常）；
2. 点 **生成演示产品** + **加载演示产品**（或先手动加载真实现场产品）；
3. 点 **投入 OK/NG 样张** → 切到「检测主监控台」看结果表、良率环形图、耗时趋势、日志；
4. 现场有真机时：用「相机管理」连相机、用「PLC 监控」读写点位，即可逐项替换验证。

### 9.7 自检页上线后又修掉的 3 个问题（都是实测暴露的）

| # | 现象 | 根因 | 修复 |
| --- | --- | --- | --- |
| 4 | 进自检页后**界面白屏卡住约 16 秒** | 自检的 Halcon 检查（首次创建 HDevEngine 会加载 HALCON 运行时）在 **UI 线程**上同步执行 | `RunAsync` 开头 `await Task.Yield()`，Halcon 两项检查整体放入 `Task.Run`；界面立刻可用 |
| 5 | **启动后侧栏选中态不对**：显示的是自检页，但侧栏仍高亮「监控台」，标题栏也还是旧页名 | 初始导航直接调 `regionManager.RequestNavigate`，绕过了 ViewModel 的选中态更新 | 初始导航改走 `MainWindowViewModel.NavigateByNameCommand` |
| 6 | 上一条修完仍不对 —— 改了选中态界面没反应 | **`MainWindowViewModel` 被解析成两个实例**：DryIoc 默认对未注册的具体类型按瞬态处理，外壳绑定的是一个、外部 `Resolve` 拿到的是另一个 | 把 `MainWindowViewModel` 注册为 **单例** |
| 7 | 自检的「建议」写在 `ToolTip` 上，用户**根本发现不了**（且 Hint 为空时无提示） | 悬停提示只挂在"详情"那一列，可发现性差 | 建议改为**行内显示**（行下方一行橙色文字，`HasHint` 控制显隐）；正常项不显示建议，避免噪音；摘要区分"异常/正常/未配置" |

> 侧栏项也从 54px 压到 46px，16 个导航项在 900 高的窗口内**全部可见**（选中项不会被滚出视野）。
