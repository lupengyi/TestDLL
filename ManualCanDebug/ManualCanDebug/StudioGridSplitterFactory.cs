using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ManualCanDebug
{
    internal static class StudioGridSplitterFactory
    {
        public static GridSplitter Create(GridResizeDirection direction, string tooltip)
        {
            GridSplitter splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = direction,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false,
                Focusable = false,
                Cursor = direction == GridResizeDirection.Columns ? Cursors.SizeWE : Cursors.SizeNS,
                ToolTip = tooltip,
                Background = new SolidColorBrush(Color.FromRgb(226, 234, 244))
            };
            Style style = new Style(typeof(GridSplitter));
            style.Setters.Add(new Setter(GridSplitter.BackgroundProperty, new SolidColorBrush(Color.FromRgb(226, 234, 244))));
            Trigger hover = new Trigger { Property = GridSplitter.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(GridSplitter.BackgroundProperty, new SolidColorBrush(Color.FromRgb(96, 158, 230))));
            style.Triggers.Add(hover);
            splitter.Style = style;
            return splitter;
        }
    }

    internal static class StudioDragDropGuard
    {
        private const double IntentionalDistance = 10d;

        public static bool HasMovedEnough(Point start, Point current)
        {
            double horizontal = System.Math.Max(SystemParameters.MinimumHorizontalDragDistance, IntentionalDistance);
            double vertical = System.Math.Max(SystemParameters.MinimumVerticalDragDistance, IntentionalDistance);
            return System.Math.Abs(current.X - start.X) >= horizontal || System.Math.Abs(current.Y - start.Y) >= vertical;
        }

        public static bool IsMultiSelectGesture
        {
            get { return (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None; }
        }
    }
}
