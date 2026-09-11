using MVS.Core;

namespace WorkBench.Models
{
    /// <summary>
    /// 步骤端口（输入 / 输出）。
    ///
    /// 背景：旧的工作台步骤只有一个「程序路径 + 超时 + 相机名」，
    /// 算法参数与结果既配不了也看不到（见 Docs/多窗口显示与检测流程方案.md 第 7 节 W-2/W-3/W-5/W-6）。
    /// 这里把每个步骤升级为"带端口的节点"：
    /// <list type="bullet">
    /// <item><b>输入端口</b>：对应 Halcon 过程的输入参数（Image / MinGray / ...），值可填字面量，
    /// 也可以写成 <c>{步骤序号}.{端口名}</c> 引用前面步骤的输出；</item>
    /// <item><b>输出端口</b>：对应过程的输出参数（out0 / box0 / image0 / code ...），
    /// 既用于界面展示，也用于后面步骤的引用。</item>
    /// </list>
    /// 端口可以由「从过程接口导入」一键生成，不必手打。
    /// </summary>
    public class StepPort : Prism.Mvvm.BindableBase
    {
        private VisionPortDirection _direction = VisionPortDirection.Input;
        public VisionPortDirection Direction
        {
            get => _direction;
            set => SetProperty(ref _direction, value);
        }

        private string _name = string.Empty;
        /// <summary>端口名（与 Halcon 过程的参数名一致，如 "Image" / "MinGray" / "out0"）</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private VisionPortDataType _dataType = VisionPortDataType.String;
        public VisionPortDataType DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }

        private string _value = string.Empty;
        /// <summary>
        /// 输入端口的取值 / 输出端口的最近结果。
        /// 输入支持三种写法：字面量（<c>128</c> / <c>model.shm</c>）、
        /// 引用前面步骤（<c>1.out0</c>）、引用上下文（<c>context:LaserCode</c>）。
        /// </summary>
        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        private string _description = string.Empty;
        /// <summary>说明（导入时自动填"输入控制参数"之类的来源信息）</summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public override string ToString() => $"{Name} ({DataType}) = {Value}";
    }
}
