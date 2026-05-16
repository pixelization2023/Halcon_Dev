using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace MultiCameraSystem.Behaviors
{
    /// <summary>
    /// ListBox 附加行为。
    ///
    /// ViewModel 里原来有个 <c>AutoScroll</c> 属性，但界面上根本没人用它（死属性）。
    /// 「滚动到最新一条」属于纯视图行为，放在附加属性里既不污染 ViewModel，
    /// 也让 XAML 可以直接 <c>behaviors:ListBoxBehaviors.AutoScrollToEnd="{Binding AutoScroll}"</c>。
    /// </summary>
    public static class ListBoxBehaviors
    {
        public static readonly DependencyProperty AutoScrollToEndProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollToEnd",
                typeof(bool),
                typeof(ListBoxBehaviors),
                new PropertyMetadata(false, OnAutoScrollToEndChanged));

        public static bool GetAutoScrollToEnd(DependencyObject obj)
            => (bool)obj.GetValue(AutoScrollToEndProperty);

        public static void SetAutoScrollToEnd(DependencyObject obj, bool value)
            => obj.SetValue(AutoScrollToEndProperty, value);

        private static readonly DependencyProperty HandlerProperty =
            DependencyProperty.RegisterAttached(
                "Handler",
                typeof(NotifyCollectionChangedEventHandler),
                typeof(ListBoxBehaviors),
                new PropertyMetadata(null));

        private static void OnAutoScrollToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox) return;

            if (listBox.GetValue(HandlerProperty) is NotifyCollectionChangedEventHandler old)
            {
                if (listBox.Items is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= old;
                listBox.ClearValue(HandlerProperty);
            }

            if (e.NewValue is not true) return;

            if (listBox.Items is not INotifyCollectionChanged collection) return;

            NotifyCollectionChangedEventHandler handler = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add && listBox.Items.Count > 0)
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
            };

            collection.CollectionChanged += handler;
            listBox.SetValue(HandlerProperty, handler);
        }
    }
}
