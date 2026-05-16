using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MultiCameraSystem.Threading
{
    /// <summary>
    /// 单写多读缓存 — 适用于PLC/MES数据等"一个线程写入，多个线程读取"的场景
    /// 读取无锁，写入有锁
    /// </summary>
    public sealed class SingleWriterCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TValue> _cache = new();
        private readonly ReaderWriterLockSlim _rwLock = new();

        public TValue? Get(TKey key)
        {
            _rwLock.EnterReadLock();
            try { return _cache.TryGetValue(key, out var v) ? v : default; }
            finally { _rwLock.ExitReadLock(); }
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            _rwLock.EnterReadLock();
            try { return _cache.TryGetValue(key, out value); }
            finally { _rwLock.ExitReadLock(); }
        }

        public void Set(TKey key, TValue value)
        {
            _rwLock.EnterWriteLock();
            try { _cache[key] = value; }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void Remove(TKey key)
        {
            _rwLock.EnterWriteLock();
            try { _cache.TryRemove(key, out _); }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void Clear()
        {
            _rwLock.EnterWriteLock();
            try { _cache.Clear(); }
            finally { _rwLock.ExitWriteLock(); }
        }

        public TValue[] GetAllValues()
        {
            _rwLock.EnterReadLock();
            try { return [.. _cache.Values]; }
            finally { _rwLock.ExitReadLock(); }
        }
    }
}
