using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NexInstaller.App.Behaviors;

/// <summary>Wheel-only animation; native scrollbar, keyboard and touch input take priority.</summary>
public static class SmoothScrolling
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScrolling), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(SmoothScrolling));
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScrolling), new PropertyMetadata(0d,
            (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void EnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement root) return;
        if (root.GetValue(StateProperty) is State old) old.Detach();
        root.SetValue(StateProperty, (bool)e.NewValue ? new State(root) : null);
    }

    private sealed class State
    {
        private readonly FrameworkElement root;
        private readonly Dictionary<ScrollViewer, double> targets = [];
        public State(FrameworkElement root)
        {
            this.root = root;
            root.PreviewMouseWheel += Wheel;
            root.PreviewMouseDown += Interrupt;
            root.PreviewKeyDown += Interrupt;
            root.PreviewTouchDown += Interrupt;
            root.Unloaded += Interrupt;
        }
        public void Detach()
        {
            Cancel();
            root.PreviewMouseWheel -= Wheel;
            root.PreviewMouseDown -= Interrupt;
            root.PreviewKeyDown -= Interrupt;
            root.PreviewTouchDown -= Interrupt;
            root.Unloaded -= Interrupt;
        }
        private void Interrupt(object? sender, RoutedEventArgs e) => Cancel();
        private void Cancel()
        {
            foreach (var viewer in targets.Keys.ToArray())
            {
                var offset = viewer.VerticalOffset;
                viewer.BeginAnimation(OffsetProperty, null);
                viewer.SetValue(OffsetProperty, offset);
            }
            targets.Clear();
        }
        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
            // Start at the innermost viewport. At an edge, try its ancestor rather
            // than letting the inner ScrollViewer swallow the wheel event.
            for (var node = e.OriginalSource as DependencyObject; node is not null; node = Parent(node))
            {
                if (node is not ScrollViewer viewer || viewer.ScrollableHeight <= 0 ||
                    viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) continue;
                // Logical item units cannot be animated as pixel offsets.
                if (viewer.CanContentScroll && viewer.Content is System.Windows.Controls.Primitives.IScrollInfo info &&
                    info is not VirtualizingStackPanel) return;
                var current = viewer.VerticalOffset;
                var moving = targets.TryGetValue(viewer, out var previous);
                var target = ScrollMotion.Target(current, previous, moving, e.Delta,
                    SystemParameters.WheelScrollLines, viewer.ViewportHeight, viewer.ScrollableHeight);
                if (Math.Abs(target - current) < 0.01) continue;
                e.Handled = true;
                Cancel();
                if (!SystemParameters.ClientAreaAnimation)
                {
                    viewer.ScrollToVerticalOffset(target);
                    return;
                }
                viewer.SetValue(OffsetProperty, current);
                targets[viewer] = target;
                var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };
                animation.Completed += (_, _) =>
                {
                    targets.Remove(viewer);
                    viewer.SetValue(OffsetProperty, Math.Min(target, viewer.ScrollableHeight));
                };
                viewer.BeginAnimation(OffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
                return;
            }
        }
        private static DependencyObject? Parent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    }
}
