using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HalconDotNet;

namespace Halcon.Core
{
    /// <summary>
    /// Halcon脚本引擎服务接口
    /// </summary>
    public interface IHalconEngine
    {

        /// <summary>
        /// 初始化引擎，设置脚本搜索路径（可选）
        /// </summary>
        /// <param name="procedurePath">脚本文件夹路径（如：AppDomain.CurrentDomain.BaseDirectory + "Scripts"）</param>
        void Initialize();


        /// <summary>
        /// 加载Halcon程序（.hdev文件），返回是否成功
        /// </summary>
        /// <param name="programPath"></param>
        /// <returns></returns>
        public bool LoadProgram(string programPath);

        /// <summary>
        /// 执行Halcon脚本文件，输入图像并返回输出图像
        /// </summary>
        /// <param name="scriptPath">脚本文件路径（.hdev）</param>
        /// <param name="inputImage">输入图像（需为有效HObject）</param>
        /// <param name="outputImage">输出图像（由调用者负责释放）</param>
        /// <returns>是否执行成功</returns>
        // 执行程序并处理图像
        public bool ProcessImage(HImage inputImage, out HObject resultRegion, out HTuple resultData);
    }
}
