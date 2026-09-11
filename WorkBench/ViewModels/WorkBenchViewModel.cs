using Serilog;
using System.Collections.ObjectModel;
using MVS.Core;
using WorkBench.Core;
using WorkBench.Interfaces;
using WorkBench.Models;

namespace WorkBench.ViewModels
{
    public class WorkBenchViewModel : BindableBase, INavigationAware
    {
        private readonly IWorkflowEngine _engine;
        private readonly IVisionInterfaceProvider _visionInterface;

        /// <summary>
        /// PLC / MES 依赖：由组合根注入，构建步骤时**显式传给 Step**。
        ///
        /// 解耦要点：这两个依赖以前是 Step 在 ExecuteAsync 里自己走
        /// <c>MVS.Core.AppContainer</c>（服务定位器）取的 —— 依赖关系编译期不可见、
        /// 只能运行到那一步才发现缺失，也无法在测试里替换。现在从这里显式传下去。
        /// </summary>
        private readonly PLCModule.Interfaces.IPLCCommunicator _plcCommunicator;
        private readonly MESModule.Interfaces.IMESConnector _mesConnector;

        private readonly ILogger _logger;

        public WorkBenchViewModel(IWorkflowEngine engine, IVisionInterfaceProvider visionInterface,
            ILogger logger,
            PLCModule.Interfaces.IPLCCommunicator plcCommunicator,
            MESModule.Interfaces.IMESConnector mesConnector)
        {
            _engine = engine;
            _visionInterface = visionInterface;
            _logger = logger.ForContext<WorkBenchViewModel>();
            _plcCommunicator = plcCommunicator
                ?? throw new ArgumentNullException(nameof(plcCommunicator), "需要注入 IPLCCommunicator（见 PLCModule.RegisterTypes）");
            _mesConnector = mesConnector
                ?? throw new ArgumentNullException(nameof(mesConnector), "需要注入 IMESConnector（见 MESModule.RegisterTypes）");

            _engine.StepCompleted += OnStepCompleted;
            _engine.WorkflowCompleted += OnWorkflowCompleted;
            _engine.WorkflowError += OnWorkflowError;

            QueryInterfaceCommand = new DelegateCommand<StepItem>(ExecuteQueryInterface);
            AddPortCommand = new DelegateCommand<StepItem>(ExecuteAddPort);
            RemovePortCommand = new DelegateCommand<StepPort>(ExecuteRemovePort);
            // 预设可选步骤类型
            AvailableStepTypes = new ObservableCollection<string>
            {
                "图像采集 (Acquisition)",
                "HALCON 检测 (Inspection)",
                "判定 (Decision)",
                "PLC 写入 (PLCWrite)",
                "MES 上传 (MESUpload)"
            };
        }

        #region 属性

        private string _workflowName = "标准检测流程";
        public string WorkflowName
        {
            get => _workflowName;
            set => SetProperty(ref _workflowName, value);
        }

        private int _maxRetries = 1;
        public int MaxRetries
        {
            get => _maxRetries;
            set => SetProperty(ref _maxRetries, value);
        }

        private int _globalTimeoutSeconds = 30;
        public int GlobalTimeoutSeconds
        {
            get => _globalTimeoutSeconds;
            set => SetProperty(ref _globalTimeoutSeconds, value);
        }

        public ObservableCollection<string> AvailableStepTypes { get; }

        public ObservableCollection<StepItem> Steps { get; } = new();

        private StepItem? _selectedStep;
        public StepItem? SelectedStep
        {
            get => _selectedStep;
            set => SetProperty(ref _selectedStep, value);
        }

        private string _status = "就绪 — 请构建流程后点击运行";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                SetProperty(ref _isRunning, value);
                RaisePropertyChanged(nameof(CanRun));
            }
        }

        public bool CanRun => !IsRunning && Steps.Count > 0;

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _currentStepName = "";
        public string CurrentStepName
        {
            get => _currentStepName;
            set => SetProperty(ref _currentStepName, value);
        }

        private string _finalResult = "";
        public string FinalResult
        {
            get => _finalResult;
            set => SetProperty(ref _finalResult, value);
        }

        private string _elapsed = "";
        public string Elapsed
        {
            get => _elapsed;
            set => SetProperty(ref _elapsed, value);
        }

        public ObservableCollection<StepResult> StepResults { get; } = new();

        public ObservableCollection<HistoryRecord> History { get; } = new();

        #endregion

        #region 步骤管理命令

        public DelegateCommand<string> AddStepCommand =>
            new(typeName =>
            {
                var step = new StepItem
                {
                    TypeName = typeName ?? "Unknown",
                    CameraName = "Camera1",
                    ProgramPath = "default.hdevp",
                    TimeoutSeconds = 10
                };
                Steps.Add(step);
                RaisePropertyChanged(nameof(CanRun));
                _logger.Debug("添加步骤: {Type}", step.TypeName);
                Status = $"已添加 {Steps.Count} 个步骤";
            });

        public DelegateCommand<StepItem> RemoveStepCommand =>
            new(step =>
            {
                if (step != null)
                {
                    Steps.Remove(step);
                    RaisePropertyChanged(nameof(CanRun));
                    _logger.Debug("移除步骤: {Type}", step.TypeName);
                }
            });

        public DelegateCommand MoveUpCommand =>
            new(() =>
            {
                if (SelectedStep == null) return;
                var idx = Steps.IndexOf(SelectedStep);
                if (idx > 0)
                    Steps.Move(idx, idx - 1);
            });

        public DelegateCommand MoveDownCommand =>
            new(() =>
            {
                if (SelectedStep == null) return;
                var idx = Steps.IndexOf(SelectedStep);
                if (idx < Steps.Count - 1)
                    Steps.Move(idx, idx + 1);
            });

        public DelegateCommand ClearStepsCommand =>
            new(() =>
            {
                Steps.Clear();
                RaisePropertyChanged(nameof(CanRun));
                Status = "步骤已清空";
            });

        /// <summary>从 Halcon 过程接口导入端口（输入+输出）</summary>
        public DelegateCommand<StepItem> QueryInterfaceCommand { get; }

        /// <summary>给某步骤手工添加一个输入端口</summary>
        public DelegateCommand<StepItem> AddPortCommand { get; }

        /// <summary>移除一个端口</summary>
        public DelegateCommand<StepPort> RemovePortCommand { get; }

        private void ExecuteQueryInterface(StepItem? step)
        {
            if (step == null)
            {
                Status = "请先在步骤列表里选中一个步骤";
                return;
            }

            if (string.IsNullOrWhiteSpace(step.ProgramPath))
            {
                Status = "该步骤没有填写「程序」（过程名），无法读取接口";
                return;
            }

            // 先清掉旧的输出端口，输入端口保留（现场已经填过值的不要被冲掉）
            foreach (var port in step.Ports.Where(p => p.Direction == VisionPortDirection.Output).ToList())
                step.Ports.Remove(port);

            var descriptors = _visionInterface.QueryPorts(null, step.ProgramPath);

            if (descriptors.Count == 0)
            {
                Status = $"未读取到过程 [{step.ProgramPath}] 的接口：请确认方案已加载、过程名与控制台里一致";
                return;
            }

            int added = 0;
            foreach (var d in descriptors)
            {
                // 输入端口如果已存在同名项，只更新类型/说明，不覆盖用户填好的值
                var existing = step.Ports.FirstOrDefault(p =>
                    p.Direction == d.Direction && string.Equals(p.Name, d.Name, StringComparison.Ordinal));

                if (existing != null)
                {
                    existing.DataType = d.DataType;
                    existing.Description = d.Description;
                    continue;
                }

                step.Ports.Add(new StepPort
                {
                    Direction = d.Direction,
                    Name = d.Name,
                    DataType = d.DataType,
                    Value = string.Empty,
                    Description = d.Description
                });
                added++;
            }

            step.RaisePortSummaries();

            var inputs = step.Ports.Count(p => p.Direction == VisionPortDirection.Input);
            var outputs = step.Ports.Count(p => p.Direction == VisionPortDirection.Output);
            Status = $"已读取过程 [{step.ProgramPath}] 的接口：输入 {inputs} 个 / 输出 {outputs} 个（新增 {added} 个）";
            _logger.Information("工作台步骤接口已导入: {Procedure} 输入{In} 输出{Out}",
                step.ProgramPath, inputs, outputs);
        }

        private void ExecuteAddPort(StepItem? step)
        {
            if (step == null) return;

            step.Ports.Add(new StepPort
            {
                Direction = VisionPortDirection.Input,
                Name = "NewParam",
                DataType = VisionPortDataType.Double,
                Value = "0",
                Description = "手工添加"
            });
            step.RaisePortSummaries();
        }

        private void ExecuteRemovePort(StepPort? port)
        {
            if (port == null) return;

            foreach (var step in Steps)
            {
                if (step.Ports.Remove(port))
                {
                    step.RaisePortSummaries();
                    break;
                }
            }
        }

        #endregion

        #region 执行命令

        public DelegateCommand RunCommand =>
            new(async () =>
            {
                if (Steps.Count == 0)
                {
                    Status = "请先添加步骤";
                    return;
                }

                try
                {
                    IsRunning = true;
                    Progress = 0;
                    StepResults.Clear();
                    CurrentStepName = "";
                    FinalResult = "";
                    Elapsed = "";

                    Status = "构建流程中...";

                    // 使用 WorkflowBuilder
                    var builder = WorkflowBuilder.Create(WorkflowName)
                        .WithGlobalTimeout(TimeSpan.FromSeconds(GlobalTimeoutSeconds))
                        .WithMaxRetries(MaxRetries);

                    foreach (var s in Steps)
                    {
                        // 步骤参数：目前由 StepItem.Parameters 提供（输入端口模型落地前的过渡，
                        // 见方案文档第 7 节）。这里必须真正传下去 ——
                        // 旧实现把 Inspect() 的 parameters 参数丢掉了，算法参数无处配置。
                        var parameters = s.BuildParameterDictionary();

                        switch (s.TypeName)
                        {
                            case var t when t.Contains("Acquisition") || t.Contains("采集"):
                                builder.Acquire(s.CameraName, TimeSpan.FromSeconds(s.TimeoutSeconds));
                                break;
                            case var t when t.Contains("Inspection") || t.Contains("检测"):
                                builder.Inspect(s.ProgramPath, parameters, TimeSpan.FromSeconds(s.TimeoutSeconds));
                                break;
                            case var t when t.Contains("Decision") || t.Contains("判定"):
                                builder.Decide();
                                break;
                            case var t when t.Contains("PLCWrite") || t.Contains("PLC"):
                                // 显式把 IPLCCommunicator 传进步骤：依赖在构造期确定，
                                // 不再由步骤自己在运行时走服务定位器（见字段注释）
                                builder.WritePLC(new Dictionary<string, object> { { "Result", true } },
                                    TimeSpan.FromSeconds(s.TimeoutSeconds), _plcCommunicator);
                                break;
                            case var t when t.Contains("MESUpload") || t.Contains("MES"):
                                builder.UploadMES(TimeSpan.FromSeconds(s.TimeoutSeconds), _mesConnector);
                                break;
                        }
                    }

                    var workflow = builder.Build();
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    Status = "执行中...";
                    _logger.Information("开始执行工作流: {Name}", WorkflowName);

                    var context = new InspectionContext();
                    var result = await _engine.ExecuteAsync(workflow, context, CancellationToken.None);

                    sw.Stop();
                    Elapsed = $"{sw.ElapsedMilliseconds} ms";

                    foreach (var sr in result.StepResults)
                        StepResults.Add(sr);

                    FinalResult = result.OverallSuccess ? "✓ PASS" : "✗ FAIL";
                    Status = result.OverallSuccess ? "流程执行成功" : "流程执行失败";

                    History.Insert(0, new HistoryRecord
                    {
                        Name = WorkflowName,
                        Result = result.FinalJudgment,
                        ElapsedMs = result.TotalElapsedMs,
                        Time = DateTime.Now,
                        StepCount = Steps.Count
                    });

                    _logger.Information("工作流执行完成: {Result} ({Ms}ms)",
                        result.FinalJudgment, result.TotalElapsedMs);
                }
                catch (Exception ex)
                {
                    Status = $"执行异常: {ex.Message}";
                    _logger.Error(ex, "工作流执行异常");
                }
                finally
                {
                    IsRunning = false;
                    Progress = 100;
                }
            });

        public DelegateCommand StopCommand =>
            new(async () =>
            {
                await _engine.StopAsync();
                Status = "已停止";
                _logger.Information("工作流手动停止");
            });

        #endregion

        #region 事件回调

        private void OnStepCompleted(object? sender, StepResult e)
        {
            var total = Steps.Count;
            var done = StepResults.Count + 1;
            Progress = (double)done / total * 100;
            CurrentStepName = e.StepName;
        }

        private void OnWorkflowCompleted(object? sender, AggregatedResult e)
        {
            IsRunning = false;
            Progress = 100;
        }

        private void OnWorkflowError(object? sender, string e)
        {
            IsRunning = false;
            Status = $"错误: {e}";
        }

        #endregion

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入工作台界面");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _logger.Debug("离开工作台界面");
        }
    }

    public class StepItem : BindableBase
    {
        private string _typeName = "";
        public string TypeName { get => _typeName; set => SetProperty(ref _typeName, value); }

        private string _cameraName = "Camera1";
        public string CameraName { get => _cameraName; set => SetProperty(ref _cameraName, value); }

        private string _programPath = "default.hdevp";
        public string ProgramPath { get => _programPath; set => SetProperty(ref _programPath, value); }

        private int _timeoutSeconds = 10;
        public int TimeoutSeconds { get => _timeoutSeconds; set => SetProperty(ref _timeoutSeconds, value); }

        private string _parametersText = "";
        /// <summary>
        /// 算法参数（输入端口的最小可用形态）。
        /// 格式：<c>参数名=值</c>，多个用 <c>;</c> 或换行分隔，例如
        /// <c>MinGray=128; MaxGray=255; ModelFile=C:\model.shm</c>。
        /// 纯数字会自动转成数值类型传给过程；其余按字符串传递。
        ///
        /// 说明：更完整的做法是用下面的 <see cref="Ports"/>（可由「从过程接口导入」一键生成）。
        /// 保留这个文本框是为了兼容手写参数的现场习惯，两者会合并后一起传给过程。
        /// </summary>
        public string ParametersText
        {
            get => _parametersText;
            set => SetProperty(ref _parametersText, value);
        }

        /// <summary>
        /// 步骤端口（输入 / 输出）。由「从过程接口导入」按 Halcon 过程的接口自动填充，
        /// 也可以在界面上手工增删。输出端口只用于查看与后续引用。
        /// </summary>
        public ObservableCollection<StepPort> Ports { get; } = new();

        /// <summary>输入端口的可读汇总（列表项上显示）</summary>
        public string InputPortSummary
        {
            get
            {
                var inputs = Ports.Where(p => p.Direction == VisionPortDirection.Input).Select(p => p.Name).ToList();
                return inputs.Count == 0 ? "（未配置输入）" : string.Join(", ", inputs);
            }
        }

        /// <summary>输出端口的可读汇总（列表项上显示）</summary>
        public string OutputPortSummary
        {
            get
            {
                var outputs = Ports.Where(p => p.Direction == VisionPortDirection.Output).Select(p => p.Name).ToList();
                return outputs.Count == 0 ? "（未读取输出）" : string.Join(", ", outputs);
            }
        }

        /// <summary>端口集合变化后刷新两个汇总文本</summary>
        public void RaisePortSummaries()
        {
            RaisePropertyChanged(nameof(InputPortSummary));
            RaisePropertyChanged(nameof(OutputPortSummary));
        }

        /// <summary>
        /// 汇总最终传给过程的参数：
        /// 先取 <see cref="ParametersText"/> 的手写键值，再用 <see cref="Ports"/> 里的输入端口覆盖/追加
        /// （端口是结构化的，优先级更高）。
        /// </summary>
        public Dictionary<string, object>? BuildParameterDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);

            // 1) 手写参数
            if (!string.IsNullOrWhiteSpace(_parametersText))
            {
                foreach (var raw in _parametersText.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;

                    var index = line.IndexOf('=');
                    if (index <= 0) continue;

                    var key = line.Substring(0, index).Trim();
                    var value = line.Substring(index + 1).Trim();
                    if (key.Length == 0) continue;

                    dict[key] = ConvertValue(value);
                }
            }

            // 2) 结构化输入端口（覆盖同名手写参数）
            foreach (var port in Ports)
            {
                if (port.Direction != VisionPortDirection.Input) continue;
                if (string.IsNullOrWhiteSpace(port.Name)) continue;

                // 图像端口由执行引擎单独设置（SetInputIconicParam），这里不放进控制参数里
                if (port.DataType == VisionPortDataType.Image) continue;

                dict[port.Name.Trim()] = ConvertValue(port.Value);
            }

            return dict.Count > 0 ? dict : null;
        }

        /// <summary>按端口类型把字符串转成合适的对象（数值优先）</summary>
        private static object ConvertValue(string? value)
        {
            var text = value ?? string.Empty;

            if (long.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var l))
                return l;

            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;

            if (bool.TryParse(text, out var b))
                return b;

            return text;
        }

        public string DisplayName => TypeName switch
        {
            var t when t.Contains("Acquisition") || t.Contains("采集") => $"📷 {TypeName} [{CameraName}]",
            var t when t.Contains("Inspection") || t.Contains("检测") => $"🔬 {TypeName} [{ProgramPath}]",
            var t when t.Contains("Decision") || t.Contains("判定") => $"📊 {TypeName}",
            var t when t.Contains("PLC") => $"🔌 {TypeName}",
            var t when t.Contains("MES") => $"☁ {TypeName}",
            _ => TypeName
        };
    }

    public class HistoryRecord
    {
        public DateTime Time { get; set; }
        public string Name { get; set; } = "";
        public string Result { get; set; } = "";
        public int StepCount { get; set; }
        public long ElapsedMs { get; set; }

        /// <summary>是否通过（颜色由视图按主题画刷决定）</summary>
        public bool IsPass => string.Equals(Result, "PASS", StringComparison.Ordinal);

        public string Summary => $"[{Time:HH:mm:ss}] {Name} → {Result} ({StepCount}步, {ElapsedMs}ms)";
    }
}
