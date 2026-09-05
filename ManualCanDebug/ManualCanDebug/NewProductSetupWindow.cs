using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManualCanDebug.Core;
using Microsoft.Win32;

namespace ManualCanDebug
{
    internal sealed class NewProductSetup
    {
        public string Product { get; set; } public string LocatorPath { get; set; } public string DbcPath { get; set; } public string DriveStructure { get; set; } public List<string> Capabilities { get; set; } public List<ProductCanCommunicationDefinition> CanCommunications { get; set; }
    }

    internal sealed class NewProductSetupWindow : Window
    {
        private readonly TextBox _product = Box("C97"); private readonly TextBox _locator = Box(); private readonly TextBox _dbc = Box();
        private readonly ComboBox _drive = new ComboBox { ItemsSource = new[] { "单主驱", "双主驱TM1/TM2" }, SelectedIndex = 0, Margin = new Thickness(4), MinHeight = 31, Padding = new Thickness(6, 4, 6, 4) };
        private readonly CheckBox _dcdc = Check("DCDC"), _oil = Check("油泵"), _air = Check("气泵"), _pdu = Check("PDU");
        private readonly List<ProductCanCommunicationDefinition> _canCommunications = DefaultCanCommunications();
        private readonly DataGrid _canGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, Margin = new Thickness(4), MinHeight = 220 };

        public NewProductSetupWindow()
        {
            Title = "增加新产品"; Width = 1120; Height = 690; MinWidth = 900; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize; FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
            Grid root = new Grid { Margin = new Thickness(24) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Add(root, "产品型号", _product, 0); Add(root, "驱动结构", _drive, 1); AddFile(root, "Locator文件", _locator, "选择Locator", "Excel文件 (*.xlsx)|*.xlsx", 2); AddFile(root, "DBC文件", _dbc, "选择DBC（可稍后配置）", "DBC文件 (*.dbc)|*.dbc", 3);
            WrapPanel capabilities = new WrapPanel { Margin = new Thickness(4, 5, 4, 5) }; capabilities.Children.Add(_dcdc); capabilities.Children.Add(_oil); capabilities.Children.Add(_air); capabilities.Children.Add(_pdu); Add(root, "附加功能", capabilities, 4);
            ConfigureCanGrid(); Grid.SetRow(_canGrid, 5); Grid.SetColumnSpan(_canGrid, 2); root.Children.Add(_canGrid);
            TextBlock hint = new TextBlock { Text = "工位CAN只表示物理通道；这里定义该产品使用哪个CAN、经典CAN/CAN FD及开放的通信协议。协议可填写：原始CAN、DBC、UDS、XCP、Locator、A2L。", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 10) }; Grid.SetRow(hint, 6); Grid.SetColumnSpan(hint, 2); root.Children.Add(hint);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button ok = new Button { Content = "完成产品配置", MinWidth = 120, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6), Background = new SolidColorBrush(Color.FromRgb(24, 112, 224)), Foreground = Brushes.White }; ok.Click += Complete_Click; Button cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; cancel.Click += (s, e) => DialogResult = false; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 6); Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons); Content = root;
            Grid.SetRow(buttons, 7); _drive.SelectionChanged += (s, e) => ApplyDriveStructure(); ApplyDriveStructure();
        }

        public NewProductSetup Result { get; private set; }
        private void Complete_Click(object sender, RoutedEventArgs e)
        {
            string product = (_product.Text ?? string.Empty).Trim().ToUpperInvariant(); if (!Regex.IsMatch(product, @"^C\d+$")) { MessageBox.Show(this, "产品型号必须是C加数字，例如C97。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (!File.Exists(_locator.Text)) { MessageBox.Show(this, "请选择有效的Locator文件。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (!string.IsNullOrWhiteSpace(_dbc.Text) && !File.Exists(_dbc.Text)) { MessageBox.Show(this, "DBC文件不存在。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            List<string> capabilities = new List<string> { _drive.SelectedIndex == 1 ? "DualMainDrive" : "SingleMainDrive" }; if (_dcdc.IsChecked == true) capabilities.Add("DCDC"); if (_oil.IsChecked == true) capabilities.Add("OilPump"); if (_air.IsChecked == true) capabilities.Add("AirPump"); if (_pdu.IsChecked == true) capabilities.Add("PDU");
            _canGrid.CommitEdit(DataGridEditingUnit.Cell, true); _canGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ProductCanCommunicationDefinition main = _canCommunications.FirstOrDefault(value => value.Role == "主驱CAN"); if (main != null && string.IsNullOrWhiteSpace(main.ResourcePath)) main.ResourcePath = _locator.Text;
            ProductCanCommunicationDefinition auxiliary = _canCommunications.FirstOrDefault(value => value.Role == "辅驱CAN"); if (auxiliary != null && string.IsNullOrWhiteSpace(auxiliary.ResourcePath)) auxiliary.ResourcePath = _dbc.Text;
            foreach (ProductCanCommunicationDefinition item in _canCommunications.Where(value => value.Enabled)) { if (string.IsNullOrWhiteSpace(item.StationCan)) { MessageBox.Show(this, item.Role + " 未选择工位CAN。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (item.ArbitrationBaudRate <= 0 || (item.BusMode == "CAN FD" && item.DataBaudRate <= 0)) { MessageBox.Show(this, item.Role + " 的波特率无效。", "新产品", MessageBoxButton.OK, MessageBoxImage.Information); return; } }
            Result = new NewProductSetup { Product = product, LocatorPath = _locator.Text, DbcPath = _dbc.Text, DriveStructure = _drive.SelectedIndex == 1 ? "DualMainDrive" : "SingleMainDrive", Capabilities = capabilities, CanCommunications = _canCommunications.Select(value => value.Clone()).ToList() }; DialogResult = true;
        }
        private void ConfigureCanGrid() { _canGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "使用", Binding = new System.Windows.Data.Binding("Enabled"), Width = 52 }); _canGrid.Columns.Add(new DataGridTextColumn { Header = "产品通信", Binding = new System.Windows.Data.Binding("Role"), IsReadOnly = true, Width = 105 }); _canGrid.Columns.Add(new DataGridComboBoxColumn { Header = "工位CAN", ItemsSource = new[] { "调试CAN", "主驱CAN", "辅驱CAN", "校准CAN", "旋变1CAN", "旋变2CAN" }, SelectedItemBinding = new System.Windows.Data.Binding("StationCan"), Width = 105 }); _canGrid.Columns.Add(new DataGridComboBoxColumn { Header = "总线模式", ItemsSource = new[] { "经典CAN", "CAN FD" }, SelectedItemBinding = new System.Windows.Data.Binding("BusMode"), Width = 90 }); _canGrid.Columns.Add(new DataGridTextColumn { Header = "仲裁波特率", Binding = new System.Windows.Data.Binding("ArbitrationBaudRate"), Width = 100 }); _canGrid.Columns.Add(new DataGridTextColumn { Header = "FD数据波特率", Binding = new System.Windows.Data.Binding("DataBaudRate"), Width = 110 }); _canGrid.Columns.Add(new DataGridTextColumn { Header = "通信协议", Binding = new System.Windows.Data.Binding("Protocols"), Width = 160 }); _canGrid.Columns.Add(new DataGridTextColumn { Header = "协议资源（可选）", Binding = new System.Windows.Data.Binding("ResourcePath"), Width = 270 }); _canGrid.ItemsSource = _canCommunications; }
        private void ApplyDriveStructure() { ProductCanCommunicationDefinition resolver2 = _canCommunications.First(value => value.Role == "旋变2CAN"); resolver2.Enabled = _drive.SelectedIndex == 1; _canGrid.Items.Refresh(); }
        private static List<ProductCanCommunicationDefinition> DefaultCanCommunications() { return new List<ProductCanCommunicationDefinition> { Can("调试CAN", "调试CAN", "原始CAN,UDS,XCP"), Can("主驱CAN", "主驱CAN", "Locator,UDS,DBC"), Can("辅驱CAN", "辅驱CAN", "DBC"), Can("校准CAN", "校准CAN", "XCP,A2L"), Can("旋变1CAN", "旋变1CAN", "DBC"), Can("旋变2CAN", "旋变2CAN", "DBC") }; }
        private static ProductCanCommunicationDefinition Can(string role, string stationCan, string protocols) { return new ProductCanCommunicationDefinition { Role = role, StationCan = stationCan, Protocols = protocols }; }
        private static void AddFile(Grid root, string label, TextBox field, string title, string filter, int row) { Grid holder = new Grid { Margin = new Thickness(4) }; holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); holder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); field.Margin = new Thickness(0); holder.Children.Add(field); Button browse = new Button { Content = "浏览...", MinWidth = 75, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) }; browse.Click += (s, e) => { OpenFileDialog dialog = new OpenFileDialog { Title = title, Filter = filter }; if (dialog.ShowDialog() == true) field.Text = dialog.FileName; }; Grid.SetColumn(browse, 1); holder.Children.Add(browse); Add(root, label, holder, row); }
        private static void Add(Grid grid, string label, FrameworkElement field, int row) { TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }; Grid.SetRow(text, row); grid.Children.Add(text); Grid.SetRow(field, row); Grid.SetColumn(field, 1); grid.Children.Add(field); }
        private static TextBox Box(string text = "") { return new TextBox { Text = text, Margin = new Thickness(4), Padding = new Thickness(7, 5, 7, 5), MinHeight = 31 }; }
        private static CheckBox Check(string text) { return new CheckBox { Content = text, Margin = new Thickness(4, 5, 16, 5), VerticalAlignment = VerticalAlignment.Center }; }
    }
}
