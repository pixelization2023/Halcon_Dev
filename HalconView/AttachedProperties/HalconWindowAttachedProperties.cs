using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HalconDotNet;
using System.Windows.Input;
using System.Windows;

namespace HalconView.AttachedProperties
{

    /// <summary>
    /// 为 HalconView 提供附加属性支持，用于传递窗口句柄和命令。
    /// </summary>
    public static class HalconWindowAttachedProperties
    {
        #region HalconWindow 附加属性（只读外部，控件内部设置）  


        /// <summary>
        /// 获取 Halcon 窗口句柄（只读）
        /// </summary>
        public static HWindow GetHalconWindow(DependencyObject obj)
        {

            return (HWindow)obj.GetValue(HalconWindowProperty);

        }

        /// <summary>
        /// 内部设置 Halcon 窗口句柄（仅控件内部调用）
        /// </summary>
        internal static void SetHalconWindow(DependencyObject obj, HWindow value)
        {
            obj.SetValue(HalconWindowProperty, value);
        }


        public static readonly DependencyProperty HalconWindowProperty =
          DependencyProperty.RegisterAttached(
              "HalconWindow",
              typeof(HWindow),
              typeof(HalconWindowAttachedProperties),
              new FrameworkPropertyMetadata(null, OnHalconWindowChanged));

        private static void OnHalconWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 当窗口句柄可用时，尝试触发 WindowReadyCommand
            if (d is UIElement element && e.NewValue is HWindow window)
            {
                var command = GetWindowReadyCommand(element);
                if (command?.CanExecute(window) == true)
                {
                    // 使用 Dispatcher 确保命令在 UI 线程执行
                    element.Dispatcher.BeginInvoke(new Action(() => command.Execute(window)));
                }
            }
        }


        #endregion



        #region WindowReadyCommand 附加属性（ViewModel 通过此命令接收句柄）


        public static ICommand GetWindowReadyCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(WindowReadyCommandProperty);
        }

        public static void SetWindowReadyCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(WindowReadyCommandProperty, value);
        }
        public static readonly DependencyProperty WindowReadyCommandProperty =
        DependencyProperty.RegisterAttached(
            "WindowReadyCommand",
            typeof(ICommand),
            typeof(HalconWindowAttachedProperties),
            new FrameworkPropertyMetadata(null, OnWindowReadyCommandChanged));

        private static void OnWindowReadyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 当命令被设置时，如果窗口句柄已存在，立即执行命令
            if (d is UIElement element)
            {
                var window = GetHalconWindow(element);
                var command = GetWindowReadyCommand(element);
                if (window != null && command?.CanExecute(window) == true)
                {
                    command.Execute(window);
                }
            }
        }


        #endregion


        #region WindowProvider 附加属性（内部使用，用于控件与附加属性的双向通信）

        internal static IHalconWindowProvider GetWindowProvider(DependencyObject obj)
        {
            return (IHalconWindowProvider)obj.GetValue(WindowProviderProperty);
        }

        internal static void SetWindowProvider(DependencyObject obj, IHalconWindowProvider value)
        {
            obj.SetValue(WindowProviderProperty, value);
        }

        internal static readonly DependencyProperty WindowProviderProperty =
            DependencyProperty.RegisterAttached(
                "WindowProvider",
                typeof(IHalconWindowProvider),
                typeof(HalconWindowAttachedProperties),
                new FrameworkPropertyMetadata(null, OnWindowProviderChanged));

        private static void OnWindowProviderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                var oldProvider = e.OldValue as IHalconWindowProvider;
                var newProvider = e.NewValue as IHalconWindowProvider;

                oldProvider?.Detach();
                newProvider?.Attach(element);
            }
        }

        #endregion
    }


    public interface IHalconWindowProvider
    {
        public void Attach(DependencyObject element);
        public void Detach();
    }
}
