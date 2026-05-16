using System;
using System.Threading;
using System.Threading.Tasks;

namespace MVS.Core.Threading
{
    /// <summary>
    /// 异步锁 — SemaphoreSlim(1,1) 的封装，支持 using 语句
    /// 用法: using(await _lock.LockAsync()) { ... }
    /// </summary>
    public sealed class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<Releaser> LockAsync(CancellationToken ct = default)
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            return new Releaser(_semaphore);
        }

        public Releaser Lock(CancellationToken ct = default)
        {
            _semaphore.Wait(ct);
            return new Releaser(_semaphore);
        }

        public void Dispose() => _semaphore.Dispose();

        public readonly struct Releaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            internal Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
            public void Dispose() => _semaphore.Release();
        }
    }
}
