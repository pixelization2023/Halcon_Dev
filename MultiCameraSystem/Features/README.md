# 预留接口层（Features）

这一层解决的问题：**旧版本（GitHub 上的 `pixelization2023/Halcon_Dev` 旧版）里的界面与功能，
在合并到当前版本时少了一部分。** 而且当前"有哪些页面"是写死在
`MainWindowViewModel.BuildNavigation()` 里的一个数组 —— 每加/补一个页面都要改那一段中心代码，
多分支合并时最容易在这里被覆盖，这正是"界面丢了"的典型成因。

所以这里把"有哪些页面"从**硬编码**改成**各模块自己声明 + 注册表汇总**：

| 文件 | 作用 |
| --- | --- |
| `INavigationFeature.cs` | 导航功能契约（视图名/标题/图标/分组/所需角色/状态） + `IFeatureProvider` + 默认实现 |
| `FeatureRegistry.cs` | 汇总所有 provider；区分**已实现**与**预留未实现**；可生成预留清单文本 |
| `ReservedFeatureProvider.cs` | 预留功能清单（已知缺失但故意没做的项，每条都写了依据） |

## 关键性质：不改变任何现有行为

- `FeatureRegistry` 在**一个 provider 都没注册**时返回空集合；
- 这一层**没有改动** `MainWindowViewModel`、没有新增视图、没有改导航顺序;
- 预留项（`FeatureStatusKind.Reserved`）**不会出现在导航里**，只进「预留清单」，
  避免出现"点进去是个空白页"这种更糟的体验。

## 怎么启用（等你确认后再做，约 6 行）

### 1) 注册（`MultiCameraSystem/App.xaml.cs` 的 `RegisterTypes`）

```csharp
// 预留接口层：汇总各模块声明的导航功能
containerRegistry.RegisterSingleton<FeatureRegistry>();
containerRegistry.RegisterSingleton<IFeatureProvider, MultiCameraSystem.Features.ReservedFeatureProvider>();
```

### 2) 把注册表接进导航（`MultiCameraSystem/ViewModels/MainWindowViewModel.cs`）

构造函数加一个**可空**参数（DryIoc 对未注册的可空参数不会报错，所以这一步不会让启动变脆）：

```csharp
public MainWindowViewModel(IRegionManager regionManager, ThemeService themeService,
    SettingsService settings, UserSessionService session, IDialogService dialogService, ILogger logger,
    FeatureRegistry? features = null)     // ← 新增
{
    _features = features;
    ...
}
```

然后在 `ApplyRoleFilter()` 末尾把"由 provider 声明的页面"追加进来：

```csharp
// 由 IFeatureProvider 声明的页面（预留接口层），不动上面那段硬编码的既有导航
if (_features != null)
{
    foreach (var f in _features.GetVisible(role))
    {
        var item = new NavigationItem(f.ViewName, f.Title, f.Caption, f.Icon,
            f.Group == FeatureGroup.TopBar, f.RequiredRole);

        SideNavigation.Add(item);
        if (item.ShowInTopBar) TopNavigation.Add(item);
    }
}
```

### 3) 以后补回一个界面时

1. 建 View + ViewModel，`RegisterForNavigation<XxxView>()`；
2. 把 `ReservedFeatureProvider` 里对应那条的 `Status` 从 `Reserved` 改成 `Declared`、
   `ViewName` 从占位的 `Reserved_Xxx` 改成真实视图名。

**不需要改 `MainWindowViewModel`，也不需要改别的模块的文件。**

## 预留清单怎么看到

```csharp
// 现在就能用（不需要启用导航接线）
var registry = Container.Resolve<FeatureRegistry>();
logger.Information(string.Join(Environment.NewLine, registry.BuildReservedFeatureReport()));
```

打算接进「自检与仿真」页作为一项检查（本轮没有动界面，所以还没接）。

## 当前预留的 6 项

| # | 功能 | 依据 |
| --- | --- | --- |
| 1 | 用户与权限管理 | `UserSessionService` 属性说明写着"管理员：全部权限 + 用户管理"，但当前只有登录对话框 |
| 2 | 旧配置（.asol）导入工具 | `Docs/迁移说明.md` 明确写"历史 .asol 无法自动读取，需现场重新配置" |
| 3 | 模板匹配与交互式建模 | 参考项目 `MachineVision.templateMach` 有绘制 ROI + 建模板 + 匹配结果渲染，当前无对应界面 |
| 4 | 历史数据查询与导出 | 结果只写 MySQL，界面侧只有内存中最近 200 条的实时表格（**旧版是否有此界面待确认**） |
| 5 | 多语言（中/英）切换 | 参考项目有 `zh_CN.xaml` / `en_US.xaml` / `LanguageConverter`（**旧版是否真的启用待确认**） |
| 6 | —— | 等你把旧版界面清单给我后继续补 |

## 我需要你提供的

本机 **没有**旧版本，也**没有**源 WinForms 项目（`FrmMian` / `SerLion` / `窗体`）：
- `D:\黑鲸鱼工作区` 只有 `AI` 与 `DO IT!!`；`DO IT!!\Halcon` 是参考项目（界面比 AI 还少）；
- `E:\` 搜过，只有 `HalconToolBox`、`HalconWpfNet`、`Prism官方示例`、`wpf-uidesign` 这类参考代码；
- `AI` 仓库是 **2026-09-16 01:39 才 clone** 的，只有一个分支/一个提交、无 stash、无悬空对象，
  所以**旧版本不在本地 git 里**；
- `github.com` 在本机被 hosts 指到 `127.0.0.1`（连同 `api.github.com`、`raw.githubusercontent.com` 等
  20 多个域名），所以**拉不到远端**。

要精确对齐"到底丢了哪些界面"，需要下面任一项：
1. 允许我临时注释掉 hosts 里屏蔽 github 的那几行（系统级改动，**需要你明确同意**）；
2. 或者你把旧版本的界面清单（截图 / 目录列表 / 导出的文件）放到 `D:\黑鲸鱼工作区` 下。
