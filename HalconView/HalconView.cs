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
            // 初始化日志（可根据实际情况替换）

            //添加logger
            this.logger = MVS.Core.AppContainer.Resolve<ILogger>();

            logger.Information("logg加载完成");
            Unloaded += OnUnloaded;

        }

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

                smartWindowControl.HalconWindow.SetPart(0, 0, height - 1, width - 1);
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
                    ClearWindow();               // 先清空窗口
                    hWindow?.DispObj(image);     // 显示图像
                    smartWindowControl.SetFullImagePart(); // 设置显示区域适应图像大小
                    AddObject(image);             // 加入管理列表（注意：此处应只管理显示对象，但图像是传入的，是否需要管理取决于使用者）
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, "显示图像失败");
                }
            });
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
        /// 清空窗口并释放管理的 Halcon 对象
        /// </summary>
        public void ClearWindow()
        {
            lock (currentDisplayedObjects)
            {
                foreach (var obj in currentDisplayedObjects)
                {
                    obj?.Dispose();
                }
                currentDisplayedObjects.Clear();
            }

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
