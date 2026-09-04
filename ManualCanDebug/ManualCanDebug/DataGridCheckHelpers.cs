using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ManualCanDebug
{
    /// <summary>
    /// DataGridCheckBoxColumn needs a row-select click first; template CheckBox toggles on one click.
    /// </summary>
    internal static class DataGridCheckHelpers
    {
        public static DataGridTemplateColumn BoundCheckColumn(string header, string propertyName, double width = 65, Style checkStyle = null, RoutedEventHandler click = null)
        {
            FrameworkElementFactory check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding(propertyName)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
            check.SetValue(CheckBox.FocusableProperty, false);
            check.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            if (checkStyle != null) check.SetValue(FrameworkElement.StyleProperty, checkStyle);
            if (click != null) check.AddHandler(CheckBox.ClickEvent, click);
            return new DataGridTemplateColumn
            {
                Header = header,
                Width = width,
                CellTemplate = new DataTemplate { VisualTree = check }
            };
        }
    }
}
