namespace PLCModule.Models
{
    /// <summary>PLC信号变化事件参数</summary>
    public class PLCSignalChangedEventArgs : EventArgs
    {
        /// <summary>信号名称</summary>
        public string SignalName { get; init; } = "";

        /// <summary>信号地址</summary>
        public string Address { get; init; } = "";

        /// <summary>旧值</summary>
        public object? OldValue { get; init; }

        /// <summary>新值</summary>
        public object? NewValue { get; init; }

        /// <summary>变化时间</summary>
        public DateTime Timestamp { get; init; } = DateTime.Now;

        public override string ToString()
            => $"{SignalName}: {OldValue} → {NewValue} @ {Timestamp:HH:mm:ss.fff}";
    }
}
