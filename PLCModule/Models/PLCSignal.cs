namespace PLCModule.Models
{
    /// <summary>PLC信号数据类型</summary>
    public enum PLCDataType
    {
        Bool,
        Byte,
        Short,
        UShort,
        Int,
        UInt,
        Float,
        Double,
        String,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Float32
    }

    /// <summary>PLC信号定义</summary>
    public class PLCSignal
    {
        /// <summary>信号名称（唯一标识）</summary>
        public string Name { get; set; } = "";

        /// <summary>PLC地址（如 DB1.DBD0 / D100 / 40001）</summary>
        public string Address { get; set; } = "";

        /// <summary>数据类型</summary>
        public PLCDataType DataType { get; set; } = PLCDataType.Bool;

        /// <summary>
        /// 读取/写入个数（0 = 按类型自动）。
        /// Bool/Int16/UInt16 每个占 1 个寄存器，Int32/UInt32/Float 占 2 个，Double/Int64 占 4 个；
        /// String 按字节长度解释。
        /// </summary>
        public int Count { get; set; }

        /// <summary>读/写权限</summary>
        public bool IsReadOnly { get; set; } = true;

        /// <summary>描述</summary>
        public string? Description { get; set; }

        /// <summary>上次读取的值（缓存）</summary>
        public object? CachedValue { get; set; }

        /// <summary>上次读取时间</summary>
        public DateTime LastReadTime { get; set; }

        /// <summary>值变化回调（可选）</summary>
        public Action<object?, object?>? OnValueChanged { get; set; }

        public override string ToString() => $"{Name} [{Address}] ({DataType})";

        /// <summary>获取泛型缓存值</summary>
        public T? GetCachedValue<T>()
        {
            if (CachedValue is T typed) return typed;
            try { return (T?)Convert.ChangeType(CachedValue, typeof(T)); }
            catch { return default; }
        }

        /// <summary>更新缓存值，触发变化回调</summary>
        public void UpdateCachedValue(object? newValue)
        {
            var old = CachedValue;
            if (!Equals(old, newValue))
            {
                CachedValue = newValue;
                LastReadTime = DateTime.Now;
                OnValueChanged?.Invoke(old, newValue);
            }
        }
    }
}
