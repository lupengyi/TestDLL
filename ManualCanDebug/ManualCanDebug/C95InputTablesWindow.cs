using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class C95InputTablesWindow : Window
    {
        private readonly Func<IReadOnlyList<C95InputSignalResult>> _reader;
        private readonly DataGrid _grid;
        private readonly ComboBox _tableFilter;
        private readonly TextBox _nameFilter;
        private readonly TextBlock _status;
        private readonly Button _refresh;
        private List<C95InputSignalResult> _results = new List<C95InputSignalResult>();

        public C95InputTablesWindow(Func<IReadOnlyList<C95InputSignalResult>> reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Title = "C95 Input Tables 全部输入信号";
            Width = 1500;
            Height = 820;
            MinWidth = 1050;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock description = new TextBlock
            {
                Text = "按C95 Locator 的 Input Tables 页读取当前值：0x00模拟量、0x0C ADC计数、0x18离散量、0x20脉冲量、0x2C逆变器温度。每张表整块读取一次，再按Excel偏移展开；原始CAN报文同步写入主窗口日志。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(description, 0);
            root.Children.Add(description);

            WrapPanel filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            filters.Children.Add(new TextBlock { Text = "表：", VerticalAlignment = VerticalAlignment.Center });
            _tableFilter = new ComboBox { Width = 230, Margin = new Thickness(4, 0, 12, 0) };
            _tableFilter.Items.Add("全部");
            foreach (C95InputTableDefinition table in C95InputCatalog.Tables) _tableFilter.Items.Add(table.Name + " " + table.AddressText);
            _tableFilter.SelectedIndex = 0;
            _tableFilter.SelectionChanged += Filter_Changed;
            filters.Children.Add(_tableFilter);
            filters.Children.Add(new TextBlock { Text = "信号名/端口：", VerticalAlignment = VerticalAlignment.Center });
            _nameFilter = new TextBox { Width = 260, Margin = new Thickness(4, 0, 12, 0), Padding = new Thickness(4, 2, 4, 2) };
            _nameFilter.TextChanged += Filter_Changed;
            filters.Children.Add(_nameFilter);
            Grid.SetRow(filters, 1);
            root.Children.Add(filters);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };
            _grid.Columns.Add(new DataGridTextColumn { Header = "表", Binding = new Binding("TableName"), Width = 170 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "表偏移", Binding = new Binding("TableAddress"), Width = 70 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "信号偏移", Binding = new Binding("SignalOffsetText"), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "信号名", Binding = new Binding("SignalName"), Width = 250 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "端口", Binding = new Binding("PortName"), Width = 130 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("DataType"), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "原始字节", Binding = new Binding("RawBytes"), Width = 130 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "解析值", Binding = new Binding("ValueText"), Width = 120 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "状态解释", Binding = new Binding("Interpretation"), Width = 240 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Excel备注", Binding = new Binding("Comment"), Width = 140 });
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            DockPanel footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _refresh = MakeButton("重新读取全部", Refresh_Click, 120);
            buttons.Children.Add(_refresh);
            buttons.Children.Add(MakeButton("复制当前列表", Copy_Click, 120));
            buttons.Children.Add(MakeButton("关闭", (sender, args) => Close(), 80));
            DockPanel.SetDock(buttons, Dock.Right);
            _status = new TextBlock { Text = "等待读取", VerticalAlignment = VerticalAlignment.Center };
            footer.Children.Add(buttons);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            Loaded += async (sender, args) => await RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) { await RefreshAsync(); }

        private async Task RefreshAsync()
        {
            _refresh.IsEnabled = false;
            _status.Text = "正在整块读取5张输入表...";
            try
            {
                _results = (await Task.Run(_reader)).ToList();
                ApplyFilter();
                _status.Text = string.Format(CultureInfo.InvariantCulture, "读取完成：5个表，共{0}个信号。", _results.Count);
            }
            catch (Exception ex)
            {
                _grid.ItemsSource = null;
                _status.Text = "读取失败：" + ex.Message;
            }
            finally { _refresh.IsEnabled = true; }
        }

        private void Filter_Changed(object sender, EventArgs e) { ApplyFilter(); }

        private void ApplyFilter()
        {
            if (_grid == null || _tableFilter == null || _nameFilter == null) return;
            string selected = _tableFilter.SelectedItem as string ?? "全部";
            string search = _nameFilter.Text.Trim();
            IEnumerable<C95InputSignalResult> filtered = _results;
            if (selected != "全部") filtered = filtered.Where(result => selected.StartsWith(result.TableName, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(result =>
                    result.SignalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    result.PortName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            _grid.ItemsSource = filtered.Select(DisplayRow.From).ToList();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<DisplayRow> rows = (_grid.ItemsSource as IEnumerable<DisplayRow>) ?? Enumerable.Empty<DisplayRow>();
            StringBuilder text = new StringBuilder();
            text.AppendLine("表\t表偏移\t信号偏移\t信号名\t端口\t类型\t原始字节\t解析值\t状态解释\tExcel备注");
            foreach (DisplayRow row in rows) text.AppendLine(row.ToCopyText());
            Clipboard.SetText(text.ToString());
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = new Button { Content = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) };
            button.Click += handler;
            return button;
        }

        private sealed class DisplayRow
        {
            public string TableName { get; set; }
            public string TableAddress { get; set; }
            public string SignalOffsetText { get; set; }
            public string SignalName { get; set; }
            public string PortName { get; set; }
            public string DataType { get; set; }
            public string RawBytes { get; set; }
            public string ValueText { get; set; }
            public string Interpretation { get; set; }
            public string Comment { get; set; }

            public static DisplayRow From(C95InputSignalResult result)
            {
                return new DisplayRow
                {
                    TableName = result.TableName,
                    TableAddress = result.TableAddress,
                    SignalOffsetText = string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:X})", result.SignalOffset),
                    SignalName = result.SignalName,
                    PortName = result.PortName,
                    DataType = result.DataType,
                    RawBytes = result.RawBytes,
                    ValueText = result.ValueText,
                    Interpretation = result.Interpretation,
                    Comment = result.Comment
                };
            }

            public string ToCopyText()
            {
                return string.Join("\t", TableName, TableAddress, SignalOffsetText, SignalName, PortName, DataType, RawBytes, ValueText, Interpretation, Comment);
            }
        }
    }
}
