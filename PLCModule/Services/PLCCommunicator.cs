using HslCommunication;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Siemens;
using PLCModule.Interfaces;
using PLCModule.Models;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace PLCModule.Services
{
    /// <summary>
    /// PLC通信器 — 基于 HslCommunication 的多协议PLC通信实现
    /// 支持: 西门子S7 / 三菱MC / Modbus TCP
    /// </summary>
    public class PLCCommunicator : IPLCCommunicator
    {
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _commLock = new(1, 1);
        private readonly ConcurrentDictionary<string, object> _signalCache = new();
        private readonly ConcurrentDictionary<string, SignalInfo> _signalDefinitions = new();

        private object? _device;
        private PLCConfig? _config;
        private int _connectionState;    // 0=断开 1=连接中 2=已连接
        private CancellationTokenSource? _reconnectCts;

        public event EventHandler<PLCSignalChangedEventArgs>? SignalChanged;
        public event EventHandler? ConnectionLost;

        public PLCCommunicator(ILogger logger)
        {
            _logger = logger.ForContext<PLCCommunicator>();
        }

        public bool IsConnected => Volatile.Read(ref _connectionState) == 2;

        /// <summary>
        /// 注册信号定义（可选，用于类型感知的读取）
        /// </summary>
        public void RegisterSignal(string name, SignalInfo signalInfo)
        {
            _signalDefinitions[name] = signalInfo;
        }

        // ==================== 连接/断开 ====================

        public async Task<bool> ConnectAsync(PLCConfig config, CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _connectionState, 1, 0) != 0)
            {
                _logger.Warning("PLC已经在连接中或已连接");
                return IsConnected;
            }

            _config = config;

            try
            {
                _device = CreateDevice(config);
                OperateResult? result = await Task.Run(() =>
                {
                    return _device switch
                    {
                        SiemensS7Net s7 => s7.ConnectServer(),
                        MelsecMcNet mc => mc.ConnectServer(),
                        ModbusTcpNet modbus => modbus.ConnectServer(),
                        _ => new OperateResult("不支持的协议类型")
                    };
                }, ct);

                if (result?.IsSuccess == true)
                {
                    Volatile.Write(ref _connectionState, 2);
                    _logger.Information("PLC连接成功: {Config}", config);
                    return true;
                }
                else
                {
                    Volatile.Write(ref _connectionState, 0);
                    _logger.Error("PLC连接失败: {Error}", result?.Message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _connectionState, 0);
                _logger.Error(ex, "PLC连接异常");
                return false;
            }
        }

        public async Task DisConnectAsync()
        {
            Volatile.Write(ref _connectionState, 0);
            CancelReconnect();

            if (_device != null)
            {
                await Task.Run(() =>
                {
                    (_device as SiemensS7Net)?.ConnectClose();
                    (_device as MelsecMcNet)?.ConnectClose();
                    (_device as ModbusTcpNet)?.ConnectClose();
                });
                _logger.Information("PLC已断开");
            }
            _device = null;
        }

        // ==================== 读取 ====================

        public async Task<T> ReadAsync<T>(string signalName, CancellationToken ct)
        {
            var result = await ReadBatchAsync([signalName], ct);
            if (result.TryGetValue(signalName, out var r) && r.Success)
            {
                var converted = ConvertRaw(r.Value, typeof(T));
                if (converted is T typedValue)
                    return typedValue;

                throw new InvalidOperationException($"无法将 {r.Value?.GetType().Name ?? "null"} 转换为 {typeof(T).Name}");
            }

            throw new InvalidOperationException($"读取信号 '{signalName}' 失败: {result.GetValueOrDefault(signalName)?.ErrorMessage}");
        }

        /// <summary>
        /// 按指定类型读取。
        ///
        /// 与 <see cref="ReadAsync{T}"/> 的区别：这里会把类型**下推到协议层**，
        /// 于是 Float / Int32 / Double 会正确地按 2（或 4）个寄存器读取，
        /// 而不是只读 1 个寄存器再硬转类型。
        /// </summary>
        public Task<T> ReadTypedAsync<T>(string signalName, CancellationToken ct)
            => ReadTypedAsync<T>(signalName, 0, ct);

        public async Task<T> ReadTypedAsync<T>(string signalName, int count, CancellationToken ct)
        {
            _signalDefinitions[signalName] = new SignalInfo
            {
                Address = signalName,
                DataType = typeof(T),
                Count = count
            };

            try
            {
                try
                {
                    return await ReadAsync<T>(signalName, ct);
                }
                catch (Exception) when (typeof(T) == typeof(bool))
                {
                    // 从站可能只有保持寄存器（不支持线圈功能码 1）。
                    // 此时退回"读寄存器，非零即真"，让 M 点位在两种从站上都能读到值。
                    _logger.Debug("线圈读取失败，回退为寄存器非零判断: {Address}", signalName);
                    _signalDefinitions.TryRemove(signalName, out _);
                    return await ReadAsync<T>(signalName, ct);
                }
            }
            finally
            {
                _signalDefinitions.TryRemove(signalName, out _);
            }
        }

        /// <summary>
        /// 按指定类型写入单个值（位写入支持"线圈→寄存器"回退）。
        /// </summary>
        public async Task<bool> WriteTypedAsync<T>(string signalName, T value, CancellationToken ct)
        {
            if (!IsConnected || _device == null)
                throw new InvalidOperationException("PLC未连接");

            await _commLock.WaitAsync(ct);
            try
            {
                var op = await Task.Run(() => WriteByAddress(_device, signalName, value!), ct);
                var ok = op?.IsSuccess == true;
                if (!ok)
                    _logger.Warning("PLC 写入失败: {Address} = {Value} ({Message})", signalName, value, op?.Message);
                return ok;
            }
            finally
            {
                _commLock.Release();
            }
        }

        /// <summary>
        /// 一次写入一组同类型值（数组）：连续寄存器整段写入（例如配方/条码）。
        /// </summary>
        public async Task<bool> WriteArrayAsync<T>(string signalName, T[] values, CancellationToken ct)
        {
            if (!IsConnected || _device == null)
                throw new InvalidOperationException("PLC未连接");

            if (values == null || values.Length == 0)
                throw new ArgumentException("写入数组不能为空", nameof(values));

            await _commLock.WaitAsync(ct);
            try
            {
                var op = await Task.Run(() => WriteArrayByAddress(_device, signalName, values), ct);
                var ok = op?.IsSuccess == true;
                if (!ok)
                    _logger.Warning("PLC 数组写入失败: {Address} × {Count} ({Message})", signalName, values.Length, op?.Message);
                else
                    _logger.Information("PLC 数组写入成功: {Address} × {Count}", signalName, values.Length);
                return ok;
            }
            finally
            {
                _commLock.Release();
            }
        }

        /// <summary>
        /// 一次读取一组同类型值（数组）。
        /// <paramref name="count"/> 为元素个数（Bool/Int16/UInt16 每个占 1 个寄存器，
        /// Int32/UInt32/Float 占 2 个，Double/Int64 占 4 个）。
        /// </summary>
        public async Task<T[]> ReadArrayAsync<T>(string signalName, int count, CancellationToken ct)
        {
            if (!IsConnected || _device == null)
                throw new InvalidOperationException("PLC未连接");

            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "读取个数必须大于 0");

            await _commLock.WaitAsync(ct);
            try
            {
                OperateResult? op;
                try
                {
                    op = await Task.Run(() => ReadTypedArrayByAddress(_device, signalName, typeof(T), count), ct);
                }
                catch (Exception) when (typeof(T) == typeof(bool))
                {
                    op = null;
                }

                // Bool 数组：从站可能不支持线圈（功能码 1），退回"读 count 个寄存器，非零即真"
                if ((op == null || op.IsSuccess != true) && typeof(T) == typeof(bool))
                {
                    _logger.Debug("线圈数组读取失败，回退为寄存器非零判断: {Address} × {Count}", signalName, count);
                    var rawOp = await Task.Run(() => ReadTypedArrayByAddress(_device, signalName, typeof(ushort), count), ct);
                    if (rawOp?.IsSuccess != true)
                        throw new InvalidOperationException($"读取数组失败: {rawOp?.Message ?? op?.Message ?? "未知错误"}");

                    var ushorts = ExtractContent(rawOp) as ushort[];
                    if (ushorts != null)
                        return ushorts.Select(u => (T)(object)(u != 0)).ToArray();
                }

                if (op?.IsSuccess != true)
                    throw new InvalidOperationException($"读取数组失败: {op?.Message ?? "未知错误"}");

                var content = ExtractContent(op);

                if (content is T[] typed) return typed;

                if (content is Array raw)
                    return raw.Cast<object?>()
                              .Select(v => (T)Convert.ChangeType(v!, typeof(T))!)
                              .ToArray();

                throw new InvalidOperationException($"返回值无法转换为 {typeof(T).Name}[]");
            }
            finally
            {
                _commLock.Release();
            }
        }

        /// <summary>
        /// 把 PLC 返回的原始值转换成调用方要的类型。
        ///
        /// 关键点：HslCommunication 的 <c>Read(address, length)</c> 返回的是 <c>byte[]</c>，
        /// 直接 ToString() 会得到 "System.Byte[]"（这正是监控页显示异常的原因），
        /// 而 <c>Convert.ChangeType(byte[], bool)</c> 这种转换会抛异常 ——
        /// 会让上层（PLC 上升沿触发）静默读不到值。所以这里统一做类型化转换。
        /// </summary>
        internal static object? ConvertRaw(object? raw, Type targetType)
        {
            if (raw == null) return null;

            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (t == typeof(object) || t.IsInstanceOfType(raw)) return raw;

            if (raw is byte[] bytes)
                return ConvertBytes(bytes, t);

            // 数值/字符串之间：先用 ChangeType，失败再按"非零即真"处理 bool
            try
            {
                return Convert.ChangeType(raw, t);
            }
            catch
            {
                if (t == typeof(bool))
                {
                    try { return Convert.ToDouble(raw) != 0d; }
                    catch { return null; }
                }
                return null;
            }
        }

        /// <summary>字节数组 → 目标类型（Modbus/S7/MC 寄存器均为大端）</summary>
        private static object? ConvertBytes(byte[] bytes, Type t)
        {
            if (t == typeof(byte[])) return bytes;
            if (t == typeof(bool)) return bytes.Any(b => b != 0);
            if (t == typeof(string)) return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            if (t == typeof(byte)) return bytes.Length > 0 ? bytes[0] : null;
            if (t == typeof(sbyte)) return bytes.Length > 0 ? (sbyte)bytes[0] : null;
            if (bytes.Length == 0) return null;

            var data = bytes.ToArray();
            if (BitConverter.IsLittleEndian) Array.Reverse(data);

            try
            {
                if (t == typeof(short) && data.Length >= 2) return BitConverter.ToInt16(data, 0);
                if (t == typeof(ushort) && data.Length >= 2) return BitConverter.ToUInt16(data, 0);
                if (t == typeof(int) && data.Length >= 4) return BitConverter.ToInt32(data, 0);
                if (t == typeof(uint) && data.Length >= 4) return BitConverter.ToUInt32(data, 0);
                if (t == typeof(float) && data.Length >= 4) return BitConverter.ToSingle(data, 0);
                if (t == typeof(long) && data.Length >= 8) return BitConverter.ToInt64(data, 0);
                if (t == typeof(ulong) && data.Length >= 8) return BitConverter.ToUInt64(data, 0);
                if (t == typeof(double) && data.Length >= 8) return BitConverter.ToDouble(data, 0);

                // 位/字混用：整数字长不够时，退化成能表示的最小整数
                if (t == typeof(short) || t == typeof(ushort)) return data[0];
            }
            catch
            {
                // 落到下面返回 null，由调用方报"无法转换"
            }

            return null;
        }

        public async Task<Dictionary<string, PLCReadResult>> ReadBatchAsync(string[] signalNames, CancellationToken ct)
        {
            var results = new Dictionary<string, PLCReadResult>();

            if (!IsConnected || _device == null)
            {
                foreach (var name in signalNames)
                    results[name] = PLCReadResult.Fail(name, "PLC未连接");
                return results;
            }

            await _commLock.WaitAsync(ct);
            try
            {
                var sw = Stopwatch.StartNew();

                foreach (var name in signalNames)
                {
                    ct.ThrowIfCancellationRequested();

                    // 根据是否注册了信号定义选择读取方式
                    OperateResult? opResult = _signalDefinitions.TryGetValue(name, out var signalInfo)
                        ? await Task.Run(() => ReadTypedByAddress(_device, name, signalInfo.DataType,
                            signalInfo.EffectiveCount(signalInfo.DataType)), ct)
                        : await Task.Run(() => ReadByAddress(_device, name), ct);

                    if (opResult?.IsSuccess == true)
                    {
                        var val = ExtractValue(opResult);
                        if (val != null)
                        {
                            _signalCache[name] = val;
                            results[name] = PLCReadResult.Ok(name, val, sw.ElapsedMilliseconds);
                        }
                        else
                        {
                            results[name] = PLCReadResult.Fail(name, "提取值失败");
                        }
                    }
                    else
                    {
                        results[name] = PLCReadResult.Fail(name, opResult?.Message ?? "未知错误");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var name in signalNames)
                    if (!results.ContainsKey(name))
                        results[name] = PLCReadResult.Fail(name, "操作已取消");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "PLC批量读取异常");
                foreach (var name in signalNames)
                    if (!results.ContainsKey(name))
                        results[name] = PLCReadResult.Fail(name, ex.Message);
            }
            finally
            {
                _commLock.Release();
            }

            return results;
        }

        // ==================== 写入 ====================

        public async Task<bool> WriteAsync<T>(string signalName, T value, CancellationToken ct)
        {
            return await WriteBatchAsync(new Dictionary<string, object> { { signalName, value! } }, ct);
        }

        public async Task<bool> WriteBatchAsync(Dictionary<string, object> signals, CancellationToken ct)
        {
            if (!IsConnected || _device == null)
            {
                _logger.Warning("PLC未连接，写入失败");
                return false;
            }

            await _commLock.WaitAsync(ct);
            bool allSuccess = true;

            try
            {
                foreach (var kv in signals)
                {
                    ct.ThrowIfCancellationRequested();

                    // 根据值的类型选择合适的写入方法
                    OperateResult? result = await Task.Run(() => WriteByAddress(_device, kv.Key, kv.Value), ct);

                    if (result?.IsSuccess != true)
                    {
                        _logger.Warning("PLC写入失败 [{Name}]: {Error}", kv.Key, result?.Message);
                        allSuccess = false;
                    }
                    else
                    {
                        _signalCache[kv.Key] = kv.Value;
                       // SignalChanged?.Invoke(this, new PLCSignalChangedEventArgs(kv.Key, kv.Value, DateTime.Now));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "PLC批量写入异常");
                return false;
            }
            finally
            {
                _commLock.Release();
            }

            return allSuccess;
        }

        // ==================== 设备创建 ====================

        private static object CreateDevice(PLCConfig config)
        {
            return config.Protocol switch
            {
                PLCProtocolType.SiemensS7 => CreateSiemensDevice(config),
                PLCProtocolType.MitsubishiMC => CreateMelsecDevice(config),
                PLCProtocolType.ModbusTcp => CreateModbusDevice(config),
                _ => throw new NotSupportedException($"不支持的协议: {config.Protocol}")
            };
        }

        private static SiemensS7Net CreateSiemensDevice(PLCConfig config)
        {
            return new SiemensS7Net(SiemensPLCS.S1200, config.IpAddress)
            {
                Rack = config.Rack,
                Slot = config.Slot,
                ConnectTimeOut = config.ConnectTimeoutMs,
                ReceiveTimeOut = config.ReadWriteTimeoutMs
            };
        }

        private static MelsecMcNet CreateMelsecDevice(PLCConfig config)
        {
            return new MelsecMcNet(config.IpAddress, config.Port)
            {
                ConnectTimeOut = config.ConnectTimeoutMs,
                ReceiveTimeOut = config.ReadWriteTimeoutMs
            };
        }

        private static ModbusTcpNet CreateModbusDevice(PLCConfig config)
        {
            return new ModbusTcpNet(config.IpAddress, config.Port, config.StationId)
            {
                ConnectTimeOut = config.ConnectTimeoutMs,
                ReceiveTimeOut = config.ReadWriteTimeoutMs
            };
        }

        // ==================== 读取底层（关键修复） ====================

        /// <summary>
        /// 读取指定地址的数据（返回 byte[]）
        /// </summary>
        private static OperateResult? ReadByAddress(object device, string address)
        {
            return device switch
            {
                SiemensS7Net s7 => s7.Read(address, 1),
                MelsecMcNet mc => mc.Read(address, 1),
                ModbusTcpNet modbus => modbus.Read(address, 1),
                _ => new OperateResult("无效设备")
            };
        }

        /// <summary>
        /// 类型感知的读取
        /// </summary>
        private static OperateResult? ReadTypedByAddress(object device, string address, Type dataType, int bytesOrLength)
        {
            return device switch
            {
                SiemensS7Net s7 => ReadSiemensTyped(s7, address, dataType, bytesOrLength),
                MelsecMcNet mc => ReadMelsecTyped(mc, address, dataType, bytesOrLength),
                ModbusTcpNet modbus => ReadModbusTyped(modbus, address, dataType, bytesOrLength),
                _ => new OperateResult("无效设备")
            };
        }

        /// <summary>
        /// 类型 + 字节长度 的读取。
        /// 注意：HslCommunication 的 <c>ReadInt32/ReadFloat</c> 等已经会按类型读够寄存器
        /// （Int32/Float=2 个，Double=4 个），这里的 <paramref name="bytesOrLength"/>
        /// 只在 string / byte[] 这类"按长度读"的场景使用。
        /// </summary>
        private static OperateResult? ReadSiemensTyped(SiemensS7Net s7, string address, Type dataType, int bytesOrLength)
        {
            if (dataType == typeof(bool)) return s7.ReadBool(address);
            if (dataType == typeof(byte)) return s7.ReadByte(address);
            if (dataType == typeof(short)) return s7.ReadInt16(address);
            if (dataType == typeof(ushort)) return s7.ReadUInt16(address);
            if (dataType == typeof(int)) return s7.ReadInt32(address);
            if (dataType == typeof(uint)) return s7.ReadUInt32(address);
            if (dataType == typeof(float)) return s7.ReadFloat(address);
            if (dataType == typeof(double)) return s7.ReadDouble(address);
            if (dataType == typeof(string)) return s7.ReadString(address, (ushort)Math.Max(2, bytesOrLength));
            if (dataType == typeof(byte[])) return s7.Read(address, (ushort)Math.Max(1, bytesOrLength));
            return s7.Read(address, 1);
        }

        private static OperateResult? ReadMelsecTyped(MelsecMcNet mc, string address, Type dataType, int bytesOrLength)
        {
            if (dataType == typeof(bool)) return mc.ReadBool(address);
            if (dataType == typeof(short)) return mc.ReadInt16(address);
            if (dataType == typeof(ushort)) return mc.ReadUInt16(address);
            if (dataType == typeof(int)) return mc.ReadInt32(address);
            if (dataType == typeof(uint)) return mc.ReadUInt32(address);
            if (dataType == typeof(float)) return mc.ReadFloat(address);
            if (dataType == typeof(double)) return mc.ReadDouble(address);
            if (dataType == typeof(string)) return mc.ReadString(address, (ushort)Math.Max(2, bytesOrLength));
            if (dataType == typeof(byte[])) return mc.Read(address, (ushort)Math.Max(1, bytesOrLength));
            return mc.Read(address, 1);
        }

        private static OperateResult? ReadModbusTyped(ModbusTcpNet modbus, string address, Type dataType, int bytesOrLength)
        {
            if (dataType == typeof(bool)) return modbus.ReadCoil(address);
            if (dataType == typeof(short)) return modbus.ReadInt16(address);
            if (dataType == typeof(ushort)) return modbus.ReadUInt16(address);
            if (dataType == typeof(int)) return modbus.ReadInt32(address);
            if (dataType == typeof(uint)) return modbus.ReadUInt32(address);
            if (dataType == typeof(float)) return modbus.ReadFloat(address);
            if (dataType == typeof(double)) return modbus.ReadDouble(address);
            if (dataType == typeof(string)) return modbus.ReadString(address, (ushort)Math.Max(2, bytesOrLength));
            if (dataType == typeof(byte[])) return modbus.Read(address, (ushort)Math.Max(1, bytesOrLength));
            return modbus.Read(address, 1);
        }

        // ==================== 类型化数组读取（一次读 N 个） ====================

        /// <summary>
        /// 读取一组同类型值。
        /// <paramref name="count"/> 的含义按类型而定：
        /// Bool/Int16/UInt16 → 元素个数；Int32/UInt32/Float → 元素个数（每个占 2 个寄存器）；
        /// Double/Int64 → 元素个数（每个占 4 个寄存器）；String → 字节长度。
        /// </summary>
        internal static OperateResult? ReadTypedArrayByAddress(object device, string address, Type dataType, int count)
        {
            count = Math.Max(1, count);

            return device switch
            {
                SiemensS7Net s7 => ReadSiemensArray(s7, address, dataType, count),
                MelsecMcNet mc => ReadMelsecArray(mc, address, dataType, count),
                ModbusTcpNet modbus => ReadModbusArray(modbus, address, dataType, count),
                _ => new OperateResult("无效设备")
            };
        }

        private static OperateResult? ReadSiemensArray(SiemensS7Net s7, string address, Type t, int count)
            => ReadArrayCore(t, count,
                (c) => s7.ReadBool(address, (ushort)c),
                (c) => s7.ReadInt16(address, (ushort)c),
                (c) => s7.ReadUInt16(address, (ushort)c),
                (c) => s7.ReadInt32(address, (ushort)c),
                (c) => s7.ReadUInt32(address, (ushort)c),
                (c) => s7.ReadFloat(address, (ushort)c),
                (c) => s7.ReadDouble(address, (ushort)c),
                (c) => s7.Read(address, (ushort)c));

        private static OperateResult? ReadMelsecArray(MelsecMcNet mc, string address, Type t, int count)
            => ReadArrayCore(t, count,
                (c) => mc.ReadBool(address, (ushort)c),
                (c) => mc.ReadInt16(address, (ushort)c),
                (c) => mc.ReadUInt16(address, (ushort)c),
                (c) => mc.ReadInt32(address, (ushort)c),
                (c) => mc.ReadUInt32(address, (ushort)c),
                (c) => mc.ReadFloat(address, (ushort)c),
                (c) => mc.ReadDouble(address, (ushort)c),
                (c) => mc.Read(address, (ushort)c));

        private static OperateResult? ReadModbusArray(ModbusTcpNet modbus, string address, Type t, int count)
            => ReadArrayCore(t, count,
                (c) => modbus.ReadBool(address, (ushort)c),
                (c) => modbus.ReadInt16(address, (ushort)c),
                (c) => modbus.ReadUInt16(address, (ushort)c),
                (c) => modbus.ReadInt32(address, (ushort)c),
                (c) => modbus.ReadUInt32(address, (ushort)c),
                (c) => modbus.ReadFloat(address, (ushort)c),
                (c) => modbus.ReadDouble(address, (ushort)c),
                (c) => modbus.Read(address, (ushort)c));

        /// <summary>按目标类型选择对应的"读数组"重载（HslCommunication 的每个协议都有同名重载）</summary>
        private static OperateResult? ReadArrayCore(Type t, int count,
            Func<int, OperateResult<bool[]>> readBool,
            Func<int, OperateResult<short[]>> readInt16,
            Func<int, OperateResult<ushort[]>> readUInt16,
            Func<int, OperateResult<int[]>> readInt32,
            Func<int, OperateResult<uint[]>> readUInt32,
            Func<int, OperateResult<float[]>> readFloat,
            Func<int, OperateResult<double[]>> readDouble,
            Func<int, OperateResult<byte[]>> readBytes)
        {
            if (t == typeof(bool)) return readBool(count);
            if (t == typeof(short)) return readInt16(count);
            if (t == typeof(ushort)) return readUInt16(count);
            if (t == typeof(int)) return readInt32(count);
            if (t == typeof(uint)) return readUInt32(count);
            if (t == typeof(float)) return readFloat(count);
            if (t == typeof(double)) return readDouble(count);
            // 其它类型（含 string）退化为原始字节读取，由上层自行解释
            return readBytes(count);
        }

        /// <summary>取出 OperateResult&lt;T&gt;.Content（不依赖具体泛型实参）</summary>
        internal static object? ExtractContent(OperateResult? result)
        {
            if (result == null) return null;
            return result.GetType().GetProperty("Content")?.GetValue(result);
        }

        // ==================== 写入底层 ====================

        private static OperateResult? WriteByAddress(object device, string address, object value)
        {
            // 位写入：某些从站（尤其是只配了保持寄存器的 Modbus 从站）不支持线圈区，
            // 直接写位会返回"不支持的功能码" → 写入静默失败。这里退化为写寄存器 0/1。
            if (value is bool bit)
                return WriteBoolWithFallback(device, address, bit);

            return device switch
            {
                SiemensS7Net s7 => WriteSiemensByType(s7, address, value),
                MelsecMcNet mc => WriteMelsecByType(mc, address, value),
                ModbusTcpNet modbus => WriteModbusByType(modbus, address, value),
                _ => new OperateResult("无效设备")
            };
        }

        /// <summary>写位；失败则退回"写寄存器 0/1"（保持寄存器型从站）</summary>
        private static OperateResult WriteBoolWithFallback(object device, string address, bool value)
        {
            var direct = device switch
            {
                SiemensS7Net s7 => s7.Write(address, value),
                MelsecMcNet mc => mc.Write(address, value),
                ModbusTcpNet modbus => modbus.Write(address, value),
                _ => new OperateResult("无效设备")
            };

            if (direct.IsSuccess) return direct;

            var word = (ushort)(value ? 1 : 0);
            var fallback = device switch
            {
                SiemensS7Net s7 => s7.Write(address, word),
                MelsecMcNet mc => mc.Write(address, word),
                ModbusTcpNet modbus => modbus.Write(address, word),
                _ => new OperateResult("无效设备")
            };

            return fallback;
        }

        // ==================== 类型化数组写入 ====================

        private static OperateResult WriteArrayByAddress(object device, string address, Array values)
        {
            // 位数组：同样支持"线圈 → 寄存器"回退
            if (values is bool[] bits)
            {
                var direct = device switch
                {
                    SiemensS7Net s7 => s7.Write(address, bits),
                    MelsecMcNet mc => mc.Write(address, bits),
                    ModbusTcpNet modbus => modbus.Write(address, bits),
                    _ => new OperateResult("无效设备")
                };

                if (direct.IsSuccess) return direct;

                var words = bits.Select(b => (ushort)(b ? 1 : 0)).ToArray();
                return device switch
                {
                    SiemensS7Net s7 => s7.Write(address, words),
                    MelsecMcNet mc => mc.Write(address, words),
                    ModbusTcpNet modbus => modbus.Write(address, words),
                    _ => new OperateResult("无效设备")
                };
            }

            return device switch
            {
                SiemensS7Net s7 => WriteArrayCore(values,
                    v => s7.Write(address, (short[])v), v => s7.Write(address, (ushort[])v),
                    v => s7.Write(address, (int[])v), v => s7.Write(address, (uint[])v),
                    v => s7.Write(address, (float[])v), v => s7.Write(address, (double[])v),
                    v => s7.Write(address, (byte[])v)),

                MelsecMcNet mc => WriteArrayCore(values,
                    v => mc.Write(address, (short[])v), v => mc.Write(address, (ushort[])v),
                    v => mc.Write(address, (int[])v), v => mc.Write(address, (uint[])v),
                    v => mc.Write(address, (float[])v), v => mc.Write(address, (double[])v),
                    v => mc.Write(address, (byte[])v)),

                ModbusTcpNet modbus => WriteArrayCore(values,
                    v => modbus.Write(address, (short[])v), v => modbus.Write(address, (ushort[])v),
                    v => modbus.Write(address, (int[])v), v => modbus.Write(address, (uint[])v),
                    v => modbus.Write(address, (float[])v), v => modbus.Write(address, (double[])v),
                    v => modbus.Write(address, (byte[])v)),

                _ => new OperateResult("无效设备")
            };
        }

        private static OperateResult WriteArrayCore(Array values,
            Func<Array, OperateResult> writeInt16, Func<Array, OperateResult> writeUInt16,
            Func<Array, OperateResult> writeInt32, Func<Array, OperateResult> writeUInt32,
            Func<Array, OperateResult> writeFloat, Func<Array, OperateResult> writeDouble,
            Func<Array, OperateResult> writeBytes)
        {
            return values switch
            {
                short[] => writeInt16(values),
                ushort[] => writeUInt16(values),
                int[] => writeInt32(values),
                uint[] => writeUInt32(values),
                float[] => writeFloat(values),
                double[] => writeDouble(values),
                byte[] => writeBytes(values),
                _ => new OperateResult($"不支持的数组类型: {values.GetType()}")
            };
        }

        private static OperateResult WriteSiemensByType(SiemensS7Net s7, string address, object value)
        {
            return value switch
            {
                bool b => s7.Write(address, b),
                byte b => s7.Write(address, b),
                short s => s7.Write(address, s),
                ushort us => s7.Write(address, us),
                int i => s7.Write(address, i),
                uint ui => s7.Write(address, ui),
                float f => s7.Write(address, f),
                double d => s7.Write(address, d),
                string str => s7.Write(address, str),
                byte[] bytes => s7.Write(address, bytes),
                _ => new OperateResult($"不支持的值类型: {value?.GetType()}")
            };
        }

        private static OperateResult WriteMelsecByType(MelsecMcNet mc, string address, object value)
        {
            return value switch
            {
                bool b => mc.Write(address, b),
                short s => mc.Write(address, s),
                ushort us => mc.Write(address, us),
                int i => mc.Write(address, i),
                uint ui => mc.Write(address, ui),
                float f => mc.Write(address, f),
                double d => mc.Write(address, d),
                string str => mc.Write(address, str),
                byte[] bytes => mc.Write(address, bytes),
                _ => new OperateResult($"不支持的值类型: {value?.GetType()}")
            };
        }

        private static OperateResult WriteModbusByType(ModbusTcpNet modbus, string address, object value)
        {
            return value switch
            {
                bool b => modbus.Write(address, b),
                short s => modbus.Write(address, s),
                ushort us => modbus.Write(address, us),
                int i => modbus.Write(address, i),
                uint ui => modbus.Write(address, ui),
                float f => modbus.Write(address, f),
                double d => modbus.Write(address, d),
                string str => modbus.Write(address, str),
                byte[] bytes => modbus.Write(address, bytes),
                _ => new OperateResult($"不支持的值类型: {value?.GetType()}")
            };
        }

        // ==================== 值提取（关键修复） ====================

        private static object? ExtractValue(OperateResult result)
        {
            if (result == null) return null;

            // 处理各种泛型 OperateResult
            if (result is OperateResult<bool> boolR) return boolR.Content;
            if (result is OperateResult<byte> byteR) return byteR.Content;
            if (result is OperateResult<short> shortR) return shortR.Content;
            if (result is OperateResult<ushort> ushortR) return ushortR.Content;
            if (result is OperateResult<int> intR) return intR.Content;
            if (result is OperateResult<uint> uintR) return uintR.Content;
            if (result is OperateResult<float> floatR) return floatR.Content;
            if (result is OperateResult<double> doubleR) return doubleR.Content;
            if (result is OperateResult<string> strR) return strR.Content;
            if (result is OperateResult<byte[]> byteArrayR)
            {
                var bytes = byteArrayR.Content;
                if (bytes == null) return null;

                // 1 字节 → byte；2 字节 → 寄存器值 ushort（大端）；
                // 4/8 字节既可能是 int/float 也可能是字符串，保持 byte[] 交给上层按需转换
                return bytes.Length switch
                {
                    1 => bytes[0],
                    2 => (ushort)((bytes[0] << 8) | bytes[1]),
                    _ => bytes
                };
            }

            // 兼容没有泛型的情况
            var contentProp = result.GetType().GetProperty("Content");
            if (contentProp != null)
            {
                return contentProp.GetValue(result);
            }

            //_logger?.Warning("无法提取值，未知的 OperateResult 类型: {Type}", result.GetType());
            return null;
        }

        // ==================== 重连 ====================

        public void StartAutoReconnect()
        {
            if (_config == null) return;
            CancelReconnect();
            _reconnectCts = new CancellationTokenSource();
            _ = AutoReconnectLoop(_config, _reconnectCts.Token);
        }

        private async Task AutoReconnectLoop(PLCConfig config, CancellationToken ct)
        {
            int attempts = 0;
            while (!ct.IsCancellationRequested)
            {
                if (!IsConnected)
                {
                    attempts++;
                    _logger.Information("PLC重连尝试 #{Attempt}...", attempts);
                    if (await ConnectAsync(config, ct))
                    {
                        _logger.Information("PLC重连成功（尝试 {Attempt} 次后）", attempts);
                        attempts = 0;
                        ConnectionLost?.Invoke(this, EventArgs.Empty);
                    }
                }
                try { await Task.Delay(config.ReconnectIntervalMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void CancelReconnect()
        {
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
        }

        public void Dispose()
        {
            CancelReconnect();
            Volatile.Write(ref _connectionState, 0);
            (_device as SiemensS7Net)?.ConnectClose();
            (_device as MelsecMcNet)?.ConnectClose();
            (_device as ModbusTcpNet)?.ConnectClose();
            (_device as IDisposable)?.Dispose();
            _commLock.Dispose();
        }
    }

    /// <summary>
    /// 信号定义
    /// </summary>
    public class SignalInfo
    {
        public string Address { get; set; } = string.Empty;
        public Type DataType { get; set; } = typeof(byte[]);

        /// <summary>
        /// 读取/写入个数。
        /// 0 = 按 <see cref="DataType"/> 自动推导（见 <see cref="SignalInfo.DefaultCount"/>）；
        /// 大于 0 时表示**数组长度**（字符串类型则按"字节长度"解释）。
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 单个值占用的寄存器（16 位字）个数：
        /// Bool 1 位、Byte/Int16/UInt16 1 个、Int32/UInt32/Float 2 个、Int64/Double 4 个、String 取决于长度。
        /// </summary>
        public static int RegisterCount(Type t)
            => t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte)
               || t == typeof(short) || t == typeof(ushort) ? 1
             : t == typeof(int) || t == typeof(uint) || t == typeof(float) ? 2
             : t == typeof(long) || t == typeof(ulong) || t == typeof(double) ? 4
             : t == typeof(string) ? 0     // 由 StringByteLength 决定
             : 1;

        /// <summary>字符串按字节读取时的默认长度</summary>
        public int StringByteLength { get; set; } = 20;

        /// <summary>实际参与协议读写的个数（0 表示按类型自动）</summary>
        public int EffectiveCount(Type t)
        {
            if (Count > 0) return Count;
            if (t == typeof(string)) return Math.Max(2, StringByteLength);
            return 1;
        }
        public double ScaleFactor { get; set; } = 1.0;
        public double Offset { get; set; } = 0;
    }
}