using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class FunctionBlockPropertiesWindow : Window
    {
        private readonly TextBox _name = Box();
        private readonly ComboBox _moduleKind = Combo(new[] { "标准模块", "产品模块", "自定义模块" }, false);
        private readonly ComboBox _category = Combo(new[] { "电源", "电阻模拟器", "DMM", "DAQ", "IO", "PLC", "产品通信", "旋变", "主驱", "DCDC/辅驱", "安全", "逻辑", "自定义", "我的模块" }, false);
        private readonly ComboBox _version = Combo(new[] { "1.0", "1.1", "1.2", "2.0" }, false);
        private readonly TextBox _description = Box(90);
        private readonly CheckBox _allProducts = new CheckBox { Content = "全部产品", Margin = new Thickness(4, 5, 14, 5), VerticalAlignment = VerticalAlignment.Center };
        private readonly Dictionary<string, CheckBox> _products = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly string _currentProduct;

        public FunctionBlockPropertiesWindow(FunctionBlockDefinition block, IEnumerable<string> availableProducts = null)
        {
            _currentProduct = (availableProducts ?? Enumerable.Empty<string>()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            Title = "模块属性"; Width = 640; Height = 515; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 12;
            _name.Text = block.Name; _moduleKind.SelectedItem = KindDisplay(block.ModuleKind); _category.SelectedItem = block.Category; if (_category.SelectedItem == null) _category.SelectedItem = "自定义"; _version.SelectedItem = string.IsNullOrWhiteSpace(block.Version) ? "1.0" : block.Version; if (_version.SelectedItem == null) _version.SelectedItem = "1.0"; _description.Text = block.Description;

            Grid root = new Grid { Margin = new Thickness(22) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(root, "模块类型", _moduleKind, 0); Add(root, "模块名称", _name, 1); Add(root, "模块分类", _category, 2); Add(root, "版本", _version, 3);

            WrapPanel productPanel = new WrapPanel { Margin = new Thickness(4, 5, 4, 5) }; productPanel.Children.Add(_allProducts); IEnumerable<string> productNames = new[] { "C91", "C92", "C95", "C96" }.Concat(availableProducts ?? Enumerable.Empty<string>()).Concat(block.SupportedProducts ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase); foreach (string product in productNames) { CheckBox check = new CheckBox { Content = product, Margin = new Thickness(4, 5, 14, 5), VerticalAlignment = VerticalAlignment.Center }; _products[product] = check; productPanel.Children.Add(check); }
            List<string> supported = block.SupportedProducts ?? new List<string>(); bool all = supported.Count == 0; _allProducts.IsChecked = all; foreach (KeyValuePair<string, CheckBox> pair in _products) pair.Value.IsChecked = supported.Contains(pair.Key, StringComparer.OrdinalIgnoreCase); UpdateProductState(); _allProducts.Checked += (s, e) => UpdateProductState(); _allProducts.Unchecked += (s, e) => UpdateProductState(); _moduleKind.SelectionChanged += (s, e) => UpdateProductState(); Add(root, "适用产品", productPanel, 4);
            Add(root, "说明", _description, 5);

            TextBlock hint = new TextBlock { Text = "自定义模块随当前SEQ保存；产品模块自动锁定当前产品和Locator；标准模块为全局复用模板。适用产品由工程决定，不能在这里手动切换。", Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 10) }; Grid.SetRow(hint, 6); Grid.SetColumnSpan(hint, 2); root.Children.Add(hint);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button ok = new Button { Content = "应用并关闭", MinWidth = 110, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; ok.Click += (s, e) => { if (string.IsNullOrWhiteSpace(_name.Text)) { MessageBox.Show(this, "模块名称不能为空。", "模块属性", MessageBoxButton.OK, MessageBoxImage.Information); return; } DialogResult = true; }; Button cancel = new Button { Content = "取消", MinWidth = 85, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; cancel.Click += (s, e) => DialogResult = false; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 7); Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons); Content = root;
        }

        public void ApplyTo(FunctionBlockDefinition block)
        {
            string kind = Convert.ToString(_moduleKind.SelectedItem); string category = Convert.ToString(_category.SelectedItem); string version = Convert.ToString(_version.SelectedItem); block.ModuleKind = kind == "标准模块" ? "Standard" : kind == "产品模块" ? "Product" : "Custom"; block.IsStandard = block.ModuleKind != "Custom"; block.Name = _name.Text.Trim(); block.Category = string.IsNullOrWhiteSpace(category) ? "自定义" : category; block.Version = string.IsNullOrWhiteSpace(version) ? "1.0" : version; block.Description = _description.Text.Trim();
            if ((block.ModuleKind == "Product" || block.ModuleKind == "Custom") && !string.IsNullOrWhiteSpace(_currentProduct)) block.SupportedProducts = new List<string> { _currentProduct.Trim().ToUpperInvariant() };
            else block.SupportedProducts = _allProducts.IsChecked == true ? new List<string>() : _products.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList();
        }

        private void UpdateProductState()
        {
            string kind = Convert.ToString(_moduleKind.SelectedItem); bool projectScoped = kind == "产品模块" || kind == "自定义模块";
            if (projectScoped && !string.IsNullOrWhiteSpace(_currentProduct)) { _allProducts.IsChecked = false; foreach (KeyValuePair<string, CheckBox> pair in _products) pair.Value.IsChecked = string.Equals(pair.Key, _currentProduct, StringComparison.OrdinalIgnoreCase); }
            _allProducts.IsEnabled = false; foreach (CheckBox check in _products.Values) check.IsEnabled = false;
        }
        private static string KindDisplay(string kind) { return string.Equals(kind, "Standard", StringComparison.OrdinalIgnoreCase) ? "标准模块" : string.Equals(kind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品模块" : "自定义模块"; }
        private static TextBox Box(double minHeight = 0) { return new TextBox { Margin = new Thickness(4), Padding = new Thickness(7, 5, 7, 5), MinHeight = minHeight, AcceptsReturn = minHeight > 0, TextWrapping = minHeight > 0 ? TextWrapping.Wrap : TextWrapping.NoWrap }; }
        private static ComboBox Combo(IEnumerable<string> items, bool editable) { return new ComboBox { ItemsSource = items.ToArray(), IsEditable = editable, Margin = new Thickness(4), Padding = new Thickness(6, 4, 6, 4), MinHeight = 31 }; }
        private static void Add(Grid grid, string label, FrameworkElement field, int row) { TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }; Grid.SetRow(text, row); grid.Children.Add(text); Grid.SetRow(field, row); Grid.SetColumn(field, 1); grid.Children.Add(field); }
    }
}
