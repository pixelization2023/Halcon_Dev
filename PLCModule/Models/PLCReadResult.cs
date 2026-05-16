using System.Text;

namespace PLCModule.Models
{
    /// <summary>PLC读取结果</summary>
    public class PLCReadResult
    {
        /// <summary>是否读取成功</summary>
        public bool Success { get; set; }

        /// <summary>读取到的值</summary>
        public object? Value { get; set; }

        /// <summary>错误信息（Success=false时）</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>读取耗时(ms)</summary>
        public long ElapsedMs { get; set; }

        /// <summary>信号名称</summary>
        public string SignalName { get; set; } = "";

        // 快速创建
        public static PLCReadResult Ok(string name, object? value, long elapsedMs = 0)
            => new() { Success = true, SignalName = name, Value = value, ElapsedMs = elapsedMs };

        public static PLCReadResult Fail(string name, string error)
            => new() { Success = false, SignalName = name, ErrorMessage = error };

        //public T? GetValue<T>()
        //{
        //    if (!Success || Value == null) return default;
        //    if (Value is T typed) return typed;
        //    try { return (T?)Convert.ChangeType(Value, typeof(T)); }
        //    catch { return default; }
        //}

        public T? GetValue<T>(bool swapBytes = false)
        {
            if (!Success || Value == null) return default;

            if (Value is T typed) return typed;

            if (Value is byte[] bytes)
            {
                var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

                // 字节序转换
                byte[] data = bytes;
                if (swapBytes && BitConverter.IsLittleEndian)
                {
                    data = bytes.ToArray();
                    int size = targetType switch
                    {
                        _ when targetType == typeof(short) || targetType == typeof(ushort) => 2,
                        _ when targetType == typeof(int) || targetType == typeof(uint) || targetType == typeof(float) => 4,
                        _ when targetType == typeof(long) || targetType == typeof(ulong) || targetType == typeof(double) => 8,
                        _ => 0
                    };
                    if (size > 0 && bytes.Length >= size)
                        Array.Reverse(data, 0, size);
                }

                // 转换
                return targetType switch
                {
                    _ when targetType == typeof(bool) => (T)(object)(data[0] != 0),
                    _ when targetType == typeof(byte) => (T)(object)data[0],
                    _ when targetType == typeof(sbyte) => (T)(object)(sbyte)data[0],
                    _ when targetType == typeof(short) => (T)(object)BitConverter.ToInt16(data, 0),
                    _ when targetType == typeof(ushort) => (T)(object)BitConverter.ToUInt16(data, 0),
                    _ when targetType == typeof(int) => (T)(object)BitConverter.ToInt32(data, 0),
                    _ when targetType == typeof(uint) => (T)(object)BitConverter.ToUInt32(data, 0),
                    _ when targetType == typeof(float) => (T)(object)BitConverter.ToSingle(data, 0),
                    _ when targetType == typeof(long) => (T)(object)BitConverter.ToInt64(data, 0),
                    _ when targetType == typeof(ulong) => (T)(object)BitConverter.ToUInt64(data, 0),
                    _ when targetType == typeof(double) => (T)(object)BitConverter.ToDouble(data, 0),
                    _ when targetType == typeof(string) => (T)(object)Encoding.ASCII.GetString(data).TrimEnd('\0'),
                    _ when targetType == typeof(byte[]) => (T)(object)data,
                    _ => default
                };
            }

            try
            {
                return (T?)Convert.ChangeType(Value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        public override string ToString()
            => Success ? $"[{SignalName}] = {Value}" : $"[{SignalName}] ✗ {ErrorMessage}";
    }
}
