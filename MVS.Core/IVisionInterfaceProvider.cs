namespace MVS.Core
{
    /// <summary>端口方向（视觉过程接口的输入/输出）</summary>
    public enum VisionPortDirection
    {
        Input = 0,
        Output = 1
    }

    /// <summary>端口数据类型</summary>
    public enum VisionPortDataType
    {
        Image = 0,
        Int = 1,
        Double = 2,
        String = 3,
        Bool = 4,
        Region = 5
    }

    /// <summary>从视觉方案（Halcon 过程）读出来的一个端口描述</summary>
    public class VisionPortDescriptor
    {
        /// <summary>参数名（与过程接口一致）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>端口方向</summary>
        public VisionPortDirection Direction { get; set; } = VisionPortDirection.Input;

        /// <summary>数据类型</summary>
        public VisionPortDataType DataType { get; set; } = VisionPortDataType.String;

        /// <summary>来源说明（例如"输入图标参数"），界面上作为提示</summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 视觉方案接口查询能力。
    ///
    /// 为什么把契约放在 MVS.Core：
    /// 需求是「工作台里能一键读出 Halcon 过程的输入/输出端口」，实现体在 Inspection 模块
    /// （只有它持有 HalconInspectionService），而消费方是 WorkBench 模块。
    /// 如果契约放在 WorkBench，就要求 Inspection 引用 WorkBench；放在 Inspection 则要
    /// WorkBench 引用 Inspection —— 两种都会让本已存在的单向依赖变成双向。
    /// 放在双方都已经引用的共享内核 MVS.Core 里，依赖关系保持单向、也不需要改任何工程引用。
    /// 这与 <c>ThemePalette</c> 的做法一致（外壳与各模块通过共享内核交换能力）。
    /// </summary>
    public interface IVisionInterfaceProvider
    {
        /// <summary>
        /// 查询某个 Halcon 过程声明的输入/输出端口。
        /// </summary>
        /// <param name="procedureFile">方案文件名（可空：用当前产品的方案文件）</param>
        /// <param name="procedureName">过程名</param>
        /// <returns>端口清单；查询不到时返回空列表（不抛异常）</returns>
        List<VisionPortDescriptor> QueryPorts(string? procedureFile, string procedureName);
    }
}
