using MESModule.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MESModule.Interfaces
{
    /// <summary>
    /// 产品数据服务接口，定义了与产品数据相关的基本方法和属性
    /// </summary>
    public interface IProductDataService
    {


        /// <summary>
        /// 获取本地缓存的产品信息，输入产品条码，返回一个包含产品信息的对象，如果本地没有缓存该产品信息，则返回null，以便后续从服务器获取并缓存该信息
        /// </summary>
        /// <param name="barcode"></param>
        /// <returns></returns>
        ProductInfo? GetLocalProductInfo(string barcode);

        /// <summary>
        /// 缓存产品信息，输入一个包含产品信息的对象，将其存储在本地缓存中，以便后续快速访问和使用，支持更新已有的产品信息
        /// </summary>
        /// <param name="info"></param>
        void CacheProductInfo(ProductInfo info);

        /// <summary>
        /// 将检测结果加入待上传队列，输入一个包含检测结果的对象，将其添加到一个待上传的队列中，以便后续批量上传到服务器进行数据分析和记录，支持异步操作和错误处理
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        Task EnqueueResultAsync(InspectionResult result);
        /// <summary>
        /// 获取待上传队列的当前数量，返回一个整数表示队列中待上传的检测结果数量，以便监控和管理待上传的数据量，支持异步操作
        /// </summary>
        event EventHandler<int> QueueCountChanged;
    }
}
