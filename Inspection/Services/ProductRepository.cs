using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inspection.Models;
using PLCModule.Models;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 产品方案仓库。
    /// 迁移自 窗体.序列化类.SolModel（目录扫描 / 新建产品 / 读取方案列表）以及 SerLion.Savesol / fanBaseSer。
    /// 关键改动：用 UTF-8 JSON 取代 BinaryFormatter(*.asol)，因为二进制序列化在 .NET 9 已被移除。
    /// </summary>
    public class ProductRepository
    {
        private readonly ILogger _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Halcon 方案文件扩展名（替代原 .sol）</summary>
        private static readonly string[] VisionExtensions = { ".hdev", ".hdvp" };

        public ProductRepository(ILogger logger)
        {
            _logger = logger.ForContext<ProductRepository>();

            ProductRoot = Path.Combine(AppContext.BaseDirectory, "Product");
            if (!Directory.Exists(ProductRoot))
                Directory.CreateDirectory(ProductRoot);
        }

        /// <summary>产品根目录（原 Application.StartupPath + "\Product"）</summary>
        public string ProductRoot { get; }

        /// <summary>取得产品目录</summary>
        public string GetProductDirectory(string productName)
            => Path.Combine(ProductRoot, productName);

        /// <summary>取得配置文件路径</summary>
        public string GetConfigPath(string productName)
            => Path.Combine(GetProductDirectory(productName), productName + ".json");

        /// <summary>枚举所有产品（原 SolModel.readSolDir）</summary>
        public IReadOnlyList<string> GetProducts()
        {
            if (!Directory.Exists(ProductRoot)) return Array.Empty<string>();

            return Directory.GetDirectories(ProductRoot)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>新建产品目录（原 SolModel.CreateProduct）</summary>
        public bool CreateProduct(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
            {
                _logger.Warning("新建产品失败：名称为空");
                return false;
            }

            try
            {
                var dir = GetProductDirectory(productName.Trim());
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _logger.Information("新建产品成功: {Product}", productName);
                return Directory.Exists(dir);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "新建产品失败: {Product}", productName);
                return false;
            }
        }

        /// <summary>读取产品目录下的 Halcon 方案文件（原 SolModel.readVMSol）</summary>
        public List<string> GetVisionProgramFiles(string productName)
        {
            var dir = GetProductDirectory(productName);
            if (!Directory.Exists(dir)) return new List<string>();

            return Directory.GetFiles(dir)
                .Where(f => VisionExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>创建默认产品配置</summary>
        public ProductConfiguration CreateDefault(string productName)
        {
            var cfg = new ProductConfiguration
            {
                ProductName = productName,
                ImageTotal = 1,
                SheetPcsTotal = 1,
                CodeCount = 1
            };

            // 默认点位分组（键名沿用原项目约定，保证 PLC 报警/复位流程可复用）
            var cameraReady = cfg.GetIoGroup(PlcIoGroups.CameraReady);
            var otherSend = cfg.GetIoGroup(PlcIoGroups.OtherSend);
            var cameraTrigger = cfg.GetIoGroup(PlcIoGroups.CameraTrigger);
            var otherTrigger = cfg.GetIoGroup(PlcIoGroups.OtherTrigger);

            // M 位元件 → Bool；D 字元件 → UInt16（读取/写入时按声明的类型解释寄存器）
            for (int i = 1; i <= 4; i++)
            {
                cameraReady["相机" + i] = new PlcIoPoint { Name = "相机" + i, Area = PlcAreaType.M, Offset = 100 + i, Direction = PlcIoDirection.Write, DataType = PLCDataType.Bool };
                cameraTrigger["相机" + i] = new PlcIoPoint { Name = "相机" + i, Area = PlcAreaType.M, Offset = 200 + i, Direction = PlcIoDirection.Read, DataType = PLCDataType.Bool };
            }
            cameraReady["扫码1"] = new PlcIoPoint { Name = "扫码1", Area = PlcAreaType.M, Offset = 105, Direction = PlcIoDirection.Write, DataType = PLCDataType.Bool };
            cameraTrigger["扫码1"] = new PlcIoPoint { Name = "扫码1", Area = PlcAreaType.M, Offset = 205, Direction = PlcIoDirection.Read, DataType = PLCDataType.Bool };

            // 其他点位（与原项目下标严格一致）
            otherSend["5"] = new PlcIoPoint { Name = "5", Description = "未扫描到二维码", Offset = 300, Direction = PlcIoDirection.Write, DataType = PLCDataType.Bool };
            otherSend["8"] = new PlcIoPoint { Name = "8", Description = "重复过站/已检测", Offset = 301, Direction = PlcIoDirection.Write, DataType = PLCDataType.Bool };
            otherSend["9"] = new PlcIoPoint { Name = "9", Description = "数据上传完成", Offset = 302, Direction = PlcIoDirection.Write, DataType = PLCDataType.Bool };
            otherSend["10"] = new PlcIoPoint { Name = "10", Description = "复位完成", Offset = 303, Direction = PlcIoDirection.Write, DataType = PLCDataType.Bool };

            otherTrigger["8"] = new PlcIoPoint { Name = "8", Description = "心跳", Offset = 400, Direction = PlcIoDirection.Read, DataType = PLCDataType.Bool };
            otherTrigger["10"] = new PlcIoPoint { Name = "10", Description = "PLC 复位请求", Offset = 401, Direction = PlcIoDirection.Read, DataType = PLCDataType.Bool };

            cfg.PlcDevices["汇川"] = new PlcDeviceConfig { Name = "汇川", Vendor = PlcVendor.Inovance, IpAddress = "192.168.1.10", Port = 502, StationNumber = 1 };

            return cfg;
        }

        /// <summary>加载产品配置；不存在时返回 null</summary>
        public ProductConfiguration? Load(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return null;

            var path = GetConfigPath(productName);
            if (!File.Exists(path))
            {
                _logger.Warning("产品配置不存在: {Path}", path);
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<ProductConfiguration>(json, JsonOptions);
                if (cfg == null)
                {
                    _logger.Error("产品配置反序列化为空: {Path}", path);
                    return null;
                }

                cfg.ProductName = productName;
                LocateVisionProgram(cfg);

                // v2 老配置没有 Display 字段：反序列化后它是默认值（窗口数 4、窗口定义 0 条），
                // 必须补齐窗口列表，否则界面会出现"有 4 个窗口但没有任何绑定规则"的错位状态。
                cfg.NormalizeDisplay();

                _logger.Information("产品配置已加载: {Product}", productName);
                return cfg;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载产品配置失败: {Path}", path);
                return null;
            }
        }

        /// <summary>保存产品配置（原 SerLion.Savesol）</summary>
        public bool Save(ProductConfiguration cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.ProductName))
            {
                _logger.Warning("保存产品配置失败：配置或产品名为空");
                return false;
            }

            try
            {
                var dir = GetProductDirectory(cfg.ProductName);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var path = GetConfigPath(cfg.ProductName);
                File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOptions));

                _logger.Information("产品配置已保存: {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存产品配置失败: {Product}", cfg.ProductName);
                return false;
            }
        }

        /// <summary>把配置中的方案文件名解析为绝对路径</summary>
        public void LocateVisionProgram(ProductConfiguration cfg)
        {
            var dir = GetProductDirectory(cfg.ProductName);
            cfg.SolutionFiles = GetVisionProgramFiles(cfg.ProductName);

            if (!string.IsNullOrWhiteSpace(cfg.SolutionName))
            {
                var candidate = Path.Combine(dir, cfg.SolutionName);
                if (File.Exists(candidate))
                {
                    cfg.VisionProgramFile = candidate;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(cfg.VisionProgramFile) && File.Exists(cfg.VisionProgramFile))
                return;

            cfg.VisionProgramFile = cfg.SolutionFiles.Count > 0
                ? Path.Combine(dir, cfg.SolutionFiles[0])
                : string.Empty;
        }

        /// <summary>按名称解析配方所属的 Halcon 方案文件绝对路径</summary>
        public string ResolveRecipeFile(ProductConfiguration cfg, InspectionRecipe recipe)
        {
            if (!string.IsNullOrWhiteSpace(recipe.ProcedureFile))
            {
                var explicitPath = Path.IsPathRooted(recipe.ProcedureFile)
                    ? recipe.ProcedureFile
                    : Path.Combine(GetProductDirectory(cfg.ProductName), recipe.ProcedureFile);

                if (File.Exists(explicitPath)) return explicitPath;
            }

            if (!string.IsNullOrWhiteSpace(cfg.VisionProgramFile))
                return cfg.VisionProgramFile;

            LocateVisionProgram(cfg);
            return cfg.VisionProgramFile;
        }

        /// <summary>发现产品目录中可用的过程名（供 UI 下拉选择）</summary>
        public List<string> DiscoverProcedureNames(string productName)
        {
            var names = new List<string>();
            var engine = new Halcon.Core.HalconEngine();
            try
            {
                if (!engine.InitEngine()) return names;

                foreach (var file in GetVisionProgramFiles(productName))
                {
                    var full = Path.Combine(GetProductDirectory(productName), file);
                    if (!engine.LoadProgram(full)) continue;

                    // 主程序模式下无法枚举本地函数，这里仅登记文件名（去扩展名）作为候选
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!names.Contains(name)) names.Add(name);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "枚举 Halcon 过程名失败: {Product}", productName);
            }
            finally
            {
                engine.Dispose();
            }

            return names;
        }
    }
}
