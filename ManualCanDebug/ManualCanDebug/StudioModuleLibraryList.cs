using System;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ManualCanDebug
{
    /// <summary>Shared module-library list used by both Studio pages.</summary>
    internal static class StudioModuleLibraryList
    {
        public static ListBox Create(System.ComponentModel.ICollectionView view, bool allowMultipleSelection = false, Action<ManualCanDebug.Core.FunctionBlockDefinition> childSelected = null)
        {
            ListBox list = new ListBox
            {
                ItemsSource = view,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                Margin = new Thickness(5, 0, 5, 2),
                Padding = new Thickness(0),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                SelectionMode = allowMultipleSelection ? SelectionMode.Extended : SelectionMode.Single
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);

            FrameworkElementFactory root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            root.SetValue(StackPanel.MinWidthProperty, 245d);
            FrameworkElementFactory line = new FrameworkElementFactory(typeof(StackPanel));
            line.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            FrameworkElementFactory expand = new FrameworkElementFactory(typeof(ToggleButton));
            expand.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay });
            expand.SetBinding(ToggleButton.ContentProperty, new Binding("IsExpanded") { Converter = new ExpandGlyphConverter() });
            expand.SetBinding(ToggleButton.VisibilityProperty, new Binding("HasChildren") { Converter = new BooleanToVisibilityConverter() });
            expand.SetValue(ToggleButton.WidthProperty, 24d); expand.SetValue(ToggleButton.HeightProperty, 24d); expand.SetValue(ToggleButton.PaddingProperty, new Thickness(0)); expand.SetValue(ToggleButton.MarginProperty, new Thickness(0, 0, 5, 0)); expand.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent); expand.SetValue(ToggleButton.BorderBrushProperty, Brushes.Transparent); expand.SetValue(ToggleButton.ToolTipProperty, "展开/收起下级标准模块");
            line.AppendChild(expand);
            FrameworkElementFactory icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new Binding("LibraryGroup") { Converter = new ModuleIconGlyphConverter() });
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding("LibraryGroup") { Converter = new ModuleIconBrushConverter() });
            icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI Symbol"));
            icon.SetValue(TextBlock.FontSizeProperty, 14d);
            icon.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 1, 8, 0));
            line.AppendChild(icon);
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("TreeText"));
            text.SetBinding(TextBlock.ToolTipProperty, new Binding("TreeText"));
            text.SetValue(TextBlock.FontSizeProperty, 12d);
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            line.AppendChild(text);
            root.AppendChild(line);
            FrameworkElementFactory children = new FrameworkElementFactory(typeof(ItemsControl)); children.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Children")); children.SetBinding(ItemsControl.VisibilityProperty, new Binding("IsExpanded") { Converter = new BooleanToVisibilityConverter() }); children.SetValue(ItemsControl.MarginProperty, new Thickness(42, 4, 2, 4));
            FrameworkElementFactory childButton = new FrameworkElementFactory(typeof(Button)); childButton.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch); childButton.SetValue(Button.PaddingProperty, new Thickness(8, 5, 8, 5)); childButton.SetValue(Button.MarginProperty, new Thickness(0, 1, 0, 1)); childButton.SetValue(Button.CursorProperty, System.Windows.Input.Cursors.Hand); childButton.SetValue(Button.ToolTipProperty, "点击选择下级模块"); Style childStyle = new Style(typeof(Button)); childStyle.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(247, 250, 254)))); childStyle.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(224, 231, 241)))); childStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1))); Trigger childHover = new Trigger { Property = Button.IsMouseOverProperty, Value = true }; childHover.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(231, 240, 255)))); childHover.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(120, 166, 230)))); childStyle.Triggers.Add(childHover); childButton.SetValue(Button.StyleProperty, childStyle); if (childSelected != null) childButton.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, args) => { Button button = sender as Button; ModuleTreeChildRow row = button == null ? null : button.DataContext as ModuleTreeChildRow; if (row != null && row.Block != null) childSelected(row.Block); args.Handled = true; }));
            FrameworkElementFactory childLine = new FrameworkElementFactory(typeof(StackPanel)); childLine.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); FrameworkElementFactory branch = new FrameworkElementFactory(typeof(TextBlock)); branch.SetValue(TextBlock.TextProperty, "└"); branch.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(164, 176, 193))); branch.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 7, 0)); childLine.AppendChild(branch); FrameworkElementFactory childIcon = new FrameworkElementFactory(typeof(TextBlock)); childIcon.SetBinding(TextBlock.TextProperty, new Binding("IconGlyph")); childIcon.SetBinding(TextBlock.ForegroundProperty, new Binding("IconBrush")); childIcon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI Symbol")); childIcon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 7, 0)); childLine.AppendChild(childIcon); FrameworkElementFactory childText = new FrameworkElementFactory(typeof(TextBlock)); childText.SetBinding(TextBlock.TextProperty, new Binding("Name")); childText.SetBinding(TextBlock.ToolTipProperty, new Binding("Name")); childText.SetValue(TextBlock.FontSizeProperty, 11.5d); childText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); childText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(55, 75, 105))); childText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); childLine.AppendChild(childText); childButton.AppendChild(childLine); children.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = childButton }); root.AppendChild(children);
            list.ItemTemplate = new DataTemplate { VisualTree = root };

            FrameworkElementFactory groupHeader = new FrameworkElementFactory(typeof(DockPanel));
            groupHeader.SetValue(DockPanel.LastChildFillProperty, true);
            FrameworkElementFactory groupIcon = new FrameworkElementFactory(typeof(TextBlock));
            groupIcon.SetValue(TextBlock.TextProperty, "▰"); groupIcon.SetValue(TextBlock.FontSizeProperty, 12d); groupIcon.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(24, 112, 224))); groupIcon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 7, 0));
            groupHeader.AppendChild(groupIcon);
            FrameworkElementFactory groupText = new FrameworkElementFactory(typeof(TextBlock));
            groupText.SetBinding(TextBlock.TextProperty, new Binding("Name")); groupText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); groupText.SetValue(TextBlock.FontSizeProperty, 13d); groupText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(37, 49, 67)));
            groupHeader.AppendChild(groupText);
            DataTemplate groupHeaderTemplate = new DataTemplate { VisualTree = groupHeader };
            FrameworkElementFactory groupExpander = new FrameworkElementFactory(typeof(Expander));
            groupExpander.SetValue(Expander.IsExpandedProperty, true); groupExpander.SetValue(Expander.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch); groupExpander.SetValue(Expander.MarginProperty, new Thickness(2, 2, 2, 3)); groupExpander.SetBinding(Expander.HeaderProperty, new Binding()); groupExpander.SetValue(Expander.HeaderTemplateProperty, groupHeaderTemplate); groupExpander.SetValue(Expander.ToolTipProperty, "点击展开或收起该模块分组");
            FrameworkElementFactory groupItems = new FrameworkElementFactory(typeof(ItemsPresenter)); groupItems.SetValue(ItemsPresenter.MarginProperty, new Thickness(0, 2, 0, 0)); groupExpander.AppendChild(groupItems);
            ControlTemplate groupTemplate = new ControlTemplate(typeof(GroupItem)) { VisualTree = groupExpander };
            Style groupContainerStyle = new Style(typeof(GroupItem)); groupContainerStyle.Setters.Add(new Setter(GroupItem.TemplateProperty, groupTemplate));
            list.GroupStyle.Add(new GroupStyle { ContainerStyle = groupContainerStyle });

            Style itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(18, 8, 8, 8)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(2, 1, 2, 1)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(37, 49, 67))));
            Trigger selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(231, 240, 255))));
            selected.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(24, 112, 224))));
            itemStyle.Triggers.Add(selected);
            list.ItemContainerStyle = itemStyle;
            return list;
        }

        private sealed class ExpandGlyphConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return System.Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "▼" : "▶"; }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }

        private sealed class ModuleIconGlyphConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                string group = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                return group == "标准模块" ? "■" : group == "产品模块" ? "♥" : "★";
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }

        private sealed class ModuleIconBrushConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                string group = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                if (group == "标准模块") return new SolidColorBrush(Color.FromRgb(112, 185, 244));
                if (group == "产品模块") return new SolidColorBrush(Color.FromRgb(105, 207, 151));
                return new SolidColorBrush(Color.FromRgb(242, 181, 55));
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }
    }

    internal sealed class ModuleTreeChildRow
    {
        public ModuleTreeChildRow(ManualCanDebug.Core.FunctionBlockDefinition block) { Block = block; Name = block == null ? string.Empty : block.Name; string kind = block == null ? string.Empty : block.ModuleKind; IconGlyph = string.Equals(kind, "Product", StringComparison.OrdinalIgnoreCase) ? "♥" : string.Equals(kind, "Custom", StringComparison.OrdinalIgnoreCase) ? "★" : "■"; IconBrush = string.Equals(kind, "Product", StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush(Color.FromRgb(105, 207, 151)) : string.Equals(kind, "Custom", StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush(Color.FromRgb(242, 181, 55)) : new SolidColorBrush(Color.FromRgb(112, 185, 244)); }
        public ManualCanDebug.Core.FunctionBlockDefinition Block { get; private set; }
        public string Name { get; private set; }
        public string IconGlyph { get; private set; }
        public Brush IconBrush { get; private set; }
    }
}
