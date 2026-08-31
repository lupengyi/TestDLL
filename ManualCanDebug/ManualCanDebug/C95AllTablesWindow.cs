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
    internal sealed class C95AllTablesWindow : Window
    {
        private readonly Func<IReadOnlyList<C95TableReadResult>> _reader;
        private readonly DataGrid _grid;
        private readonly ComboBox _category;
        private readonly TextBox _search;
        private readonly TextBox _detail;
        private readonly TextBlock _status;
        private readonly Button _refresh;
        private List<C95TableReadResult> _results = new List<C95TableReadResult>();
        private List<C95TableFieldResult> _fields = new List<C95TableFieldResult>();

        public C95AllTablesWindow(Func<IReadOnlyList<C95TableReadResult>> reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Title = "C95 Locator 全部地址表读取";
            Width = 1500;
            Height = 850;
            MinWidth = 1050;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock description = new TextBlock
            {
                Text = "按C95 Locator地址表从0x00读取到0xA8，共43项；所有已定义表均按字段展开，显示字段名、偏移、类型、RAW、解析值和状态说明。单项失败不会中断后续，MPI按二级指针读取。所有TX/RX及字段解析同步写入主LOG。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(description);

            WrapPanel filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            filters.Children.Add(new TextBlock { Text = "分类：", VerticalAlignment = VerticalAlignment.Center });
            _category = new ComboBox { Width = 150, Margin = new Thickness(4, 0, 12, 0) };
            _category.Items.Add("全部");
            foreach (string item in C95AllTableCatalog.Tables.Select(table => table.Category).Distinct()) _category.Items.Add(item);
            _category.SelectedIndex = 0;
            _category.SelectionChanged += FilterChanged;
            filters.Children.Add(_category);
            filters.Children.Add(new TextBlock { Text = "表名：", VerticalAlignment = VerticalAlignment.Center });
            _search = new TextBox { Width = 260, Margin = new Thickness(4, 0, 12, 0), Padding = new Thickness(4, 2, 4, 2) };
            _search.TextChanged += FilterChanged;
            filters.Children.Add(_search);
            Grid.SetRow(filters, 1);
            root.Children.Add(filters);

            _grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionUnit = DataGridSelectionUnit.FullRow };
            _grid.Columns.Add(new DataGridTextColumn { Header = "分类", Binding = new Binding("Category"), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "表偏移", Binding = new Binding("Offset"), Width = 70 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "表名", Binding = new Binding("Name"), Width = 260 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "字段偏移", Binding = new Binding("FieldOffset"), Width = 85 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "字段名", Binding = new Binding("FieldName"), Width = 260 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("DataType"), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "原始字节", Binding = new Binding("Raw"), Width = 155 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "解析值", Binding = new Binding("Value"), Width = 170 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "状态解释", Binding = new Binding("Interpretation"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _grid.SelectionChanged += GridSelectionChanged;
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            _detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(_detail, 3);
            root.Children.Add(_detail);

            DockPanel footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _refresh = MakeButton("重新读取43项", RefreshClick, 130);
            buttons.Children.Add(_refresh);
            buttons.Children.Add(MakeButton("复制当前列表", CopyListClick, 120));
            buttons.Children.Add(MakeButton("复制选中原始数据", CopyRawClick, 150));
            buttons.Children.Add(MakeButton("关闭", (s, e) => Close(), 80));
            DockPanel.SetDock(buttons, Dock.Right);
            _status = new TextBlock { Text = "等待读取", VerticalAlignment = VerticalAlignment.Center };
            footer.Children.Add(buttons);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            Content = root;
            Loaded += async (s, e) => await RefreshAsync();
        }

        private async void RefreshClick(object sender, RoutedEventArgs e) { await RefreshAsync(); }

        private async Task RefreshAsync()
        {
            _refresh.IsEnabled = false;
            _status.Text = "正在逐项读取43个地址表...";
            try
            {
                _results = (await Task.Run(_reader)).ToList();
                _fields = _results.SelectMany(C95TableFieldDecoder.Decode).ToList();
                ApplyFilter();
                _status.Text = string.Format(CultureInfo.InvariantCulture, "完成：43个地址项展开为{0}个字段；表成功{1}，失败{2}，仅指针{3}。",
                    _fields.Count, _results.Count(result => result.Succeeded), _results.Count(result => !result.Succeeded), _results.Count(result => result.Succeeded && !result.Table.HasDefinedLength));
            }
            catch (Exception ex) { _status.Text = "批量读取失败：" + ex.Message; }
            finally { _refresh.IsEnabled = true; }
        }

        private void FilterChanged(object sender, EventArgs e) { ApplyFilter(); }

        private void ApplyFilter()
        {
            string category = _category.SelectedItem as string ?? "全部";
            string search = _search.Text.Trim();
            IEnumerable<C95TableFieldResult> filtered = _fields;
            if (category != "全部") filtered = filtered.Where(result => result.Category == category);
            if (!string.IsNullOrEmpty(search)) filtered = filtered.Where(result => result.TableName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || result.FieldName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            _grid.ItemsSource = filtered.Select(DisplayRow.From).ToList();
        }

        private void GridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DisplayRow row = _grid.SelectedItem as DisplayRow;
            _detail.Text = row == null ? string.Empty : row.Detail;
        }

        private void CopyListClick(object sender, RoutedEventArgs e)
        {
            IEnumerable<DisplayRow> rows = (_grid.ItemsSource as IEnumerable<DisplayRow>) ?? Enumerable.Empty<DisplayRow>();
            StringBuilder text = new StringBuilder("分类\t表偏移\t表名\t字段偏移\t字段名\t类型\t原始字节\t解析值\t状态解释\t产品指针\t读取状态\r\n");
            foreach (DisplayRow row in rows) text.AppendLine(row.CopyText);
            Clipboard.SetText(text.ToString());
        }

        private void CopyRawClick(object sender, RoutedEventArgs e)
        {
            DisplayRow row = _grid.SelectedItem as DisplayRow;
            if (row != null) Clipboard.SetText(row.Detail);
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = new Button { Content = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) };
            button.Click += handler;
            return button;
        }

        private sealed class DisplayRow
        {
            public string Category { get; set; }
            public string Offset { get; set; }
            public string Name { get; set; }
            public string FieldOffset { get; set; }
            public string FieldName { get; set; }
            public string DataType { get; set; }
            public string Pointer { get; set; }
            public string Status { get; set; }
            public string Raw { get; set; }
            public string Value { get; set; }
            public string Interpretation { get; set; }
            public string Detail { get { return Name + "  " + Offset + "\r\n字段：" + FieldName + "  " + FieldOffset + "  " + DataType + "\r\n指针：" + Pointer + "\r\n状态：" + Status + "\r\n解析值：" + Value + "\r\n状态解释：" + Interpretation + "\r\nRAW：" + Raw; } }
            public string CopyText { get { return string.Join("\t", Category, Offset, Name, FieldOffset, FieldName, DataType, Raw, Value, Interpretation, Pointer, Status); } }

            public static DisplayRow From(C95TableFieldResult result)
            {
                return new DisplayRow
                {
                    Category = result.Category,
                    Offset = result.TableAddress,
                    Name = result.TableName,
                    FieldOffset = string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:X})", result.FieldOffset),
                    FieldName = result.FieldName,
                    DataType = result.DataType,
                    Pointer = result.PointerAddress,
                    Status = result.Status,
                    Raw = result.RawBytes,
                    Value = result.ValueText,
                    Interpretation = result.Interpretation
                };
            }
        }
    }
}
