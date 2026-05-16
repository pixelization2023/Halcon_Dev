using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalconDotNet;
using Serilog;

namespace Halcon.Core
{
    /// <summary>
    /// Halcon 脚本引擎封装（迁移自 WinForms 窗体项目中对 VisionMaster 流程的调用方式）。
    ///
    /// 支持两种执行模式，对应 HDevelop 的两种导出形态：
    /// <list type="bullet">
    /// <item><b>主程序模式</b>：加载 .hdev / .hdvp 主程序（HDevProgram + HDevProgramCall），
    /// 只能执行与取结果，输入需通过 global 变量注入。</item>
    /// <item><b>外部过程模式</b>：加载 .hdev 文件中的本地函数（HDevProcedure + HDevProcedureCall），
    /// 支持按名字设置/读取输入输出参数，是检测流程推荐的使用方式。</item>
    /// </list>
    /// 注意：原实现只创建了 <see cref="HDevProcedureCall"/>，却用它去执行主程序，
    /// 因此 .hdvp 主程序路径实际上是失效的，此处一并修复。
    /// </summary>
    public sealed class HalconEngine : IHalconEngine, IDisposable
    {
        #region 字段

        private readonly ILogger _logger;
        private readonly object _sync = new();

        /// <summary>Halcon 引擎单例</summary>
        private HDevEngine? _engine;

        /// <summary>主程序模式：程序与执行上下文</summary>
        private HDevProgram? _program;
        private HDevProgramCall? _programCall;

        /// <summary>外部过程模式：过程与执行上下文</summary>
        private HDevProcedure? _procedure;
        private HDevProcedureCall? _procedureCall;

        /// <summary>当前已加载的资源标识（用于避免重复加载）</summary>
        private string? _loadedKey;

        private bool _isDisposed;

        #endregion

        #region 属性

        /// <summary>引擎是否初始化完成</summary>
        public bool IsEngineInitialized => _engine != null && _engine.IsInitialized();

        /// <summary>程序/函数是否加载完成</summary>
        public bool IsContentLoaded => _programCall != null || _procedureCall != null;

        /// <summary>当前是否处于外部过程模式（可读写命名参数）</summary>
        public bool IsProcedureMode => _procedureCall != null;

        /// <summary>当前加载的资源标识</summary>
        public string? LoadedKey => _loadedKey;

        #endregion

        public HalconEngine()
        {
            _logger = ResolveLogger();
        }

        public HalconEngine(ILogger? logger)
        {
            _logger = logger ?? ResolveLogger();
        }

        private static ILogger ResolveLogger()
        {
            try
            {
                return MVS.Core.AppContainer.Resolve<ILogger>() ?? Serilog.Log.Logger;
            }
            catch
            {
                // 容器尚未初始化（例如单元测试或设计期），退回到全局静态 logger
                return Serilog.Log.Logger;
            }
        }

        #region 引擎初始化与配置

        /// <summary>初始化引擎，设置脚本搜索路径（可选）</summary>
        public void Initialize() => InitEngine();

        /// <summary>初始化 Halcon 引擎</summary>
        /// <param name="procedurePath">外部函数目录路径（可选，有 hdev 外部函数时必须设置）</param>
        public bool InitEngine(string procedurePath = "")
        {
            lock (_sync)
            {
                try
                {
                    if (_engine == null)
                    {
                        _engine = new HDevEngine();
                    }

                    if (!string.IsNullOrWhiteSpace(procedurePath) && Directory.Exists(procedurePath))
                    {
                        _engine.SetProcedurePath(procedurePath);
                        _logger.Information("Halcon 外部函数搜索路径已设置: {Path}", procedurePath);
                    }

                    _logger.Information("Halcon 引擎初始化成功");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Halcon 引擎初始化失败");
                    return false;
                }
            }
        }

        /// <summary>追加外部函数搜索路径</summary>
        public bool AddProcedurePath(string procedurePath)
        {
            if (!IsEngineInitialized)
            {
                _logger.Warning("尝试添加函数路径，但引擎未初始化");
                return false;
            }

            try
            {
                _engine!.AddProcedurePath(procedurePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "添加函数路径失败: {Path}", procedurePath);
                return false;
            }
        }

        #endregion

        #region 程序 / 过程加载

        /// <summary>
        /// 加载 HDevelop 主程序（.hdev / .hdvp）。
        /// 主程序的输入变量需通过 <see cref="SetGlobalIconicParam"/> / <see cref="SetGlobalCtrlParam"/> 注入，
        /// 输出变量通过 <see cref="GetOutputIconicParam"/> / <see cref="GetOutputCtrlParam"/> 读取。
        /// </summary>
        public bool LoadProgram(string programPath)
        {
            if (!IsEngineInitialized)
            {
                _logger.Warning("尝试加载 Halcon 主程序，但引擎未初始化");
                return false;
            }

            if (string.IsNullOrWhiteSpace(programPath) || !File.Exists(programPath))
            {
                _logger.Warning("Halcon 主程序文件不存在: {Path}", programPath);
                return false;
            }

            lock (_sync)
            {
                try
                {
                    UnloadContentCore();

                    _program = new HDevProgram(programPath);
                    _programCall = new HDevProgramCall(_program);
                    _loadedKey = programPath;

                    _logger.Information("Halcon 主程序加载成功: {File}", Path.GetFileName(programPath));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error("Halcon 主程序加载失败: {Path} —— {Message}", programPath, ex.Message);
                    _loadedKey = null;
                    return false;
                }
            }
        }

        /// <summary>兼容旧 API：加载 hdvp / hdev 主程序</summary>
        public bool LoadHdvpProgram(string hdvpFilePath) => LoadProgram(hdvpFilePath);

        /// <summary>
        /// 从 .hdev 文件中加载一个本地函数（外部过程），并创建可读写命名参数的执行上下文。
        /// </summary>
        /// <param name="hdevFilePath">.hdev 文件路径</param>
        /// <param name="procedureName">文件内的本地函数名（procedure）</param>
        public bool LoadProcedure(string hdevFilePath, string procedureName)
        {
            if (!IsEngineInitialized)
            {
                _logger.Warning("尝试加载 Halcon 过程，但引擎未初始化");
                return false;
            }

            if (string.IsNullOrWhiteSpace(hdevFilePath) || !File.Exists(hdevFilePath))
            {
                _logger.Warning("Halcon 过程文件不存在: {Path}", hdevFilePath);
                return false;
            }

            if (string.IsNullOrWhiteSpace(procedureName))
            {
                _logger.Warning("Halcon 过程名为空: {Path}", hdevFilePath);
                return false;
            }

            lock (_sync)
            {
                try
                {
                    UnloadContentCore();

                    // 同一个 .hdev 文件可包含多个本地函数，缓存程序对象以便复用
                    _program = new HDevProgram(hdevFilePath);
                    _procedure = new HDevProcedure(_program, procedureName);
                    _procedureCall = new HDevProcedureCall(_procedure);
                    _loadedKey = hdevFilePath + "::" + procedureName;

                    _logger.Information("Halcon 过程加载成功: {File}::{Procedure}",
                        Path.GetFileName(hdevFilePath), procedureName);
                    return true;
                }
                catch (Exception ex)
                {
                    // 把 HALCON 的原始错误信息带出来（否则只看到"加载失败"，无法定位语法问题）
                    _logger.Error("Halcon 过程加载失败: {File}::{Procedure} —— {Message}",
                        hdevFilePath, procedureName, ex.Message);
                    _loadedKey = null;
                    return false;
                }
            }
        }

        /// <summary>兼容旧 API：从 hdev 文件加载本地函数</summary>
        public bool LoadHdevProcedure(string hdevFilePath, string procedureName)
            => LoadProcedure(hdevFilePath, procedureName);

        /// <summary>
        /// 通过引擎的“外部函数搜索路径”按名字加载外部过程（.hdvp）。
        /// 需先调用 <see cref="AddProcedurePath"/> 或 <see cref="InitEngine"/> 指定目录。
        /// </summary>
        public bool LoadExternalProcedure(string procedureName)
        {
            if (!IsEngineInitialized)
            {
                _logger.Warning("尝试加载 Halcon 外部过程，但引擎未初始化");
                return false;
            }

            lock (_sync)
            {
                try
                {
                    UnloadContentCore();

                    _procedure = new HDevProcedure(procedureName);
                    _procedureCall = new HDevProcedureCall(_procedure);
                    _loadedKey = procedureName;

                    _logger.Information("Halcon 外部过程加载成功: {Procedure}", procedureName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Halcon 外部过程加载失败: {Procedure}", procedureName);
                    _loadedKey = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// 加载并校验：若操作名与过程名一致则视为过程，否则按主程序加载。
        /// 便于上层用统一入口加载 “方案”。
        /// </summary>
        public bool Load(string filePath, string? procedureName = null)
        {
            if (!string.IsNullOrWhiteSpace(procedureName) &&
                !string.Equals(Path.GetFileNameWithoutExtension(filePath), procedureName, StringComparison.OrdinalIgnoreCase))
            {
                return LoadProcedure(filePath, procedureName);
            }

            // 优先尝试过程（.hdev 内通常包含本地函数），失败再退回主程序模式
            if (!string.IsNullOrWhiteSpace(procedureName) && LoadProcedure(filePath, procedureName))
                return true;

            return LoadProgram(filePath);
        }

        #endregion

        #region 输入参数

        /// <summary>设置输入图标参数（图像、区域、XLD 等 HObject）</summary>
        public bool SetInputIconicParam(string paramName, HObject value)
        {
            if (!IsContentLoaded)
            {
                _logger.Warning("尝试设置图标参数，但程序/函数未加载");
                return false;
            }

            if (value == null || !value.IsInitialized())
            {
                _logger.Warning("尝试设置图标参数[{Param}]，但提供的 HObject 无效", paramName);
                return false;
            }

            lock (_sync)
            {
                try
                {
                    if (_procedureCall != null)
                    {
                        _procedureCall.SetInputIconicParamObject(paramName, value);
                        return true;
                    }

                    _logger.Warning("主程序模式不支持按名字设置输入参数[{Param}]，请改用 global 变量或外部过程模式", paramName);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "设置图标参数[{Param}]失败", paramName);
                    return false;
                }
            }
        }

        /// <summary>批量设置输入图标参数</summary>
        public bool SetInputIconicParams(Dictionary<string, HObject> inputParams)
        {
            foreach (var param in inputParams)
            {
                if (!SetInputIconicParam(param.Key, param.Value))
                    return false;
            }
            return true;
        }

        /// <summary>设置输入控制参数（数值、字符串、数组等 HTuple）</summary>
        public bool SetInputCtrlParam(string paramName, HTuple value)
        {
            if (!IsContentLoaded)
            {
                _logger.Warning("尝试设置控制参数，但程序/函数未加载");
                return false;
            }

            lock (_sync)
            {
                try
                {
                    if (_procedureCall != null)
                    {
                        _procedureCall.SetInputCtrlParamTuple(paramName, value);
                        return true;
                    }

                    _logger.Warning("主程序模式不支持按名字设置输入参数[{Param}]", paramName);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "设置控制参数[{Param}]失败", paramName);
                    return false;
                }
            }
        }

        /// <summary>批量设置输入控制参数</summary>
        public bool SetInputCtrlParams(Dictionary<string, HTuple> inputParams)
        {
            foreach (var param in inputParams)
            {
                if (!SetInputCtrlParam(param.Key, param.Value))
                    return false;
            }
            return true;
        }

        /// <summary>设置引擎 global 图标变量（主程序模式注入输入的主要手段）</summary>
        public bool SetGlobalIconicParam(string name, HObject value)
        {
            if (!IsEngineInitialized) return false;
            try
            {
                _engine!.SetGlobalIconicVarObject(name, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "设置 global 图标变量[{Name}]失败", name);
                return false;
            }
        }

        /// <summary>设置引擎 global 控制变量</summary>
        public bool SetGlobalCtrlParam(string name, HTuple value)
        {
            if (!IsEngineInitialized) return false;
            try
            {
                _engine!.SetGlobalCtrlVarTuple(name, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "设置 global 控制变量[{Name}]失败", name);
                return false;
            }
        }

        #endregion

        #region 执行与结果获取

        /// <summary>执行已加载的程序 / 函数</summary>
        public bool Execute()
        {
            if (!IsContentLoaded)
            {
                _logger.Warning("尝试执行，但程序/函数未加载");
                return false;
            }

            lock (_sync)
            {
                try
                {
                    if (_procedureCall != null)
                        _procedureCall.Execute();
                    else
                        _programCall!.Execute();

                    return true;
                }
                catch (HDevEngineException ex)
                {
                    _logger.Error(ex, "Halcon 程序执行异常: {Message}", ex.Message);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "程序执行失败");
                    return false;
                }
            }
        }

        /// <summary>获取输出图标参数</summary>
        public bool GetOutputIconicParam(string paramName, out HObject result)
        {
            result = null!;
            if (!IsContentLoaded)
            {
                _logger.Warning("尝试获取图标参数，但程序/函数未加载");
                return false;
            }

            lock (_sync)
            {
                try
                {
                    result = _procedureCall != null
                        ? _procedureCall.GetOutputIconicParamObject(paramName)
                        : _programCall!.GetIconicVarObject(paramName);

                    return result != null && result.IsInitialized();
                }
                catch (Exception ex)
                {
                    _logger.Warning("获取图标参数[{Param}]失败: {Message}", paramName, ex.Message);
                    return false;
                }
            }
        }

        /// <summary>获取输出控制参数</summary>
        public bool GetOutputCtrlParam(string paramName, out HTuple result)
        {
            result = null!;
            if (!IsContentLoaded)
            {
                _logger.Warning("尝试获取控制参数，但程序/函数未加载");
                return false;
            }

            lock (_sync)
            {
                try
                {
                    result = _procedureCall != null
                        ? _procedureCall.GetOutputCtrlParamTuple(paramName)
                        : _programCall!.GetCtrlVarTuple(paramName);

                    return result != null;
                }
                catch (Exception ex)
                {
                    _logger.Warning("获取控制参数[{Param}]失败: {Message}", paramName, ex.Message);
                    return false;
                }
            }
        }

        /// <summary>获取输出图标参数向量（数组输出）</summary>
        public bool GetOutputIconicParamVector(string paramName, out HObjectVector result)
        {
            result = null!;
            if (!IsContentLoaded) return false;

            lock (_sync)
            {
                try
                {
                    result = _procedureCall != null
                        ? _procedureCall.GetOutputIconicParamVector(paramName)
                        : _programCall!.GetIconicVarVector(paramName);
                    return result != null;
                }
                catch (Exception ex)
                {
                    _logger.Warning("获取图标参数向量[{Param}]失败: {Message}", paramName, ex.Message);
                    return false;
                }
            }
        }

        /// <summary>获取输出控制参数向量（数组输出）</summary>
        public bool GetOutputCtrlParamVector(string paramName, out HTupleVector result)
        {
            result = null!;
            if (!IsContentLoaded) return false;

            lock (_sync)
            {
                try
                {
                    result = _procedureCall != null
                        ? _procedureCall.GetOutputCtrlParamVector(paramName)
                        : _programCall!.GetCtrlVarVector(paramName);
                    return result != null;
                }
                catch (Exception ex)
                {
                    _logger.Warning("获取控制参数向量[{Param}]失败: {Message}", paramName, ex.Message);
                    return false;
                }
            }
        }

        /// <summary>兼容自定义算子接口：处理图像并返回区域与控制结果</summary>
        public bool ProcessImage(HImage inputImage, out HObject resultRegion, out HTuple resultData)
        {
            resultRegion = null!;
            resultData = new HTuple();

            var iconicNames = GetInputIconicParamNames();
            if (iconicNames.Length == 0)
            {
                _logger.Warning("ProcessImage 失败：当前过程没有输入图标参数");
                return false;
            }

            if (!SetInputIconicParam(iconicNames[0], inputImage))
                return false;

            if (!Execute())
                return false;

            var outIconic = GetOutputIconicParamNames();
            if (outIconic.Length > 0)
                GetOutputIconicParam(outIconic[0], out resultRegion);

            var outCtrl = GetOutputCtrlParamNames();
            if (outCtrl.Length > 0)
                GetOutputCtrlParam(outCtrl[0], out resultData);

            return true;
        }

        #endregion

        #region 过程接口自省（供设置界面生成参数编辑表格）

        /// <summary>获取输入图标参数名</summary>
        public string[] GetInputIconicParamNames() => QueryNames(p => p.GetInputIconicParamNames());

        /// <summary>获取输出图标参数名</summary>
        public string[] GetOutputIconicParamNames() => QueryNames(p => p.GetOutputIconicParamNames());

        /// <summary>获取输入控制参数名</summary>
        public string[] GetInputCtrlParamNames() => QueryNames(p => p.GetInputCtrlParamNames());

        /// <summary>获取输出控制参数名</summary>
        public string[] GetOutputCtrlParamNames() => QueryNames(p => p.GetOutputCtrlParamNames());

        private string[] QueryNames(Func<HDevProcedure, HTuple> selector)
        {
            lock (_sync)
            {
                try
                {
                    if (_procedure == null) return Array.Empty<string>();
                    var tuple = selector(_procedure);
                    if (tuple == null || tuple.Length == 0) return Array.Empty<string>();

                    var names = new List<string>(tuple.Length);
                    for (int i = 0; i < tuple.Length; i++)
                    {
                        var s = tuple[i].S;
                        if (!string.IsNullOrEmpty(s)) names.Add(s);
                    }
                    return names.ToArray();
                }
                catch (Exception ex)
                {
                    _logger.Warning("查询过程接口失败: {Message}", ex.Message);
                    return Array.Empty<string>();
                }
            }
        }

        /// <summary>生成当前过程接口描述（迁移自原项目的 ProcedureInterface 模型）</summary>
        public ProcedureInterface? QueryProcedureInterface()
        {
            if (!IsProcedureMode) return null;

            var iface = new ProcedureInterface
            {
                Name = _procedure?.Name ?? _loadedKey ?? string.Empty
            };

            foreach (var n in GetInputIconicParamNames())
                iface.InputImageParams.Add(new ParameterInfo { Name = n });
            foreach (var n in GetOutputIconicParamNames())
                iface.OutputImageParams.Add(new ParameterInfo { Name = n });
            foreach (var n in GetInputCtrlParamNames())
                iface.InputControlParams.Add(new ParameterInfo { Name = n });
            foreach (var n in GetOutputCtrlParamNames())
                iface.OutputControlParams.Add(new ParameterInfo { Name = n });

            return iface;
        }

        #endregion

        #region 显示窗口辅助功能（可选，WinForms/句柄方式）

        /// <summary>在指定窗口句柄上显示图像与结果区域（WPF 请使用 HalconView 控件）</summary>
        public void DisplayResult(HImage image, IntPtr windowHandle, params HObject[] regions)
        {
            if (windowHandle == IntPtr.Zero) return;

            HOperatorSet.GetImageSize(image, out HTuple width, out HTuple height);

            using var window = new HWindow();
            window.OpenWindow(0, 0, width, height, windowHandle, "visible", "");
            window.DispObj(image);
            window.SetColor("red");
            window.SetDraw("margin");
            foreach (var region in regions)
            {
                if (region != null && region.IsInitialized())
                    window.DispObj(region);
            }
        }

        #endregion

        #region 卸载与释放

        /// <summary>卸载已加载的程序 / 函数，保留引擎实例</summary>
        public void UnloadContent()
        {
            lock (_sync)
            {
                UnloadContentCore();
            }
        }

        private void UnloadContentCore()
        {
            _programCall?.Dispose();
            _procedureCall?.Dispose();
            _procedure?.Dispose();
            _program?.Dispose();

            _programCall = null;
            _procedureCall = null;
            _procedure = null;
            _program = null;
            _loadedKey = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            lock (_sync)
            {
                UnloadContentCore();
                _engine?.Dispose();
                _engine = null;
            }

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~HalconEngine() => Dispose();

        #endregion
    }
}
