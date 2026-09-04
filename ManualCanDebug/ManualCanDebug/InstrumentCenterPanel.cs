using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ManualCanDebug.Core;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    internal sealed class InstrumentCenterPanel : Grid
    {
        private readonly string _configDirectory;
        private readonly string _configPath;
        private readonly string _catalogPath;
        private readonly string _profilesDirectory;
        private readonly Func<SequenceStepDefinition, Task<string>> _executeStep;
        private readonly Action<string> _log;
        private readonly Action _configSaved;
        private readonly Func<string, Task> _initializeSelected;
        private readonly ObservableCollection<InstrumentConfigRow> _configRows = new ObservableCollection<InstrumentConfigRow>();
        private readonly ObservableCollection<InstrumentActionRow> _actionRows = new ObservableCollection<InstrumentActionRow>();
        private readonly ObservableCollection<LiveParameterRow> _parameters = new ObservableCollection<LiveParameterRow>();
        private DataGrid _configGrid;
        private DataGrid _actionGrid;
        private DataGrid _parameterGrid;
        private TabControl _instrumentActionTabs;
        private TextBox _resultBox;
        private SequenceStepDefinition _editingAction;
        private TabControl _workspaceTabs;
        private InstrumentWorkspaceDesigner _workspaceDesigner;
        private CheckBox _initializeSelectAllCheck;
        private bool _syncingInitializeSelectAll;

        public InstrumentCenterPanel(string baseDirectory, Func<SequenceStepDefinition, Task<string>> executeStep, Action<string> log, Action configSaved, Func<string, Task> initializeSelected)
        {
            _configDirectory = Path.Combine(baseDirectory, "Config");
            _configPath = Path.Combine(_configDirectory, "InstrumentConfig.json");
            _catalogPath = Path.Combine(_configDirectory, "InstrumentCatalog.json");
            _profilesDirectory = Path.Combine(_configDirectory, "InstrumentProfiles");
            _executeStep = executeStep ?? throw new ArgumentNullException(nameof(executeStep));
            _log = log ?? delegate { };
            _configSaved = configSaved ?? delegate { };
            _initializeSelected = initializeSelected ?? throw new ArgumentNullException(nameof(initializeSelected));
            Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));
            BuildUi();
            LoadConfiguration();
        }

        public void RefreshTemplates(IEnumerable<SequenceStepDefinition> steps)
        {
            _actionRows.Clear();
            IEnumerable<SequenceStepDefinition> catalog = ActionCatalog.Descriptors
                .Where(value => value.Source == "仪器" || value.Source == "产品内部通信")
                .Select(ActionConfigurationPanel.CreateFromDescriptor);
            IEnumerable<SequenceStepDefinition> merged = (steps ?? Enumerable.Empty<SequenceStepDefinition>()).Concat(catalog);
            foreach (SequenceStepDefinition template in SequenceEditing.BuildFunctionTemplates(merged))
                _actionRows.Add(new InstrumentActionRow(template));
            RefreshInstrumentActionTabs();
            if (_actionRows.Count > 0) _actionGrid.SelectedIndex = 0;
        }

        private void BuildUi()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock header = new TextBlock
            {
                Text = "仪器中心",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 48, 65)),
                Margin = new Thickness(12, 10, 12, 6)
            };
            Children.Add(header);

            TabControl tabs = new TabControl { Margin = new Thickness(12, 4, 12, 10), ItemContainerStyle = StudioTabStyleFactory.Create(12) };
            _workspaceTabs = tabs;
            InstrumentWorkspaceDesigner workspace = new InstrumentWorkspaceDesigner(AppDomain.CurrentDomain.BaseDirectory, _log, delegate { _configSaved(); }, _initializeSelected);
            _workspaceDesigner = workspace;
            tabs.Items.Add(new TabItem { Header = "仪器中心", Content = workspace.BuildProjectInstrumentPage() });
            tabs.Items.Add(new TabItem { Header = "工位仪器配置", Content = workspace.BuildStationConfigurationPage() });
            tabs.Items.Add(new TabItem { Header = "兼容配置", Content = BuildConfigurationPage() });
            tabs.Items.Add(new TabItem { Header = "实时设置 / 单步执行", Content = BuildLiveControlPage() });
            Grid.SetRow(tabs, 1);
            Children.Add(tabs);
        }

        internal void SelectWorkspaceTab(int index)
        {
            if (_workspaceTabs != null && index >= 0 && index < _workspaceTabs.Items.Count) _workspaceTabs.SelectedIndex = index;
        }

        internal void SelectInstrumentForPreview(string device)
        {
            if (_workspaceDesigner != null) _workspaceDesigner.SelectInstrumentByDevice(device);
        }

        private UIElement BuildConfigurationPage()
        {
            Grid page = new Grid { Margin = new Thickness(8) };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock note = new TextBlock
            {
                Text = "通过编辑 InstrumentConfig.json 或下方按钮增删仪器。勾选“本次初始化”决定连接哪些仪器。FST006 四路CAN：DUTCAN=调试(.17 CAN0)、MAINCAN=主驱(.17 CAN1)、AUXCAN=辅驱(.18 CAN0)、RESOLVERCAN=旋变(.19 CAN0)。参数格式：DeviceType,Channel,BaudRate,Port,DeviceIndex。",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(255, 247, 226)),
                Foreground = new SolidColorBrush(Color.FromRgb(130, 84, 18)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            page.Children.Add(note);

            _configGrid = new DataGrid
            {
                ItemsSource = _configRows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
                SelectionUnit = DataGridSelectionUnit.FullRow
            };
            _configGrid.Columns.Add(CreateInitializeCheckColumn());
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "仪器", Binding = new Binding("Name"), Width = 105, IsReadOnly = true });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "仪器说明", Binding = new Binding("Comment") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 320 });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "MainTest状态", Binding = new Binding("ConnectionStatus"), Width = 105, IsReadOnly = true });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("Type"), Width = 110, IsReadOnly = true });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "驱动模式", Binding = new Binding("Mode"), Width = 105, IsReadOnly = true });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "Resource（可编辑）", Binding = new Binding("Resource") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 280 });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "Parameter（可编辑）", Binding = new Binding("Parameter") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 180 });
            _configGrid.Columns.Add(new DataGridTextColumn { Header = "生效方式", Binding = new Binding("EffectiveScope"), Width = 220, IsReadOnly = true });
            _configRows.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null) foreach (InstrumentConfigRow row in e.NewItems.OfType<InstrumentConfigRow>()) row.PropertyChanged += ConfigRow_PropertyChanged;
                if (e.OldItems != null) foreach (InstrumentConfigRow row in e.OldItems.OfType<InstrumentConfigRow>()) row.PropertyChanged -= ConfigRow_PropertyChanged;
                SyncInitializeSelectAllHeader();
            };
            Grid.SetRow(_configGrid, 1);
            page.Children.Add(_configGrid);

            WrapPanel buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            Button addFromCatalog = new Button { Content = "从目录添加", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            addFromCatalog.Click += AddFromCatalog_Click;
            Button removeSelected = new Button { Content = "删除选中", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            removeSelected.Click += RemoveSelected_Click;
            Button loadProfile = new Button { Content = "加载配置方案", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            loadProfile.Click += LoadProfile_Click;
            Button reload = new Button { Content = "重新读取配置", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            reload.Click += (s, e) => LoadConfiguration();
            Button save = new Button { Content = "保存仪器配置", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromRgb(32, 104, 190)), Foreground = Brushes.White };
            save.Click += SaveConfiguration_Click;
            Button selectAll = new Button { Content = "全选", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) }; selectAll.Click += (s, e) => SetAllInitialize(true);
            Button clear = new Button { Content = "清空选择", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) }; clear.Click += (s, e) => SetAllInitialize(false);
            Button initialize = new Button { Content = "初始化已勾选仪器", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromRgb(37, 145, 91)), Foreground = Brushes.White }; initialize.Click += async (s, e) => await InitializeSelectedAsync();
            buttons.Children.Add(addFromCatalog);
            buttons.Children.Add(removeSelected);
            buttons.Children.Add(loadProfile);
            buttons.Children.Add(reload);
            buttons.Children.Add(selectAll);
            buttons.Children.Add(clear);
            buttons.Children.Add(save);
            buttons.Children.Add(initialize);
            Grid.SetRow(buttons, 2);
            page.Children.Add(buttons);
            return page;
        }

        private UIElement BuildLiveControlPage()
        {
            Grid page = new Grid { Margin = new Thickness(8) };
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid left = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _instrumentActionTabs = new TabControl { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(214, 224, 236)), BorderThickness = new Thickness(1), MinHeight = 42 };
            _instrumentActionTabs.SelectionChanged += InstrumentActionTabs_SelectionChanged;
            left.Children.Add(_instrumentActionTabs);
            _actionGrid = new DataGrid { ItemsSource = _actionRows, AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 253)) };
            _actionGrid.Columns.Add(new DataGridTextColumn { Header = "仪器", Binding = new Binding("Category"), Width = 125 });
            _actionGrid.Columns.Add(new DataGridTextColumn { Header = "已开发STEP", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _actionGrid.Columns.Add(new DataGridTextColumn { Header = "FunctionName", Binding = new Binding("FunctionName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _actionGrid.SelectionChanged += ActionGrid_SelectionChanged;
            Grid.SetRow(_actionGrid, 1);
            left.Children.Add(_actionGrid);
            page.Children.Add(left);

            Grid right = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
            TextBlock liveNote = new TextBlock
            {
                Text = "选择已开发STEP后只显示该STEP的参数。点击立即执行会调用原TestDllMain单步逻辑。整段流程运行时禁止并发操作仪器。",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(238, 247, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(38, 92, 145)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            right.Children.Add(liveNote);
            _parameterGrid = new DataGrid { ItemsSource = _parameters, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 253)) };
            _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "参数", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
            _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "实时设定值", Binding = new Binding("ValueText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("TypeName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            Grid.SetRow(_parameterGrid, 1);
            right.Children.Add(_parameterGrid);
            Button execute = new Button { Content = "立即执行当前仪器STEP", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(37, 145, 91)), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
            execute.Click += Execute_Click;
            Grid.SetRow(execute, 2);
            right.Children.Add(execute);
            _resultBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(249, 251, 253)), FontFamily = new FontFamily("Consolas") };
            Grid.SetRow(_resultBox, 3);
            right.Children.Add(_resultBox);
            Grid.SetColumn(right, 1);
            page.Children.Add(right);
            return page;
        }

        private void RefreshInstrumentActionTabs()
        {
            if (_instrumentActionTabs == null) return;
            string selected = (_instrumentActionTabs.SelectedItem as TabItem)?.Tag as string;
            _instrumentActionTabs.Items.Clear();
            IEnumerable<string> categories = new[] { "全部仪器" }.Concat(_actionRows.Select(row => row.Category).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().OrderBy(value => value));
            foreach (string category in categories)
            {
                int count = category == "全部仪器" ? _actionRows.Count : _actionRows.Count(row => row.Category == category);
                StackPanel header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 1, 4, 1) };
                header.Children.Add(new TextBlock { Text = InstrumentGlyph(category), FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = new SolidColorBrush(Color.FromRgb(35, 105, 190)), FontSize = 15, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                header.Children.Add(new TextBlock { Text = category + "  " + count, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                _instrumentActionTabs.Items.Add(new TabItem { Header = header, Tag = category, ToolTip = "显示" + category + "可直接执行的MainTest动作" });
            }
            _instrumentActionTabs.SelectedItem = _instrumentActionTabs.Items.Cast<TabItem>().FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.Ordinal)) ?? _instrumentActionTabs.Items.Cast<TabItem>().FirstOrDefault();
        }

        private static string InstrumentGlyph(string category)
        {
            string value = (category ?? string.Empty).ToUpperInvariant();
            if (value.Contains("CAN") || value.Contains("产品")) return "\uE8D4";
            if (value.Contains("电源") || value.Contains("LVDC") || value.Contains("HVDC")) return "\uE945";
            if (value.Contains("DMM") || value.Contains("DAQ")) return "\uE9D9";
            if (value.Contains("继电器") || value.Contains("RELAY")) return "\uE950";
            if (value.Contains("负载") || value.Contains("DCDC")) return "\uE7E8";
            if (value.Contains("旋变")) return "\uE7AD";
            return "\uE713";
        }

        private void InstrumentActionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_actionGrid == null || _instrumentActionTabs == null) return;
            TabItem selected = _instrumentActionTabs.SelectedItem as TabItem;
            string category = selected == null ? "全部仪器" : selected.Tag as string ?? "全部仪器";
            ICollectionView view = CollectionViewSource.GetDefaultView(_actionGrid.ItemsSource);
            view.Filter = item => category == "全部仪器" || ((InstrumentActionRow)item).Category == category;
            view.Refresh();
            if (_actionGrid.Items.Count > 0) _actionGrid.SelectedIndex = 0;
        }

        private void LoadConfiguration()
        {
            _configRows.Clear();
            if (File.Exists(_configPath))
            {
                JArray array = JArray.Parse(File.ReadAllText(_configPath));
                foreach (JObject item in array.OfType<JObject>())
                    _configRows.Add(InstrumentConfigRow.FromJson(item));
            }
            ApplyEffectiveScopeNotes();
            _log("仪器配置已读取：" + _configPath);
        }

        private void ApplyEffectiveScopeNotes()
        {
            foreach (InstrumentConfigRow row in _configRows)
            {
                if (!row.Persisted) continue;
                JObject catalog = FindCatalogEntry(row.Name) ?? FindCatalogEntryByPrefix(row.Name);
                if (catalog != null)
                {
                    string comment = (string)catalog["Comment"] ?? string.Empty;
                    string seqDevice = (string)catalog["SeqDevice"] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(row.Comment) && !string.IsNullOrWhiteSpace(comment))
                        row.Comment = comment;
                    row.EffectiveScope = string.IsNullOrWhiteSpace(seqDevice) ? "保存后重新初始化生效" : "SEQ Device=" + seqDevice + "；保存后重新初始化生效";
                }
                else row.EffectiveScope = "未在 InstrumentCatalog.json 登记；MainTest 可能不支持";
            }
        }

        private JObject FindCatalogEntryByPrefix(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            // RES_1 / DMM_HV 等实例名回落到目录中的基础仪器说明
            string[] aliases = { "RES", "DMM", "LVDC", "HVDC", "RELAY", "CAN" };
            foreach (string alias in aliases)
            {
                if (name.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                {
                    JObject exact = FindCatalogEntry(alias);
                    if (exact != null) return exact;
                }
            }
            return null;
        }

        private List<JObject> LoadCatalogEntries()
        {
            if (!File.Exists(_catalogPath)) return new List<JObject>();
            JObject root = JObject.Parse(File.ReadAllText(_catalogPath));
            return root["Instruments"] == null ? new List<JObject>() : root["Instruments"].OfType<JObject>().ToList();
        }

        private JObject FindCatalogEntry(string name)
        {
            return LoadCatalogEntries().FirstOrDefault(item => string.Equals((string)item["Name"], name, StringComparison.OrdinalIgnoreCase));
        }

        private int NextUniqueIndex()
        {
            return _configRows.Count == 0 ? 0 : _configRows.Max(row => row.UniqueIndex) + 1;
        }

        private void AddFromCatalog_Click(object sender, RoutedEventArgs e)
        {
            List<JObject> catalog = LoadCatalogEntries();
            if (catalog.Count == 0)
            {
                MessageBox.Show("找不到仪器目录：\n" + _catalogPath, "仪器中心", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            HashSet<string> existing = new HashSet<string>(_configRows.Select(row => row.Name), StringComparer.OrdinalIgnoreCase);
            List<JObject> available = catalog.Where(item => !existing.Contains((string)item["Name"] ?? string.Empty)).ToList();
            if (available.Count == 0)
            {
                MessageBox.Show("目录中的仪器已全部加入当前配置。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Window dialog = new Window
            {
                Title = "从目录添加仪器",
                Width = 520,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };
            ListBox list = new ListBox { Margin = new Thickness(12), DisplayMemberPath = "Display" };
            foreach (JObject item in available)
            {
                string display = (string)item["Name"] + "  ·  " + (string)item["Type"] + "/" + (string)item["Mode"];
                list.Items.Add(new { Display = display, Entry = item });
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            Button ok = new Button { Content = "添加", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4), IsDefault = true };
            Button cancel = new Button { Content = "取消", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4), IsCancel = true };
            ok.Click += (s, args) => { dialog.DialogResult = true; dialog.Close(); };
            cancel.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };
            Grid root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(list);
            WrapPanel footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(ok);
            footer.Children.Add(cancel);
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);
            dialog.Content = root;
            if (dialog.ShowDialog() != true || list.SelectedItem == null) return;
            JObject selected = ((dynamic)list.SelectedItem).Entry;
            _configRows.Add(InstrumentConfigRow.FromCatalog(NextUniqueIndex(), selected, false));
            ApplyEffectiveScopeNotes();
            _log("已从目录添加仪器：" + (string)selected["Name"]);
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            InstrumentConfigRow row = _configGrid.SelectedItem as InstrumentConfigRow;
            if (row == null)
            {
                MessageBox.Show("请先在表格中选中要删除的仪器。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show("确定从当前配置中删除仪器 “" + row.Name + "”？\n保存后生效；不会修改 InstrumentCatalog.json。", "仪器中心", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _configRows.Remove(row);
            _log("已从当前配置移除仪器：" + row.Name);
        }

        private void LoadProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(_profilesDirectory))
            {
                MessageBox.Show("找不到配置方案目录：\n" + _profilesDirectory, "仪器中心", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string[] files = Directory.GetFiles(_profilesDirectory, "*.json");
            if (files.Length == 0)
            {
                MessageBox.Show("InstrumentProfiles 目录下没有 .json 配置方案。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Window dialog = new Window
            {
                Title = "加载配置方案",
                Width = 560,
                Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };
            ListBox list = new ListBox { Margin = new Thickness(12), DisplayMemberPath = "Display" };
            foreach (string file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    JObject profile = JObject.Parse(File.ReadAllText(file));
                    string name = (string)profile["ProfileName"] ?? Path.GetFileNameWithoutExtension(file);
                    string description = (string)profile["Description"] ?? string.Empty;
                    list.Items.Add(new { Display = name + (string.IsNullOrWhiteSpace(description) ? string.Empty : "  ·  " + description), FilePath = file });
                }
                catch (Exception ex)
                {
                    list.Items.Add(new { Display = Path.GetFileName(file) + "  [解析失败: " + ex.Message + "]", FilePath = file });
                }
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            Button ok = new Button { Content = "加载", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4), IsDefault = true };
            Button cancel = new Button { Content = "取消", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4), IsCancel = true };
            ok.Click += (s, args) => { dialog.DialogResult = true; dialog.Close(); };
            cancel.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };
            Grid root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock hint = new TextBlock
            {
                Text = "加载方案会替换当前表格内容。建议加载后检查 Resource/Parameter，再点“保存仪器配置”。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 4, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(130, 84, 18))
            };
            root.Children.Add(hint);
            Grid.SetRow(list, 1);
            root.Children.Add(list);
            WrapPanel footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(ok);
            footer.Children.Add(cancel);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            dialog.Content = root;
            if (dialog.ShowDialog() != true || list.SelectedItem == null) return;
            string path = ((dynamic)list.SelectedItem).FilePath;
            try
            {
                JObject profile = JObject.Parse(File.ReadAllText(path));
                JArray instruments = profile["Instruments"] as JArray;
                if (instruments == null || instruments.Count == 0) throw new InvalidOperationException("配置方案中没有 Instruments 数组。");
                _configRows.Clear();
                int index = 0;
                foreach (JObject item in instruments.OfType<JObject>())
                {
                    if (item["UniqueIndex"] == null) item["UniqueIndex"] = index;
                    _configRows.Add(InstrumentConfigRow.FromJson(item));
                    index++;
                }
                ApplyEffectiveScopeNotes();
                _log("已加载配置方案：" + path);
                MessageBox.Show("配置方案已加载到表格。\n请检查 Resource/Parameter 后点击“保存仪器配置”。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载配置方案失败：\n" + ex.Message, "仪器中心", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveConfiguration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _configGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                _configGrid.CommitEdit(DataGridEditingUnit.Row, true);
                JArray array = new JArray(_configRows.Where(row => row.Persisted).OrderBy(row => row.UniqueIndex).Select(row => row.ToJson()));
                File.WriteAllText(_configPath, array.ToString());
                _log("仪器配置已保存：" + _configPath);
                _configSaved();
                MessageBox.Show("配置已保存。若仪器已经初始化，请先安全下电，再重新初始化。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存仪器配置失败：\n" + ex.Message, "仪器中心", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task InitializeSelectedAsync()
        {
            try
            {
                _configGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                _configGrid.CommitEdit(DataGridEditingUnit.Row, true);
                List<InstrumentConfigRow> selected = _configRows.Where(row => row.Initialize).ToList();
                if (selected.Count == 0) throw new InvalidOperationException("请先在仪器中心勾选至少一个需要初始化的仪器。");
                JArray payload = new JArray(selected.Select(row => new JObject { ["Name"] = row.Name, ["Type"] = row.Type, ["Mode"] = row.Mode, ["Resource"] = row.Resource, ["Parameter"] = row.Parameter }));
                await _initializeSelected(payload.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            {
                _log("初始化已勾选仪器失败：" + ex.Message);
                MessageBox.Show("初始化已勾选仪器失败：\n" + ex.Message, "仪器中心", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SetInitializedInstruments(IEnumerable<string> names)
        {
            HashSet<string> initialized = new HashSet<string>(names ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (InstrumentConfigRow row in _configRows) row.ConnectionStatus = initialized.Contains(row.Name) ? "已初始化" : "未初始化";
        }

        /// <summary>
        /// DataGridCheckBoxColumn needs a row-select click first. Template CheckBox toggles on one click.
        /// Header hosts a master checkbox for select-all / clear-all.
        /// </summary>
        private DataGridTemplateColumn CreateInitializeCheckColumn()
        {
            _initializeSelectAllCheck = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "全选",
                Focusable = false,
                IsThreeState = true
            };
            _initializeSelectAllCheck.Click += InitializeSelectAll_Click;

            FrameworkElementFactory check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Initialize") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
            check.SetValue(CheckBox.FocusableProperty, false);
            return new DataGridTemplateColumn
            {
                Header = _initializeSelectAllCheck,
                Width = 56,
                CellTemplate = new DataTemplate { VisualTree = check }
            };
        }

        private void InitializeSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingInitializeSelectAll) return;
            bool allSelected = _configRows.Count > 0 && _configRows.All(row => row.Initialize);
            SetAllInitialize(!allSelected);
        }

        private void SetAllInitialize(bool value)
        {
            foreach (InstrumentConfigRow row in _configRows) row.Initialize = value;
            SyncInitializeSelectAllHeader();
        }

        private void ConfigRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Initialize") SyncInitializeSelectAllHeader();
        }

        private void SyncInitializeSelectAllHeader()
        {
            if (_initializeSelectAllCheck == null) return;
            int total = _configRows.Count;
            int selected = _configRows.Count(row => row.Initialize);
            bool? next = total == 0 || selected == 0 ? false : selected == total ? true : (bool?)null;
            if (_initializeSelectAllCheck.IsChecked == next) return;
            _syncingInitializeSelectAll = true;
            _initializeSelectAllCheck.IsChecked = next;
            _syncingInitializeSelectAll = false;
        }

        private void ActionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InstrumentActionRow row = _actionGrid.SelectedItem as InstrumentActionRow;
            _editingAction = row == null ? null : SequenceEditing.Clone(row.Definition);
            _parameters.Clear();
            if (_editingAction == null) return;
            foreach (KeyValuePair<string, object> parameter in _editingAction.Parameters)
                _parameters.Add(new LiveParameterRow(parameter.Key, parameter.Value));
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (_editingAction == null)
            {
                MessageBox.Show("请先选择一个仪器STEP。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                _parameterGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                _parameterGrid.CommitEdit(DataGridEditingUnit.Row, true);
                foreach (LiveParameterRow parameter in _parameters)
                    _editingAction.SetParameterFromText(parameter.Name, parameter.ValueText, parameter.OriginalType);
                string result = await _executeStep(SequenceEditing.Clone(_editingAction));
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + _editingAction.FunctionName + "  =>  " + (string.IsNullOrWhiteSpace(result) ? "完成" : result);
                _resultBox.AppendText(line + Environment.NewLine);
                _resultBox.ScrollToEnd();
                _log("仪器中心实时执行：" + line);
            }
            catch (Exception ex)
            {
                _resultBox.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  失败：" + ex.Message + Environment.NewLine);
                MessageBox.Show("实时执行失败：\n" + ex.Message, "仪器中心", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    internal sealed class InstrumentConfigRow : INotifyPropertyChanged
    {
        private string _resource;
        private string _parameter;
        private string _comment;
        private bool _initialize;
        private string _connectionStatus = "未初始化";
        public InstrumentConfigRow(int uniqueIndex, string name, string type, string mode, string resource, string parameter, string comment, bool persisted, bool initialize = false)
        {
            UniqueIndex = uniqueIndex; Name = name; Type = type; Mode = mode; _resource = resource; _parameter = parameter; _comment = comment ?? string.Empty; Persisted = persisted; _initialize = initialize;
            EffectiveScope = persisted ? "保存后重新初始化生效" : comment;
        }
        public int UniqueIndex { get; private set; }
        public string Name { get; private set; }
        public string Type { get; private set; }
        public string Mode { get; private set; }
        public string Resource { get { return _resource; } set { _resource = value ?? string.Empty; Raise("Resource"); } }
        public string Parameter { get { return _parameter; } set { _parameter = value ?? string.Empty; Raise("Parameter"); } }
        public bool Initialize { get { return _initialize; } set { _initialize = value; Raise("Initialize"); } }
        public string ConnectionStatus { get { return _connectionStatus; } set { _connectionStatus = value ?? "未初始化"; Raise("ConnectionStatus"); } }
        public string Comment { get { return _comment; } set { _comment = value ?? string.Empty; Raise("Comment"); } }
        public bool Persisted { get; private set; }
        public string EffectiveScope { get; set; }
        public static InstrumentConfigRow FromJson(JObject item)
        {
            return new InstrumentConfigRow((int?)item["UniqueIndex"] ?? 0, (string)item["Name"] ?? string.Empty, (string)item["Type"] ?? string.Empty, (string)item["Mode"] ?? string.Empty, (string)item["Resource"] ?? string.Empty, (string)item["Parameter"] ?? string.Empty, (string)item["Comment"] ?? string.Empty, true, (bool?)item["Initialize"] ?? false);
        }

        public static InstrumentConfigRow FromCatalog(int uniqueIndex, JObject catalogEntry, bool initialize)
        {
            return new InstrumentConfigRow(uniqueIndex, (string)catalogEntry["Name"] ?? string.Empty, (string)catalogEntry["Type"] ?? string.Empty, (string)catalogEntry["Mode"] ?? string.Empty, (string)catalogEntry["Resource"] ?? string.Empty, (string)catalogEntry["Parameter"] ?? string.Empty, (string)catalogEntry["Comment"] ?? string.Empty, true, initialize);
        }
        public JObject ToJson()
        {
            return new JObject { ["UniqueIndex"] = UniqueIndex, ["Name"] = Name, ["Type"] = Type, ["Mode"] = Mode, ["Resource"] = Resource, ["Parameter"] = Parameter, ["Comment"] = Comment, ["Initialize"] = Initialize };
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class InstrumentActionRow
    {
        public InstrumentActionRow(SequenceStepDefinition definition) { Definition = definition; }
        public SequenceStepDefinition Definition { get; private set; }
        public string Name { get { return Definition.StepName; } }
        public string FunctionName { get { return Definition.FunctionName; } }
        public string Category { get { return InstrumentStepCatalog.CategoryFor(Definition); } }
    }

    internal sealed class LiveParameterRow : INotifyPropertyChanged
    {
        private string _valueText;
        public LiveParameterRow(string name, object value)
        {
            Name = name; OriginalType = value == null ? typeof(string) : value.GetType(); TypeName = OriginalType.Name; _valueText = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        public string Name { get; private set; }
        public Type OriginalType { get; private set; }
        public string TypeName { get; private set; }
        public string ValueText { get { return _valueText; } set { _valueText = value ?? string.Empty; PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs("ValueText")); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
