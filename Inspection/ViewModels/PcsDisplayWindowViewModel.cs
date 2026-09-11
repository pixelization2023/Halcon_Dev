using System.Collections.ObjectModel;
using HalconDotNet;
using HalconView;
using Inspection.Models;
using Prism.Mvvm;

namespace Inspection.ViewModels
{
    /// <summary>
    /// 检测主监控台上**一个显示窗口**的视图模型。
    ///
    /// 背景：旧实现只有一个 Halcon 窗口，绑 <c>DashboardViewModel.CurrentImage</c>，
    /// 每来一个新 PCS 就把上一张替换掉（还把上一张 Dispose 了），
    /// 操作员因此看不到"其他 PCS 长什么样"。
    ///
    /// 现在每个窗口是一格，按 <see cref="DisplayWindowSpec"/> 自己决定"绑哪个 PCS、显示哪种图、
    /// 要不要 NG 框"。本类只持有 <see cref="PcsInspectionResult"/> 的**引用**，
    /// 不拥有图像所有权（释放由 InspectionOrchestrator.ClearSheet 统一负责），
    /// 因此同一张图被多个窗口同时引用是安全的。
    /// </summary>
    public class PcsDisplayWindowViewModel : BindableBase
    {
        private DisplayWindowSpec _spec;
        private bool _followLatest;

        public PcsDisplayWindowViewModel(DisplayWindowSpec spec)
        {
            _spec = spec ?? new DisplayWindowSpec();
            _followLatest = _spec.Bind == DisplayBindMode.Follow;
        }

        /// <summary>窗口序号（1 开始）</summary>
        public int Index => _spec.Index;

        /// <summary>绑定规则（界面可读写，改动后由父 VM 触发重新匹配）</summary>
        public DisplayWindowSpec Spec => _spec;

        /// <summary>
        /// 换用新的绑定规则（配置里改了"绑定哪个 PCS/哪种图/框样式"之后调用）。
        ///
        /// 为什么需要这个方法：父 VM 重建窗口集合时，若只复用已有窗口实例而不刷新规则，
        /// 用户在配置页改的绑定方式/图像来源/框样式**不会生效** ——
        /// 窗口还拿着构造时的旧 spec。这是个很容易漏的坑。
        /// </summary>
        public void SetSpec(DisplayWindowSpec spec)
        {
            if (spec == null) return;

            _spec = spec;
            _followLatest = _spec.Bind == DisplayBindMode.Follow;

            RaisePropertyChanged(nameof(Index));
            RaisePropertyChanged(nameof(Spec));
            RaisePropertyChanged(nameof(Title));

            // 规则变了，叠加层要按新规则重画；图像按新来源重新解析
            RebuildOverlays();
            RaisePropertyChanged(nameof(Image));
            RaisePropertyChanged(nameof(IsEmpty));
        }

        /// <summary>窗口标题</summary>
        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_spec.Title)) return _spec.Title;
                return _spec.Bind switch
                {
                    DisplayBindMode.Follow => $"最新 PCS",
                    DisplayBindMode.ByImage => $"图片 {_spec.Target}",
                    _ => string.IsNullOrWhiteSpace(_spec.Target) ? $"窗口 {Index}" : $"PCS {_spec.Target}"
                };
            }
        }

        private PcsInspectionResult? _result;

        /// <summary>当前绑定到的 PCS 结果（只持引用，不负责释放）</summary>
        public PcsInspectionResult? Result
        {
            get => _result;
            private set
            {
                if (SetProperty(ref _result, value))
                {
                    RaisePropertyChanged(nameof(Image));
                    RaisePropertyChanged(nameof(IsEmpty));
                    RaisePropertyChanged(nameof(JudgmentText));
                    RaisePropertyChanged(nameof(DetailText));
                    RaisePropertyChanged(nameof(IsNg));
                    RaisePropertyChanged(nameof(IsOk));
                }
            }
        }

        /// <summary>叠加层（NG 框 / 检测项文本），交给 HalconView 绘制</summary>
        public ObservableCollection<HalconOverlay> Overlays { get; } = new();

        /// <summary>当前应显示的图像（按 Spec.Source 与结果可用性决定）</summary>
        public HObject? Image => ResolveImage();

        /// <summary>是否没有可显示的内容</summary>
        public bool IsEmpty => ResolveImage() == null;

        public bool IsNg => _result?.Judgment == PcsJudgment.Ng;
        public bool IsOk => _result?.Judgment == PcsJudgment.Ok;

        public string JudgmentText => _result == null ? "—" : IsNg ? "NG" : IsOk ? "OK" : "?";

        /// <summary>卡片底部的明细：PCS 序号 / 结果串 / 耗时 / 图片序号</summary>
        public string DetailText
        {
            get
            {
                if (_result == null) return "等待中…";
                var text = $"PCS {_result.PcsIndex}  结果 {_result.ResultText}  {_result.ElapsedMs}ms";
                if (_result.SourceImageIndex > 0) text += $"  图 {_result.SourceImageIndex}";
                return text;
            }
        }

        /// <summary>判断某条 PCS 结果是否应该落进本窗口</summary>
        /// <param name="result">PCS 结果</param>
        public bool Match(PcsInspectionResult result)
        {
            if (result == null) return false;

            // 判定过滤：只显示 OK / 只显示 NG。
            // 注意：C# 里 "case A when ...: case B when ...: return false;" 这种「两个标签共用一个体」
            // 的写法**不生效** —— 第二个标签的 when 会被忽略，两个 case 都被当成可落入。
            // 必须写成单选条件。这正是验证探针抓到的真实缺陷（NgOnly 窗口当时会放行 OK 的 PCS）。
            if (_spec.Filter == DisplayJudgmentFilter.OkOnly && result.Judgment != PcsJudgment.Ok)
                return false;

            if (_spec.Filter == DisplayJudgmentFilter.NgOnly && result.Judgment != PcsJudgment.Ng)
                return false;

            switch (_spec.Bind)
            {
                case DisplayBindMode.Follow:
                    return true;

                case DisplayBindMode.ByPcs:
                    return string.Equals(result.PcsIndex, _spec.Target?.Trim(), StringComparison.Ordinal);

                case DisplayBindMode.ByKey:
                    return string.Equals(result.PcsKey, _spec.Target?.Trim(), StringComparison.Ordinal);

                case DisplayBindMode.ByImage:
                    return int.TryParse(_spec.Target?.Trim(), out var imageIndex)
                           && result.SourceImageIndex == imageIndex;

                default:
                    return false;
            }
        }

        /// <summary>把一条 PCS 结果绑定到本窗口（更新图像与叠加层）</summary>
        public void Attach(PcsInspectionResult result)
        {
            Result = result;
            RebuildOverlays();
        }

        /// <summary>清空窗口（换料 / 清除数据时调用）</summary>
        public void Clear()
        {
            Result = null;
            Overlays.Clear();
        }

        /// <summary>按 Spec 重建叠加层</summary>
        public void RebuildOverlays()
        {
            Overlays.Clear();

            if (_result == null || !_spec.ShowNgBoxes) return;

            var color = ResolveBoxColor(_spec.BoxColor);
            var lineWidth = Math.Clamp(_spec.BoxLineWidth, 1, 5);

            for (int item = 0; item < _result.PointSets.Count; item++)
            {
                var boxes = _result.PointSets[item];
                if (boxes == null) continue;

                foreach (var box in boxes)
                {
                    // 只画有面积的框（0 面积的框在图上就是一个点，没有意义）
                    if (box.Width <= 0 || box.Height <= 0) continue;

                    Overlays.Add(new HalconOverlay
                    {
                        Row = box.Row,
                        Column = box.Column,
                        Width = box.Width,
                        Height = box.Height,
                        Color = color,
                        LineWidth = lineWidth,
                        Text = _spec.ShowItemText ? $"{(item < _result.ItemResults.Count && _result.ItemResults[item] == "1" ? "NG" : "OK")}{item}" : null,
                        TextColor = color
                    });
                }
            }
        }

        /// <summary>取当前应显示的图像：结果图 / 原图副本，按 Spec.Source 与可用性回退</summary>
        private HObject? ResolveImage()
        {
            if (_result == null) return null;

            var resultImage = _result.OutputImage;
            var originalImage = _result.SourceImage;

            static bool HasImage(HObject? img) => img != null && img.IsInitialized();

            switch (_spec.Source)
            {
                case DisplayImageSource.ResultImage:
                    return HasImage(resultImage) ? resultImage : null;

                case DisplayImageSource.OriginalImage:
                    return HasImage(originalImage) ? originalImage : null;

                default: // Auto：有结果图用结果图，否则用原图
                    if (HasImage(resultImage)) return resultImage;
                    return HasImage(originalImage) ? originalImage : null;
            }
        }

        /// <summary>#RRGGBB → Halcon 颜色名（Halcon 只认颜色名或 RGB 三元组字符串）</summary>
        private static string ResolveBoxColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return "red";

            var value = hex.Trim();
            if (!value.StartsWith("#", StringComparison.Ordinal)) return value;

            // 常见色直接映射；其它情况转成 Halcon 支持的 "rgb" 三通道写法
            var rgb = value.TrimStart('#');
            if (rgb.Length != 6) return "red";

            try
            {
                var r = Convert.ToInt32(rgb.Substring(0, 2), 16) * 255 / 255;
                var g = Convert.ToInt32(rgb.Substring(2, 2), 16) * 255 / 255;
                var b = Convert.ToInt32(rgb.Substring(4, 2), 16) * 255 / 255;

                if (r > 200 && g < 80 && b < 80) return "red";
                if (g > 200 && r < 80 && b < 80) return "green";
                if (b > 200 && r < 80 && g < 80) return "blue";
                if (r > 200 && g > 150 && b < 80) return "yellow";
                if (r > 200 && g > 200 && b > 200) return "white";

                return $"#{r:D3}{g:D3}{b:D3}";
            }
            catch
            {
                return "red";
            }
        }
    }
}
