using System.Collections.Concurrent;
using Inspection.Models;

namespace Inspection.Services
{
    /// <summary>
    /// 待检测图像的**有界**队列（生产者 = 相机回调，消费者 = 检测编排线程）。
    ///
    /// 从 <see cref="InspectionOrchestrator"/> 抽出的理由：容量、丢帧策略、丢弃计数与
    /// 节流告警与"一帧怎么检测"没有任何关系，是可以独立测试的数据结构职责；
    /// 抽走后编排器只剩"拿到一帧做什么"。
    ///
    /// **队列满时的取舍：丢最旧的帧**（腾位置给新帧），不阻塞、也不丢新帧：
    /// <list type="number">
    /// <item>不能阻塞 —— 在相机回调里阻塞会连累整个取流；</item>
    /// <item>丢最新帧更糟 —— 那张图对应的料可能已经过了工位，之后永远等不到它；</item>
    /// <item>生产节拍是"连续过板"，已经有积压时，最新的图才最能反映当前状态。</item>
    /// </list>
    /// 无论丢哪一种，都会**明确计数 + 按秒节流上报**，让现场知道"我们在丢图、
    /// 说明检测节拍跟不上采集节拍"，而不是让它静默演变成内存增长。
    /// </summary>
    internal sealed class FrameQueue : IDisposable
    {
        /// <summary>队列容量上限（张）</summary>
        public const int Capacity = 32;

        private readonly BlockingCollection<QueuedFrame> _frames;
        private readonly Action<int> _onDropped;

        private int _droppedCount;
        private long _lastDropReportTicks;
        private bool _disposed;

        /// <param name="onDropped">
        /// 发生丢弃时的回调，参数为**累计**丢弃帧数。调用方负责日志与界面上报
        /// （本类只管"什么时候该报"，不管"报给谁"）。
        /// </param>
        public FrameQueue(Action<int> onDropped)
        {
            _onDropped = onDropped ?? throw new ArgumentNullException(nameof(onDropped));
            _frames = new BlockingCollection<QueuedFrame>(new ConcurrentQueue<QueuedFrame>(), Capacity);
        }

        /// <summary>累计丢弃的帧数（自检/诊断用）</summary>
        public int DroppedCount => Volatile.Read(ref _droppedCount);

        /// <summary>
        /// 入队一帧。队列已关闭/已释放时**释放该帧**并返回 false；
        /// 队列已满时丢最旧的一帧后再入队（返回值反映最终是否入队成功）。
        /// </summary>
        public bool TryEnqueue(QueuedFrame frame)
        {
            try
            {
                if (_frames.TryAdd(frame))
                    return true;

                if (_frames.TryTake(out var oldest))
                {
                    oldest.Dispose();
                    Interlocked.Increment(ref _droppedCount);
                    RaiseDroppedThrottled();
                }

                return _frames.TryAdd(frame);
            }
            catch (InvalidOperationException)
            {
                // 队列已 CompleteAdding / 已释放
                frame.Dispose();
                return false;
            }
        }

        /// <summary>取消费序列（供消费者 <c>foreach</c>；取消时结束枚举）</summary>
        public IEnumerable<QueuedFrame> Consume(CancellationToken ct) => _frames.GetConsumingEnumerable(ct);

        /// <summary>取空队列并释放其中的每一帧（停止编排时调用，避免遗留图像句柄）</summary>
        public void Drain()
        {
            while (_frames.TryTake(out var frame))
                frame.Dispose();
        }

        /// <summary>丢弃告警：**1 秒内只上报一次**，避免连续采集时刷爆日志</summary>
        private void RaiseDroppedThrottled()
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastDropReportTicks);

            if (now - last < TimeSpan.TicksPerSecond) return;
            if (Interlocked.CompareExchange(ref _lastDropReportTicks, now, last) != last) return;

            _onDropped(Volatile.Read(ref _droppedCount));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frames.Dispose();
        }
    }
}
