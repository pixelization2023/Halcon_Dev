using System.Collections.ObjectModel;
using System.Globalization;
using PLCModule.Interfaces;
using PLCModule.Models;
using Serilog;

namespace PLCModule.ViewModels
{
    public class PLCMonitorViewModel : BindableBase, INavigationAware
    {
        private readonly ILogger _logger;
        private readonly IPLCCommunicator _plcCommunicator;

        public PLCMonitorViewModel(ILogger logger, IPLCCommunicator plcCommunicator)
        {
            _logger = logger.ForContext<PLCMonitorViewModel>();
            _plcCommunicator = plcCommunicator;
        }

        private string _ipAddress = "192.168.1.100";
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        private int _port = 502;
        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        private string _protocol = "ModbusTcp";
        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                SetProperty(ref _isConnected, value);
                RaisePropertyChanged(nameof(ConnectionStatus));
            }
        }

        public string ConnectionStatus => IsConnected ? "已连接" : "未连接";

        private string _status = "未连接 - 配置PLC参数后点击连接";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _lastSignalName = "0";
        public string LastSignalName
        {
            get => _lastSignalName;
            set => SetProperty(ref _lastSignalName, value);
        }

        private string _lastReadValue = "--";
        public string LastReadValue
        {
            get => _lastReadValue;
            set => SetProperty(ref _lastReadValue, value);
        }

        /// <summary>
        /// 读取时按什么类型解释寄存器。
        /// PLC 的 <c>Read(address, len)</c> 只给字节，必须由使用方声明类型，
        /// 否则显示出来就是 "System.Byte[]"。
        /// </summary>
        public IReadOnlyList<string> DataTypeOptions { get; } =
            new[] { "Bool", "UInt16", "Int16", "Int32", "Float", "String" };

        private string _selectedDataType = "UInt16";
        public string SelectedDataType
        {
            get => _selectedDataType;
            set => SetProperty(ref _selectedDataType, value);
        }

        private int _readCount = 1;
        /// <summary>
        /// 读取个数。
        /// 1 = 读单个值；大于 1 = 读一组（Bool/Int16/UInt16 各占 1 个寄存器，
        /// Int32/Float 各占 2 个，Double 各占 4 个）。
        /// </summary>
        public int ReadCount
        {
            get => _readCount;
            set => SetProperty(ref _readCount, value < 1 ? 1 : value);
        }

        private string _writeValue = "0";
        /// <summary>待写入的值；个数 > 1 时用逗号分隔多个值</summary>
        public string WriteValue
        {
            get => _writeValue;
            set => SetProperty(ref _writeValue, value);
        }

        /// <summary>数组读取时逐个值的结果（界面上列表显示）</summary>
        public ObservableCollection<string> ReadValues { get; } = new();

        private bool _hasValues;
        public bool HasValues
        {
            get => _hasValues;
            private set => SetProperty(ref _hasValues, value);
        }

        /// <summary>HslCommunication 组件授权状态（商业组件必须注册）</summary>
        public string LicenseStatus => HslLicense.IsRegistered
            ? "HslCommunication 授权：已注册"
            : "HslCommunication 授权：试用模式（受限）";

        /// <summary>授权状态颜色（供界面绑定）</summary>
        public bool IsLicenseOk => HslLicense.IsRegistered;

        private int _signalCount;
        public int SignalCount
        {
            get => _signalCount;
            set => SetProperty(ref _signalCount, value);
        }

        private DelegateCommand? _connectCommand;
        public DelegateCommand ConnectCommand =>
            _connectCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    var config = new PLCConfig
                    {
                        IpAddress = IpAddress,
                        Port = Port,
                        Protocol = Enum.TryParse<PLCProtocolType>(Protocol, out var p) ? p : PLCProtocolType.ModbusTcp,
                        ConnectTimeoutMs = 3000,
                        ReadWriteTimeoutMs = 2000
                    };
                    Status = "连接中...";
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var result = await _plcCommunicator.ConnectAsync(config, cts.Token);
                    IsConnected = result;
                    Status = result ? "连接成功" : "连接失败";
                    _logger.Information("PLC连接: {Result}", result ? "成功" : "失败");
                }
                catch (Exception ex)
                {
                    Status = $"连接失败: {ex.Message}";
                    _logger.Error(ex, "PLC连接异常");
                }
            });

        private DelegateCommand? _disconnectCommand;
        public DelegateCommand DisconnectCommand =>
            _disconnectCommand ??= new DelegateCommand(async () =>
            {
                await _plcCommunicator.DisConnectAsync();
                IsConnected = false;
                Status = "已断开连接";
                _logger.Information("PLC已断开");
            });

        private DelegateCommand? _readSignalCommand;
        public DelegateCommand ReadSignalCommand =>
            _readSignalCommand ??= new DelegateCommand(async () => await ReadCurrentAsync());

        private DelegateCommand? _writeCommand;
        /// <summary>写入：按「地址 + 数据类型 + 个数」把值写进 PLC，成功后自动回读确认</summary>
        public DelegateCommand WriteCommand =>
            _writeCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    var address = LastSignalName?.Trim() ?? string.Empty;
                    if (address.Length == 0)
                    {
                        Status = "请先填写信号地址";
                        return;
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var count = Math.Max(1, ReadCount);
                    bool ok;

                    if (count > 1)
                    {
                        // 一次写多个：逗号/空格分隔
                        var parts = (WriteValue ?? string.Empty)
                            .Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length == 0)
                        {
                            Status = "请填写要写入的多个值（用逗号分隔）";
                            return;
                        }

                        ok = SelectedDataType switch
                        {
                            "Bool" => await _plcCommunicator.WriteArrayAsync(address, parts.Select(ParseBool).ToArray(), cts.Token),
                            "Int16" => await _plcCommunicator.WriteArrayAsync(address, parts.Select(short.Parse).ToArray(), cts.Token),
                            "Int32" => await _plcCommunicator.WriteArrayAsync(address, parts.Select(int.Parse).ToArray(), cts.Token),
                            "Float" => await _plcCommunicator.WriteArrayAsync(address, parts.Select(f => float.Parse(f, CultureInfo.InvariantCulture)).ToArray(), cts.Token),
                            "Double" => await _plcCommunicator.WriteArrayAsync(address, parts.Select(d => double.Parse(d, CultureInfo.InvariantCulture)).ToArray(), cts.Token),
                            _ => await _plcCommunicator.WriteArrayAsync(address, parts.Select(ushort.Parse).ToArray(), cts.Token)
                        };
                    }
                    else
                    {
                        var text = (WriteValue ?? string.Empty).Trim();
                        if (text.Length == 0)
                        {
                            Status = "请填写要写入的值";
                            return;
                        }

                        ok = SelectedDataType switch
                        {
                            "Bool" => await _plcCommunicator.WriteTypedAsync(address, ParseBool(text), cts.Token),
                            "Int16" => await _plcCommunicator.WriteTypedAsync(address, short.Parse(text), cts.Token),
                            "Int32" => await _plcCommunicator.WriteTypedAsync(address, int.Parse(text), cts.Token),
                            "Float" => await _plcCommunicator.WriteTypedAsync(address, float.Parse(text, CultureInfo.InvariantCulture), cts.Token),
                            "Double" => await _plcCommunicator.WriteTypedAsync(address, double.Parse(text, CultureInfo.InvariantCulture), cts.Token),
                            "String" => await _plcCommunicator.WriteTypedAsync(address, text, cts.Token),
                            _ => await _plcCommunicator.WriteTypedAsync(address, ushort.Parse(text), cts.Token)
                        };
                    }

                    Status = ok
                        ? $"写入成功：{address} [{SelectedDataType}] ← {WriteValue}（正在回读…）"
                        : $"写入失败：{address} [{SelectedDataType}] ← {WriteValue}（详见日志）";

                    _logger.Information("PLC 写入: {Address} [{Type}] ← {Value} = {Ok}", address, SelectedDataType, WriteValue, ok);

                    // 回读确认，避免"以为写进去了"
                    if (ok) await ReadCurrentAsync();
                }
                catch (Exception ex)
                {
                    Status = $"写入异常: {ex.Message}";
                    _logger.Warning(ex, "PLC 写入失败: {Address}", LastSignalName);
                }
            });

        /// <summary>读取当前地址（读取命令与写入后的回读共用）</summary>
        private async Task ReadCurrentAsync()
        {
            try
            {
                {
                    var address = LastSignalName?.Trim() ?? string.Empty;
                    // 说明：下面这段是"地址校验 + 读取"的独立块，保持与写入命令对称
                    if (address.Length == 0)
                    {
                        Status = "请先填写信号地址";
                        return;
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                    // 按声明类型读取：类型转换在 PLCCommunicator.ConvertRaw 里统一处理
                    var count = Math.Max(1, ReadCount);

                    if (count > 1)
                    {
                        // 一次读多个：个数按类型语义传给协议层
                        var values = SelectedDataType switch
                        {
                            "Bool" => ToTexts(await _plcCommunicator.ReadArrayAsync<bool>(address, count, cts.Token)),
                            "Int16" => ToTexts(await _plcCommunicator.ReadArrayAsync<short>(address, count, cts.Token)),
                            "Int32" => ToTexts(await _plcCommunicator.ReadArrayAsync<int>(address, count, cts.Token)),
                            "Float" => ToTexts(await _plcCommunicator.ReadArrayAsync<float>(address, count, cts.Token)),
                            _ => ToTexts(await _plcCommunicator.ReadArrayAsync<ushort>(address, count, cts.Token))
                        };

                        ReadValues.Clear();
                        foreach (var v in values) ReadValues.Add(v);
                        HasValues = true;

                        LastReadValue = values.Count <= 8
                            ? string.Join(", ", values)
                            : string.Join(", ", values.Take(8)) + $" … (共 {values.Count} 个)";

                        Status = $"读取成功：{address} [{SelectedDataType}] × {values.Count}";
                        _logger.Information("PLC 读取数组: {Address} [{Type}] × {Count} = {Value}",
                            address, SelectedDataType, values.Count, LastReadValue);
                        return;
                    }

                    HasValues = false;
                    ReadValues.Clear();

                    object? value = SelectedDataType switch
                    {
                        "Bool" => await _plcCommunicator.ReadTypedAsync<bool>(address, cts.Token),
                        "Int16" => await _plcCommunicator.ReadTypedAsync<short>(address, cts.Token),
                        "Int32" => await _plcCommunicator.ReadTypedAsync<int>(address, cts.Token),
                        "Float" => await _plcCommunicator.ReadTypedAsync<float>(address, cts.Token),
                        "String" => await _plcCommunicator.ReadTypedAsync<string>(address, cts.Token),
                        _ => await _plcCommunicator.ReadTypedAsync<ushort>(address, cts.Token)
                    };

                    LastReadValue = FormatValue(value);
                    Status = $"读取成功：{address} [{SelectedDataType}] = {LastReadValue}";
                    _logger.Information("PLC 读取: {Address} [{Type}] = {Value}", address, SelectedDataType, LastReadValue);
                }
            }
            catch (Exception ex)
            {
                LastReadValue = "--";
                HasValues = false;
                ReadValues.Clear();
                Status = $"读取失败: {ex.Message}";
                _logger.Warning(ex, "PLC 读取失败: {Address}", LastSignalName);
            }
        }

        private static bool ParseBool(string text)
            => text is "1" or "true" or "TRUE" or "True" or "on" or "ON";

        /// <summary>按类型规范化显示</summary>
        private static string FormatValue(object? value) => value switch
        {
            null => "--",
            bool b => b ? "1 (ON)" : "0 (OFF)",
            float f => f.ToString("F3"),
            double d => d.ToString("F3"),
            _ => value.ToString() ?? "--"
        };

        private static List<string> ToTexts<T>(IEnumerable<T> values)
            => values.Select(v => FormatValue(v)).ToList();

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入PLC监控界面");
            _isConnected = _plcCommunicator.IsConnected;
            RaisePropertyChanged(nameof(IsConnected));
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
