using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class StepSelectionWindow : Window
    {
        private readonly ObservableCollection<StepTemplateRow> _rows;
        private readonly bool _multiSelect;
        private readonly DataGrid _stepGrid;
        private readonly DataGrid _parameterGrid;
        private readonly TextBox _searchBox;
        private readonly ComboBox _categoryBox;
        private readonly ProductLocatorRepository _locatorRepository;
        private ComboBox _productBox;
        private ComboBox _operationBox;
        private ComboBox _tableBox;
        private DataGrid _productSignalGrid;
        private TextBlock _addressText;
        private TextBlock _productCommentText;
        private TextBox _productStepNameBox;
        private TextBox _valueBox;
        private TextBox _lowLimitBox;
        private TextBox _highLimitBox;
        private ComboBox _compareBox;
        private TextBlock _locatorSummaryText;
        private DataGridColumn _tableChangeColumn;
        private DataGridColumn _tableValueColumn;
        private Button _addProductStepButton;

        public StepSelectionWindow(string title, IEnumerable<SequenceStepDefinition> steps, bool multiSelect)
            : this(title, steps, multiSelect, null)
        {
        }

        public StepSelectionWindow(string title, IEnumerable<SequenceStepDefinition> steps, bool multiSelect, ProductLocatorRepository locatorRepository)
        {
            Title = title;
            Width = 980;
            Height = 680;
            MinWidth = 780;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));
            _multiSelect = multiSelect;
            _locatorRepository = locatorRepository;
            _rows = new ObservableCollection<StepTemplateRow>((steps ?? Enumerable.Empty<SequenceStepDefinition>()).Select((step, index) => new StepTemplateRow(index + 1, step)));

            Grid root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            DockPanel filter = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            _categoryBox = new ComboBox { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
            _categoryBox.ItemsSource = new[] { "全部分类" }.Concat(_rows.Select(row => row.Category).Distinct().OrderBy(value => value)).ToArray();
            _categoryBox.SelectedIndex = 0;
            _categoryBox.SelectionChanged += Filter_Changed;
            DockPanel.SetDock(_categoryBox, Dock.Left);
            filter.Children.Add(_categoryBox);
            _searchBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), ToolTip = "搜索测试项名称或FunctionName" };
            _searchBox.TextChanged += Filter_Changed;
            filter.Children.Add(_searchBox);
            Grid.SetRow(filter, 0);
            root.Children.Add(filter);

            _stepGrid = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = true,
                SelectionMode = multiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 253))
            };
            _stepGrid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new Binding("Number"), Width = 65 });
            _stepGrid.Columns.Add(new DataGridTextColumn { Header = "分类", Binding = new Binding("Category"), Width = 145 });
            _stepGrid.Columns.Add(new DataGridTextColumn { Header = "测试项", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _stepGrid.Columns.Add(new DataGridTextColumn { Header = "FunctionName", Binding = new Binding("FunctionName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _stepGrid.SelectionChanged += StepGrid_SelectionChanged;
            Grid.SetRow(_stepGrid, 1);
            root.Children.Add(_stepGrid);

            GroupBox preview = new GroupBox { Header = "测试项专属参数预览", Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(6) };
            _parameterGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column };
            _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "参数", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "默认值", Binding = new Binding("Value"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("Type"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            preview.Content = _parameterGrid;
            Grid.SetRow(preview, 2);
            root.Children.Add(preview);

            DockPanel footer = new DockPanel();
            TextBlock hint = new TextBlock
            {
                Text = multiSelect ? "可按 Ctrl/Shift 多选；导入后仍可逐项修改参数。" : "插入后在主界面修改该STEP的专属参数。",
                Foreground = Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(hint);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button ok = new Button { Content = multiSelect ? "导入选中测试项" : "插入测试项", MinWidth = 120, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            ok.Click += Ok_Click;
            Button cancel = new Button { Content = "取消", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);
            if (_locatorRepository != null && !multiSelect)
            {
                TabControl tabs = new TabControl { Margin = new Thickness(8) };
                tabs.Items.Add(new TabItem { Header = "仪器STEP", Content = root });
                tabs.Items.Add(new TabItem { Header = "产品信号STEP", Content = BuildProductStepPage() });
                Content = tabs;
            }
            else Content = root;
            if (_rows.Count > 0) _stepGrid.SelectedIndex = 0;
        }

        public IReadOnlyList<SequenceStepDefinition> SelectedSteps { get; private set; } = new List<SequenceStepDefinition>().AsReadOnly();

        private void Filter_Changed(object sender, EventArgs e)
        {
            string category = Convert.ToString(_categoryBox.SelectedItem, CultureInfo.InvariantCulture) ?? "全部分类";
            string keyword = (_searchBox.Text ?? string.Empty).Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(_stepGrid.ItemsSource);
            view.Filter = item =>
            {
                StepTemplateRow row = item as StepTemplateRow;
                if (row == null) return false;
                bool categoryMatch = category == "全部分类" || row.Category == category;
                bool textMatch = keyword.Length == 0 || row.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || row.FunctionName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                return categoryMatch && textMatch;
            };
            view.Refresh();
        }

        private void StepGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StepTemplateRow row = _stepGrid.SelectedItem as StepTemplateRow;
            _parameterGrid.ItemsSource = row == null
                ? null
                : row.Definition.Parameters.Select(pair => new ParameterPreviewRow(pair.Key, pair.Value)).ToList();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            List<StepTemplateRow> selected = _multiSelect
                ? _stepGrid.SelectedItems.Cast<StepTemplateRow>().OrderBy(row => row.Number).ToList()
                : new[] { _stepGrid.SelectedItem as StepTemplateRow }.Where(row => row != null).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请至少选择一个测试项。", "测试项", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedSteps = selected.Select(row => SequenceEditing.Clone(row.Definition)).ToList().AsReadOnly();
            DialogResult = true;
        }

        private UIElement BuildProductStepPage()
        {
            Grid page = new Grid { Margin = new Thickness(12) };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            DockPanel title = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            Button import = new Button { Content = "导入新产品Locator...", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            import.Click += ImportLocator_Click;
            DockPanel.SetDock(import, Dock.Right);
            title.Children.Add(import);
            StackPanel titleText = new StackPanel();
            titleText.Children.Add(new TextBlock { Text = "产品Locator信号STEP", FontSize = 16, FontWeight = FontWeights.SemiBold });
            _locatorSummaryText = new TextBlock { Foreground = Brushes.DimGray, Margin = new Thickness(0, 3, 0, 0) };
            titleText.Children.Add(_locatorSummaryText);
            title.Children.Add(titleText);
            page.Children.Add(title);

            WrapPanel selector = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), VerticalAlignment = VerticalAlignment.Center };
            selector.Children.Add(Label("产品"));
            _productBox = new ComboBox { DisplayMemberPath = "Product", Width = 105, Margin = new Thickness(3) };
            _productBox.SelectionChanged += ProductBox_SelectionChanged;
            selector.Children.Add(_productBox);
            selector.Children.Add(Label("操作"));
            _operationBox = new ComboBox { ItemsSource = new[] { "读取信号", "写入信号", "读取整表", "写入整表" }, SelectedIndex = 0, Width = 115, Margin = new Thickness(3) };
            _operationBox.SelectionChanged += OperationBox_SelectionChanged;
            selector.Children.Add(_operationBox);
            selector.Children.Add(Label("表名"));
            _tableBox = new ComboBox { DisplayMemberPath = "DisplayName", Width = 360, Margin = new Thickness(3) };
            _tableBox.SelectionChanged += TableBox_SelectionChanged;
            selector.Children.Add(_tableBox);
            selector.Children.Add(Label("表地址"));
            _addressText = new TextBlock { MinWidth = 70, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(32, 104, 190)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) };
            selector.Children.Add(_addressText);
            Grid.SetRow(selector, 1); page.Children.Add(selector);

            _productSignalGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "信号名称", Binding = new Binding("Name"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star), IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "Offset(Hex)", Binding = new Binding("OffsetHex"), Width = 90, IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "Offset(Dec)", Binding = new Binding("Offset"), Width = 90, IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "数据类型", Binding = new Binding("DataType"), Width = 115, IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "DataSize", Binding = new Binding("DataSize"), Width = 80, IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 85, IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "读写", Binding = new Binding("Access"), Width = 75, IsReadOnly = true });
            _productSignalGrid.Columns.Add(new DataGridTextColumn { Header = "Locator Comment", Binding = new Binding("Comment"), Width = new DataGridLength(2.5, DataGridLengthUnitType.Star), IsReadOnly = true });
            _tableChangeColumn = DataGridCheckHelpers.BoundCheckColumn("修改", "SelectedForWrite", 55); _tableChangeColumn.Visibility = Visibility.Collapsed;
            _tableValueColumn = new DataGridTextColumn { Header = "整表设定值", Binding = new Binding("ValueText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 110, Visibility = Visibility.Collapsed };
            _productSignalGrid.Columns.Insert(0, _tableChangeColumn);
            _productSignalGrid.Columns.Insert(7, _tableValueColumn);
            _productSignalGrid.SelectionChanged += ProductSignalGrid_SelectionChanged;
            Grid.SetRow(_productSignalGrid, 2); page.Children.Add(_productSignalGrid);

            GroupBox settings = new GroupBox { Header = "生成STEP参数", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
            WrapPanel settingsRow = new WrapPanel();
            settingsRow.Children.Add(Label("STEP名称")); _productStepNameBox = Box(260); settingsRow.Children.Add(_productStepNameBox);
            settingsRow.Children.Add(Label("写入值")); _valueBox = Box(90, "0"); settingsRow.Children.Add(_valueBox);
            settingsRow.Children.Add(Label("下限")); _lowLimitBox = Box(80, "0"); settingsRow.Children.Add(_lowLimitBox);
            settingsRow.Children.Add(Label("上限")); _highLimitBox = Box(80, "0"); settingsRow.Children.Add(_highLimitBox);
            settingsRow.Children.Add(Label("比较")); _compareBox = new ComboBox { Width = 90, ItemsSource = new[] { "GELE", "GTLT", "EQ", "NE" }, SelectedIndex = 0, Margin = new Thickness(3) }; settingsRow.Children.Add(_compareBox);
            settings.Content = settingsRow;
            Grid.SetRow(settings, 3); page.Children.Add(settings);

            TextBlock explanation = new TextBlock
            {
                Text = "读取/写入均生成类型化 FCT_CANSignal。AddrOffset来自FT IO地址表，TableIndex来自信号Offset，DataSize和DataType按Locator自动计算；写入后默认回读校验。",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(238, 247, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(38, 92, 145))
            };
            Grid.SetRow(explanation, 4); page.Children.Add(explanation);

            _productCommentText = new TextBlock { Text = "在上方信号表格中选择一行。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 4), Foreground = Brushes.DimGray };
            settingsRow.Children.Add(_productCommentText);

            StackPanel footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _addProductStepButton = new Button { Content = "添加产品信号STEP", MinWidth = 140, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromRgb(32, 104, 190)), Foreground = Brushes.White };
            _addProductStepButton.Click += AddProductStep_Click;
            Button cancel = new Button { Content = "取消", MinWidth = 80, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4) };
            cancel.Click += (s, e) => DialogResult = false;
            footer.Children.Add(_addProductStepButton); footer.Children.Add(cancel);
            Grid.SetRow(footer, 5); page.Children.Add(footer);
            RefreshProducts();
            UpdateOperationFields();
            return page;
        }

        private void RefreshProducts()
        {
            _productBox.ItemsSource = _locatorRepository.Products;
            if (_productBox.Items.Count > 0) _productBox.SelectedIndex = 0;
            _locatorSummaryText.Text = _locatorRepository.Products.Count + " 个产品Locator已加载；C92当前复用C96。";
        }

        private void ProductBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshTables(); }
        private void OperationBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshTables(); UpdateOperationFields(); }
        private void UpdateOperationFields()
        {
            if (_valueBox == null) return;
            string operation = Convert.ToString(_operationBox.SelectedItem, CultureInfo.InvariantCulture) ?? "读取信号";
            bool signalWrite = operation == "写入信号", signalRead = operation == "读取信号", tableWrite = operation == "写入整表";
            _valueBox.IsEnabled = signalWrite;
            _lowLimitBox.IsEnabled = signalRead;
            _highLimitBox.IsEnabled = signalRead;
            _compareBox.IsEnabled = signalRead;
            _tableChangeColumn.Visibility = tableWrite ? Visibility.Visible : Visibility.Collapsed;
            _tableValueColumn.Visibility = tableWrite ? Visibility.Visible : Visibility.Collapsed;
            _productSignalGrid.SelectionMode = operation.EndsWith("整表", StringComparison.Ordinal) ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;
            _addProductStepButton.Content = operation.EndsWith("整表", StringComparison.Ordinal) ? "添加产品整表STEP" : "添加产品信号STEP";
        }
        private void RefreshTables()
        {
            ProductLocatorDefinition product = _productBox == null ? null : _productBox.SelectedItem as ProductLocatorDefinition;
            if (product == null || _tableBox == null) return;
            bool write = (Convert.ToString(_operationBox.SelectedItem, CultureInfo.InvariantCulture) ?? string.Empty).StartsWith("写入", StringComparison.Ordinal);
            _tableBox.ItemsSource = product.Tables.Where(table => !write || table.CanWrite).ToList();
            if (_tableBox.Items.Count > 0) _tableBox.SelectedIndex = 0;
        }
        private void TableBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ProductLocatorTable table = _tableBox.SelectedItem as ProductLocatorTable;
            _productSignalGrid.ItemsSource = table == null ? null : table.Signals.Select(signal => new ProductSignalRow(signal, table.CanWrite)).ToList();
            if (_productSignalGrid.Items.Count > 0) _productSignalGrid.SelectedIndex = 0;
            _addressText.Text = table == null ? string.Empty : "0x" + table.AddressOffset.ToString("X2", CultureInfo.InvariantCulture);
        }
        private void ProductSignalGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ProductSignalRow selected = _productSignalGrid.SelectedItem as ProductSignalRow;
            ProductLocatorSignal signal = selected == null ? null : selected.Signal;
            ProductLocatorDefinition product = _productBox.SelectedItem as ProductLocatorDefinition;
            if (signal == null) return;
            string operation = Convert.ToString(_operationBox.SelectedItem, CultureInfo.InvariantCulture) ?? "读取信号";
            _productStepNameBox.Text = (operation.StartsWith("写入", StringComparison.Ordinal) ? "Write " : "Read ") + (product == null ? string.Empty : product.Product + " ") + (operation.EndsWith("整表", StringComparison.Ordinal) ? (_tableBox.SelectedItem as ProductLocatorTable)?.Name : signal.Name);
            _productCommentText.Text = string.IsNullOrWhiteSpace(signal.Comment) ? "Locator未提供Comment。" : signal.Comment;
        }

        private void AddProductStep_Click(object sender, RoutedEventArgs e)
        {
            ProductLocatorDefinition product = _productBox.SelectedItem as ProductLocatorDefinition;
            ProductLocatorTable table = _tableBox.SelectedItem as ProductLocatorTable;
            ProductSignalRow selected = _productSignalGrid.SelectedItem as ProductSignalRow;
            ProductLocatorSignal signal = selected == null ? null : selected.Signal;
            string operation = Convert.ToString(_operationBox.SelectedItem, CultureInfo.InvariantCulture) ?? "读取信号";
            bool write = operation.StartsWith("写入", StringComparison.Ordinal), tableOperation = operation.EndsWith("整表", StringComparison.Ordinal);
            if (product == null || table == null || (!tableOperation && signal == null)) { MessageBox.Show(this, "请选择产品、数据表和信号。", "产品STEP", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            double low = 0, high = 0;
            if (operation == "读取信号" && (!double.TryParse(_lowLimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out low) || !double.TryParse(_highLimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out high)))
            { MessageBox.Show(this, "读取上下限必须是有效数字。", "产品STEP", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string fallbackName = (write ? "Write " : "Read ") + (tableOperation ? table.Name : signal.Name);
            string stepName = string.IsNullOrWhiteSpace(_productStepNameBox.Text) ? fallbackName : _productStepNameBox.Text.Trim();
            SequenceStepDefinition definition;
            if (operation == "读取整表") definition = ProductSignalStepFactory.CreateTableRead(stepName, table);
            else if (operation == "写入整表")
            {
                _productSignalGrid.CommitEdit(DataGridEditingUnit.Cell, true); _productSignalGrid.CommitEdit(DataGridEditingUnit.Row, true);
                List<KeyValuePair<ProductLocatorSignal, string>> changes = _productSignalGrid.Items.Cast<ProductSignalRow>().Where(row => row.SelectedForWrite).Select(row => new KeyValuePair<ProductLocatorSignal, string>(row.Signal, row.ValueText)).ToList();
                definition = ProductSignalStepFactory.CreateTableWrite(stepName, table, changes);
            }
            else if (write) definition = ProductSignalStepFactory.CreateWrite(stepName, table, signal, _valueBox.Text.Trim());
            else definition = ProductSignalStepFactory.CreateRead(stepName, table, signal, low, high, Convert.ToString(_compareBox.SelectedItem, CultureInfo.InvariantCulture) ?? "GELE");
            SelectedSteps = new[] { definition }.ToList().AsReadOnly();
            DialogResult = true;
        }

        private void ImportLocator_Click(object sender, RoutedEventArgs e)
        {
            ProductLocatorImportWindow dialog = new ProductLocatorImportWindow { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try { _locatorRepository.Import(dialog.Product, dialog.LocatorPath); RefreshProducts(); }
            catch (Exception ex) { MessageBox.Show(this, "Locator解析/导入失败：\n" + ex.Message, "导入Locator", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static TextBlock Label(string text) { return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 3, 2, 3) }; }
        private static TextBox Box(double width, string text = "") { return new TextBox { Width = width, Text = text, Margin = new Thickness(3), Padding = new Thickness(5, 3, 5, 3) }; }
    }

    internal sealed class StepTemplateRow
    {
        public StepTemplateRow(int number, SequenceStepDefinition definition)
        {
            Number = number;
            Definition = definition;
        }
        public int Number { get; private set; }
        public SequenceStepDefinition Definition { get; private set; }
        public string Name { get { return Definition.StepName; } }
        public string FunctionName { get { return Definition.FunctionName; } }
        public string Category { get { return InstrumentStepCatalog.CategoryFor(Definition); } }
    }

    internal sealed class ProductSignalRow : INotifyPropertyChanged
    {
        private bool _selectedForWrite;
        private string _valueText = "0";
        public ProductSignalRow(ProductLocatorSignal signal, bool writable)
        {
            Signal = signal;
            Access = writable ? "读 / 写" : "只读";
        }
        public ProductLocatorSignal Signal { get; private set; }
        public string Name { get { return Signal.Name; } }
        public int Offset { get { return Signal.Offset; } }
        public string OffsetHex { get { return "0x" + Signal.Offset.ToString("X", CultureInfo.InvariantCulture); } }
        public string DataType { get { return Signal.DataType; } }
        public int DataSize { get { return Signal.DataSize; } }
        public string Unit { get { return Signal.Unit; } }
        public string Comment { get { return Signal.Comment; } }
        public string Access { get; private set; }
        public bool SelectedForWrite { get { return _selectedForWrite; } set { _selectedForWrite = value; Raise("SelectedForWrite"); } }
        public string ValueText { get { return _valueText; } set { _valueText = value ?? string.Empty; Raise("ValueText"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class ParameterPreviewRow
    {
        public ParameterPreviewRow(string name, object value)
        {
            Name = name;
            Value = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
            Type = value == null ? "Text" : value.GetType().Name;
        }
        public string Name { get; private set; }
        public string Value { get; private set; }
        public string Type { get; private set; }
    }
}
