using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace MultiCameraSystem.Threading
{
    /// <summary>
    /// 固定容量环形缓冲区 — 线程安全，旧数据自动覆盖
    /// </summary>
    public sealed class RingBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private long _writeIndex;
        private long _readIndex;
        private int _count;
        private readonly object _lock = new();

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be positive");
            _buffer = new T[capacity];
        }

        public int Capacity => _buffer.Length;
        public int Count { get { lock (_lock) return _count; } }

        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer[_writeIndex % Capacity] = item;
                _writeIndex++;
                if (_count < Capacity)
                    _count++;
                else
                    _readIndex = _writeIndex - Capacity;
            }
        }

        public T[] ToArray()
        {
            lock (_lock)
            {
                var result = new T[_count];
                for (int i = 0; i < _count; i++)
                    result[i] = _buffer[(_readIndex + i) % Capacity];
                return result;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _writeIndex = 0;
                _readIndex = 0;
                _count = 0;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            var snapshot = ToArray();
            foreach (var item in snapshot) yield return item;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
