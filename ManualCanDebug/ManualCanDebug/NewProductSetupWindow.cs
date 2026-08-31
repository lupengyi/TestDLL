using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ManualCanDebug
{
    internal sealed class NewProductSetup
    {
        public string Product { get; set; } public string LocatorPath { get; set; } public string DbcPath { get; set; } public string DriveStructure { get; set; } public List<string> Capabilities { get; set; }
    }

    internal sealed class NewProductSetupWindow : Window
    {
        private readonly TextBox _product = Box("C97"); private readonly TextBox _locator = Box(); private readonly TextBox _dbc = Box();
        private readonly ComboBox _drive = new ComboBox { ItemsSource = new[] { "单主驱", "双主驱TM1/TM2" }, SelectedIndex = 0, Margin = new Thickness(4), MinHeight = 31, Padding = new Thickness(6, 4, 6, 4) };
        private readonly CheckBox _dcdc = Check("DCDC"), _oil = Check("油泵"), _air = Check("气泵"), _pdu = Check("PDU");

        public NewProductSetupWindow()
        {
            Title = "增加新产品"; Width = 690; Height = 410; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
            Grid root = new Grid { Margin = new Thickness(24) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(root, "产品型号", _product, 0); Add(root, "驱动结构", _drive, 1); AddFile(root, "Locator文件", _locator, "选择Locator", "Excel文件 (*.xlsx)|*.xlsx", 2); AddFile(root, "DBC文件", _dbc, "选择DBC（可稍后配置）", "DBC文件 (*.dbc)|*.dbc", 3);
            WrapPanel capabilities = new WrapPanel { Margin = new Thickness(4, 5, 4, 5) }; capabilities.Children.Add(_dcdc); capabilities.Children.Add(_oil); capabilities.Children.Add(_air); capabilities.Children.Add(_pdu); Add(root, "附加功能", capabilities, 4);
            TextBlock hint = new TextBlock { Text = "这里只建立产品通信资源，不创建测试章节和主SEQ。Locator必须提供；没有辅驱时DBC可以留空。", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 12) }; Grid.SetRow(hint, 5); Grid.SetColumnSpan(hint, 2); root.Children.Add(hint);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button ok = new Button { Content = "完成产品配置", MinWidth = 120, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6), Background = new SolidColorBrush(Color.FromRgb(24, 112, 224)), Foreground = Brushes.White }; ok.Click += Complete_Click; Button cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; cancel.Click += (s, e) => DialogResult = false; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 6); Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons); Content = root;
        }

        public NewProductSetup Result { get; private set; }
        private void Complete_Click(object sender, RoutedEventArgs e)
        {
            string product = (_product.Text ?? string.Empty).Trim().ToUpperInvariant(); if (!Regex.IsMatch(product, @"^C\d+$")) { MessageBox.Show(this, "产品型号必须是C加数字，例如C97。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (!File.Exists(_locator.Text)) { MessageBox.Show(this, "请选择有效的Locator文件。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (!string.IsNullOrWhiteSpace(_dbc.Text) && !File.Exists(_dbc.Text)) { MessageBox.Show(this, "DBC文件不存在。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            List<string> capabilities = new List<string> { _drive.SelectedIndex == 1 ? "DualMainDrive" : "SingleMainDrive" }; if (_dcdc.IsChecked == true) capabilities.Add("DCDC"); if (_oil.IsChecked == true) capabilities.Add("OilPump"); if (_air.IsChecked == true) capabilities.Add("AirPump"); if (_pdu.IsChecked == true) capabilities.Add("PDU");
            Result = new NewProductSetup { Product = product, LocatorPath = _locator.Text, DbcPath = _dbc.Text, DriveStructure = _drive.SelectedIndex == 1 ? "DualMainDrive" : "SingleMainDrive", Capabilities = capabilities }; DialogResult = true;
        }
        private static void AddFile(Grid root, string label, TextBox field, string title, string filter, int row) { Grid holder = new Grid { Margin = new Thickness(4) }; holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); holder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); field.Margin = new Thickness(0); holder.Children.Add(field); Button browse = new Button { Content = "浏览...", MinWidth = 75, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) }; browse.Click += (s, e) => { OpenFileDialog dialog = new OpenFileDialog { Title = title, Filter = filter }; if (dialog.ShowDialog() == true) field.Text = dialog.FileName; }; Grid.SetColumn(browse, 1); holder.Children.Add(browse); Add(root, label, holder, row); }
        private static void Add(Grid grid, string label, FrameworkElement field, int row) { TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }; Grid.SetRow(text, row); grid.Children.Add(text); Grid.SetRow(field, row); Grid.SetColumn(field, 1); grid.Children.Add(field); }
        private static TextBox Box(string text = "") { return new TextBox { Text = text, Margin = new Thickness(4), Padding = new Thickness(7, 5, 7, 5), MinHeight = 31 }; }
        private static CheckBox Check(string text) { return new CheckBox { Content = text, Margin = new Thickness(4, 5, 16, 5), VerticalAlignment = VerticalAlignment.Center }; }
    }
}
