using MESModule.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MESModule.Interfaces
{
    /// <summary>
    /// MES连接器接口，定义了与MES系统进行通信的基本方法和属性
    /// </summary>
    public interface IMESConnector
    {

        /// <summary>
        /// 连接MES系统
        /// </summary>
        /// <param name="config"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> ConnectAsync(MESConfig config, CancellationToken ct);

        /// <summary>
        /// 断开MES连接
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        ///连接状态
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 获取工单信息
        /// </summary>
        /// <param name="barcode"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        // 工单
        Task<WorkOrderInfo?> GetWorkOrderAsync(string barcode, CancellationToken ct);

        /// <summary>
        /// 上传检测结果
        /// </summary>
        /// <param name="result"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> UploadInspectionResultAsync(InspectionResult result, CancellationToken ct);

        /// <summary>
        /// 获取产品信息
        /// </summary>
        /// <param name="serialNumber"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<ProductInfo?> GetProductInfoAsync(string serialNumber, CancellationToken ct);

        /// <summary>
        /// 更新产品状态
        /// </summary>
        /// <param name="serialNumber"></param>
        /// <param name="status"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> UpdateProductStatusAsync(string serialNumber, string status, CancellationToken ct);

        /// <summary>
        /// 健康检查，验证与MES系统的连接是否正常，返回一个布尔值表示连接是否健康，支持异步操作和取消功能
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<bool> HealthCheckAsync(CancellationToken ct);
    }
}
