using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MultiCameraSystem.Threading
{
    /// <summary>
    /// UI线程调度辅助 — 检查/切换到UI线程执行
    /// </summary>
    public static class UIThreadHelper
    {
        /// <summary>判断当前是否在UI线程</summary>
        public static bool IsUIThread => Application.Current?.Dispatcher.CheckAccess() ?? false;

        /// <summary>在UI线程上执行 Action（同步，如果已在UI线程则直接执行）</summary>
        public static void Execute(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) { action(); return; }
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        /// <summary>在UI线程上执行 Action（异步）</summary>
        public static async Task ExecuteAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) { action(); return; }
            if (dispatcher.CheckAccess())
                action();
            else
                await dispatcher.InvokeAsync(action);
        }

        /// <summary>在UI线程上执行 Func（异步）</summary>
        public static async Task<T> ExecuteAsync<T>(Func<T> func)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return func();
            if (dispatcher.CheckAccess())
                return func();
            return await dispatcher.InvokeAsync(func);
        }
    }
}
