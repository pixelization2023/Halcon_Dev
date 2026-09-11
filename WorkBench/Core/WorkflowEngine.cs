using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using WorkBench.Interfaces;

namespace WorkBench.Core
{
    /// <summary>
    /// 流程引擎核心 — 按顺序执行步骤，支持超时和重试
    /// </summary>
    public class WorkflowEngine : IWorkflowEngine
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider? _serviceProvider;
        private CancellationTokenSource? _currentCts;
        private readonly object _runLock = new();

        public bool IsRunning { get; private set; }
        public IInspectionContext? CurrentContext { get; private set; }

        public event EventHandler<StepResult>? StepCompleted;
        public event EventHandler<AggregatedResult>? WorkflowCompleted;
        public event EventHandler<string>? WorkflowError;

        /// <summary>
        /// 说明：<paramref name="serviceProvider"/> 目前**未被使用** —— 步骤的外部依赖
        /// （PLC / MES）由 WorkBenchViewModel 在构建步骤时显式注入
        ///（见 PLCWriteStep / MESUploadStep 的构造函数）。
        /// 这里保留可空参数只是为了兼容既有注册方式。
        /// </summary>
        public WorkflowEngine(ILogger logger, IServiceProvider? serviceProvider = null)
        {
            _logger = logger.ForContext<WorkflowEngine>();
            _serviceProvider = serviceProvider;
        }

        public async Task<AggregatedResult> ExecuteAsync(
            WorkflowDefinition workflow, IInspectionContext context, CancellationToken ct)
        {
            lock (_runLock)
            {
                if (IsRunning) throw new InvalidOperationException("引擎正在运行中");
                IsRunning = true;
            }

            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CurrentContext = context;
            context.Status = InspectionStatus.Running;

            var result = new AggregatedResult();
            var overallSw = Stopwatch.StartNew();

            _logger.Information("开始执行工作流: {Name} ({Count}个步骤, 最大重试{Retries}次)",
                workflow.Name, workflow.Steps.Count, workflow.MaxRetries);

            try
            {
                // 全局超时
                using var timeoutCts = new CancellationTokenSource(workflow.GlobalTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_currentCts.Token, timeoutCts.Token);

                for (int retry = 0; retry <= workflow.MaxRetries; retry++)
                {
                    result.StepResults.Clear();
                    bool allOk = true;

                    foreach (var step in workflow.Steps)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();

                        // 校验
                        if (!await step.ValidateAsync(context))
                        {
                            var failResult = StepResult.Fail(step.Name, step.Type, "步骤校验失败", 0);
                            result.StepResults.Add(failResult);
                            StepCompleted?.Invoke(this, failResult);
                            allOk = false;
                            break;
                        }

                        // 执行（带步骤超时）
                        var stepSw = Stopwatch.StartNew();
                        StepResult stepResult;

                        try
                        {
                            using var stepCts = new CancellationTokenSource(step.Timeout);
                            using var stepLinked = CancellationTokenSource.CreateLinkedTokenSource(
                                linkedCts.Token, stepCts.Token);

                            stepResult = await step.ExecuteAsync(context, stepLinked.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            stepResult = StepResult.Fail(step.Name, step.Type, "步骤超时", stepSw.ElapsedMilliseconds);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "步骤 [{Step}] 执行异常", step.Name);
                            stepResult = StepResult.Fail(step.Name, step.Type, ex.Message, stepSw.ElapsedMilliseconds);
                        }

                        result.StepResults.Add(stepResult);
                        StepCompleted?.Invoke(this, stepResult);

                        if (!stepResult.Success)
                        {
                            allOk = false;
                            break; // 某步骤失败则跳出，进入重试
                        }
                    }

                    if (allOk)
                    {
                        result.OverallSuccess = true;
                        result.FinalJudgment = "PASS";
                        context.Status = InspectionStatus.Completed;
                        break;
                    }
                    else if (retry < workflow.MaxRetries)
                    {
                        _logger.Warning("流程 [{Name}] 第 {Retry} 次重试", workflow.Name, retry + 1);
                        await Task.Delay(500, linkedCts.Token); // 重试前短暂等待
                    }
                    else
                    {
                        result.OverallSuccess = false;
                        result.FinalJudgment = "FAIL";
                        result.ErrorSummary = $"步骤失败: {result.StepResults.LastOrDefault(r => !r.Success)?.StepName}";
                        context.Status = InspectionStatus.Failed;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.OverallSuccess = false;
                result.FinalJudgment = "CANCELLED";
                context.Status = InspectionStatus.Cancelled;
                WorkflowError?.Invoke(this, "流程被取消");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "流程 [{Name}] 异常", workflow.Name);
                result.OverallSuccess = false;
                result.FinalJudgment = "ERROR";
                result.ErrorSummary = ex.Message;
                context.Status = InspectionStatus.Failed;
                WorkflowError?.Invoke(this, ex.Message);
            }
            finally
            {
                overallSw.Stop();
                result.TotalElapsedMs = overallSw.ElapsedMilliseconds;
                result.CompletedAt = DateTime.Now;
                IsRunning = false;
                CurrentContext = null;
                _currentCts?.Dispose();
                _currentCts = null;
            }

            WorkflowCompleted?.Invoke(this, result);
            _logger.Information("工作流 [{Name}] 执行完成: {Result} ({Ms}ms)",
                workflow.Name, result.FinalJudgment, result.TotalElapsedMs);
            return result;
        }

        public Task StopAsync()
        {
            _currentCts?.Cancel();
            return Task.CompletedTask;
        }
    }
}
