using System.Collections.ObjectModel;
using Halcon.Core;
using Inspection.Services;
using MultiCameraSystem.Services.Interfaces;
using Prism.Mvvm;
using Serilog;

namespace MultiCameraSystem.Services
{
    /// <summary>启动步骤状态</summary>
    public enum StartupStepState
    {
        Pending,
        Running,
        Success,
        /// <summary>降级通过（失败但不阻塞启动，例如相机 SDK / 数据库不可用）</summary>
        Warn,
        Failed
    }

    /// <summary>单个启动步骤（供启动窗口的清单列表绑定）</summary>
    public class StartupStep : BindableBase
    {
        public StartupStep(int index, string title)
        {
            Index = index;
            Title = title;
        }

        public int Index { get; }

        /// <summary>步骤标题</summary>
        public string Title { get; }

        private StartupStepState _state = StartupStepState.Pending;
        public StartupStepState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                    RaisePropertyChanged(nameof(Glyph));
            }
        }

        private string _detail = "等待中…";
        /// <summary>结果说明（成功提示 / 失败原因）</summary>
        public string Detail
        {
            get => _detail;
            set => SetProperty(ref _detail, value);
        }

        /// <summary>状态图标（用文本而不是字体图标，避免启动阶段依赖主题资源）</summary>
        public string Glyph => State switch
        {
            StartupStepState.Running => "◐",
            StartupStepState.Success => "✔",
            StartupStepState.Warn => "!",
            StartupStepState.Failed => "✘",
            _ => "○"
        };

        public override string ToString() => $"{Index}. {Title} [{State}] {Detail}";
    }

    /// <summary>启动进度聚合（启动窗口的 DataContext）</summary>
    public class StartupProgress : BindableBase
    {
        public StartupProgress(IEnumerable<string> stepTitles)
        {
            var index = 1;
            foreach (var title in stepTitles)
                Steps.Add(new StartupStep(index++, title));
        }

        public ObservableCollection<StartupStep> Steps { get; } = new();

        private int _currentIndex = -1;
        public int CurrentIndex
        {
            get => _currentIndex;
            set
            {
                if (SetProperty(ref _currentIndex, value))
                {
                    RaisePropertyChanged(nameof(ProgressPercent));
                    RaisePropertyChanged(nameof(ProgressText));
                }
            }
        }

        private string _statusMessage = "正在准备…";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _lastError = string.Empty;
        public string LastError
        {
            get => _lastError;
            set => SetProperty(ref _lastError, value);
        }

        /// <summary>已完成步骤数 / 总步骤数 → 百分比</summary>
        public double ProgressPercent
        {
            get
            {
                if (Steps.Count == 0) return 0;
                return Math.Clamp((CurrentIndex + 1) * 100.0 / Steps.Count, 0, 100);
            }
        }

        public string ProgressText => $"{Math.Max(0, CurrentIndex + 1)} / {Steps.Count}";

        /// <summary>把某一步置为运行中，其余保留现状</summary>
        public void MarkRunning(int index, string? detail = null)
        {
            if (index < 0 || index >= Steps.Count) return;
            Steps[index].State = StartupStepState.Running;
            if (detail != null) Steps[index].Detail = detail;
        }

        public void MarkResult(int index, StartupStepState state, string detail)
        {
            if (index < 0 || index >= Steps.Count) return;
            Steps[index].State = state;
            Steps[index].Detail = detail;
        }
    }

    /// <summary>
    /// 启动编排服务。
    ///
    /// 解决的问题（方案文档第 10 节 S-1~S-6）：
    /// 旧实现把真正的服务初始化散在 App.OnInitialized() 里"裸做" —— 没有任何进度反馈，
    /// 失败就被全局异常处理吞掉并 Environment.Exit(1)，现场只看到"程序闪一下就没了"；
    /// 而做好的加载界面（LoadingUserControl）没人调用、进度还是假的定时器。
    ///
    /// 现在把所有启动期初始化收敛到这里，按步骤执行并向启动窗口报告**真实**进度，
    /// 每一步区分「致命」与「可降级」。
    /// </summary>
    public class AppStartupService
    {
        /// <summary>
        /// 步骤定义：标题 + 是否为致命步骤。
        /// 注意：配置加载与主题应用**不在**这里 —— 它们必须在 Shell/视图创建之前完成，
        /// 由 App.Initialize() 直接做（没有 IO、不影响启动耗时）。
        /// 这里只放"较慢 / 可能失败 / 需要给用户看进度"的服务初始化。
        /// </summary>
        private static readonly (string Title, bool Fatal)[] StepDefinitions =
        {
            ("应用商业组件授权并启动状态轮询", true),
            ("检测数据库连通性", false),
            ("初始化相机 SDK 并枚举设备", false),
            ("预热 Halcon 运行时", false),
            ("启动检测宿主（相机 → PLC → 检测链路）", false)
        };

        private readonly SettingsService _settings;
        private readonly InspectionResultStore _store;
        private readonly ICameraService _cameraService;
        private readonly InspectionHostService _hostService;
        private readonly StatusMonitorService _status;
        private readonly ILogger _logger;

        public AppStartupService(SettingsService settings,
            InspectionResultStore store, ICameraService cameraService,
            InspectionHostService hostService, StatusMonitorService status, ILogger logger)
        {
            _settings = settings;
            _store = store;
            _cameraService = cameraService;
            _hostService = hostService;
            _status = status;
            _logger = logger.ForContext<AppStartupService>();
        }

        /// <summary>是否需要（重新）执行某一步 —— 重试时只重跑失败的</summary>
        private readonly bool[] _completed = new bool[StepDefinitions.Length];

        private readonly StartupProgress _progress = new(StepDefinitions.Select(s => s.Title));

        public static IReadOnlyList<string> StepTitles => StepDefinitions.Select(s => s.Title).ToList();

        /// <summary>与启动窗口绑定的进度聚合对象（同一实例）</summary>
        public StartupProgress Progress => _progress;

        /// <summary>本次启动中失败过、但被降级跳过的步骤数</summary>
        public int DegradedStepCount => Enumerable.Range(0, StepDefinitions.Length)
            .Count(i => _progress.Steps[i].State == StartupStepState.Warn);

        /// <summary>执行全部未完成的步骤。返回 false 表示存在致命失败（应停在启动界面）。</summary>
        public bool RunAll(Action? onStepChanged = null)
        {
            for (int i = 0; i < StepDefinitions.Length; i++)
            {
                if (_completed[i]) continue;
                if (!RunStep(i, onStepChanged))
                    return false;
            }

            return true;
        }

        /// <summary>执行单个步骤；返回 false 表示致命失败</summary>
        public bool RunStep(int index, Action? onStepChanged = null)
        {
            var (title, fatal) = StepDefinitions[index];

            _progress.CurrentIndex = index;
            _progress.MarkRunning(index);
            _progress.StatusMessage = title;
            onStepChanged?.Invoke();

            try
            {
                var detail = Execute(index);
                _progress.MarkResult(index, StartupStepState.Success, detail);
                _completed[index] = true;
                _logger.Information("启动步骤 {Index} 完成: {Title} — {Detail}", index + 1, title, detail);
                onStepChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                var reason = $"{ex.GetType().Name}: {ex.Message}";
                _progress.LastError = $"{title} 失败 — {reason}";
                _logger.Error(ex, "启动步骤 {Index} 失败: {Title}", index + 1, title);

                if (fatal)
                {
                    _progress.MarkResult(index, StartupStepState.Failed, reason);
                    onStepChanged?.Invoke();
                    return false;
                }

                // 可降级：标记为警告并视为"已处理"，避免重试时反复卡在同一处
                _progress.MarkResult(index, StartupStepState.Warn, $"已跳过（{reason}）");
                _completed[index] = true;
                onStepChanged?.Invoke();
                return true;
            }
        }

        /// <summary>该步骤失败是否致命（供启动窗口决定是否显示"仍要继续"）</summary>
        public static bool IsFatal(int index) => StepDefinitions[index].Fatal;

        /// <summary>真正的初始化动作</summary>
        private string Execute(int index)
        {
            switch (index)
            {
                case 0:
                {
                    // 授权码必须在任何 HslCommunication 对象创建之前注册
                    PLCModule.HslLicense.Initialize(_settings.Current.Plc.HslAuthorizationCode, _logger);
                    _status.Start();
                    return "HslCommunication 授权已注册，状态轮询已启动";
                }

                case 1:
                {
                    // 数据库不可用时整机仍能跑（结果暂存本地），所以是"可降级"步骤
                    var connected = _store.TestConnection();
                    if (!connected)
                        throw new InvalidOperationException("数据库连接失败（可在系统设置里检查 MySQL 参数）");

                    var schema = _store.EnsureSchema();
                    return schema ? "数据库连接成功，表结构已就绪" : "数据库连接成功（表结构检查未通过）";
                }

                case 2:
                {
                    _cameraService.Initialize();
                    var count = _cameraService.AvailableCameras.Count();
                    return count > 0
                        ? $"相机 SDK 已初始化，发现 {count} 台设备"
                        : "相机 SDK 已初始化，但未发现设备（请检查供电与网线）";
                }

                case 3:
                {
                    // Halcon 运行时首次加载很慢（本机实测 34.8 秒），但检测主界面本身并不需要它 ——
                    // 只有真正投产跑配方时才需要。如果把它算进启动阻塞，用户要盯着启动界面等半分钟。
                    // 所以这里只**发起**后台预热，不等待：
                    //   - 用户很快就能进主界面；
                    //   - 等真正开始检测时运行时早已就绪。
                    StartHalconPreheatAsync();
                    return "Halcon 运行时预热已在后台启动（不阻塞进入主界面）";
                }

                case 4:
                {
                    // 这一步把 PLC 触发、相机取流接到检测编排器上。
                    // InspectionHostService 在构造时完成事件订阅（见其构造函数），
                    // 本类通过构造注入持有单例，因此实例已经就绪。
                    return _hostService.HasBoundCameras
                        ? "检测宿主已就绪，相机已绑定"
                        : "检测宿主已就绪（等待加载产品后自动绑定相机）";
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, "未知的启动步骤");
            }
        }

        /// <summary>
        /// 后台预热 Halcon 运行时。
        /// 预热成功与否不影响程序可用性：真的失败时，投产那一刻会由检测流程给出明确错误
        ///（"加载 Halcon 过程失败"），这里只负责把"第一次执行的 30 多秒"提前消化掉。
        /// </summary>
        private void StartHalconPreheatAsync()
        {
            if (_halconPreheatStarted) return;
            _halconPreheatStarted = true;

            _ = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var engine = new HalconEngine(_logger);
                    if (engine.InitEngine())
                    {
                        sw.Stop();
                        _logger.Information("Halcon 运行时后台预热完成（{Ms}ms）", sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger.Warning("Halcon 运行时后台预热失败：InitEngine 返回 false（请检查 HALCON 授权与 halcon.dll）");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Halcon 运行时后台预热异常");
                }
            });
        }

        private bool _halconPreheatStarted;
    }
}
