using System.Net;

namespace PLCModule.Models
{
    /// <summary>PLC协议类型</summary>
    public enum PLCProtocolType
    {
        SiemensS7,      // 西门子 S7-1200/1500
        MitsubishiMC,   // 三菱 MC Protocol
        ModbusTcp,      // Modbus TCP（通用）
        OmronFins       // 欧姆龙 FINS
    }

    /// <summary>PLC连接配置</summary>
    public class PLCConfig
    {
        /// <summary>PLC IP地址</summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>端口号（S7默认102，Modbus默认502，MC默认6000）</summary>
        public int Port { get; set; } = 502;

        /// <summary>通信协议</summary>
        public PLCProtocolType Protocol { get; set; } = PLCProtocolType.ModbusTcp;

        /// <summary>连接超时(ms)</summary>
        public int ConnectTimeoutMs { get; set; } = 3000;

        /// <summary>读写超时(ms)</summary>
        public int ReadWriteTimeoutMs { get; set; } = 2000;

        /// <summary>重连间隔(ms)</summary>
        public int ReconnectIntervalMs { get; set; } = 5000;

        /// <summary>最大重连次数（0=无限）</summary>
        public int MaxReconnectAttempts { get; set; } = 0;

        // ---- 西门子S7专用 ----
        public byte Rack { get; set; } = 0;
        public byte Slot { get; set; } = 1;

        // ---- 三菱MC专用 ----
        public bool IsBinary { get; set; } = true;

        // ---- Modbus专用 ----
        public byte StationId { get; set; } = 1;
        public bool IsLittleEndian { get; set; } = true;

        public override string ToString() => $"{Protocol}://{IpAddress}:{Port}";
    }
}
