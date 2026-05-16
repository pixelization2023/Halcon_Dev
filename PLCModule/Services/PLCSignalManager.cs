using PLCModule.Interfaces;
using PLCModule.Models;
using Serilog;
using System.Collections.Concurrent;

namespace PLCModule.Services
{
    public class PLCSignalManager : IPLCDataHandler
    {
        private readonly IPLCCommunicator _communicator;
        private readonly ILogger _logger;
        private readonly object _signalsLock = new();
        private readonly Dictionary<string, PLCSignal> _signals = new();
        private readonly Dictionary<string, Func<object>> _writeCallbacks = new();
        private readonly ConcurrentDictionary<string, object?> _cache = new();

        public PLCSignalManager(IPLCCommunicator communicator, ILogger logger)
        {
            _communicator = communicator;
            _logger = logger.ForContext<PLCSignalManager>();
            _communicator.SignalChanged += OnSignalChanged;
        }

        public int SignalCount { get { lock (_signalsLock) return _signals.Count; } }

        public void RegisterSignal(PLCSignal signal)
        {
            lock (_signalsLock)
            {
                _signals[signal.Name] = signal;
                if (signal.CachedValue != null)
                    _cache[signal.Name] = signal.CachedValue;
            }
            _logger.Debug("注册PLC信号: {Name} [{Address}]", signal.Name, signal.Address);
        }

        public T? GetCachedValue<T>(string signalName)
        {
            if (_cache.TryGetValue(signalName, out var val) && val is T typed)
                return typed;
            return default;
        }

        public void RegisterWriteCallback(string signalName, Func<object> valueProvider)
        {
            lock (_writeCallbacks)
            {
                _writeCallbacks[signalName] = valueProvider;
            }
            _logger.Debug("注册PLC写入回调: {Name}", signalName);
        }

        public async Task ExecuteWriteCallbacksAsync(CancellationToken ct = default)
        {
            Dictionary<string, Func<object>> callbacks;
            lock (_writeCallbacks) { callbacks = new(_writeCallbacks); }

            var writes = new Dictionary<string, object>();
            foreach (var kv in callbacks)
            {
                try { writes[kv.Key] = kv.Value(); }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "PLC写入回调执行失败: {Name}", kv.Key);
                }
            }

            if (writes.Count > 0)
            {
                _logger.Debug("批量写入 {Count} 个PLC信号", writes.Count);
                await _communicator.WriteBatchAsync(writes, ct);
            }
        }

        public PLCSignal[] GetAllSignals()
        {
            lock (_signalsLock) return _signals.Values.ToArray();
        }

        public async Task PollAllAsync(CancellationToken ct = default)
        {
            string[] names;
            lock (_signalsLock) { names = _signals.Keys.ToArray(); }

            if (names.Length == 0) return;

            _logger.Debug("轮询 {Count} 个PLC信号", names.Length);
            var results = await _communicator.ReadBatchAsync(names, ct);

            foreach (var kv in results)
            {
                if (kv.Value.Success)
                {
                    lock (_signalsLock)
                    {
                        if (_signals.TryGetValue(kv.Key, out var signal))
                        {
                            signal.UpdateCachedValue(kv.Value.Value);
                            _cache[kv.Key] = kv.Value.Value;
                        }
                    }
                }
                else
                {
                    _logger.Warning("PLC信号读取失败: {Name} - {Error}", kv.Key, kv.Value.ErrorMessage);
                }
            }
        }

        private void OnSignalChanged(object? sender, PLCSignalChangedEventArgs e)
        {
            lock (_signalsLock)
            {
                if (_signals.TryGetValue(e.SignalName, out var signal))
                {
                    signal.UpdateCachedValue(e.NewValue);
                    _cache[e.SignalName] = e.NewValue;
                }
            }
            _logger.Debug("PLC信号变化: {Name} = {Value}", e.SignalName, e.NewValue);
        }
    }
}
