using Halcon.Core;
using Inspection.Models;
using MVS.Core;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 把 Halcon 过程接口暴露给 WorkBench（工作台）。
    ///
    /// 实现的是共享内核 MVS.Core 里的 <see cref="IVisionInterfaceProvider"/>，
    /// 因此既不需要 WorkBench 引用 Inspection，也不需要 Inspection 引用 WorkBench，
    /// 依赖关系保持单向。
    ///
    /// 用途：「工作台 → 选中检测步骤 → 从过程接口导入」一键把
    /// Image / MinGray / out0 / box0 / image0 这些端口铺到界面上。
    /// </summary>
    public class VisionInterfaceProvider : IVisionInterfaceProvider
    {
        private readonly HalconInspectionService _halcon;
        private readonly ProductRepository _repository;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly ILogger _logger;

        public VisionInterfaceProvider(HalconInspectionService halcon, ProductRepository repository,
            InspectionOrchestrator orchestrator, ILogger logger)
        {
            _halcon = halcon;
            _repository = repository;
            _orchestrator = orchestrator;
            _logger = logger.ForContext<VisionInterfaceProvider>();
        }

        public List<VisionPortDescriptor> QueryPorts(string? procedureFile, string procedureName)
        {
            var ports = new List<VisionPortDescriptor>();

            if (string.IsNullOrWhiteSpace(procedureName))
                return ports;

            try
            {
                // 组装一个最小配方，复用 HalconInspectionService 的引擎缓存与接口查询能力
                var cfg = _orchestrator.Configuration ?? new ProductConfiguration();
                var recipe = new InspectionRecipe
                {
                    ProcedureName = procedureName,
                    ProcedureFile = procedureFile ?? string.Empty
                };

                var file = _repository.ResolveRecipeFile(cfg, recipe);
                if (string.IsNullOrWhiteSpace(file))
                {
                    _logger.Warning("查询过程接口失败：未找到方案文件（过程 {Procedure}）", procedureName);
                    return ports;
                }

                var engine = _halcon.GetEngine(file, procedureName);
                var iface = engine?.QueryProcedureInterface();
                if (iface == null)
                {
                    _logger.Warning("查询过程接口失败：方案里没有过程 {Procedure}", procedureName);
                    return ports;
                }

                Append(ports, iface.InputImageParams, VisionPortDirection.Input, VisionPortDataType.Image, "输入图标参数");
                Append(ports, iface.InputControlParams, VisionPortDirection.Input, VisionPortDataType.Double, "输入控制参数");
                Append(ports, iface.OutputImageParams, VisionPortDirection.Output, VisionPortDataType.Image, "输出图标参数");
                Append(ports, iface.OutputControlParams, VisionPortDirection.Output, VisionPortDataType.String, "输出控制参数");

                _logger.Information("已读取过程 {Procedure} 的接口：{Input} 个输入 / {Output} 个输出",
                    procedureName,
                    ports.Count(p => p.Direction == VisionPortDirection.Input),
                    ports.Count(p => p.Direction == VisionPortDirection.Output));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "查询过程接口异常（过程 {Procedure}）", procedureName);
            }

            return ports;
        }

        private static void Append(List<VisionPortDescriptor> target, List<ParameterInfo>? source,
            VisionPortDirection direction, VisionPortDataType dataType, string description)
        {
            if (source == null) return;

            foreach (var p in source)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;

                target.Add(new VisionPortDescriptor
                {
                    Name = p.Name,
                    Direction = direction,
                    DataType = dataType,
                    Description = description
                });
            }
        }
    }
}
