using System.Collections.ObjectModel;
using Inspection.Models;
using Inspection.Services;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Navigation;
using Serilog;

namespace Inspection.ViewModels
{
    /// <summary>
    /// PLC 点位编辑行。
    /// 原来点位只能改偏移，**数据类型与个数没法配**；
    /// 而"读多少个寄存器"恰恰取决于类型（Int16=1、Int32/Float=2、Double=4、String=N），
    /// 所以这里把类型与个数一并暴露成可编辑列。
    /// </summary>
    public class PlcPointRow : BindableBase
    {
        public PlcPointRow(string group, PlcIoPoint point)
        {
            Group = group;
            Point = point;
        }

        public string Group { get; }

        public PlcIoPoint Point { get; }

        public string Name => Point.Name;

        /// <summary>拼装出来的实际协议地址（未启用时给出提示）</summary>
        public string Address => Point.IsDisabled ? "（未启用）" : Point.BuildAddress();

        public PlcAreaType Area
        {
            get => Point.Area;
            set { Point.Area = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Address)); }
        }

        public int Offset
        {
            get => Point.Offset;
            set { Point.Offset = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Address)); }
        }

        public PLCModule.Models.PLCDataType DataType
        {
            get => Point.DataType;
            set { Point.DataType = value; RaisePropertyChanged(); }
        }

        public int Count
        {
            get => Point.Count;
            set { Point.Count = value; RaisePropertyChanged(); }
        }

        public PlcIoDirection Direction
        {
            get => Point.Direction;
            set { Point.Direction = value; RaisePropertyChanged(); }
        }

        public string Description
        {
            get => Point.Description;
            set { Point.Description = value; RaisePropertyChanged(); }
        }
    }

    /// <summary>
    /// 系统设置。
    /// 迁移自 窗体.UI.FrmSetUp 的 "通用设置 / 存图设置 / MES设置 / 通讯设置 / 数据库设置" 五个页签，
    /// 以及 ControlActions 那套基于反射 + GroupBox 遍历的动态控件绑定 —— 这里改为强类型属性绑定。
    /// </summary>
    public class SystemSettingsViewModel : BindableBase, INavigationAware
    {
        private readonly InspectionOrchestrator _orchestrator;
        private readonly InspectionResultStore _store;
        private readonly PlcIoService _plc;
        private readonly TraceSocketService _trace;
        private readonly ProductRepository _repository;
        private readonly UserSessionService _session;

        /// <summary>
        /// 文件夹选择服务。
        /// 解耦要点：不再直接 <c>new OpenFolderDialog()</c> —— ViewModel 不依赖 UI 类型，
        /// 且测试里可以替换（见 <see cref="MVS.Core.IFileDialogService"/>）。
        /// </summary>
        private readonly MVS.Core.IFileDialogService _fileDialogs;

        private readonly ILogger _logger;

        public SystemSettingsViewModel(InspectionOrchestrator orchestrator, InspectionResultStore store,
            PlcIoService plc, TraceSocketService trace, ProductRepository repository,
            UserSessionService session, MVS.Core.IFileDialogService fileDialogs, ILogger logger)
        {
            _orchestrator = orchestrator;
            _store = store;
            _plc = plc;
            _trace = trace;
            _repository = repository;
            _session = session;
            _fileDialogs = fileDialogs;
            _logger = logger.ForContext<SystemSettingsViewModel>();

            SaveCommand = new DelegateCommand(ExecuteSave);
            TestDbCommand = new DelegateCommand(ExecuteTestDb);
            EnsureSchemaCommand = new DelegateCommand(ExecuteEnsureSchema);
            ConnectPlcCommand = new DelegateCommand(async () => await ExecuteConnectPlcAsync());
            StartSocketCommand = new DelegateCommand(ExecuteStartSocket);
            QueryAllCommand = new DelegateCommand(ExecuteQueryAll);
            QueryByTimeCommand = new DelegateCommand(ExecuteQueryByTime);
            QueryByLotCommand = new DelegateCommand(ExecuteQueryByLot);
            QueryByLaserCodeCommand = new DelegateCommand(ExecuteQueryByLaserCode);
            DeleteRecordCommand = new DelegateCommand<InspectionRecord>(ExecuteDeleteRecord);
            BrowseOriginalCommand = new DelegateCommand(() => BrowseInto(l => l.OriginalPath = BrowseFolder(l.OriginalPath)));
            BrowseNgCommand = new DelegateCommand(() => BrowseInto(l => l.NgPath = BrowseFolder(l.NgPath)));
            BrowseOkCommand = new DelegateCommand(() => BrowseInto(l => l.OkPath = BrowseFolder(l.OkPath)));
            BrowseReCheckCommand = new DelegateCommand(() => BrowseInto(l => l.ReCheckPath = BrowseFolder(l.ReCheckPath)));
            AddPlcCommand = new DelegateCommand(ExecuteAddPlc);
            RemovePlcCommand = new DelegateCommand<PlcDeviceConfig>(ExecuteRemovePlc);
        }

        #region 集合

        public ObservableCollection<PlcDeviceConfig> PlcDevices { get; } = new();

        /// <summary>全部 PLC 点位（按分组展开），可编辑类型与个数</summary>
        public ObservableCollection<PlcPointRow> PlcPoints { get; } = new();

        public IReadOnlyList<PlcAreaType> PlcAreas { get; } = Enum.GetValues<PlcAreaType>();
        public IReadOnlyList<PLCModule.Models.PLCDataType> PlcDataTypes { get; } = Enum.GetValues<PLCModule.Models.PLCDataType>();
        public IReadOnlyList<PlcIoDirection> PlcDirections { get; } = Enum.GetValues<PlcIoDirection>();
        public ObservableCollection<InspectionRecord> Records { get; } = new();
        public IReadOnlyList<PlcVendor> PlcVendors { get; } = new[] { PlcVendor.Inovance, PlcVendor.Mitsubishi, PlcVendor.Siemens, PlcVendor.ModbusTcp };

        #endregion

        #region 配置对象

        public FeatureToggleSet GeneralToggles => GetToggles(SettingsGroups.General);
        public FeatureToggleSet ImageToggles => GetToggles(SettingsGroups.ImageSave);
        public FeatureToggleSet VisionToggles => GetToggles(SettingsGroups.Vision);

        public ImageSaveLocation ImageLocation => Configuration?.GetImageLocation() ?? new ImageSaveLocation();
        public MesParameter Mes => Configuration?.Mes ?? new MesParameter();
        public MySqlSettings MySql => Configuration?.MySql ?? new MySqlSettings();
        public SocketParameter Socket => Configuration?.Socket ?? new SocketParameter();
        public SampleParameter Sample => Configuration?.Sample ?? new SampleParameter();

        private ProductConfiguration? Configuration => _orchestrator.Configuration;

        private FeatureToggleSet GetToggles(string group)
            => Configuration?.GetToggles(group) ?? new FeatureToggleSet();

        #endregion

        #region 绑定属性

        private string _queryLot = string.Empty;
        public string QueryLot
        {
            get => _queryLot;
            set => SetProperty(ref _queryLot, value);
        }

        private string _queryLaserCode = string.Empty;
        public string QueryLaserCode
        {
            get => _queryLaserCode;
            set => SetProperty(ref _queryLaserCode, value);
        }

        private DateTime _queryFrom = DateTime.Today.AddDays(-1);
        public DateTime QueryFrom
        {
            get => _queryFrom;
            set => SetProperty(ref _queryFrom, value);
        }

        private DateTime _queryTo = DateTime.Now;
        public DateTime QueryTo
        {
            get => _queryTo;
            set => SetProperty(ref _queryTo, value);
        }

        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        /// <summary>权限：仅工程师及以上可修改全部页签（对应原 FrmSetUp 的页签过滤）</summary>
        public bool CanEditGeneral => _session.CanEditGroup(SettingsGroups.General);
        public bool CanEditImageSave => _session.CanEditGroup(SettingsGroups.ImageSave);
        public bool CanEditMes => _session.CanEditGroup(SettingsGroups.Mes);
        public bool CanEditVision => _session.CanEditGroup(SettingsGroups.Vision);
        public bool CanEditPlc => _session.CanEditGroup(SettingsGroups.Plc);

        /// <summary>PLC 连接状态</summary>
        public bool IsPlcConnected => _plc.IsConnected;

        /// <summary>复判 Socket 状态</summary>
        public bool IsSocketListening => _trace.IsListening;

        #endregion

        #region 命令

        public DelegateCommand SaveCommand { get; }
        public DelegateCommand TestDbCommand { get; }
        public DelegateCommand EnsureSchemaCommand { get; }
        public DelegateCommand ConnectPlcCommand { get; }
        public DelegateCommand StartSocketCommand { get; }
        public DelegateCommand QueryAllCommand { get; }
        public DelegateCommand QueryByTimeCommand { get; }
        public DelegateCommand QueryByLotCommand { get; }
        public DelegateCommand QueryByLaserCodeCommand { get; }
        public DelegateCommand<InspectionRecord> DeleteRecordCommand { get; }
        public DelegateCommand BrowseOriginalCommand { get; }
        public DelegateCommand BrowseNgCommand { get; }
        public DelegateCommand BrowseOkCommand { get; }
        public DelegateCommand BrowseReCheckCommand { get; }
        public DelegateCommand AddPlcCommand { get; }
        public DelegateCommand<PlcDeviceConfig> RemovePlcCommand { get; }

        private void ExecuteSave()
        {
            var cfg = Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            StatusText = _repository.Save(cfg) ? "设置已保存" : "保存失败";
        }

        private void ExecuteTestDb()
        {
            var ok = _store.TestConnection();
            StatusText = ok ? "数据库连接成功！" : "数据库连接失败！";
        }

        private void ExecuteEnsureSchema()
        {
            var ok = _store.TestConnection() && _store.EnsureSchema();
            StatusText = ok ? "数据库表已就绪" : "建表失败，请检查连接参数";
        }

        private async Task ExecuteConnectPlcAsync()
        {
            var cfg = Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            IsBusy = true;
            try
            {
                var ok = await _plc.ConnectAsync(cfg);
                if (ok)
                {
                    _plc.StartMonitor();
                    StatusText = "PLC连接成功！";
                }
                else
                {
                    StatusText = "PLC连接失败！";
                }

                RaisePropertyChanged(nameof(IsPlcConnected));
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteStartSocket()
        {
            var cfg = Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            var ok = _trace.Start(cfg.Socket);
            StatusText = ok ? "复判 Socket 服务器已开启！" : "复判 Socket 服务器开启失败（可能端口被占用）";
            RaisePropertyChanged(nameof(IsSocketListening));
        }

        private void ExecuteQueryAll() => RunQuery(() => _store.QueryAll(), "全部数据");

        private void ExecuteQueryByTime() => RunQuery(() => _store.QueryByTime(QueryFrom, QueryTo), "时间范围");

        private void ExecuteQueryByLot() => RunQuery(() => _store.QueryByLot(QueryLot), "LOT");

        private void ExecuteQueryByLaserCode() => RunQuery(() => _store.QueryByLaserCode(QueryLaserCode), "镭射码");

        private void RunQuery(Func<List<InspectionRecord>> query, string label)
        {
            IsBusy = true;
            try
            {
                var list = query();
                Records.Clear();
                foreach (var record in list) Records.Add(record);
                StatusText = $"{label}查询完成：{list.Count} 条";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteDeleteRecord(InspectionRecord? record)
        {
            if (record == null) return;

            if (_store.DeleteById(record.Index))
            {
                Records.Remove(record);
                StatusText = $"已删除记录 #{record.Index}";
            }
            else
            {
                StatusText = "删除失败";
            }
        }

        private void ExecuteAddPlc()
        {
            var cfg = Configuration;
            if (cfg == null) { StatusText = "请先加载产品"; return; }

            var device = new PlcDeviceConfig { Name = "PLC" + (cfg.PlcDevices.Count + 1) };
            cfg.PlcDevices[device.Name] = device;
            PlcDevices.Add(device);
            StatusText = "已添加 PLC 设备";
        }

        private void ExecuteRemovePlc(PlcDeviceConfig? device)
        {
            if (device == null) return;

            Configuration?.PlcDevices.Remove(device.Name);
            PlcDevices.Remove(device);
            StatusText = "已移除 PLC 设备";
        }

        #endregion

        #region 辅助

        private void BrowseInto(Action<ImageSaveLocation> apply)
        {
            apply(ImageLocation);
            RaisePropertyChanged(nameof(ImageLocation));
        }

        /// <summary>
        /// 选择文件夹（经 <see cref="MVS.Core.IFileDialogService"/>，不再直接 new OpenFolderDialog）。
        /// 用户取消时返回原值。
        /// </summary>
        private string BrowseFolder(string current)
            => _fileDialogs.PickFolder("选择文件夹", current) ?? current;

        /// <summary>重新展开点位表</summary>
        private void ReloadPlcPoints()
        {
            PlcPoints.Clear();
            var cfg = Configuration;
            if (cfg == null) return;

            foreach (var group in cfg.PlcIoGroups)
                foreach (var point in group.Value.Values)
                    PlcPoints.Add(new PlcPointRow(group.Key, point));
        }

        public void Reload()
        {
            PlcDevices.Clear();
            var cfg = Configuration;
            if (cfg != null)
            {
                foreach (var device in cfg.PlcDevices.Values) PlcDevices.Add(device);
                ReloadPlcPoints();
                StatusText = $"当前产品：{cfg.ProductName}";
            }
            else
            {
                StatusText = "请先加载产品";
            }

            RaisePropertyChanged(nameof(GeneralToggles));
            RaisePropertyChanged(nameof(ImageToggles));
            RaisePropertyChanged(nameof(VisionToggles));
            RaisePropertyChanged(nameof(ImageLocation));
            RaisePropertyChanged(nameof(Mes));
            RaisePropertyChanged(nameof(MySql));
            RaisePropertyChanged(nameof(Socket));
            RaisePropertyChanged(nameof(Sample));
            RaisePropertyChanged(nameof(IsPlcConnected));
            RaisePropertyChanged(nameof(IsSocketListening));
        }

        #endregion

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext) => Reload();

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;

        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        #endregion
    }
}
