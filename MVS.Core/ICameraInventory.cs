namespace MVS.Core
{
    /// <summary>
    /// 相机清单与 SDK 状态的**只读**访问契约。
    ///
    /// 为什么放在共享内核：相机服务由外壳（MultiCameraSystem）持有，而"自检"在 Inspection 模块里，
    /// Inspection 不能反向引用外壳工程。所以把只读契约下沉到内核、由外壳实现
    /// —— 与 <see cref="IVisionInterfaceProvider"/> 同一思路（契约在内核、实现在使用方）。
    ///
    /// 只读是刻意的：自检、状态展示这类消费方不需要（也不应该）能打开/关闭相机。
    /// </summary>
    public interface ICameraInventory
    {
        /// <summary>MVS SDK 是否初始化成功</summary>
        bool IsInitialized { get; }

        /// <summary>
        /// SDK 初始化失败原因（成功时为 null）。
        /// 暴露它的原因：Initialize() 失败以前是被吞掉的，界面上只表现为"点刷新没反应"，
        /// 自检必须能把真实原因显示出来。
        /// </summary>
        string? InitializationError { get; }

        /// <summary>当前枚举到的设备快照（每次读取返回一份新的只读列表）</summary>
        IReadOnlyList<CameraInfo> Devices { get; }
    }
}
