using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ManualCanDebug
{
    internal static class StudioTabStyleFactory
    {
        public static Style Create(double fontSize)
        {
            Style style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(TabItem.FontFamilyProperty, new FontFamily("Segoe UI")));
            style.Setters.Add(new Setter(TabItem.FontSizeProperty, fontSize));
            style.Setters.Add(new Setter(TabItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(73, 87, 106))));
            style.Setters.Add(new Setter(TabItem.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(TabItem.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(14, 9, 14, 9)));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TabItem.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(TabItem.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(TabItem.PaddingProperty));
            border.AppendChild(presenter);
            ControlTemplate template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };
            style.Setters.Add(new Setter(TabItem.TemplateProperty, template));
            Trigger hover = new Trigger { Property = TabItem.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(TabItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(246, 249, 253)))); style.Triggers.Add(hover);
            Trigger selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(TabItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(24, 112, 224)))); selected.Setters.Add(new Setter(TabItem.FontWeightProperty, FontWeights.SemiBold)); selected.Setters.Add(new Setter(TabItem.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(24, 112, 224)))); selected.Setters.Add(new Setter(TabItem.BackgroundProperty, Brushes.White)); style.Triggers.Add(selected);
            return style;
        }
    }
}
