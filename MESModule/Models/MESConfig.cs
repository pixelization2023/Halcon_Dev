namespace MESModule.Models
{
    /// <summary>MES连接配置</summary>
    public class MESConfig
    {
        /// <summary>MES服务器地址（如 http://10.0.0.50:8080/api）</summary>
        public string BaseUrl { get; set; } = "http://localhost:5000/api";

        /// <summary>API认证Token</summary>
        public string? ApiKey { get; set; }

        /// <summary>连接超时(秒)</summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>最大重试次数</summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>重试间隔(毫秒)</summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>离线队列最大容量</summary>
        public int OfflineQueueCapacity { get; set; } = 1000;

        /// <summary>产线编号</summary>
        public string LineId { get; set; } = "LINE-01";

        /// <summary>工站编号</summary>
        public string StationId { get; set; } = "CCD-01";
    }
}
