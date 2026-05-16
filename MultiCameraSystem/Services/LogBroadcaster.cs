using MultiCameraSystem.Threading;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MultiCameraSystem.Services
{
    public class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public string Level { get; init; } = "INFO";
        public string Message { get; init; } = "";
        public string? Source { get; init; }
        public string? Exception { get; init; }
    }

    /// <summary>
    /// 日志广播器 — Serilog Sink + UI 数据源
    /// Emit 在后台线程被 Serilog 调用，通过订阅者模式推送到 UI
    /// </summary>
    public class LogBroadcaster : ILogEventSink, IDisposable
    {
        private readonly RingBuffer<LogEntry> _buffer;
        private readonly object _subLock = new();
        private Action<LogEntry>? _subscribers;

        public LogBroadcaster(int capacity = 5000)
        {
            _buffer = new RingBuffer<LogEntry>(capacity);
        }

        public void Emit(LogEvent logEvent)
        {
            var entry = new LogEntry
            {
                Timestamp = logEvent.Timestamp.DateTime,
                Level = logEvent.Level.ToString().ToUpper(),
                Message = logEvent.RenderMessage(),
                Exception = logEvent.Exception?.ToString(),
                Source = logEvent.Properties.TryGetValue("SourceContext", out var sv)
                    ? sv.ToString().Trim('"')
                    : null
            };

            _buffer.Add(entry);

            Action<LogEntry>? handlers;
            lock (_subLock) { handlers = _subscribers; }

            if (handlers != null)
            {
                // 通过 Application.Dispatcher 确保回调在 UI 线程执行
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                    dispatcher.BeginInvoke(() => handlers.Invoke(entry));
                else
                    handlers.Invoke(entry);
            }
        }

        public IDisposable Subscribe(Action<LogEntry> handler)
        {
            lock (_subLock) { _subscribers += handler; }
            return new Unsubscriber(this, handler);
        }

        private void Unsubscribe(Action<LogEntry> handler)
        {
            lock (_subLock) { _subscribers -= handler; }
        }

        /// <summary>获取最近N条日志快照（用于初始化 UI）</summary>
        public IReadOnlyList<LogEntry> RecentLogs => _buffer.ToArray();

        public void Clear()
        {
            _buffer.Clear();
        }

        public void Dispose()
        {
            lock (_subLock) { _subscribers = null; }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly LogBroadcaster _parent;
            private readonly Action<LogEntry> _handler;
            public Unsubscriber(LogBroadcaster parent, Action<LogEntry> handler)
            { _parent = parent; _handler = handler; }
            public void Dispose() => _parent.Unsubscribe(_handler);
        }
    }
}
