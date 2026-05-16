using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Halcon.Core
{
    /// <summary>
    /// 过程接口类，包含过程接口名称、输入输出参数列表等信息
    /// </summary>
    public class ProcedureInterface
    {
        /// <summary>
        /// 过程接口名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 输入图像类参数
        /// </summary>
        public List<ParameterInfo> InputImageParams { get; set; } = new();
        /// <summary>
        /// 输出图像类参数
        /// </summary>
        public List<ParameterInfo> OutputImageParams { get; set; } = new();
        /// <summary>
        /// 输入控制参数列表
        /// </summary>
        public List<ParameterInfo> InputControlParams { get; set; } = new();
        /// <summary>
        /// 输出控制参数列表
        /// </summary>
        public List<ParameterInfo> OutputControlParams { get; set; } = new();

    }
}
