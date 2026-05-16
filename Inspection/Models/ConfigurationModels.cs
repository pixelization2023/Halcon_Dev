using System.Text.Json.Serialization;

namespace Inspection.Models
{
    /// <summary>
    /// 配置分组常量。原 窗体 项目用中文字符串作为 Dictionary Key 持久化，
    /// 为保证历史配置可读，这里保留原名并额外提供 “视觉设置” 对 “VM设置” 的兼容映射。
    /// </summary>
    public static class SettingsGroups
    {
        public const string General = "通用设置";
        public const string ImageSave = "存图设置";

        /// <summary>视觉流程设置（原项目为 "VM设置"，迁移到 Halcon 后改称 "视觉设置"）</summary>
        public const string Vision = "视觉设置";

        /// <summary>历史分组名，等价于 <see cref="Vision"/></summary>
        public const string VisionLegacy = "VM设置";

        public const string Mes = "MES设置";
        public const string Plc = "通讯设置";
        public const string Database = "数据库设置";
        public const string Camera = "相机设置";

        /// <summary>把历史分组名归一化为当前分组名</summary>
        public static string Normalize(string group)
            => string.Equals(group, VisionLegacy, StringComparison.Ordinal) ? Vision : group;
    }

    /// <summary>
    /// 功能使能开关集合。
    /// 迁移自 窗体.序列化类.Checkclass（原类同时被 "通用设置" / "存图设置" / "VM设置" 三个分组复用）。
    /// </summary>
    public class FeatureToggleSet
    {
        // ---------------- 通用设置 ----------------
        /// <summary>是否启用重复过站检查（调用 MES 工序管控接口）</summary>
        public bool Repeat { get; set; }

        /// <summary>是否上传工序数据</summary>
        public bool MesProcess { get; set; }

        /// <summary>是否上传检测数据</summary>
        public bool MesData { get; set; }

        /// <summary>是否上传统计良率</summary>
        public bool MesYield { get; set; }

        /// <summary>是否上传日报</summary>
        public bool MesDaily { get; set; }

        /// <summary>是否上传样品板数据</summary>
        public bool MesSample { get; set; }

        /// <summary>是否通过 Socket 把条码发送给复判程序</summary>
        public bool ChooseDataUploadsCheck { get; set; }

        /// <summary>是否开启 MES 自动更新</summary>
        public bool ChooseOpenUpdateMes { get; set; }

        /// <summary>是否开启扫码通讯（OpenCoderComm）</summary>
        public bool OpenCoderComm { get; set; }

        /// <summary>是否上传数据库</summary>
        public bool Upload { get; set; }

        // ---------------- 存图设置 ----------------
        /// <summary>是否保存复判图片</summary>
        public bool ReCheckImage { get; set; }

        /// <summary>是否保存原始图片</summary>
        public bool OriginalImage { get; set; }

        /// <summary>是否保存 NG 图片</summary>
        public bool NgImage { get; set; }

        // ---------------- 视觉设置（原 VM 设置） ----------------
        /// <summary>单流程模式</summary>
        public bool Single { get; set; }

        /// <summary>多流程模式</summary>
        public bool Many { get; set; }

        /// <summary>整张 PCS 一起上传</summary>
        public bool WholePcs { get; set; }

        /// <summary>单 PCS 即时上传</summary>
        public bool SinglePcs { get; set; }

        /// <summary>使用镭射码获取机种、LOT 号、品目</summary>
        public bool ProcessAcquisition { get; set; }

        /// <summary>单码单 PCS</summary>
        public bool OneImageOneCode { get; set; }

        /// <summary>多码多 PCS</summary>
        public bool ManyImageOneCode { get; set; }

        /// <summary>流道模式</summary>
        public bool Runners { get; set; }

        /// <summary>单张模式</summary>
        public bool Leaflets { get; set; }

        [JsonIgnore]
        public UploadMode UploadMode =>
            WholePcs || !SinglePcs ? UploadMode.SingleProcess : UploadMode.MultiProcess;
    }

    /// <summary>
    /// 图片保存路径与保留天数。迁移自 窗体.序列化类.PhtoSave。
    /// </summary>
    public class ImageSaveLocation
    {
        /// <summary>原始图保存目录</summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>NG 图保存目录</summary>
        public string NgPath { get; set; } = string.Empty;

        /// <summary>OK 图保存目录</summary>
        public string OkPath { get; set; } = string.Empty;

        /// <summary>复判图保存目录</summary>
        public string ReCheckPath { get; set; } = string.Empty;

        /// <summary>JPEG 压缩质量（原项目 ImageZipPercent，字符串持久化以兼容历史配置）</summary>
        public string ImageZipPercent { get; set; } = "80";

        /// <summary>原始图保留天数（空串表示不清理）</summary>
        public string OriginalRetentionDays { get; set; } = string.Empty;

        /// <summary>NG 图保留天数</summary>
        public string NgRetentionDays { get; set; } = string.Empty;

        /// <summary>复判图保留天数</summary>
        public string ReCheckRetentionDays { get; set; } = string.Empty;

        [JsonIgnore]
        public int ZipQuality
            => int.TryParse(ImageZipPercent, out var q) ? Math.Clamp(q, 1, 100) : 80;
    }

    /// <summary>PLC 设备连接配置（迁移自 窗体.PlcParameter 中的 IP_PLC / Port_PLC / StationNo）</summary>
    public class PlcDeviceConfig
    {
        /// <summary>PLC 名称（原 PlcClass.PLC名称），如 "汇川" / "三菱"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>厂商</summary>
        public PlcVendor Vendor { get; set; } = PlcVendor.Inovance;

        /// <summary>IP 地址</summary>
        public string IpAddress { get; set; } = "192.168.1.10";

        /// <summary>端口</summary>
        public int Port { get; set; } = 502;

        /// <summary>站号</summary>
        public byte StationNumber { get; set; } = 1;

        /// <summary>连接超时（ms）</summary>
        public int ConnectTimeoutMs { get; set; } = 3000;

        /// <summary>读写超时（ms）</summary>
        public int ReadWriteTimeoutMs { get; set; } = 2000;

        public override string ToString() => $"{Name}({Vendor}) {IpAddress}:{Port} #{StationNumber}";
    }

    /// <summary>
    /// PLC 点位定义。
    /// 迁移自 窗体.Plc.inovanceModbusTransform（地址字符串 + 汇川软元件类型 + 三菱地址），
    /// 统一为 "区域 + 偏移" 描述，读写时再拼装成具体协议地址。
    /// </summary>
    public class PlcIoPoint
    {
        /// <summary>点位逻辑名（原 Name），如 "相机1" / "扫码1" / "9"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>软元件区域</summary>
        public PlcAreaType Area { get; set; } = PlcAreaType.M;

        /// <summary>起始地址偏移（原 nStartAddr；999 表示该点位未启用）</summary>
        public int Offset { get; set; } = 999;

        /// <summary>读取/写入的元素个数（原 NCount）</summary>
        public int Count { get; set; } = 1;

        /// <summary>方向</summary>
        public PlcIoDirection Direction { get; set; } = PlcIoDirection.Read;

        /// <summary>三菱协议专用地址（原 MitsubishiResult），为空时按 <see cref="Area"/> + <see cref="Offset"/> 拼装</summary>
        public string VendorAddress { get; set; } = string.Empty;

        /// <summary>
        /// 数据类型。决定读取/写入时解释寄存器的方式：
        /// Bool(位)、Int16/UInt16(1 个寄存器)、Int32/UInt32/Float(2 个寄存器)、
        /// Double(4 个寄存器)、String(按 <see cref="Count"/> 字节长度读取，例如从寄存器读条码)。
        /// </summary>
        public PLCModule.Models.PLCDataType DataType { get; set; } = PLCModule.Models.PLCDataType.Bool;

        /// <summary>点位说明</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>是否为未启用点位</summary>
        [JsonIgnore]
        public bool IsDisabled => Offset >= 999;

        /// <summary>拼装协议地址，例如 "M100" / "D200"</summary>
        public string BuildAddress()
        {
            if (IsDisabled) return string.Empty;
            if (!string.IsNullOrWhiteSpace(VendorAddress)) return VendorAddress.Trim();
            return Area.ToString() + Offset;
        }

        public override string ToString() => $"{Name} -> {BuildAddress()}";
    }

    /// <summary>MES 接口参数（迁移自 窗体.序列化类.MecParameter）</summary>
    public class MesParameter
    {
        /// <summary>员工工号</summary>
        public string OperatorId { get; set; } = string.Empty;

        /// <summary>线别</summary>
        public string LineName { get; set; } = string.Empty;

        /// <summary>机种</summary>
        public string ProductModel { get; set; } = string.Empty;

        /// <summary>FlowID</summary>
        public string FlowId { get; set; } = string.Empty;

        /// <summary>设备编号</summary>
        public string EquipmentId { get; set; } = string.Empty;

        /// <summary>工程 ID</summary>
        public string EngineerId { get; set; } = string.Empty;

        /// <summary>工站 ID</summary>
        public string SubEngineerId { get; set; } = string.Empty;

        /// <summary>机台</summary>
        public string Memo { get; set; } = string.Empty;

        /// <summary>计算机编号</summary>
        public string ComputerNumber { get; set; } = string.Empty;

        /// <summary>MES 内网 IP（用于主界面连通性检测）</summary>
        public string HostIp { get; set; } = string.Empty;

        /// <summary>MES WebAPI 根地址</summary>
        public string BaseUrl { get; set; } = "http://suzapi:9201";

        /// <summary>设备类型（CCD）</summary>
        public string EquipmentType { get; set; } = "CCD";
    }

    /// <summary>样品板参数（迁移自 窗体.序列化类.SampleParameter）</summary>
    public class SampleParameter
    {
        public string SampleNgProject { get; set; } = string.Empty;
        public string SampleNgLocation { get; set; } = string.Empty;
        public string SampleDetectionItems { get; set; } = string.Empty;

        /// <summary>样品板条码</summary>
        public string SampleCode { get; set; } = string.Empty;
    }

    /// <summary>复判 Socket 参数（迁移自 窗体.序列化类.SocketParameter）</summary>
    public class SocketParameter
    {
        public string Ip { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;
        public string RejudicationIp { get; set; } = "127.0.0.1";
        public int RejudicationPort { get; set; } = 9001;
    }

    /// <summary>MySQL 参数（迁移自 窗体.序列化类.MysqlParameter；此处改名以避免与 MySql.Data 的 MySqlParameter 冲突）</summary>
    public class MySqlSettings
    {
        public string Ip { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 3306;
        public string UserName { get; set; } = "root";
        public string Password { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "ccd";

        /// <summary>结果表</summary>
        public string ResultTable { get; set; } = "tb_data1";

        /// <summary>条码表</summary>
        public string CodeTable { get; set; } = "tb_data2";
    }

    /// <summary>相机/扫码枪绑定（迁移自 窗体.序列化类.GlobalJobClass）</summary>
    public class CameraBinding
    {
        /// <summary>逻辑名称，如 "海康相机0"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>绑定顺序</summary>
        public int Order { get; set; }

        /// <summary>设备序列号</summary>
        public string SerialNumber { get; set; } = string.Empty;

        /// <summary>设备种类</summary>
        public DeviceKind Kind { get; set; } = DeviceKind.HikCamera;

        public override string ToString() => $"{Name} [{Kind}] {SerialNumber}";
    }

    /// <summary>
    /// 检测配方：把「Halcon 过程」与「图片索引 / PCS 数 / 检测项数」绑定在一起。
    /// 迁移自 窗体.序列化类.ProPicBuild（检测流程 / Pcs个数 / 检查位置 / 检测项目数）。
    /// </summary>
    public class InspectionRecipe
    {
        /// <summary>HDevelop 过程名（原 "检测流程"；扫码流程约定为 "扫码"）</summary>
        public string ProcedureName { get; set; } = string.Empty;

        /// <summary>所属 .hdev 方案文件（为空时使用产品级方案文件）</summary>
        public string ProcedureFile { get; set; } = string.Empty;

        /// <summary>该流程负责的图片索引（原 "检查位置"，逗号分隔，1 开始）</summary>
        public string ImageIndexes { get; set; } = string.Empty;

        /// <summary>单张图片包含的 PCS 个数（原 "Pcs个数"）</summary>
        public int PcsPerImage { get; set; } = 1;

        /// <summary>单 PCS 的检测项目数（原 "检测项目数"）</summary>
        public int ItemCount { get; set; } = 1;

        /// <summary>输入图像参数名（HDevelop 过程签名中的图标输入）</summary>
        public string InputImageParam { get; set; } = "Image";

        /// <summary>检测结果输出名模板，索引 = pcs * ItemCount + item</summary>
        public string ResultOutputPattern { get; set; } = "out{0}";

        /// <summary>NG 区域（box）输出名模板</summary>
        public string BoxOutputPattern { get; set; } = "box{0}";

        /// <summary>PCS 结果图像输出名模板（可空，为空表示不需要输出图像）</summary>
        public string ImageOutputPattern { get; set; } = "image{0}";

        /// <summary>扫码结果输出参数名（仅扫码流程使用）</summary>
        public string CodeOutputParam { get; set; } = "code";

        /// <summary>附加控制输入参数（变量名 -> 值）</summary>
        public Dictionary<string, string> InputParameters { get; set; } = new();

        /// <summary>
        /// 是否显式声明为扫码流程。
        /// 名字约定（含「扫码」/scan/code）只能算兜底 —— 现场流程名千奇百怪，
        /// 认不出来就会把扫码流程当检测流程跑，凭空多出一个空 PCS（实测踩过的坑）。
        /// </summary>
        public bool IsScannerRecipe { get; set; }

        /// <summary>是否为扫码流程（显式声明优先，其次按名字约定）</summary>
        [JsonIgnore]
        public bool IsCodeRecipe => IsScannerRecipe
                                    || ProcedureName.Contains("扫码", StringComparison.Ordinal)
                                    || ProcedureName.Contains("scan", StringComparison.OrdinalIgnoreCase)
                                    || ProcedureName.Contains("code", StringComparison.OrdinalIgnoreCase);

        /// <summary>解析图片索引集合（1 开始）</summary>
        public HashSet<int> ParseImageIndexes()
        {
            var set = new HashSet<int>();
            foreach (var part in ImageIndexes.Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var v) && v > 0)
                    set.Add(v);
            }
            return set;
        }

        public override string ToString()
            => $"{ProcedureName} (PCS={PcsPerImage}, 项={ItemCount}, 图片={ImageIndexes})";
    }

    /// <summary>
    /// 产品方案配置（顶层）。
    /// 迁移自 窗体.序列化类.SerLion —— 原使用 BinaryFormatter 写入 *.asol，
    /// 现改为 UTF-8 JSON，避免二进制序列化在 .NET 9 下不可用且更易审阅。
    /// </summary>
    public class ProductConfiguration
    {
        /// <summary>配置结构版本，便于后续兼容升级</summary>
        public int SchemaVersion { get; set; } = 2;

        /// <summary>产品名称（原 SolName）</summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>当前选用的方案文件名（原 SolName + CB_SolLoad）</summary>
        public string SolutionName { get; set; } = string.Empty;

        /// <summary>方案目录下可供选择的 .hdev 文件（原 sols）</summary>
        public List<string> SolutionFiles { get; set; } = new();

        /// <summary>产品级 Halcon 方案文件（原 vmpairs 指向的 .sol）</summary>
        public string VisionProgramFile { get; set; } = string.Empty;

        /// <summary>相机拍摄图片总数（原 PicNum）</summary>
        public int ImageTotal { get; set; } = 1;

        /// <summary>整张 PCS 总数（原 UploadNum）</summary>
        public int SheetPcsTotal { get; set; } = 1;

        /// <summary>二维码个数（原 CodeNum）</summary>
        public int CodeCount { get; set; } = 1;

        /// <summary>上传顺序绑定（原 UploadOrderBuild，按真实 PCS 位置顺序）</summary>
        public List<string> UploadOrder { get; set; } = new();

        /// <summary>检测配方列表（原 PicProList）</summary>
        public List<InspectionRecipe> Recipes { get; set; } = new();

        /// <summary>图片索引 -> 二维码索引 绑定（原 ImageCodeBuild，值以 "。" 分隔）</summary>
        public Dictionary<string, string> ImageCodeBinding { get; set; } = new();

        /// <summary>主界面检测项显示（原 DetectionItems）</summary>
        public Dictionary<string, string> DetectionItems { get; set; } = new();

        /// <summary>功能使能：分组名 -> 开关集合（原 checkParmeter）</summary>
        public Dictionary<string, FeatureToggleSet> FeatureToggles { get; set; } = new();

        /// <summary>存图设置：分组名 -> 路径（原 pictrueLocation）</summary>
        public Dictionary<string, ImageSaveLocation> ImageLocations { get; set; } = new();

        /// <summary>PLC 设备：PLC 名称 -> 配置（原 plcpam + plcclas）</summary>
        public Dictionary<string, PlcDeviceConfig> PlcDevices { get; set; } = new();

        /// <summary>PLC 点位：分组名 -> (点位名 -> 点位)（原 ReceivingAndSending）</summary>
        public Dictionary<string, Dictionary<string, PlcIoPoint>> PlcIoGroups { get; set; } = new();

        /// <summary>MES 参数</summary>
        public MesParameter Mes { get; set; } = new();

        /// <summary>样品板参数</summary>
        public SampleParameter Sample { get; set; } = new();

        /// <summary>复判 Socket 参数</summary>
        public SocketParameter Socket { get; set; } = new();

        /// <summary>数据库参数</summary>
        public MySqlSettings MySql { get; set; } = new();

        /// <summary>相机绑定（原 globaljob）</summary>
        public List<CameraBinding> Cameras { get; set; } = new();

        // ------------------------------------------------------------------

        /// <summary>取得分组开关（不存在时创建默认值）</summary>
        public FeatureToggleSet GetToggles(string group)
        {
            var key = SettingsGroups.Normalize(group);
            if (!FeatureToggles.TryGetValue(key, out var set) || set == null)
            {
                set = new FeatureToggleSet();
                FeatureToggles[key] = set;
            }
            return set;
        }

        /// <summary>取得存图设置（不存在时创建默认值）</summary>
        public ImageSaveLocation GetImageLocation(string group = SettingsGroups.ImageSave)
        {
            var key = SettingsGroups.Normalize(group);
            if (!ImageLocations.TryGetValue(key, out var loc) || loc == null)
            {
                loc = new ImageSaveLocation();
                ImageLocations[key] = loc;
            }
            return loc;
        }

        /// <summary>取得点位分组（不存在时创建空分组）</summary>
        public Dictionary<string, PlcIoPoint> GetIoGroup(string group)
        {
            if (!PlcIoGroups.TryGetValue(group, out var dict) || dict == null)
            {
                dict = new Dictionary<string, PlcIoPoint>();
                PlcIoGroups[group] = dict;
            }
            return dict;
        }

        /// <summary>取第一个 PLC 设备</summary>
        [JsonIgnore]
        public PlcDeviceConfig? PrimaryPlc
            => PlcDevices.Count > 0 ? PlcDevices.Values.First() : null;

        /// <summary>校验配置完整性，返回问题描述列表</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(ProductName))
                issues.Add("产品名称为空");
            if (ImageTotal <= 0)
                issues.Add("图片总数必须大于 0");
            if (SheetPcsTotal <= 0)
                issues.Add("整张 PCS 数必须大于 0");
            if (Recipes.Count == 0)
                issues.Add("尚未配置任何检测配方");
            if (Recipes.Count > 0 && !Recipes.Any(r => r.IsCodeRecipe))
                issues.Add("未找到扫码流程（过程名需包含「扫码」）");
            if (string.IsNullOrWhiteSpace(VisionProgramFile))
                issues.Add("未指定 Halcon 方案文件（.hdev）");

            foreach (var recipe in Recipes)
            {
                if (recipe.PcsPerImage <= 0)
                    issues.Add($"配方 {recipe.ProcedureName}: PCS 个数必须大于 0");
                if (recipe.ItemCount <= 0)
                    issues.Add($"配方 {recipe.ProcedureName}: 检测项目数必须大于 0");
                if (recipe.ParseImageIndexes().Count == 0)
                    issues.Add($"配方 {recipe.ProcedureName}: 未绑定任何图片索引");
            }

            var boundIndexes = Recipes.SelectMany(r => r.ParseImageIndexes()).Distinct().Count();
            if (ImageTotal > 0 && boundIndexes != ImageTotal)
                issues.Add($"图片索引绑定不完整：总图片 {ImageTotal} 张，已绑定 {boundIndexes} 张");

            return issues;
        }
    }
}
