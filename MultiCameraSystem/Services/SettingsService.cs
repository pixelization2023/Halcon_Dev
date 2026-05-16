using MultiCameraSystem.Models;
using Serilog;
using System.IO;
using System.Text.Json;

namespace MultiCameraSystem.Services
{
    public class SettingsService
    {
        private readonly ILogger _logger;
        private readonly string _filePath;
        private AppSettings _current = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public AppSettings Current => _current;

        public SettingsService(ILogger logger, string? filePath = null)
        {
            _logger = logger.ForContext<SettingsService>();
            _filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    _logger.Information("配置文件不存在，创建默认配置: {Path}", _filePath);
                    Save();
                    return true;
                }

                var json = File.ReadAllText(_filePath);
                _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                _logger.Information("配置已加载: {Path}", _filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载配置失败: {Path}", _filePath);
                _current = new AppSettings();
                return false;
            }
        }

        public bool Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_current, JsonOptions);
                File.WriteAllText(_filePath, json);
                _logger.Information("配置已保存: {Path}", _filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存配置失败: {Path}", _filePath);
                return false;
            }
        }

        public void Update(Action<AppSettings> updateAction)
        {
            updateAction(_current);
            Save();
        }
    }
}
