using Serilog;
using WorkBench.Interfaces;

namespace WorkBench.Core
{
    public class WorkflowBuilder
    {
        private readonly WorkflowDefinition _workflow = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        private WorkflowBuilder(string name, IServiceProvider serviceProvider)
        {
            _workflow.Name = name;
            _serviceProvider = serviceProvider;
            _logger = Serilog.Log.Logger.ForContext<WorkflowBuilder>();
        }

        public static WorkflowBuilder Create(string name, IServiceProvider? serviceProvider = null)
            => new(name, serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider)));

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

        public WorkflowBuilder WritePLC(Dictionary<string, object> signals, TimeSpan? timeout = null)
        {
            var step = new Steps.PLCWriteStep(signals, timeout ?? TimeSpan.FromSeconds(2));
            _workflow.Steps.Add(step);
            _logger.Debug("添加PLC写入步骤: {Count}个信号", signals.Count);
            return this;
        }

        public WorkflowBuilder UploadMES(TimeSpan? timeout = null)
        {
            var step = new Steps.MESUploadStep(timeout ?? TimeSpan.FromSeconds(5));
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
