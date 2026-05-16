using HalconDotNet;
using System.Collections.Concurrent;
using WorkBench.Interfaces;

namespace WorkBench.Models
{
    /// <summary>检测上下文实现 — 线程安全</summary>
    public class InspectionContext : IInspectionContext
    {
        private readonly ConcurrentDictionary<string, HObject> _images = new();
        private readonly ConcurrentDictionary<string, object> _results = new();
        private readonly ConcurrentDictionary<string, object> _plcData = new();

        public string ProductBarcode { get; set; } = "";
        public DateTime StartTime { get; } = DateTime.Now;
        public InspectionStatus Status { get; set; } = InspectionStatus.Idle;
        public Dictionary<string, string> Metadata { get; } = new();

        public void SetImage(string cameraName, HObject image)
        {
            // 深拷贝确保线程安全
            var clone = image.Clone();
            _images.AddOrUpdate(cameraName, clone, (_, old) => { old?.Dispose(); return clone; });
        }

        public HObject? GetImage(string cameraName)
        {
            return _images.TryGetValue(cameraName, out var img) ? img : null;
        }

        public void SetResult(string stepName, object result)
        {
            _results[stepName] = result;
        }

        public T? GetResult<T>(string stepName)
        {
            if (_results.TryGetValue(stepName, out var val) && val is T typed)
                return typed;
            return default;
        }

        public void SetPLCData(string signalName, object value)
        {
            _plcData[signalName] = value;
        }

        public T? GetPLCData<T>(string signalName)
        {
            if (_plcData.TryGetValue(signalName, out var val) && val is T typed)
                return typed;
            return default;
        }

        /// <summary>清理所有图像资源</summary>
        public void DisposeImages()
        {
            foreach (var kv in _images)
                kv.Value?.Dispose();
            _images.Clear();
        }
    }
}
