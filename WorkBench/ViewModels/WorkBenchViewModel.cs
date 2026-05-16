using Serilog;
using System.Collections.ObjectModel;
using WorkBench.Core;
using WorkBench.Interfaces;
using WorkBench.Models;

namespace WorkBench.ViewModels
{
    public class WorkBenchViewModel : BindableBase, INavigationAware
    {
        private readonly IWorkflowEngine _engine;
        private readonly ILogger _logger;

        public WorkBenchViewModel(IWorkflowEngine engine, ILogger logger)
        {
            _engine = engine;
            _logger = logger.ForContext<WorkBenchViewModel>();

            _engine.StepCompleted += OnStepCompleted;
            _engine.WorkflowCompleted += OnWorkflowCompleted;
            _engine.WorkflowError += OnWorkflowError;

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
                        switch (s.TypeName)
                        {
                            case var t when t.Contains("Acquisition") || t.Contains("采集"):
                                builder.Acquire(s.CameraName, TimeSpan.FromSeconds(s.TimeoutSeconds));
                                break;
                            case var t when t.Contains("Inspection") || t.Contains("检测"):
                                builder.Inspect(s.ProgramPath, timeout: TimeSpan.FromSeconds(s.TimeoutSeconds));
                                break;
                            case var t when t.Contains("Decision") || t.Contains("判定"):
                                builder.Decide();
                                break;
                            case var t when t.Contains("PLCWrite") || t.Contains("PLC"):
                                builder.WritePLC(new Dictionary<string, object> { { "Result", true } },
                                    TimeSpan.FromSeconds(s.TimeoutSeconds));
                                break;
                            case var t when t.Contains("MESUpload") || t.Contains("MES"):
                                builder.UploadMES(TimeSpan.FromSeconds(s.TimeoutSeconds));
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
