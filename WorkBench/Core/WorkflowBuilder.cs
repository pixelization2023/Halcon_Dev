using Serilog;
using WorkBench.Interfaces;

namespace WorkBench.Core
{
    /// <summary>
    /// 流程构建器（Fluent API）。
    ///
    /// 注意：本类**不需要** IServiceProvider —— 步骤的外部依赖（PLC / MES）
    /// 由 <see cref="WritePLC"/> / <see cref="UploadMES"/> 的参数显式传入，构建器只负责拼装
    /// <see cref="WorkflowDefinition"/>。
    /// （旧实现是步骤在运行时自己走 MVS.Core.AppContainer 服务定位器；该定位器已删除。）
    /// 旧签名还把 IServiceProvider 声明为"可选参数"却在方法体里 `?? throw ArgumentNullException`，
    /// 而唯一的调用方（WorkBenchViewModel）从来没传过 —— 结果就是**点「运行」必抛异常**。
    /// </summary>
    public class WorkflowBuilder
    {
        private readonly WorkflowDefinition _workflow = new();
        private readonly ILogger _logger;

        private WorkflowBuilder(string name)
        {
            _workflow.Name = name;
            _logger = Serilog.Log.Logger.ForContext<WorkflowBuilder>();
        }

        /// <summary>创建一个流程构建器</summary>
        /// <param name="name">流程名称</param>
        /// <param name="serviceProvider">保留参数（历史签名，已不再需要；传入也会被忽略）</param>
        public static WorkflowBuilder Create(string name, IServiceProvider? serviceProvider = null)
            => new(string.IsNullOrWhiteSpace(name) ? "未命名流程" : name);

        public WorkflowBuilder AddStep(IWorkflowStep step)
        {
            _workflow.Steps.Add(step);
            _logger.Debug("添加步骤: {Step} [{Type}]", step.Name, step.Type);
            return this;
        }

        public WorkflowBuilder Acquire(string cameraName, TimeSpan? timeout = null)
        {
            var step = new Steps.AcquisitionStep(cameraName, timeout ?? TimeSpan.FromSeconds(3));
            _workflow.Steps.Add(step);
            _logger.Debug("添加采集步骤: {Camera}", cameraName);
            return this;
        }

        public WorkflowBuilder Inspect(string programName, Dictionary<string, object>? parameters = null, TimeSpan? timeout = null)
        {
            var step = new Steps.InspectionStep(programName, parameters, timeout ?? TimeSpan.FromSeconds(10));
            _workflow.Steps.Add(step);
            _logger.Debug("添加检测步骤: {Program}", programName);
            return this;
        }

        public WorkflowBuilder Decide(Func<IInspectionContext, bool>? condition = null)
        {
            var step = new Steps.DecisionStep(condition);
            _workflow.Steps.Add(step);
            _logger.Debug("添加判定步骤");
            return this;
        }

        /// <summary>
        /// 添加「PLC 写入」步骤。
        /// </summary>
        /// <param name="signals">要写入的信号</param>
        /// <param name="timeout">超时</param>
        /// <param name="plcCommunicator">
        /// PLC 通信器（**必需**）。显式传入后步骤的依赖在构造期就确定，
        /// 不再需要 ExecuteAsync 里那套"运行时从容器取"的服务定位器。
        /// </param>
        public WorkflowBuilder WritePLC(Dictionary<string, object> signals, TimeSpan? timeout,
            PLCModule.Interfaces.IPLCCommunicator plcCommunicator)
        {
            var step = new Steps.PLCWriteStep(signals, timeout ?? TimeSpan.FromSeconds(2), plcCommunicator);
            _workflow.Steps.Add(step);
            _logger.Debug("添加PLC写入步骤: {Count}个信号", signals.Count);
            return this;
        }

        /// <summary>
        /// 添加「MES 上传」步骤。
        /// </summary>
        /// <param name="timeout">超时</param>
        /// <param name="mesConnector">MES 连接器（**必需**，理由同 <see cref="WritePLC"/>）</param>
        public WorkflowBuilder UploadMES(TimeSpan? timeout, MESModule.Interfaces.IMESConnector mesConnector)
        {
            var step = new Steps.MESUploadStep(timeout ?? TimeSpan.FromSeconds(5), mesConnector);
            _workflow.Steps.Add(step);
            _logger.Debug("添加MES上传步骤");
            return this;
        }

        public WorkflowBuilder WithGlobalTimeout(TimeSpan timeout)
        {
            _workflow.GlobalTimeout = timeout;
            return this;
        }

        public WorkflowBuilder WithMaxRetries(int retries)
        {
            _workflow.MaxRetries = retries;
            return this;
        }

        public WorkflowBuilder WithDescription(string desc)
        {
            _workflow.Description = desc;
            return this;
        }

        public WorkflowDefinition Build()
        {
            _logger.Information("构建工作流: {Name} ({Count}个步骤, 超时{Timeout}s, 重试{Retries}次)",
                _workflow.Name, _workflow.Steps.Count, _workflow.GlobalTimeout.TotalSeconds, _workflow.MaxRetries);
            return _workflow;
        }
    }
}
