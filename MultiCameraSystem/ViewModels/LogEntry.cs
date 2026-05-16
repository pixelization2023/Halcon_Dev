using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiCameraSystem.ViewModels
{
    public class LogEntry
    {

        public DateTime Timestamp { get; init; }
        public string Level { get; init; }        // INFO/WARN/ERROR/DEBUG
        public string Message { get; init; }
        public string? Source { get; init; }      // 来源模块
        public string? Exception{ get; init; }
    }
}
