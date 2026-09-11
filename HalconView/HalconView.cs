using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HalconDotNet;
using HalconView.AttachedProperties;
using Serilog;

namespace HalconView
{

    /// <summary>
    /// 叠加层对象：显示在图像之上的框 / 文本。
    ///
    /// 设计意图：NG 框**不烧进图像**，而是作为独立叠加层绘制。
    /// 这样同一张图可以在不同窗口里用不同的框样式（颜色/线宽/是否显示）呈现，
    /// 存图时也不会被框污染。
    /// </summary>
    public sealed class HalconOverlay
    {
        /// <summary>行坐标（Halcon 习惯）</summary>
        public double Row { get; set; }

        /// <summary>列坐标</summary>
        public double Column { get; set; }

        /// <summary>宽（像素）</summary>
        public double Width { get; set; }

        /// <summary>高（像素）</summary>
        public double Height { get; set; }

        /// <summary>框线颜色（"red"/"green" 等 Halcon 颜色名；留空用 ErrorBrush 对应的 red）</summary>
        public string Color { get; set; } = "red";

        /// <summary>线宽（1~5）</summary>
        public int LineWidth { get; set; } = 2;

        /// <summary>叠加文本（为空则不画）</summary>
        public string? Text { get; set; }

        /// <summary>文本颜色</summary>
        public string TextColor { get; set; } = "white";
    }

    public class HalconView : Control,IHalconWindowProvider
    {
        private HSmartWindowControlWPF smartWindowControl;
        private HWindow hWindow;
        private readonly ILogger logger;

        // 用于管理当前显示的对象
        private readonly List<HObject> currentDisplayedObjects = new List<HObject>();


        static HalconView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(HalconView), new FrameworkPropertyMetadata(typeof(HalconView)));
        }


        public HalconView()
        {
            // 日志器：直接用 Serilog 全局静态 logger（详见 ResolveLogger 的说明）。
            // 旧写法是在这里 AppContainer.Resolve<ILogger>()，容器未就绪就抛 —— 启动期脆弱的根源。
            this.logger = ResolveLogger();

            logger.Information("HalconView 控件已创建");
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// 解析日志器。
        ///
        /// 解耦要点：这里**不再走 MVS.Core.AppContainer**（静态容器定位器）。
        /// 那个定位器存在两个真实问题：
        /// <list type="number">
        /// <item>它要求"容器必须先于任何控件创建就绪"，否则构造函数抛异常
        /// —— 启动期白屏且日志里什么都没有（本项目踩过这个坑）；</item>
        /// <item>它只为取 ILogger 而存在，而 Serilog 本来就有全局静态 logger
        ///（外壳已把容器 logger 提升为 <c>Serilog.Log.Logger</c>，两者是同一个实例）。</item>
        /// </list>
        /// 因此直接用 <see cref="Serilog.Log.Logger"/>：它永远不为 null，控件构造期绝不抛异常。
        /// </summary>
        private static ILogger ResolveLogger() => Serilog.Log.Logger;

        #region 控件模板加载

        public override void OnApplyTemplate()
        {

            if (GetTemplateChild("PART_HalconControl") is HSmartWindowControlWPF smartWindowControl)
            {
                this.smartWindowControl = smartWindowControl;

                logger.Information("成功找到 PART_HalconControl，并获取 HSmartWindowControlWPF 实例。");
                this.smartWindowControl.Loaded += (s, e) => UpdateWindowHandle();
            }
            else
            {
                logger.Warning("未找到 PART_HalconControl，请确保控件模板中包含 HSmartWindowControlWPF。");
                return;
            }


            // 将自身设置为窗口提供者（附加属性机制会调用 Attach/ Detach）
            HalconWindowAttachedProperties.SetWindowProvider(this, this);

            // 窗口加载完成后获取句柄


            base.OnApplyTemplate();
        }

        private void UpdateWindowHandle()
        {


            if (smartWindowControl == null) return;

            // 如果控件尺寸无效，等待尺寸变化
            if (smartWindowControl.ActualWidth <= 0 || smartWindowControl.ActualHeight <= 0)
            {
                logger?.Warning("控件尺寸无效，等待尺寸变化...");
                smartWindowControl.SizeChanged += OnSmartWindowSizeChanged;
                return;
            }

            // 尺寸有效，尝试获取 Halcon 窗口
            if (smartWindowControl.HalconWindow == null)
            {
                logger?.Warning("Halcon窗口句柄暂未就绪，将在布局完成后重试。");
                Dispatcher.BeginInvoke(new Action(() => UpdateWindowHandle()), DispatcherPriority.Loaded);
                return;
            }

            SetWindowHandle();

        }
        private void SetWindowHandle()
        {
            try
            {
                // 设置显示区域（确保控件尺寸有效）
                double width = smartWindowControl.ActualWidth;
                double height = smartWindowControl.ActualHeight;
                if (width <= 0 || height <= 0)
                {
                    logger?.Warning("控件尺寸无效，等待尺寸变化。");
                    smartWindowControl.SizeChanged += OnSmartWindowSizeChanged;
                    return;
                }

                // 说明：这里**刻意不再用控件像素尺寸**去 SetPart(0, 0, h-1, w-1)。
                // 那样做的后果是显示区域被设成"控件像素尺寸"，与图像实际尺寸无关 ——
                // 单窗口时凑巧接近还能看，多窗口（每格更小）时比例明显失真。
                // 正确的做法是显示图像时按图像自身尺寸 SetPart，再 SetFullImagePart() 自适应
                //（见 DisplayImage / 参考项目 ImageEdeitView.DisplayAutoResize）。
                hWindow = smartWindowControl.HalconWindow;
                HalconWindowAttachedProperties.SetHalconWindow(this, hWindow);
                logger?.Information("Halcon窗口句柄已设置。");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "设置Halcon窗口句柄失败");
            }
        }


        private void OnSmartWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                smartWindowControl.SizeChanged -= OnSmartWindowSizeChanged;
                UpdateWindowHandle();
            }
        }

        #endregion



        #region 依赖属性


        /// <summary>
        ///  要显示的 Halcon 图像
        /// </summary>
        public HObject HImage
        {
            get { return (HObject)GetValue(HImageProperty); }
            set { SetValue(HImageProperty, value); }
        }


        public static readonly DependencyProperty HImageProperty =
            DependencyProperty.Register("HImage", typeof(HObject), typeof(HalconView), new PropertyMetadata(null, OnHImageChanged));

        private static void OnHImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (HalconView)d;
            view?.DisplayImage(e.NewValue as HObject);
        }

        /// <summary>
        /// 叠加层集合（NG 框 / 文本）。绑定后图像上会实时叠加这些标记。
        /// </summary>
        public System.Collections.IEnumerable? Overlays
        {
            get => (System.Collections.IEnumerable?)GetValue(OverlaysProperty);
            set => SetValue(OverlaysProperty, value);
        }

        public static readonly DependencyProperty OverlaysProperty =
            DependencyProperty.Register("Overlays", typeof(System.Collections.IEnumerable), typeof(HalconView),
                new PropertyMetadata(null, OnOverlaysChanged));

        private static void OnOverlaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (HalconView)d;

            // 换了一个集合实例：退订旧的、订阅新的，集合内容变化时重绘
            if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= view.OnOverlaysCollectionChanged;

            if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += view.OnOverlaysCollectionChanged;

            view.RedrawOverlays();
        }

        private void OnOverlaysCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => RedrawOverlays();


        #endregion


        #region IIHalconWindowProvider 实现


        void Attach(DependencyObject element)
        {
            // 当附加属性被设置时，确保句柄已传递
            UpdateWindowHandle();

        }


        void Detach()
        {
            // 清理句柄，避免内存泄漏
            HalconWindowAttachedProperties.SetHalconWindow(this, null);
            hWindow = null;
        }
        #endregion





        #region   公共显示方法

        /// <summary>
        /// 显示图像(自动清空窗口并显示新图像)
        ///
        /// 所有权约定（重要）：本控件**不接管**传入图像的所有权。
        /// 传入的 HObject 可能同时被多个 HalconView、PCS 结果表、存图任务共享
        /// （例如同一张 PCS 结果图既在窗口里显示、又被存图、又被结果表引用），
        /// 因此这里既不把它加进内部释放列表，也不在任何时机 Dispose 它。
        /// 释放由图像的所有者（检测编排器 / 调用方）负责。
        /// </summary>
        /// <param name="image"></param>
        public void DisplayImage(HObject image)
        {
            if (image == null || !image.IsInitialized())
            {
                ClearWindow();
                return;
            }

            ExecuteOnUIThread(() =>
            {
                try
                {
                    hWindow?.ClearWindow();

                    // 按图像自身尺寸设置显示区域，再让控件自适应整幅 —— 多窗口下比例才正确
                    HTuple width, height;
                    HOperatorSet.GetImageSize(image, out width, out height);
                    if (width.Length > 0 && height.Length > 0 && width.D > 0 && height.D > 0)
                        hWindow?.SetPart(0, 0, height.D - 1, width.D - 1);

                    hWindow?.DispObj(image);     // 显示图像
                    smartWindowControl.SetFullImagePart(); // 设置显示区域适应图像大小

                    // 图像重画后叠加层会一起被清掉，这里补画
                    DrawOverlaysCore();
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, "显示图像失败");
                }
            });
        }

        /// <summary>
        /// 重画叠加层：清空窗口 → 重画当前图像 → 画叠加标记。
        /// 之所以要重画图像：Halcon 窗口没有"只清叠加层"的操作，
        /// clear_window 会把图像一起清掉。
        /// </summary>
        public void RedrawOverlays()
        {
            var image = HImage;
            if (image != null && image.IsInitialized())
                DisplayImage(image);
            else
                ExecuteOnUIThread(DrawOverlaysCore);
        }

        /// <summary>在已显示的图像上绘制叠加标记（必须在 UI 线程、窗口句柄就绪时调用）</summary>
        private void DrawOverlaysCore()
        {
            if (hWindow == null) return;
            if (Overlays == null) return;

            try
            {
                foreach (var item in Overlays)
                {
                    if (item is not HalconOverlay overlay) continue;

                    try
                    {
                        hWindow.SetColor(string.IsNullOrWhiteSpace(overlay.Color) ? "red" : overlay.Color);
                        hWindow.SetLineWidth(Math.Clamp(overlay.LineWidth, 1, 5));
                        hWindow.SetDraw("margin");

                        if (overlay.Width > 0 && overlay.Height > 0)
                        {
                            double row1 = overlay.Row - overlay.Height / 2.0;
                            double col1 = overlay.Column - overlay.Width / 2.0;
                            double row2 = overlay.Row + overlay.Height / 2.0;
                            double col2 = overlay.Column + overlay.Width / 2.0;

                            hWindow.DispRectangle1(row1, col1, row2, col2);
                        }

                        if (!string.IsNullOrWhiteSpace(overlay.Text))
                        {
                            hWindow.SetColor(string.IsNullOrWhiteSpace(overlay.TextColor) ? "white" : overlay.TextColor);
                            hWindow.DispText(overlay.Text, "image",
                                overlay.Row - overlay.Height / 2.0 - 4, overlay.Column - overlay.Width / 2.0,
                                overlay.TextColor, "box", "false");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 单个叠加对象画失败不应打断整幅显示
                        logger?.Warning("绘制叠加对象失败: {Message}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "绘制叠加层失败");
            }
        }



        /// <summary>
        /// 在窗口中添加一个 Halcon 对象（如 region、xld 等），不会清空已有内容
        /// </summary>
        public void AddObject(HObject obj)
        {
            if (obj == null || !obj.IsInitialized()) return;

            lock (currentDisplayedObjects)
            {
                // 注意：如果外部传入的对象需要由控件管理生命周期，则应复制一份或约定由控件释放。
                // 此处简单起见，假设传入对象由外部管理，控件仅用于显示。如果需要管理，应使用 obj.Clone()。
                currentDisplayedObjects.Add(obj);
            }

            ExecuteOnUIThread(() =>
            {
                try
                {
                    hWindow?.DispObj(obj);
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, "添加对象失败");
                }
            });
        }



        /// <summary>
        /// 清空窗口显示。
        ///
        /// 注意：**不释放**任何 Halcon 对象。图像与叠加对象的生命周期由所有者（调用方）管理，
        /// 控件只是"显示者"。旧实现在这里 Dispose 了自己 DisplayImage 过的图像，
        /// 与 DashboardViewModel 的释放逻辑叠加后会把同一张图释放两次（多窗口下必然踩）。
        /// </summary>
        public void ClearWindow()
        {
            ExecuteOnUIThread(() =>
            {
                try
                {
                    hWindow?.ClearWindow();
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, "清空窗口失败");
                }
            });
        }


        #endregion




        #region 辅助方法

        /// <summary>
        /// 确保操作在 UI 线程执行（HSmartWindowControl 必须在 UI 线程操作）
        /// </summary>
        private void ExecuteOnUIThread(Action action)
        {
            try
            {

                if (hWindow == null)
                {
                    logger?.Warning("窗口句柄未就绪，操作被忽略。");
                    return;
                }

                if (Dispatcher.CheckAccess())
                    action();
                else
                    Dispatcher.Invoke(action);
            }
            catch (Exception ex)
            {

                logger.Error(ex, "执行UI操作失败");
            }

        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // 控件卸载时释放所有资源
            ClearWindow();
            HalconWindowAttachedProperties.SetWindowProvider(this, null);
            hWindow = null;
        }

        void IHalconWindowProvider.Attach(DependencyObject element)
        {
            Attach(element);
        }

        void IHalconWindowProvider.Detach()
        {
            Detach();
        }

        #endregion
    }
}
