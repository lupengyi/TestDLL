using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ManualCanDebug.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    internal sealed class ActionConfigurationPanel : Grid
    {
        private readonly ProductLocatorRepository _locatorRepository;
        private readonly Func<SequenceStepDefinition, Task<string>> _execute;
        private readonly Action<SequenceStepDefinition, IDictionary<string, string>> _save;
        private readonly Action<string> _log;
        private readonly Func<string> _getProduct;
        private readonly Func<string> _getDbcPath;
        private readonly Func<LegacyStepExecutionResult> _getLastPlatformResult;
        private readonly ComboBox _source = Combo(150);
        private readonly ComboBox _target = Combo(180);
        private readonly ComboBox _operation = Combo(220);
        private readonly TextBox _stepName = Box(260);
        private readonly Border _body = Card();
        private StackPanel _fieldPanel;
        private readonly Dictionary<string, ActionFieldEditor> _fieldEditors = new Dictionary<string, ActionFieldEditor>(StringComparer.Ordinal);
        private readonly ComboBox _resultMode = Combo(170);
        private readonly TextBox _lowLimit = Box(95); private readonly TextBox _highLimit = Box(95); private readonly ComboBox _compare = Combo(120); private readonly TextBox _unit = Box(80); private readonly TextBox _stringLimit = Box(160); private readonly TextBox _outputVariable = Box(150);
        private StackPanel _resultFields;
        private readonly Expander _resultExpander = new Expander { Header = "结果与判断", Margin = new Thickness(0, 8, 0, 0) };
        private readonly ComboBox _runMode = Combo(110); private readonly CheckBox _recordLog = new CheckBox { Content = "显示在平台界面", IsChecked = true, Margin = new Thickness(8, 6, 8, 4), ToolTip = "对应SEQ字段 RecordingLog；取消后动作仍执行，但不作为平台显示记录项" };
        private readonly TextBlock _status = new TextBlock { Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ObservableCollection<LocatorSignalRow> _locatorSignals = new ObservableCollection<LocatorSignalRow>();
        private readonly ObservableCollection<DbcSignalEditRow> _dbcSignals = new ObservableCollection<DbcSignalEditRow>();
        private ComboBox _productBox, _locatorOperation, _tableBox;
        private DataGrid _locatorGrid;
        private CheckBox _verifyAfterWrite;
        private TextBlock _locatorSelectionSummary;
        private WrapPanel _locatorSelectedSignalsPanel;
        private Button _configureLocatorSignalsButton;
        private ComboBox _dbcMessage, _dbcMode, _dbcReadSignal;
        private DataGrid _dbcGrid;
        private TextBox _dbcPeriod, _dbcTimeout, _dbcRawData;
        private WrapPanel _dbcSelectedSignalsPanel;
        private Button _configureDbcSignalsButton;
        private TextBlock _dbcSelectionSummary;
        private DbcDatabase _dbcDatabase;
        private string _dbcLoadedPath;
        private ActionDescriptor _descriptor;
        private SequenceStepDefinition _loadedStep;
        private Dictionary<string, string> _loadedBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _loading;
        private bool _debugMode;
        private Button _runButton;
        private TextBox _relayChannelsBox, _relayValuesBox;
        private readonly TextBlock _debugDetails = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(42, 92, 160)), Margin = new Thickness(8, 4, 8, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        public event Action<ActionHistoryRow> ExecutionRecorded;

        public void SetDebugMode(bool enabled)
        {
            _debugMode = enabled;
            if (_runButton != null) _runButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            _debugDetails.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        public ActionConfigurationPanel(ProductLocatorRepository locatorRepository, Func<SequenceStepDefinition, Task<string>> execute, Action<SequenceStepDefinition, IDictionary<string, string>> save, Action<string> log, Func<string> getProduct, Func<string> getDbcPath = null, Func<LegacyStepExecutionResult> getLastPlatformResult = null)
        {
            _locatorRepository = locatorRepository; _execute = execute; _save = save; _log = log; _getProduct = getProduct; _getDbcPath = getDbcPath; _getLastPlatformResult = getLastPlatformResult;
            Background = Brushes.White; BuildUi(); LoadSources();
        }

        public static SequenceStepDefinition CreateDraft()
        {
            return new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "新动作（未配置）" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", "LVDC" }, { "Operation", "SetVoltage" }, { "Voltage", 24.0 }, { "ResultMode", "Action" } });
        }
        public static SequenceStepDefinition CreateFromDescriptor(ActionDescriptor descriptor)
        {
            if (descriptor == null) return CreateDraft(); bool mainTest = string.Equals(descriptor.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase), logic = string.Equals(descriptor.Source, "流程逻辑", StringComparison.OrdinalIgnoreCase); Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", descriptor.Target + " " + descriptor.DisplayName }, { "RunMode", "Normal" }, { "FunctionName", mainTest ? descriptor.FunctionName : logic ? "FCT_ExecuteLogic" : "FCT_ExecuteAction" }, { "RecordingLog", true }, { "ResultMode", descriptor.ReturnsValue ? "Information" : "Action" } }; if (!string.IsNullOrWhiteSpace(descriptor.Operation)) values["Operation"] = descriptor.Operation; if (!logic && !string.IsNullOrWhiteSpace(descriptor.Device)) values["Device"] = descriptor.Device; if (string.Equals(descriptor.BindingMode, "Plugin", StringComparison.OrdinalIgnoreCase)) { values["PluginAssembly"] = descriptor.PluginAssembly; values["PluginType"] = descriptor.PluginType; } foreach (ActionFieldSpec field in descriptor.Fields) values[field.Name] = field.DefaultValue; return new SequenceStepDefinition(values);
        }

        public void SelectActionShortcut(string source, string target, string operation)
        {
            if (string.Equals(source, "产品内部通信", StringComparison.Ordinal) && string.Equals(target, "FT/Locator内存", StringComparison.Ordinal)) operation = (operation ?? string.Empty).StartsWith("写", StringComparison.Ordinal) ? "写入" : "读取"; _loading = true; _source.SelectedItem = source; RefreshTargets(); _target.SelectedItem = target; RefreshOperations(); object selected = _operation.Items.Cast<object>().FirstOrDefault(value => value is ActionDescriptor ? string.Equals(((ActionDescriptor)value).Operation, operation, StringComparison.OrdinalIgnoreCase) || string.Equals(((ActionDescriptor)value).DisplayName, operation, StringComparison.OrdinalIgnoreCase) : string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), operation, StringComparison.OrdinalIgnoreCase)); if (selected != null) _operation.SelectedItem = selected; BuildEditor(); _loading = false; UpdateMainTestBindingStatus();
        }

        public void LoadStep(SequenceStepDefinition step, IDictionary<string, string> bindings)
        {
            _loading = true; _loadedStep = step == null ? CreateDraft() : SequenceEditing.Clone(step); _loadedBindings = bindings == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(bindings, StringComparer.Ordinal);
            _stepName.Text = _loadedStep.StepName; _runMode.SelectedItem = _loadedStep.RunMode; _recordLog.IsChecked = _loadedStep.RecordingLog;
            string resultMode = InferResultMode(_loadedStep); _resultMode.SelectedItem = resultMode == "NumericLimit" ? "数值范围判断" : resultMode == "StringLimit" ? "字符串判断" : resultMode == "PassFail" ? "PASS/FAIL" : resultMode == "Variable" ? "保存变量" : resultMode == "Information" ? "只记录信息" : "不产生结果"; _lowLimit.Text = Convert.ToString(_loadedStep.Get("LowLimit", 0), CultureInfo.InvariantCulture); _highLimit.Text = Convert.ToString(_loadedStep.Get("HighLimit", 0), CultureInfo.InvariantCulture); _compare.SelectedItem = Convert.ToString(_loadedStep.Get("Comtype", "GELE"), CultureInfo.InvariantCulture); _unit.Text = Convert.ToString(_loadedStep.Get("Unit", string.Empty), CultureInfo.InvariantCulture); _stringLimit.Text = Convert.ToString(_loadedStep.Get("Limit", string.Empty), CultureInfo.InvariantCulture); _outputVariable.Text = Convert.ToString(_loadedStep.Get("OutputVariable", string.Empty), CultureInfo.InvariantCulture);
            string source = DetectSource(_loadedStep); _source.SelectedItem = source; RefreshTargets(); SelectTargetAndOperation(_loadedStep); BuildEditor(); _loading = false; UpdateMainTestBindingStatus();
        }

        public SequenceStepDefinition BuildStep()
        {
            if (_source.SelectedItem == null) throw new InvalidOperationException("请选择动作来源。");
            SequenceStepDefinition step;
            string source = Convert.ToString(_source.SelectedItem, CultureInfo.InvariantCulture);
            if (source == "产品内部通信" && string.Equals(Convert.ToString(_target.SelectedItem, CultureInfo.InvariantCulture), "FT/Locator内存", StringComparison.Ordinal)) step = BuildLocatorStep();
            else if (source == "产品内部通信") step = BuildDescriptorStep(false);
            else if (source == "产品DBC通信") step = BuildDbcStep();
            else step = BuildDescriptorStep(source == "流程逻辑");
            step.StepName = string.IsNullOrWhiteSpace(_stepName.Text) ? BuildAutomaticName() : _stepName.Text.Trim(); step.RunMode = Convert.ToString(_runMode.SelectedItem, CultureInfo.InvariantCulture) ?? "Normal"; step.RecordingLog = _recordLog.IsChecked == true; ApplyResult(step); return step;
        }

        public IDictionary<string, string> BuildBindings()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal); foreach (KeyValuePair<string, ActionFieldEditor> pair in _fieldEditors) if (pair.Value.IsExposed) result[pair.Key] = pair.Value.ParameterName; return result;
        }

        public bool TryShowDirectBatchConfiguration(Window owner, out SequenceStepDefinition configured)
        {
            configured = null; string source = Convert.ToString(_source.SelectedItem, CultureInfo.InvariantCulture), target = Convert.ToString(_target.SelectedItem, CultureInfo.InvariantCulture), operation = Convert.ToString(_operation.SelectedItem, CultureInfo.InvariantCulture);
            if (source == "产品内部通信" && target == "FT/Locator内存" && _locatorSignals.Count > 0)
            {
                ProductLocatorTable table = _tableBox == null ? null : _tableBox.SelectedItem as ProductLocatorTable; LocatorSignalConfigurationWindow dialog = new LocatorSignalConfigurationWindow(_locatorSignals, operation, table == null ? string.Empty : table.DisplayName, _stepName.Text, _debugMode ? ExecuteSpecializedAsync : null) { Owner = owner }; if (dialog.ShowDialog() == true) { _stepName.Text = dialog.StepName; configured = BuildStep(); } return true;
            }
            if (source == "产品DBC通信" && _dbcSignals.Count > 0 && operation != "停止周期发送" && operation != "发送原始帧")
            {
                DbcMessageDefinition message = _dbcMessage == null ? null : _dbcMessage.SelectedItem as DbcMessageDefinition; DbcSignalConfigurationWindow dialog = new DbcSignalConfigurationWindow(_dbcSignals, operation, message == null ? string.Empty : message.Name, _stepName.Text, _debugMode ? ExecuteSpecializedAsync : null) { Owner = owner }; if (dialog.ShowDialog() == true) { _stepName.Text = dialog.StepName; configured = BuildStep(); } return true;
            }
            return false;
        }

        private void BuildUi()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border selector = Card(); selector.Padding = new Thickness(12, 8, 12, 8); WrapPanel top = new WrapPanel(); top.Children.Add(Label("动作来源")); top.Children.Add(_source); top.Children.Add(Label("目标设备/协议")); top.Children.Add(_target); top.Children.Add(Label("功能")); top.Children.Add(_operation); top.Children.Add(Label("动作名称")); top.Children.Add(_stepName); selector.Child = top; Children.Add(selector);
            ScrollViewer scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 8) }; Grid.SetRow(scroll, 1); Children.Add(scroll);
            Border footer = Card(); footer.Padding = new Thickness(12, 8, 12, 8); DockPanel footerDock = new DockPanel(); StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); _runButton = Button("立即试运行", Execute_Click); Button save = PrimaryButton("完成配置", Complete_Click); buttons.Children.Add(_runButton); buttons.Children.Add(save); footerDock.Children.Add(buttons); StackPanel resultArea = new StackPanel(); WrapPanel common = new WrapPanel(); common.Children.Add(Label("运行模式")); _runMode.ItemsSource = new[] { "Normal", "Skip", "Break" }; _runMode.SelectedIndex = 0; common.Children.Add(_runMode); common.Children.Add(_recordLog); common.Children.Add(_status); resultArea.Children.Add(common); resultArea.Children.Add(_debugDetails); footerDock.Children.Add(resultArea); footer.Child = footerDock; Grid.SetRow(footer, 2); Children.Add(footer);
            _source.SelectionChanged += (s, e) => { if (_loading) return; RefreshTargets(); BuildEditor(); };
            _target.SelectionChanged += (s, e) => { if (_loading) return; RefreshOperations(); BuildEditor(); };
            _operation.SelectionChanged += (s, e) => { if (_loading) return; BuildEditor(); };
            _resultMode.ItemsSource = new[] { "不产生结果", "只记录信息", "保存变量", "数值范围判断", "字符串判断", "PASS/FAIL" }; _resultMode.SelectedIndex = 0; _resultMode.SelectionChanged += (s, e) => RefreshResultFields();
            _compare.ItemsSource = new[] { "GELE", "GE", "GT", "LE", "LT", "EQ", "NE" }; _compare.SelectedIndex = 0;
            _resultExpander.Content = BuildResultPanel();
        }

        private void LoadSources() { _loading = true; _source.ItemsSource = new[] { "仪器", "产品内部通信", "产品DBC通信", "流程逻辑", "原平台组合测试" }; _source.SelectedIndex = 0; RefreshTargets(); _target.SelectedIndex = 0; RefreshOperations(); _operation.SelectedIndex = 0; BuildEditor(); _loading = false; UpdateMainTestBindingStatus(); }

        private void RefreshTargets()
        {
            string source = Convert.ToString(_source.SelectedItem, CultureInfo.InvariantCulture);
            if (source == "仪器") _target.ItemsSource = ActionCatalog.PickerDescriptors(source).Select(ActionCatalog.PickerTarget).Distinct().ToList();
            else if (source == "流程逻辑") _target.ItemsSource = new[] { "流程控制" };
            else if (source == "产品内部通信") _target.ItemsSource = new[] { "FT/Locator内存", "产品基础命令" };
            else if (source == "产品DBC通信") _target.ItemsSource = new[] { "辅驱/DCDC/PDU DBC" }; else _target.ItemsSource = new[] { "原平台TestDllMain" };
            if (_target.Items.Count > 0) _target.SelectedIndex = 0; RefreshOperations();
        }

        private void RefreshOperations()
        {
            string source = Convert.ToString(_source.SelectedItem, CultureInfo.InvariantCulture), target = Convert.ToString(_target.SelectedItem, CultureInfo.InvariantCulture);
            if (source == "仪器") _operation.ItemsSource = ActionCatalog.PickerDescriptors(source).Where(value => string.Equals(ActionCatalog.PickerTarget(value), target, StringComparison.Ordinal)).ToList();
            else if (source == "流程逻辑") _operation.ItemsSource = ActionCatalog.Descriptors.Where(value => value.Source == source && value.Target == target).ToList();
            else if (source == "产品内部通信" && target == "产品基础命令") _operation.ItemsSource = ActionCatalog.Descriptors.Where(value => value.Source == source && value.Target == target).ToList();
            else if (source == "产品内部通信") _operation.ItemsSource = new[] { "读取", "写入" };
            else if (source == "产品DBC通信") _operation.ItemsSource = new[] { "发送一次", "开始周期发送", "停止周期发送", "读取DBC信号", "发送原始帧" }; else _operation.ItemsSource = new[] { _loadedStep == null ? "原平台组合方法" : _loadedStep.FunctionName };
            if (_operation.Items.Count > 0) _operation.SelectedIndex = 0;
        }

        private void BuildEditor()
        {
            _fieldEditors.Clear(); _relayChannelsBox = null; _relayValuesBox = null; Grid content = new Grid { Margin = new Thickness(14) }; content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string source = Convert.ToString(_source.SelectedItem, CultureInfo.InvariantCulture), target = Convert.ToString(_target.SelectedItem, CultureInfo.InvariantCulture);
            bool calculatedResult = source == "原平台组合测试" && string.Equals(_loadedStep.FunctionName, "FCT_CANCalculatedResults", StringComparison.Ordinal); bool hasMainPanel = source != "原平台组合测试" || calculatedResult;
            if (source == "原平台组合测试") { _descriptor = null; if (calculatedResult) content.Children.Add(BuildCalculatedResultPanel()); }
            else if (source == "产品内部通信" && target == "FT/Locator内存") content.Children.Add(BuildLocatorPanel());
            else if (source == "产品DBC通信") content.Children.Add(BuildDbcPanel());
            else { _descriptor = _operation.SelectedItem as ActionDescriptor; hasMainPanel = _descriptor == null || _descriptor.Fields.Count > 0 || !IsReadAction(); if (hasMainPanel) content.Children.Add(BuildDescriptorPanel()); }
            bool readAction = !calculatedResult && IsReadAction(); Panel oldParent = _resultExpander.Parent as Panel; if (oldParent != null) oldParent.Children.Remove(_resultExpander); _resultExpander.Header = null; _resultExpander.Margin = new Thickness(0); _resultExpander.IsExpanded = readAction; _resultExpander.Visibility = readAction ? Visibility.Visible : Visibility.Collapsed; Grid.SetRow(_resultExpander, hasMainPanel ? 1 : 0); content.Children.Add(_resultExpander); _body.Child = content; RefreshResultFields(); UpdateMainTestBindingStatus();
        }

        private void UpdateMainTestBindingStatus()
        {
            if (_loading) { _status.Text = "MainTest：正在加载动作配置"; _status.Foreground = Brushes.DimGray; return; }
            try { SequenceStepDefinition step = BuildStep(); bool linked = MainTestMethodCatalog.Contains(step.FunctionName); _status.Text = MainTestMethodCatalog.BindingSummary(step); _status.Foreground = linked ? Brushes.DarkGreen : Brushes.DarkRed; _status.ToolTip = _status.Text; }
            catch { _status.Text = "MainTest：等待完成动作配置"; _status.Foreground = Brushes.DarkOrange; }
        }

        private UIElement BuildDescriptorPanel()
        {
            Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.Children.Add(Heading(_descriptor == null ? "兼容旧STEP" : _descriptor.Target + " · " + _descriptor.DisplayName)); _fieldPanel = new StackPanel();
            if (_descriptor == null) _fieldPanel.Children.Add(new TextBlock { Text = "当前是导入的旧STEP。可以保留原参数，也可以重新选择来源、设备和功能转换为新动作。", Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) });
            else foreach (ActionFieldSpec field in _descriptor.Fields) AddFieldEditor(_fieldPanel, field);
            Grid.SetRow(_fieldPanel, 1); grid.Children.Add(_fieldPanel); return grid;
        }

        private UIElement BuildCalculatedResultPanel()
        {
            string product = _getProduct == null ? string.Empty : (_getProduct() ?? string.Empty).ToUpperInvariant(); int offset = _loadedStep.GetInt("AddrOffset", -1); int length = _loadedStep.GetInt("TableLength", 0); string calculation = Convert.ToString(_loadedStep.Get("CalculationType"), CultureInfo.InvariantCulture) ?? string.Empty; bool automatic = Convert.ToBoolean(_loadedStep.Get("AutoProductProfile", true), CultureInfo.InvariantCulture); string configuredDrive = Convert.ToString(_loadedStep.Get("DriveTarget"), CultureInfo.InvariantCulture); string target; int expectedOffset; int expectedLength;
            bool currentCalculation = calculation == "ThreePhaseCurrentRms";
            if (product == "C92" || product == "C96") { int tm1Offset = currentCalculation ? 0x7C : 0x6C, tm2Offset = currentCalculation ? 0x94 : 0x84; if (string.Equals(configuredDrive, "TM2", StringComparison.OrdinalIgnoreCase) || (!automatic && offset == tm2Offset)) target = "TM2 主驱"; else target = "TM1 主驱"; expectedOffset = target.StartsWith("TM2", StringComparison.Ordinal) ? tm2Offset : tm1Offset; expectedLength = currentCalculation ? 40 : 10; }
            else if (product == "C95") { target = "单主驱"; expectedOffset = currentCalculation ? 0x70 : 0x5C; expectedLength = currentCalculation ? 36 : 8; }
            else if (product == "C91") { target = "单主驱"; expectedOffset = currentCalculation ? 0x74 : 0x64; expectedLength = currentCalculation ? 36 : 9; }
            else { target = "未识别产品"; expectedOffset = offset; expectedLength = length; }
            bool matched = automatic || (offset == expectedOffset && length == expectedLength && target != "未知驱动" && target != "未识别产品"); Grid grid = new Grid { Margin = new Thickness(6, 8, 6, 8) }; for (int index = 0; index < 4; index++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = index % 2 == 0 ? new GridLength(110) : new GridLength(1, GridUnitType.Star) }); for (int index = 0; index < 4; index++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); TextBlock heading = Heading("产品内部结果解析"); Grid.SetColumnSpan(heading, 4); grid.Children.Add(heading); AddReadOnlyCalculatedField(grid, "当前产品", product, 1, 0); AddReadOnlyCalculatedField(grid, "驱动目标", target, 1, 2); AddReadOnlyCalculatedField(grid, "实际读取表", "0x" + expectedOffset.ToString("X2", CultureInfo.InvariantCulture), 2, 0); AddReadOnlyCalculatedField(grid, "读取长度", expectedLength + " 字节", 2, 2); AddReadOnlyCalculatedField(grid, "解析类型", calculation == "PackedFaultStatus" ? "电机故障位解析" : calculation == "ThreePhaseCurrentRms" ? "三相RMS电流计算" : calculation, 3, 0); TextBlock stateLabel = Label("配置方式"); Grid.SetRow(stateLabel, 3); Grid.SetColumn(stateLabel, 2); grid.Children.Add(stateLabel); Border state = new Border { Background = matched ? new SolidColorBrush(Color.FromRgb(235, 249, 241)) : new SolidColorBrush(Color.FromRgb(255, 235, 235)), BorderBrush = matched ? new SolidColorBrush(Color.FromRgb(44, 166, 96)) : new SolidColorBrush(Color.FromRgb(210, 51, 51)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(4), Child = new TextBlock { Text = automatic ? "自动跟随当前产品Locator" : matched ? "固定地址与当前产品匹配" : "固定地址不匹配，应为0x" + expectedOffset.ToString("X2", CultureInfo.InvariantCulture) + " / " + expectedLength + "字节", Foreground = matched ? Brushes.DarkGreen : Brushes.DarkRed, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } }; Grid.SetRow(state, 3); Grid.SetColumn(state, 3); grid.Children.Add(state); return grid;
        }

        private static void AddReadOnlyCalculatedField(Grid grid, string label, string value, int row, int column)
        {
            TextBlock name = Label(label); Grid.SetRow(name, row); Grid.SetColumn(name, column); grid.Children.Add(name); TextBox box = Box(200); box.Text = value ?? string.Empty; box.IsReadOnly = true; box.Background = new SolidColorBrush(Color.FromRgb(247, 249, 252)); box.Foreground = new SolidColorBrush(Color.FromRgb(37, 49, 67)); box.FontWeight = FontWeights.SemiBold; Grid.SetRow(box, row); Grid.SetColumn(box, column + 1); grid.Children.Add(box);
        }
        private UIElement BuildLocatorPanel()
        {
            Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.Children.Add(Heading("产品内部 FT/Locator 通信"));
            WrapPanel selector = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
            _productBox = Combo(120); _productBox.ItemsSource = _locatorRepository.Products; _productBox.ItemTemplate = TextItemTemplate("Product"); _productBox.SelectionChanged += (s, e) => RefreshLocatorTables();
            _locatorOperation = Combo(120); _locatorOperation.ItemsSource = new[] { "读取", "写入" }; _locatorOperation.SelectedItem = Convert.ToString(_operation.SelectedItem, CultureInfo.InvariantCulture); _locatorOperation.SelectionChanged += (s, e) => { _operation.SelectedItem = _locatorOperation.SelectedItem; RefreshLocatorTables(); };
            _tableBox = Combo(300); _tableBox.ItemTemplate = TextItemTemplate("DisplayName"); _tableBox.SelectionChanged += (s, e) => RefreshLocatorSignals();
            selector.Children.Add(Label("产品")); selector.Children.Add(_productBox); selector.Children.Add(Label("操作")); selector.Children.Add(_locatorOperation); selector.Children.Add(Label("表名")); selector.Children.Add(_tableBox);
            _verifyAfterWrite = new CheckBox { Content = "写入后回读验证", IsChecked = Convert.ToBoolean(_loadedStep.Get("VerifyAfterWrite", true)), Margin = new Thickness(10, 8, 8, 4), VerticalAlignment = VerticalAlignment.Center }; selector.Children.Add(_verifyAfterWrite);
            _configureLocatorSignalsButton = PrimaryButton("选择信号与判定规则", OpenLocatorSignalConfiguration); _configureLocatorSignalsButton.Margin = new Thickness(10, 2, 6, 2); selector.Children.Add(_configureLocatorSignalsButton);
            _locatorSelectionSummary = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(65, 78, 96)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), FontWeight = FontWeights.SemiBold }; selector.Children.Add(_locatorSelectionSummary); Grid.SetRow(selector, 1); grid.Children.Add(selector);
            _productBox.IsEnabled = false; _productBox.ToolTip = "产品由当前工程确定，Locator地址不允许在STEP中手动切换";
            _locatorGrid = new DataGrid { ItemsSource = _locatorSignals, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, Visibility = Visibility.Collapsed };
            Border summaryCard = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)), BorderBrush = new SolidColorBrush(Color.FromRgb(216, 225, 237)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14), MinHeight = 88 };
            Grid summaryGrid = new Grid(); summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock summaryTitle = new TextBlock { Text = "已选信号", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 49, 67)), Margin = new Thickness(0, 0, 0, 9) }; summaryGrid.Children.Add(summaryTitle);
            _locatorSelectedSignalsPanel = new WrapPanel { Orientation = Orientation.Horizontal }; Grid.SetRow(_locatorSelectedSignalsPanel, 1); summaryGrid.Children.Add(_locatorSelectedSignalsPanel); summaryCard.Child = summaryGrid; Grid.SetRow(summaryCard, 2); grid.Children.Add(summaryCard);
            if (_productBox.Items.Count > 0) _productBox.SelectedIndex = 0; LoadLocatorSelection(); return grid;
        }

        private void OpenLocatorSignalConfiguration(object sender, RoutedEventArgs e)
        {
            string operation = Convert.ToString(_locatorOperation == null ? null : _locatorOperation.SelectedItem, CultureInfo.InvariantCulture); ProductLocatorTable table = _tableBox == null ? null : _tableBox.SelectedItem as ProductLocatorTable; if (table == null) { MessageBox.Show("请先选择Locator表。", "信号与判定配置", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            LocatorSignalConfigurationWindow dialog = new LocatorSignalConfigurationWindow(_locatorSignals, operation, table.DisplayName, _stepName.Text) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) { _stepName.Text = dialog.StepName; LocatorSignalRow selected = _locatorSignals.FirstOrDefault(value => value.Use) ?? _locatorSignals.FirstOrDefault(); if (_locatorGrid != null) _locatorGrid.SelectedItem = selected; UpdateLocatorSelectionSummary(); _status.Text = "已应用信号配置：" + _locatorSignals.Count(value => value.Use) + " 项"; _status.Foreground = Brushes.DarkGreen; }
        }
        private UIElement BuildDbcPanel()
        {
            Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.Children.Add(Heading("产品 DBC 通信（辅驱 / DCDC / 油泵 / 气泵 / PDU）"));
            EnsureDbcLoaded(); WrapPanel selector = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) }; _dbcMode = Combo(150); _dbcMode.ItemsSource = new[] { "发送一次", "开始周期发送", "停止周期发送", "读取DBC信号", "发送原始帧" }; _dbcMode.SelectedItem = Convert.ToString(_operation.SelectedItem, CultureInfo.InvariantCulture); _dbcMode.SelectionChanged += (s, e) => { _operation.SelectedItem = _dbcMode.SelectedItem; RefreshDbcMode(); UpdateDbcSelectionSummary(); }; _dbcMessage = Combo(280); _dbcMessage.ItemsSource = _dbcDatabase == null ? null : _dbcDatabase.Messages; _dbcMessage.ItemTemplate = TextItemTemplate("Name"); _dbcMessage.SelectionChanged += (s, e) => RefreshDbcSignals(); _dbcPeriod = Box(80); _dbcPeriod.Text = Convert.ToString(_loadedStep.Get("PeriodMs", 100), CultureInfo.InvariantCulture); _dbcTimeout = Box(80); _dbcTimeout.Text = Convert.ToString(_loadedStep.Get("TimeoutMs", 1000), CultureInfo.InvariantCulture); _dbcRawData = Box(320); _dbcRawData.FontFamily = new FontFamily("Consolas"); _dbcRawData.ToolTip = "可直接输入原始DATA；信号配置弹窗也会同步生成DATA。"; _dbcRawData.TextChanged += (s, e) => { if (!_loading) ApplyRawDataToDbcRows(false); }; selector.Children.Add(Label("操作")); selector.Children.Add(_dbcMode); selector.Children.Add(Label("报文")); selector.Children.Add(_dbcMessage); selector.Children.Add(Label("周期")); selector.Children.Add(_dbcPeriod); selector.Children.Add(Label("超时")); selector.Children.Add(_dbcTimeout); selector.Children.Add(Label("DATA")); selector.Children.Add(_dbcRawData);
            _configureDbcSignalsButton = PrimaryButton("配置信号", OpenDbcSignalConfiguration); _configureDbcSignalsButton.Margin = new Thickness(10, 2, 6, 2); selector.Children.Add(_configureDbcSignalsButton); _dbcSelectionSummary = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(65, 78, 96)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), FontWeight = FontWeights.SemiBold }; selector.Children.Add(_dbcSelectionSummary); Grid.SetRow(selector, 1); grid.Children.Add(selector);
            _dbcGrid = new DataGrid { ItemsSource = _dbcSignals, AutoGenerateColumns = false, CanUserAddRows = false, Visibility = Visibility.Collapsed }; _dbcReadSignal = Combo(260); _dbcReadSignal.Visibility = Visibility.Collapsed; _dbcReadSignal.SelectionChanged += (s, e) => { DbcMessageDefinition message = _dbcMessage == null ? null : _dbcMessage.SelectedItem as DbcMessageDefinition; DbcSignalDefinition signal = message == null ? null : message.Signals.FirstOrDefault(value => value.Name == Convert.ToString(_dbcReadSignal.SelectedItem)); if (signal != null) _unit.Text = signal.Unit; };
            Border summaryCard = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)), BorderBrush = new SolidColorBrush(Color.FromRgb(216, 225, 237)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14), MinHeight = 88 }; Grid summaryGrid = new Grid(); summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); summaryGrid.Children.Add(new TextBlock { Text = "已选DBC信号", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 49, 67)), Margin = new Thickness(0, 0, 0, 9) }); _dbcSelectedSignalsPanel = new WrapPanel(); Grid.SetRow(_dbcSelectedSignalsPanel, 1); summaryGrid.Children.Add(_dbcSelectedSignalsPanel); summaryCard.Child = summaryGrid; Grid.SetRow(summaryCard, 2); grid.Children.Add(summaryCard);
            if (_dbcMessage.Items.Count > 0) _dbcMessage.SelectedIndex = 0; LoadDbcSelection(); RefreshDbcMode(); UpdateDbcSelectionSummary(); return grid;
        }

        private void OpenDbcSignalConfiguration(object sender, RoutedEventArgs e)
        {
            DbcMessageDefinition message = _dbcMessage == null ? null : _dbcMessage.SelectedItem as DbcMessageDefinition; string mode = Convert.ToString(_dbcMode == null ? null : _dbcMode.SelectedItem, CultureInfo.InvariantCulture); if (message == null) { MessageBox.Show("请先选择DBC报文。", "DBC信号配置", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            DbcSignalConfigurationWindow dialog = new DbcSignalConfigurationWindow(_dbcSignals, mode, message.Name, _stepName.Text) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) { _stepName.Text = dialog.StepName; if (mode == "读取DBC信号") { DbcSignalEditRow selected = _dbcSignals.FirstOrDefault(value => value.Use); if (selected != null) _dbcReadSignal.SelectedItem = selected.Name; } UpdateRawDataFromDbcRows(); UpdateDbcSelectionSummary(); _status.Text = "已应用DBC信号配置"; _status.Foreground = Brushes.DarkGreen; }
        }

        private UIElement BuildResultPanel()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(8) }; WrapPanel first = new WrapPanel(); first.Children.Add(Label("结果处理")); first.Children.Add(_resultMode); _resultFields = new StackPanel { Orientation = Orientation.Horizontal }; first.Children.Add(_resultFields); panel.Children.Add(first); return panel;
        }

        private void RefreshResultFields()
        {
            if (_resultFields == null) return; _resultFields.Children.Clear(); string mode = Convert.ToString(_resultMode.SelectedItem, CultureInfo.InvariantCulture);
            if (mode == "数值范围判断") { _resultFields.Children.Add(Label("下限")); _resultFields.Children.Add(_lowLimit); _resultFields.Children.Add(Label("上限")); _resultFields.Children.Add(_highLimit); _resultFields.Children.Add(Label("比较")); _resultFields.Children.Add(_compare); _resultFields.Children.Add(Label("单位")); _resultFields.Children.Add(_unit); _resultFields.Children.Add(Label("同时保存变量")); _resultFields.Children.Add(_outputVariable); }
            else if (mode == "字符串判断") { _resultFields.Children.Add(Label("期望字符串")); _resultFields.Children.Add(_stringLimit); }
            else if (mode == "保存变量") { _resultFields.Children.Add(Label("变量名称")); _resultFields.Children.Add(_outputVariable); }
        }

        private void AddFieldEditor(Panel panel, ActionFieldSpec spec)
        {
            Grid row = new Grid { Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromRgb(249, 251, 254)) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); TextBlock label = Label(spec.Label); row.Children.Add(label); Control editor;
            object loaded = LoadedFieldValue(spec); string[] safeOptions = EffectiveFieldOptions(spec, loaded); if (safeOptions != null) { ComboBox combo = Combo(Math.Max(210, safeOptions.Max(value => (value ?? string.Empty).Length) * 8 + 24)); combo.ItemsSource = safeOptions; string loadedText = Convert.ToString(loaded, CultureInfo.InvariantCulture); string loadedCode = ActionFieldEditor.NormalizeOptionCode(loadedText); combo.SelectedItem = safeOptions.FirstOrDefault(value => string.Equals(value, loadedText, StringComparison.OrdinalIgnoreCase) || string.Equals(ActionFieldEditor.NormalizeOptionCode(value), loadedCode, StringComparison.OrdinalIgnoreCase) || value.StartsWith(loadedText + " ", StringComparison.OrdinalIgnoreCase) || value.StartsWith(loadedText + " -", StringComparison.OrdinalIgnoreCase) || value.StartsWith(loadedText + "-", StringComparison.OrdinalIgnoreCase)); if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0; editor = combo; } else if (spec.Type == "bool") editor = new CheckBox { IsChecked = Convert.ToBoolean(loaded, CultureInfo.InvariantCulture), Margin = new Thickness(8, 6, 8, 4) }; else { TextBox text = Box(210); text.Text = Convert.ToString(loaded, CultureInfo.InvariantCulture); editor = text; }
            Grid.SetColumn(editor, 1); row.Children.Add(editor); TextBlock unit = new TextBlock { Text = spec.Unit, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) }; Grid.SetColumn(unit, 2); row.Children.Add(unit); string binding; bool exposed = _loadedBindings.TryGetValue(spec.Name, out binding); CheckBox expose = new CheckBox { Content = "开放为模块参数", IsChecked = exposed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), ToolTip = "勾选后，章节引用该模块时可以单独修改此值" }; Grid.SetColumn(expose, 3); row.Children.Add(expose); TextBox parameterName = Box(180); parameterName.Text = exposed ? binding : spec.Label; parameterName.IsEnabled = exposed; parameterName.ToolTip = "模块参数显示名称，例如：目标电流、低压电压、保持时间"; expose.Checked += (s, e) => parameterName.IsEnabled = true; expose.Unchecked += (s, e) => parameterName.IsEnabled = false; Grid.SetColumn(parameterName, 4); row.Children.Add(parameterName); panel.Children.Add(row); _fieldEditors[spec.Name] = new ActionFieldEditor(spec, editor, expose, parameterName); if (IsRelaySetDo() && spec.Name == "Channels") { _relayChannelsBox = editor as TextBox; if (_relayChannelsBox != null) _relayChannelsBox.IsReadOnly = true; row.ColumnDefinitions[2].Width = new GridLength(105); Button chooseIo = Button("选择IO...", OpenRelayIoConfiguration); chooseIo.Margin = new Thickness(4); Grid.SetColumn(chooseIo, 2); row.Children.Add(chooseIo); } if (IsRelaySetDo() && spec.Name == "Values") { _relayValuesBox = editor as TextBox; if (_relayValuesBox != null) _relayValuesBox.IsReadOnly = true; AttachRelayStateOptions(editor as ComboBox); }
            if (IsDcdcLoadSetMode() && spec.Name == "Mode")
            {
                ComboBox modeCombo = editor as ComboBox;
                if (modeCombo != null)
                {
                    modeCombo.SelectionChanged += (s, e) => SyncDcdcModeStepName();
                    SyncDcdcModeStepName();
                }
            }
        }
        private bool IsDcdcLoadSetMode() { return _descriptor != null && string.Equals(_descriptor.Device, "DCDC_LOAD", StringComparison.OrdinalIgnoreCase) && string.Equals(_descriptor.Operation, "SetMode", StringComparison.OrdinalIgnoreCase); }
        private void SyncDcdcModeStepName()
        {
            if (!IsDcdcLoadSetMode() || _stepName == null) return;
            ActionFieldEditor editor;
            if (!_fieldEditors.TryGetValue("Mode", out editor)) return;
            string selected = editor.Control is ComboBox ? Convert.ToString(((ComboBox)editor.Control).SelectedItem, CultureInfo.InvariantCulture) : string.Empty;
            string family = DcdcModeFamily(ActionFieldEditor.NormalizeOptionCode(selected));
            if (string.IsNullOrWhiteSpace(family)) return;
            string current = _stepName.Text ?? string.Empty;
            System.Text.RegularExpressions.Match prefix = System.Text.RegularExpressions.Regex.Match(current, @"^(\d+[-\._]?)");
            string head = prefix.Success ? prefix.Groups[1].Value : string.Empty;
            if (System.Text.RegularExpressions.Regex.IsMatch(current, @"(CC|CV|CR|CP)\s*模式") || current.IndexOf("电子负载", StringComparison.Ordinal) >= 0 || current.IndexOf("设置模式", StringComparison.Ordinal) >= 0 || string.IsNullOrWhiteSpace(current))
                _stepName.Text = head + "电子负载设置" + family + "模式";
            else
                _stepName.Text = System.Text.RegularExpressions.Regex.Replace(current, @"\b(CC|CV|CR|CP)\b", family);
        }
        internal static string DcdcModeFamily(string modeCode)
        {
            string code = ActionFieldEditor.ResolveDcdcLoadModeCode(modeCode) ?? ActionFieldEditor.NormalizeOptionCode(modeCode) ?? string.Empty;
            if (code.Length < 2) return "CC";
            switch (char.ToUpperInvariant(code[1]))
            {
                case 'V': return "CV";
                case 'R': return "CR";
                case 'P': return "CP";
                default: return "CC";
            }
        }
        internal static string DcdcSetpointOperation(string family)
        {
            switch ((family ?? string.Empty).ToUpperInvariant())
            {
                case "CV": return "SetVoltage";
                case "CR": return "SetResistance";
                case "CP": return "SetPower";
                default: return "SetCurrent";
            }
        }
        internal static string DcdcSetpointField(string family)
        {
            switch ((family ?? string.Empty).ToUpperInvariant())
            {
                case "CV": return "Voltage";
                case "CR": return "Resistance";
                case "CP": return "Power";
                default: return "Current";
            }
        }
        internal static string DcdcSetpointUnit(string family)
        {
            switch ((family ?? string.Empty).ToUpperInvariant())
            {
                case "CV": return "V";
                case "CR": return "Ω";
                case "CP": return "W";
                default: return "A";
            }
        }
        internal static string DcdcSetpointStepName(string currentName, string family)
        {
            string unit = DcdcSetpointUnit(family);
            System.Text.RegularExpressions.Match prefix = System.Text.RegularExpressions.Regex.Match(currentName ?? string.Empty, @"^(\d+[-\._]?)");
            string head = prefix.Success ? prefix.Groups[1].Value : string.Empty;
            if ((currentName ?? string.Empty).IndexOf("电子负载", StringComparison.Ordinal) >= 0 || System.Text.RegularExpressions.Regex.IsMatch(currentName ?? string.Empty, @"设置0?[AVWΩohm]|设置恒"))
                return head + "电子负载设置0" + unit;
            return head + "电子负载设置0" + unit;
        }
        private bool IsRelaySetDo() { return _descriptor != null && (_descriptor.Device == "RELAY_FCT" || _descriptor.Device == "RELAY_HVMUX") && _descriptor.Operation == "SetDO"; }
        private string[] EffectiveFieldOptions(ActionFieldSpec spec, object loaded)
        {
            string[] options = spec.Options;
            if (IsRelaySetDo() && (spec.Name == "Channels" || spec.Name == "Values")) options = null;
            else if (IsRelaySetDo() && spec.Name == "Slave") options = Enumerable.Range(1, 16).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray();
            string current = Convert.ToString(loaded, CultureInfo.InvariantCulture);
            string currentCode = ActionFieldEditor.NormalizeOptionCode(current);
            if (options != null && !string.IsNullOrWhiteSpace(current) &&
                !options.Any(value => string.Equals(value, current, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ActionFieldEditor.NormalizeOptionCode(value), currentCode, StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith(current + " ", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith(current + " -", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith(current + "-", StringComparison.OrdinalIgnoreCase)))
                options = options.Concat(new[] { current }).ToArray();
            return options;
        }
        private void OpenRelayIoConfiguration(object sender, RoutedEventArgs e) { if (_descriptor == null || _relayChannelsBox == null || _relayValuesBox == null) return; RelayIoConfigurationWindow dialog = new RelayIoConfigurationWindow(_descriptor.Device, _relayChannelsBox.Text, _relayValuesBox.Text) { Owner = Window.GetWindow(this) }; if (dialog.ShowDialog() == true) { _relayChannelsBox.Text = dialog.Channels; _relayValuesBox.Text = dialog.Values; _status.Text = "已选择 " + ChannelCount(dialog.Channels).ToString(CultureInfo.InvariantCulture) + " 个IO"; _status.Foreground = Brushes.DarkGreen; } }
        private void AttachRelayStateOptions(ComboBox values) { ActionFieldEditor channelsEditor; ComboBox channels = _fieldEditors.TryGetValue("Channels", out channelsEditor) ? channelsEditor.Control as ComboBox : null; if (values == null || channels == null) return; Action refresh = () => { string current = Convert.ToString(values.SelectedItem, CultureInfo.InvariantCulture); string[] options = BinaryStateOptions(ChannelCount(Convert.ToString(channels.SelectedItem, CultureInfo.InvariantCulture))); values.ItemsSource = options; values.SelectedItem = options.Contains(current) ? current : options[0]; }; channels.SelectionChanged += (s, e) => refresh(); refresh(); }
        private static int ChannelCount(string text) { return Math.Max(1, (text ?? string.Empty).Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Length); }
        private static string[] BinaryStateOptions(int count) { count = Math.Max(1, Math.Min(4, count)); List<string> result = new List<string>(); for (int mask = 0; mask < (1 << count); mask++) result.Add(string.Join(",", Enumerable.Range(0, count).Select(bit => ((mask >> (count - bit - 1)) & 1).ToString(CultureInfo.InvariantCulture)))); return result.ToArray(); }
        private static string[] RelayFctChannelOptions() { return new[] { "OUT6", "OUT7", "OUT9", "OUT10", "OUT9,OUT10", "OUT11", "OUT12", "OUT13", "OUT14", "OUT15", "OUT16", "OUT17", "OUT18", "OUT19", "OUT20", "OUT21", "OUT22", "OUT23", "OUT24", "OUT25", "OUT26", "OUT27", "OUT28", "OUT29", "OUT30", "OUT31", "OUT32", "OUT33", "OUT34", "OUT35", "OUT36", "OUT37", "OUT38", "OUT39", "OUT40", "OUT41", "OUT42", "OUT43", "OUT45" }; }
        internal static string[] FctMuxFunctionOptions() { return new[] { "1 - HVDC测量备用（J1）", "2 - 高压测量未用（J2）", "3 - 高压测量未用（J3）", "4 - 高压测量未用（J4）", "5 - CAN0 H-L电阻（J19）", "6 - CAN1 H-L电阻（J20）", "7 - CAN2 H-L电阻（J21）", "8 - CAN0 H-GND电压（J28）", "9 - CAN0 L-GND电压（J29）", "10 - CAN1 H-GND电压（J30）", "11 - CAN1 L-GND电压（J31）", "12 - KL30/KL31电压（J35）", "13 - OBC KL30/KL31电压-未接（J36）" }; }
        // Display uses instrument panel labels CC/CV/CR/CP; Value() maps back to driver codes Ccl/Ccm/...
        internal static string[] DcdcLoadModeOptions()
        {
            return new[]
            {
                "CC 低量程 - 恒流：小电流精细加载",
                "CC 中量程 - 恒流：常用恒流加载",
                "CC 高量程 - 恒流：大电流加载",
                "CV 低量程 - 恒压：低压精细控制",
                "CV 中量程 - 恒压：常用恒压加载",
                "CV 高量程 - 恒压：高压加载",
                "CR 低量程 - 恒阻：模拟小电阻负载",
                "CR 中量程 - 恒阻：模拟中等电阻",
                "CR 高量程 - 恒阻：模拟大电阻",
                "CP 低量程 - 恒功率：小功率加载",
                "CP 中量程 - 恒功率：常用恒功率",
                "CP 高量程 - 恒功率：大功率加载"
            };
        }
        private object LoadedFieldValue(ActionFieldSpec spec) { if (_loadedStep == null) return spec.DefaultValue; object value = _loadedStep.Get(spec.Name); if (value != null) return value; string alias = spec.Name == "Voltage" ? "SourceVoltage" : spec.Name == "Current" ? "SourceCurrent" : spec.Name; return _loadedStep.Get(alias, spec.DefaultValue); }

        private SequenceStepDefinition BuildDescriptorStep(bool logic)
        {
            if (_descriptor == null) return SequenceEditing.Clone(_loadedStep);
            bool mainTest = string.Equals(_descriptor.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase); Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", BuildAutomaticName() }, { "RunMode", "Normal" }, { "FunctionName", mainTest ? _descriptor.FunctionName : logic ? "FCT_ExecuteLogic" : "FCT_ExecuteAction" }, { "RecordingLog", true }, { "ResultMode", _descriptor.ReturnsValue ? "Information" : "Action" } }; if (!string.IsNullOrWhiteSpace(_descriptor.Operation)) values["Operation"] = _descriptor.Operation; if (!logic && !string.IsNullOrWhiteSpace(_descriptor.Device)) values["Device"] = _descriptor.Device; if (string.Equals(_descriptor.BindingMode, "Plugin", StringComparison.OrdinalIgnoreCase)) { values["PluginAssembly"] = _descriptor.PluginAssembly; values["PluginType"] = _descriptor.PluginType; }
            foreach (KeyValuePair<string, ActionFieldEditor> pair in _fieldEditors) values[pair.Key] = pair.Value.Value(); return new SequenceStepDefinition(values);
        }

        private SequenceStepDefinition BuildLocatorStep()
        {
            if (_locatorOperation == null || _tableBox == null || _locatorGrid == null) throw new InvalidOperationException("Locator配置界面尚未准备完成，请重新选择“FT/Locator内存”和操作类型。"); _locatorGrid.CommitEdit(DataGridEditingUnit.Cell, true); _locatorGrid.CommitEdit(DataGridEditingUnit.Row, true); ProductLocatorTable table = _tableBox.SelectedItem as ProductLocatorTable; string operation = Convert.ToString(_locatorOperation.SelectedItem, CultureInfo.InvariantCulture); if (string.IsNullOrWhiteSpace(operation)) throw new InvalidOperationException("请选择Locator读写操作。"); if (table == null) throw new InvalidOperationException("请选择Locator表。");
            if (operation == "读取") { SequenceStepDefinition tableRead = ProductSignalStepFactory.CreateTableRead(BuildAutomaticName(), table); List<object> checks = _locatorSignals.Where(value => value.Use).Select(value => value.ToReadCheck()).Cast<object>().ToList(); if (checks.Count == 0) throw new InvalidOperationException("请至少勾选一个要读取的信号。"); tableRead.Properties["SignalChecksJson"] = JsonConvert.SerializeObject(checks); return WithLocatorProduct(tableRead); }
            if (operation == "写入") { ProductLocatorDefinition selectedProduct = _productBox == null ? null : _productBox.SelectedItem as ProductLocatorDefinition; if (selectedProduct != null && (selectedProduct.Product == "C96" || selectedProduct.Product == "C92") && table.AddressOffset == 0x80) return WithLocatorProduct(BuildHardwareTm2MotorControlStep()); List<KeyValuePair<ProductLocatorSignal, string>> changes = _locatorSignals.Where(value => value.Use).Select(value => new KeyValuePair<ProductLocatorSignal, string>(value.Signal, value.ValueText)).ToList(); if (changes.Count == 0) throw new InvalidOperationException("请至少勾选一个要写入的信号。"); SequenceStepDefinition tableWrite = ProductSignalStepFactory.CreateTableWrite(BuildAutomaticName(), table, changes); tableWrite.Properties["VerifyAfterWrite"] = _verifyAfterWrite == null || _verifyAfterWrite.IsChecked == true; return WithLocatorProduct(tableWrite); }
            throw new InvalidOperationException("Locator操作只支持读取或写入。");
        }

        private SequenceStepDefinition WithLocatorProduct(SequenceStepDefinition step) { ProductLocatorDefinition product = _productBox == null ? null : _productBox.SelectedItem as ProductLocatorDefinition; if (step != null && product != null) step.Properties["Product"] = product.Product; return step; }
        private SequenceStepDefinition BuildHardwareTm2MotorControlStep()
        {
            string targetText = _locatorSignals.FirstOrDefault(value => value.Offset == 0x04)?.ValueText; if (string.IsNullOrWhiteSpace(targetText)) targetText = LoadedChangeValue(0x04, "100*1.414"); string targetPeak = NumericFormula.Evaluate(targetText).ToString("R", CultureInfo.InvariantCulture); string gate = LoadedChangeValue(0x1A, "1"); JArray changes = new JArray
            {
                Change("Iqs_Start",0,4,"float32","0",false), Change("Iqs_End",4,4,"float32",targetPeak,false), Change("Iqs_Step",8,4,"float32","20",false), Change("Hold_Time_S",12,4,"float32","10",false), Change("Output_Frequency",16,4,"float32","60",false), Change("Mode",20,1,"uint8","4",false), Change("Unused",21,1,"int8","0",false), Change("Ramp_Time_MS",22,2,"uint16","50",false), Change("Base_Frequency",24,2,"uint16","10000",false), Change("Motor_Gate_Enable",26,1,"uint8",gate,false), Change("New Data Flag",27,1,"uint8","255",true), Change("Reset_Motor_Faults",28,1,"uint8","0",false), Change("Speed_Control_Enable",29,1,"uint8","0",false), Change("Speed_Setpoint",30,4,"float32","0",false), Change("Voltage_Control_Enable",34,1,"uint8","0",false), Change("Voltage_Setpoint",35,4,"float32","0",false)
            };
            return new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", BuildAutomaticName() }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true }, { "Operation", "Write" }, { "ResultMode", "Action" }, { "AddrOffset", 0x80 }, { "TableLength", 39 }, { "ChangesJson", changes.ToString(Formatting.None) }, { "VerifyAfterWrite", _verifyAfterWrite == null || _verifyAfterWrite.IsChecked == true } });
        }
        private string LoadedChangeValue(int offset, string fallback) { try { JObject item = JArray.Parse(Convert.ToString(_loadedStep.Get("ChangesJson", "[]"), CultureInfo.InvariantCulture)).OfType<JObject>().FirstOrDefault(value => (int?)value["Offset"] == offset); string text = item == null ? string.Empty : Convert.ToString(item["Value"], CultureInfo.InvariantCulture); return string.IsNullOrWhiteSpace(text) ? fallback : text; } catch { return fallback; } }
        private static JObject Change(string name, int offset, int size, string type, string value, bool final) { return new JObject { ["Name"] = name, ["Offset"] = offset, ["DataSize"] = size, ["DataType"] = type, ["Endian"] = "Little", ["Value"] = value, ["WriteLast"] = final, ["WriteFinal"] = final }; }

        private SequenceStepDefinition BuildDbcStep()
        {
            if (_dbcMode == null || _dbcMessage == null) throw new InvalidOperationException("DBC配置界面尚未准备完成，请重新选择产品DBC通信。");
            string mode = Convert.ToString(_dbcMode.SelectedItem, CultureInfo.InvariantCulture), operation = mode == "发送一次" ? "SendDbcSignals" : mode == "开始周期发送" ? "StartPeriodicDbc" : mode == "停止周期发送" ? "StopPeriodicDbc" : mode == "读取DBC信号" ? "ReadDbcSignal" : "SendRaw"; DbcMessageDefinition message = _dbcMessage.SelectedItem as DbcMessageDefinition;
            string configuredDbc = _getDbcPath == null ? string.Empty : _getDbcPath(); if (string.IsNullOrWhiteSpace(configuredDbc)) configuredDbc = "Config\\C95C96Auxiliary.dbc"; Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", BuildAutomaticName() }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", "AUXCAN" }, { "Operation", operation }, { "ResultMode", operation == "ReadDbcSignal" ? "Information" : "Action" }, { "DbcPath", configuredDbc }, { "DeviceType", 52 }, { "Channel", 0 }, { "BaudRate", 500000 }, { "IP", "192.166.6.10" } };
            if (message != null) { values["MessageName"] = message.Name; values["CanId"] = message.Id.ToString("X", CultureInfo.InvariantCulture); }
            if (operation == "StartPeriodicDbc") values["PeriodMs"] = Parse(_dbcPeriod.Text, 100); if (operation == "ReadDbcSignal") { values["SignalName"] = Convert.ToString(_dbcReadSignal.SelectedItem, CultureInfo.InvariantCulture); values["TimeoutMs"] = Parse(_dbcTimeout.Text, 1000); }
            if (operation == "SendDbcSignals" || operation == "StartPeriodicDbc") { if (_dbcDatabase == null || message == null) throw new InvalidOperationException("请选择有效的DBC报文。"); byte[] directData = ParseDbcRawData(true); Dictionary<string, double> signals = _dbcSignals.Where(row => row.Use).ToDictionary(row => row.Name, row => Parse(row.ValueText, 0), StringComparer.Ordinal); CanFrame encoded; if (signals.Count > 0) encoded = _dbcDatabase.Encode(message.Name, signals); else if (directData != null) { encoded = new CanFrame(message.Id, directData); DbcDecodedFrame decoded = _dbcDatabase.Decode(encoded); signals = decoded == null ? new Dictionary<string, double>(StringComparer.Ordinal) : decoded.Signals.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal); } else throw new InvalidOperationException("请输入原始DATA，或者通过“配置信号”至少选择一个DBC信号。"); values["SignalsJson"] = JsonConvert.SerializeObject(signals); values["PeriodicKey"] = message.Name; values["CanId"] = encoded.Id.ToString("X", CultureInfo.InvariantCulture); values["DataHex"] = HexDataParser.Format(encoded.Data); }
            if (operation == "StopPeriodicDbc") values["PeriodicKey"] = message == null ? "AUX" : message.Name; if (operation == "SendRaw") values["DataHex"] = "00 00 00 00 00 00 00 00"; return new SequenceStepDefinition(values);
        }

        private void ApplyResult(SequenceStepDefinition step)
        {
            if (step.FunctionName == "FCT_CANCalculatedResults") return;
            if (step.FunctionName == "FCT_CANTable" && step.Properties.ContainsKey("SignalChecksJson")) { step.Properties["ResultMode"] = "Information"; foreach (string key in new[] { "LowLimit", "HighLimit", "Comtype", "Unit", "Limit" }) step.Properties.Remove(key); return; }
            if (!IsReadAction()) { step.Properties["ResultMode"] = "Action"; return; } string mode = Convert.ToString(_resultMode.SelectedItem, CultureInfo.InvariantCulture); string resultMode = mode == "数值范围判断" ? "NumericLimit" : mode == "字符串判断" ? "StringLimit" : mode == "PASS/FAIL" ? "PassFail" : mode == "保存变量" ? "Variable" : mode == "只记录信息" ? "Information" : "Action"; step.Properties["ResultMode"] = resultMode;
            if (resultMode == "NumericLimit") { step.Properties["LowLimit"] = Parse(_lowLimit.Text, double.MinValue); step.Properties["HighLimit"] = Parse(_highLimit.Text, double.MaxValue); step.Properties["Comtype"] = Convert.ToString(_compare.SelectedItem, CultureInfo.InvariantCulture) ?? "GELE"; step.Properties["Unit"] = _unit.Text; }
            if (resultMode == "StringLimit") step.Properties["Limit"] = _stringLimit.Text; if (!string.IsNullOrWhiteSpace(_outputVariable.Text)) step.Properties["OutputVariable"] = _outputVariable.Text.Trim();
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            SequenceStepDefinition step = null;
            DateTime started = DateTime.Now;
            string diagnosticPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "MainTest_CAN_" + started.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
            long diagnosticOffset = File.Exists(diagnosticPath) ? new FileInfo(diagnosticPath).Length : 0;
            try
            {
                step = BuildStep();
                _status.Text = "正在执行：" + step.StepName;
                _status.Foreground = Brushes.DarkOrange;
                string result = await _execute(step);
                LegacyStepExecutionResult platformResult = _getLastPlatformResult == null ? null : _getLastPlatformResult();
                string displayResult = FormatPlatformMeasurements(platformResult, string.IsNullOrWhiteSpace(result) ? "执行完成（平台未返回文本）" : result);
                _status.Text = displayResult;
                _debugDetails.Text = "本次调试结果：" + displayResult;
                _status.Foreground = Brushes.DarkGreen;
                RecordExecution(new ActionHistoryRow(step, displayResult, true, started, DateTime.Now, ReadDiagnosticDelta(diagnosticPath, diagnosticOffset), platformResult));
                _log("动作面板试运行完成：" + step.StepName);
            }
            catch (Exception ex)
            {
                _status.Text = "试运行失败：" + ex.Message;
                _debugDetails.Text = "本次调试结果：FAILED · " + ex.Message;
                _status.Foreground = Brushes.DarkRed;
                SequenceStepDefinition failedStep = step ?? _loadedStep ?? CreateDraft();
                LegacyStepExecutionResult platformResult = _getLastPlatformResult == null ? null : _getLastPlatformResult();
                RecordExecution(new ActionHistoryRow(failedStep, ex.Message, false, started, DateTime.Now, ReadDiagnosticDelta(diagnosticPath, diagnosticOffset), platformResult));
                MessageBox.Show("动作试运行失败：\n" + ex.Message, "动作配置", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public void ExecuteCurrent() { Execute_Click(this, new RoutedEventArgs()); }
        private async Task<string> ExecuteSpecializedAsync(string stepName)
        {
            if (!string.IsNullOrWhiteSpace(stepName)) _stepName.Text = stepName.Trim(); SequenceStepDefinition step = BuildStep(); DateTime started = DateTime.Now; string result = await _execute(step); LegacyStepExecutionResult platform = _getLastPlatformResult == null ? null : _getLastPlatformResult(); string display = FormatPlatformMeasurements(platform, string.IsNullOrWhiteSpace(result) ? "执行完成（平台未返回文本）" : result); _debugDetails.Text = "本次调试结果：" + display; RecordExecution(new ActionHistoryRow(step, display, true, started, DateTime.Now, string.Empty, platform)); _log("动作面板试运行完成：" + step.StepName); return display;
        }

        internal static string FormatPlatformMeasurements(LegacyStepExecutionResult platform, string fallback)
        {
            if (platform == null || platform.Results == null) return fallback ?? string.Empty;
            // Hierarchy「测试值」列只显示实测值，不拼接步骤名/描述文字。
            string[] values = platform.Results
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Value))
                .Select(value => (value.Value ?? string.Empty).Trim())
                .Where(value => value.Length > 0)
                .ToArray();
            return values.Length == 0 ? fallback ?? string.Empty : string.Join("；", values);
        }

        private void RecordExecution(ActionHistoryRow record)
        {
            Action<ActionHistoryRow> handler = ExecutionRecorded;
            if (handler != null) handler(record);
        }

        private static string ReadDiagnosticDelta(string path, long offset)
        {
            try
            {
                if (!File.Exists(path)) return string.Empty;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (offset > 0 && offset <= stream.Length) stream.Position = offset;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd().Trim();
                }
            }
            catch (Exception ex) { return "读取诊断日志失败：" + ex.Message; }
        }
        private void Complete_Click(object sender, RoutedEventArgs e) { try { if (IsDcdcLoadSetMode()) SyncDcdcModeStepName(); SequenceStepDefinition step = BuildStep(); if (!MainTestMethodCatalog.Contains(step.FunctionName)) throw new MissingMethodException("MainTest中不存在函数：" + step.FunctionName); IDictionary<string, string> bindings = BuildBindings(); _save(step, bindings); LoadStep(step, bindings); _status.Text = "配置完成 · " + MainTestMethodCatalog.BindingSummary(step); _status.Foreground = Brushes.DarkGreen; } catch (Exception ex) { _status.Text = "配置不完整：" + ex.Message; _status.Foreground = Brushes.DarkRed; MessageBox.Show(ex.Message, "完成配置", MessageBoxButton.OK, MessageBoxImage.Information); } }

        private void SelectTargetAndOperation(SequenceStepDefinition step)
        {
            string source = DetectSource(step); if (source == "产品内部通信") { bool locator = step.FunctionName == "FCT_CANSignal" || step.FunctionName == "FCT_CANTable" || string.Equals(step.FunctionName, "CAN_ReadSignalValue", StringComparison.OrdinalIgnoreCase); _target.SelectedItem = locator ? "FT/Locator内存" : "产品基础命令"; RefreshOperations(); if (locator) _operation.SelectedItem = DetectLocatorOperation(step); else { ActionDescriptor descriptor = ActionCatalog.Find(source, Convert.ToString(step.Get("Device"), CultureInfo.InvariantCulture), Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture), step.FunctionName); if (descriptor != null) _operation.SelectedItem = _operation.Items.Cast<object>().OfType<ActionDescriptor>().FirstOrDefault(value => value.Operation == descriptor.Operation && value.Device == descriptor.Device); } }
            else if (source == "产品DBC通信") { _target.SelectedIndex = 0; RefreshOperations(); _operation.SelectedItem = DetectDbcMode(step); }
            else { string device = Convert.ToString(step.Get("Device"), CultureInfo.InvariantCulture); ActionDescriptor descriptor = ActionCatalog.Find(source, device, Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture), step.FunctionName); if (descriptor != null) { _target.SelectedItem = source == "仪器" ? ActionCatalog.PickerTarget(descriptor) : descriptor.Target; RefreshOperations(); _operation.SelectedItem = _operation.Items.Cast<object>().OfType<ActionDescriptor>().FirstOrDefault(value => value.Operation == descriptor.Operation && value.Device == descriptor.Device); } }
        }

        private void LoadLocatorSelection() { if (_productBox == null) return; string product = _getProduct == null ? string.Empty : _getProduct(); if (string.IsNullOrWhiteSpace(product)) product = Convert.ToString(_loadedStep.Get("Product"), CultureInfo.InvariantCulture); ProductLocatorDefinition found = _locatorRepository.Products.FirstOrDefault(value => value.Product == product) ?? _locatorRepository.Products.FirstOrDefault(value => value.Product == "C95"); _productBox.SelectedItem = found ?? _locatorRepository.Products.FirstOrDefault(); RefreshLocatorTables(); }
        private void RefreshLocatorTables() { if (_productBox == null || _tableBox == null) return; ProductLocatorDefinition product = _productBox.SelectedItem as ProductLocatorDefinition; bool write = Convert.ToString(_locatorOperation.SelectedItem, CultureInfo.InvariantCulture).Contains("写入"); if (_verifyAfterWrite != null) _verifyAfterWrite.Visibility = write ? Visibility.Visible : Visibility.Collapsed; _tableBox.ItemsSource = product == null ? null : product.Tables.Where(table => !write || table.CanWrite).ToList(); if (_tableBox.Items.Count > 0) { int address = _loadedStep.GetInt("AddrOffset", -1); _tableBox.SelectedItem = _tableBox.Items.Cast<ProductLocatorTable>().FirstOrDefault(table => table.AddressOffset == address) ?? _tableBox.Items[0]; } RefreshLocatorSignals(); }
        private void RefreshLocatorSignals()
        {
            _locatorSignals.Clear(); ProductLocatorTable table = _tableBox == null ? null : _tableBox.SelectedItem as ProductLocatorTable; if (table == null) return; foreach (ProductLocatorSignal signal in table.Signals.OrderBy(value => value.Offset).ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)) { LocatorSignalRow row = new LocatorSignalRow(signal); if (string.IsNullOrWhiteSpace(row.UnitText) && row.Name.IndexOf("Current", StringComparison.OrdinalIgnoreCase) >= 0) row.UnitText = "A"; _locatorSignals.Add(row); }
            try { JArray changes = JArray.Parse(Convert.ToString(_loadedStep.Get("ChangesJson", "[]"), CultureInfo.InvariantCulture)); foreach (JObject change in changes.OfType<JObject>()) { int offset = (int?)change["Offset"] ?? -1; LocatorSignalRow row = _locatorSignals.FirstOrDefault(value => value.Signal.Offset == offset); if (row != null) { row.Use = true; row.ValueText = Convert.ToString(change["Value"], CultureInfo.InvariantCulture); } } } catch { }
            try { JArray checks = JArray.Parse(Convert.ToString(_loadedStep.Get("SignalChecksJson", "[]"), CultureInfo.InvariantCulture)); foreach (JObject check in checks.OfType<JObject>()) { int offset = (int?)check["Offset"] ?? -1; string name = Convert.ToString(check["Name"], CultureInfo.InvariantCulture); LocatorSignalRow row = _locatorSignals.FirstOrDefault(value => value.Signal.Offset == offset && string.Equals(value.Name, name, StringComparison.Ordinal)) ?? _locatorSignals.FirstOrDefault(value => value.Signal.Offset == offset); if (row != null) row.LoadReadCheck(check); } } catch { }
            ApplyMotorControlWriteDefaults(table);
            int selectedOffset = _loadedStep.GetInt("TableIndex", -1); _locatorGrid.SelectedItem = _locatorSignals.FirstOrDefault(row => row.Signal.Offset == selectedOffset) ?? _locatorSignals.FirstOrDefault(); LocatorSignalRow selected = _locatorGrid.SelectedItem as LocatorSignalRow; if (selected != null) { _unit.Text = selected.Unit; if (_loadedStep.FunctionName == "FCT_CANSignal") { selected.Use = true; bool write = string.Equals(Convert.ToString(_loadedStep.Get("Operation")), "Write", StringComparison.OrdinalIgnoreCase); if (write) selected.ValueText = Convert.ToString(_loadedStep.Get("ValueText"), CultureInfo.InvariantCulture); else { string mode = InferResultMode(_loadedStep); selected.ResultMode = mode == "NumericLimit" ? "数值LIMIT" : mode == "StringLimit" ? "字符串匹配" : "只记录信息"; selected.LowLimitText = Convert.ToString(_loadedStep.Get("LowLimit", 0), CultureInfo.InvariantCulture); selected.HighLimitText = Convert.ToString(_loadedStep.Get("HighLimit", 0), CultureInfo.InvariantCulture); selected.CompareText = Convert.ToString(_loadedStep.Get("Comtype", "GELE"), CultureInfo.InvariantCulture); selected.UnitText = Convert.ToString(_loadedStep.Get("Unit", selected.Unit), CultureInfo.InvariantCulture); selected.ExpectedText = Convert.ToString(_loadedStep.Get("Limit", string.Empty), CultureInfo.InvariantCulture); } } } UpdateLocatorSelectionSummary();
        }
        private void ApplyMotorControlWriteDefaults(ProductLocatorTable table)
        {
            string operation = Convert.ToString(_locatorOperation == null ? null : _locatorOperation.SelectedItem, CultureInfo.InvariantCulture); ProductLocatorDefinition product = _productBox == null ? null : _productBox.SelectedItem as ProductLocatorDefinition; if (table == null || product == null || operation != "写入" || (product.Product != "C96" && product.Product != "C92") || (table.AddressOffset != 0x68 && table.AddressOffset != 0x80)) return; LocatorSignalRow targetCurrentRow = _locatorSignals.FirstOrDefault(value => value.Offset == 0x04); string targetCurrent = targetCurrentRow != null && targetCurrentRow.Use && !string.IsNullOrWhiteSpace(targetCurrentRow.ValueText) ? targetCurrentRow.ValueText : "100*1.414";
            Dictionary<int, string> defaults = table.AddressOffset == 0x68
                ? new Dictionary<int, string> { { 0x00, "0" }, { 0x04, "100*1.414" }, { 0x08, "20" }, { 0x0C, "10" }, { 0x10, "60" }, { 0x14, "4" }, { 0x15, "0" }, { 0x16, "50" }, { 0x18, "10000" }, { 0x1A, "1" }, { 0x1B, "255" }, { 0x1C, "0" }, { 0x1D, "0" }, { 0x1E, "0" }, { 0x22, "0" }, { 0x23, "0" } }
                : new Dictionary<int, string> { { 0x00, "0" }, { 0x04, "100*1.414" }, { 0x08, "20" }, { 0x0C, "50" }, { 0x0E, "10" }, { 0x10, "60" }, { 0x14, "4" }, { 0x16, "10000" }, { 0x18, "1" }, { 0x19, "255" }, { 0x1A, "0" }, { 0x1E, "0" } };
            defaults[0x04] = targetCurrent; foreach (LocatorSignalRow row in _locatorSignals) { string value; if (!defaults.TryGetValue(row.Offset, out value)) continue; row.Use = true; row.ValueText = value; } UpdateLocatorSelectionSummary(); _status.Text = (table.AddressOffset == 0x68 ? "TM1" : "TM2") + "三相出流固定字段已校正；只需修改Iqs_End中的RMS电流×1.414。"; _status.Foreground = Brushes.DarkGreen;
        }

        private void SetAllLocatorSignals(bool selected)
        {
            foreach (LocatorSignalRow row in _locatorSignals) row.Use = selected;
            UpdateLocatorSelectionSummary();
        }

        private void InvertLocatorSignals()
        {
            foreach (LocatorSignalRow row in _locatorSignals) row.Use = !row.Use;
            UpdateLocatorSelectionSummary();
        }

        private void UpdateLocatorSelectionSummary()
        {
            int selectedCount = _locatorSignals.Count(row => row.Use), judgmentCount = _locatorSignals.Count(row => row.Use && row.NeedsJudgment); if (_locatorSelectionSummary != null) _locatorSelectionSummary.Text = "已选 " + selectedCount + " / " + _locatorSignals.Count + "  ·  " + judgmentCount + " 项判断  ·  " + Math.Max(0, selectedCount - judgmentCount) + " 项只记录";
            if (_locatorSelectedSignalsPanel == null) return; _locatorSelectedSignalsPanel.Children.Clear(); List<LocatorSignalRow> selected = _locatorSignals.Where(row => row.Use).ToList();
            if (selected.Count == 0) { _locatorSelectedSignalsPanel.Children.Add(new TextBlock { Text = "尚未选择信号，点击“选择信号与判定规则”开始配置。", Foreground = Brushes.DimGray, Margin = new Thickness(2, 4, 2, 4) }); return; }
            foreach (LocatorSignalRow row in selected.Take(6)) { string detail = row.NeedsJudgment ? row.ResultMode + (row.ResultMode == "数值LIMIT" ? " " + row.LowLimitText + "～" + row.HighLimitText + (string.IsNullOrWhiteSpace(row.UnitText) ? string.Empty : " " + row.UnitText) : string.Empty) : "只记录"; Border chip = new Border { Background = Brushes.White, BorderBrush = row.NeedsJudgment ? new SolidColorBrush(Color.FromRgb(80, 145, 230)) : new SolidColorBrush(Color.FromRgb(211, 221, 234)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6), Child = new TextBlock { Text = row.Name + "\n" + row.OffsetText + "  ·  " + detail, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(47, 63, 86)) } }; _locatorSelectedSignalsPanel.Children.Add(chip); }
            if (selected.Count > 6) _locatorSelectedSignalsPanel.Children.Add(new TextBlock { Text = "+ " + (selected.Count - 6) + " 项", Foreground = new SolidColorBrush(Color.FromRgb(24, 112, 224)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 10, 4, 4) });
        }

        private void CopyAllLocatorRows()
        {
            if (_locatorSignals.Count == 0) { MessageBox.Show("当前表没有可复制的信号。", "复制表格", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            StringBuilder text = new StringBuilder(); text.AppendLine("使用\t信号\tOffset\t类型\t结果类型\t下限\t上限\t比较\t单位\t字符串期望值\t写入值\t说明");
            foreach (LocatorSignalRow row in _locatorSignals) text.AppendLine(string.Join("\t", new[] { row.Use ? "1" : "0", row.Name, row.OffsetText, row.DataType, row.ResultMode, row.LowLimitText, row.HighLimitText, row.CompareText, row.UnitText, row.ExpectedText, row.ValueText, (row.Comment ?? string.Empty).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ") }));
            Clipboard.SetText(text.ToString()); _status.Text = "已复制当前表全部 " + _locatorSignals.Count + " 个信号（含表头）"; _status.Foreground = Brushes.DarkGreen;
        }

        private void CopySelectedLocatorSignalNames()
        {
            List<LocatorSignalRow> rows = _locatorSignals.Where(value => value.Use).ToList();
            if (rows.Count == 0 && _locatorGrid != null) rows = _locatorGrid.SelectedCells.Select(value => value.Item).OfType<LocatorSignalRow>().Concat(_locatorGrid.SelectedItems.Cast<object>().OfType<LocatorSignalRow>()).Distinct().ToList();
            if (rows.Count == 0 && _locatorGrid != null && _locatorGrid.CurrentItem is LocatorSignalRow) rows.Add((LocatorSignalRow)_locatorGrid.CurrentItem);
            if (rows.Count == 0) { MessageBox.Show("请先勾选信号，或者选中信号所在的单元格。", "复制信号名", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(value => value.Name)));
            _status.Text = "已复制 " + rows.Count + " 个信号名"; _status.Foreground = Brushes.DarkGreen;
        }

        private void SetCheckedLocatorResultMode(string mode)
        {
            List<LocatorSignalRow> rows = _locatorSignals.Where(value => value.Use).ToList(); if (rows.Count == 0) { MessageBox.Show("请先勾选需要批量设置的信号。", "批量结果类型", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            foreach (LocatorSignalRow row in rows) row.ResultMode = mode; _status.Text = "已将 " + rows.Count + " 个信号设置为“" + mode + "”"; _status.Foreground = Brushes.DarkGreen;
        }

        private void LocatorGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (FindAncestor<CheckBox>(source) != null || FindAncestor<TextBox>(source) != null || FindAncestor<ComboBox>(source) != null) return;
            DataGridCell cell = FindAncestor<DataGridCell>(source);
            if (cell == null || cell.IsReadOnly || cell.IsEditing) return;
            cell.Focus(); _locatorGrid.CurrentCell = new DataGridCellInfo(cell); _locatorGrid.BeginEdit();
            Dispatcher.BeginInvoke(new Action(() => { ComboBox combo = FindDescendant<ComboBox>(cell); if (combo != null) combo.IsDropDownOpen = true; }));
        }

        private static T FindAncestor<T>(DependencyObject value) where T : DependencyObject
        {
            while (value != null) { T match = value as T; if (match != null) return match; value = VisualTreeHelper.GetParent(value); }
            return null;
        }

        private static T FindDescendant<T>(DependencyObject value) where T : DependencyObject
        {
            if (value == null) return null; for (int index = 0; index < VisualTreeHelper.GetChildrenCount(value); index++) { DependencyObject child = VisualTreeHelper.GetChild(value, index); T match = child as T ?? FindDescendant<T>(child); if (match != null) return match; } return null;
        }

        private void EnsureDbcLoaded() { string baseDirectory = AppDomain.CurrentDomain.BaseDirectory; string configured = _getDbcPath == null ? string.Empty : _getDbcPath(); if (string.IsNullOrWhiteSpace(configured)) configured = "Config\\C95C96Auxiliary.dbc"; string path = Path.IsPathRooted(configured) ? configured : Path.Combine(baseDirectory, configured); if (_dbcDatabase != null && string.Equals(_dbcLoadedPath, path, StringComparison.OrdinalIgnoreCase)) return; _dbcDatabase = File.Exists(path) ? DbcDatabase.Load(path) : null; _dbcLoadedPath = path; }
        private void LoadDbcSelection() { if (_dbcMessage == null || _dbcDatabase == null) return; string name = Convert.ToString(_loadedStep.Get("MessageName"), CultureInfo.InvariantCulture); _dbcMessage.SelectedItem = _dbcDatabase.Messages.FirstOrDefault(value => value.Name == name) ?? _dbcDatabase.Messages.FirstOrDefault(); RefreshDbcSignals(); }
        private void RefreshDbcSignals() { _dbcSignals.Clear(); DbcMessageDefinition message = _dbcMessage == null ? null : _dbcMessage.SelectedItem as DbcMessageDefinition; if (message == null) return; JObject loaded = null; try { loaded = JObject.Parse(Convert.ToString(_loadedStep.Get("SignalsJson", "{}"), CultureInfo.InvariantCulture)); } catch { loaded = new JObject(); } foreach (DbcSignalDefinition signal in message.Signals) { JToken token = loaded[signal.Name]; _dbcSignals.Add(new DbcSignalEditRow(signal, token != null, token == null ? "0" : Convert.ToString(token, CultureInfo.InvariantCulture))); } _dbcReadSignal.ItemsSource = message.Signals.Select(value => value.Name).ToList(); string read = Convert.ToString(_loadedStep.Get("SignalName"), CultureInfo.InvariantCulture); _dbcReadSignal.SelectedItem = _dbcReadSignal.Items.Cast<object>().FirstOrDefault(value => Convert.ToString(value) == read) ?? _dbcReadSignal.Items.Cast<object>().FirstOrDefault(); if (Convert.ToString(_dbcMode == null ? null : _dbcMode.SelectedItem) == "读取DBC信号") foreach (DbcSignalEditRow row in _dbcSignals) row.Use = string.Equals(row.Name, Convert.ToString(_dbcReadSignal.SelectedItem), StringComparison.Ordinal); string loadedRaw = Convert.ToString(_loadedStep.Get("DataHex", string.Empty), CultureInfo.InvariantCulture); if (_dbcRawData != null) { bool wasLoading = _loading; _loading = true; _dbcRawData.Text = loadedRaw; _loading = wasLoading; if (!string.IsNullOrWhiteSpace(loadedRaw)) ApplyRawDataToDbcRows(false); else UpdateRawDataFromDbcRows(); } UpdateDbcSelectionSummary(); }
        private byte[] ParseDbcRawData(bool strict)
        {
            string text = _dbcRawData == null ? string.Empty : _dbcRawData.Text;
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                byte[] data = HexDataParser.Parse(text); DbcMessageDefinition message = _dbcMessage == null ? null : _dbcMessage.SelectedItem as DbcMessageDefinition;
                if (message != null && data.Length != message.Length) throw new ArgumentException("当前报文需要 " + message.Length + " 字节，实际输入 " + data.Length + " 字节。");
                return data;
            }
            catch (Exception ex) { if (strict) throw new InvalidOperationException("原始DATA格式不正确：" + ex.Message, ex); return null; }
        }
        private void ApplyRawDataToDbcRows(bool strict)
        {
            if (_dbcDatabase == null || _dbcMessage == null) return; byte[] data = ParseDbcRawData(strict); if (data == null) return; DbcMessageDefinition message = _dbcMessage.SelectedItem as DbcMessageDefinition; if (message == null) return; DbcDecodedFrame decoded = _dbcDatabase.Decode(new CanFrame(message.Id, data)); if (decoded == null) return;
            bool wasLoading = _loading; _loading = true; foreach (DbcDecodedSignal signal in decoded.Signals) { DbcSignalEditRow row = _dbcSignals.FirstOrDefault(value => value.Name == signal.Name); if (row != null) row.ValueText = signal.Value.ToString("0.########", CultureInfo.InvariantCulture); } if (_dbcGrid != null) _dbcGrid.Items.Refresh(); _loading = wasLoading; _status.Text = "原始DATA已解析为 " + decoded.Signals.Count + " 个DBC信号"; _status.Foreground = Brushes.DarkGreen;
        }
        private void UpdateRawDataFromDbcRows()
        {
            if (_dbcDatabase == null || _dbcMessage == null || _dbcRawData == null) return; DbcMessageDefinition message = _dbcMessage.SelectedItem as DbcMessageDefinition; if (message == null) return;
            try { Dictionary<string, double> values = _dbcSignals.ToDictionary(row => row.Name, row => Parse(row.ValueText, 0), StringComparer.Ordinal); CanFrame frame = _dbcDatabase.Encode(message.Name, values); bool wasLoading = _loading; _loading = true; _dbcRawData.Text = HexDataParser.Format(frame.Data); _loading = wasLoading; }
            catch { }
        }
        private void RefreshDbcMode() { if (_dbcReadSignal == null) return; bool read = Convert.ToString(_dbcMode.SelectedItem) == "读取DBC信号"; _dbcReadSignal.IsEnabled = read; _dbcTimeout.IsEnabled = read; _dbcPeriod.IsEnabled = Convert.ToString(_dbcMode.SelectedItem) == "开始周期发送"; }
        private void UpdateDbcSelectionSummary()
        {
            int selectedCount = _dbcSignals.Count(value => value.Use); if (_dbcSelectionSummary != null) _dbcSelectionSummary.Text = "已选 " + selectedCount + " / " + _dbcSignals.Count;
            if (_dbcSelectedSignalsPanel == null) return; _dbcSelectedSignalsPanel.Children.Clear(); List<DbcSignalEditRow> selected = _dbcSignals.Where(value => value.Use).ToList(); if (selected.Count == 0) { _dbcSelectedSignalsPanel.Children.Add(new TextBlock { Text = "尚未选择DBC信号，点击“配置信号”开始。", Foreground = Brushes.DimGray, Margin = new Thickness(2, 4, 2, 4) }); return; }
            foreach (DbcSignalEditRow row in selected.Take(6)) { Border chip = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(211, 221, 234)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6), Child = new TextBlock { Text = row.Name + "\n" + row.ValueText + (string.IsNullOrWhiteSpace(row.Unit) ? string.Empty : " " + row.Unit), FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(47, 63, 86)) } }; _dbcSelectedSignalsPanel.Children.Add(chip); } if (selected.Count > 6) _dbcSelectedSignalsPanel.Children.Add(new TextBlock { Text = "+ " + (selected.Count - 6) + " 项", Foreground = new SolidColorBrush(Color.FromRgb(24, 112, 224)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 10, 4, 4) });
        }

        private bool IsReadAction() { string source = Convert.ToString(_source.SelectedItem); if (source == "产品内部通信") return Convert.ToString(_operation.SelectedItem).StartsWith("读取", StringComparison.Ordinal); if (source == "产品DBC通信") return Convert.ToString(_operation.SelectedItem) == "读取DBC信号"; if (source == "原平台组合测试") return !string.Equals(InferResultMode(_loadedStep), "Action", StringComparison.Ordinal); return _descriptor != null && _descriptor.ReturnsValue; }
        private string BuildAutomaticName() { string source = Convert.ToString(_source.SelectedItem), target = Convert.ToString(_target.SelectedItem), operation = _operation.SelectedItem is ActionDescriptor ? ((ActionDescriptor)_operation.SelectedItem).DisplayName : Convert.ToString(_operation.SelectedItem); return (target + " " + operation).Trim(); }
        private static string DetectSource(SequenceStepDefinition step) { if (step.FunctionName == "FCT_CANSignal" || step.FunctionName == "FCT_CANTable" || string.Equals(step.FunctionName, "CAN_ReadSignalValue", StringComparison.OrdinalIgnoreCase)) return "产品内部通信"; if (step.FunctionName == "FCT_ExecuteLogic") return "流程逻辑"; string device = Convert.ToString(step.Get("Device"), CultureInfo.InvariantCulture); if (device == "AUXCAN") return "产品DBC通信"; if (device == "PRODUCTCAN") return "产品内部通信"; string function = (step.FunctionName ?? string.Empty).Replace("_", string.Empty).ToUpperInvariant(); if (function.Contains("DUTCOMUCATIONINIT") || function.Contains("CANAPP2FT") || function.Contains("CANSENDWAKEUP") || function.Contains("TESTCANCOMMUNICATION")) return "产品内部通信"; if (function.Contains("TESTDELAYMS")) return "流程逻辑"; if (step.FunctionName == "FCT_ExecuteAction" || ActionCatalog.Find("仪器", device, Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture), step.FunctionName) != null) return "仪器"; return "原平台组合测试"; }
        private static string DetectLocatorOperation(SequenceStepDefinition step) { bool write = string.Equals(Convert.ToString(step.Get("Operation")), "Write", StringComparison.OrdinalIgnoreCase); return write ? "写入" : "读取"; }
        private static string DetectDbcMode(SequenceStepDefinition step) { string operation = Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture); if (operation == "StartPeriodicDbc") return "开始周期发送"; if (operation == "StopPeriodicDbc") return "停止周期发送"; if (operation == "ReadDbcSignal") return "读取DBC信号"; if (operation == "SendRaw") return "发送原始帧"; return "发送一次"; }
        private static string InferResultMode(SequenceStepDefinition step) { if (step == null) return "Action"; object explicitMode = step.Get("ResultMode"); string explicitText = Convert.ToString(explicitMode, CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(explicitText) && !string.Equals(explicitText, "Action", StringComparison.OrdinalIgnoreCase)) return explicitText; if (step.Properties.ContainsKey("LowLimit") || step.Properties.ContainsKey("HighLimit")) return "NumericLimit"; if (step.Properties.ContainsKey("Limit")) return "StringLimit"; MainTestMethodSemantics semantics = MainTestMethodCatalog.Inspect(step.FunctionName); if (semantics.ResultKind == MainTestResultKind.NumericLimit) return "NumericLimit"; if (semantics.ResultKind == MainTestResultKind.StringLimit) return "StringLimit"; return "Action"; }
        private static double Parse(string text, double fallback) { double value; return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback; }
        private static ComboBox Combo(double width) { return new ComboBox { Width = width, Margin = new Thickness(4), MinHeight = 30 }; }
        private static DataTemplate TextItemTemplate(string property) { FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(property)); text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); return new DataTemplate { VisualTree = text }; }
        private static TextBox Box(double width) { return new TextBox { Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) }; }
        private static TextBlock Label(string text) { return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) }; }
        private static TextBlock Heading(string text) { return new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 49, 67)), Margin = new Thickness(0, 0, 0, 4) }; }
        private static Border Card() { return new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 239)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) }; }
        private static Button Button(string text, RoutedEventHandler handler) { Button button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6), MinHeight = 32 }; button.Click += handler; return button; }
        private static Button PrimaryButton(string text, RoutedEventHandler handler) { Button button = Button(text, handler); button.Background = new SolidColorBrush(Color.FromRgb(24, 112, 224)); button.Foreground = Brushes.White; return button; }
    }

    internal sealed class ActionFieldEditor
    {
        public ActionFieldEditor(ActionFieldSpec spec, Control control, CheckBox expose = null, TextBox parameterName = null) { Spec = spec; Control = control; Expose = expose; ParameterNameBox = parameterName; }
        public ActionFieldSpec Spec { get; private set; } public Control Control { get; private set; } public CheckBox Expose { get; private set; } public TextBox ParameterNameBox { get; private set; }
        public bool IsExposed { get { return Expose != null && Expose.IsChecked == true && !string.IsNullOrWhiteSpace(ParameterName); } }
        public string ParameterName { get { return ParameterNameBox == null ? Spec.Label : (ParameterNameBox.Text ?? string.Empty).Trim(); } }
        public object Value() { if (Control is CheckBox) return ((CheckBox)Control).IsChecked == true; string text = Control is ComboBox ? Convert.ToString(((ComboBox)Control).SelectedItem, CultureInfo.InvariantCulture) : ((TextBox)Control).Text; text = NormalizeOptionCode(text); if (Spec.Type == "int") return Convert.ToInt32(Parse(text, 0)); if (Spec.Type == "double") return Parse(text, 0); return text ?? string.Empty; }
        internal static string NormalizeOptionCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string mapped = ResolveDcdcLoadModeCode(text);
            if (!string.IsNullOrEmpty(mapped)) return mapped;
            string value = text.Trim();
            int separator = value.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0) separator = value.IndexOf('-');
            if (separator < 0) separator = value.IndexOf(' ');
            if (separator > 0) value = value.Substring(0, separator).Trim();
            return value;
        }
        // Accept instrument labels (CC/CV/CR/CP + 量程) and legacy driver codes (Ccm...).
        internal static string ResolveDcdcLoadModeCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string value = text.Trim();
            int separator = value.IndexOf(" - ", StringComparison.Ordinal);
            if (separator > 0) value = value.Substring(0, separator).Trim();
            string key = value.Replace("（", "").Replace("）", "").Replace("(", "").Replace(")", "").Replace("·", " ").Replace("　", " ");
            while (key.Contains("  ")) key = key.Replace("  ", " ");
            key = key.Trim().ToUpperInvariant();
            switch (key)
            {
                case "CCL": case "CC L": case "CC低": case "CC 低": case "CC低量程": case "CC 低量程": return "Ccl";
                case "CCM": case "CC M": case "CC中": case "CC 中": case "CC中量程": case "CC 中量程": return "Ccm";
                case "CCH": case "CC H": case "CC高": case "CC 高": case "CC高量程": case "CC 高量程": return "Cch";
                case "CVL": case "CV L": case "CV低": case "CV 低": case "CV低量程": case "CV 低量程": return "Cvl";
                case "CVM": case "CV M": case "CV中": case "CV 中": case "CV中量程": case "CV 中量程": return "Cvm";
                case "CVH": case "CV H": case "CV高": case "CV 高": case "CV高量程": case "CV 高量程": return "Cvh";
                case "CRL": case "CR L": case "CR低": case "CR 低": case "CR低量程": case "CR 低量程": return "Crl";
                case "CRM": case "CR M": case "CR中": case "CR 中": case "CR中量程": case "CR 中量程": return "Crm";
                case "CRH": case "CR H": case "CR高": case "CR 高": case "CR高量程": case "CR 高量程": return "Crh";
                case "CPL": case "CP L": case "CP低": case "CP 低": case "CP低量程": case "CP 低量程": return "Cpl";
                case "CPM": case "CP M": case "CP中": case "CP 中": case "CP中量程": case "CP 中量程": return "Cpm";
                case "CPH": case "CP H": case "CP高": case "CP 高": case "CP高量程": case "CP 高量程": return "Cph";
                default: return null;
            }
        }
        private static double Parse(string text, double fallback) { double value; return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback; }
    }

    internal sealed class ActionFieldSpec { public ActionFieldSpec(string name, string label, string type, object defaultValue, string unit = "", string[] options = null) { Name = name; Label = label; Type = type; DefaultValue = defaultValue; Unit = unit; Options = options; } public string Name, Label, Type, Unit; public object DefaultValue; public string[] Options; }
    internal sealed class ActionDescriptor
    {
        public ActionDescriptor(string source, string target, string device, string operation, string displayName, bool returnsValue, params ActionFieldSpec[] fields) { Source = source; Target = target; Device = device; Operation = operation; DisplayName = displayName; ReturnsValue = returnsValue; Fields = fields.ToList(); BindingMode = "Generic"; FunctionName = string.Empty; PluginAssembly = string.Empty; PluginType = string.Empty; }
        public string Source, Target, Device, Operation, DisplayName, BindingMode, FunctionName, PluginAssembly, PluginType; public bool ReturnsValue; public List<ActionFieldSpec> Fields; public override string ToString() { return DisplayName; }
    }

    internal sealed class InstrumentActionDefinition
    {
        public InstrumentActionDefinition() { Source = "仪器"; Target = "新仪器"; Device = "CUSTOM"; Operation = "Execute"; DisplayName = "新动作"; BindingMode = "Plugin"; FunctionName = string.Empty; PluginAssembly = string.Empty; PluginType = string.Empty; Fields = new List<InstrumentActionFieldDefinition>(); }
        public string Source { get; set; } public string Target { get; set; } public string Device { get; set; } public string Operation { get; set; } public string DisplayName { get; set; } public bool ReturnsValue { get; set; } public string BindingMode { get; set; } public string FunctionName { get; set; } public string PluginAssembly { get; set; } public string PluginType { get; set; } public List<InstrumentActionFieldDefinition> Fields { get; set; }
    }
    internal sealed class InstrumentActionFieldDefinition
    {
        public InstrumentActionFieldDefinition() { Name = "Value"; Label = "参数"; Type = "double"; DefaultValue = "0"; Unit = string.Empty; Options = string.Empty; }
        public string Name { get; set; } public string Label { get; set; } public string Type { get; set; } public string DefaultValue { get; set; } public string Unit { get; set; } public string Options { get; set; }
    }

    internal static class ActionCatalog
    {
        private static List<ActionDescriptor> _descriptors = Build(); private static string _configurationPath;
        // Descriptors is what every editor surface binds to, so the station filter is applied here once
        // instead of at each call site.
        public static IReadOnlyList<ActionDescriptor> Descriptors { get { HashSet<string> generic = new HashSet<string>(_descriptors.Where(value => string.Equals(value.BindingMode, "Generic", StringComparison.OrdinalIgnoreCase)).Select(value => (value.Device ?? string.Empty) + "|" + (value.Operation ?? string.Empty)), StringComparer.OrdinalIgnoreCase); return _descriptors.Where(value => !(string.Equals(value.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase) && (value.FunctionName ?? string.Empty).StartsWith("UI_", StringComparison.OrdinalIgnoreCase) && generic.Contains((value.Device ?? string.Empty) + "|" + (value.Operation ?? string.Empty)))).ToList().AsReadOnly(); } }
        public static IReadOnlyList<ActionDescriptor> AllDescriptors { get { return _descriptors.AsReadOnly(); } }
        public static IEnumerable<ActionDescriptor> PickerDescriptors(string source) { HashSet<string> legacy = new HashSet<string>(new[] { "MOXA", "RELAY", "DMM", "RES" }, StringComparer.OrdinalIgnoreCase); return Descriptors.Where(value => string.Equals(value.Source, source, StringComparison.OrdinalIgnoreCase) && !legacy.Contains(value.Device ?? string.Empty) && (!(string.Equals(value.Device, "RELAY_FCT", StringComparison.OrdinalIgnoreCase) || string.Equals(value.Device, "RELAY_HVMUX", StringComparison.OrdinalIgnoreCase)) || string.Equals(value.Operation, "SetDO", StringComparison.OrdinalIgnoreCase))); }
        public static string PickerTarget(ActionDescriptor descriptor) { string device = descriptor == null ? string.Empty : descriptor.Device ?? string.Empty; if (string.Equals(device, "RELAY_FCT", StringComparison.OrdinalIgnoreCase)) return "FCT功能板"; if (string.Equals(device, "RELAY_HVMUX", StringComparison.OrdinalIgnoreCase)) return "高压继电器板"; if (string.Equals(device, "LVDC", StringComparison.OrdinalIgnoreCase)) return "KL30低压电源"; if (string.Equals(device, "LVDC_KL15", StringComparison.OrdinalIgnoreCase)) return "KL15低压电源"; return descriptor == null ? string.Empty : descriptor.Target; }
        public static string ConfigurationPath { get { return _configurationPath; } }
        public static void Configure(string baseDirectory)
        {
            _configurationPath = Path.Combine(baseDirectory, "Config", "InstrumentActions.json"); Directory.CreateDirectory(Path.GetDirectoryName(_configurationPath));
            if (!File.Exists(_configurationPath)) { SaveDefinitions(Build().Select(ToDefinition)); return; }
            List<InstrumentActionDefinition> merged = LoadDefinitions().ToList();
            foreach (InstrumentActionDefinition required in Build().Select(ToDefinition))
            {
                bool exists = merged.Any(value => string.Equals(value.Source, required.Source, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Device, required.Device, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Operation, required.Operation, StringComparison.OrdinalIgnoreCase) && string.Equals(value.FunctionName ?? string.Empty, required.FunctionName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (!exists) merged.Add(required);
            }
            SaveDefinitions(merged);
        }
        public static IReadOnlyList<InstrumentActionDefinition> LoadDefinitions() { if (string.IsNullOrWhiteSpace(_configurationPath) || !File.Exists(_configurationPath)) return Build().Select(ToDefinition).ToList().AsReadOnly(); List<InstrumentActionDefinition> values = JsonConvert.DeserializeObject<List<InstrumentActionDefinition>>(File.ReadAllText(_configurationPath)) ?? new List<InstrumentActionDefinition>(); foreach (InstrumentActionDefinition value in values) value.Fields = value.Fields ?? new List<InstrumentActionFieldDefinition>(); List<InstrumentActionDefinition> built = Build().Select(ToDefinition).ToList(); foreach (InstrumentActionDefinition required in built) if (!values.Any(value => string.Equals(value.Device, required.Device, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Operation, required.Operation, StringComparison.OrdinalIgnoreCase))) values.Add(required); foreach (InstrumentActionDefinition improved in built.Where(value => (value.Device == "RELAY_FCT" || value.Device == "RELAY_HVMUX") && (value.Operation == "SetDO" || value.Operation == "Select15"))) { int index = values.FindIndex(value => value.Device == improved.Device && value.Operation == improved.Operation); if (index >= 0) values[index] = improved; } return values.AsReadOnly(); }
        public static void SaveDefinitions(IEnumerable<InstrumentActionDefinition> definitions) { if (string.IsNullOrWhiteSpace(_configurationPath)) throw new InvalidOperationException("Instrument action catalog is not configured."); List<InstrumentActionDefinition> values = (definitions ?? Enumerable.Empty<InstrumentActionDefinition>()).ToList(); File.WriteAllText(_configurationPath, JsonConvert.SerializeObject(values, Formatting.Indented)); _descriptors = values.Select(ToDescriptor).ToList(); }
        public static void Reload() { _descriptors = LoadDefinitions().Select(ToDescriptor).ToList(); }
        public static ActionDescriptor Find(string source, string device, string operation, string functionName)
        {
            ActionDescriptor method = _descriptors.FirstOrDefault(value => string.Equals(value.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase) && string.Equals(value.FunctionName, functionName, StringComparison.OrdinalIgnoreCase)); if (method != null) return method;
            ActionDescriptor direct = _descriptors.FirstOrDefault(value => value.Source == source && string.Equals(value.Device, device, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Operation, operation, StringComparison.OrdinalIgnoreCase)); if (direct != null) return direct;
            string name = (functionName ?? string.Empty).Replace("_", string.Empty).ToUpperInvariant(); string mappedDevice = null, mappedOperation = null;
            if (name.Contains("LVDCSETSOURCEVOLTAGE")) { mappedDevice = "LVDC"; mappedOperation = "SetVoltage"; } else if (name.Contains("LVDCSETSOURCECURRENT")) { mappedDevice = "LVDC"; mappedOperation = "SetCurrent"; } else if (name.Contains("LVDCSETOUTPUT")) { mappedDevice = "LVDC"; mappedOperation = "SetOutput"; }
            else if (name.Contains("HVDCSETSOURCEVOLTAGE")) { mappedDevice = "HVDC"; mappedOperation = "SetVoltage"; } else if (name.Contains("HVDCSETSOURCECURRENT")) { mappedDevice = "HVDC"; mappedOperation = "SetCurrent"; } else if (name.Contains("HVDCSETOUTPUT")) { mappedDevice = "HVDC"; mappedOperation = "SetOutput"; }
            else if (name.Contains("DMMCONFIGDMMFORDCCUR")) { mappedDevice = "DMM"; mappedOperation = "ConfigDCCurrent"; } else if (name.Contains("DMMCONFIGDMMFORDC")) { mappedDevice = "DMM"; mappedOperation = "ConfigDCVoltage"; } else if (name.Contains("DMMCONFIGDMMFORACCURRENT")) { mappedDevice = "DMM"; mappedOperation = "ConfigACCurrent"; } else if (name.Contains("DMMCONFIGDMMFORAC")) { mappedDevice = "DMM"; mappedOperation = "ConfigACVoltage"; } else if (name.Contains("DMMGETMEASURE")) { mappedDevice = "DMM"; mappedOperation = "Read"; }
            else if (name.Contains("RESSETRESISTANCE")) { mappedDevice = "RES"; mappedOperation = "SetResistance"; } else if (name.Contains("DAQREADCURRENT")) { mappedDevice = "DAQ"; mappedOperation = "Read"; } else if (name.Contains("MOXASETDO")) { mappedDevice = "MOXA"; mappedOperation = "SetDO"; } else if (name.Contains("RELAYSETDO")) { mappedDevice = "RELAY"; mappedOperation = "SetDO"; } else if (name.Contains("PLCLOADFINISHED")) { mappedDevice = "PLC"; mappedOperation = "LoadFinished"; }
            else if (name.Contains("RESOLVERSETSPEED")) { mappedDevice = "RESOLVER"; mappedOperation = "SetSpeed"; } else if (name.Contains("RESOLVERSETPOSITION")) { mappedDevice = "RESOLVER"; mappedOperation = "SetPosition"; } else if (name.Contains("RESOLVERINIT")) { mappedDevice = "RESOLVER"; mappedOperation = "Init"; } else if (name.Contains("RESOLVERSTOP")) { mappedDevice = "RESOLVER"; mappedOperation = "Stop"; }
            else if (name.Contains("TESTDELAYMS")) { mappedDevice = "FLOW"; mappedOperation = "Delay"; }
            else if (name.Contains("DUTCOMUCATIONINIT")) { mappedDevice = "PRODUCTCAN"; mappedOperation = "CommunicationInit"; } else if (name.Contains("CANAPP2FT")) { mappedDevice = "PRODUCTCAN"; mappedOperation = "EnterFT"; } else if (name.Contains("CANSENDWAKEUP")) { mappedDevice = "PRODUCTCAN"; mappedOperation = "Wakeup"; } else if (name.Contains("TESTCANCOMMUNICATION")) { mappedDevice = "PRODUCTCAN"; mappedOperation = "CommunicationTest"; }
            return mappedDevice == null ? null : _descriptors.FirstOrDefault(value => string.Equals(value.Device, mappedDevice, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Operation, mappedOperation, StringComparison.OrdinalIgnoreCase));
        }
        private static InstrumentActionDefinition ToDefinition(ActionDescriptor value) { return new InstrumentActionDefinition { Source = value.Source, Target = value.Target, Device = value.Device, Operation = value.Operation, DisplayName = value.DisplayName, ReturnsValue = value.ReturnsValue, BindingMode = value.BindingMode, FunctionName = value.FunctionName, PluginAssembly = value.PluginAssembly, PluginType = value.PluginType, Fields = value.Fields.Select(field => new InstrumentActionFieldDefinition { Name = field.Name, Label = field.Label, Type = field.Type, DefaultValue = Convert.ToString(field.DefaultValue, CultureInfo.InvariantCulture), Unit = field.Unit, Options = field.Options == null ? string.Empty : string.Join("|", field.Options) }).ToList() }; }
        private static ActionDescriptor ToDescriptor(InstrumentActionDefinition value) { ActionDescriptor descriptor = new ActionDescriptor(string.IsNullOrWhiteSpace(value.Source) ? "仪器" : value.Source, value.Target ?? string.Empty, value.Device ?? string.Empty, value.Operation ?? string.Empty, value.DisplayName ?? string.Empty, value.ReturnsValue, (value.Fields ?? new List<InstrumentActionFieldDefinition>()).Select(field => new ActionFieldSpec(field.Name ?? string.Empty, field.Label ?? field.Name ?? string.Empty, field.Type ?? "string", ParseDefault(field), field.Unit ?? string.Empty, string.IsNullOrWhiteSpace(field.Options) ? null : field.Options.Split('|'))).ToArray()); descriptor.BindingMode = string.IsNullOrWhiteSpace(value.BindingMode) ? "Generic" : value.BindingMode; descriptor.FunctionName = value.FunctionName ?? string.Empty; descriptor.PluginAssembly = value.PluginAssembly ?? string.Empty; descriptor.PluginType = value.PluginType ?? string.Empty; return descriptor; }
        private static object ParseDefault(InstrumentActionFieldDefinition field) { string text = field.DefaultValue ?? string.Empty; if (string.Equals(field.Type, "bool", StringComparison.OrdinalIgnoreCase)) { bool value; return bool.TryParse(text, out value) && value; } if (string.Equals(field.Type, "int", StringComparison.OrdinalIgnoreCase)) { int value; return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0; } if (string.Equals(field.Type, "double", StringComparison.OrdinalIgnoreCase)) { double value; return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0.0; } return text; }
        private static List<ActionDescriptor> Build()
        {
            List<ActionDescriptor> list = new List<ActionDescriptor>();
            foreach (string device in new[] { "LVDC", "HVDC" }) { string target = device == "LVDC" ? "低压电源（KL30）" : "高压电源"; list.Add(new ActionDescriptor("仪器", target, device, "SetVoltage", "设置电压", false, new ActionFieldSpec("Voltage", "设定电压", "double", device == "LVDC" ? 24.0 : 600.0, "V"))); list.Add(new ActionDescriptor("仪器", target, device, "SetCurrent", "设置电流", false, new ActionFieldSpec("Current", "电流限制", "double", 5.0, "A"))); if (device == "HVDC") list.Add(new ActionDescriptor("仪器", target, device, "SetPower", "设置功率", false, new ActionFieldSpec("Power", "功率限制", "double", 8600.0, "W"))); list.Add(new ActionDescriptor("仪器", target, device, "SetOutput", "输出开关", false, new ActionFieldSpec("Output", "输出状态", "bool", true))); list.Add(new ActionDescriptor("仪器", target, device, "ReadVoltage", "读取电压", true)); list.Add(new ActionDescriptor("仪器", target, device, "ReadCurrent", "读取电流", true)); if (device == "HVDC") list.Add(new ActionDescriptor("仪器", target, device, "ReadPower", "读取功率", true)); }
            list.Add(new ActionDescriptor("仪器", "低压电源（KL15）", "LVDC_KL15", "SetVoltage", "设置电压", false, new ActionFieldSpec("Voltage", "设定电压", "double", 24.0, "V"))); list.Add(new ActionDescriptor("仪器", "低压电源（KL15）", "LVDC_KL15", "SetCurrent", "设置电流", false, new ActionFieldSpec("Current", "电流限制", "double", 5.0, "A"))); list.Add(new ActionDescriptor("仪器", "低压电源（KL15）", "LVDC_KL15", "SetOutput", "输出开关", false, new ActionFieldSpec("Output", "输出状态", "bool", true))); list.Add(new ActionDescriptor("仪器", "低压电源（KL15）", "LVDC_KL15", "ReadVoltage", "读取电压", true)); list.Add(new ActionDescriptor("仪器", "低压电源（KL15）", "LVDC_KL15", "ReadCurrent", "读取电流", true));
            list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "SetMode", "设置模式", false, new ActionFieldSpec("Mode", "负载模式", "string", "Ccm", "", ActionConfigurationPanel.DcdcLoadModeOptions())));
            list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "SetCurrent", "设置恒流值", false, new ActionFieldSpec("Current", "电流", "double", 0.0, "A"))); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "SetVoltage", "设置恒压值", false, new ActionFieldSpec("Voltage", "电压", "double", 0.0, "V"))); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "SetResistance", "设置恒阻值", false, new ActionFieldSpec("Resistance", "电阻", "double", 1.0, "Ω"))); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "SetPower", "设置恒功率值", false, new ActionFieldSpec("Power", "功率", "double", 0.0, "W")));
            list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "OutputOn", "负载输入开", false)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "OutputOff", "负载输入关", false)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "ReadVoltage", "读取电压", true)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "ReadCurrent", "读取电流", true)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "ReadPower", "读取功率", true)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "ReadProtection", "读取保护状态", true)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "ClearProtection", "清除保护", false)); list.Add(new ActionDescriptor("仪器", "DCDC电子负载（AN23600E）", "DCDC_LOAD", "Reset", "复位", false));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "Init", "初始化万用表", false));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "Reset", "复位万用表", false));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "Identify", "读取设备信息", true));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigMeasure", "选择测量类型", false, new ActionFieldSpec("MeasureType", "测量类型", "string", "DCVoltage", "", new[] { "DCVoltage - 直流电压", "DCCurrent - 直流电流", "ACVoltage - 交流电压", "ACCurrent - 交流电流", "Resistance - 电阻", "Frequence - 频率" })));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigDCVoltage", "配置直流电压", false, new ActionFieldSpec("Range", "量程", "double", 1000.0, "V"), new ActionFieldSpec("Solution", "分辨率", "double", 0.01, "V")));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigDCCurrent", "配置直流电流", false, new ActionFieldSpec("Range", "量程", "double", 3.0, "A"), new ActionFieldSpec("Solution", "分辨率", "double", 0.00001, "A")));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigACVoltage", "配置交流电压", false, new ActionFieldSpec("Range", "量程", "double", 1000.0, "V"), new ActionFieldSpec("Solution", "分辨率", "double", 0.01, "V")));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigACCurrent", "配置交流电流", false, new ActionFieldSpec("Range", "量程", "double", 3.0, "A"), new ActionFieldSpec("Solution", "分辨率", "double", 0.00001, "A")));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigResistance", "配置电阻测量", false, new ActionFieldSpec("Range", "量程", "double", -1.0, "Ω"), new ActionFieldSpec("Solution", "分辨率", "double", -1.0, "Ω")));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigFrequency", "配置频率测量", false, new ActionFieldSpec("Range", "量程", "double", -1.0, "Hz"), new ActionFieldSpec("Solution", "分辨率", "double", -1.0, "Hz")));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "ConfigCalculateMaximum", "配置最大值计算", false));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "Read", "读取测量值", true));
            list.Add(new ActionDescriptor("仪器", "DMM", "DMM", "Close", "关闭会话", false));
            list.Add(new ActionDescriptor("仪器", "电阻模拟器", "RES", "SetResistance", "设置电阻", false, new ActionFieldSpec("ResValue", "电阻值", "double", 1000.0, "Ω"), new ActionFieldSpec("Channel", "通道", "int", 1))); list.Add(new ActionDescriptor("仪器", "DAQ电流采集", "DAQ", "Read", "读取电流", true, new ActionFieldSpec("Hardware", "采集硬件", "string", "NI9227", "", new[] { "NI9227", "PCI6229" }), new ActionFieldSpec("PhysicalChannel", "物理通道", "string", "cDAQ1Mod1/ai0"), new ActionFieldSpec("Channel", "旧6229通道", "int", 0), new ActionFieldSpec("Ratio", "互感器倍率", "double", 5000.0), new ActionFieldSpec("Scale", "旧6229比例", "double", 22.058823529), new ActionFieldSpec("Offset", "偏移", "double", 0.0)));
            foreach (ActionDescriptor original in list.Where(value => value.Device == "DMM").ToList()) { list.Add(new ActionDescriptor("仪器", "高压万用表", "DMM_HV", original.Operation, original.DisplayName, original.ReturnsValue, original.Fields.ToArray())); list.Add(new ActionDescriptor("仪器", "低压万用表", "DMM_LV", original.Operation, original.DisplayName, original.ReturnsValue, original.Fields.ToArray())); }
            foreach (string device in new[] { "RES_1", "RES_2", "RES_3" }) list.Add(new ActionDescriptor("仪器", "程控电阻卡 " + device.Substring(4), device, "SetResistance", "设置电阻", false, new ActionFieldSpec("ResValue", "电阻值", "double", 1000.0, "Ω"), new ActionFieldSpec("Channel", "通道", "int", 1)));
            list.Add(new ActionDescriptor("仪器", "PLC", "PLC", "LoadFinished", "写入完成信号", false));
            list.Add(new ActionDescriptor("仪器", "FCT功能继电器板（48路）", "RELAY_FCT", "SetDO", "设置IO（Y00-Y57）", false, new ActionFieldSpec("Channels", "输出端口", "string", "", "", null), new ActionFieldSpec("Values", "状态列表", "string", "")));
            list.Add(new ActionDescriptor("仪器", "FCT功能继电器板（48路）", "RELAY_FCT", "SelectFctMux", "安全选择测试功能", false, new ActionFieldSpec("Selection", "测试功能", "string", "1 - HVDC测量备用（J1）", "", ActionConfigurationPanel.FctMuxFunctionOptions()), new ActionFieldSpec("SwitchDelayMs", "切换等待", "int", 50, "ms"), new ActionFieldSpec("Slave", "从站地址", "int", 1, "", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" })));
            list.Add(new ActionDescriptor("仪器", "FCT功能继电器板（48路）", "RELAY_FCT", "DisableFctMux", "安全关闭测试选择", false, new ActionFieldSpec("SwitchDelayMs", "关闭等待", "int", 50, "ms"), new ActionFieldSpec("Slave", "从站地址", "int", 1, "", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" })));
            list.Add(new ActionDescriptor("仪器", "高压15选1锁存板", "RELAY_HVMUX", "Select15", "选择高压测量位置", false, new ActionFieldSpec("Selection", "测量位置", "string", "1 - HV1 / HVDC", "", new[] { "1 - HV1 / HVDC", "2 - HV2 / DCDC", "3 - HV3 / G1", "4 - HV4 / G2", "5 - HV5 / G3", "6 - HV6 / G4", "7 - HV7 / G5", "8 - HV8 / UP1", "9 - HV9 / UP3", "10 - HV10 / fast1", "11 - HV11 / fast2", "12 - HV12 / fast3", "13 - HV13 / fast4", "14 - HV14 / battery1", "15 - HV15 / battery2" }), new ActionFieldSpec("SwitchDelayMs", "切换等待", "int", 50, "ms"), new ActionFieldSpec("Slave", "从站地址", "int", 1, "", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" })));
            list.Add(new ActionDescriptor("仪器", "高压15选1锁存板（48路）", "RELAY_HVMUX", "Disable15", "安全关闭全部通道", false, new ActionFieldSpec("SwitchDelayMs", "关闭等待", "int", 50, "ms"), new ActionFieldSpec("Slave", "从站地址", "int", 1, "", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" })));
            list.Add(new ActionDescriptor("仪器", "高压继电器板（15路）", "RELAY_HVMUX", "SetDO", "设置IO（Y00-Y16）", false, new ActionFieldSpec("Channels", "输出端口", "string", ""), new ActionFieldSpec("Values", "状态列表", "string", "")));
            list.Add(new ActionDescriptor("仪器", "旋变模拟器", "RESOLVER", "Init", "初始化", false)); list.Add(new ActionDescriptor("仪器", "旋变模拟器", "RESOLVER", "SetPolePairs", "设置极对数", false, new ActionFieldSpec("PolePairs", "极对数", "int", 6))); list.Add(new ActionDescriptor("仪器", "旋变模拟器", "RESOLVER", "SetSpeed", "设置转速", false, new ActionFieldSpec("Speed", "转速", "double", 700.0, "rpm"), new ActionFieldSpec("PolePairs", "极对数", "int", 6))); list.Add(new ActionDescriptor("仪器", "旋变模拟器", "RESOLVER", "SetPosition", "设置角度", false, new ActionFieldSpec("Position", "位置", "double", 225.0, "deg"), new ActionFieldSpec("PolePairs", "极对数", "int", 1))); list.Add(new ActionDescriptor("仪器", "旋变模拟器", "RESOLVER", "SendDbcSignal", "发送DBC信号", false, new ActionFieldSpec("SignalName", "信号名", "string", "2505419280_Speed"), new ActionFieldSpec("Value", "信号值", "double", 0.0), new ActionFieldSpec("SendFlag", "立即发送", "bool", true))); list.Add(new ActionDescriptor("仪器", "旋变模拟器", "RESOLVER", "Stop", "停止", false));
            list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "EnterFT", "进入FT模式", false)); list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "CommunicationInit", "通信初始化", false, new ActionFieldSpec("TxID", "发送ID", "string", "2030"), new ActionFieldSpec("RxID", "接收ID", "string", "2031"))); list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "Wakeup", "发送唤醒", false)); list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "CommunicationTest", "通信测试", true)); list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "SendDbcSignal", "发送产品DBC信号", false, new ActionFieldSpec("SignalName", "信号名", "string", ""), new ActionFieldSpec("Value", "信号值", "double", 0.0), new ActionFieldSpec("SendFlag", "立即发送", "bool", true))); list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "SendRaw", "发送原始CAN帧", true, new ActionFieldSpec("CanId", "CAN ID", "string", "7EE"), new ActionFieldSpec("DataHex", "数据", "string", "00 00 00 00 00 00 00 00"))); list.Add(new ActionDescriptor("产品内部通信", "产品基础命令", "PRODUCTCAN", "ReceiveRaw", "读取原始CAN帧", true, new ActionFieldSpec("FilterId", "过滤ID", "string", "7EF")));
            list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "Delay", "延时", false, new ActionFieldSpec("TimeMs", "延时时间", "int", 1000, "ms"))); list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "SetVariable", "设置变量", false, new ActionFieldSpec("VariableName", "变量名", "string", "Value1"), new ActionFieldSpec("ValueText", "变量值", "string", "0"))); list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "Goto", "跳转", false, new ActionFieldSpec("TargetStepName", "目标步骤", "string", ""))); list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "FixedLoop", "固定循环", false, new ActionFieldSpec("LoopId", "循环ID", "string", "Loop1"), new ActionFieldSpec("Count", "次数", "int", 2), new ActionFieldSpec("TargetStepName", "返回步骤", "string", ""))); list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "Condition", "条件判断", false, new ActionFieldSpec("VariableName", "变量名", "string", "Value1"), new ActionFieldSpec("DataType", "数据类型", "string", "Number", "", new[] { "Number", "String", "Boolean" }), new ActionFieldSpec("Compare", "比较方式", "string", "GE", "", new[] { "GT", "GE", "LT", "LE", "EQ", "NE", "CONTAINS", "STARTSWITH" }), new ActionFieldSpec("RightValue", "比较值", "string", "0"), new ActionFieldSpec("TrueGoto", "成立跳转", "string", ""), new ActionFieldSpec("FalseGoto", "不成立跳转", "string", ""), new ActionFieldSpec("RecordResult", "记录结果", "bool", true))); list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "SafeShutdown", "安全下电", false)); list.Add(new ActionDescriptor("流程逻辑", "流程控制", "FLOW", "Stop", "停止流程", false)); return list;
        }
    }

    internal sealed class LocatorSignalRow : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _use; private string _valueText, _resultMode, _lowLimitText, _highLimitText, _compareText, _unitText, _expectedText;
        public LocatorSignalRow(ProductLocatorSignal signal) { Signal = signal; _valueText = "0"; _resultMode = "只记录信息"; _lowLimitText = "0"; _highLimitText = "0"; _compareText = "GELE"; _unitText = signal.Unit; _expectedText = string.Empty; }
        public ProductLocatorSignal Signal { get; private set; } public bool Use { get { return _use; } set { _use = value; Raise("Use"); } } public string ValueText { get { return _valueText; } set { _valueText = value ?? string.Empty; Raise("ValueText"); } } public string ResultMode { get { return _resultMode; } set { _resultMode = value ?? "只记录信息"; Raise("ResultMode"); } } public string LowLimitText { get { return _lowLimitText; } set { _lowLimitText = value ?? string.Empty; Raise("LowLimitText"); } } public string HighLimitText { get { return _highLimitText; } set { _highLimitText = value ?? string.Empty; Raise("HighLimitText"); } } public string CompareText { get { return _compareText; } set { _compareText = value ?? string.Empty; Raise("CompareText"); } } public string UnitText { get { return _unitText; } set { _unitText = value ?? string.Empty; Raise("UnitText"); } } public string ExpectedText { get { return _expectedText; } set { _expectedText = value ?? string.Empty; Raise("ExpectedText"); } }
        public bool NeedsJudgment { get { return ResultMode == "数值LIMIT" || ResultMode == "字符串匹配"; } }
        public LocatorSignalRow CloneForEditing() { return new LocatorSignalRow(Signal) { Use = Use, ValueText = ValueText, ResultMode = ResultMode, LowLimitText = LowLimitText, HighLimitText = HighLimitText, CompareText = CompareText, UnitText = UnitText, ExpectedText = ExpectedText }; }
        public void CopyConfigurationFrom(LocatorSignalRow source) { if (source == null) return; Use = source.Use; ValueText = source.ValueText; ResultMode = source.ResultMode; LowLimitText = source.LowLimitText; HighLimitText = source.HighLimitText; CompareText = source.CompareText; UnitText = source.UnitText; ExpectedText = source.ExpectedText; }
        public string Name { get { return Signal.Name; } } public int Offset { get { return Signal.Offset; } } public string OffsetText { get { return "0x" + Signal.Offset.ToString("X", CultureInfo.InvariantCulture); } } public string DataType { get { return Signal.DataType; } } public string Unit { get { return Signal.Unit; } } public string Comment { get { return Signal.Comment; } }
        public IDictionary<string, object> ToReadCheck() { string mode = ResultMode == "数值LIMIT" ? "NumericLimit" : ResultMode == "字符串匹配" ? "StringLimit" : "Information"; Dictionary<string, object> result = new Dictionary<string, object> { { "Name", Name }, { "Offset", Signal.Offset }, { "DataSize", Signal.DataSize }, { "DataType", Signal.DataType }, { "Endian", "Little" }, { "ResultMode", mode } }; if (!string.IsNullOrWhiteSpace(UnitText)) result["Unit"] = UnitText; if (mode == "NumericLimit") { result["LowLimit"] = Parse(LowLimitText, 0); result["HighLimit"] = Parse(HighLimitText, 0); result["Comtype"] = string.IsNullOrWhiteSpace(CompareText) ? "GELE" : CompareText; } else if (mode == "StringLimit") result["Limit"] = ExpectedText ?? string.Empty; return result; }
        public void LoadReadCheck(JObject check) { Use = true; string mode = Convert.ToString(check["ResultMode"], CultureInfo.InvariantCulture); ResultMode = mode == "NumericLimit" ? "数值LIMIT" : mode == "StringLimit" ? "字符串匹配" : "只记录信息"; LowLimitText = Convert.ToString(check["LowLimit"], CultureInfo.InvariantCulture); HighLimitText = Convert.ToString(check["HighLimit"], CultureInfo.InvariantCulture); CompareText = Convert.ToString(check["Comtype"], CultureInfo.InvariantCulture); UnitText = Convert.ToString(check["Unit"], CultureInfo.InvariantCulture); ExpectedText = Convert.ToString(check["Limit"], CultureInfo.InvariantCulture); }
        private static bool IsString(string type) { string value = (type ?? string.Empty).ToLowerInvariant(); return value.Contains("string") || value.Contains("char"); } private static double Parse(string text, double fallback) { double value; return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback; }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged; private void Raise(string name) { System.ComponentModel.PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new System.ComponentModel.PropertyChangedEventArgs(name)); }
    }
    internal sealed class DbcSignalEditRow : INotifyPropertyChanged { private bool _use; private string _valueText; public DbcSignalEditRow(DbcSignalDefinition signal, bool use, string value) { Signal = signal; _use = use; _valueText = value; } public DbcSignalDefinition Signal { get; private set; } public bool Use { get { return _use; } set { if (_use == value) return; _use = value; Raise("Use"); } } public string Name { get { return Signal.Name; } } public string ValueText { get { return _valueText; } set { if (_valueText == value) return; _valueText = value ?? string.Empty; Raise("ValueText"); } } public string Unit { get { return Signal.Unit; } } public string RawRange { get { return Signal.Signed ? "signed " + Signal.BitLength : "0.." + (Signal.BitLength >= 63 ? "2^" + Signal.BitLength : ((1L << Signal.BitLength) - 1).ToString(CultureInfo.InvariantCulture)); } } public string EnumText { get { return string.Join("; ", Signal.ValueDescriptions.Take(6).Select(pair => pair.Key + "=" + pair.Value)); } } public DbcSignalEditRow CloneForEditing() { return new DbcSignalEditRow(Signal, Use, ValueText); } public void CopyConfigurationFrom(DbcSignalEditRow source) { if (source == null) return; Use = source.Use; ValueText = source.ValueText; } public event PropertyChangedEventHandler PropertyChanged; private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); } }
}
