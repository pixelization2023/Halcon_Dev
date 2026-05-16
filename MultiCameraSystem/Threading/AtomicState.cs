using System;
using System.Threading;

namespace MultiCameraSystem.Threading
{
    /// <summary>
    /// 原子状态机 — 基于 Interlocked.CompareExchange 的无锁状态切换
    /// </summary>
    public sealed class AtomicState
    {
        private int _state;

        public AtomicState(int initialState = 0) => _state = initialState;

        public int Value => Volatile.Read(ref _state);

        /// <summary>尝试将状态从 expected 切换到 next，成功返回 true</summary>
        public bool TrySet(int expected, int next)
        {
            return Interlocked.CompareExchange(ref _state, next, expected) == expected;
        }

        /// <summary>强制设置状态，返回旧值</summary>
        public int Set(int value) => Interlocked.Exchange(ref _state, value);

        /// <summary>自旋等待直到状态等于 target</summary>
        public void SpinWait(int target)
        {
            var sw = new SpinWait();
            while (Volatile.Read(ref _state) != target)
                sw.SpinOnce();
        }
    }

    /// <summary>
    /// 泛型原子引用 — 无锁的对象引用切换
    /// </summary>
    public sealed class AtomicReference<T> where T : class?
    {
        private T? _value;

        public AtomicReference(T? initial = default) => _value = initial;

        public T? Value => Volatile.Read(ref _value);

        public bool TrySet(T? expected, T? next)
        {
            return Interlocked.CompareExchange(ref _value, next, expected) == expected;
        }

        public T? Set(T? value) => Interlocked.Exchange(ref _value, value);
    }
}
