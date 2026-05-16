using PLCModule.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLCModule.Interfaces
{
    /// <summary>
    /// PLC通信器接口，定义了与PLC进行通信的基本方法和属性
    /// </summary>
    public interface IPLCCommunicator : IDisposable
    {
        /// <summary>
        /// 连接PLC，输入PLC配置参数，返回连接结果，支持异步操作和取消功能
        /// </summary>
        /// <param name="config">Plc配置文件</param>
        /// <param name="cancellationToken">令牌</param>
        /// <returns></returns>
        
        Task<bool> ConnectAsync(PLCConfig config,CancellationToken cancellationToken);

        /// <summary>
        /// 断开与PLC的连接，释放资源，支持异步操作
        /// </summary>
        /// <returns></returns>
        Task DisConnectAsync();

        /// <summary>
        /// 检查与PLC的连接状态，返回一个布尔值表示是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 批量读取PLC信号值，输入信号名称数组，返回一个包含信号名称和读取结果的字典
        /// </summary>
        /// <param name="signalNames">地址</param>
        /// <param name="ct">令牌</param>
        /// <returns></returns>
        Task<Dictionary<string,PLCReadResult>> ReadBatchAsync(string[] signalNames, CancellationToken ct);

        /// <summary>
        /// 批量写入PLC信号值，输入一个包含信号名称和要写入的值的字典，返回一个布尔值表示写入是否成功
        /// </summary>
        /// <param name="signals"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> WriteBatchAsync(Dictionary<string, object> signals, CancellationToken ct);

        /// <summary>
        /// 读取单个PLC信号值，输入信号名称，返回一个包含读取结果的对象，支持异步操作和取消功能
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="signalName"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<T> ReadAsync<T>(string signalName, CancellationToken ct);

        /// <summary>
        /// 按指定类型读取（类型会下推到协议层：
        /// Float/Int32/Double 会按 2/4 个寄存器读取，Bool 在 Modbus 下读线圈）。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="signalName">PLC 地址</param>
        /// <param name="ct">令牌</param>
        Task<T> ReadTypedAsync<T>(string signalName, CancellationToken ct);

        /// <summary>
        /// 按指定类型读取，并指定个数/长度。
        /// 个数语义按类型而定：Bool/Int16/UInt16 为元素个数（各占 1 个寄存器），
        /// Int32/UInt32/Float 元素各占 2 个寄存器，Double/Int64 各占 4 个，String 为字节长度。
        /// </summary>
        Task<T> ReadTypedAsync<T>(string signalName, int count, CancellationToken ct);

        /// <summary>一次读取一组同类型值（数组）</summary>
        Task<T[]> ReadArrayAsync<T>(string signalName, int count, CancellationToken ct);

        /// <summary>按指定类型写入单个值（位写入在从站无线圈区时自动退化为写寄存器 0/1）</summary>
        Task<bool> WriteTypedAsync<T>(string signalName, T value, CancellationToken ct);

        /// <summary>一次写入一组同类型值（数组），用于连续寄存器整段写（配方/条码等）</summary>
        Task<bool> WriteArrayAsync<T>(string signalName, T[] values, CancellationToken ct);

        /// <summary>
        /// 写入单个PLC信号值，输入信号名称和要写入的值，返回一个布尔值表示写入是否成功，支持异步操作和取消功能
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="signalName"></param>
        /// <param name="value"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> WriteAsync<T>(string signalName, T value, CancellationToken ct);

        /// <summary>
        /// 订阅PLC信号变化事件，输入一个包含信号名称和回调函数的字典，当指定的PLC信号发生变化时，回调函数将被调用.
        /// </summary>
        event EventHandler<PLCSignalChangedEventArgs> SignalChanged;

        /// <summary>
        /// 订阅PLC连接丢失事件，当与PLC的连接意外断开时，事件将被触发，通知订阅者连接已丢失.
        /// </summary>
        event EventHandler ConnectionLost;

    }
}
