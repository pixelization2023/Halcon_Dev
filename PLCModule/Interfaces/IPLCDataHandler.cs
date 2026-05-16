using PLCModule.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLCModule.Interfaces
{
    /// <summary>
    /// PLC数据处理器接口，定义了处理PLC数据的基本方法和属性
    /// </summary>
    public interface IPLCDataHandler
    {

        /// <summary>
        /// 注册PLC信号，输入一个PLCSignal对象，包含信号的名称、地址、数据类型等信息，将信号添加到数据处理器的管理列表中，以便后续的读取和写入操作
        /// </summary>
        /// <param name="signal"></param>
        void RegisterSignal(PLCSignal signal);
        /// <summary>
        /// 获取PLC信号的当前值，输入信号名称，返回一个包含信号名称和当前值的对象，支持异步操作和取消功能
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="signalName"></param>
        /// <returns></returns>
        T GetCachedValue<T>(string signalName);
        /// <summary>
        /// 注册一个持续写入回调函数，输入信号名称和一个返回要写入值的函数，当数据处理器需要写入该信号时，将调用该函数获取要写入的值，并将其写入PLC中，以实现持续更新PLC信号的功能
        /// </summary>
        /// <param name="signalName"></param>
        /// <param name="valueProvider"></param>
        void RegisterWriteCallback(string signalName, Func<object> valueProvider); // 持续写入回调
    }
}
