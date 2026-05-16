using MultiCameraSystem.Models;
using MultiCameraSystem.Services;
using Serilog;

namespace MultiCameraSystem.ViewModels
{
    public class SettingsViewModel : BindableBase, INavigationAware
    {
        private readonly SettingsService _settings;
        private readonly ILogger _logger;

        public SettingsViewModel(SettingsService settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger.ForContext<SettingsViewModel>();

            SaveCommand = new DelegateCommand(ExecuteSave);
            ReloadCommand = new DelegateCommand(ExecuteReload);
            ResetCommand = new DelegateCommand(ExecuteReset);

            LoadFromSettings();
        }

        /// <summary>是否存在未保存修改（原来 _dirty 只写不读，是死字段）</summary>
        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }

        #region 常规

        private string _imageSavePath = @"D:\Captures";
        public string ImageSavePath { get => _imageSavePath; set { if (SetProperty(ref _imageSavePath, value)) MarkDirty(); } }

        private string _logPath = "Logs";
        public string LogPath { get => _logPath; set { if (SetProperty(ref _logPath, value)) MarkDirty(); } }

        private int _logRetentionDays = 30;
        public int LogRetentionDays { get => _logRetentionDays; set { if (SetProperty(ref _logRetentionDays, value)) MarkDirty(); } }

        private bool _autoStartCamera = true;
        public bool AutoStartCamera { get => _autoStartCamera; set { if (SetProperty(ref _autoStartCamera, value)) MarkDirty(); } }

        #endregion

        #region 相机默认

        private float _exposureTime = 5000f;
        public float ExposureTime { get => _exposureTime; set { if (SetProperty(ref _exposureTime, value)) MarkDirty(); } }

        private float _gainValue = 20f;
        public float GainValue { get => _gainValue; set { if (SetProperty(ref _gainValue, value)) MarkDirty(); } }

        private int _grabTimeout = 1000;
        public int GrabTimeout { get => _grabTimeout; set { if (SetProperty(ref _grabTimeout, value)) MarkDirty(); } }

        private int _bufferCount = 5;
        public int BufferCount { get => _bufferCount; set { if (SetProperty(ref _bufferCount, value)) MarkDirty(); } }

        private string _imageFormat = "Bmp";
        public string ImageFormat { get => _imageFormat; set { if (SetProperty(ref _imageFormat, value)) MarkDirty(); } }

        #endregion

        #region PLC

        private string _plcIp = "192.168.1.100";
        public string PlcIp { get => _plcIp; set { if (SetProperty(ref _plcIp, value)) MarkDirty(); } }

        private int _plcPort = 502;
        public int PlcPort { get => _plcPort; set { if (SetProperty(ref _plcPort, value)) MarkDirty(); } }

        private string _plcProtocol = "ModbusTcp";
        public string PlcProtocol { get => _plcProtocol; set { if (SetProperty(ref _plcProtocol, value)) MarkDirty(); } }

        private int _plcConnectTimeout = 3000;
        public int PlcConnectTimeout { get => _plcConnectTimeout; set { if (SetProperty(ref _plcConnectTimeout, value)) MarkDirty(); } }

        private int _plcReadWriteTimeout = 2000;
        public int PlcReadWriteTimeout { get => _plcReadWriteTimeout; set { if (SetProperty(ref _plcReadWriteTimeout, value)) MarkDirty(); } }

        #endregion

        #region MES

        private string _mesUrl = "http://localhost:5000/api";
        public string MesUrl { get => _mesUrl; set { if (SetProperty(ref _mesUrl, value)) MarkDirty(); } }

        private string _mesApiKey = "";
        public string MesApiKey { get => _mesApiKey; set { if (SetProperty(ref _mesApiKey, value)) MarkDirty(); } }

        private string _mesLineId = "LINE-01";
        public string MesLineId { get => _mesLineId; set { if (SetProperty(ref _mesLineId, value)) MarkDirty(); } }

        private string _mesStationId = "CCD-01";
        public string MesStationId { get => _mesStationId; set { if (SetProperty(ref _mesStationId, value)) MarkDirty(); } }

        private int _mesTimeout = 10;
        public int MesTimeout { get => _mesTimeout; set { if (SetProperty(ref _mesTimeout, value)) MarkDirty(); } }

        private int _mesMaxRetries = 3;
        public int MesMaxRetries { get => _mesMaxRetries; set { if (SetProperty(ref _mesMaxRetries, value)) MarkDirty(); } }

        #endregion

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private void MarkDirty()
        {
            HasUnsavedChanges = true;
            StatusMessage = "已修改 (未保存)";
        }

        private void LoadFromSettings()
        {
            var s = _settings.Current;
            ImageSavePath = s.General.ImageSavePath;
            LogPath = s.General.LogPath;
            LogRetentionDays = s.General.LogRetentionDays;
            AutoStartCamera = s.General.AutoStartCamera;

            ExposureTime = s.CameraDefaults.ExposureTime;
            GainValue = s.CameraDefaults.GainValue;
            GrabTimeout = s.CameraDefaults.GrabTimeout;
            BufferCount = s.CameraDefaults.BufferCount;
            ImageFormat = s.CameraDefaults.ImageFormat;

            PlcIp = s.Plc.IpAddress;
            PlcPort = s.Plc.Port;
            PlcProtocol = s.Plc.Protocol;
            PlcConnectTimeout = s.Plc.ConnectTimeoutMs;
            PlcReadWriteTimeout = s.Plc.ReadWriteTimeoutMs;

            MesUrl = s.Mes.BaseUrl;
            MesApiKey = s.Mes.ApiKey;
            MesLineId = s.Mes.LineId;
            MesStationId = s.Mes.StationId;
            MesTimeout = s.Mes.TimeoutSeconds;
            MesMaxRetries = s.Mes.MaxRetries;

            HasUnsavedChanges = false;
            StatusMessage = "";
        }

        private void SaveToSettings()
        {
            _settings.Update(s =>
            {
                s.General.ImageSavePath = ImageSavePath;
                s.General.LogPath = LogPath;
                s.General.LogRetentionDays = LogRetentionDays;
                s.General.AutoStartCamera = AutoStartCamera;

                s.CameraDefaults.ExposureTime = ExposureTime;
                s.CameraDefaults.GainValue = GainValue;
                s.CameraDefaults.GrabTimeout = GrabTimeout;
                s.CameraDefaults.BufferCount = BufferCount;
                s.CameraDefaults.ImageFormat = ImageFormat;

                s.Plc.IpAddress = PlcIp;
                s.Plc.Port = PlcPort;
                s.Plc.Protocol = PlcProtocol;
                s.Plc.ConnectTimeoutMs = PlcConnectTimeout;
                s.Plc.ReadWriteTimeoutMs = PlcReadWriteTimeout;

                s.Mes.BaseUrl = MesUrl;
                s.Mes.ApiKey = MesApiKey;
                s.Mes.LineId = MesLineId;
                s.Mes.StationId = MesStationId;
                s.Mes.TimeoutSeconds = MesTimeout;
                s.Mes.MaxRetries = MesMaxRetries;
            });
            HasUnsavedChanges = false;
            StatusMessage = "配置已保存 ✓";
            _logger.Information("配置已保存到文件");
        }

        public DelegateCommand SaveCommand { get; }
        public DelegateCommand ReloadCommand { get; }
        public DelegateCommand ResetCommand { get; }

        private void ExecuteSave() => SaveToSettings();

        private void ExecuteReload()
        {
            _settings.Load();
            LoadFromSettings();
            StatusMessage = "配置已重新加载";
            _logger.Information("配置已重新加载");
        }

        private void ExecuteReset()
        {
            var fresh = new AppSettings();
            _settings.Update(s =>
            {
                s.General = fresh.General;
                s.CameraDefaults = fresh.CameraDefaults;
                s.Plc = fresh.Plc;
                s.Mes = fresh.Mes;
            });
            LoadFromSettings();
            StatusMessage = "已恢复默认配置";
            _logger.Information("配置已重置为默认值");
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _logger.Information("进入设置界面");
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }
    }
}
