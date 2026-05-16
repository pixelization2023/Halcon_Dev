using Inspection.Models;
using PLCModule.Interfaces;
using PLCModule.Models;
using PLCDataType = PLCModule.Models.PLCDataType;
using Serilog;

namespace Inspection.Services
{
    /// <summary>PLC 点位分组名（与原项目 ReceivingAndSending 的键完全一致）</summary>
    public static class PlcIoGroups
    {
        /// <summary>相机就绪信号（写）</summary>
        public const string CameraReady = "相机Ready";

        /// <summary>相机触发信号（读，上升沿触发拍照）</summary>
        public const string CameraTrigger = "相机触发";

        /// <summary>其他发送点位（写）</summary>
        public const string OtherSend = "其他点位发送";

        /// <summary>其他触发点位（读）</summary>
        public const string OtherTrigger = "其他点位触发";
    }

    /// <summary>点位写入/读取结果事件</summary>
    public sealed class PlcTriggerEventArgs : EventArgs
    {
        public string PointName { get; init; } = string.Empty;
        public int Group { get; init; }
    }

    /// <summary>
    /// 上升沿检测。
    /// 迁移自 窗体.Plc.PLRisingedge（PLCRisingedge）—— 原实现把 getter/setter 语义写在一起，难读且易错，
    /// 这里改为语义清晰的 Trigger() 方法。
    /// </summary>
    public sealed class PlcRisingEdge
    {
        private bool _last;

        /// <summary>上一次检测到的电平</summary>
        public bool Last => _last;

        /// <summary>本次是否检测到上升沿</summary>
        public bool HasRisingEdge { get; private set; }

        /// <summary>送入当前电平，返回是否产生上升沿</summary>
        public bool Trigger(bool value)
        {
            HasRisingEdge = value && !_last;
            _last = value;
            return HasRisingEdge;
        }

        public void Reset()
        {
            _last = false;
            HasRisingEdge = false;
        }
    }

    /// <summary>
    /// PLC IO 点位服务。
    /// 迁移自 窗体 的 PlcParameter.PlcTextbox / DefineGlobalIo（点位字典与上升沿表）
    /// 以及 Inovance.GlobalIoRead / Mitsubishi.GlobalIoRead（触发轮询线程）、
    /// FrmMian.timer1_Tick 中的心跳握手、FrmMian.PlcReposition 中的复位流程。
    /// 底层通信复用 PLCModule 的 IPLCCommunicator（HslCommunication）。
    /// </summary>
    public class PlcIoService : IDisposable
    {
        private readonly IPLCCommunicator _communicator;
        private readonly ILogger _logger;
        private readonly Dictionary<string, PlcRisingEdge> _risingEdges = new();
        private readonly SemaphoreSlim _ioLock = new(1, 1);

        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;
        private bool _disposed;

        public PlcIoService(IPLCCommunicator communicator, ILogger logger)
        {
            _communicator = communicator;
            _logger = logger.ForContext<PlcIoService>();
        }

        /// <summary>当前加载的产品配置</summary>
        public ProductConfiguration? Configuration { get; private set; }

        /// <summary>PLC 是否已连接</summary>
        public bool IsConnected => _communicator.IsConnected;

        /// <summary>由 PLC 触发相机拍照</summary>
        public event EventHandler<PlcTriggerEventArgs>? CameraTriggerRequested;

        /// <summary>PLC 复位请求</summary>
        public event EventHandler<PlcTriggerEventArgs>? RepositionRequested;

        /// <summary>连接状态变化</summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        #region 连接

        /// <summary>把产品配置中的 PLC 设备参数转换为 PLCModule 的配置对象</summary>
        public static PLCConfig BuildConfig(PlcDeviceConfig device) => new()
        {
            IpAddress = device.IpAddress,
            Port = device.Port,
            Protocol = device.Vendor switch
            {
                PlcVendor.Mitsubishi => PLCProtocolType.MitsubishiMC,
                PlcVendor.Siemens => PLCProtocolType.SiemensS7,
                PlcVendor.ModbusTcp => PLCProtocolType.ModbusTcp,
                _ => PLCProtocolType.ModbusTcp   // 汇川 H5U 走标准 Modbus 寄存器
            },
            StationId = device.StationNumber,
            ConnectTimeoutMs = device.ConnectTimeoutMs,
            ReadWriteTimeoutMs = device.ReadWriteTimeoutMs
        };

        /// <summary>连接 PLC 并建立点位上升沿表（原 FrmMian.LoadProduct 中的 PLC 连接段）</summary>
        public async Task<bool> ConnectAsync(ProductConfiguration config, CancellationToken ct = default)
        {
            Configuration = config;

            if (config.PlcDevices.Count == 0)
            {
                _logger.Warning("产品配置中没有 PLC 设备，跳过连接");
                return false;
            }

            BuildRisingEdges(config);

            var device = config.PrimaryPlc!;
            var ok = await _communicator.ConnectAsync(BuildConfig(device), ct).ConfigureAwait(false);

            ConnectionStateChanged?.Invoke(this, ok);
            _logger.Information("PLC 连接结果: {Result} ({Device})", ok ? "成功" : "失败", device);
            return ok;
        }

        /// <summary>断开 PLC</summary>
        public async Task DisconnectAsync()
        {
            await StopMonitorAsync().ConfigureAwait(false);
            await _communicator.DisConnectAsync().ConfigureAwait(false);
            ConnectionStateChanged?.Invoke(this, false);
        }

        private void BuildRisingEdges(ProductConfiguration config)
        {
            lock (_risingEdges)
            {
                _risingEdges.Clear();

                foreach (var groupName in new[] { PlcIoGroups.CameraTrigger, PlcIoGroups.OtherTrigger })
                {
                    if (!config.PlcIoGroups.TryGetValue(groupName, out var group) || group == null)
                        continue;

                    foreach (var point in group.Values)
                    {
                        if (point.IsDisabled) continue;
                        var key = groupName + "/" + point.Name;
                        _risingEdges[key] = new PlcRisingEdge();
                    }
                }
            }
        }

        #endregion

        #region 读写

        /// <summary>按分组 + 点位名写入（兼容原 WriteIO(ReceivingAndSending[group][name], value) 的调用方式）</summary>
        public async Task<bool> WriteAsync(string group, string pointName, int value, CancellationToken ct = default)
        {
            var point = FindPoint(group, pointName);
            if (point == null)
            {
                _logger.Warning("未找到 PLC 点位: {Group}/{Point}", group, pointName);
                return false;
            }

            return await WriteAsync(point, value, ct).ConfigureAwait(false);
        }

        /// <summary>写入单个点位</summary>
        public async Task<bool> WriteAsync(PlcIoPoint point, int value, CancellationToken ct = default)
        {
            if (point.IsDisabled || !IsConnected) return false;

            var address = point.BuildAddress();
            if (string.IsNullOrEmpty(address)) return false;

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 写入负载按声明的类型决定：位 → bool；字 → 相应数值类型
                object payload = point.DataType switch
                {
                    PLCDataType.Bool => value != 0,
                    PLCDataType.Int16 => (short)value,
                    PLCDataType.UInt16 => (ushort)value,
                    PLCDataType.Int32 => value,
                    PLCDataType.UInt32 => (uint)value,
                    PLCDataType.Float => (float)value,
                    PLCDataType.Double => (double)value,
                    PLCDataType.String => value.ToString() ?? string.Empty,
                    _ => value != 0
                };

                return await _communicator.WriteAsync(address, payload, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "PLC 写入失败: {Point}", point);
                return false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>读取单个点位（按 <see cref="PlcIoPoint.DataType"/> 解释寄存器），失败返回 null</summary>
        public async Task<bool?> ReadAsync(PlcIoPoint point, CancellationToken ct = default)
        {
            if (point.IsDisabled || !IsConnected) return null;

            var address = point.BuildAddress();
            if (string.IsNullOrEmpty(address)) return null;

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 类型下推到协议层：Bool 走位读取（Modbus 为线圈，从站不支持时自动回退为寄存器非零）
                switch (point.DataType)
                {
                    case PLCDataType.Bool:
                    {
                        var b = await _communicator.ReadTypedAsync<bool>(address, ct).ConfigureAwait(false);
                        return b;
                    }
                    case PLCDataType.Int16:
                        return await _communicator.ReadTypedAsync<short>(address, ct).ConfigureAwait(false) != 0;
                    case PLCDataType.UInt16:
                        return await _communicator.ReadTypedAsync<ushort>(address, ct).ConfigureAwait(false) != 0;
                    case PLCDataType.Int32:
                        return await _communicator.ReadTypedAsync<int>(address, ct).ConfigureAwait(false) != 0;
                    case PLCDataType.UInt32:
                        return await _communicator.ReadTypedAsync<uint>(address, ct).ConfigureAwait(false) != 0;
                    case PLCDataType.Float:
                        return Math.Abs(await _communicator.ReadTypedAsync<float>(address, ct).ConfigureAwait(false)) > float.Epsilon;
                    case PLCDataType.Double:
                        return Math.Abs(await _communicator.ReadTypedAsync<double>(address, ct).ConfigureAwait(false)) > double.Epsilon;
                    case PLCDataType.String:
                        var text = await ReadStringAsync(point, ct).ConfigureAwait(false);
                        return !string.IsNullOrEmpty(text);
                    default:
                        return await _communicator.ReadTypedAsync<bool>(address, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "PLC 读取失败: {Point}", point);
                return null;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 读取字符串点位（例如从连续寄存器里读一段条码/配方号）。
        /// 个数按 <see cref="PlcIoPoint.Count"/> 字节长度；
        /// 由于协议层对 String 的处理是返回整段字节，这里对非字符串结果是拼装后的文本。
        /// </summary>
        public async Task<string?> ReadStringAsync(PlcIoPoint point, CancellationToken ct = default)
        {
            if (point.IsDisabled || !IsConnected) return null;

            var address = point.BuildAddress();
            if (string.IsNullOrEmpty(address)) return null;

            var length = point.Count > 1 ? point.Count : 20;

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await _communicator.ReadTypedAsync<string>(address, length, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "PLC 字符串读取失败: {Point}", point);
                return null;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>一次读取一组数值（数组），个数按 <see cref="PlcIoPoint.Count"/></summary>
        public async Task<T[]> ReadArrayAsync<T>(PlcIoPoint point, CancellationToken ct = default)
        {
            if (point.IsDisabled || !IsConnected) return Array.Empty<T>();

            var address = point.BuildAddress();
            if (string.IsNullOrEmpty(address)) return Array.Empty<T>();

            var count = point.Count > 1 ? point.Count : 1;

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await _communicator.ReadArrayAsync<T>(address, count, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "PLC 数组读取失败: {Point}", point);
                return Array.Empty<T>();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>按分组 + 点位名读取</summary>
        public async Task<bool?> ReadAsync(string group, string pointName, CancellationToken ct = default)
        {
            var point = FindPoint(group, pointName);
            return point == null ? null : await ReadAsync(point, ct).ConfigureAwait(false);
        }

        /// <summary>查找点位</summary>
        public PlcIoPoint? FindPoint(string group, string pointName)
        {
            if (Configuration == null) return null;
            if (!Configuration.PlcIoGroups.TryGetValue(group, out var dict) || dict == null) return null;

            return dict.TryGetValue(pointName, out var point) ? point : null;
        }

        #endregion

        #region 语义化信号（对应原项目散落各处的 WriteIO 调用）

        /// <summary>相机就绪（原 ReceivingAndSending["相机Ready"][name] = 1）</summary>
        public Task<bool> SignalCameraReadyAsync(string pointName, int value = 1, CancellationToken ct = default)
            => WriteAsync(PlcIoGroups.CameraReady, pointName, value, ct);

        /// <summary>未扫描到二维码（原 ["其他点位发送"]["5"] = 1）</summary>
        public Task<bool> SignalNoCodeAsync(int value = 1, CancellationToken ct = default)
            => WriteAsync(PlcIoGroups.OtherSend, "5", value, ct);

        /// <summary>重复过站 / 已检测（原 ["其他点位发送"]["8"] = 1|2）</summary>
        public Task<bool> SignalDuplicateAsync(int value = 1, CancellationToken ct = default)
            => WriteAsync(PlcIoGroups.OtherSend, "8", value, ct);

        /// <summary>数据上传完成（原 ["其他点位发送"]["9"] = 1）</summary>
        public Task<bool> SignalUploadCompletedAsync(int value = 1, CancellationToken ct = default)
            => WriteAsync(PlcIoGroups.OtherSend, "9", value, ct);

        /// <summary>复位完成（原 ["其他点位发送"]["10"] = 2 后回 0）</summary>
        public Task<bool> SignalRepositionCompletedAsync(int value = 2, CancellationToken ct = default)
            => WriteAsync(PlcIoGroups.OtherSend, "10", value, ct);

        /// <summary>心跳握手：写 1 后读回，模拟原 timer1_Tick 的上升沿确认</summary>
        public async Task<bool> HandshakeAsync(string pointName = "8", CancellationToken ct = default)
        {
            var point = FindPoint(PlcIoGroups.OtherTrigger, pointName);
            if (point == null || point.IsDisabled || !IsConnected) return false;

            try
            {
                await WriteAsync(point, 1, ct).ConfigureAwait(false);
                var value = await ReadAsync(point, ct).ConfigureAwait(false);
                if (value == true)
                {
                    await WriteAsync(point, 0, ct).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "PLC 心跳握手失败");
            }

            return false;
        }

        #endregion

        #region 触发轮询（原 Inovance.GlobalIoRead / Mitsubishi.GlobalIoRead）

        /// <summary>启动触发轮询线程</summary>
        public void StartMonitor()
        {
            if (_monitorTask != null) return;

            _monitorCts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoop(_monitorCts.Token));
            _logger.Information("PLC 触发轮询已启动");
        }

        /// <summary>停止触发轮询线程</summary>
        public async Task StopMonitorAsync()
        {
            if (_monitorTask == null) return;

            _monitorCts?.Cancel();
            try { await _monitorTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.Warning(ex, "PLC 触发轮询线程退出异常"); }

            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
        }

        private async Task MonitorLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!IsConnected)
                    {
                        ConnectionStateChanged?.Invoke(this, false);
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    await PollGroupAsync(PlcIoGroups.CameraTrigger, ct).ConfigureAwait(false);
                    await PollGroupAsync(PlcIoGroups.OtherTrigger, ct).ConfigureAwait(false);

                    await Task.Delay(8, ct).ConfigureAwait(false);   // 原实现 Thread.Sleep(8)
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "PLC 轮询异常");
                    try { await Task.Delay(500, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private async Task PollGroupAsync(string groupName, CancellationToken ct)
        {
            var config = Configuration;
            if (config == null) return;
            if (!config.PlcIoGroups.TryGetValue(groupName, out var group) || group == null) return;

            foreach (var point in group.Values)
            {
                if (point.IsDisabled) continue;

                var value = await ReadAsync(point, ct).ConfigureAwait(false);
                if (value == null) continue;

                PlcRisingEdge? edge;
                lock (_risingEdges)
                {
                    _risingEdges.TryGetValue(groupName + "/" + point.Name, out edge);
                }

                if (edge == null) continue;
                if (!edge.Trigger(value.Value)) continue;

                _logger.Information("PLC 上升沿触发: {Group}/{Point}", groupName, point.Name);

                if (groupName == PlcIoGroups.CameraTrigger)
                    CameraTriggerRequested?.Invoke(this, new PlcTriggerEventArgs { PointName = point.Name });
                else
                    RepositionRequested?.Invoke(this, new PlcTriggerEventArgs { PointName = point.Name });
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { StopMonitorAsync().GetAwaiter().GetResult(); } catch { }
            _ioLock.Dispose();
        }
    }
}
