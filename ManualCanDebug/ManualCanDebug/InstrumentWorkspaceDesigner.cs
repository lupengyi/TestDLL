using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    internal sealed class InstrumentWorkspaceDesigner
    {
        private readonly InstrumentWorkspaceService _service;
        private readonly Action<string> _log;
        private readonly Action _configurationChanged;
        private readonly Func<string, System.Threading.Tasks.Task> _testConnection;
        private InstrumentWorkspaceDocument _document;
        private List<DriverDiscoveryItem> _drivers;
        private ProjectInstrumentDefinition _selectedInstrument;
        private StationInstrumentDefinition _selectedStation;
        private Grid _projectEditor;
        private ContentControl _generatedMethodHost;
        private Grid _projectPageRoot;
        private ContentControl _resourcePaletteHost = null;
        private Grid _stationEditor = null;
        private Grid _unifiedStationRoot;
        private ContentControl _stationNavigationHost;
        private ContentControl _stationInspectorHost;
        private StationInstrumentInstance _selectedStationInstance;
        private ProjectInstrumentDefinition _selectedSharedInstrument;
        private Grid _stationCards = null;
        private Canvas _wiringCanvas = null;
        private TextBlock _driverCount;
        private TextBlock _methodCount;
        private readonly Dictionary<string, FrameworkElement> _resourceEndpoints = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _stationPowerEndpoints = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _stationPlcEndpoints = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _stationCardEndpoints = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ProjectInstrumentDefinition, Border> _instrumentRows = new Dictionary<ProjectInstrumentDefinition, Border>();
        private readonly HashSet<string> _initializedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public InstrumentWorkspaceDesigner(string baseDirectory, Action<string> log, Action configurationChanged, Func<string, System.Threading.Tasks.Task> testConnection)
        {
            _service = new InstrumentWorkspaceService(baseDirectory);
            _log = log ?? delegate { };
            _configurationChanged = configurationChanged ?? delegate { };
            _testConnection = testConnection;
            _document = _service.Load();
            _drivers = _service.ScanDrivers();
            _selectedInstrument = _document.Instruments.FirstOrDefault(i => i.Device == "DMM") ?? _document.Instruments.FirstOrDefault();
            _selectedStation = _document.Stations.FirstOrDefault(s => s.StationNumber == 3) ?? _document.Stations.FirstOrDefault();
        }

        public UIElement BuildProjectInstrumentPage()
        {
            _projectPageRoot = new Grid { Background = Bg(246, 248, 251), Margin = new Thickness(8) };
            PopulateProjectInstrumentPage();
            return _projectPageRoot;
        }

        internal void SetInitializedInstruments(IEnumerable<string> names) { _initializedDevices.Clear(); foreach (string name in names ?? Enumerable.Empty<string>()) if (!string.IsNullOrWhiteSpace(name)) _initializedDevices.Add(name); RefreshUnifiedStationPage(); }

        private void PopulateProjectInstrumentPage()
        {
            if (_projectPageRoot == null) return;
            _instrumentRows.Clear();
            Grid root = _projectPageRoot; root.Children.Clear(); root.RowDefinitions.Clear(); root.ColumnDefinitions.Clear();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star) });
            root.Children.Add(BuildDiscoveryBar());

            Grid lists = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            lists.ColumnDefinitions.Add(new ColumnDefinition());
            lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            lists.ColumnDefinitions.Add(new ColumnDefinition());
            Border shared = BuildInstrumentListPanel(true); Grid.SetColumn(shared, 0); lists.Children.Add(shared);
            Border independent = BuildInstrumentListPanel(false); Grid.SetColumn(independent, 2); lists.Children.Add(independent);
            Grid.SetRow(lists, 1); root.Children.Add(lists);

            Grid editors = new Grid();
            editors.ColumnDefinitions.Add(new ColumnDefinition());
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            _projectEditor = new Grid(); editors.Children.Add(_projectEditor);
            _generatedMethodHost = new ContentControl(); Grid.SetColumn(_generatedMethodHost, 2); editors.Children.Add(_generatedMethodHost);
            Grid.SetRow(editors, 3); root.Children.Add(editors);
            RefreshProjectEditor();
            RefreshGeneratedMethodPanel();
        }

        public UIElement BuildStationConfigurationPage()
        {
            _unifiedStationRoot = new Grid { Background = Brushes.White, Margin = new Thickness(8) };
            _unifiedStationRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            _unifiedStationRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            _unifiedStationRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _stationNavigationHost = new ContentControl(); _unifiedStationRoot.Children.Add(_stationNavigationHost);
            Border divider = new Border { Background = BorderBrush() }; Grid.SetColumn(divider, 1); _unifiedStationRoot.Children.Add(divider);
            _stationInspectorHost = new ContentControl(); Grid.SetColumn(_stationInspectorHost, 2); _unifiedStationRoot.Children.Add(_stationInspectorHost);
            if (_selectedStation == null) _selectedStation = _document.Stations.OrderBy(value => value.StationNumber).FirstOrDefault();
            _selectedStationInstance = PreferredStationInstance(_selectedStation);
            RefreshUnifiedStationPage();
            return _unifiedStationRoot;
        }

        private void RefreshUnifiedStationPage()
        {
            if (_stationNavigationHost != null) _stationNavigationHost.Content = BuildUnifiedStationNavigation();
            if (_stationInspectorHost != null) _stationInspectorHost.Content = BuildUnifiedInstrumentInspector();
        }

        private UIElement BuildUnifiedStationNavigation()
        {
            Grid root = new Grid { Background = Bg(249, 251, 254) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid commands = new Grid { Margin = new Thickness(14, 12, 14, 10) }; commands.ColumnDefinitions.Add(new ColumnDefinition()); commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            commands.Children.Add(new TextBlock { Text = "工位与仪器", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ink(), VerticalAlignment = VerticalAlignment.Center });
            StackPanel commandButtons = new StackPanel { Orientation = Orientation.Horizontal }; Button addStation = Secondary("＋ 新建工位"); addStation.MinWidth = 96; addStation.Click += delegate { ChangeStationCount(Math.Min(12, _document.StationCount + 1)); SaveWorkspace(false); RefreshUnifiedStationPage(); }; Button addInstrument = Primary("＋ 添加仪器"); addInstrument.MinWidth = 96; addInstrument.Click += AddStationInstrument_Click; commandButtons.Children.Add(addStation); commandButtons.Children.Add(addInstrument); Grid.SetColumn(commandButtons, 1); commands.Children.Add(commandButtons); root.Children.Add(commands);
            StackPanel stations = new StackPanel { Margin = new Thickness(10, 0, 10, 12) };
            foreach (StationInstrumentDefinition station in _document.Stations.OrderBy(value => value.StationNumber))
            {
                bool selectedStation = ReferenceEquals(station, _selectedStation); Border stationHeader = new Border { Background = selectedStation ? Bg(235, 244, 255) : Brushes.White, BorderBrush = selectedStation ? Accent() : BorderBrush(), BorderThickness = new Thickness(selectedStation ? 2 : 1), Padding = new Thickness(12, 8, 10, 8), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand };
                DockPanel header = new DockPanel(); header.Children.Add(new TextBlock { Text = station.StationName, FontWeight = FontWeights.SemiBold, Foreground = selectedStation ? Accent() : Ink() }); TextBlock count = new TextBlock { Text = station.IndependentDevices.Count + _document.Instruments.Count(value => value.IsShared) + " 台仪器 · " + _document.Instruments.Count(value => value.IsShared) + " 台共用", Foreground = new SolidColorBrush(Color.FromRgb(103, 116, 134)), HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(count, Dock.Right); header.Children.Add(count); stationHeader.Child = header; stationHeader.MouseLeftButtonDown += delegate { _selectedStation = station; _selectedStationInstance = PreferredStationInstance(station); _selectedSharedInstrument = null; RefreshUnifiedStationPage(); }; stations.Children.Add(stationHeader);
                if (!selectedStation) continue;
                foreach (StationInstrumentInstance instance in station.IndependentDevices.Where(value => value.Enabled)) stations.Children.Add(BuildUnifiedInstrumentRow(instance, null));
                if (_document.Instruments.Any(value => value.IsShared)) stations.Children.Add(new TextBlock { Text = "全部工位共用", Foreground = Accent(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 8, 0, 5) });
                foreach (ProjectInstrumentDefinition shared in _document.Instruments.Where(value => value.IsShared).OrderBy(value => value.DisplayName)) stations.Children.Add(BuildUnifiedInstrumentRow(null, shared));
            }
            ScrollViewer scroll = new ScrollViewer { Content = stations, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); return root;
        }

        private Border BuildUnifiedInstrumentRow(StationInstrumentInstance instance, ProjectInstrumentDefinition shared)
        {
            ProjectInstrumentDefinition template = shared ?? _document.Instruments.FirstOrDefault(value => string.Equals(value.Device, instance == null ? string.Empty : instance.TemplateDevice, StringComparison.OrdinalIgnoreCase)); bool selected = instance != null ? ReferenceEquals(instance, _selectedStationInstance) : ReferenceEquals(shared, _selectedSharedInstrument); string name = shared == null ? (string.IsNullOrWhiteSpace(instance.InstanceName) ? template == null ? instance.TemplateDevice : template.DisplayName : instance.InstanceName) : shared.DisplayName;
            Border row = new Border { Background = selected ? Bg(229, 241, 255) : Brushes.White, BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(159, 196, 241)) : BorderBrush(), BorderThickness = new Thickness(1), Padding = new Thickness(28, 7, 10, 7), Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand };
            bool initialized = template != null && _initializedDevices.Contains(template.Device); DockPanel content = new DockPanel(); TextBlock state = new TextBlock { Text = "●", Foreground = initialized ? new SolidColorBrush(Color.FromRgb(53, 175, 105)) : new SolidColorBrush(Color.FromRgb(170, 181, 195)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, ToolTip = initialized ? "已初始化" : "未初始化" }; DockPanel.SetDock(state, Dock.Right); content.Children.Add(state); content.Children.Add(new TextBlock { Text = name + (shared == null ? string.Empty : "  ·  全工位"), Foreground = selected ? Accent() : Ink(), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal }); row.Child = content;
            row.MouseLeftButtonDown += delegate { _selectedStationInstance = instance; _selectedSharedInstrument = shared; RefreshUnifiedStationPage(); }; return row;
        }

        private static StationInstrumentInstance PreferredStationInstance(StationInstrumentDefinition station) { return station == null ? null : (station.IndependentDevices.FirstOrDefault(value => string.Equals(value.TemplateDevice, "MAINCAN", StringComparison.OrdinalIgnoreCase)) ?? station.IndependentDevices.FirstOrDefault(value => string.Equals(value.TemplateDevice, "DUTCAN", StringComparison.OrdinalIgnoreCase)) ?? station.IndependentDevices.FirstOrDefault()); }

        private UIElement BuildUnifiedInstrumentInspector()
        {
            ProjectInstrumentDefinition template = _selectedSharedInstrument ?? (_selectedStationInstance == null ? null : _document.Instruments.FirstOrDefault(value => string.Equals(value.Device, _selectedStationInstance.TemplateDevice, StringComparison.OrdinalIgnoreCase)));
            if (template == null) return new TextBlock { Text = "请从左侧选择仪器，或点击“添加仪器”。", Foreground = new SolidColorBrush(Color.FromRgb(105, 118, 136)), Margin = new Thickness(28), FontSize = 15 };
            bool shared = _selectedSharedInstrument != null; string currentName = shared ? template.DisplayName : _selectedStationInstance.InstanceName; string currentResource = shared ? template.Resource : _selectedStationInstance.Resource; string currentParameter = shared ? template.Parameter : _selectedStationInstance.Parameter;
            Grid root = new Grid { Margin = new Thickness(20, 12, 18, 14) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DockPanel title = new DockPanel(); title.Children.Add(new TextBlock { Text = "配置仪器 · " + currentName, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ink() }); TextBlock summary = new TextBlock { Text = (_selectedStation == null ? string.Empty : _selectedStation.StationName + " · ") + (_selectedStation == null ? 0 : _selectedStation.IndependentDevices.Count) + " 台专用 · " + _document.Instruments.Count(value => value.IsShared) + " 台共用", Foreground = new SolidColorBrush(Color.FromRgb(103, 116, 134)), HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(summary, Dock.Right); title.Children.Add(summary); root.Children.Add(title);
            Grid form = new Grid { Margin = new Thickness(0, 18, 0, 0) }; form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); form.ColumnDefinitions.Add(new ColumnDefinition()); for (int i = 0; i < 9; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabel(form, "仪器名称", 0, 0); TextBox name = FlatText(currentName); name.TextChanged += delegate { if (shared) template.DisplayName = name.Text; else _selectedStationInstance.InstanceName = name.Text; }; AddControl(form, name, 0, 1);
            AddLabel(form, "仪器类型", 1, 0); TextBox type = FlatText(template.DisplayName + "  /  " + template.DriverName); type.IsReadOnly = true; type.Background = Bg(246, 248, 251); AddControl(form, type, 1, 1);
            AddLabel(form, "所属范围", 2, 0); StackPanel scope = new StackPanel { Orientation = Orientation.Horizontal }; ToggleButton local = SegmentButton("当前工位（" + (_selectedStation == null ? "" : _selectedStation.StationName) + "）", !shared); ToggleButton all = SegmentButton("全部工位共用", shared); local.Click += delegate { if (shared) ConvertSharedToStation(template); }; all.Click += delegate { if (!shared) ConvertStationToShared(template, _selectedStationInstance); }; scope.Children.Add(local); scope.Children.Add(all); AddControl(form, scope, 2, 1);
            bool can = string.Equals(template.DriverName, "Instruments.CAN.CANWrapper", StringComparison.OrdinalIgnoreCase) || template.Device.EndsWith("CAN", StringComparison.OrdinalIgnoreCase) || template.Device.Contains("CAN"); string[] canParts = (currentParameter ?? string.Empty).Split(',');
            AddLabel(form, can ? "IP地址" : "Resource", 3, 0); TextBox resource = FlatText(currentResource); resource.TextChanged += delegate { SetSelectedConnection(resource.Text, null); }; AddControl(form, resource, 3, 1);
            if (can)
            {
                AddLabel(form, "通道", 4, 0); ComboBox channel = FlatCombo(new[] { "CAN0", "CAN1" }); channel.SelectedItem = canParts.Length > 1 && canParts[1].Trim() == "1" ? "CAN1" : "CAN0"; channel.SelectionChanged += delegate { SetCanParameterPart(1, Convert.ToString(channel.SelectedItem) == "CAN1" ? "1" : "0"); }; AddControl(form, channel, 4, 1);
                AddLabel(form, "通信模式", 5, 0); ComboBox mode = FlatCombo(new[] { "经典CAN" }); mode.SelectedIndex = 0; mode.IsEnabled = false; mode.ToolTip = "当前200U工位配置使用经典CAN 500 kbit/s；CAN FD需单独配置数据波特率后开放。"; AddControl(form, mode, 5, 1);
                AddLabel(form, "仲裁波特率", 6, 0); TextBox baud = FlatText(canParts.Length > 2 ? canParts[2] : "500000"); baud.TextChanged += delegate { SetCanParameterPart(2, baud.Text); }; AddControl(form, baud, 6, 1);
                AddLabel(form, "端口", 7, 0); TextBox port = FlatText(canParts.Length > 3 ? canParts[3] : "8000"); port.TextChanged += delegate { SetCanParameterPart(3, port.Text); }; AddControl(form, port, 7, 1);
            }
            else { AddLabel(form, "连接参数", 4, 0); TextBox parameter = FlatText(currentParameter); parameter.TextChanged += delegate { SetSelectedConnection(null, parameter.Text); }; AddControl(form, parameter, 4, 1); }
            ScrollViewer formScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(formScroll, 1); root.Children.Add(formScroll);
            DockPanel footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; Button remove = Danger(shared ? "从全部工位删除" : "从当前工位删除"); remove.Click += RemoveSelectedStationInstrument_Click; footer.Children.Add(remove); StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button save = Primary("保存配置"); save.Click += delegate { SaveWorkspace(true); RefreshUnifiedStationPage(); }; Button test = Secondary("测试连接"); test.Click += async delegate { await TestSelectedConnectionAsync(template, test); }; Button initialize = Primary("初始化本工位"); initialize.Click += async delegate { await InitializeCurrentStationAsync(initialize); }; actions.Children.Add(save); actions.Children.Add(test); actions.Children.Add(initialize); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer); return root;
        }

        private UIElement BuildDiscoveryBar()
        {
            Border border = Box(); border.Padding = new Thickness(14, 8, 14, 8);
            DockPanel dock = new DockPanel();
            StackPanel facts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _driverCount = Fact("自动发现 " + _drivers.Count + " 个驱动", "\uE721");
            _methodCount = Fact(_drivers.Sum(d => d.Methods.Count) + " 个方法", "\uE943");
            facts.Children.Add(_driverCount); facts.Children.Add(Divider()); facts.Children.Add(_methodCount); facts.Children.Add(Divider()); facts.Children.Add(Fact("全部可用", "\uE73E"));
            Button scan = LinkButton("重新扫描"); scan.Click += delegate { _drivers = _service.ScanDrivers(); PopulateProjectInstrumentPage(); RefreshStationResources(); };
            facts.Children.Add(Divider()); facts.Children.Add(scan); dock.Children.Add(facts);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button allocate = Primary("＋ 分配仪器"); allocate.Click += AllocateInstrument_Click;
            Button delete = Danger("删除定义"); delete.Click += DeleteInstrument_Click;
            actions.Children.Add(allocate); actions.Children.Add(delete); DockPanel.SetDock(actions, Dock.Right); dock.Children.Add(actions);
            border.Child = dock; return border;
        }

        private Border BuildInstrumentListPanel(bool shared)
        {
            Border outer = Box(); Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            int count = _document.Instruments.Count(i => shared ? i.IsShared : !i.IsShared);
            grid.Children.Add(new TextBlock { Text = shared ? "共用仪器（" + count + " 台）" : "独立仪器模板（拖入工位分配）", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink(), Margin = new Thickness(16, 11, 12, 8) });
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            StackPanel rows = new StackPanel { Margin = new Thickness(10, 0, 10, 10) };
            Grid header = InstrumentColumns(); header.Margin = new Thickness(0, 0, 0, 4);
            BuildInstrumentHeader(header, shared); rows.Children.Add(header);
            foreach (ProjectInstrumentDefinition item in _document.Instruments.Where(i => shared ? i.IsShared : !i.IsShared).OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase))
                rows.Children.Add(BuildInstrumentRow(item, shared));
            scroll.Content = rows; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
            outer.Child = grid; return outer;
        }

        private void BuildInstrumentHeader(Grid grid, bool shared)
        {
            AddCell(grid, shared ? "项目名称" : "模板名称", 0, true);
            AddCell(grid, "自动匹配驱动", 1, true);
            if (shared) AddCell(grid, "连接地址", 2, true);
            AddCell(grid, "资源概况", shared ? 3 : 2, true);
        }

        private UIElement BuildInstrumentRow(ProjectInstrumentDefinition item, bool shared)
        {
            bool selected = ReferenceEquals(item, _selectedInstrument);
            Border rowBorder = new Border { Background = selected ? Bg(235, 245, 255) : Brushes.White, BorderBrush = selected ? Accent() : BorderBrush(), BorderThickness = selected ? new Thickness(3, 1, 1, 1) : new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Tag = item };
            _instrumentRows[item] = rowBorder;
            Grid row = InstrumentColumns();
            AddCell(row, item.DisplayName, 0, false);
            AddCell(row, item.DriverName, 1, false);
            if (shared) AddCell(row, item.Resource, 2, false);
            string summary = item.IsShared ? (Math.Max(1, item.ChannelCount) + "通道 · 多工位共享") : "独立仪器（每工位1台）";
            AddCell(row, summary, shared ? 3 : 2, false);
            rowBorder.Child = row;
            rowBorder.PreviewMouseLeftButtonDown += delegate { SelectInstrument(item); };
            return rowBorder;
        }

        private void SelectInstrument(ProjectInstrumentDefinition item)
        {
            _selectedInstrument = item;
            foreach (KeyValuePair<ProjectInstrumentDefinition, Border> pair in _instrumentRows)
            {
                bool selected = ReferenceEquals(pair.Key, item);
                pair.Value.Background = selected ? Bg(235, 245, 255) : Brushes.White;
                pair.Value.BorderBrush = selected ? Accent() : BorderBrush();
                pair.Value.BorderThickness = selected ? new Thickness(3, 1, 1, 1) : new Thickness(1);
            }
            RefreshProjectEditor();
            RefreshGeneratedMethodPanel();
        }

        internal void SelectInstrumentByDevice(string device)
        {
            ProjectInstrumentDefinition item = _document.Instruments.FirstOrDefault(v => string.Equals(v.Device, device, StringComparison.OrdinalIgnoreCase));
            if (item != null) SelectInstrument(item);
        }

        private void RefreshProjectEditor()
        {
            if (_projectEditor == null) return; _projectEditor.Children.Clear(); _projectEditor.ColumnDefinitions.Clear();
            Border box = Box();
            box.Child = _selectedInstrument == null
                ? (UIElement)new TextBlock { Text = "在上方列表中选择一台项目仪器后，在这里设置它的驱动、连接、独立/共用方式和要生成的方法。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), Foreground = new SolidColorBrush(Color.FromRgb(112, 124, 140)) }
                : BuildDefinitionEditor(_selectedInstrument);
            _projectEditor.Children.Add(box);
        }

        private void RefreshGeneratedMethodPanel()
        {
            if (_generatedMethodHost == null) return;
            Border box = Box();
            box.Child = _selectedInstrument == null
                ? (UIElement)new TextBlock { Text = "已生成的方法会显示在这里。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), Foreground = new SolidColorBrush(Color.FromRgb(112, 124, 140)) }
                : BuildGeneratedMethodPanel(_selectedInstrument);
            _generatedMethodHost.Content = box;
        }

        private UIElement BuildDefinitionEditor(ProjectInstrumentDefinition item)
        {
            Grid root = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "项目仪器定义", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink() });

            Grid fields = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) }); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 5; i++) fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabel(fields, "显示名称", 0, 0); TextBox name = FlatText(item.DisplayName); name.TextChanged += delegate { item.DisplayName = name.Text; }; AddControl(fields, name, 0, 1);
            AddLabel(fields, "自动匹配驱动", 1, 0); ComboBox driver = FlatCombo(_drivers.Select(d => d.AssemblyName).Concat(new[] { item.DriverName }).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v)); driver.SelectedItem = item.DriverName; driver.SelectionChanged += delegate { ApplyDriverSelection(item, Convert.ToString(driver.SelectedItem)); }; AddControl(fields, driver, 1, 1);
            AddLabel(fields, "连接资源", 2, 0); Grid connection = new Grid(); connection.ColumnDefinitions.Add(new ColumnDefinition()); connection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); TextBox resource = FlatText(item.Resource); resource.TextChanged += delegate { item.Resource = resource.Text; }; connection.Children.Add(resource); Button test = Secondary("测试连接"); test.MinWidth = 88; test.Margin = new Thickness(8, 0, 0, 0); test.Click += async delegate { await TestConnectionAsync(item, test); }; Grid.SetColumn(test, 1); connection.Children.Add(test); AddControl(fields, connection, 2, 1);
            AddLabel(fields, "使用方式", 3, 0); StackPanel usage = new StackPanel { Orientation = Orientation.Horizontal };
            ToggleButton sharedToggle = SegmentButton("共用仪器", item.IsShared); ToggleButton independentToggle = SegmentButton("独立仪器", !item.IsShared);
            sharedToggle.Click += delegate { item.Usage = "Shared"; PopulateProjectInstrumentPage(); RefreshStationResources(); };
            independentToggle.Click += delegate { item.Usage = "Independent"; PopulateProjectInstrumentPage(); RefreshStationResources(); };
            usage.Children.Add(sharedToggle); usage.Children.Add(independentToggle); AddControl(fields, usage, 3, 1);
            Grid.SetRow(fields, 1); root.Children.Add(fields);

            StackPanel methods = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            methods.Children.Add(new TextBlock { Text = "选择要生成的方法", FontWeight = FontWeights.SemiBold, Foreground = Ink(), Margin = new Thickness(0, 0, 0, 6) });
            foreach (GeneratedInstrumentMethod method in item.GeneratedMethods.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                GeneratedInstrumentMethod current = method;
                CheckBox check = new CheckBox { Content = current.DisplayName, IsChecked = current.Selected, Margin = new Thickness(2, 3, 0, 3), Foreground = Ink() };
                check.Checked += delegate { current.Selected = true; RefreshGeneratedMethodPanel(); };
                check.Unchecked += delegate { current.Selected = false; RefreshGeneratedMethodPanel(); };
                methods.Children.Add(check);
            }
            ScrollViewer methodScroll = new ScrollViewer { Content = methods, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(methodScroll, 2); root.Children.Add(methodScroll);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Button save = Secondary("保存仪器定义"); save.Click += delegate { SaveWorkspace(false); PopulateProjectInstrumentPage(); };
            buttons.Children.Add(save); Grid.SetRow(buttons, 3); root.Children.Add(buttons);
            return root;
        }

        private UIElement BuildGeneratedMethodPanel(ProjectInstrumentDefinition item)
        {
            Grid root = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "已生成方法", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink() });
            heading.Children.Add(new TextBlock { Text = "保存后这些方法会进入 SEQ 目录，并在软件关闭后保留。", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(112, 124, 140)), Margin = new Thickness(0, 4, 0, 0) });
            root.Children.Add(heading);

            Grid table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            AddCell(table, "方法名称", 0, true); AddCell(table, "对应驱动方法", 1, true); AddCell(table, "生成结果", 2, true);
            int rowIndex = 1;
            foreach (GeneratedInstrumentMethod method in item.GeneratedMethods.Where(m => m.Selected).OrderBy(m => m.FunctionName, StringComparer.OrdinalIgnoreCase))
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock name = new TextBlock { Text = method.FunctionName, Padding = new Thickness(8, 5, 8, 5), TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetRow(name, rowIndex); Grid.SetColumn(name, 0); table.Children.Add(name);
                TextBlock driver = new TextBlock { Text = method.DriverMethod, Padding = new Thickness(8, 5, 8, 5), TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetRow(driver, rowIndex); Grid.SetColumn(driver, 1); table.Children.Add(driver);
                TextBlock result = new TextBlock { Text = string.IsNullOrWhiteSpace(method.ResultText) ? "生成成功" : method.ResultText, Padding = new Thickness(8, 5, 8, 5), Foreground = new SolidColorBrush(Color.FromRgb(39, 111, 68)) }; Grid.SetRow(result, rowIndex); Grid.SetColumn(result, 2); table.Children.Add(result);
                rowIndex++;
            }
            ScrollViewer scroll = new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 2); root.Children.Add(scroll);

            StackPanel methodActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; Button editActions = Secondary("编辑SEQ名称与参数"); editActions.Click += delegate { InstrumentActionManagerWindow dialog = new InstrumentActionManagerWindow { Owner = Application.Current == null ? null : Application.Current.MainWindow }; if (dialog.ShowDialog() == true) { ActionCatalog.Reload(); _configurationChanged(); RefreshProjectEditor(); RefreshGeneratedMethodPanel(); } }; Button generate = Primary("生成方法"); generate.Click += GenerateMethods_Click; methodActions.Children.Add(editActions); methodActions.Children.Add(generate);
            Grid.SetRow(methodActions, 3); root.Children.Add(methodActions);
            return root;
        }

        private UIElement BuildStationCommandBar()
        {
            DockPanel dock = new DockPanel { LastChildFill = true };
            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal }; left.Children.Add(new TextBlock { Text = "工位数量：", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }); ComboBox count = FlatCombo(Enumerable.Range(1, 12).Select(v => v.ToString())); count.Width = 75; count.SelectedItem = _document.StationCount.ToString(); count.SelectionChanged += delegate { int value; if (int.TryParse(Convert.ToString(count.SelectedItem), out value)) ChangeStationCount(value); }; left.Children.Add(count); dock.Children.Add(left);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button copy = Secondary("从工位复制配置"); copy.Click += CopyStation_Click; Button conflict = Secondary("检查资源冲突"); conflict.Click += CheckConflicts_Click; Button save = Primary("保存全部工位配置"); save.Click += delegate { SaveWorkspace(true); }; buttons.Children.Add(copy); buttons.Children.Add(conflict); buttons.Children.Add(save); DockPanel.SetDock(buttons, Dock.Right); dock.Children.Add(buttons); return dock;
        }

        private UIElement BuildResourcePalette()
        {
            Border outer = Box(); ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; StackPanel panel = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            panel.Children.Add(new TextBlock { Text = "可分配资源", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink(), Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(PaletteHeading("共用资源", "拖到工位后建立连线"));
            ProjectInstrumentDefinition plc = _document.Instruments.FirstOrDefault(i => i.Usage == "Shared" && i.Device == "PLC"); if (plc != null) panel.Children.Add(DraggableResource("PLC-01", "PLC:" + plc.Id, true));
            foreach (ProjectInstrumentDefinition power in _document.Instruments.Where(i => i.Usage == "Shared" && (i.Device == "LVDC" || i.Device == "LVDC_KL15"))) foreach (string group in ChannelGroups(power)) panel.Children.Add(DraggableResource(PowerShortName(power) + " / " + group, "POWER:" + power.Id + ":" + group, false));
            panel.Children.Add(PaletteHeading("独立仪器模板", "拖到工位内部"));
            foreach (ProjectInstrumentDefinition template in _document.Instruments.Where(i => i.Usage != "Shared")) panel.Children.Add(DraggableResource(template.DisplayName, "INDEPENDENT:" + template.Device, false));
            scroll.Content = panel; outer.Child = scroll; return outer;
        }

        private UIElement DraggableResource(string text, string payload, bool plc)
        {
            Border row = new Border { Background = plc ? Bg(239, 249, 242) : Brushes.White, BorderBrush = BorderBrush(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 0, 0, 6), Tag = payload };
            row.Child = new TextBlock { Text = text, Foreground = plc ? new SolidColorBrush(Color.FromRgb(39, 111, 68)) : Ink(), FontWeight = FontWeights.Medium };
            row.PreviewMouseMove += Resource_PreviewMouseMove;
            if (payload.StartsWith("PLC:") || payload.StartsWith("POWER:")) _resourceEndpoints[payload] = row;
            return row;
        }

        private void RefreshStationCards()
        {
            if (_stationCards == null) return; _stationCards.Children.Clear(); _stationCards.RowDefinitions.Clear(); for (int i = 0; i < Math.Max(1, (int)Math.Ceiling(_document.Stations.Count / 2.0)); i++) _stationCards.RowDefinitions.Add(new RowDefinition { MinHeight = 150 }); _stationPowerEndpoints.Clear(); _stationPlcEndpoints.Clear(); _stationCardEndpoints.Clear();
            foreach (StationInstrumentDefinition station in _document.Stations.OrderBy(s => s.StationNumber))
            {
                Border card = BuildStationCard(station); int index = station.StationNumber - 1; Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); if (_document.Stations.Count == 1) Grid.SetColumnSpan(card, 2); _stationCards.Children.Add(card);
            }
            ScheduleWiring();
        }

        private void RefreshStationResources()
        {
            _resourceEndpoints.Clear();
            if (_resourcePaletteHost != null) _resourcePaletteHost.Content = BuildResourcePalette();
            RefreshStationCards();
            RefreshStationEditor();
        }

        private Border BuildStationCard(StationInstrumentDefinition station)
        {
            bool selected = _selectedStation == station;
            Border card = new Border { Background = Brushes.White, BorderBrush = selected ? Accent() : BorderBrush(), BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(5), Margin = new Thickness(5), Padding = new Thickness(12), Tag = station, AllowDrop = true };
            _stationCardEndpoints[station.StationName] = card;
            card.Drop += Station_Drop; card.DragEnter += delegate { card.Background = Bg(242, 248, 255); }; card.DragLeave += delegate { card.Background = Brushes.White; }; card.MouseLeftButtonDown += delegate { _selectedStation = station; RefreshStationCards(); RefreshStationEditor(); };
            Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = station.StationName, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink() });
            StackPanel shared = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 8) };
            ProjectInstrumentDefinition power = FindInstrument(station.PowerInstrumentId); if (power != null) { Border powerChip = Chip(PowerShortName(power) + " / " + station.PowerChannelGroup, false); powerChip.Cursor = Cursors.Hand; powerChip.ToolTip = "按住拖到其他工位可移动电源连线；右键可取消连接"; powerChip.Tag = "MOVEPOWER:" + station.StationNumber + ":" + station.PowerInstrumentId + ":" + station.PowerChannelGroup; powerChip.PreviewMouseMove += Resource_PreviewMouseMove; powerChip.ContextMenu = DisconnectMenu(delegate { station.PowerInstrumentId = string.Empty; station.PowerChannelGroup = string.Empty; RefreshStationCards(); RefreshStationEditor(); }); shared.Children.Add(powerChip); _stationPowerEndpoints[station.StationName] = powerChip; }
            ProjectInstrumentDefinition plc = FindInstrument(station.PlcInstrumentId); Border plcChip = Chip((plc == null ? "未分配PLC" : plc.DisplayName + " / DB+" + station.PlcDbOffset), true); plcChip.Margin = new Thickness(12, 0, 0, 0); plcChip.Cursor = Cursors.Hand; plcChip.ToolTip = "按住拖到其他工位可复制PLC连接；右键可取消连接"; plcChip.Tag = "MOVEPLC:" + station.PlcInstrumentId; plcChip.PreviewMouseMove += Resource_PreviewMouseMove; plcChip.ContextMenu = DisconnectMenu(delegate { station.PlcInstrumentId = string.Empty; RefreshStationCards(); RefreshStationEditor(); }); shared.Children.Add(plcChip); _stationPlcEndpoints[station.StationName] = plcChip;
            Grid.SetRow(shared, 1); grid.Children.Add(shared);
            Border independentBox = new Border { Background = Bg(250, 251, 253), BorderBrush = BorderBrush(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8) };
            StackPanel independent = new StackPanel(); independent.Children.Add(new TextBlock { Text = "独立仪器", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) }); WrapPanel tags = new WrapPanel(); foreach (StationInstrumentInstance instance in station.IndependentDevices) tags.Children.Add(SmallTag(instance.TemplateDevice)); independent.Children.Add(tags); independentBox.Child = independent; Grid.SetRow(independentBox, 2); grid.Children.Add(independentBox);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) }; Button assign = LinkButton("分配独立仪器"); assign.Click += delegate { _selectedStation = station; RefreshStationEditor(); }; Button settings = LinkButton("设置"); settings.Click += delegate { _selectedStation = station; RefreshStationEditor(); }; actions.Children.Add(assign); actions.Children.Add(settings); Grid.SetRow(actions, 3); grid.Children.Add(actions);
            card.Child = grid; return card;
        }

        private void RefreshStationEditor()
        {
            if (_stationEditor == null) return; _stationEditor.Children.Clear(); if (_selectedStation == null) return;
            Border outer = Box(); Grid root = new Grid { Margin = new Thickness(14, 8, 14, 8) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            StackPanel independent = new StackPanel(); independent.Children.Add(new TextBlock { Text = _selectedStation.StationName + "设置", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }); WrapPanel tags = new WrapPanel(); foreach (StationInstrumentInstance instance in _selectedStation.IndependentDevices.ToArray()) tags.Children.Add(RemovableTag(instance)); independent.Children.Add(tags);
            Grid allocator = new Grid { Margin = new Thickness(0, 10, 0, 0) }; allocator.ColumnDefinitions.Add(new ColumnDefinition()); allocator.ColumnDefinitions.Add(new ColumnDefinition()); allocator.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) }); allocator.RowDefinitions.Add(new RowDefinition()); allocator.RowDefinitions.Add(new RowDefinition());
            ComboBox template = FlatCombo(_document.Instruments.Where(i => i.Usage != "Shared").Select(i => i.Device)); template.SelectedItem = _document.Instruments.FirstOrDefault(i => i.Usage != "Shared" && !_selectedStation.IndependentDevices.Any(v => v.TemplateDevice == i.Device))?.Device ?? "DMM"; AddControl(allocator, template, 0, 0);
            TextBox instanceName = FlatText(Convert.ToString(template.SelectedItem) + "-" + _selectedStation.StationNumber.ToString("00")); AddControl(allocator, instanceName, 0, 1); Button assign = Primary("分配到" + _selectedStation.StationName); assign.Click += delegate { AssignIndependent(Convert.ToString(template.SelectedItem), instanceName.Text, string.Empty); }; AddControl(allocator, assign, 0, 2);
            template.SelectionChanged += delegate { instanceName.Text = Convert.ToString(template.SelectedItem) + "-" + _selectedStation.StationNumber.ToString("00"); };
            StationInstrumentInstance selectedInstance = _selectedStation.IndependentDevices.FirstOrDefault(v => v.TemplateDevice == "DMM") ?? _selectedStation.IndependentDevices.FirstOrDefault();
            if (selectedInstance != null) { TextBox editName = FlatText(selectedInstance.InstanceName); editName.TextChanged += delegate { selectedInstance.InstanceName = editName.Text; }; TextBox address = FlatText(selectedInstance.Resource); address.TextChanged += delegate { selectedInstance.Resource = address.Text; }; AddControl(allocator, editName, 1, 0); AddControl(allocator, address, 1, 1); }
            independent.Children.Add(allocator); root.Children.Add(independent);

            StackPanel shared = new StackPanel(); shared.Children.Add(new TextBlock { Text = "共用资源", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 28, 0, 10) });
            List<ProjectInstrumentDefinition> powers = _document.Instruments.Where(i => i.Usage == "Shared" && (i.Device == "LVDC" || i.Device == "LVDC_KL15")).ToList(); List<ResourceOption> powerOptions = powers.SelectMany(p => ChannelGroups(p).Select(group => new ResourceOption(p.Id + "|" + group, PowerShortName(p) + " / " + group))).ToList(); ComboBox powerCombo = new ComboBox { ItemsSource = powerOptions, SelectedItem = powerOptions.FirstOrDefault(v => v.Key == _selectedStation.PowerInstrumentId + "|" + _selectedStation.PowerChannelGroup), Padding = new Thickness(6, 4, 6, 4), BorderBrush = BorderBrush() }; powerCombo.SelectionChanged += delegate { ResourceOption option = powerCombo.SelectedItem as ResourceOption; string[] parts = option == null ? new string[0] : option.Key.Split('|'); if (parts.Length == 2) { _selectedStation.PowerInstrumentId = parts[0]; _selectedStation.PowerChannelGroup = parts[1]; RefreshStationCards(); } };
            if (powerOptions.Count > 0) shared.Children.Add(Labeled("低压电源", powerCombo)); List<ProjectInstrumentDefinition> plcs = _document.Instruments.Where(i => i.Usage == "Shared" && i.Device == "PLC").ToList(); List<ResourceOption> plcOptions = plcs.Select(p => new ResourceOption(p.Id, p.DisplayName)).ToList(); ComboBox plcCombo = new ComboBox { ItemsSource = plcOptions, SelectedItem = plcOptions.FirstOrDefault(v => v.Key == _selectedStation.PlcInstrumentId), Padding = new Thickness(6, 4, 6, 4), BorderBrush = BorderBrush() }; plcCombo.SelectionChanged += delegate { ResourceOption option = plcCombo.SelectedItem as ResourceOption; _selectedStation.PlcInstrumentId = option == null ? string.Empty : option.Key; RefreshStationCards(); }; shared.Children.Add(Labeled("PLC", plcCombo)); TextBox offset = FlatText(_selectedStation.PlcDbOffset.ToString()); offset.TextChanged += delegate { int value; if (int.TryParse(offset.Text, out value)) { _selectedStation.PlcDbOffset = value; RefreshStationCards(); } }; shared.Children.Add(Labeled("DB偏移", offset)); Grid.SetColumn(shared, 2); root.Children.Add(shared);
            StackPanel saveArea = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) }; Button save = Primary("保存" + _selectedStation.StationName + "设置"); save.Click += delegate { SaveWorkspace(true); }; saveArea.Children.Add(save); saveArea.Children.Add(new TextBlock { Text = "驱动和方法已在项目仪器页自动发现并生成；这里仅分配实例和共享资源。", Foreground = Accent(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) }); Grid.SetColumn(saveArea, 3); root.Children.Add(saveArea);
            outer.Child = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly }; _stationEditor.Children.Add(outer);
        }

        private void AddStationInstrument_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStation == null) return; List<ProjectInstrumentDefinition> available = _document.Instruments.Where(value => !value.IsShared && !_selectedStation.IndependentDevices.Any(item => string.Equals(item.TemplateDevice, value.Device, StringComparison.OrdinalIgnoreCase))).OrderBy(value => value.DisplayName).ToList();
            if (available.Count == 0) { MessageBox.Show("当前仪器定义都已添加到本工位。请先在“仪器定义”中增加新的仪器类型。", "添加仪器", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            Window dialog = new Window { Title = "添加仪器到" + _selectedStation.StationName, Width = 470, Height = 250, Owner = Window.GetWindow((DependencyObject)sender), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            Grid form = new Grid { Margin = new Thickness(20) }; form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) }); form.ColumnDefinitions.Add(new ColumnDefinition()); form.RowDefinitions.Add(new RowDefinition()); form.RowDefinitions.Add(new RowDefinition()); form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); ComboBox type = new ComboBox { ItemsSource = available, DisplayMemberPath = "DisplayName", SelectedIndex = 0, Height = 32 }; TextBox name = FlatText(available[0].DisplayName); AddLabel(form, "仪器类型", 0, 0); AddControl(form, type, 0, 1); AddLabel(form, "仪器名称", 1, 0); AddControl(form, name, 1, 1); type.SelectionChanged += delegate { ProjectInstrumentDefinition selected = type.SelectedItem as ProjectInstrumentDefinition; if (selected != null) name.Text = selected.DisplayName; }; StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button cancel = Secondary("取消"); cancel.Click += delegate { dialog.Close(); }; Button add = Primary("添加并配置"); add.Click += delegate { ProjectInstrumentDefinition selected = type.SelectedItem as ProjectInstrumentDefinition; if (selected == null) return; StationInstrumentInstance instance = new StationInstrumentInstance { TemplateDevice = selected.Device, InstanceName = string.IsNullOrWhiteSpace(name.Text) ? selected.DisplayName : name.Text.Trim(), Resource = selected.Resource, Parameter = selected.Parameter }; _selectedStation.IndependentDevices.Add(instance); _selectedStationInstance = instance; _selectedSharedInstrument = null; dialog.DialogResult = true; }; buttons.Children.Add(cancel); buttons.Children.Add(add); Grid.SetRow(buttons, 2); Grid.SetColumnSpan(buttons, 2); form.Children.Add(buttons); dialog.Content = form; if (dialog.ShowDialog() == true) { SaveWorkspace(false); RefreshUnifiedStationPage(); }
        }

        private void SetSelectedConnection(string resource, string parameter)
        {
            if (_selectedSharedInstrument != null) { if (resource != null) _selectedSharedInstrument.Resource = resource; if (parameter != null) _selectedSharedInstrument.Parameter = parameter; }
            else if (_selectedStationInstance != null) { if (resource != null) _selectedStationInstance.Resource = resource; if (parameter != null) _selectedStationInstance.Parameter = parameter; }
        }

        private void SetCanParameterPart(int index, string value)
        {
            string current = _selectedSharedInstrument != null ? _selectedSharedInstrument.Parameter : _selectedStationInstance == null ? string.Empty : _selectedStationInstance.Parameter; List<string> parts = (current ?? string.Empty).Split(',').ToList(); while (parts.Count < 5) parts.Add(parts.Count == 0 ? "48" : parts.Count == 1 ? "0" : parts.Count == 2 ? "500000" : parts.Count == 3 ? "8000" : "0"); parts[index] = value ?? string.Empty; SetSelectedConnection(null, string.Join(",", parts));
        }

        private void ConvertStationToShared(ProjectInstrumentDefinition template, StationInstrumentInstance instance)
        {
            if (template == null || instance == null) return; if (MessageBox.Show("转换为共用仪器后，所有工位都会使用同一台真实仪器和同一份连接配置。是否继续？", "全部工位共用", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; template.Usage = "Shared"; template.DisplayName = instance.InstanceName; template.Resource = instance.Resource; template.Parameter = instance.Parameter; foreach (StationInstrumentDefinition station in _document.Stations) station.IndependentDevices.RemoveAll(value => string.Equals(value.TemplateDevice, template.Device, StringComparison.OrdinalIgnoreCase)); _selectedStationInstance = null; _selectedSharedInstrument = template; SaveWorkspace(false); RefreshUnifiedStationPage();
        }

        private void ConvertSharedToStation(ProjectInstrumentDefinition template)
        {
            if (template == null || _selectedStation == null) return; if (MessageBox.Show("转换后该仪器只保留在" + _selectedStation.StationName + "，其他工位将不再拥有它。是否继续？", "转为当前工位专用", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; template.Usage = "Independent"; StationInstrumentInstance instance = new StationInstrumentInstance { TemplateDevice = template.Device, InstanceName = template.DisplayName, Resource = template.Resource, Parameter = template.Parameter }; _selectedStation.IndependentDevices.Add(instance); _selectedSharedInstrument = null; _selectedStationInstance = instance; SaveWorkspace(false); RefreshUnifiedStationPage();
        }

        private void RemoveSelectedStationInstrument_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSharedInstrument != null) { if (MessageBox.Show("删除后该共用仪器将从全部工位移除。是否继续？", "删除共用仪器", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; _document.Instruments.Remove(_selectedSharedInstrument); _selectedSharedInstrument = null; }
            else if (_selectedStation != null && _selectedStationInstance != null) { if (MessageBox.Show("确定从" + _selectedStation.StationName + "移除“" + _selectedStationInstance.InstanceName + "”？", "移除仪器", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; _selectedStation.IndependentDevices.Remove(_selectedStationInstance); _selectedStationInstance = null; }
            SaveWorkspace(false); RefreshUnifiedStationPage();
        }

        private async System.Threading.Tasks.Task InitializeCurrentStationAsync(Button button)
        {
            if (_selectedStation == null || _testConnection == null) return; List<WorkspaceConflict> conflicts = _service.Validate(_document); if (conflicts.Count > 0) { MessageBox.Show(string.Join(Environment.NewLine, conflicts), "工位资源冲突", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            JArray payload = new JArray(); foreach (StationInstrumentInstance instance in _selectedStation.IndependentDevices.Where(value => value.Enabled)) { ProjectInstrumentDefinition template = _document.Instruments.FirstOrDefault(value => string.Equals(value.Device, instance.TemplateDevice, StringComparison.OrdinalIgnoreCase)); if (template != null) payload.Add(InitializationRow(template, instance.Resource, instance.Parameter)); } foreach (ProjectInstrumentDefinition shared in _document.Instruments.Where(value => value.IsShared)) payload.Add(InitializationRow(shared, shared.Resource, shared.Parameter));
            try { button.IsEnabled = false; button.Content = "正在初始化..."; await _testConnection(payload.ToString(Newtonsoft.Json.Formatting.None)); MessageBox.Show(_selectedStation.StationName + "已初始化。旧产品调试页、实时单步和SEQ调试将复用同一个MainTest实例。", "初始化本工位", MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { MessageBox.Show("初始化失败：\n" + ex.Message, "初始化本工位", MessageBoxButton.OK, MessageBoxImage.Error); } finally { button.IsEnabled = true; button.Content = "初始化本工位"; }
        }

        private async System.Threading.Tasks.Task TestSelectedConnectionAsync(ProjectInstrumentDefinition template, Button button)
        {
            if (_testConnection == null || template == null) return; string resource = _selectedSharedInstrument != null ? _selectedSharedInstrument.Resource : _selectedStationInstance == null ? template.Resource : _selectedStationInstance.Resource; string parameter = _selectedSharedInstrument != null ? _selectedSharedInstrument.Parameter : _selectedStationInstance == null ? template.Parameter : _selectedStationInstance.Parameter; try { button.IsEnabled = false; button.Content = "连接中..."; await _testConnection(new JArray(InitializationRow(template, resource, parameter)).ToString(Newtonsoft.Json.Formatting.None)); MessageBox.Show("测试连接成功。该连接与正式初始化使用同一套MainTest驱动。", "测试连接", MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { MessageBox.Show("测试连接失败：\n" + ex.Message, "测试连接", MessageBoxButton.OK, MessageBoxImage.Error); } finally { button.IsEnabled = true; button.Content = "测试连接"; }
        }

        private static JObject InitializationRow(ProjectInstrumentDefinition template, string resource, string parameter) { return new JObject { ["Name"] = template.Device, ["Type"] = template.Device, ["Mode"] = template.DriverName, ["Resource"] = string.IsNullOrWhiteSpace(resource) ? template.Resource : resource, ["Parameter"] = string.IsNullOrWhiteSpace(parameter) ? template.Parameter : parameter }; }

        private void Resource_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Border row = sender as Border; if (row != null && e.LeftButton == MouseButtonState.Pressed) DragDrop.DoDragDrop(row, Convert.ToString(row.Tag), DragDropEffects.Copy);
        }
        private void Station_Drop(object sender, DragEventArgs e)
        {
            string payload = e.Data.GetData(typeof(string)) as string; StationInstrumentDefinition station = ((Border)sender).Tag as StationInstrumentDefinition; if (station == null || string.IsNullOrWhiteSpace(payload)) return;
            if (payload.StartsWith("INDEPENDENT:", StringComparison.OrdinalIgnoreCase)) { string device = payload.Substring("INDEPENDENT:".Length); AssignIndependent(station, device, device + "-" + station.StationNumber.ToString("00"), string.Empty); }
            else if (payload.StartsWith("POWER:", StringComparison.OrdinalIgnoreCase)) { string[] parts = payload.Split(':'); if (parts.Length >= 3) { station.PowerInstrumentId = parts[1]; station.PowerChannelGroup = parts[2]; } }
            else if (payload.StartsWith("PLC:", StringComparison.OrdinalIgnoreCase)) station.PlcInstrumentId = payload.Substring("PLC:".Length);
            else if (payload.StartsWith("MOVEPOWER:", StringComparison.OrdinalIgnoreCase)) { string[] parts = payload.Split(':'); int from; if (parts.Length >= 4 && int.TryParse(parts[1], out from)) { StationInstrumentDefinition sourceStation = _document.Stations.FirstOrDefault(v => v.StationNumber == from); if (sourceStation != null && sourceStation != station) { sourceStation.PowerInstrumentId = string.Empty; sourceStation.PowerChannelGroup = string.Empty; } station.PowerInstrumentId = parts[2]; station.PowerChannelGroup = parts[3]; } }
            else if (payload.StartsWith("MOVEPLC:", StringComparison.OrdinalIgnoreCase)) station.PlcInstrumentId = payload.Substring("MOVEPLC:".Length);
            _selectedStation = station; RefreshStationCards(); RefreshStationEditor();
        }

        private void ScheduleWiring() { if (_wiringCanvas == null) return; _wiringCanvas.Dispatcher.BeginInvoke(new Action(DrawWiring), DispatcherPriority.Render); }
        private void DrawWiring()
        {
            if (_wiringCanvas == null || !_wiringCanvas.IsLoaded) return; _wiringCanvas.Children.Clear();
            foreach (StationInstrumentDefinition station in _document.Stations)
            {
                FrameworkElement source, target; string powerKey = "POWER:" + station.PowerInstrumentId + ":" + station.PowerChannelGroup;
                if (_resourceEndpoints.TryGetValue(powerKey, out source) && _stationPowerEndpoints.TryGetValue(station.StationName, out target)) AddWire(source, target, Color.FromRgb(44, 125, 230), -5);
                string plcKey = "PLC:" + station.PlcInstrumentId; if (_resourceEndpoints.TryGetValue(plcKey, out source) && _stationPlcEndpoints.TryGetValue(station.StationName, out target)) AddWire(source, target, Color.FromRgb(43, 151, 79), 5);
            }
        }
        private void AddWireToCard(FrameworkElement source, FrameworkElement card, Color color, double targetOffset)
        {
            try
            {
                Point start = source.TranslatePoint(new Point(source.ActualWidth, source.ActualHeight / 2), _wiringCanvas); Point cardTopLeft = card.TranslatePoint(new Point(0, 0), _wiringCanvas); Point end = new Point(cardTopLeft.X + 5, cardTopLeft.Y + targetOffset); double gutter = start.X + 14; double approach = cardTopLeft.X - 8;
                Polyline line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 1.5, Points = new PointCollection { start, new Point(gutter, start.Y), new Point(gutter, end.Y), new Point(approach, end.Y), end } }; _wiringCanvas.Children.Add(line);
            }
            catch { }
        }
        private void AddWire(FrameworkElement source, FrameworkElement target, Color color, double targetYOffset = 0)
        {
            try
            {
                Point start = source.TranslatePoint(new Point(source.ActualWidth, source.ActualHeight / 2), _wiringCanvas); Point end = target.TranslatePoint(new Point(0, target.ActualHeight / 2 + targetYOffset), _wiringCanvas); double gutter = start.X + 14;
                Polyline line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 1.5, Points = new PointCollection { start, new Point(gutter, start.Y), new Point(gutter, end.Y), end } }; _wiringCanvas.Children.Add(line);
            }
            catch { }
        }

        private void AllocateInstrument_Click(object sender, RoutedEventArgs e)
        {
            Window dialog = new Window { Title = "分配仪器", Width = 470, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow((DependencyObject)sender), ResizeMode = ResizeMode.NoResize };
            Grid root = new Grid { Margin = new Thickness(20) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            ComboBox driver = FlatCombo(_drivers.Select(d => d.AssemblyName)); driver.SelectedIndex = 0; TextBox name = FlatText("新仪器"); ComboBox usage = FlatCombo(new[] { "共用仪器", "独立仪器模板" }); usage.SelectedIndex = 1; TextBox address = FlatText(string.Empty);
            AddLabel(root, "自动发现驱动", 0, 0); AddControl(root, driver, 0, 1); AddLabel(root, "显示名称", 1, 0); AddControl(root, name, 1, 1); AddLabel(root, "使用方式", 2, 0); AddControl(root, usage, 2, 1); AddLabel(root, "连接参数", 3, 0); AddControl(root, address, 3, 1);
            StackPanel footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button cancel = Secondary("取消"); cancel.Click += delegate { dialog.Close(); }; Button ok = Primary("分配并设置"); ok.Click += delegate { string driverName = Convert.ToString(driver.SelectedItem); string device = GuessDevice(driverName, name.Text); ProjectInstrumentDefinition item = new ProjectInstrumentDefinition { DisplayName = name.Text, Device = device, DriverName = driverName, Resource = address.Text, Usage = usage.SelectedIndex == 0 ? "Shared" : "Independent", ChannelCount = 1, GeneratedMethods = new ObservableCollection<GeneratedInstrumentMethod>() }; item.GeneratedMethods = CreateMethodSelection(device, driverName); _document.Instruments.Add(item); _selectedInstrument = item; dialog.DialogResult = true; dialog.Close(); }; footer.Children.Add(cancel); footer.Children.Add(ok); Grid.SetRow(footer, 4); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer); dialog.Content = root;
            if (dialog.ShowDialog() == true) { SaveWorkspace(false); PopulateProjectInstrumentPage(); RefreshStationResources(); MessageBox.Show("仪器定义已保存并立即加入当前页面。", "分配仪器", MessageBoxButton.OK, MessageBoxImage.Information); }
        }

        private ObservableCollection<GeneratedInstrumentMethod> CreateMethodSelection(string device, string driverName)
        {
            ObservableCollection<GeneratedInstrumentMethod> result = new ObservableCollection<GeneratedInstrumentMethod>();
            foreach (ActionDescriptor d in ActionCatalog.AllDescriptors.Where(v => string.Equals(v.Device, device, StringComparison.OrdinalIgnoreCase) && !(string.Equals(v.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase) && (v.FunctionName ?? string.Empty).StartsWith("UI_", StringComparison.OrdinalIgnoreCase)))) result.Add(new GeneratedInstrumentMethod { Device = device, Operation = d.Operation, DisplayName = d.DisplayName, DriverMethod = d.Operation, FunctionName = "UI_" + InstrumentWorkspaceService.SanitizeIdentifier(device) + "_" + InstrumentWorkspaceService.SanitizeIdentifier(d.Operation), ReturnsValue = d.ReturnsValue, Selected = true, Fields = d.Fields.Select(f => new InstrumentActionFieldDefinition { Name = f.Name, Label = f.Label, Type = f.Type, DefaultValue = Convert.ToString(f.DefaultValue, CultureInfo.InvariantCulture), Unit = f.Unit, Options = f.Options == null ? string.Empty : string.Join("|", f.Options) }).ToList() });
            if (result.Count == 0)
            {
                DriverDiscoveryItem driver = _drivers.FirstOrDefault(v => string.Equals(v.AssemblyName, driverName, StringComparison.OrdinalIgnoreCase));
                if (driver != null) foreach (DriverMethodDiscovery method in driver.Methods) result.Add(new GeneratedInstrumentMethod { Device = device, Operation = method.Name, DisplayName = method.Name, DriverMethod = method.Name, DriverAssemblyPath = driver.Path, DriverTypeName = driver.TypeName, UseDirectReflection = true, FunctionName = "UI_" + InstrumentWorkspaceService.SanitizeIdentifier(device) + "_" + InstrumentWorkspaceService.SanitizeIdentifier(method.Name), ReturnsValue = !string.Equals(method.ReturnType, typeof(void).FullName, StringComparison.Ordinal), Selected = false, Fields = method.Parameters.Select(p => new InstrumentActionFieldDefinition { Name = p.Name, Label = p.Name, Type = ActionFieldType(p.TypeName), DefaultValue = p.DefaultValue, Unit = string.Empty, Options = string.Empty }).ToList() });
            }
            return result;
        }

        private void ApplyDriverSelection(ProjectInstrumentDefinition item, string driverName)
        {
            if (item == null || string.IsNullOrWhiteSpace(driverName) || string.Equals(item.DriverName, driverName, StringComparison.OrdinalIgnoreCase)) return;
            DriverDiscoveryItem driver = _drivers.FirstOrDefault(v => string.Equals(v.AssemblyName, driverName, StringComparison.OrdinalIgnoreCase));
            item.DriverName = driverName;
            if (driver != null) { item.DriverAssemblyPath = driver.Path; item.DriverTypeName = driver.TypeName; if (string.IsNullOrWhiteSpace(item.Category)) item.Category = driver.Category; }
            item.GeneratedMethods = CreateMethodSelection(item.Device, driverName); if (ReferenceEquals(item, _selectedInstrument)) { RefreshProjectEditor(); RefreshGeneratedMethodPanel(); }
        }
        private static string ActionFieldType(string typeName) { string value = typeName ?? string.Empty; if (value == typeof(bool).FullName) return "bool"; if (value == typeof(byte).FullName || value == typeof(short).FullName || value == typeof(int).FullName || value == typeof(long).FullName || value == typeof(ushort).FullName || value == typeof(uint).FullName) return "int"; if (value == typeof(float).FullName || value == typeof(double).FullName || value == typeof(decimal).FullName) return "double"; return "string"; }

        private void DeleteInstrument_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedInstrument == null) return; if (MessageBox.Show("确定删除仪器定义“" + _selectedInstrument.DisplayName + "”？", "删除定义", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _document.Instruments.Remove(_selectedInstrument); _selectedInstrument = _document.Instruments.FirstOrDefault(); SaveWorkspace(false); PopulateProjectInstrumentPage(); RefreshStationResources(); MessageBox.Show("仪器定义已删除，当前页面已更新。", "删除定义");
        }
        private void GenerateMethods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _service.Save(_document);
                int count = _service.GenerateMethods(_document);
                _configurationChanged();
                _log("已生成仪器方法 " + count + " 个：" + _service.GeneratedSourcePath);
                MessageBox.Show("已生成 " + count + " 个方法。\n\n请重新初始化项目，SEQ 编辑器即可使用这些方法。", "生成方法", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("生成方法失败：\n" + ex.Message, "生成方法", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { RefreshProjectEditor(); RefreshGeneratedMethodPanel(); }
        }

        private static string Tail(string log)
        {
            string[] lines = (log ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - 12)));
        }
        private async System.Threading.Tasks.Task TestConnectionAsync(ProjectInstrumentDefinition item, Button button)
        {
            if (_testConnection == null) { MessageBox.Show("当前运行环境没有连接入口。", "测试连接", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                button.IsEnabled = false; button.Content = "连接中...";
                JArray payload = new JArray(new JObject { ["Name"] = item.Device, ["Type"] = item.Device, ["Mode"] = item.DriverName, ["Resource"] = item.Resource, ["Parameter"] = item.Parameter });
                await _testConnection(payload.ToString(Newtonsoft.Json.Formatting.None));
                MessageBox.Show("连接请求已完成。可在运行日志中查看仪器返回信息。", "测试连接", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("连接失败：\n" + ex.Message, "测试连接", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { button.IsEnabled = true; button.Content = "测试连接"; }
        }
        private void SaveWorkspace(bool showMessage)
        {
            _service.Save(_document); _configurationChanged(); _log("多工位仪器配置已保存：" + _service.ConfigPath);
            if (showMessage) MessageBox.Show("配置已保存。", "仪器中心", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void CheckConflicts_Click(object sender, RoutedEventArgs e)
        {
            List<WorkspaceConflict> conflicts = _service.Validate(_document); MessageBox.Show(conflicts.Count == 0 ? "未发现资源冲突。" : string.Join(Environment.NewLine, conflicts), "资源冲突检查", MessageBoxButton.OK, conflicts.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        private void CopyStation_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStation == null) return; Window dialog = new Window { Title = "从工位复制配置", Width = 360, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow((DependencyObject)sender), ResizeMode = ResizeMode.NoResize };
            StackPanel panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(new TextBlock { Text = "将其他工位的独立仪器和共用资源配置复制到 " + _selectedStation.StationName, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }); ComboBox source = FlatCombo(_document.Stations.Where(s => s != _selectedStation).Select(s => s.StationName)); source.SelectedIndex = 0; panel.Children.Add(source); Button copy = Primary("复制配置"); copy.HorizontalAlignment = HorizontalAlignment.Right; copy.Click += delegate { StationInstrumentDefinition from = _document.Stations.FirstOrDefault(s => s.StationName == Convert.ToString(source.SelectedItem)); if (from != null) { _selectedStation.IndependentDevices = from.IndependentDevices.Select(v => new StationInstrumentInstance { TemplateDevice = v.TemplateDevice, InstanceName = v.TemplateDevice + "-" + _selectedStation.StationNumber.ToString("00"), Resource = v.Resource }).ToList(); _selectedStation.PowerInstrumentId = from.PowerInstrumentId; _selectedStation.PowerChannelGroup = from.PowerChannelGroup; _selectedStation.PlcInstrumentId = from.PlcInstrumentId; } dialog.DialogResult = true; dialog.Close(); }; panel.Children.Add(copy); dialog.Content = panel; if (dialog.ShowDialog() == true) { RefreshStationCards(); RefreshStationEditor(); }
        }
        private void ChangeStationCount(int count)
        {
            count = Math.Max(1, Math.Min(12, count)); _document.StationCount = count; _document.Stations.RemoveAll(s => s.StationNumber > count); for (int i = 1; i <= count; i++) if (!_document.Stations.Any(s => s.StationNumber == i)) _document.Stations.Add(new StationInstrumentDefinition { StationNumber = i }); _selectedStation = _document.Stations.FirstOrDefault(); RefreshStationCards(); RefreshStationEditor();
        }
        private void AssignIndependent(string device, string name, string resource) { AssignIndependent(_selectedStation, device, name, resource); }
        private void AssignIndependent(StationInstrumentDefinition station, string device, string name, string resource)
        {
            if (station == null || string.IsNullOrWhiteSpace(device)) return; StationInstrumentInstance existing = station.IndependentDevices.FirstOrDefault(v => v.TemplateDevice == device); if (existing == null) station.IndependentDevices.Add(new StationInstrumentInstance { TemplateDevice = device, InstanceName = name, Resource = resource }); else { existing.InstanceName = name; existing.Resource = resource; } _selectedStation = station; RefreshStationCards(); RefreshStationEditor();
        }
        private Border RemovableTag(StationInstrumentInstance instance)
        {
            Border tag = SmallTag(instance.TemplateDevice + "  ×"); tag.Cursor = Cursors.Hand; tag.MouseLeftButtonDown += delegate { _selectedStation.IndependentDevices.Remove(instance); RefreshStationCards(); RefreshStationEditor(); }; return tag;
        }

        private static ContextMenu DisconnectMenu(Action disconnect)
        {
            ContextMenu menu = new ContextMenu(); MenuItem item = new MenuItem { Header = "取消连接" }; item.Click += delegate { disconnect(); }; menu.Items.Add(item); return menu;
        }

        private void UpdateDiscoveryCounts() { if (_driverCount != null) _driverCount.Text = "自动发现 " + _drivers.Count + " 个驱动"; if (_methodCount != null) _methodCount.Text = _drivers.Sum(d => d.Methods.Count) + " 个方法"; }
        private ProjectInstrumentDefinition FindInstrument(string id) { return _document.Instruments.FirstOrDefault(i => i.Id == id); }
        private static string PowerShortName(ProjectInstrumentDefinition item) { if (item == null) return "电源"; int index = item.DisplayName.LastIndexOf('-'); return index >= 0 ? "电源" + item.DisplayName.Substring(index + 1) : item.DisplayName; }
        private static IEnumerable<string> ChannelGroups(ProjectInstrumentDefinition item) { int count = item == null ? 1 : Math.Max(1, item.ChannelCount); if (count >= 4) return new[] { "CH1+CH2", "CH3+CH4" }; if (count == 3) return new[] { "CH1+CH2", "CH3" }; if (count == 2) return new[] { "CH1+CH2" }; return new[] { "CH1" }; }
        private static string GuessDevice(string driver, string name) { string value = ((driver ?? string.Empty) + " " + (name ?? string.Empty)).ToUpperInvariant(); if (value.Contains("DMM") || value.Contains("34461")) return "DMM"; if (value.Contains("DCDC") || value.Contains("AN23600")) return "DCDC_LOAD"; if (value.Contains("PLC") || value.Contains("S7")) return "PLC"; if (value.Contains("DAQ") || value.Contains("9227") || value.Contains("6229")) return "DAQ"; if (value.Contains("RELAY") || value.Contains("SHT")) return "RELAY_FCT"; if (value.Contains("HVDC") || value.Contains("C3000")) return "HVDC"; if (value.Contains("POWER") || value.Contains("ITECH")) return "LVDC"; if (value.Contains("CAN")) return "DUTCAN"; if (value.Contains("RES")) return "RES"; return InstrumentWorkspaceService.SanitizeIdentifier(name).ToUpperInvariant(); }

        private static Grid InstrumentColumns() { Grid grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }); return grid; }
        private static Border Box() { return new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(225, 231, 239)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) }; }
        private static SolidColorBrush BorderBrush() { return new SolidColorBrush(Color.FromRgb(216, 226, 238)); }
        private static SolidColorBrush Accent() { return new SolidColorBrush(Color.FromRgb(31, 111, 232)); }
        private static SolidColorBrush Ink() { return new SolidColorBrush(Color.FromRgb(30, 42, 58)); }
        private static SolidColorBrush Bg(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
        private static TextBlock Fact(string text, string glyph) { return new TextBlock { Text = text, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, Foreground = Ink(), VerticalAlignment = VerticalAlignment.Center }; }
        private static Border Divider() { return new Border { Width = 1, Height = 22, Background = BorderBrush(), Margin = new Thickness(20, 0, 20, 0) }; }
        private static Button Primary(string text) { return new Button { Content = text, MinWidth = 120, Height = 34, Padding = new Thickness(15, 6, 15, 6), Margin = new Thickness(5, 0, 0, 0), Background = Accent(), Foreground = Brushes.White, BorderBrush = Accent(), FontWeight = FontWeights.SemiBold, Template = RoundedButtonTemplate(4) }; }
        private static Button Secondary(string text) { return new Button { Content = text, MinWidth = 105, Height = 34, Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(5, 0, 0, 0), Background = Brushes.White, BorderBrush = BorderBrush(), Foreground = Ink(), Template = RoundedButtonTemplate(4) }; }
        private static Button Danger(string text) { return new Button { Content = text, MinWidth = 110, Height = 34, Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(8, 0, 0, 0), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(240, 112, 112)), Foreground = new SolidColorBrush(Color.FromRgb(214, 45, 45)), Template = RoundedButtonTemplate(4) }; }
        private static Button LinkButton(string text) { return new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Accent(), Cursor = Cursors.Hand }; }
        private static TextBox FlatText(string text) { return new TextBox { Text = text ?? string.Empty, Height = 32, Padding = new Thickness(8, 4, 8, 4), BorderBrush = BorderBrush(), Background = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center }; }
        private static ComboBox FlatCombo(IEnumerable<string> source) { return new ComboBox { ItemsSource = source == null ? null : source.ToList(), Height = 32, Padding = new Thickness(7, 3, 7, 3), BorderBrush = BorderBrush(), Background = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center }; }
        private static ToggleButton SegmentButton(string text, bool selected) { ToggleButton button = new ToggleButton { Content = text, IsChecked = selected, Height = 32, MinWidth = 82, Margin = new Thickness(0, 0, 6, 0), Background = selected ? Accent() : Bg(244, 247, 251), Foreground = selected ? Brushes.White : Ink(), BorderBrush = selected ? Accent() : BorderBrush(), Template = RoundedButtonTemplate(4) }; button.Checked += delegate { button.Background = Accent(); button.Foreground = Brushes.White; button.BorderBrush = Accent(); }; button.Unchecked += delegate { button.Background = Bg(244, 247, 251); button.Foreground = Ink(); button.BorderBrush = BorderBrush(); }; return button; }
        private static ToggleButton ChipButton(string text, bool selected) { ToggleButton button = new ToggleButton { Content = text, IsChecked = selected, MinHeight = 28, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 2, 7, 5), Background = selected ? Bg(229, 241, 255) : Bg(247, 249, 252), Foreground = selected ? Accent() : Ink(), BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(154, 194, 242)) : BorderBrush(), Template = RoundedButtonTemplate(13) }; button.Checked += delegate { button.Background = Bg(229, 241, 255); button.Foreground = Accent(); button.BorderBrush = new SolidColorBrush(Color.FromRgb(154, 194, 242)); }; button.Unchecked += delegate { button.Background = Bg(247, 249, 252); button.Foreground = Ink(); button.BorderBrush = BorderBrush(); }; return button; }
        private static ControlTemplate RoundedButtonTemplate(double radius) { FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius)); border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.AppendChild(presenter); return new ControlTemplate(typeof(ButtonBase)) { VisualTree = border }; }
        private static Border Chip(string text, bool green) { return new Border { Background = green ? Bg(239, 249, 242) : Bg(239, 246, 255), BorderBrush = green ? new SolidColorBrush(Color.FromRgb(190, 224, 199)) : new SolidColorBrush(Color.FromRgb(194, 216, 243)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 5, 9, 5), Child = new TextBlock { Text = text, Foreground = green ? new SolidColorBrush(Color.FromRgb(39, 111, 68)) : Accent() } }; }
        private static Border SmallTag(string text) { return new Border { Background = Brushes.White, BorderBrush = BorderBrush(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 6, 5), Child = new TextBlock { Text = text, FontSize = 11, Foreground = Ink() } }; }
        private static TextBlock PaletteHeading(string title, string hint) { return new TextBlock { Text = title + "    " + hint, FontWeight = FontWeights.SemiBold, Foreground = Ink(), Margin = new Thickness(0, 7, 0, 8) }; }
        private static void AddCell(Grid grid, string text, int column, bool header) { TextBlock block = new TextBlock { Text = text, Padding = new Thickness(8, 6, 8, 6), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal, Foreground = header ? Ink() : new SolidColorBrush(Color.FromRgb(65, 76, 90)), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; grid.Children.Add(block); Grid.SetColumn(block, column); }
        private static void AddLabel(Grid grid, string text, int row, int column) { TextBlock label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4), Foreground = Ink() }; grid.Children.Add(label); Grid.SetRow(label, row); Grid.SetColumn(label, column); }
        private static void AddControl(Grid grid, UIElement control, int row, int column, int span = 1) { FrameworkElement element = control as FrameworkElement; if (element != null) element.Margin = new Thickness(0, 4, 12, 4); grid.Children.Add(control); Grid.SetRow(control, row); Grid.SetColumn(control, column); Grid.SetColumnSpan(control, span); }
        private static StackPanel Labeled(string label, UIElement control) { StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) }; row.Children.Add(new TextBlock { Text = label, Width = 75, VerticalAlignment = VerticalAlignment.Center }); FrameworkElement element = control as FrameworkElement; if (element != null) element.Width = 200; row.Children.Add(control); return row; }
        internal sealed class ResourceOption { public ResourceOption(string key, string label) { Key = key; Label = label; } public string Key { get; private set; } public string Label { get; private set; } public override string ToString() { return Label; } }
    }
}
