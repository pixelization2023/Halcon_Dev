namespace Inspection.Models
{
    /// <summary>用户角色（迁移自 窗体 的 FrmPower 权限等级）</summary>
    public enum UserRole
    {
        /// <summary>未登录</summary>
        None = 0,

        /// <summary>操作员：仅可查看/操作 通用设置、MES设置、存图设置</summary>
        Operator = 1,

        /// <summary>工程师：全部设置页可编辑</summary>
        Engineer = 2,

        /// <summary>管理员：全部权限 + 用户管理</summary>
        Administrator = 3
    }

    /// <summary>PLC 厂商/协议（对应 窗体 的 "汇川" / "三菱"）</summary>
    public enum PlcVendor
    {
        /// <summary>汇川 H5U / H3U（Modbus 寄存器）</summary>
        Inovance = 0,

        /// <summary>三菱 MC 协议</summary>
        Mitsubishi = 1,

        /// <summary>西门子 S7</summary>
        Siemens = 2,

        /// <summary>通用 Modbus TCP</summary>
        ModbusTcp = 3
    }

    /// <summary>PLC 软元件区域（与 窗体.inovanceModbusTransform 的地址前缀一致）</summary>
    public enum PlcAreaType
    {
        /// <summary>M 位元件（默认）</summary>
        M = 0,

        /// <summary>D 字元件</summary>
        D = 1,

        /// <summary>X 输入</summary>
        X = 2,

        /// <summary>Y 输出</summary>
        Y = 3,

        /// <summary>S 步进继电器</summary>
        S = 4,

        /// <summary>R 文件寄存器</summary>
        R = 5
    }

    /// <summary>PLC 点位方向</summary>
    public enum PlcIoDirection
    {
        Read = 0,
        Write = 1
    }

    /// <summary>整张/单 PCS 上传模式（迁移自 Checkclass 的 Single/Many/WholePCS/SinglePCS）</summary>
    public enum UploadMode
    {
        /// <summary>单流程单 PCS（VM设置.Single）</summary>
        SingleProcess = 0,

        /// <summary>多流程多 PCS（VM设置.Many）</summary>
        MultiProcess = 1
    }

    /// <summary>二维码与图片的绑定关系（迁移自 Checkclass 的 OneImageOneCode / ManyImageOneCode）</summary>
    public enum CodeBindMode
    {
        /// <summary>未启用</summary>
        Disabled = 0,

        /// <summary>一张图一个码（对应 OneImageOneCode）</summary>
        OneImageOneCode = 1,

        /// <summary>多张图一个码（对应 ManyImageOneCode）</summary>
        ManyImageOneCode = 2
    }

    /// <summary>流道模式（迁移自 Checkclass 的 Runners / Leaflets）</summary>
    public enum ConveyorMode
    {
        None = 0,

        /// <summary>流道模式：连续过板</summary>
        Runners = 1,

        /// <summary>单张模式：单张送板</summary>
        Leaflets = 2
    }

    /// <summary>相机/扫码枪种类（迁移自 FrmSetUp.AddCamera）</summary>
    public enum DeviceKind
    {
        HikCamera = 0,
        DahengCamera = 1,
        HikCodeReader = 2
    }

    /// <summary>检测判定</summary>
    public enum PcsJudgment
    {
        Unknown = 0,
        Ok = 1,
        Ng = 2
    }

    // ==================== 多窗口显示（见 Docs/多窗口显示与检测流程方案.md 第 2 节） ====================

    /// <summary>显示窗口绑定某个 PCS 的方式</summary>
    public enum DisplayBindMode
    {
        /// <summary>按 PCS 序号（PcsInspectionResult.PcsIndex）</summary>
        ByPcs = 0,

        /// <summary>按上传顺序键（PcsInspectionResult.PcsKey，现场真实位置）</summary>
        ByKey = 1,

        /// <summary>按来源图片序号（该图片产出的 PCS 依次占位）</summary>
        ByImage = 2,

        /// <summary>跟随最新完成的 PCS（滚动刷新）</summary>
        Follow = 3
    }

    /// <summary>窗口里显示哪种图像</summary>
    public enum DisplayImageSource
    {
        /// <summary>有结果图就用结果图，否则回退原始输入图</summary>
        Auto = 0,

        /// <summary>强制用 Halcon 输出的结果图</summary>
        ResultImage = 1,

        /// <summary>强制用原始输入图</summary>
        OriginalImage = 2
    }

    /// <summary>窗口只看某种判定的 PCS</summary>
    public enum DisplayJudgmentFilter
    {
        Any = 0,
        OkOnly = 1,
        NgOnly = 2
    }

    /// <summary>窗口缩放方式</summary>
    public enum DisplayScaleMode
    {
        /// <summary>整幅适应窗口（默认）</summary>
        Fit = 0,

        /// <summary>1:1 显示，可拖动查看</summary>
        None = 1
    }

    /// <summary>没有结果时窗口里显示什么</summary>
    public enum EmptyWindowMode
    {
        /// <summary>空窗（黑底）</summary>
        Empty = 0,

        /// <summary>显示最近一次的原始输入图</summary>
        OriginalImage = 1
    }
}
