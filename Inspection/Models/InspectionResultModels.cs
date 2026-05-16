using HalconDotNet;

namespace Inspection.Models
{
    /// <summary>
    /// 检测框（像素坐标）。
    /// 迁移自 窗体.VMResult.PointFs 中的 VM.PlatformSDKCS.RectBox —— 改用 Halcon 的
    /// smallest_rectangle1 / 区域特征直接产出，不再依赖 VisionMaster 的点集类型。
    /// </summary>
    public sealed class BoxRegion
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary>行坐标（Halcon 习惯，方便直接画框）</summary>
        public double Row => CenterY;

        /// <summary>列坐标</summary>
        public double Column => CenterX;

        /// <summary>由 smallest_rectangle1 的四元组构造</summary>
        public static BoxRegion FromRectangle1(double row1, double column1, double row2, double column2)
            => new()
            {
                CenterY = (row1 + row2) / 2.0,
                CenterX = (column1 + column2) / 2.0,
                Height = Math.Abs(row2 - row1) + 1,
                Width = Math.Abs(column2 - column1) + 1
            };

        public override string ToString()
            => $"({CenterX:F0},{CenterY:F0}) {Width:F0}x{Height:F0}";
    }

    /// <summary>
    /// 单个 PCS 的检测结果。
    /// 迁移自 窗体.序列化类.VMResult：StringResult / PointFs / OutImage / PhotoName / PaperCode / PcsIndex。
    /// </summary>
    public class PcsInspectionResult
    {
        /// <summary>结果字典键（原 Resultsdict 的 Key，即"上传顺序"里的位置号）</summary>
        public string PcsKey { get; set; } = string.Empty;

        /// <summary>PCS 序号（原 PcsIndex，1 开始）</summary>
        public string PcsIndex { get; set; } = string.Empty;

        /// <summary>
        /// 检测项结果：
        /// 迁移自 VM 的 OK/NG 字符串，这里保留 "0"=OK / "1"=NG 的编码以便与历史上传数据一致。
        /// </summary>
        public List<string> ItemResults { get; set; } = new();

        /// <summary>每个检测项的框集合（原 PointFs）</summary>
        public List<List<BoxRegion>> PointSets { get; set; } = new();

        /// <summary>Halcon 输出的 PCS 结果图像（由调用方负责释放）</summary>
        public HObject? OutputImage { get; set; }

        /// <summary>结果图片文件名</summary>
        public string PhotoName { get; set; } = string.Empty;

        /// <summary>纸质码</summary>
        public string PaperCode { get; set; } = string.Empty;

        /// <summary>镭射码</summary>
        public string LaserCode { get; set; } = string.Empty;

        /// <summary>判定</summary>
        public PcsJudgment Judgment { get; set; } = PcsJudgment.Unknown;

        /// <summary>检测耗时（ms）</summary>
        public long ElapsedMs { get; set; }

        /// <summary>是否有 NG 项</summary>
        public bool HasNg => ItemResults.Any(r => r == "1");

        /// <summary>结果拼接串（原 string.Join("", StringResult)）</summary>
        public string ResultText => string.Concat(ItemResults);

        public void DisposeImage()
        {
            OutputImage?.Dispose();
            OutputImage = null;
        }

        public override string ToString()
            => $"PCS{_pcs} [{(Judgment == PcsJudgment.Ng ? "NG" : "OK")}] {ResultText}";
        private string _pcs => string.IsNullOrEmpty(PcsIndex) ? PcsKey : PcsIndex;
    }

    /// <summary>待检测的一帧图像（迁移自 窗体.序列化类.ImageInfo / BMPInfo 的合并）</summary>
    public sealed class QueuedFrame : IDisposable
    {
        /// <summary>图片序号（1 开始）</summary>
        public int ImageIndex { get; set; }

        /// <summary>图片名称（用于存图命名）</summary>
        public string PhotoName { get; set; } = string.Empty;

        /// <summary>来源相机逻辑名</summary>
        public string CameraName { get; set; } = string.Empty;

        /// <summary>Halcon 图像（拥有所有权，Dispose 时释放）</summary>
        public HObject Image { get; set; } = null!;

        /// <summary>采集时间</summary>
        public DateTime CapturedAt { get; set; } = DateTime.Now;

        public void Dispose()
        {
            Image?.Dispose();
            Image = null!;
        }
    }

    /// <summary>单张料的检测汇总（原 RunImage / PicNum 进度）</summary>
    public sealed class InspectionSummary
    {
        public string ProductName { get; set; } = string.Empty;

        /// <summary>图片总数</summary>
        public int ImageTotal { get; set; }

        /// <summary>已处理图片数</summary>
        public int ProcessedImages { get; set; }

        /// <summary>已产出 PCS 结果数</summary>
        public int ProcessedPcs { get; set; }

        /// <summary>整张 PCS 总数</summary>
        public int SheetPcsTotal { get; set; }

        public int OkCount { get; set; }
        public int NgCount { get; set; }

        /// <summary>检测总耗时（ms）</summary>
        public long ElapsedMs { get; set; }

        /// <summary>视觉流程累计耗时（ms）</summary>
        public long VisionElapsedMs { get; set; }

        /// <summary>上传累计耗时（ms）</summary>
        public long UploadElapsedMs { get; set; }

        public string? LastError { get; set; }

        /// <summary>进度百分比 0~1</summary>
        public double Progress => ImageTotal <= 0 ? 0 : Math.Clamp((double)ProcessedImages / ImageTotal, 0, 1);
    }

    /// <summary>存图任务（迁移自 窗体.序列化类.BMPInfo）</summary>
    public sealed class ImageArchiveTask
    {
        public HObject Image { get; set; } = null!;
        public string FilePath { get; set; } = string.Empty;
        public bool UseJpeg { get; set; }
        public int Quality { get; set; } = 80;
    }

    /// <summary>
    /// 状态灯（主界面一列状态监视，迁移自 FrmMian.timer1_Tick 中的一系列 Label）。
    /// 原实现每次刷新都直接改 Label.Text/ForeColor，这里实现 INotifyPropertyChanged 供 WPF 数据绑定。
    /// </summary>
    public sealed class StatusIndicator : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline == value) return;
                _isOnline = value;
                RaisePropertyChanged(nameof(IsOnline));
                RaiseTextChanged();
            }
        }

        public string OkText { get; set; } = "连接成功";
        public string FailText { get; set; } = "连接失败";

        public string Text => IsOnline ? OkText : FailText;

        /// <summary>状态色（供 XAML 绑定）</summary>
        public string Color => IsOnline ? "#00E676" : "#FF5252";

        public void RaiseTextChanged()
        {
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(Color));
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void RaisePropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
