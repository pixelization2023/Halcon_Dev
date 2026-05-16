using System.IO;
using System.Net.NetworkInformation;
using Halcon.Core;
using HalconDotNet;
using Inspection.Models;
using MVS.Core;
using PLCModule;
using PLCModule.Interfaces;
using Serilog;

namespace Inspection.Services
{
    /// <summary>自检项</summary>
    public sealed class SelfTestItem
    {
        public string Name { get; init; } = string.Empty;
        public bool Ok { get; init; }
        /// <summary>是否只是"可选/未配置"（不计入失败）</summary>
        public bool Optional { get; init; }
        public string Detail { get; init; } = string.Empty;
        public string? Hint { get; init; }

        public string StatusText => Ok ? "正常" : Optional ? "未配置" : "异常";
        public string Color => Ok ? "#00E676" : Optional ? "#FFAB40" : "#FF5252";

        /// <summary>是否有可显示的处理建议（界面行内展示，不依赖鼠标悬停）</summary>
        public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

        /// <summary>行内展示的建议文本</summary>
        public string HintText => HasHint ? "建议：" + Hint : string.Empty;

        /// <summary>异常项的优先级（用于排序：异常在前、未配置居中、正常在后）</summary>
        public int SortOrder => Ok ? 2 : Optional ? 1 : 0;
    }

    /// <summary>自检报告</summary>
    public sealed class SelfTestReport
    {
        public List<SelfTestItem> Items { get; } = new();
        public DateTime RunAt { get; } = DateTime.Now;

        public int FailCount => Items.Count(i => !i.Ok && !i.Optional);
        public int OkCount => Items.Count(i => i.Ok);
        public int OptionalCount => Items.Count(i => !i.Ok && i.Optional);
        public bool AllPassed => FailCount == 0;

        /// <summary>摘要文本（把"未配置"的可选项单独说明，避免误以为全绿）</summary>
        public string Summary
        {
            get
            {
                var text = FailCount > 0
                    ? $"{FailCount} 项异常"
                    : "无异常项";
                text += $"，{OkCount} 项正常";
                if (OptionalCount > 0) text += $"，{OptionalCount} 项未配置（可选项）";
                return text;
            }
        }
    }

    /// <summary>
    /// 演示数据与自检服务。
    ///
    /// 解决的问题：没有相机 / PLC / MES / 数据库 / 现场方案文件时，
    /// 检测流程、存图、结果梳理、图表、日志这些功能**根本没法验证**。
    /// 这里提供：
    ///   1) 一键生成演示产品（含可用的 XML .hdev 方案文件 + 完整配方配置）
    ///   2) 合成 OK / NG 样张（真实 Halcon 图像），可投入编排器跑完整链路
    ///   3) 依赖自检（Halcon / 方案文件 / PLC 授权 / 存图 / 数据库 / MES / 相机）
    /// </summary>
    public class SelfTestService
    {
        /// <summary>演示产品名称</summary>
        public const string DemoProductName = "演示产品";

        private readonly ProductRepository _repository;
        private readonly HalconInspectionService _halcon;
        private readonly ImageArchiveService _archive;
        private readonly InspectionResultStore _store;
        private readonly IPLCCommunicator _plc;
        private readonly InspectionOrchestrator _orchestrator;
        private readonly ILogger _logger;

        public SelfTestService(ProductRepository repository, HalconInspectionService halcon,
            ImageArchiveService archive, InspectionResultStore store, IPLCCommunicator plc,
            InspectionOrchestrator orchestrator, ILogger logger)
        {
            _repository = repository;
            _halcon = halcon;
            _archive = archive;
            _store = store;
            _plc = plc;
            _orchestrator = orchestrator;
            _logger = logger.ForContext<SelfTestService>();
        }

        #region 演示数据

        /// <summary>演示产品目录是否已就绪</summary>
        public bool DemoProductExists()
            => File.Exists(Path.Combine(_repository.GetProductDirectory(DemoProductName), DemoVisionProgram.FileName));

        /// <summary>
        /// 一键生成演示产品：方案文件（XML .hdev）+ 完整配置（扫码流程 + 检测流程 + 存图路径）。
        /// </summary>
        public ProductConfiguration CreateDemoProduct()
        {
            _repository.CreateProduct(DemoProductName);

            var dir = _repository.GetProductDirectory(DemoProductName);
            var hdevPath = Path.Combine(dir, DemoVisionProgram.FileName);
            File.WriteAllText(hdevPath, DemoVisionProgram.Xml);

            var cfg = new ProductConfiguration
            {
                ProductName = DemoProductName,
                SolutionName = DemoVisionProgram.FileName,
                ImageTotal = 1,
                // 1 张图 1 个 PCS；每个 PCS 的检测项数由配方的 ItemCount 决定，
                // 需要的输出个数 = PcsPerImage × ItemCount（必须与 .hdev 里声明的 out{i} 数量一致）
                SheetPcsTotal = 1,
                CodeCount = 1,
                UploadOrder = new List<string> { "1" },
                VisionProgramFile = hdevPath
            };

            // 两个配方：一个扫码、一个检测（图片索引都绑到第 1 张图，模拟"一张图两 PCS"）
            cfg.Recipes.Add(new InspectionRecipe
            {
                ProcedureName = DemoVisionProgram.ScanProcedure,
                ProcedureFile = DemoVisionProgram.FileName,
                ImageIndexes = "1",
                PcsPerImage = 1,
                ItemCount = 1,
                IsScannerRecipe = true,          // 显式声明，避免被当成检测流程
                CodeOutputParam = "code"
            });

            cfg.Recipes.Add(new InspectionRecipe
            {
                ProcedureName = DemoVisionProgram.InspectProcedure,
                ProcedureFile = DemoVisionProgram.FileName,
                ImageIndexes = "1",
                PcsPerImage = 1,                 // 1 个 PCS
                ItemCount = 2,                   // 2 个检测项 → 需要 out0/out1（与演示方案一致）
                ResultOutputPattern = "out{0}",
                BoxOutputPattern = "box{0}",
                ImageOutputPattern = "image{0}"
            });

            // 演示用：不依赖 MES / 数据库也能跑（默认关掉上传，避免自检时全是失败）
            cfg.GetToggles(SettingsGroups.General).MesProcess = false;
            cfg.GetToggles(SettingsGroups.General).MesData = false;
            cfg.GetToggles(SettingsGroups.Vision).Single = true;
            cfg.GetToggles(SettingsGroups.Vision).WholePcs = true;
            cfg.GetToggles(SettingsGroups.ImageSave).NgImage = true;
            cfg.GetToggles(SettingsGroups.ImageSave).OriginalImage = false;

            var save = cfg.GetImageLocation();
            var root = Path.Combine(AppContext.BaseDirectory, "DemoImages");
            save.NgPath = Path.Combine(root, "NG");
            save.OkPath = Path.Combine(root, "OK");
            save.OriginalPath = Path.Combine(root, "Original");
            save.ReCheckPath = Path.Combine(root, "ReCheck");

            _repository.Save(cfg);
            _logger.Information("演示产品已生成: {Dir}", dir);
            return cfg;
        }

        /// <summary>合成样张：ng = true 生成"有大块亮区"的不良样张，否则良品样张</summary>
        public static HObject CreateSampleImage(bool ng)
        {
            HOperatorSet.GenImageConst(out HObject image, "byte", 800, 600);

            if (ng)
            {
                // 大块亮区 → 面积 >= 500 → out0 = NG
                HOperatorSet.GenRectangle1(out HObject big, 100, 100, 300, 500);
                HOperatorSet.PaintRegion(big, image, out HObject painted, 220, "fill");
                big.Dispose();
                return painted;
            }

            // 良品：只有一个很小的亮点（面积 < 500）
            HOperatorSet.GenRectangle1(out HObject small, 10, 10, 20, 30);
            HOperatorSet.PaintRegion(small, image, out HObject ok, 220, "fill");
            small.Dispose();
            return ok;
        }

        #endregion

        #region 依赖自检

        private SelfTestReport? _pendingReport;

        /// <summary>
        /// Halcon 相关的两项检查（引擎初始化 / 方案文件与过程可加载）。
        /// 首次执行会加载 HALCON 运行时，可能耗时十几秒，因此**只在后台线程调用**，
        /// 否则进入自检页时界面会白屏卡住（实测踩过的坑）。
        /// </summary>
        private void RunHalconChecks(CancellationToken ct)
        {
            var report = new SelfTestReport();
            _pendingReport = report;

            var cfg = _orchestrator.Configuration;

            // 1) Halcon 引擎
            try
            {
                using var engine = new HalconEngine(_logger);
                var ok = engine.InitEngine();
                report.Items.Add(new SelfTestItem
                {
                    Name = "Halcon 引擎",
                    Ok = ok,
                    Detail = ok ? "HDevEngine 初始化成功" : "初始化失败",
                    Hint = ok ? null : "检查 HALCON 是否正确安装、License 是否可用"
                });
            }
            catch (Exception ex)
            {
                report.Items.Add(new SelfTestItem { Name = "Halcon 引擎", Ok = false, Detail = ex.Message });
            }

            // 3) 方案文件 + 过程可加载
            var productName = cfg?.ProductName ?? DemoProductName;
            var programFile = cfg?.VisionProgramFile;
            if (string.IsNullOrWhiteSpace(programFile))
                programFile = Path.Combine(_repository.GetProductDirectory(productName), DemoVisionProgram.FileName);

            var fileExists = File.Exists(programFile);
            var procedures = cfg?.Recipes.Select(r => r.ProcedureName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                             ?? new List<string> { DemoVisionProgram.InspectProcedure };

            var loadDetail = string.Empty;
            var loadOk = false;

            if (fileExists && procedures.Count > 0)
            {
                try
                {
                    using var engine = new HalconEngine(_logger);
                    if (engine.InitEngine())
                    {
                        loadOk = true;
                        foreach (var p in procedures)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (!engine.LoadProcedure(programFile, p))
                            {
                                loadOk = false;
                                loadDetail = $"过程 [{p}] 加载失败";
                                break;
                            }
                        }

                        if (loadOk)
                            loadDetail = $"{Path.GetFileName(programFile)} 的 {procedures.Count} 个过程均可加载";
                    }
                }
                catch (Exception ex)
                {
                    loadDetail = ex.Message;
                }
            }
            else if (!fileExists)
            {
                loadDetail = "未找到方案文件（.hdev）";
            }

            var programOk = fileExists && loadOk;
            report.Items.Add(new SelfTestItem
            {
                Name = "Halcon 方案文件",
                Ok = programOk,
                Detail = fileExists ? loadDetail : $"{programFile} 不存在",
                // 通过时不显示建议，避免噪音
                Hint = programOk
                    ? null
                    : "HALCON 25 的 .hdev 必须是 XML 格式（用 HDevelop 另存为）；可用「生成演示产品」得到一份可用示例"
            });
        }

        /// <summary>运行全部自检项（不抛异常，逐项给出结论与建议）</summary>
        public async Task<SelfTestReport> RunAsync(CancellationToken ct = default)
        {
            // 让调用方（UI 线程）先拿到控制权：下面的 Halcon 检查会加载 HALCON 运行时，首次可能耗时十几秒，
            // 必须放到后台线程，否则界面会白屏卡住（实测踩过的坑）。
            await Task.Yield();
            await Task.Run(() => RunHalconChecks(ct), ct).ConfigureAwait(true);

            var report = _pendingReport!;
            var cfg = _orchestrator.Configuration;

            // 2) 产品配置
            if (cfg == null)
            {
                report.Items.Add(new SelfTestItem
                {
                    Name = "产品配置",
                    Ok = false,
                    Optional = true,
                    Detail = "未加载产品",
                    Hint = "到「产品与方案」加载，或在「自检与仿真」里一键生成演示产品"
                });
            }
            else
            {
                var issues = cfg.Validate();
                report.Items.Add(new SelfTestItem
                {
                    Name = "产品配置",
                    Ok = issues.Count == 0,
                    Detail = issues.Count == 0 ? $"{cfg.ProductName}：校验通过" : string.Join("；", issues),
                    Hint = issues.Count == 0 ? null : "到「检测配置」补齐配方/绑定"
                });
            }

            // 4) HslCommunication 授权（商业组件）
            report.Items.Add(new SelfTestItem
            {
                Name = "PLC 组件授权",
                Ok = HslLicense.IsRegistered,
                Detail = HslLicense.LastMessage,
                Hint = HslLicense.IsRegistered ? null : "在 appsettings.json 的 Plc.HslAuthorizationCode 或环境变量 HSL_AUTH_CODE 填入授权码"
            });

            // 5) PLC 连接（可选）
            report.Items.Add(new SelfTestItem
            {
                Name = "PLC 连接",
                Ok = _plc.IsConnected,
                Optional = true,
                Detail = _plc.IsConnected ? "已连接" : "未连接",
                Hint = _plc.IsConnected ? null : "「PLC 监控」里填 IP/端口后连接（无 PLC 可跳过）"
            });

            // 6) 存图目录可写
            var location = cfg?.GetImageLocation();
            var imageRoot = location?.NgPath;
            if (string.IsNullOrWhiteSpace(imageRoot))
                imageRoot = Path.Combine(AppContext.BaseDirectory, "DemoImages", "NG");

            var writeOk = false;
            var writeDetail = "";
            try
            {
                Directory.CreateDirectory(imageRoot);
                var probe = Path.Combine(imageRoot, $".probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                writeOk = true;
                writeDetail = imageRoot;
            }
            catch (Exception ex)
            {
                writeDetail = $"{imageRoot} 不可写：{ex.Message}";
            }

            report.Items.Add(new SelfTestItem
            {
                Name = "存图目录",
                Ok = writeOk,
                Detail = writeDetail,
                Hint = writeOk ? null : "到「检测设置 → 存图设置」改成有写权限的路径"
            });

            // 7) 数据库（可选）
            await Task.Run(() =>
            {
                var ok = false;
                var detail = "未配置";
                try
                {
                    if (cfg != null)
                    {
                        _store.Configure(cfg.MySql);
                        ok = _store.TestConnection();
                        detail = ok ? $"{cfg.MySql.Ip}:{cfg.MySql.Port}/{cfg.MySql.DatabaseName} 连接成功" : "连接失败";
                    }
                }
                catch (Exception ex) { detail = ex.Message; }

                report.Items.Add(new SelfTestItem
                {
                    Name = "数据库",
                    Ok = ok,
                    Optional = true,
                    Detail = detail,
                    Hint = ok ? null : "「检测设置 → 数据库设置」检查 IP/账号；无数据库可跳过"
                });
            }, ct).ConfigureAwait(false);

            // 8) MES 可达（可选）
            var mesHost = cfg?.Mes.HostIp;
            if (!string.IsNullOrWhiteSpace(mesHost))
            {
                var reachable = false;
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(mesHost, 1500).ConfigureAwait(false);
                    reachable = reply.Status == IPStatus.Success;
                }
                catch { /* 不可达 */ }

                report.Items.Add(new SelfTestItem
                {
                    Name = "MES 主机",
                    Ok = reachable,
                    Optional = true,
                    Detail = reachable ? $"{mesHost} 可达" : $"{mesHost} 不可达",
                    Hint = reachable ? null : "「检测设置 → MES设置」检查主机地址；无 MES 可跳过"
                });
            }

            // 9) 演示方案是否就绪
            report.Items.Add(new SelfTestItem
            {
                Name = "演示方案",
                Ok = DemoProductExists(),
                Optional = true,
                Detail = DemoProductExists() ? $"{DemoProductName}\\{DemoVisionProgram.FileName} 已就绪" : "尚未生成",
                Hint = DemoProductExists() ? null : "点「生成演示产品」即可得到一份可用的示例方案"
            });

            _logger.Information("自检完成：{Ok} 项正常，{Fail} 项异常", report.OkCount, report.FailCount);
            return report;
        }

        #endregion
    }
}
