using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using ManualCanDebug.Core;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    public sealed partial class MainWindow : Window
    {
        private readonly CanDebugService _service;
        private IAdvancedCanService _advancedCanService;
        private readonly BorderlessWindowSizing _borderlessWindowSizing;
        private LegacySequenceRuntime _legacyRuntime;
        private TextBlock _legacyRuntimeStatusText;
        private TextBlock _workspaceStatusIcon;
        private Button _initializeAllInstrumentsButton;
        private Button _safeShutdownButton;
        private TextBlock _productStatusText;
        private TextBlock _resolverStatusText;
        private TextBlock _auxiliaryStatusText;
        private ComboBox _productModelComboBox;
        private TextBlock _productSelectorLabel;
        private ComboBox _workModeComboBox;
        private bool _advancedManualMode;
        private MenuItem _editModeMenuItem;
        private MenuItem _debugModeMenuItem;
        private MenuItem _initializeWorkspaceMenuItem;
        private MenuItem _safeShutdownMenuItem;
        private Separator _runDebugSeparator;
        private TabControl _mainTabs;
        private TabItem _c92ReadTab;
        private TabItem _c92ControlTab;
        private TabItem _c96ReadTab;
        private TabItem _c96ControlTab;
        private TabItem _auxiliaryTab;
        private TabItem _sequenceTab;
        private TabItem _productCanTab;
        private TabItem _c91ReadTab;
        private TabItem _resolverTab;
        private TabItem _instrumentCenterTab;
        private InstrumentCenterPanel _instrumentCenterPanel;
        private ProductLocatorRepository _productLocatorRepository;
        private TabItem _functionBlockStudioTab;
        private TabItem _studioFlowTab;
        private TabItem _advancedToolsTab;
        private TabControl _advancedTabs;
        private FunctionBlockStudioPanel _functionBlockStudioPanel;
        private StudioFlowEditorPanel _studioFlowEditorPanel;
        private ContentControl _studioWorkspaceHost;
        private bool _studioBlockMode;
        private string _studioReturnFlowInstanceId;
        private FctStudioProject _studioProject;
        private string _studioProjectPath;
        private string _loadedSequencePath;
        private bool _studioProjectDirty;
        private FctStudioCompileResult _studioDebugCompile;
        private HashSet<int> _studioBreakpointIndexes = new HashSet<int>();
        private int _studioDebugNextIndex;
        private bool _studioDebugActive;
        private IReadOnlyList<SequenceStepDefinition> _atomicCatalogSteps;
        private readonly Stack<string> _studioUndo = new Stack<string>();
        private readonly Stack<string> _studioRedo = new Stack<string>();
        private string _lastStudioSnapshot;
        private bool _restoringStudioHistory;
        private readonly Stack<StudioNavigationState> _studioNavigationBack = new Stack<StudioNavigationState>();
        private bool _restoringStudioNavigation;
        private Button _navigationBackButton;
        private ListBox _sequenceList;
        private TextBlock _selectedStepText;
        private TextBox _workflowStepNameTextBox;
        private ComboBox _workflowRunModeComboBox;
        private CheckBox _workflowRecordingLogCheckBox;
        private TextBlock _workflowSupportText;
        private DataGrid _workflowParameterGrid;
        private ObservableCollection<WorkflowParameterRow> _workflowParameters;
        private ObservableCollection<WorkflowStepState> _workflowSteps;
        private SequenceDocument _sequenceDocument;
        private WorkflowStepState _selectedWorkflowStep;
        private CancellationTokenSource _workflowCancellation;
        private bool _workflowRunning;
        private TextBox _readAddressOffsetTextBox;
        private TextBox _readTableIndexTextBox;
        private TextBox _readDataSizeTextBox;
        private TextBox _productSignalNameTextBox;
        private TextBox _productSignalValueTextBox;
        private CheckBox _productSignalSendFlagCheckBox;
        private TextBox _productRawIdTextBox;
        private TextBox _productRawDataTextBox;
        private TextBox _productReceiveIdTextBox;
        private TextBox _resolverSpeedTextBox;
        private TextBox _resolverPositionTextBox;
        private TextBox _resolverPolePairsTextBox;
        private TextBox _resolverSignalNameTextBox;
        private TextBox _resolverSignalValueTextBox;
        private CheckBox _resolverSignalSendFlagCheckBox;
        private TextBox _logTextBox;
        private Border _logPanel;
        private RowDefinition _logRowDefinition;
        private TextBlock _applicationStatusText;
        private TextBlock _sequenceSummaryText;
        private TextBlock _productSummaryText;
        private TextBlock _currentFileText;
        private TextBlock _headerProductText;
        private TextBlock _headerSequenceText;
        private Border _headerDirtyBadge;
        private TextBlock _headerDirtyText;
        private TextBlock _headerSavePathText;
        private TextBox _sequenceSearchTextBox;
        private bool _logVisible;
        private Button _maximizeWindowButton;
        private Button _c95InputsButton;
        private Button _c95TablesButton;
        private Button _productResolverButton;
        private C96AuxiliaryPanel _auxiliaryPanel;
        private C96ReadPanel _c92ReadPanel;
        private C96ReadPanel _c96ReadPanel;

        public MainWindow()
        {
            Title = "FCT Engineering Studio - 产品调试与SEQ开发工具";
            Width = 1560;
            Height = 960;
            MinWidth = 1180;
            MinHeight = 760;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Maximized;
            Background = NewBrush(246, 248, 252);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(7),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
            _borderlessWindowSizing = BorderlessWindowSizing.Attach(this);

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _service = new CanDebugService(
                baseDirectory,
                Path.Combine(baseDirectory, "Config", "Flywheel_900A_Z405.dbc"),
                Path.Combine(baseDirectory, "Config", "Resolver.dbc"),
                Path.Combine(baseDirectory, "Config", "C95C96Auxiliary.dbc"));
            _service.Log += Service_Log;

            _advancedCanService = new MainTestAdvancedCanService(
                ExecuteInstrumentStepAsync,
                () => _legacyRuntime == null ? null : _legacyRuntime.LastStepExecution,
                () => _service.ProductProfile,
                name => _legacyRuntime != null && _legacyRuntime.InstrumentsInitialized && _legacyRuntime.InitializedInstrumentNames.Contains(name),
                Path.Combine(baseDirectory, "Config", "C95C96Auxiliary.dbc"),
                Service_Log);

            ActionCatalog.Configure(baseDirectory);
            GlobalModuleLibraryService.Configure(baseDirectory);
            BuildUserInterface();
            _productLocatorRepository = new ProductLocatorRepository(baseDirectory, Service_Log);
            LoadSequenceFromFile(Path.Combine(baseDirectory, "Config", "DefaultSequence.json"), false);
            _atomicCatalogSteps = _sequenceDocument.Steps.Select(SequenceEditing.Clone).Concat(GenericStepCatalog.CreateTemplates().Select(SequenceEditing.Clone)).ToList().AsReadOnly();
            _instrumentCenterPanel.RefreshTemplates(_atomicCatalogSteps);
            InitializeStudioProject();
            CreateLegacyRuntime();
            Service_Log("当前产品型号：C95；产品高压读取映射为 FT_Analog_Inputs / HVDC_SENSE_AI，表0x00，字节44 (0x2C)。");
            Closing += Window_Closing;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            StateChanged += MainWindow_StateChanged;
        }

        private void BuildUserInterface()
        {
            ApplyProfessionalTheme();
            Grid root = new Grid { Background = NewBrush(242, 245, 249) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _logRowDefinition = new RowDefinition { Height = new GridLength(0) };
            root.RowDefinitions.Add(_logRowDefinition);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });

            UIElement titleBar = BuildCustomTitleBar();
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            Menu menu = BuildMainMenu();
            Grid.SetRow(menu, 1);
            root.Children.Add(menu);

            UIElement commandBar = BuildConnectionPanel();
            Grid.SetRow(commandBar, 2);
            root.Children.Add(commandBar);

            _mainTabs = new TabControl { Margin = new Thickness(5, 4, 5, 5), Background = NewBrush(242, 245, 249), BorderThickness = new Thickness(0), ItemContainerStyle = StudioTabStyleFactory.Create(12) };
            _c92ReadPanel = new C96ReadPanel(_advancedCanService, SelectC92, ProductModel.C92); _c92ReadTab = new TabItem { Header = "C92 读取", Content = _c92ReadPanel };
            _c92ControlTab = new TabItem { Header = "C92 控制", Content = new C96ControlPanel(_advancedCanService, SelectC92, ProductModel.C92) };
            _c96ReadPanel = new C96ReadPanel(_advancedCanService, SelectC96, ProductModel.C96); _c96ReadTab = new TabItem { Header = "C96 读取", Content = _c96ReadPanel };
            _c96ControlTab = new TabItem { Header = "C96 控制", Content = new C96ControlPanel(_advancedCanService, SelectC96, ProductModel.C96) };
            _auxiliaryPanel = new C96AuxiliaryPanel(_advancedCanService, EnsureAuxiliaryProduct); _auxiliaryTab = new TabItem { Header = "C95/C96 DCDC/辅驱", Content = _auxiliaryPanel };
            _studioWorkspaceHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            _studioFlowTab = new TabItem { Header = BuildWorkspaceTabHeader("\uE768", "序列调试与编辑"), Content = _studioWorkspaceHost };
            _functionBlockStudioTab = new TabItem { Header = BuildWorkspaceTabHeader("\uE8F1", "自定义功能块"), Visibility = Visibility.Collapsed };
            _sequenceTab = new TabItem { Header = "原始SEQ明细", Content = BuildSequencePanel() };
            _productCanTab = new TabItem { Header = "产品 CAN", Content = BuildProductPanel() };
            _c91ReadTab = new TabItem { Header = "C91 读取", Content = BuildC91ReadPanel() };
            _resolverTab = new TabItem { Header = "旋变 CAN", Content = BuildResolverPanel() };
            _instrumentCenterPanel = new InstrumentCenterPanel(AppDomain.CurrentDomain.BaseDirectory, ExecuteInstrumentStepAsync, Service_Log, InstrumentConfigurationSaved, InitializeSelectedInstrumentsAsync);
            _instrumentCenterTab = new TabItem { Header = "仪器中心", Content = _instrumentCenterPanel };
            _advancedTabs = new TabControl();
            foreach (TabItem tab in new[] { _instrumentCenterTab, _sequenceTab, _productCanTab, _resolverTab, _c91ReadTab, _c92ReadTab, _c92ControlTab, _c96ReadTab, _c96ControlTab, _auxiliaryTab })
                _advancedTabs.Items.Add(tab);
            _advancedToolsTab = new TabItem { Header = "高级工具", Content = _advancedTabs, Visibility = Visibility.Collapsed };
            foreach (TabItem tab in new[] { _studioFlowTab, _advancedToolsTab })
                _mainTabs.Items.Add(tab);
            _mainTabs.SelectedItem = _studioFlowTab;
            UpdateProductTabs(_service.ProductProfile.Model);
            Grid.SetRow(_mainTabs, 3);
            root.Children.Add(_mainTabs);

            _logPanel = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = Brushes.White,
                BorderBrush = NewBrush(210, 218, 229),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(12, 0, 12, 6),
                Padding = new Thickness(8)
            };
            DockPanel logDock = new DockPanel();
            DockPanel logHeader = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
            TextBlock logTitle = new TextBlock
            {
                Text = "运行日志",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = NewBrush(42, 53, 71),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel logButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button copyLog = MakeButton("复制", CopyLog_Click, 72);
            Button clearLog = MakeButton("清空", ClearLog_Click, 72);
            Button hideLog = MakeButton("收起", ToggleLog_Click, 72);
            logButtons.Children.Add(copyLog);
            logButtons.Children.Add(clearLog);
            logButtons.Children.Add(hideLog);
            DockPanel.SetDock(logButtons, Dock.Right);
            logHeader.Children.Add(logButtons);
            logHeader.Children.Add(logTitle);
            DockPanel.SetDock(logHeader, Dock.Top);
            _logTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = NewBrush(249, 251, 253),
                BorderBrush = NewBrush(220, 226, 234),
                Padding = new Thickness(8)
            };
            logDock.Children.Add(logHeader);
            logDock.Children.Add(_logTextBox);
            _logPanel.Child = logDock;
            Grid.SetRow(_logPanel, 4);
            root.Children.Add(_logPanel);

            Content = root;
        }

        private UIElement BuildConnectionPanel()
        {
            Border shell = new Border
            {
                Background = Brushes.White,
                BorderBrush = NewBrush(210, 218, 229),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 7, 10, 7)
            };
            DockPanel panel = new DockPanel();
            StackPanel selectorPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock appMark = new TextBlock
            {
                Text = "FCT",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = NewBrush(28, 92, 171),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            selectorPanel.Children.Add(appMark);
            _navigationBackButton = new Button { Content = "←  返回", Width = 82, Height = 32, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(8, 3, 8, 3), Background = NewBrush(247, 250, 254), BorderBrush = NewBrush(199, 213, 232), BorderThickness = new Thickness(1), Foreground = NewBrush(48, 72, 106), FontSize = 12.5, FontWeight = FontWeights.SemiBold, IsEnabled = false, ToolTip = "返回上一个操作界面（Alt+←），不撤销任何数据" }; _navigationBackButton.Click += NavigateBack_Click; selectorPanel.Children.Add(_navigationBackButton);
            Border contextDivider = new Border { Width = 1, Height = 28, Background = NewBrush(210, 220, 232), Margin = new Thickness(0, 0, 14, 0) }; selectorPanel.Children.Add(contextDivider);
            StackPanel productContext = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            productContext.Children.Add(new TextBlock { Text = "当前产品", FontSize = 10, Foreground = NewBrush(108, 120, 138) });
            _headerProductText = new TextBlock { Text = "C95", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = NewBrush(28, 92, 171), ToolTip = "当前产品由打开的SEQ或新建工程确定，不能在编辑过程中手动切换" }; productContext.Children.Add(_headerProductText); selectorPanel.Children.Add(productContext);
            StackPanel sequenceContext = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 300 };
            sequenceContext.Children.Add(new TextBlock { Text = "当前SEQ", FontSize = 10, Foreground = NewBrush(108, 120, 138) });
            _headerSequenceText = new TextBlock { Text = "DefaultSequence.json", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = NewBrush(37, 49, 67), MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis }; sequenceContext.Children.Add(_headerSequenceText); selectorPanel.Children.Add(sequenceContext);
            _headerDirtyText = new TextBlock { Text = "已保存", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = NewBrush(34, 145, 82), VerticalAlignment = VerticalAlignment.Center };
            _headerDirtyBadge = new Border { Child = _headerDirtyText, Background = NewBrush(241, 251, 245), BorderBrush = NewBrush(42, 160, 91), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; selectorPanel.Children.Add(_headerDirtyBadge);
            _headerSavePathText = new TextBlock { FontSize = 11, Foreground = NewBrush(93, 106, 125), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 14, 0), Visibility = Visibility.Collapsed }; selectorPanel.Children.Add(_headerSavePathText);
            _productSelectorLabel = MakeFieldLabel("产品"); selectorPanel.Children.Add(_productSelectorLabel);
            _productModelComboBox = new ComboBox
            {
                Width = 175,
                Margin = new Thickness(5, 0, 14, 0),
                ItemsSource = new[]
                {
                    ProductCanProfile.For(ProductModel.C95),
                    ProductCanProfile.For(ProductModel.C91),
                    ProductCanProfile.For(ProductModel.C92),
                    ProductCanProfile.For(ProductModel.C96)
                },
                SelectedIndex = 0,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Focusable = false
            };
            _productModelComboBox.SelectionChanged += ProductModel_SelectionChanged;
            selectorPanel.Children.Add(_productModelComboBox);
            _workModeComboBox = new ComboBox
            {
                Width = 165,
                Margin = new Thickness(5, 0, 8, 0),
                ItemsSource = new[] { "编辑模式", "调试模式" },
                SelectedIndex = 0
            };
            _workModeComboBox.SelectionChanged += WorkMode_SelectionChanged;
            UpdateRunModeMenuChecks();
            DockPanel.SetDock(selectorPanel, Dock.Left);
            panel.Children.Add(selectorPanel);

            WrapPanel actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _productStatusText = MakeStatusText("未连接");
            _resolverStatusText = MakeStatusText("未连接");
            _auxiliaryStatusText = MakeStatusText("未连接");

            _legacyRuntimeStatusText = MakeStatusText("工作区未初始化"); _legacyRuntimeStatusText.FontSize = 13; _legacyRuntimeStatusText.FontWeight = FontWeights.Normal; _legacyRuntimeStatusText.Foreground = NewBrush(75, 83, 96);
            StackPanel platformBlock = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            StackPanel platformText = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            _workspaceStatusIcon = new TextBlock { Text = "\uE7BA", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = NewBrush(238, 142, 25), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            platformText.Children.Add(_workspaceStatusIcon); platformText.Children.Add(_legacyRuntimeStatusText);
            platformBlock.Children.Add(platformText);
            _initializeAllInstrumentsButton = MakePrimaryButton("初始化当前工作区", InitializeCurrentWorkspace_Click, 142); _initializeAllInstrumentsButton.Height = 34; _initializeAllInstrumentsButton.Margin = new Thickness(4, 0, 6, 0); _initializeAllInstrumentsButton.FontWeight = FontWeights.SemiBold;
            _initializeAllInstrumentsButton.ToolTip = "按仪器中心“本次初始化”的勾选，通过MainTest统一初始化当前项目所需仪器。";
            platformBlock.Children.Add(_initializeAllInstrumentsButton);
            _safeShutdownButton = MakeDangerButton("安全下电", SafeShutdown_Click, 104); _safeShutdownButton.Height = 34; _safeShutdownButton.Margin = new Thickness(4, 0, 0, 0); _safeShutdownButton.Background = Brushes.White; _safeShutdownButton.BorderBrush = NewBrush(218, 48, 55); _safeShutdownButton.BorderThickness = new Thickness(1); _safeShutdownButton.Foreground = NewBrush(218, 48, 55); StackPanel safeContent = new StackPanel { Orientation = Orientation.Horizontal }; safeContent.Children.Add(new TextBlock { Text = "\uEA18", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }); safeContent.Children.Add(new TextBlock { Text = "安全下电", FontSize = 13, VerticalAlignment = VerticalAlignment.Center }); _safeShutdownButton.Content = safeContent;
            platformBlock.Children.Add(_safeShutdownButton);
            actions.Children.Add(platformBlock);
            DockPanel.SetDock(actions, Dock.Right);
            panel.Children.Add(actions);
            shell.Child = panel;
            return shell;
        }

        private static StackPanel BuildWorkspaceTabHeader(string glyph, string text)
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private UIElement BuildSequencePanel()
        {
            Grid grid = new Grid { Margin = new Thickness(8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border listCard = MakeCard(new Thickness(0, 0, 8, 0));
            DockPanel listDock = new DockPanel();
            StackPanel listHeader = new StackPanel { Margin = new Thickness(4, 2, 4, 8) };
            DockPanel titleRow = new DockPanel();
            _sequenceSummaryText = new TextBlock { Text = "0 STEP", Foreground = NewBrush(94, 106, 122), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_sequenceSummaryText, Dock.Right);
            titleRow.Children.Add(_sequenceSummaryText);
            titleRow.Children.Add(new TextBlock { Text = "测试流程", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = NewBrush(36, 48, 65) });
            listHeader.Children.Add(titleRow);
            _sequenceSearchTextBox = new TextBox { Margin = new Thickness(0, 8, 0, 0), ToolTip = "按步骤名称或函数名筛选", Padding = new Thickness(8, 5, 8, 5) };
            _sequenceSearchTextBox.TextChanged += SequenceSearchTextBox_TextChanged;
            listHeader.Children.Add(_sequenceSearchTextBox);
            DockPanel.SetDock(listHeader, Dock.Top);
            listDock.Children.Add(listHeader);
            TextBlock hint = new TextBlock { Text = "提示：单步只执行当前函数；整段运行包含原平台前处理、限值判断、跳转和安全收尾。", Foreground = NewBrush(112, 122, 136), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 2) };
            DockPanel.SetDock(hint, Dock.Bottom);
            _sequenceList = new ListBox
            {
                DisplayMemberPath = "DisplayText",
                BorderThickness = new Thickness(0),
                Background = NewBrush(249, 251, 253),
                Padding = new Thickness(2)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_sequenceList, ScrollBarVisibility.Auto);
            _sequenceList.SelectionChanged += SequenceList_SelectionChanged;
            listDock.Children.Add(hint);
            listDock.Children.Add(_sequenceList);
            listCard.Child = listDock;
            Grid.SetColumn(listCard, 0);
            grid.Children.Add(listCard);

            Border parameterCard = MakeCard(new Thickness(0));
            Grid parameterPanel = new Grid();
            parameterPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parameterPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parameterPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parameterPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            parameterPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parameterPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            DockPanel stepHeader = new DockPanel { Margin = new Thickness(4, 2, 4, 10) };
            TextBlock functionBadge = new TextBlock { Text = "STEP 参数", Foreground = NewBrush(28, 92, 171), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(functionBadge, Dock.Right);
            stepHeader.Children.Add(functionBadge);
            _selectedStepText = new TextBlock { Text = "未选择步骤", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = NewBrush(36, 48, 65) };
            stepHeader.Children.Add(_selectedStepText);
            Grid.SetRow(stepHeader, 0);
            parameterPanel.Children.Add(stepHeader);

            WrapPanel row = new WrapPanel();
            row.Margin = new Thickness(0, 0, 0, 8);
            row.Children.Add(MakeFieldLabel("测试项目"));
            _workflowStepNameTextBox = MakeBox("", 390); row.Children.Add(_workflowStepNameTextBox);
            row.Children.Add(MakeFieldLabel("运行模式"));
            _workflowRunModeComboBox = new ComboBox { Width = 110, Margin = new Thickness(5, 3, 12, 3), ItemsSource = new[] { "Normal", "Skip", "Break" }, SelectedIndex = 0 };
            row.Children.Add(_workflowRunModeComboBox);
            _workflowRecordingLogCheckBox = new CheckBox { Content = "记录日志", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 3, 8, 3) };
            row.Children.Add(_workflowRecordingLogCheckBox);
            Grid.SetRow(row, 1);
            parameterPanel.Children.Add(row);

            _workflowSupportText = new TextBlock
            {
                Margin = new Thickness(4, 0, 4, 8),
                Padding = new Thickness(10, 7, 10, 7),
                Background = NewBrush(238, 247, 255),
                Foreground = NewBrush(38, 92, 145),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_workflowSupportText, 2);
            parameterPanel.Children.Add(_workflowSupportText);

            _workflowParameters = new ObservableCollection<WorkflowParameterRow>();
            _workflowParameterGrid = new DataGrid
            {
                ItemsSource = _workflowParameters,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                MinHeight = 240,
                Margin = new Thickness(4, 0, 4, 10),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = NewBrush(248, 250, 253),
                RowHeaderWidth = 0
            };
            _workflowParameterGrid.Columns.Add(new DataGridTextColumn { Header = "参数名称", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
            _workflowParameterGrid.Columns.Add(new DataGridTextColumn { Header = "当前值（可编辑）", Binding = new Binding("ValueText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _workflowParameterGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("TypeName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            Grid.SetRow(_workflowParameterGrid, 3);
            parameterPanel.Children.Add(_workflowParameterGrid);

            WrapPanel runButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            runButtons.Children.Add(MakePrimaryButton("▶ 运行选中", ExecuteSequence_Click, 125));
            runButtons.Children.Add(MakeButton("从这里运行", RunFromCurrent_Click, 115));
            runButtons.Children.Add(MakeSuccessButton("运行全部  F5", RunAllWorkflow_Click, 120));
            runButtons.Children.Add(MakeDangerButton("■ 停止", StopWorkflow_Click, 95));
            runButtons.Children.Add(MakeButton("导入SEQ", ImportSequence_Click, 92));
            runButtons.Children.Add(MakeButton("保存SEQ", SaveStudioProject_Click, 92));
            runButtons.Children.Add(MakeButton("＋ 测试项库", OpenTestItemLibrary_Click, 105));
            runButtons.Children.Add(MakeButton("从SEQ导入项", ImportStepsFromSequence_Click, 110));
            runButtons.Children.Add(MakeButton("复制", DuplicateSelectedStep_Click, 68));
            runButtons.Children.Add(MakeButton("删除", DeleteSelectedStep_Click, 68));
            runButtons.Children.Add(MakeButton("↑", MoveStepUp_Click, 42));
            runButtons.Children.Add(MakeButton("↓", MoveStepDown_Click, 42));
            Grid.SetRow(runButtons, 4);
            parameterPanel.Children.Add(runButtons);
            TextBlock footer = new TextBlock
            {
                Text = "执行范围：RES、LVDC、HVDC、DMM、DAQ、MOXA、继电器、PLC、产品CAN、旋变CAN。运行前请先在顶部初始化原平台仪器。",
                Foreground = NewBrush(108, 118, 132),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 2, 4, 0)
            };
            Grid.SetRow(footer, 5);
            parameterPanel.Children.Add(footer);
            parameterCard.Child = parameterPanel;
            Grid.SetColumn(parameterCard, 1);
            grid.Children.Add(parameterCard);
            return grid;
        }

        private UIElement BuildProductPanel()
        {
            StackPanel stack = new StackPanel();
            stack.Children.Add(MakeGroup("产品通信基础动作", MakeRow(
                MakeButton("进入 FT 模式", EnterFtMode_Click),
                MakeButton("DUT 通信初始化", InitializeDut_Click),
                MakeButton("CAN 通信测试", TestProductCommunication_Click),
                MakeButton("发送唤醒帧 0x50F", SendWakeup_Click))));

            stack.Children.Add(MakeGroup("产品内部电流与状态（无 DAQ、无自动判定）", MakeRow(
                MakeButton("读取产品三相电流 / RMS", ReadProductCurrent_Click, 200),
                MakeLabel("读取 Current Sense 结果及 Motor Status；TX/RX 与具体值同步写入 LOG。", Brushes.Gray))));

            _c95InputsButton = MakeButton("读取C95全部输入信号", ReadAllC95Inputs_Click, 210);
            stack.Children.Add(MakeGroup("C95 Input Tables 全页核对", MakeRow(
                _c95InputsButton,
                MakeLabel("整块读取 0x00 / 0x0C / 0x18 / 0x20 / 0x2C，共189个信号，可筛选和复制。", Brushes.Gray))));

            _c95TablesButton = MakeButton("读取C95所有表数据", ReadAllC95Tables_Click, 210);
            stack.Children.Add(MakeGroup("C95 Locator 所有地址表", MakeRow(
                _c95TablesButton,
                MakeLabel("按地址表读取0x00到0xA8全部43项；单项失败继续，MPI按二级指针读取。", Brushes.Gray))));

            _productResolverButton = MakeButton("读取并解析C95产品旋变", ReadProductResolver_Click, 210);
            stack.Children.Add(MakeGroup("产品内部旋变数据（随型号切换）", MakeRow(
                _productResolverButton,
                MakeLabel("C91读取0x48共8字节；C95读取0x44共9字节。显示完整报文和解析值。", Brushes.Gray))));

            _readAddressOffsetTextBox = MakeBox("0x44", 100);
            _readTableIndexTextBox = MakeBox("4", 90);
            _readDataSizeTextBox = MakeBox("4", 80);
            stack.Children.Add(MakeGroup("DUT 内存参数读取（SEQ CAN_ReadSignalValue 逻辑）", MakeRow(
                MakeLabel("AddrOffset："), _readAddressOffsetTextBox,
                MakeLabel("TableIndex："), _readTableIndexTextBox,
                MakeLabel("DataSize："), _readDataSizeTextBox,
                MakeButton("读取 DUT 参数", ReadDutValue_Click))));

            _productSignalNameTextBox = MakeBox("s00_mcuEnable_1", 280);
            _productSignalValueTextBox = MakeBox("1", 100);
            _productSignalSendFlagCheckBox = new CheckBox { Content = "send flag", IsChecked = true, Margin = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(MakeGroup("产品 DBC 信号发送", MakeRow(
                MakeLabel("信号名："), _productSignalNameTextBox,
                MakeLabel("值："), _productSignalValueTextBox,
                _productSignalSendFlagCheckBox,
                MakeButton("发送产品 DBC 信号", SendProductSignal_Click))));

            _productRawIdTextBox = MakeBox("0x7EE", 100);
            _productRawDataTextBox = MakeBox("02 02 02 02 02 02 02 02", 330);
            _productReceiveIdTextBox = MakeBox("0x7EF", 100);
            stack.Children.Add(MakeGroup("产品原始 CAN 帧", MakeRow(
                MakeLabel("发送 ID："), _productRawIdTextBox,
                MakeLabel("数据："), _productRawDataTextBox,
                MakeButton("发送原始帧", SendProductRaw_Click),
                MakeLabel("接收 ID："), _productReceiveIdTextBox,
                MakeButton("读取接收帧", ReceiveProductRaw_Click))));
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        }

        private UIElement BuildResolverPanel()
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(5) };
            StackPanel actions = new StackPanel();
            WrapPanel row = new WrapPanel();
            row.Children.Add(MakeButton("旋变初始化", InitializeResolver_Click));
            row.Children.Add(MakeLabel("极对数："));
            _resolverPolePairsTextBox = MakeBox("6", 70);
            row.Children.Add(_resolverPolePairsTextBox);
            row.Children.Add(MakeButton("设置极对数", SetResolverPolePairs_Click));
            row.Children.Add(MakeLabel("转速："));
            _resolverSpeedTextBox = MakeBox("700", 100);
            row.Children.Add(_resolverSpeedTextBox);
            row.Children.Add(MakeButton("设置转速", SetResolverSpeed_Click));
            row.Children.Add(MakeButton("700 RPM", Resolver700_Click));
            row.Children.Add(MakeButton("3500 RPM", Resolver3500_Click));
            row.Children.Add(MakeButton("7000 RPM", Resolver7000_Click));
            actions.Children.Add(row);
            row = new WrapPanel();
            row.Children.Add(MakeLabel("位置："));
            _resolverPositionTextBox = MakeBox("225", 100);
            row.Children.Add(_resolverPositionTextBox);
            row.Children.Add(MakeButton("设置位置", SetResolverPosition_Click));
            row.Children.Add(MakeButton("位置 225", Resolver225_Click));
            row.Children.Add(MakeButton("位置 315", Resolver315_Click));
            row.Children.Add(MakeButton("停止旋变", StopResolver_Click));
            actions.Children.Add(row);
            stack.Children.Add(MakeGroup("旋变 SEQ 快捷动作", actions));

            _resolverSignalNameTextBox = MakeBox("2505419280_Speed", 280);
            _resolverSignalValueTextBox = MakeBox("0", 100);
            _resolverSignalSendFlagCheckBox = new CheckBox { Content = "send flag", IsChecked = true, Margin = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(MakeGroup("旋变 DBC 信号发送", MakeRow(
                MakeLabel("信号名："), _resolverSignalNameTextBox,
                MakeLabel("值："), _resolverSignalValueTextBox,
                _resolverSignalSendFlagCheckBox,
                MakeButton("发送旋变 DBC 信号", SendResolverSignal_Click))));
            return stack;
        }

        private UIElement BuildC91ReadPanel()
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(5) };
            stack.Children.Add(MakeGroup("C91型号准备", MakeRow(
                MakeButton("切换产品型号到C91", SwitchToC91_Click, 180),
                MakeLabel("切换后需要重新执行DUT通信初始化；FirstAddress和所有读取偏移都会改用C91配置。", Brushes.Gray))));
            stack.Children.Add(MakeGroup("C91 Locator 输入表", MakeRow(
                MakeButton("读取C91全部输入信号", ReadAllC91Inputs_Click, 210),
                MakeLabel("读取0x00/0x0C/0x18/0x20/0x2C五张表，共155个信号，显示解析值和RAW报文。", Brushes.Gray))));
            stack.Children.Add(MakeGroup("C91产品内部旋变", MakeRow(
                MakeButton("读取C91产品旋变", ReadC91Resolver_Click, 210),
                MakeLabel("读取FT_Resolver_Data 0x48共9字节：位置、速度/频率和故障状态。", Brushes.Gray))));
            stack.Children.Add(MakeGroup("C91电流与电机状态", MakeRow(
                MakeButton("读取C91三相电流/RMS", ReadC91Current_Click, 210),
                MakeButton("读取C91 Motor Status", ReadC91MotorStatus_Click, 210),
                MakeLabel("Current Cmd=0x70，Current Result=0x74，Motor Status=0x64；解析值和RAW同步写入LOG。", Brushes.Gray))));
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        }

        private void SwitchToC91_Click(object sender, RoutedEventArgs e)
        {
            _productModelComboBox.SelectedItem = ProductCanProfile.For(ProductModel.C91);
            Service_Log("已切换到C91；请重新执行DUT通信初始化后再读取。");
        }

        private bool RequireC91()
        {
            if (_service.ProductProfile.Model == ProductModel.C91) return true;
            MessageBox.Show(this, "请先在顶部选择C91，或点击本页的‘切换产品型号到C91’。", "C91读取", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void ReadAllC91Inputs_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireC91()) return;
            new C91InputTablesWindow(_advancedCanService.ReadAllC91InputTables) { Owner = this }.ShowDialog();
        }

        private void ReadC91Resolver_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireC91()) return;
            new ProductResolverWindow(_service.ProductProfile, _advancedCanService.ReadProductResolverData) { Owner = this }.ShowDialog();
        }

        private void ReadC91Current_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireC91()) return;
            ShowProductCurrentWindow(_service.LastRequestedCurrentRms);
        }

        private async void ReadC91MotorStatus_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireC91()) return;
            await RunActionAsync("读取C91 Motor Status", () =>
            {
                byte[] raw = _advancedCanService.ReadMotorStatus();
                MotorStatusInfo parsed = MotorStatusInfo.Parse(raw);
                Service_Log("C91 Motor Status RAW=" + HexDataParser.Format(raw) + "；" + parsed.Summary);
            });
        }

        private void ConnectProduct_Click(object sender, RoutedEventArgs e) { ShowMainTestConnectionState("DUTCAN", _productStatusText); }
        private void ConnectResolver_Click(object sender, RoutedEventArgs e) { ShowMainTestConnectionState("RESOLVERCAN", _resolverStatusText); }
        private void ConnectAuxiliary_Click(object sender, RoutedEventArgs e) { ShowMainTestConnectionState("AUXCAN", _auxiliaryStatusText); }

        private void ShowMainTestConnectionState(string instrument, TextBlock status)
        {
            bool ready = _legacyRuntime != null && _legacyRuntime.InstrumentsInitialized && _legacyRuntime.InitializedInstrumentNames.Contains(instrument);
            status.Text = ready ? "MainTest已连接" : "请在仪器中心初始化"; status.Foreground = ready ? Brushes.DarkGreen : Brushes.DarkOrange;
            if (!ready) { _advancedTabs.SelectedItem = _instrumentCenterTab; MessageBox.Show(this, "高级调试不再单独连接仪器。\n\n请在仪器中心勾选 " + instrument + "，然后点击“初始化当前工作区”。", "统一MainTest连接", MessageBoxButton.OK, MessageBoxImage.Information); }
        }

        private bool EnsureLegacyDiagnosticAccess()
        {
            if (_legacyRuntime != null && _legacyRuntime.InstrumentsInitialized) return true;
            MessageBox.Show(this, "高级调试现在统一通过MainTest执行。\n\n请先在仪器中心勾选需要的仪器，并点击顶部“初始化当前工作区”。", "MainTest尚未初始化", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void StopAdvancedDiagnosticActivities()
        {
            if (_auxiliaryPanel != null) _auxiliaryPanel.StopAllActivities();
            if (_c92ReadPanel != null) _c92ReadPanel.StopAllActivities();
            if (_c96ReadPanel != null) _c96ReadPanel.StopAllActivities();
            Service_Log("旧高级诊断的周期发送和自动接收已全部停止。");
        }

        private void SetAdvancedDiagnosticAvailability(bool available)
        {
            foreach (TabItem tab in new[] { _productCanTab, _resolverTab, _c91ReadTab, _c92ReadTab, _c92ControlTab, _c96ReadTab, _c96ControlTab, _auxiliaryTab }) if (tab != null) { tab.IsEnabled = true; tab.ToolTip = "统一通过MainTest STEP执行，不直接占用CAN卡"; }
        }

        private void CreateLegacyRuntime()
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                _legacyRuntime = new LegacySequenceRuntime(baseDirectory, Path.Combine(baseDirectory, "Config", "DefaultSequence.json"));
                _legacyRuntime.Log += Service_Log;
                _legacyRuntime.CurrentStepChanged += LegacyRuntime_CurrentStepChanged;
                UpdateLegacyRuntimeStatus("执行引擎已加载 / 仪器未初始化", Brushes.DarkOrange);
                if (_initializeAllInstrumentsButton != null) { _initializeAllInstrumentsButton.IsEnabled = true; _initializeAllInstrumentsButton.ToolTip = "按仪器中心“本次初始化”的勾选，通过MainTest统一初始化当前项目所需仪器。"; }
            }
            catch (Exception ex)
            {
                _legacyRuntime = null;
                UpdateLegacyRuntimeStatus("执行引擎加载失败", Brushes.DarkRed);
                if (_initializeAllInstrumentsButton != null) { _initializeAllInstrumentsButton.IsEnabled = false; _initializeAllInstrumentsButton.ToolTip = "MainTest执行引擎加载失败：" + ex.Message; }
                Service_Log("完整原平台执行引擎加载失败：" + ex);
            }
        }

        private void InstrumentConfigurationSaved()
        {
            if (_functionBlockStudioPanel != null) _functionBlockStudioPanel.ReloadActionCatalog();
            if (_instrumentCenterPanel != null && _atomicCatalogSteps != null) _instrumentCenterPanel.RefreshTemplates(_atomicCatalogSteps);
            if (_legacyRuntime == null) { Service_Log("仪器配置已保存，但MainTest执行引擎当前不可用；请关闭并重新启动工具。 "); return; }
            if (_legacyRuntime.InstrumentsInitialized) Service_Log("仪器配置已保存；当前已连接仪器不热切换，请安全下电后重新初始化。 ");
            else Service_Log("仪器配置与初始化选择已保存；MainTest实例保持不变，初始化时将直接使用最新Resource/Parameter。 ");
        }

        private async void InitializeCurrentWorkspace_Click(object sender, RoutedEventArgs e)
        {
            try { await _instrumentCenterPanel.InitializeSelectedAsync(); }
            catch (Exception ex) { MessageBox.Show(this, "初始化已选仪器失败：\n" + ex.Message, "初始化当前工作区", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task InitializeSelectedInstrumentsAsync(string instrumentsJson)
        {
            if (_legacyRuntime == null) throw new InvalidOperationException("原平台MainTest执行引擎没有加载成功。");
            if (_legacyRuntime.InstrumentsInitialized) throw new InvalidOperationException("仪器已经初始化。请先安全下电，再改变仪器中心的勾选。");
            JArray selected = JArray.Parse(instrumentsJson ?? "[]");
            string[] names = selected.OfType<JObject>().Select(item => ((string)item["Name"] ?? string.Empty).ToUpperInvariant()).Where(name => name.Length > 0).ToArray();
            if (names.Length == 0) throw new InvalidOperationException("请先在仪器中心勾选至少一个需要初始化的仪器。");
            _initializeAllInstrumentsButton.IsEnabled = false;
            UpdateLegacyRuntimeStatus("MainTest正在初始化：" + string.Join(" / ", names), Brushes.DarkOrange);
            try
            {
                StopAdvancedDiagnosticActivities();
                await _legacyRuntime.InitializeInstrumentsAsync(instrumentsJson);
                names = _legacyRuntime.InitializedInstrumentNames.ToArray();
                _instrumentCenterPanel.SetInitializedInstruments(names);
                bool dut = names.Contains("DUTCAN"), resolver = names.Contains("RESOLVERCAN"), auxiliary = names.Contains("AUXCAN");
                _productStatusText.Text = dut ? "MainTest已连接" : "未选择"; _productStatusText.Foreground = dut ? Brushes.DarkGreen : Brushes.DimGray;
                _resolverStatusText.Text = resolver ? "MainTest已连接" : "未选择"; _resolverStatusText.Foreground = resolver ? Brushes.DarkGreen : Brushes.DimGray;
                _auxiliaryStatusText.Text = auxiliary ? "MainTest已连接" : "未选择"; _auxiliaryStatusText.Foreground = auxiliary ? Brushes.DarkGreen : Brushes.DimGray;
                UpdateLegacyRuntimeStatus("MainTest已初始化：" + string.Join(" / ", names), Brushes.DarkGreen);
                SetAdvancedDiagnosticAvailability(false);
                Service_Log("仪器中心选择性初始化完成，后续立即执行和流程调试均使用同一个MainTest实例：" + string.Join(", ", names));
            }
            catch
            {
                SetAdvancedDiagnosticAvailability(true);
                UpdateLegacyRuntimeStatus("MainTest选择性初始化失败", Brushes.DarkRed);
                throw;
            }
            finally { _initializeAllInstrumentsButton.IsEnabled = true; }
        }

        private async void SafeShutdown_Click(object sender, RoutedEventArgs e)
        {
            _safeShutdownButton.IsEnabled = false;
            UpdateLegacyRuntimeStatus("正在安全下电...", Brushes.DarkOrange);
            try
            {
                if (_legacyRuntime != null) await _legacyRuntime.SafeShutdownAsync();
                _productStatusText.Text = "未连接";
                _resolverStatusText.Text = "未连接";
                _auxiliaryStatusText.Text = "未连接";
                if (_instrumentCenterPanel != null) _instrumentCenterPanel.SetInitializedInstruments(new string[0]);
                UpdateLegacyRuntimeStatus("未初始化", Brushes.DarkOrange);
                SetAdvancedDiagnosticAvailability(true);
                Service_Log("当前工作区已安全下电，所有CAN已断开。");
            }
            catch (Exception ex)
            {
                UpdateLegacyRuntimeStatus("安全下电失败", Brushes.DarkRed);
                Service_Log("安全下电失败：" + ex.Message);
                MessageBox.Show(this, "安全下电失败：\n" + ex.Message, "完整流程调试", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _safeShutdownButton.IsEnabled = true;
            }
        }

        private void UpdateLegacyRuntimeStatus(string text, Brush color)
        {
            if (_legacyRuntimeStatusText == null) return;
            bool ready = color == Brushes.DarkGreen;
            string display = text ?? string.Empty; if (display.IndexOf("未初始化", StringComparison.OrdinalIgnoreCase) >= 0 || display.IndexOf("加载中", StringComparison.OrdinalIgnoreCase) >= 0) display = "工作区未初始化"; else if (ready) display = "工作区已初始化";
            _legacyRuntimeStatusText.Text = display;
            _legacyRuntimeStatusText.Foreground = color;
            _legacyRuntimeStatusText.FontWeight = ready ? FontWeights.Bold : FontWeights.Normal;
            if (_workspaceStatusIcon != null) { _workspaceStatusIcon.Text = ready ? "\uE73E" : "\uE7BA"; _workspaceStatusIcon.Foreground = ready ? NewBrush(42, 160, 91) : NewBrush(238, 142, 25); }
        }

        private void ProductModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ProductCanProfile profile = _productModelComboBox.SelectedItem as ProductCanProfile;
            if (profile == null) return;
            _service.SetProductModel(profile.Model);
            if (_studioProject != null && !string.Equals(_studioProject.Product, profile.Model.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _studioProject.Product = profile.Model.ToString(); StudioFlowChanged();
                if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.RefreshProject();
                Service_Log("当前JSON SEQ产品已切换为 " + _studioProject.Product + "；流程检查会提示不兼容功能块。");
            }
            if (_productSummaryText != null) _productSummaryText.Text = "产品：" + profile.Model + " · " + profile.DisplayName;
            if (_readAddressOffsetTextBox != null)
                _readAddressOffsetTextBox.Text = "0x" + profile.ResolverDataOffset.ToString("X2", CultureInfo.InvariantCulture);
            if (_productResolverButton != null)
                _productResolverButton.Content = "读取并解析" + profile.Model + "产品旋变";
            if (_c95InputsButton != null)
                _c95InputsButton.IsEnabled = profile.SupportsLocatorPages;
            if (_c95TablesButton != null)
                _c95TablesButton.IsEnabled = profile.SupportsLocatorPages;
            UpdateProductTabs(profile.Model);
            UpdateCurrentFileDisplay();
        }

        private void UpdateProductTabs(ProductModel model)
        {
            if (_mainTabs == null) return;

            bool simpleMode = !_advancedManualMode;
            if (_productSelectorLabel != null) _productSelectorLabel.Visibility = Visibility.Collapsed; if (_productModelComboBox != null) _productModelComboBox.Visibility = Visibility.Collapsed;

            _functionBlockStudioTab.Visibility = Visibility.Collapsed;
            _studioFlowTab.Visibility = Visibility.Visible;
            _advancedToolsTab.Visibility = simpleMode ? Visibility.Collapsed : Visibility.Visible;
            _sequenceTab.Visibility = simpleMode ? Visibility.Collapsed : Visibility.Visible;
            _instrumentCenterTab.Visibility = Visibility.Visible;
            _productCanTab.Visibility = simpleMode ? Visibility.Collapsed : Visibility.Visible;
            _resolverTab.Visibility = simpleMode ? Visibility.Collapsed : Visibility.Visible;

            _c91ReadTab.Visibility = !simpleMode && model == ProductModel.C91 ? Visibility.Visible : Visibility.Collapsed;
            _c92ReadTab.Visibility = !simpleMode && model == ProductModel.C92 ? Visibility.Visible : Visibility.Collapsed;
            _c92ControlTab.Visibility = !simpleMode && model == ProductModel.C92 ? Visibility.Visible : Visibility.Collapsed;
            _c96ReadTab.Visibility = !simpleMode && model == ProductModel.C96 ? Visibility.Visible : Visibility.Collapsed;
            _c96ControlTab.Visibility = !simpleMode && model == ProductModel.C96 ? Visibility.Visible : Visibility.Collapsed;
            bool showAuxiliary = model == ProductModel.C95 || model == ProductModel.C96;
            _auxiliaryTab.Visibility = !simpleMode && showAuxiliary ? Visibility.Visible : Visibility.Collapsed;
            if (showAuxiliary) _auxiliaryTab.Header = model + " DCDC/辅驱";

            TabItem selected = _mainTabs.SelectedItem as TabItem;
            if (simpleMode)
            {
                if (selected == _advancedToolsTab || selected == null) ShowStudioFlowWorkspace(null);
                return;
            }
            _mainTabs.SelectedItem = _advancedToolsTab;
            if (model == ProductModel.C91) _advancedTabs.SelectedItem = _c91ReadTab;
            else if (model == ProductModel.C92) _advancedTabs.SelectedItem = _c92ReadTab;
            else if (model == ProductModel.C96) _advancedTabs.SelectedItem = _c96ReadTab;
            else _advancedTabs.SelectedItem = _productCanTab;
        }

        private void WorkMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_service == null) return;
            if ((_studioDebugActive || _workflowRunning) && _workModeComboBox.SelectedIndex != 1) { _workModeComboBox.SelectedIndex = 1; MessageBox.Show(this, "调试正在运行，请先停止并完成安全下电后再切换模式。", "切换运行模式", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _advancedManualMode = false;
            UpdateRunModeMenuChecks();
            ApplyStudioRunMode();
            UpdateProductTabs(_service.ProductProfile.Model);
            if (_workModeComboBox.SelectedIndex == 0)
            {
                UpdateLegacyRuntimeStatus("编辑模式", new SolidColorBrush(Color.FromRgb(75, 83, 96)));
                Service_Log("已切换到编辑模式；运行、单步、停止和立即试运行入口已隐藏。");
            }
            else if (_workModeComboBox.SelectedIndex == 1)
            {
                bool ready = _legacyRuntime != null && _legacyRuntime.InstrumentsInitialized;
                UpdateLegacyRuntimeStatus(ready ? "调试工作区已初始化" : "调试工作区：待初始化", ready ? Brushes.DarkGreen : Brushes.DarkOrange);
                Service_Log("已切换到调试模式；运行、单步、断点和立即试运行入口已显示。");
            }
        }

        private void OpenAdvancedTool(TabItem tab)
        {
            _workModeComboBox.SelectedIndex = 1; _advancedManualMode = true;
            UpdateProductTabs(_service.ProductProfile.Model);
            _mainTabs.SelectedItem = _advancedToolsTab;
            if (tab != null && tab.Visibility == Visibility.Visible) _advancedTabs.SelectedItem = tab;
        }

        private void ApplyStudioRunMode()
        {
            int mode = _workModeComboBox == null ? 0 : _workModeComboBox.SelectedIndex; bool debug = mode == 1, showRuntime = mode != 0;
            if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.SetDebugMode(debug);
            if (_functionBlockStudioPanel != null) _functionBlockStudioPanel.SetDebugMode(debug);
            if (_workspaceStatusIcon != null) _workspaceStatusIcon.Visibility = showRuntime ? Visibility.Visible : Visibility.Collapsed;
            if (_legacyRuntimeStatusText != null) _legacyRuntimeStatusText.Visibility = showRuntime ? Visibility.Visible : Visibility.Collapsed;
            if (_initializeAllInstrumentsButton != null) _initializeAllInstrumentsButton.Visibility = showRuntime ? Visibility.Visible : Visibility.Collapsed;
            if (_safeShutdownButton != null) _safeShutdownButton.Visibility = showRuntime ? Visibility.Visible : Visibility.Collapsed;
            if (_runDebugSeparator != null) _runDebugSeparator.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
            if (_initializeWorkspaceMenuItem != null) _initializeWorkspaceMenuItem.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
            if (_safeShutdownMenuItem != null) _safeShutdownMenuItem.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectStudioRunMode(int index) { if (_workModeComboBox != null) _workModeComboBox.SelectedIndex = index; UpdateRunModeMenuChecks(); }
        private void UpdateRunModeMenuChecks() { int index = _workModeComboBox == null ? 0 : _workModeComboBox.SelectedIndex; if (_editModeMenuItem != null) _editModeMenuItem.IsChecked = index == 0; if (_debugModeMenuItem != null) _debugModeMenuItem.IsChecked = index == 1; }

        private void SelectC96()
        {
            _productModelComboBox.SelectedItem = ProductCanProfile.For(ProductModel.C96);
            Service_Log("已切换到 C96 双驱产品；高级调试动作将通过MainTest执行。");
        }

        private void SelectC92()
        {
            _productModelComboBox.SelectedItem = ProductCanProfile.For(ProductModel.C92);
            Service_Log("已切换到 C92 双主驱产品；高级调试动作将通过MainTest执行。");
        }

        private bool EnsureAuxiliaryProduct()
        {
            if (!EnsureLegacyDiagnosticAccess()) return false;
            if (_service.ProductProfile.SupportsAuxiliary) return true;
            MessageBox.Show(this, "请先在顶部选择C95或C96。DCDC/辅驱功能不支持C91。", "DCDC/辅驱", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private async void DisconnectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_legacyRuntime != null) await _legacyRuntime.SafeShutdownAsync();
                _productStatusText.Text = "未连接";
                _productStatusText.Foreground = NewBrush(190, 59, 59);
                _resolverStatusText.Text = "未连接";
                _resolverStatusText.Foreground = NewBrush(190, 59, 59);
                _auxiliaryStatusText.Text = "未连接";
                _auxiliaryStatusText.Foreground = Brushes.DarkRed;
                if (_instrumentCenterPanel != null) _instrumentCenterPanel.SetInitializedInstruments(new string[0]);
                if (_legacyRuntime == null || !_legacyRuntime.InstrumentsInitialized)
                    UpdateLegacyRuntimeStatus(_advancedManualMode ? "高级手动工作区：待初始化" : "调试工作区：待初始化", Brushes.DarkOrange);
                Service_Log("已通过MainTest执行安全下电并断开全部仪器。");
            }
            catch (Exception ex) { Service_Log("断开失败：" + ex.Message); }
        }

        private void SequenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedWorkflowStep != null) TryCommitWorkflowStep(_selectedWorkflowStep);
            WorkflowStepState step = _sequenceList.SelectedItem as WorkflowStepState;
            if (step == null) return;
            _selectedWorkflowStep = step;
            _selectedStepText.Text = step.Name + "  [" + step.FunctionName + "]";
            _workflowStepNameTextBox.Text = step.Name;
            _workflowRunModeComboBox.SelectedItem = step.Definition.RunMode;
            if (_workflowRunModeComboBox.SelectedIndex < 0) _workflowRunModeComboBox.SelectedIndex = 0;
            _workflowRecordingLogCheckBox.IsChecked = step.Definition.RecordingLog;
            _workflowParameters.Clear();
            foreach (KeyValuePair<string, object> parameter in step.Definition.Parameters)
                _workflowParameters.Add(new WorkflowParameterRow(parameter.Key, parameter.Value));
            _workflowSupportText.Text = "原平台可执行；底层函数：" + step.FunctionName + "。本STEP只显示自己实际拥有的参数。";
            _workflowSupportText.Foreground = Brushes.DarkGreen;
        }

        private async void ExecuteSequence_Click(object sender, RoutedEventArgs e)
        {
            int index = _sequenceList.SelectedIndex;
            if (index >= 0) await RunWorkflowRangeAsync(index, index);
        }

        private async void RunFromCurrent_Click(object sender, RoutedEventArgs e)
        {
            int index = _sequenceList.SelectedIndex;
            if (index >= 0) await RunWorkflowRangeAsync(index, _workflowSteps.Count - 1);
        }

        private async void RunAllWorkflow_Click(object sender, RoutedEventArgs e)
        {
            await RunWorkflowRangeAsync(0, _workflowSteps.Count - 1);
        }

        private void StopWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (_workflowCancellation == null) return;
            _workflowCancellation.Cancel();
            if (_legacyRuntime != null) _legacyRuntime.Stop();
            Service_Log("已请求原平台停止调试流程。");
        }

        private async Task RunWorkflowRangeAsync(int startIndex, int endIndex)
        {
            if (_workflowRunning)
            {
                MessageBox.Show(this, "调试流程正在运行，请先停止。", "流程调试", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_workflowSteps == null || _workflowSteps.Count == 0) return;
            if (_selectedWorkflowStep != null && !TryCommitWorkflowStep(_selectedWorkflowStep)) return;
            if (_legacyRuntime == null)
            {
                MessageBox.Show(this, "原平台执行引擎没有加载成功，请查看运行日志。", "流程调试", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!_legacyRuntime.InstrumentsInitialized)
            {
                MessageBox.Show(this, "请先点击顶部“初始化全部仪器”。\n\n该操作会连接RES、产品CAN、旋变CAN、LVDC、HVDC、MOXA、DMM、继电器和PLC。", "流程调试", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            startIndex = Math.Max(0, startIndex);
            endIndex = Math.Min(_workflowSteps.Count - 1, endIndex);
            _workflowCancellation = new CancellationTokenSource();
            _workflowRunning = true;
            _productModelComboBox.IsEnabled = false;
            _workModeComboBox.IsEnabled = false;
            SetWorkflowParameterEditing(false);

            try
            {
                foreach (WorkflowStepState step in _workflowSteps.Skip(startIndex).Take(endIndex - startIndex + 1))
                    step.Status = "待运行";

                string runtimeSequencePath = WriteRuntimeSequence(startIndex, endIndex);
                if (startIndex == endIndex)
                {
                    WorkflowStepState step = _workflowSteps[startIndex];
                    step.Status = "运行中";
                    Service_Log(string.Format("原平台单步 {0}/{1}：{2} [{3}]", startIndex + 1, _workflowSteps.Count, step.Name, step.FunctionName));
                    string result = await _legacyRuntime.RunSingleStepAsync(runtimeSequencePath, 0);
                    step.Status = IsPassingStatus(result) ? "完成" : "失败";
                    Service_Log("原平台单步结果：" + (string.IsNullOrWhiteSpace(result) ? "<空>" : result));
                }
                else
                {
                    LegacyRunResult result = await _legacyRuntime.RunSequenceAsync(runtimeSequencePath, startIndex, _workflowCancellation.Token);
                    for (int index = startIndex; index <= endIndex; index++)
                    {
                        WorkflowStepState step = _workflowSteps[index];
                        if (step.Status == "运行中") step.Status = result.Cancelled ? "已停止" : "完成";
                        else if (step.Status == "待运行" && !result.Cancelled && index <= startIndex + result.LastStepIndex) step.Status = "完成";
                    }
                    Service_Log("原平台流程结果：" + (string.IsNullOrWhiteSpace(result.Status) ? "<未返回总状态>" : result.Status));
                }
            }
            catch (Exception ex)
            {
                int index = _sequenceList.SelectedIndex;
                if (index >= startIndex && index <= endIndex) _workflowSteps[index].Status = "失败";
                Service_Log("原平台流程执行失败：" + ex.Message);
                MessageBox.Show(this, "原平台流程执行失败：\n" + ex.Message, "流程调试", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                bool cancelled = _workflowCancellation.IsCancellationRequested;
                _workflowRunning = false;
                _workflowCancellation.Dispose();
                _workflowCancellation = null;
                _productModelComboBox.IsEnabled = true;
                _workModeComboBox.IsEnabled = true;
                SetWorkflowParameterEditing(true);
                Service_Log(cancelled ? "调试流程已停止。" : "调试流程运行结束。");
            }
        }

        private string WriteRuntimeSequence(int startIndex, int endIndex)
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugSequences");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, string.Format("Runtime_{0:yyyyMMdd_HHmmss_fff}_{1}_{2}.json", DateTime.Now, startIndex + 1, endIndex + 1));
            IEnumerable<SequenceStepDefinition> selected = _workflowSteps
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .Select(step => step.Definition);
            File.WriteAllText(path, _sequenceDocument.ToJson(selected), new UTF8Encoding(false));
            return path;
        }

        private async Task<string> ExecuteInstrumentStepAsync(SequenceStepDefinition step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (_legacyRuntime == null) throw new InvalidOperationException("原平台执行引擎没有加载成功。");
            if (!MainTestMethodCatalog.Contains(step.FunctionName)) throw new MissingMethodException("MainTest中没有找到STEP函数：" + step.FunctionName + "。请先按其他MainTest函数格式实现，再加入标准库。");
            if (!_legacyRuntime.InstrumentsInitialized) throw new InvalidOperationException("MainTest尚未执行ProcessSetup。请先在仪器中心勾选并初始化所需仪器。");
            string requiredInstrument = MainTestMethodCatalog.RequiredInstrument(step);
            if (!string.IsNullOrWhiteSpace(requiredInstrument))
            {
                HashSet<string> initialized = new HashSet<string>(_legacyRuntime.InitializedInstrumentNames, StringComparer.OrdinalIgnoreCase);
                string[] alternatives = requiredInstrument.Split('/');
                if (!alternatives.Any(initialized.Contains)) throw new InvalidOperationException("STEP “" + step.StepName + "” 已关联MainTest." + step.FunctionName + "，但依赖仪器 " + requiredInstrument + " 尚未初始化。请在仪器中心勾选后执行ProcessSetup。");
            }
            if (_workflowRunning || _legacyRuntime.IsRunning) throw new InvalidOperationException("整段流程正在占用仪器。请先停止流程，再执行实时仪器STEP。");
            if (_sequenceDocument == null) throw new InvalidOperationException("当前没有可用SEQ上下文。");
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugSequences");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "InstrumentLive_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
            File.WriteAllText(path, _sequenceDocument.ToJson(new[] { step }), new UTF8Encoding(false));
            return await _legacyRuntime.RunSingleStepAsync(path, 0);
        }

        private async Task<LegacyStepExecutionResult> RunMainTestAdvancedAsync(string stepName, string functionName, IDictionary<string, object> parameters)
        {
            if (!EnsureLegacyDiagnosticAccess()) return null;
            Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", stepName }, { "RunMode", "Normal" }, { "FunctionName", functionName }, { "RecordingLog", true } };
            if (parameters != null) foreach (KeyValuePair<string, object> pair in parameters) values[pair.Key] = pair.Value;
            try
            {
                SequenceStepDefinition step = new SequenceStepDefinition(values);
                string raw = await ExecuteInstrumentStepAsync(step);
                LegacyStepExecutionResult result = _legacyRuntime.LastStepExecution;
                Service_Log("MainTest高级调试完成：" + stepName + "；返回=" + (string.IsNullOrWhiteSpace(raw) ? "<空>" : raw));
                if (result != null && result.Results != null)
                    foreach (LegacyPlatformResultRow row in result.Results) Service_Log("平台结果：" + row.StepName + "；测试值=" + row.Value + "；下限=" + row.LimitsLow + "；上限=" + row.LimitsHigh + "；比较=" + row.LimitExpression + "；单位=" + row.Unit + "；结果=" + row.Status);
                return result;
            }
            catch (Exception ex)
            {
                Service_Log("MainTest高级调试失败：" + stepName + "；" + ex.Message);
                MessageBox.Show(this, stepName + "失败：\n" + ex.Message, "MainTest高级调试", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void LegacyRuntime_CurrentStepChanged(int originalIndex)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_workflowSteps == null || originalIndex < 0 || originalIndex >= _workflowSteps.Count) return;
                foreach (WorkflowStepState item in _workflowSteps.Where(step => step.Status == "运行中")) item.Status = "完成";
                WorkflowStepState current = _workflowSteps[originalIndex];
                current.Status = "运行中";
                _sequenceList.SelectedIndex = originalIndex;
                _sequenceList.ScrollIntoView(current);
            }));
        }

        private static bool IsPassingStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return true;
            return status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) < 0 &&
                   status.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0 &&
                   status.IndexOf("失败", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private bool TryCommitWorkflowStep(WorkflowStepState state)
        {
            if (state == null || _workflowRunning) return true;
            try
            {
                _workflowParameterGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                _workflowParameterGrid.CommitEdit(DataGridEditingUnit.Row, true);
                state.Definition.StepName = _workflowStepNameTextBox.Text.Trim();
                state.Definition.RunMode = Convert.ToString(_workflowRunModeComboBox.SelectedItem, CultureInfo.InvariantCulture) ?? "Normal";
                state.Definition.RecordingLog = _workflowRecordingLogCheckBox.IsChecked == true;
                foreach (WorkflowParameterRow parameter in _workflowParameters)
                    state.Definition.SetParameterFromText(parameter.Name, parameter.ValueText, parameter.OriginalType);
                state.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "参数保存失败：\n" + ex.Message, "流程参数", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void SetWorkflowParameterEditing(bool enabled)
        {
            _workflowStepNameTextBox.IsEnabled = enabled;
            _workflowRunModeComboBox.IsEnabled = enabled;
            _workflowRecordingLogCheckBox.IsEnabled = enabled;
            _workflowParameterGrid.IsReadOnly = !enabled;
        }

        private void ImportSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_workflowRunning)
            {
                MessageBox.Show(this, "请先停止调试流程。", "导入SEQ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ConfirmDiscardStudioChanges()) return;
            OpenFileDialog dialog = new OpenFileDialog { Title = "打开平台SEQ JSON并进入编辑", Filter = "平台SEQ JSON (*.json)|*.json|所有文件 (*.*)|*.*", InitialDirectory = PlatformSequenceDirectory() };
            if (dialog.ShowDialog(this) != true) return; LoadSequenceFromFile(dialog.FileName, false); if (_sequenceDocument == null || !string.Equals(_loadedSequencePath, dialog.FileName, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                string product = ResolveSequenceProduct(dialog.FileName); bool restored = FctStudioProjectService.TryLoadEditorState(dialog.FileName, out _studioProject); if (!restored) { bool c91 = string.Equals(product, "C91", StringComparison.OrdinalIgnoreCase); _studioProject = c91 ? C91SequenceProjectFactory.Create(_sequenceDocument) : CreateImportedSequenceProject(_sequenceDocument, product, Path.GetFileNameWithoutExtension(dialog.FileName)); } _studioProject.Product = string.IsNullOrWhiteSpace(_studioProject.Product) ? product : _studioProject.Product; product = _studioProject.Product; GlobalModuleLibraryService.MergeInto(_studioProject); _studioProjectPath = dialog.FileName; _loadedSequencePath = dialog.FileName; _studioProjectDirty = false; SelectProductContext(product); ResetStudioHistory(); _functionBlockStudioPanel.RefreshProject(); _studioFlowEditorPanel.RefreshProject(); ShowStudioFlowWorkspace(null); UpdateCurrentFileDisplay(); Service_Log("平台JSON SEQ已打开：" + dialog.FileName + "；识别产品=" + product + (restored ? "；已恢复完整编辑状态" : string.Empty)); MessageBox.Show(this, "SEQ已打开并进入编辑。\n\n产品：" + product + "\nSTEP：" + _sequenceDocument.Steps.Count + (restored ? "\n模块停用状态和编辑配置已恢复。" : string.Empty), "打开SEQ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, "JSON SEQ打开失败：\n" + ex.Message, "打开SEQ", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void NewStudioProject_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardStudioChanges()) return;
            NewStudioProjectWindow dialog = new NewStudioProjectWindow(_service.ProductProfile.Model, _productLocatorRepository.Products.Select(value => value.Product)) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            string product = dialog.SelectedProductName;
            _studioProject = FctStudioProjectService.CreateBlank(_sequenceDocument, product); GlobalModuleLibraryService.MergeInto(_studioProject);
            SelectProductContext(product);
            if (dialog.NewProduct != null) { ProductLocatorDefinition locator = _productLocatorRepository.Import(product, dialog.NewProduct.LocatorPath); _studioProject.ProductLocatorPath = "Config\\ProductLocators\\" + product + "_Locator.xlsx"; _studioProject.AuxiliaryDbcPath = ProductResourceService.ImportDbc(AppDomain.CurrentDomain.BaseDirectory, product, dialog.NewProduct.DbcPath); _studioProject.DriveStructure = dialog.NewProduct.DriveStructure; _studioProject.Capabilities = new List<string>(dialog.NewProduct.Capabilities); Service_Log("新产品资源已导入：" + product + "，Locator信号=" + locator.SignalCount + "，DBC=" + (string.IsNullOrWhiteSpace(_studioProject.AuxiliaryDbcPath) ? "未配置" : _studioProject.AuxiliaryDbcPath)); }
            string sequenceDirectory = PlatformSequenceDirectory(); _studioProject.ProjectName = dialog.SelectedSequenceName; _studioProjectPath = Path.Combine(sequenceDirectory, SafeFileName(dialog.SelectedSequenceName) + ".json"); _loadedSequencePath = _studioProjectPath;
            _studioProjectDirty = true;
            UpdateCurrentFileDisplay();
            ResetStudioHistory();
            _functionBlockStudioPanel.RefreshProject(); _studioFlowEditorPanel.RefreshProject(); ShowStudioBlockWorkspace(_functionBlockStudioPanel.SelectedBlockId, null);
            SaveStudioProject_Click(this, new RoutedEventArgs()); Service_Log("已新建 " + product + " 空白JSON SEQ：" + _studioProjectPath);
        }
        private static FctStudioProject CreateImportedSequenceProject(SequenceDocument sequence, string product, string displayName)
        {
            FctStudioProject project = FctStudioProjectService.CreateBlank(sequence, product);
            project.ProjectName = string.IsNullOrWhiteSpace(displayName) ? "Imported SEQ" : displayName;
            List<Tuple<string, string, List<SequenceStepDefinition>>> groups = new List<Tuple<string, string, List<SequenceStepDefinition>>>();
            foreach (SequenceStepDefinition source in sequence.Steps)
            {
                string stepName = source.StepName ?? string.Empty;
                string key, title, localName;
                ResolveImportedStepGroup(stepName, out key, out title, out localName);
                Tuple<string, string, List<SequenceStepDefinition>> group = groups.FirstOrDefault(value => value.Item1 == key);
                if (group == null) { group = Tuple.Create(key, title, new List<SequenceStepDefinition>()); groups.Add(group); }
                SequenceStepDefinition step = SequenceEditing.Clone(source); step.StepName = localName; group.Item3.Add(step);
            }
            Dictionary<string, FunctionBlockDefinition> parameterizedCurrentBlocks = new Dictionary<string, FunctionBlockDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (Tuple<string, string, List<SequenceStepDefinition>> group in groups)
            {
                string currentDrive; double targetCurrent;
                if (TryParseCurrentModule(group.Item2, out currentDrive, out targetCurrent))
                {
                    FunctionBlockDefinition currentBlock;
                    if (!parameterizedCurrentBlocks.TryGetValue(currentDrive, out currentBlock))
                    {
                        currentBlock = BuildParameterizedCurrentBlock(group.Item3, currentDrive, targetCurrent, product); parameterizedCurrentBlocks[currentDrive] = currentBlock; project.Blocks.Add(currentBlock);
                    }
                    double low = targetCurrent == 0 ? -10 : targetCurrent * 0.9, high = targetCurrent == 0 ? 10 : targetCurrent * 1.1, imbalance = targetCurrent == 0 ? 10 : Math.Max(10, targetCurrent * 0.1);
                    project.Flow.Add(new FlowBlockInstance { BlockId = currentBlock.Id, DisplayName = currentDrive + "出流" + targetCurrent.ToString("0.###", CultureInfo.InvariantCulture) + "A", Phase = "主驱测试", PreserveStepNames = false, Snapshot = currentBlock.Clone(), ParameterOverrides = new Dictionary<string, object>(StringComparer.Ordinal) { { "TargetCurrent", targetCurrent }, { "CurrentLow", low }, { "CurrentHigh", high }, { "ImbalanceHigh", imbalance } } });
                    continue;
                }
                string category = ImportedModuleCategory(group.Item2);
                FunctionBlockDefinition block = new FunctionBlockDefinition { Name = group.Item2, Category = category, ModuleKind = "Custom", Version = "1.0", Description = "从平台JSON按功能重建，共" + group.Item3.Count + "步；可在自定义功能块中继续增删和配置。", IsStandard = false, SupportedProducts = string.IsNullOrWhiteSpace(product) ? new List<string>() : new List<string> { product } };
                foreach (SequenceStepDefinition step in group.Item3) block.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object>(step.Properties, StringComparer.Ordinal) });
                project.Blocks.Add(block); project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = block.Name, Phase = ImportedPhase(category), PreserveStepNames = false, Snapshot = block.Clone(), ParameterOverrides = new Dictionary<string, object>(StringComparer.Ordinal) });
            }
            string importedProjectName = Convert.ToString(sequence.RootProperties.ContainsKey("ProjectName") ? sequence.RootProperties["ProjectName"] : string.Empty, CultureInfo.InvariantCulture); if (string.Equals(product, "C96", StringComparison.OrdinalIgnoreCase) && (importedProjectName.IndexOf("C96_FCT_主驱辅驱", StringComparison.OrdinalIgnoreCase) >= 0 || importedProjectName.IndexOf("C96_FCT_11章", StringComparison.OrdinalIgnoreCase) >= 0 || importedProjectName.IndexOf("C96_FCT_12章", StringComparison.OrdinalIgnoreCase) >= 0)) return BuildC96ElevenChapterLayout(project, sequence);
            return project;
        }

        private static bool TryParseCurrentModule(string title, out string drive, out double current)
        {
            drive = string.Empty; current = 0; string value = (title ?? string.Empty).Trim(); if (value.StartsWith("TM1出流", StringComparison.OrdinalIgnoreCase)) drive = "TM1"; else if (value.StartsWith("TM2出流", StringComparison.OrdinalIgnoreCase)) drive = "TM2"; else return false; int start = value.IndexOf("出流", StringComparison.Ordinal) + 2, end = value.LastIndexOf('A'); return start >= 2 && end > start && double.TryParse(value.Substring(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out current);
        }

        private static FunctionBlockDefinition BuildParameterizedCurrentBlock(IList<SequenceStepDefinition> sourceSteps, string drive, double initialCurrent, string product)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = drive + "三相出流与验证", Category = "主驱", ModuleKind = "Custom", Version = "1.0", Description = "参数化三相出流模块；流程实例只需修改目标电流和LIMIT，同一模块可重复复用。", IsStandard = false, SupportedProducts = string.IsNullOrWhiteSpace(product) ? new List<string>() : new List<string> { product } };
            block.Parameters.Add(new BlockParameterDefinition { Name = "TargetCurrent", DisplayName = "目标电流", Type = "double", DefaultValue = initialCurrent, Unit = "A RMS", Description = "目标有效值；写表时自动乘1.414", Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "StepCurrent", DisplayName = "步进电流", Type = "double", DefaultValue = 20d, Unit = "A Peak", Description = "每步上升/下降峰值", Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "HoldTime", DisplayName = "保持时间", Type = "double", DefaultValue = 10d, Unit = "s", Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "Frequency", DisplayName = "输出频率", Type = "double", DefaultValue = 60d, Unit = "Hz", Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "CurrentLow", DisplayName = "电流下限", Type = "double", DefaultValue = initialCurrent == 0 ? -10 : initialCurrent * 0.9, Unit = "A" });
            block.Parameters.Add(new BlockParameterDefinition { Name = "CurrentHigh", DisplayName = "电流上限", Type = "double", DefaultValue = initialCurrent == 0 ? 10 : initialCurrent * 1.1, Unit = "A" });
            block.Parameters.Add(new BlockParameterDefinition { Name = "ImbalanceHigh", DisplayName = "不平衡度上限", Type = "double", DefaultValue = initialCurrent == 0 ? 10 : Math.Max(10, initialCurrent * 0.1), Unit = "A" });
            foreach (SequenceStepDefinition source in sourceSteps)
            {
                SequenceStepDefinition step = SequenceEditing.Clone(source); BlockStepDefinition blockStep = new BlockStepDefinition { StepProperties = new Dictionary<string, object>(step.Properties, StringComparer.Ordinal) };
                if (step.FunctionName == "FCT_CANTable" && string.Equals(Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture), "Write", StringComparison.OrdinalIgnoreCase))
                {
                    JArray changes = JArray.Parse(Convert.ToString(step.Get("ChangesJson"), CultureInfo.InvariantCulture)); foreach (JObject change in changes.OfType<JObject>()) { string name = Convert.ToString(change["Name"], CultureInfo.InvariantCulture); if (name == "Iqs_End") change["Value"] = "${TargetCurrent*1.414}"; else if (name == "Iqs_Step") change["Value"] = "${StepCurrent}"; else if (name == "Hold_Time_S") change["Value"] = "${HoldTime}"; else if (name == "Output_Frequency") change["Value"] = "${Frequency}"; } blockStep.StepProperties["ChangesJson"] = changes.ToString(Newtonsoft.Json.Formatting.None); blockStep.StepProperties["StepName"] = "写入目标电流";
                }
                else if (step.FunctionName == "FCT_CANCalculatedResults" && string.Equals(Convert.ToString(step.Get("CalculationType"), CultureInfo.InvariantCulture), "ThreePhaseCurrentRms", StringComparison.OrdinalIgnoreCase)) { blockStep.StepProperties["StepName"] = "三相实际RMS电流"; blockStep.ParameterBindings["LowLimit"] = "CurrentLow"; blockStep.ParameterBindings["HighLimit"] = "CurrentHigh"; blockStep.ParameterBindings["ImbalanceHighLimit"] = "ImbalanceHigh"; }
                else if (step.FunctionName == "FCT_CANCalculatedResults") blockStep.StepProperties["StepName"] = "电机故障";
                else if (step.FunctionName == "FCT_ExecuteLogic") blockStep.StepProperties["StepName"] = "等待电流稳定";
                blockStep.StepProperties["AutoProductProfile"] = true; blockStep.StepProperties["DriveTarget"] = drive; block.Steps.Add(blockStep);
            }
            return block;
        }

        private static void ResolveImportedStepGroup(string stepName, out string key, out string title, out string localName)
        {
            string value = (stepName ?? string.Empty).Trim(); int separator = value.IndexOf(" / ", StringComparison.Ordinal); string left = separator >= 0 ? value.Substring(0, separator).Trim() : value; localName = separator >= 0 ? value.Substring(separator + 3).Trim() : value;
            int underscore = left.IndexOf('_'); if (underscore >= 3 && left.Take(underscore).All(char.IsDigit)) { key = left.Substring(0, underscore); title = left.Substring(underscore + 1).Trim(); if (title.Length == 0) title = "步骤" + key; return; }
            int numericLength = 0; while (numericLength < left.Length && char.IsDigit(left[numericLength])) numericLength++;
            if (numericLength >= 3)
            {
                int chapter; int.TryParse(left.Substring(0, Math.Min(3, numericLength)), out chapter); key = left.Substring(0, Math.Min(3, numericLength)); title = ImportedChapterTitle(chapter, left.Substring(numericLength).Trim()); if (separator < 0) localName = left.Substring(numericLength).Trim(); return;
            }
            key = "RAW_" + ImportedModuleCategory(value); title = ImportedModuleCategory(value); localName = value;
        }

        private static string ImportedChapterTitle(int chapter, string fallback)
        {
            if (chapter <= 9) return chapter <= 1 ? "产品初始化与FT通信" : "主驱故障清除";
            if (chapter < 100) return "主驱基础状态读取";
            if (chapter < 120) return "TM1三相出流与验证";
            if (chapter < 200) return "TM2三相出流与验证";
            if (chapter < 210) return "PDU与辅驱上电";
            if (chapter < 220) return "油泵测试";
            if (chapter < 230) return "气泵测试";
            if (chapter < 290) return "DCDC与电子负载测试";
            if (chapter >= 290) return "安全停机与下电";
            return string.IsNullOrWhiteSpace(fallback) ? "导入模块" : fallback;
        }

        private static string ImportedModuleCategory(string title)
        {
            string value = title ?? string.Empty; if (value.IndexOf("继电器", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("PDU", StringComparison.OrdinalIgnoreCase) >= 0) return "继电器/PDU"; if (value.IndexOf("油泵", StringComparison.OrdinalIgnoreCase) >= 0) return "油泵"; if (value.IndexOf("气泵", StringComparison.OrdinalIgnoreCase) >= 0) return "气泵"; if (value.IndexOf("DCDC", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("负载", StringComparison.OrdinalIgnoreCase) >= 0) return "DCDC"; if (value.IndexOf("TM1", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("TM2", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("主驱", StringComparison.OrdinalIgnoreCase) >= 0) return "主驱"; if (value.IndexOf("安全", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("下电", StringComparison.OrdinalIgnoreCase) >= 0) return "安全"; return "公共准备";
        }
        private static string ImportedPhase(string category) { return category == "安全" ? "安全收尾" : category == "油泵" || category == "气泵" || category == "DCDC" ? "辅驱测试" : category == "主驱" ? "主驱测试" : "准备阶段"; }
        private static string SafeFileName(string value) { foreach (char invalid in Path.GetInvalidFileNameChars()) value = (value ?? string.Empty).Replace(invalid, '_'); return string.IsNullOrWhiteSpace(value) ? "ImportedSEQ" : value; }
        private static string PlatformSequenceDirectory() { string path = @"E:\FST\TestDLL\TestDLL\bin\Sequence"; Directory.CreateDirectory(path); return path; }

        private string ResolveSequenceProduct(string path)
        {
            List<string> candidates = new List<string> { "C91", "C92", "C95", "C96" };
            if (_productLocatorRepository != null) candidates.AddRange(_productLocatorRepository.Products.Select(value => value.Product));
            candidates = candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(value => value.Length).ToList();
            foreach (string key in new[] { "Product", "ProductModel", "ProjectName" })
            {
                object raw; if (_sequenceDocument != null && _sequenceDocument.RootProperties.TryGetValue(key, out raw)) { string found = MatchProductCandidate(Convert.ToString(raw, CultureInfo.InvariantCulture), candidates); if (!string.IsNullOrWhiteSpace(found)) return found; }
            }
            if (_sequenceDocument != null) foreach (SequenceStepDefinition step in _sequenceDocument.Steps) { string found = MatchProductCandidate(Convert.ToString(step.Get("Product", string.Empty), CultureInfo.InvariantCulture), candidates); if (!string.IsNullOrWhiteSpace(found)) return found; }
            string fromFile = MatchProductCandidate(Path.GetFileNameWithoutExtension(path) ?? string.Empty, candidates); return string.IsNullOrWhiteSpace(fromFile) ? _service.ProductProfile.Model.ToString() : fromFile;
        }

        private static string MatchProductCandidate(string text, IEnumerable<string> candidates)
        {
            string value = (text ?? string.Empty).Trim().ToUpperInvariant(); foreach (string candidate in candidates) if (value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return candidate; return string.Empty;
        }

        private void SelectProductContext(string product)
        {
            ProductModel model; if (!Enum.TryParse(product, true, out model)) return; ProductCanProfile selected = _productModelComboBox == null ? null : _productModelComboBox.Items.Cast<object>().OfType<ProductCanProfile>().FirstOrDefault(value => value.Model == model); _service.SetProductModel(model); if (_productModelComboBox != null && selected != null && !ReferenceEquals(_productModelComboBox.SelectedItem, selected)) _productModelComboBox.SelectedItem = selected; UpdateProductTabs(model);
        }

        private void OpenStudioProject_Click(object sender, RoutedEventArgs e)
        {
            ImportSequence_Click(sender, e);
        }

        private void SaveStudioProject_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_studioProjectPath) || !string.Equals(Path.GetExtension(_studioProjectPath), ".json", StringComparison.OrdinalIgnoreCase)) { SaveStudioProjectAs_Click(sender, e); return; }
            try { SaveCompiledSequenceToPath(_studioProjectPath, true); }
            catch (Exception ex) { MessageBox.Show(this, "SEQ保存失败：\n" + ex.Message, "保存JSON SEQ", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void SaveStudioProjectAs_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog { Title = "SEQ另存为", Filter = "平台SEQ JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true, InitialDirectory = PlatformSequenceDirectory(), FileName = string.IsNullOrWhiteSpace(_studioProjectPath) ? (_studioProject.ProjectName ?? "FCT_SEQ") + ".json" : Path.GetFileNameWithoutExtension(_studioProjectPath) + ".json" };
            if (dialog.ShowDialog(this) != true) return; _studioProjectPath = dialog.FileName; SaveStudioProject_Click(sender, e);
        }

        private void SaveCompiledSequenceToPath(string path, bool allowEmpty)
        {
            SynchronizeFlowStructuresForSave(); FctStudioCompileResult compiled = FctStudioCompiler.Compile(_studioProject); if (!allowEmpty && compiled.Document.Steps.Count == 0) throw new InvalidOperationException("当前流程为空，不能生成平台SEQ。"); string[] unsupported = _legacyRuntime == null ? new string[0] : compiled.Document.Steps.Select(step => step.FunctionName).Distinct().Where(name => !_legacyRuntime.SupportsFunction(name)).OrderBy(name => name).ToArray(); if (unsupported.Length > 0) throw new InvalidOperationException("当前CSP.TestDLL缺少以下FunctionName：\n" + string.Join("\n", unsupported)); string json = compiled.Document.ToJson(compiled.Document.Steps); SequenceDocument reparsed = SequenceDocument.Parse(json); if (reparsed.Steps.Count != compiled.Document.Steps.Count) throw new InvalidOperationException("SEQ回读STEP数量不一致。"); string directory = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory); File.WriteAllText(path, json, new UTF8Encoding(false)); FctStudioProjectService.SaveEditorState(path, _studioProject); _studioProjectPath = path; _loadedSequencePath = path; ApplyCompiledStudioSequence(compiled); _studioProjectDirty = false; UpdateCurrentFileDisplay(); Service_Log("JSON SEQ及完整编辑状态已保存：" + path + "；" + reparsed.Steps.Count + " STEP");
        }

        private void SynchronizeFlowStructuresForSave()
        {
            if (_studioProject == null || _studioProject.Flow == null || _studioProject.Blocks == null) return;
            string[] perInstanceKeys = { "ResultMode", "LowLimit", "HighLimit", "Comtype", "Unit", "Limit", "SignalChecksJson", "ImbalanceLowLimit", "ImbalanceHighLimit" };
            foreach (FlowBlockInstance instance in _studioProject.Flow)
            {
                FunctionBlockDefinition source = _studioProject.Blocks.FirstOrDefault(block => block.Id == instance.BlockId); if (source == null || !string.Equals(source.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase)) continue;
                FunctionBlockDefinition previous = instance.Snapshot; FunctionBlockDefinition merged = source.Clone();
                if (previous != null)
                    foreach (BlockStepDefinition step in merged.Steps)
                    {
                        BlockStepDefinition old = previous.Steps.FirstOrDefault(value => value.Id == step.Id); if (old == null) continue;
                        foreach (string key in perInstanceKeys) { object value; if (old.StepProperties.TryGetValue(key, out value)) step.StepProperties[key] = value; }
                    }
                instance.Snapshot = merged; instance.DisplayName = string.IsNullOrWhiteSpace(instance.DisplayName) ? source.Name : instance.DisplayName;
            }
        }

        private void StudioProjectProperties_Click(object sender, RoutedEventArgs e)
        {
            StudioProjectPropertiesWindow dialog = new StudioProjectPropertiesWindow(_studioProject) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try { dialog.ApplyTo(_studioProject); StudioFlowChanged(); _studioFlowEditorPanel.RefreshProject(); Service_Log("工程属性已更新：" + _studioProject.ProjectName + " / " + _studioProject.Product); }
            catch (Exception ex) { MessageBox.Show(this, "工程属性保存失败：\n" + ex.Message, "FCT Studio", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ExportStudioSequence_Click(object sender, RoutedEventArgs e)
        {
            string temporaryPath = null;
            try
            {
                FctStudioCompileResult compiled = FctStudioCompiler.Compile(_studioProject);
                if (compiled.Document.Steps.Count == 0) throw new InvalidOperationException("当前流程为空，不能生成平台SEQ。请先在“流程调试与编辑”页添加功能块。");
                string[] unsupported = _legacyRuntime == null ? new string[0] : compiled.Document.Steps.Select(step => step.FunctionName).Distinct().Where(name => !_legacyRuntime.SupportsFunction(name)).OrderBy(name => name).ToArray();
                if (unsupported.Length > 0) throw new InvalidOperationException("当前CSP.TestDLL缺少以下FunctionName：\n" + string.Join("\n", unsupported));
                SaveFileDialog dialog = new SaveFileDialog { Title = "生成原平台SEQ", Filter = "SEQ JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true, InitialDirectory = PlatformSequenceDirectory(), FileName = (_studioProject.ProjectName ?? "FCT_Project").Replace(" ", "_") + ".json" };
                if (dialog.ShowDialog(this) != true) return;
                SaveCompiledSequenceToPath(dialog.FileName, false); int platformCount = _legacyRuntime == null ? compiled.Document.Steps.Count : _legacyRuntime.ValidateSequenceFile(dialog.FileName); if (platformCount != compiled.Document.Steps.Count) throw new InvalidOperationException("原CSP引擎加载数量不一致：" + platformCount + "/" + compiled.Document.Steps.Count); MessageBox.Show(this, "平台SEQ保存成功：\n" + dialog.FileName + "\n\n共 " + compiled.Document.Steps.Count + " STEP。", "保存JSON SEQ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, "生成平台SEQ失败：\n" + ex.Message, "FCT Studio", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        private bool ConfirmDiscardStudioChanges()
        {
            if (!_studioProjectDirty) return true;
            MessageBoxResult result = MessageBox.Show(this, "当前JSON SEQ尚未保存。\n\n选择“是”保存后继续，“否”放弃修改，“取消”返回。", "JSON SEQ", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes) { SaveStudioProject_Click(this, new RoutedEventArgs()); return !_studioProjectDirty; }
            return true;
        }

        private void ResetStudioHistory()
        {
            _studioUndo.Clear(); _studioRedo.Clear(); _lastStudioSnapshot = _studioProject == null ? null : FctStudioProjectService.Serialize(_studioProject); ResetStudioNavigation();
        }

        private void RecordStudioHistory()
        {
            if (_restoringStudioHistory || _studioProject == null) return;
            string current = FctStudioProjectService.Serialize(_studioProject);
            if (_lastStudioSnapshot != null && !string.Equals(_lastStudioSnapshot, current, StringComparison.Ordinal)) _studioUndo.Push(_lastStudioSnapshot);
            _lastStudioSnapshot = current; _studioRedo.Clear();
            try { FctStudioProjectService.Save(StudioAutosavePath(), _studioProject); } catch (Exception ex) { Service_Log("自动保存失败：" + ex.Message); }
        }

        private void UndoStudio_Click(object sender, RoutedEventArgs e)
        {
            if (_studioUndo.Count == 0) return; string current = FctStudioProjectService.Serialize(_studioProject); _studioRedo.Push(current); RestoreStudioSnapshot(_studioUndo.Pop(), "已撤销上一次修改");
        }

        private void RedoStudio_Click(object sender, RoutedEventArgs e)
        {
            if (_studioRedo.Count == 0) return; string current = FctStudioProjectService.Serialize(_studioProject); _studioUndo.Push(current); RestoreStudioSnapshot(_studioRedo.Pop(), "已重做修改");
        }

        private void RestoreStudioSnapshot(string json, string message)
        {
            _restoringStudioHistory = true;
            try { _studioProject = FctStudioProjectService.Deserialize(json); _lastStudioSnapshot = json; _studioProjectDirty = true; _functionBlockStudioPanel.RefreshProject(); _studioFlowEditorPanel.RefreshProject(); try { FctStudioProjectService.Save(StudioAutosavePath(), _studioProject); } catch (Exception ex) { Service_Log("撤回后自动保存失败：" + ex.Message); } Service_Log(message); }
            finally { _restoringStudioHistory = false; }
        }

        private void RecoverStudioAutosave_Click(object sender, RoutedEventArgs e)
        {
            string path = StudioAutosavePath(); if (!File.Exists(path)) { MessageBox.Show(this, "没有找到自动保存文件。", "FCT Studio", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (!ConfirmDiscardStudioChanges()) return;
            try { _studioProject = FctStudioProjectService.Load(path); _studioProjectDirty = true; ResetStudioHistory(); _functionBlockStudioPanel.RefreshProject(); _studioFlowEditorPanel.RefreshProject(); Service_Log("已恢复自动保存：" + path); }
            catch (Exception ex) { MessageBox.Show(this, "恢复自动保存失败：\n" + ex.Message, "FCT Studio", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static string StudioAutosavePath() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StudioProjects", ".editor-autosave.json"); }

        private void OpenTestItemLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureWorkflowEditable()) return;
            IReadOnlyList<SequenceStepDefinition> templates = SequenceEditing.BuildFunctionTemplates(_workflowSteps.Select(step => step.Definition));
            StepSelectionWindow dialog = new StepSelectionWindow("添加测试项", templates, false, _productLocatorRepository) { Owner = this };
            if (dialog.ShowDialog() == true) InsertStepsAfterSelected(dialog.SelectedSteps);
        }

        private void ImportStepsFromSequence_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureWorkflowEditable()) return;
            OpenFileDialog fileDialog = new OpenFileDialog { Title = "选择测试项来源SEQ", Filter = "SEQ JSON (*.json)|*.json|所有文件 (*.*)|*.*" };
            if (fileDialog.ShowDialog(this) != true) return;
            try
            {
                SequenceDocument source = SequenceDocument.Parse(File.ReadAllText(fileDialog.FileName, Encoding.UTF8));
                StepSelectionWindow selection = new StepSelectionWindow("从SEQ选择要导入的测试项", source.Steps, true) { Owner = this };
                if (selection.ShowDialog() == true) InsertStepsAfterSelected(selection.SelectedSteps);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "测试项导入失败：\n" + ex.Message, "导入测试项", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DuplicateSelectedStep_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureWorkflowEditable()) return;
            WorkflowStepState selected = _sequenceList.SelectedItem as WorkflowStepState;
            if (selected == null) return;
            InsertStepsAfterSelected(new[] { SequenceEditing.Clone(selected.Definition) });
        }

        private void DeleteSelectedStep_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureWorkflowEditable()) return;
            WorkflowStepState selected = _sequenceList.SelectedItem as WorkflowStepState;
            if (selected == null) return;
            if (MessageBox.Show(this, "确定从当前调试SEQ删除测试项？\n\n" + selected.Name, "删除测试项", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int index = _workflowSteps.IndexOf(selected);
            _workflowSteps.Remove(selected);
            RefreshWorkflowAfterStructureChange(Math.Min(index, _workflowSteps.Count - 1));
        }

        private void MoveStepUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedStep(-1);
        }

        private void MoveStepDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedStep(1);
        }

        private void MoveSelectedStep(int offset)
        {
            if (!EnsureWorkflowEditable()) return;
            WorkflowStepState selected = _sequenceList.SelectedItem as WorkflowStepState;
            if (selected == null) return;
            int oldIndex = _workflowSteps.IndexOf(selected);
            int newIndex = oldIndex + offset;
            if (newIndex < 0 || newIndex >= _workflowSteps.Count) return;
            _workflowSteps.Move(oldIndex, newIndex);
            RefreshWorkflowAfterStructureChange(newIndex);
        }

        private void InsertStepsAfterSelected(IEnumerable<SequenceStepDefinition> definitions)
        {
            List<SequenceStepDefinition> items = definitions == null ? new List<SequenceStepDefinition>() : definitions.Select(SequenceEditing.Clone).ToList();
            if (items.Count == 0) return;
            if (_legacyRuntime != null)
            {
                string[] unsupported = items.Where(step => !_legacyRuntime.SupportsFunction(step.FunctionName)).Select(step => step.FunctionName).Distinct().OrderBy(name => name).ToArray();
                if (unsupported.Length > 0)
                {
                    MessageBox.Show(this, "以下FunctionName不在当前CSP.TestDLL中，未执行导入：\n\n" + string.Join("\n", unsupported), "测试项兼容性检查", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            WorkflowStepState selected = _sequenceList.SelectedItem as WorkflowStepState;
            int insertIndex = selected == null ? _workflowSteps.Count : _workflowSteps.IndexOf(selected) + 1;
            int firstIndex = insertIndex;
            foreach (SequenceStepDefinition definition in items)
                _workflowSteps.Insert(insertIndex++, new WorkflowStepState(insertIndex, definition));
            RefreshWorkflowAfterStructureChange(firstIndex);
            Service_Log("已插入 " + items.Count + " 个测试项；当前共 " + _workflowSteps.Count + " STEP。");
        }

        private bool EnsureWorkflowEditable()
        {
            if (!_workflowRunning) return true;
            MessageBox.Show(this, "流程运行中不能修改测试项结构，请先停止流程。", "测试项编辑", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void RefreshWorkflowAfterStructureChange(int selectedIndex)
        {
            for (int index = 0; index < _workflowSteps.Count; index++) _workflowSteps[index].Renumber(index + 1);
            ICollectionView view = CollectionViewSource.GetDefaultView(_sequenceList.ItemsSource);
            view.Refresh();
            if (_sequenceSummaryText != null) _sequenceSummaryText.Text = _workflowSteps.Count + " STEP";
            if (_instrumentCenterPanel != null) _instrumentCenterPanel.RefreshTemplates(_workflowSteps.Select(step => step.Definition));
            if (selectedIndex >= 0 && selectedIndex < _workflowSteps.Count)
            {
                _sequenceList.SelectedItem = _workflowSteps[selectedIndex];
                _sequenceList.ScrollIntoView(_workflowSteps[selectedIndex]);
            }
        }

        private void LoadSequenceFromFile(string path, bool showMessage)
        {
            try
            {
                _sequenceDocument = SequenceDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                _loadedSequencePath = path; UpdateCurrentFileDisplay();
                _workflowSteps = new ObservableCollection<WorkflowStepState>(_sequenceDocument.Steps.Select((step, index) => new WorkflowStepState(index + 1, step)));
                _sequenceList.ItemsSource = _workflowSteps;
                if (_sequenceSearchTextBox != null) _sequenceSearchTextBox.Clear();
                if (_sequenceSummaryText != null) _sequenceSummaryText.Text = _workflowSteps.Count + " STEP";
                if (_instrumentCenterPanel != null) _instrumentCenterPanel.RefreshTemplates(_workflowSteps.Select(step => step.Definition));
                if (_functionBlockStudioPanel != null)
                {
                    _atomicCatalogSteps = _sequenceDocument.Steps.Select(SequenceEditing.Clone).Concat(GenericStepCatalog.CreateTemplates().Select(SequenceEditing.Clone)).ToList().AsReadOnly();
                    if (_instrumentCenterPanel != null) _instrumentCenterPanel.RefreshTemplates(_atomicCatalogSteps);
                    _functionBlockStudioPanel.RefreshProject();
                }
                _selectedWorkflowStep = null;
                if (_workflowSteps.Count > 0) _sequenceList.SelectedIndex = 0;
                Service_Log("SEQ已加载：" + path + "；共 " + _workflowSteps.Count + " 个STEP。全部通过原平台 TestDllMain 执行，可逐项编辑和导出。");
                if (showMessage) MessageBox.Show(this, "SEQ导入成功：" + _workflowSteps.Count + " 个STEP。", "导入SEQ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Service_Log("SEQ加载失败：" + ex.Message);
                if (showMessage) MessageBox.Show(this, "SEQ加载失败：\n" + ex.Message, "导入SEQ", MessageBoxButton.OK, MessageBoxImage.Error);
                else LoadFallbackWorkflow();
            }
        }

        private void LoadFallbackWorkflow()
        {
            Dictionary<string, object> root = new Dictionary<string, object>
            {
                { "SerialNumberLen", 0 }, { "ProjectName", "ManualCanDebug" }, { "StationName", "DEBUG" },
                { "SequenceVersion", "DEBUG-FALLBACK" }, { "UIDisplayType", "All" }, { "LogFilePath", "D:\\LogfilePath" }
            };
            List<SequenceStepDefinition> steps = CanSequenceCatalog.OrderedSteps.Select(step =>
            {
                Dictionary<string, object> values = new Dictionary<string, object>
                {
                    { "StepName", step.Name }, { "RunMode", "Normal" }, { "FunctionName", step.FunctionName }, { "RecordingLog", true }
                };
                if (step.FunctionName == "Resolver_SetSpeed") values["Speed"] = step.Value;
                else if (step.FunctionName == "Resolver_SetPosition") values["Position"] = step.Value;
                else if (step.FunctionName == "CAN_SetDUTCurrent") { values["MaxCurrent"] = step.Value; values["StepCurrent"] = step.StepCurrent; values["HoldTime"] = step.HoldTime; values["Frequency"] = step.Frequency; }
                return new SequenceStepDefinition(values);
            }).ToList();
            _sequenceDocument = new SequenceDocument(root, steps);
            _workflowSteps = new ObservableCollection<WorkflowStepState>(steps.Select((step, index) => new WorkflowStepState(index + 1, step)));
            _sequenceList.ItemsSource = _workflowSteps;
            if (_sequenceSummaryText != null) _sequenceSummaryText.Text = _workflowSteps.Count + " STEP";
            if (_instrumentCenterPanel != null) _instrumentCenterPanel.RefreshTemplates(_workflowSteps.Select(step => step.Definition));
            if (_workflowSteps.Count > 0) _sequenceList.SelectedIndex = 0;
        }

        private void InitializeStudioProject()
        {
            _studioProjectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StudioProjects", "DefaultSequence.json");
            if (File.Exists(_studioProjectPath))
            {
                try { LoadSequenceFromFile(_studioProjectPath, false); bool restored = FctStudioProjectService.TryLoadEditorState(_studioProjectPath, out _studioProject); if (!restored) _studioProject = CreateImportedSequenceProject(_sequenceDocument, _service.ProductProfile.Model.ToString(), Path.GetFileNameWithoutExtension(_studioProjectPath)); GlobalModuleLibraryService.MergeInto(_studioProject); Service_Log("已自动加载JSON SEQ：" + _studioProjectPath + (restored ? "；已恢复完整编辑状态" : string.Empty)); }
                catch (Exception ex) { _studioProject = FctStudioProjectService.CreateBlank(_sequenceDocument, _service.ProductProfile.Model.ToString()); GlobalModuleLibraryService.MergeInto(_studioProject); Service_Log("默认JSON加载失败，已创建空白SEQ：" + ex.Message); }
            }
            else
            {
                SequenceDocument blank = new SequenceDocument(new Dictionary<string, object>(_sequenceDocument.RootProperties, StringComparer.Ordinal), new SequenceStepDefinition[0]); string directory = Path.GetDirectoryName(_studioProjectPath); Directory.CreateDirectory(directory); File.WriteAllText(_studioProjectPath, blank.ToJson(blank.Steps), new UTF8Encoding(false)); LoadSequenceFromFile(_studioProjectPath, false); _studioProject = FctStudioProjectService.CreateBlank(_sequenceDocument, _service.ProductProfile.Model.ToString()); GlobalModuleLibraryService.MergeInto(_studioProject); Service_Log("已建立默认空白JSON SEQ：" + _studioProjectPath);
            }
            _functionBlockStudioPanel = new FunctionBlockStudioPanel(() => _studioProject, () => _atomicCatalogSteps, _productLocatorRepository, Service_Log, StudioBlockChanged, ExecuteInstrumentStepAsync, () => _legacyRuntime == null ? null : _legacyRuntime.LastStepExecution, OpenFunctionBlockEditor, ReturnToStudioFlowWorkspace);
            _studioFlowEditorPanel = new StudioFlowEditorPanel(() => _studioProject, StudioFlowChanged, ApplyCompiledStudioSequence, StartStudioDebugAsync, ContinueStudioDebugAsync, StepStudioDebugAsync, StopStudioDebug, Service_Log, OpenFunctionBlockEditor);
            _functionBlockStudioPanel.RefreshProject();
            _studioFlowEditorPanel.RefreshProject();
            ApplyStudioRunMode();
            ShowStudioFlowWorkspace(null);
            _studioProjectDirty = false;
            ResetStudioHistory();
            Service_Log("FCT Studio工程已就绪：" + _studioProject.Blocks.Count + " 个功能块，" + _studioProject.Flow.Count + " 个流程实例。");
        }

        private void StudioBlockChanged()
        {
            RecordStudioHistory();
            _studioProjectDirty = true;
            if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.RefreshProject();
            SetApplicationStatus("功能块已修改，尚未保存");
            UpdateCurrentFileDisplay();
        }

        private void OpenFunctionBlockEditor(FunctionBlockDefinition block)
        {
            if (block == null || _functionBlockStudioPanel == null || _mainTabs == null) return; if (!_restoringStudioNavigation) PushStudioNavigation(); if (!_studioBlockMode && _studioFlowEditorPanel != null) _studioReturnFlowInstanceId = _studioFlowEditorPanel.SelectedFlowInstanceId; ShowStudioBlockWorkspace(block.Id, null); SetApplicationStatus("正在编辑功能块：" + block.Name); UpdateNavigationBackButton();
        }
        private void ShowStudioFlowWorkspace(string instanceId)
        {
            if (_studioWorkspaceHost == null || _studioFlowEditorPanel == null) return; if (_studioBlockMode && _functionBlockStudioPanel != null) _functionBlockStudioPanel.CommitPendingChanges(); _studioBlockMode = false; _studioWorkspaceHost.Content = _studioFlowEditorPanel; _mainTabs.SelectedItem = _studioFlowTab; string restoreId = string.IsNullOrWhiteSpace(instanceId) ? _studioReturnFlowInstanceId : instanceId; if (!string.IsNullOrWhiteSpace(restoreId)) _studioFlowEditorPanel.RestoreNavigation(restoreId);
        }
        private void ShowStudioBlockWorkspace(string blockId, string stepId)
        {
            if (_studioWorkspaceHost == null || _functionBlockStudioPanel == null) return; _studioBlockMode = true; _studioWorkspaceHost.Content = _functionBlockStudioPanel; _mainTabs.SelectedItem = _studioFlowTab; if (!string.IsNullOrWhiteSpace(blockId)) _functionBlockStudioPanel.RestoreNavigation(blockId, stepId);
        }
        private void ReturnToStudioFlowWorkspace()
        {
            if (!_studioBlockMode) return; if (!_restoringStudioNavigation) PushStudioNavigation(); ShowStudioFlowWorkspace(_studioReturnFlowInstanceId); SetApplicationStatus("已返回序列模块排序"); UpdateNavigationBackButton();
        }
        private void PushStudioNavigation() { StudioNavigationState state = CaptureStudioNavigation(); if (state == null) return; StudioNavigationState previous = _studioNavigationBack.Count == 0 ? null : _studioNavigationBack.Peek(); if (previous == null || !previous.SamePosition(state)) _studioNavigationBack.Push(state); UpdateNavigationBackButton(); }
        private StudioNavigationState CaptureStudioNavigation() { if (_mainTabs == null) return null; return new StudioNavigationState { MainTab = _mainTabs.SelectedItem as TabItem, AdvancedTab = _advancedTabs == null ? null : _advancedTabs.SelectedItem as TabItem, BlockMode = _studioBlockMode, BlockId = _functionBlockStudioPanel == null ? string.Empty : _functionBlockStudioPanel.SelectedBlockId, BlockStepId = _functionBlockStudioPanel == null ? string.Empty : _functionBlockStudioPanel.SelectedStepId, FlowInstanceId = _studioFlowEditorPanel == null ? string.Empty : _studioFlowEditorPanel.SelectedFlowInstanceId }; }
        private void NavigateBack_Click(object sender, RoutedEventArgs e) { if (_studioNavigationBack.Count == 0) return; StudioNavigationState state = _studioNavigationBack.Pop(); _restoringStudioNavigation = true; try { if (state.MainTab == _studioFlowTab) { if (state.BlockMode) ShowStudioBlockWorkspace(state.BlockId, state.BlockStepId); else ShowStudioFlowWorkspace(state.FlowInstanceId); } else { if (state.MainTab != null) _mainTabs.SelectedItem = state.MainTab; if (state.MainTab == _advancedToolsTab && _advancedTabs != null && state.AdvancedTab != null) _advancedTabs.SelectedItem = state.AdvancedTab; } SetApplicationStatus("已返回上一个操作位置"); } finally { _restoringStudioNavigation = false; UpdateNavigationBackButton(); } }
        private void ResetStudioNavigation() { _studioNavigationBack.Clear(); UpdateNavigationBackButton(); }
        private void UpdateNavigationBackButton() { if (_navigationBackButton != null) { _navigationBackButton.IsEnabled = _studioNavigationBack.Count > 0; _navigationBackButton.ToolTip = _studioNavigationBack.Count > 0 ? "返回上一个操作界面（Alt+←），当前可返回" + _studioNavigationBack.Count + "级" : "没有可返回的操作界面（Alt+←）"; } }

        private void StudioFlowChanged()
        {
            RecordStudioHistory();
            _studioProjectDirty = true;
            SetApplicationStatus("流程工程已修改，尚未保存");
            UpdateCurrentFileDisplay();
        }

        private void ApplyCompiledStudioSequence(FctStudioCompileResult compiled)
        {
            if (compiled == null) return;
            List<SequenceStepDefinition> missing = compiled.Document.Steps.Where(step => !MainTestMethodCatalog.Contains(step.FunctionName)).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("以下STEP尚未在MainTest中实现，不能进入调试或导出：\n" + string.Join("\n", missing.Take(12).Select(step => "- " + step.StepName + " → " + step.FunctionName)));
            _sequenceDocument = compiled.Document;
            _workflowSteps = new ObservableCollection<WorkflowStepState>(compiled.Document.Steps.Select((step, index) => new WorkflowStepState(index + 1, step)));
            _sequenceList.ItemsSource = _workflowSteps;
            _selectedWorkflowStep = null;
            if (_sequenceSearchTextBox != null) _sequenceSearchTextBox.Clear();
            if (_sequenceSummaryText != null) _sequenceSummaryText.Text = _workflowSteps.Count + " STEP";
            if (_instrumentCenterPanel != null) _instrumentCenterPanel.RefreshTemplates(_workflowSteps.Select(step => step.Definition));
            if (_workflowSteps.Count > 0) _sequenceList.SelectedIndex = 0;
            Service_Log("功能块流程已展开：" + _workflowSteps.Count + " 个原平台STEP；正式JSON中不包含功能块或断点字段。");
        }

        private async Task StartStudioDebugAsync(FctStudioCompileResult compiled, int startIndex)
        {
            if (_legacyRuntime == null || !_legacyRuntime.InstrumentsInitialized) throw new InvalidOperationException("请先在仪器中心勾选并初始化当前项目所需仪器。");
            if (_workflowRunning || _legacyRuntime.IsRunning) throw new InvalidOperationException("已有流程正在运行。");
            bool highRisk = compiled.Document.Steps.Any(step => step.FunctionName.StartsWith("HVDC_", StringComparison.Ordinal) || (step.FunctionName == "FCT_ExecuteAction" && string.Equals(Convert.ToString(step.Get("Device")), "HVDC", StringComparison.OrdinalIgnoreCase)));
            if (highRisk && MessageBox.Show(this, "该调试流程包含高压电源操作。\n\n请确认接线、负载、急停、水冷和人员安全条件已经满足。是否继续？", "高压调试确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (compiled.Warnings.Any(warning => warning.IndexOf("安全下电", StringComparison.Ordinal) >= 0) && MessageBox.Show(this, "流程检查提示没有显式安全下电功能块。\n\n虽然停止和结束时仍会调用PostUUT，但建议先补充安全下电。是否仍然继续调试？", "安全收尾提醒", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (_studioDebugActive) await EndStudioDebugSessionAsync();
            ApplyCompiledStudioSequence(compiled);
            string path = WriteRuntimeSequence(0, _workflowSteps.Count - 1);
            _studioDebugCompile = compiled;
            _studioBreakpointIndexes = new HashSet<int>(compiled.Trace.Where(trace => _studioProject.Breakpoints.Contains(trace.FlowInstanceId + ":" + trace.BlockStepId)).Select(trace => trace.SequenceIndex));
            _studioDebugNextIndex = Math.Max(0, Math.Min(startIndex, _workflowSteps.Count));
            await _legacyRuntime.PrepareDebugSessionAsync(path);
            _studioDebugActive = true;
            Service_Log("功能块调试已通过MainTest启动；起始STEP=" + (_studioDebugNextIndex + 1) + "，断点=" + _studioBreakpointIndexes.Count + "。可单步或继续到断点。");
            await ContinueStudioDebugInternalAsync(true);
        }

        private Task ContinueStudioDebugAsync()
        {
            if (!_studioDebugActive) throw new InvalidOperationException("当前没有暂停中的功能块调试会话。");
            return ContinueStudioDebugInternalAsync(true);
        }

        private async Task ContinueStudioDebugInternalAsync(bool ignoreBreakpointAtCurrent)
        {
            bool first = true;
            while (_studioDebugNextIndex < _workflowSteps.Count)
            {
                if (_studioBreakpointIndexes.Contains(_studioDebugNextIndex) && !(first && ignoreBreakpointAtCurrent))
                {
                    Service_Log("命中断点：" + (_studioDebugNextIndex + 1) + " " + _workflowSteps[_studioDebugNextIndex].Name);
                    SetApplicationStatus("断点暂停：" + _workflowSteps[_studioDebugNextIndex].Name);
                    return;
                }
                await ExecuteStudioDebugStepAsync();
                first = false;
            }
            await EndStudioDebugSessionAsync();
            Service_Log("功能块流程调试完成。");
        }

        private async Task StepStudioDebugAsync()
        {
            if (!_studioDebugActive) throw new InvalidOperationException("当前没有功能块调试会话，请先点击“从选中处调试”。");
            if (_studioDebugNextIndex >= _workflowSteps.Count) { await EndStudioDebugSessionAsync(); return; }
            await ExecuteStudioDebugStepAsync();
            if (_studioDebugNextIndex >= _workflowSteps.Count) await EndStudioDebugSessionAsync();
            else SetApplicationStatus("单步暂停：下一步 " + _workflowSteps[_studioDebugNextIndex].Name);
        }

        private async Task ExecuteStudioDebugStepAsync()
        {
            int index = _studioDebugNextIndex;
            WorkflowStepState step = _workflowSteps[index];
            _sequenceList.SelectedIndex = index; _sequenceList.ScrollIntoView(step); step.Status = "运行中";
            if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.UpdateDebugStep(index, "运行中", string.Empty);
            Service_Log(string.Format("功能块调试 STEP {0}/{1}：{2} [{3}]", index + 1, _workflowSteps.Count, step.Name, step.FunctionName));
            try
            {
                string result = await _legacyRuntime.RunLoadedSingleStepAsync(index);
                step.Status = IsPassingStatus(result) ? "完成" : "失败";
                if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.UpdateDebugStep(index, step.Status, string.IsNullOrWhiteSpace(result) ? "完成" : result);
                if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.UpdateRuntimeVariables(_legacyRuntime.GetRuntimeSnapshot());
                int runtimeIndex = _legacyRuntime.RuntimeCurrentStepIndex;
                _studioDebugNextIndex = runtimeIndex >= 0 && runtimeIndex < _workflowSteps.Count && runtimeIndex != index ? runtimeIndex : index + 1;
                Service_Log("STEP结果：" + (string.IsNullOrWhiteSpace(result) ? "完成" : result));
            }
            catch
            {
                step.Status = "失败";
                if (_studioFlowEditorPanel != null) _studioFlowEditorPanel.UpdateDebugStep(index, "失败", "查看运行日志");
                await EndStudioDebugSessionAsync();
                throw;
            }
        }

        private async void StopStudioDebug()
        {
            try { await EndStudioDebugSessionAsync(); Service_Log("功能块调试已停止并由MainTest执行已选仪器安全收尾。"); }
            catch (Exception ex) { Service_Log("停止功能块调试失败：" + ex.Message); }
        }

        private async Task EndStudioDebugSessionAsync()
        {
            if (!_studioDebugActive) return;
            try { await _legacyRuntime.EndDebugSessionAsync(); }
            finally { _studioDebugActive = false; _studioDebugCompile = null; }
        }

        private void GenerateDebugSequence_Click(object sender, RoutedEventArgs e)
        {
            ExportStudioSequence_Click(sender, e);
        }

        private async void EnterFtMode_Click(object sender, RoutedEventArgs e) { await RunMainTestAdvancedAsync("进入 FT 模式", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "EnterFT" }, { "ResultMode", "Action" } }); }
        private async void InitializeDut_Click(object sender, RoutedEventArgs e)
        {
            await RunMainTestAdvancedAsync("DUT 通信初始化", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "CommunicationInit" }, { "TxID", "2030" }, { "RxID", "2031" }, { "ResultMode", "Action" } });
        }
        private async void TestProductCommunication_Click(object sender, RoutedEventArgs e) { await RunMainTestAdvancedAsync("CAN 通信测试", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "CommunicationTest" }, { "ResultMode", "Information" } }); }
        private async void SendWakeup_Click(object sender, RoutedEventArgs e) { await RunMainTestAdvancedAsync("发送唤醒帧", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "Wakeup" }, { "ResultMode", "Action" } }); }
        private void ReadProductCurrent_Click(object sender, RoutedEventArgs e) { ShowProductCurrentWindow(_service.LastRequestedCurrentRms); }
        private void ReadAllC95Inputs_Click(object sender, RoutedEventArgs e)
        {
            if (_service.ProductProfile.Model != ProductModel.C95)
            {
                MessageBox.Show(this, "请先把产品型号切换为 C95。", "CAN Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            C95InputTablesWindow window = new C95InputTablesWindow(_advancedCanService.ReadAllC95InputTables) { Owner = this };
            window.ShowDialog();
        }

        private void ReadAllC95Tables_Click(object sender, RoutedEventArgs e)
        {
            if (_service.ProductProfile.Model != ProductModel.C95)
            {
                MessageBox.Show(this, "请先把产品型号切换为 C95。", "CAN Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            C95AllTablesWindow window = new C95AllTablesWindow(_advancedCanService.ReadAllC95Tables) { Owner = this };
            window.ShowDialog();
        }

        private void ReadProductResolver_Click(object sender, RoutedEventArgs e)
        {
            ProductResolverWindow window = new ProductResolverWindow(_service.ProductProfile, _advancedCanService.ReadProductResolverData) { Owner = this };
            window.ShowDialog();
        }

        private void ShowProductCurrentWindow(double requestedCurrent)
        {
            ProductCurrentWindow window = new ProductCurrentWindow(
                _advancedCanService.ReadProductCurrent,
                _service.ProductProfile.DisplayName,
                requestedCurrent)
            {
                Owner = this
            };
            window.ShowDialog();
        }
        private async void ReadDutValue_Click(object sender, RoutedEventArgs e)
        {
            string addressOffsetText = _readAddressOffsetTextBox.Text;
            string tableIndexText = _readTableIndexTextBox.Text;
            string dataSizeText = _readDataSizeTextBox.Text;
            await RunMainTestAdvancedAsync("读取 DUT 参数", "FCT_CANSignal", new Dictionary<string, object> { { "Operation", "Read" }, { "AddrOffset", ParseUInt(addressOffsetText, "AddrOffset") }, { "TableIndex", ParseInt(tableIndexText, "TableIndex") }, { "DataSize", ParseInt(dataSizeText, "DataSize") }, { "DataType", ParseInt(dataSizeText, "DataSize") == 4 ? "float32" : "uint8" }, { "ResultMode", "Information" } });
        }
        private async void SendProductSignal_Click(object sender, RoutedEventArgs e)
        {
            string signalName = _productSignalNameTextBox.Text.Trim();
            string signalValueText = _productSignalValueTextBox.Text;
            bool sendFlag = _productSignalSendFlagCheckBox.IsChecked == true;
            await RunMainTestAdvancedAsync("发送产品 DBC 信号", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "SendDbcSignal" }, { "SignalName", signalName }, { "Value", ParseDouble(signalValueText, "产品信号值") }, { "SendFlag", sendFlag }, { "ResultMode", "Action" } });
        }
        private async void SendProductRaw_Click(object sender, RoutedEventArgs e)
        {
            string idText = _productRawIdTextBox.Text;
            string dataText = _productRawDataTextBox.Text;
            await RunMainTestAdvancedAsync("发送产品原始帧", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "SendRaw" }, { "CanId", ParseCanId(idText).ToString("X", CultureInfo.InvariantCulture) }, { "DataHex", dataText }, { "ResultMode", "Information" } });
        }
        private async void ReceiveProductRaw_Click(object sender, RoutedEventArgs e)
        {
            string idText = _productReceiveIdTextBox.Text;
            await RunMainTestAdvancedAsync("读取产品接收帧", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "PRODUCTCAN" }, { "Operation", "ReceiveRaw" }, { "FilterId", ParseCanId(idText).ToString("X", CultureInfo.InvariantCulture) }, { "ResultMode", "Information" } });
        }
        private async void InitializeResolver_Click(object sender, RoutedEventArgs e) { await RunMainTestAdvancedAsync("旋变初始化", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "RESOLVER" }, { "Operation", "Init" }, { "ResultMode", "Action" } }); }
        private async void SetResolverPolePairs_Click(object sender, RoutedEventArgs e)
        {
            string polePairsText = _resolverPolePairsTextBox.Text;
            await RunMainTestAdvancedAsync("设置旋变极对数", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "RESOLVER" }, { "Operation", "SetPolePairs" }, { "PolePairs", ParseDouble(polePairsText, "极对数") }, { "ResultMode", "Action" } });
        }
        private async void SetResolverSpeed_Click(object sender, RoutedEventArgs e)
        {
            string speedText = _resolverSpeedTextBox.Text;
            await RunMainTestAdvancedAsync("设置旋变转速", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "RESOLVER" }, { "Operation", "SetSpeed" }, { "Speed", ParseDouble(speedText, "转速") }, { "PolePairs", ParseDouble(_resolverPolePairsTextBox.Text, "极对数") }, { "ResultMode", "Action" } });
        }
        private async void SetResolverPosition_Click(object sender, RoutedEventArgs e)
        {
            string positionText = _resolverPositionTextBox.Text;
            await RunMainTestAdvancedAsync("设置旋变位置", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "RESOLVER" }, { "Operation", "SetPosition" }, { "Position", ParseDouble(positionText, "位置") }, { "PolePairs", ParseDouble(_resolverPolePairsTextBox.Text, "极对数") }, { "ResultMode", "Action" } });
        }
        private async void Resolver700_Click(object sender, RoutedEventArgs e) { _resolverSpeedTextBox.Text = "700"; SetResolverSpeed_Click(sender, e); await Task.CompletedTask; }
        private async void Resolver3500_Click(object sender, RoutedEventArgs e) { _resolverSpeedTextBox.Text = "3500"; SetResolverSpeed_Click(sender, e); await Task.CompletedTask; }
        private async void Resolver7000_Click(object sender, RoutedEventArgs e) { _resolverSpeedTextBox.Text = "7000"; SetResolverSpeed_Click(sender, e); await Task.CompletedTask; }
        private async void Resolver225_Click(object sender, RoutedEventArgs e) { _resolverPositionTextBox.Text = "225"; SetResolverPosition_Click(sender, e); await Task.CompletedTask; }
        private async void Resolver315_Click(object sender, RoutedEventArgs e) { _resolverPositionTextBox.Text = "315"; SetResolverPosition_Click(sender, e); await Task.CompletedTask; }
        private async void StopResolver_Click(object sender, RoutedEventArgs e) { await RunMainTestAdvancedAsync("停止旋变", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "RESOLVER" }, { "Operation", "Stop" }, { "ResultMode", "Action" } }); }
        private async void SendResolverSignal_Click(object sender, RoutedEventArgs e)
        {
            string signalName = _resolverSignalNameTextBox.Text.Trim();
            string signalValueText = _resolverSignalValueTextBox.Text;
            bool sendFlag = _resolverSignalSendFlagCheckBox.IsChecked == true;
            await RunMainTestAdvancedAsync("发送旋变 DBC 信号", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "RESOLVER" }, { "Operation", "SendDbcSignal" }, { "SignalName", signalName }, { "Value", ParseDouble(signalValueText, "旋变信号值") }, { "SendFlag", sendFlag }, { "ResultMode", "Action" } });
        }
        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logTextBox == null || string.IsNullOrEmpty(_logTextBox.Text)) return;
            Clipboard.SetText(_logTextBox.Text);
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) { if (_logTextBox != null) _logTextBox.Clear(); }

        private UIElement BuildCustomTitleBar()
        {
            Border titleBar = new Border
            {
                Height = 32,
                Background = NewBrush(248, 250, 253),
                Padding = new Thickness(12, 0, 0, 0)
            };
            titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
            titleBar.MouseRightButtonUp += TitleBar_MouseRightButtonUp;

            DockPanel content = new DockPanel();
            StackPanel windowButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button minimize = MakeTitleBarButton("—", false);
            minimize.ToolTip = "最小化";
            minimize.Click += (s, e) => WindowState = WindowState.Minimized;
            _maximizeWindowButton = MakeTitleBarButton("□", false);
            _maximizeWindowButton.ToolTip = "最大化 / 还原";
            _maximizeWindowButton.Click += (s, e) => ToggleMaximizeWindow();
            Button close = MakeTitleBarButton("✕", true);
            close.ToolTip = "关闭";
            close.Click += (s, e) => Close();
            foreach (Button button in new[] { minimize, _maximizeWindowButton, close })
            {
                WindowChrome.SetIsHitTestVisibleInChrome(button, true);
                windowButtons.Children.Add(button);
            }
            DockPanel.SetDock(windowButtons, Dock.Right);
            content.Children.Add(windowButtons);

            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border logo = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = NewBrush(35, 112, 204),
                Margin = new Thickness(0, 0, 9, 0),
                Child = new TextBlock { Text = "F", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            title.Children.Add(logo);
            title.Children.Add(new TextBlock
            {
                Text = "FCT Engineering Studio",
                Foreground = NewBrush(37, 49, 67),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(new TextBlock
            {
                Text = "  ·  产品调试与 SEQ 开发工具",
                Foreground = NewBrush(104, 118, 138),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(title);
            titleBar.Child = content;
            return titleBar;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximizeWindow();
                return;
            }
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));
        }

        private void ToggleMaximizeWindow()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (_maximizeWindowButton == null) return;
            _maximizeWindowButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            _maximizeWindowButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
        }

        private Button MakeTitleBarButton(string text, bool isClose)
        {
            Style style = new Style(typeof(Button), Resources[typeof(Button)] as Style);
            style.Setters.Add(new Setter(Button.WidthProperty, 48d));
            style.Setters.Add(new Setter(Button.HeightProperty, 32d));
            style.Setters.Add(new Setter(Button.MinHeightProperty, 0d));
            style.Setters.Add(new Setter(Button.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.ForegroundProperty, NewBrush(75, 88, 107)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14d));
            Trigger hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Button.BackgroundProperty, isClose ? NewBrush(196, 43, 43) : NewBrush(232, 238, 247)));
            if (isClose) hover.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hover);
            return new Button { Content = text, Style = style };
        }

        private Menu BuildMainMenu()
        {
            Menu menu = new Menu
            {
                Background = NewBrush(248, 250, 253),
                Foreground = NewBrush(55, 68, 86),
                Padding = new Thickness(8, 2, 8, 2)
            };

            MenuItem file = new MenuItem { Header = "文件(_F)" };
            file.Items.Add(MakeMenuItem("新建JSON SEQ...", "Ctrl+N", NewStudioProject_Click));
            file.Items.Add(MakeMenuItem("打开JSON SEQ...", "Ctrl+O", ImportSequence_Click));
            file.Items.Add(MakeMenuItem("保存当前JSON SEQ", "Ctrl+S", SaveStudioProject_Click));
            file.Items.Add(MakeMenuItem("JSON SEQ另存为...", "Ctrl+Shift+S", SaveStudioProjectAs_Click));
            file.Items.Add(MakeMenuItem("SEQ属性...", string.Empty, StudioProjectProperties_Click));
            file.Items.Add(new Separator());
            file.Items.Add(MakeMenuItem("从其他SEQ导入测试项...", string.Empty, ImportStepsFromSequence_Click));
            file.Items.Add(new Separator());
            file.Items.Add(MakeMenuItem("退出", "Alt+F4", (s, e) => Close()));

            MenuItem run = new MenuItem { Header = "运行(_R)" };
            MenuItem runMode = new MenuItem { Header = "运行模式" };
            _editModeMenuItem = MakeMenuItem("编辑模式", string.Empty, (s, e) => SelectStudioRunMode(0)); _editModeMenuItem.IsCheckable = true;
            _debugModeMenuItem = MakeMenuItem("调试模式", string.Empty, (s, e) => SelectStudioRunMode(1)); _debugModeMenuItem.IsCheckable = true;
            runMode.Items.Add(_editModeMenuItem); runMode.Items.Add(_debugModeMenuItem); run.Items.Add(runMode); _runDebugSeparator = new Separator { Visibility = Visibility.Collapsed }; run.Items.Add(_runDebugSeparator); UpdateRunModeMenuChecks();
            _initializeWorkspaceMenuItem = MakeMenuItem("初始化当前工作区", string.Empty, InitializeCurrentWorkspace_Click); _initializeWorkspaceMenuItem.Visibility = Visibility.Collapsed; run.Items.Add(_initializeWorkspaceMenuItem);
            _safeShutdownMenuItem = MakeMenuItem("安全下电并断开", string.Empty, SafeShutdown_Click); _safeShutdownMenuItem.Visibility = Visibility.Collapsed; run.Items.Add(_safeShutdownMenuItem);

            MenuItem history = new MenuItem { Header = "编辑(_E)" };
            history.Items.Add(MakeMenuItem("撤销", "Ctrl+Z", UndoStudio_Click));
            history.Items.Add(MakeMenuItem("重做", "Ctrl+Y", RedoStudio_Click));

            MenuItem edit = new MenuItem { Header = "测试项(_S)" };
            edit.Items.Add(MakeMenuItem("打开测试项库...", "Ctrl+I", OpenTestItemLibrary_Click));
            edit.Items.Add(MakeMenuItem("复制选中测试项", "Ctrl+D", DuplicateSelectedStep_Click));
            edit.Items.Add(MakeMenuItem("删除选中测试项", "Delete", DeleteSelectedStep_Click));
            edit.Items.Add(new Separator());
            edit.Items.Add(MakeMenuItem("上移", "Alt+Up", MoveStepUp_Click));
            edit.Items.Add(MakeMenuItem("下移", "Alt+Down", MoveStepDown_Click));

            MenuItem view = new MenuItem { Header = "视图(_V)" };
            view.Items.Add(MakeMenuItem("返回上一个操作界面", "Alt+Left", NavigateBack_Click));
            view.Items.Add(new Separator());
            view.Items.Add(MakeMenuItem("简洁主工作区", string.Empty, (s, e) => { _workModeComboBox.SelectedIndex = 0; ShowStudioFlowWorkspace(null); }));
            view.Items.Add(MakeMenuItem("高级工具工作区", string.Empty, (s, e) => OpenAdvancedTool(_instrumentCenterTab)));
            view.Items.Add(new Separator());
            view.Items.Add(MakeMenuItem("显示 / 隐藏运行日志", "Ctrl+L", ToggleLog_Click));

            MenuItem tools = new MenuItem { Header = "工具(_T)" };
            tools.Items.Add(MakeMenuItem("连接产品 CAN", string.Empty, ConnectProduct_Click));
            tools.Items.Add(MakeMenuItem("连接旋变 CAN", string.Empty, ConnectResolver_Click));
            tools.Items.Add(MakeMenuItem("连接 DCDC / 辅驱 CAN", string.Empty, ConnectAuxiliary_Click));
            tools.Items.Add(MakeMenuItem("断开全部 CAN", string.Empty, DisconnectAll_Click));
            tools.Items.Add(new Separator());
            tools.Items.Add(MakeMenuItem("仪器与动作管理...", string.Empty, OpenInstrumentActionManager_Click));
            tools.Items.Add(MakeMenuItem("仪器中心", string.Empty, (s, e) => OpenAdvancedTool(_instrumentCenterTab)));
            tools.Items.Add(MakeMenuItem("原始SEQ明细", string.Empty, (s, e) => OpenAdvancedTool(_sequenceTab)));
            tools.Items.Add(MakeMenuItem("产品CAN手动调试", string.Empty, (s, e) => OpenAdvancedTool(_productCanTab)));
            tools.Items.Add(MakeMenuItem("旋变CAN手动调试", string.Empty, (s, e) => OpenAdvancedTool(_resolverTab)));

            MenuItem help = new MenuItem { Header = "帮助(_H)" };
            help.Items.Add(MakeMenuItem("功能块工作流说明", string.Empty, ShowStudioHelp_Click));
            help.Items.Add(MakeMenuItem("快捷键", string.Empty, ShowShortcutHelp_Click));
            help.Items.Add(MakeMenuItem("关于", string.Empty, ShowAbout_Click));

            menu.Items.Add(file);
            menu.Items.Add(history);
            menu.Items.Add(run);
            menu.Items.Add(view);
            menu.Items.Add(tools);
            menu.Items.Add(help);
            return menu;
        }

        private void OpenInstrumentActionManager_Click(object sender, RoutedEventArgs e) { InstrumentActionManagerWindow dialog = new InstrumentActionManagerWindow { Owner = this }; if (dialog.ShowDialog() == true) { ActionCatalog.Reload(); if (_functionBlockStudioPanel != null) _functionBlockStudioPanel.ReloadActionCatalog(); Service_Log("仪器与动作目录已重新加载：" + ActionCatalog.ConfigurationPath); } }

        private StatusBar BuildStatusBar()
        {
            StatusBar bar = new StatusBar
            {
                Background = NewBrush(248, 250, 253),
                Foreground = NewBrush(75, 88, 107),
                Padding = new Thickness(10, 3, 10, 3)
            };
            _applicationStatusText = new TextBlock { Text = "就绪", Width = 330, TextTrimming = TextTrimming.CharacterEllipsis };
            _currentFileText = new TextBlock { Text = "当前文件：未加载", Width = 470, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            _productSummaryText = new TextBlock { Text = "产品：" + _service.ProductProfile.Model, Margin = new Thickness(8, 0, 8, 0) };
            TextBlock engine = new TextBlock { Text = ".NET Framework 4.8 · x86 · 原平台 TestDllMain", Foreground = NewBrush(104, 118, 138) };
            bar.Items.Add(_applicationStatusText);
            bar.Items.Add(new Separator());
            bar.Items.Add(_currentFileText);
            bar.Items.Add(new Separator());
            bar.Items.Add(_productSummaryText);
            bar.Items.Add(new Separator());
            bar.Items.Add(engine);
            return bar;
        }

        private void ApplyProfessionalTheme()
        {
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 11;

            Style buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.White));
            buttonStyle.Setters.Add(new Setter(Button.ForegroundProperty, NewBrush(43, 55, 72)));
            buttonStyle.Setters.Add(new Setter(Button.BorderBrushProperty, NewBrush(220, 228, 239)));
            buttonStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            buttonStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(8, 4, 8, 4)));
            buttonStyle.Setters.Add(new Setter(Button.MarginProperty, new Thickness(2)));
            buttonStyle.Setters.Add(new Setter(Button.MinHeightProperty, 27d));
            buttonStyle.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            Trigger buttonHover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            buttonHover.Setters.Add(new Setter(Button.BackgroundProperty, NewBrush(228, 237, 248)));
            buttonHover.Setters.Add(new Setter(Button.BorderBrushProperty, NewBrush(109, 146, 190)));
            buttonStyle.Triggers.Add(buttonHover);
            Trigger buttonDisabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            buttonDisabled.Setters.Add(new Setter(Button.OpacityProperty, 0.48d));
            buttonStyle.Triggers.Add(buttonDisabled);
            Resources[typeof(Button)] = buttonStyle;

            Style inputStyle = new Style(typeof(TextBox));
            inputStyle.Setters.Add(new Setter(TextBox.BackgroundProperty, Brushes.White));
            inputStyle.Setters.Add(new Setter(TextBox.BorderBrushProperty, NewBrush(220, 228, 239)));
            inputStyle.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(1)));
            inputStyle.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(6, 4, 6, 4)));
            inputStyle.Setters.Add(new Setter(TextBox.MarginProperty, new Thickness(3)));
            Resources[typeof(TextBox)] = inputStyle;

            Style comboStyle = new Style(typeof(ComboBox));
            comboStyle.Setters.Add(new Setter(ComboBox.BackgroundProperty, Brushes.White));
            comboStyle.Setters.Add(new Setter(ComboBox.BorderBrushProperty, NewBrush(220, 228, 239)));
            comboStyle.Setters.Add(new Setter(ComboBox.PaddingProperty, new Thickness(5, 3, 5, 3)));
            comboStyle.Setters.Add(new Setter(ComboBox.MinHeightProperty, 26d));
            Resources[typeof(ComboBox)] = comboStyle;

            Style groupStyle = new Style(typeof(GroupBox));
            groupStyle.Setters.Add(new Setter(GroupBox.ForegroundProperty, NewBrush(45, 58, 77)));
            groupStyle.Setters.Add(new Setter(GroupBox.BorderBrushProperty, NewBrush(210, 218, 229)));
            groupStyle.Setters.Add(new Setter(GroupBox.BackgroundProperty, Brushes.White));
            groupStyle.Setters.Add(new Setter(GroupBox.MarginProperty, new Thickness(5)));
            groupStyle.Setters.Add(new Setter(GroupBox.PaddingProperty, new Thickness(7)));
            Resources[typeof(GroupBox)] = groupStyle;

            Style tabStyle = new Style(typeof(TabItem));
            tabStyle.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(12, 6, 12, 6)));
            tabStyle.Setters.Add(new Setter(TabItem.ForegroundProperty, NewBrush(60, 72, 89)));
            tabStyle.Setters.Add(new Setter(TabItem.FontWeightProperty, FontWeights.Normal));
            Trigger selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(TabItem.ForegroundProperty, NewBrush(28, 92, 171)));
            selected.Setters.Add(new Setter(TabItem.FontWeightProperty, FontWeights.SemiBold));
            tabStyle.Triggers.Add(selected);
            Resources[typeof(TabItem)] = tabStyle;

            Style gridStyle = new Style(typeof(DataGrid));
            gridStyle.Setters.Add(new Setter(DataGrid.BorderBrushProperty, NewBrush(210, 218, 229)));
            gridStyle.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, NewBrush(231, 235, 241)));
            gridStyle.Setters.Add(new Setter(DataGrid.VerticalGridLinesBrushProperty, NewBrush(231, 235, 241)));
            gridStyle.Setters.Add(new Setter(DataGrid.RowHeightProperty, 28d));
            gridStyle.Setters.Add(new Setter(DataGrid.ColumnHeaderHeightProperty, 31d));
            Resources[typeof(DataGrid)] = gridStyle;
            Resources[typeof(ComboBox)] = StudioControlTheme.ComboBoxStyle();
            Resources[typeof(ScrollBar)] = StudioControlTheme.ScrollBarStyle();
        }

        private void ToggleLog_Click(object sender, RoutedEventArgs e)
        {
            _logVisible = !_logVisible;
            _logPanel.Visibility = _logVisible ? Visibility.Visible : Visibility.Collapsed;
            _logRowDefinition.Height = _logVisible ? new GridLength(210) : new GridLength(0);
            SetApplicationStatus(_logVisible ? "运行日志已展开" : "运行日志已收起");
        }

        private void SequenceSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_workflowSteps == null || _sequenceList == null) return;
            string keyword = (_sequenceSearchTextBox.Text ?? string.Empty).Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(_sequenceList.ItemsSource);
            view.Filter = item =>
            {
                WorkflowStepState step = item as WorkflowStepState;
                return step != null && (keyword.Length == 0 ||
                    step.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    step.FunctionName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    step.Number.ToString(CultureInfo.InvariantCulture).Contains(keyword));
            };
            view.Refresh();
            if (_sequenceSummaryText != null)
                _sequenceSummaryText.Text = keyword.Length == 0 ? _workflowSteps.Count + " STEP" : view.Cast<object>().Count() + " / " + _workflowSteps.Count + " STEP";
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S) { SaveStudioProjectAs_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { NewStudioProject_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { UndoStudio_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { RedoStudio_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { ImportSequence_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { SaveStudioProject_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { ToggleLog_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.I) { OpenTestItemLibrary_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D) { DuplicateSelectedStep_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Left) { NavigateBack_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Up) { MoveStepUp_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Down) { MoveStepDown_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.Delete && !(Keyboard.FocusedElement is TextBoxBase)) { DeleteSelectedStep_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.F6) { if (_advancedManualMode) ExecuteSequence_Click(this, new RoutedEventArgs()); else ShowStudioFlowWorkspace(null); e.Handled = true; }
            else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Shift) { if (_studioDebugActive) StopStudioDebug(); else StopWorkflow_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.F5) { if (_advancedManualMode) RunAllWorkflow_Click(this, new RoutedEventArgs()); else { ShowStudioFlowWorkspace(null); SetApplicationStatus(_workModeComboBox.SelectedIndex == 1 ? "请使用流程调试栏运行功能块流程" : "当前为编辑模式，请先切换到调试模式"); } e.Handled = true; }
        }

        private void ShowShortcutHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "Ctrl+N  新建JSON SEQ\nCtrl+O  打开JSON SEQ\nCtrl+S  保存当前JSON SEQ\nCtrl+Shift+S  JSON SEQ另存为\nCtrl+Z / Ctrl+Y  撤销 / 重做\nCtrl+L  显示/隐藏日志\nF5  切换到测试流程页\nShift+F5  停止并安全下电", "快捷键", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowStudioHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "推荐使用顺序：\n\n1. 新建并命名JSON SEQ。\n2. 在“自定义功能块”页创建模块并直接填写实际值。\n3. 在“流程调试与编辑”页把模块加入流程并排序。\n4. 初始化工作区后单步或连续调试。\n5. 按Ctrl+S直接保存为平台JSON SEQ。\n\n程序只使用JSON文件；模块库也使用独立JSON文件保存。", "功能块工作流", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "FCT Engineering Studio\n产品调试、原平台STEP执行与SEQ开发工具\n.NET Framework 4.8 · x86", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SetApplicationStatus(string text)
        {
            if (_applicationStatusText != null) _applicationStatusText.Text = text;
        }
        private void UpdateCurrentFileDisplay()
        {
            string path = !string.IsNullOrWhiteSpace(_loadedSequencePath) ? _loadedSequencePath : _studioProjectPath;
            string product = _studioProject == null || string.IsNullOrWhiteSpace(_studioProject.Product) ? _service.ProductProfile.Model.ToString() : _studioProject.Product;
            string sequenceName = !string.IsNullOrWhiteSpace(_studioProjectPath) ? Path.GetFileName(_studioProjectPath) : !string.IsNullOrWhiteSpace(_loadedSequencePath) ? Path.GetFileName(_loadedSequencePath) : "未加载SEQ";
            if (_currentFileText != null) { _currentFileText.Text = string.IsNullOrWhiteSpace(path) ? "当前文件：未加载" : "当前SEQ：" + Path.GetFileName(path) + (_studioProjectDirty ? "  *已修改" : string.Empty); _currentFileText.ToolTip = path ?? string.Empty; }
            if (_headerProductText != null) _headerProductText.Text = product;
            if (_headerSequenceText != null) { _headerSequenceText.Text = sequenceName; _headerSequenceText.ToolTip = path ?? string.Empty; }
            if (_headerDirtyText != null) _headerDirtyText.Text = _studioProjectDirty ? "已修改" : "已保存";
            if (_headerDirtyBadge != null) { _headerDirtyBadge.Background = _studioProjectDirty ? NewBrush(255, 248, 235) : NewBrush(241, 251, 245); _headerDirtyBadge.BorderBrush = _studioProjectDirty ? NewBrush(238, 142, 25) : NewBrush(42, 160, 91); }
            if (_headerDirtyText != null) _headerDirtyText.Foreground = _studioProjectDirty ? NewBrush(222, 126, 13) : NewBrush(34, 145, 82);
            if (_headerSavePathText != null) { bool showSavedPath = !_studioProjectDirty && !string.IsNullOrWhiteSpace(path); _headerSavePathText.Text = showSavedPath ? path : string.Empty; _headerSavePathText.ToolTip = showSavedPath ? path : null; _headerSavePathText.Visibility = showSavedPath ? Visibility.Visible : Visibility.Collapsed; }
        }

        private async Task RunActionAsync(string actionName, Action action, Action onSuccess = null)
        {
            try
            {
                SetApplicationStatus("正在执行：" + actionName);
                IsEnabled = false;
                await Task.Run(action);
                if (onSuccess != null) onSuccess();
                SetApplicationStatus(actionName + "完成");
            }
            catch (Exception ex)
            {
                SetApplicationStatus(actionName + "失败");
                Service_Log(actionName + "失败：" + ex.Message);
                MessageBox.Show(this, actionName + "失败：\n" + ex.Message, "CAN Debug", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsEnabled = true; }
        }

        private void Service_Log(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_logTextBox == null) return;
                if (_logTextBox.Text.Length > 1000000)
                {
                    int firstLineBreak = _logTextBox.Text.IndexOf('\n', 250000);
                    _logTextBox.Text = firstLineBreak >= 0
                        ? _logTextBox.Text.Substring(firstLineBreak + 1)
                        : string.Empty;
                }
                _logTextBox.AppendText(message + Environment.NewLine);
                _logTextBox.ScrollToEnd();
                SetApplicationStatus(message.Length > 90 ? message.Substring(0, 90) + "..." : message);
            }));
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ConfirmDiscardStudioChanges()) { e.Cancel = true; return; }
            if (_legacyRuntime != null)
            {
                _legacyRuntime.Log -= Service_Log;
                _legacyRuntime.CurrentStepChanged -= LegacyRuntime_CurrentStepChanged;
                _legacyRuntime.Dispose();
            }
            _service.Dispose();
        }

        private static GroupBox MakeGroup(string header, UIElement content) { return new GroupBox { Header = header, Content = content }; }
        private static WrapPanel MakeRow(params UIElement[] children) { WrapPanel row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (UIElement child in children) row.Children.Add(child); return row; }
        private static Button MakeButton(string text, RoutedEventHandler handler, double width = double.NaN)
        {
            Button button = new Button { Content = text };
            if (!double.IsNaN(width)) button.Width = width;
            button.Click += handler;
            return button;
        }
        private static Button MakePrimaryButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = MakeButton(text, handler, width);
            button.Background = NewBrush(32, 104, 190);
            button.BorderBrush = NewBrush(32, 104, 190);
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.SemiBold;
            return button;
        }
        private static Button MakeSuccessButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = MakeButton(text, handler, width);
            button.Background = NewBrush(37, 145, 91);
            button.BorderBrush = NewBrush(37, 145, 91);
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.SemiBold;
            return button;
        }
        private static Button MakeDangerButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = MakeButton(text, handler, width);
            button.Background = NewBrush(196, 66, 66);
            button.BorderBrush = NewBrush(196, 66, 66);
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.SemiBold;
            return button;
        }
        private static TextBlock MakeLabel(string text, Brush brush = null) { return new TextBlock { Text = text, Foreground = brush ?? NewBrush(48, 60, 78), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 3, 4, 3) }; }
        private static TextBlock MakeFieldLabel(string text) { return new TextBlock { Text = text, Foreground = NewBrush(89, 101, 117), FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 3, 2, 3) }; }
        private static TextBlock MakeStatusText(string text) { return new TextBlock { Text = text, Foreground = NewBrush(190, 59, 59), FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; }
        private static TextBox MakeBox(string text, double width) { return new TextBox { Text = text, Width = width }; }
        private static Border MakeCard(Thickness margin)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = NewBrush(210, 218, 229),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = margin
            };
        }
        private static MenuItem MakeMenuItem(string header, string shortcut, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem { Header = header, InputGestureText = shortcut, Foreground = NewBrush(38, 49, 66), Padding = new Thickness(8, 4, 16, 4) };
            item.Click += handler;
            return item;
        }
        private static SolidColorBrush NewBrush(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
        private static double ParseDouble(string text, string name) { double value; if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new FormatException(name + "不是有效数字。"); return value; }
        private static int ParseInt(string text, string name) { int value; if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new FormatException(name + "不是有效整数。"); return value; }
        private static uint ParseUInt(string text, string name) { string valueText = text.Trim(); uint value; bool parsed = valueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? uint.TryParse(valueText.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) : uint.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value); if (!parsed) throw new FormatException(name + "不是有效无符号整数。"); return value; }
        private static uint ParseCanId(string text) { uint value = ParseUInt(text, "CAN ID"); if (value > 0x1FFFFFFF) throw new ArgumentOutOfRangeException(nameof(text), "CAN ID 超出 29 位范围。"); return value; }
    }

    internal sealed class WorkflowStepState : INotifyPropertyChanged
    {
        private string _status = "待运行";

        public WorkflowStepState(int number, SequenceStepDefinition definition)
        {
            Number = number;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public int Number { get; private set; }
        public SequenceStepDefinition Definition { get; private set; }
        public string Name { get { return Definition.StepName; } }
        public string FunctionName { get { return Definition.FunctionName; } }
        public bool IsExecutable { get { return true; } }
        public double Value
        {
            get
            {
                if (FunctionName == "Resolver_SetSpeed") return Definition.GetDouble("Speed");
                if (FunctionName == "Resolver_SetPosition") return Definition.GetDouble("Position");
                if (FunctionName == "CAN_SetDUTCurrent") return Definition.GetDouble("MaxCurrent");
                if (FunctionName == "CAN_ReadDutCurrent" || FunctionName == "Test_UVW_Current_RMS")
                    return (Definition.GetDouble("LowLimit") + Definition.GetDouble("HighLimit")) / 2.0;
                return 0;
            }
        }
        public double StepCurrent { get { return Definition.GetDouble("StepCurrent", 20); } }
        public double HoldTime { get { return Definition.GetDouble("HoldTime", 10); } }
        public double Frequency { get { return Definition.GetDouble("Frequency", 60); } }
        public string Status
        {
            get { return _status; }
            set { if (_status == value) return; _status = value; Raise("Status"); Raise("DisplayText"); }
        }
        public string DisplayText
        {
            get
            {
                string icon = Status == "运行中" ? "▶" : Status == "完成" ? "✓" : Status == "失败" ? "✕" : Status == "已停止" ? "■" : Status == "跳过" ? "↷" : Status == "未接入" ? "◇" : "·";
                List<string> parameters = Definition.Parameters.Take(2).Select(pair => pair.Key + "=" + Convert.ToString(pair.Value, CultureInfo.InvariantCulture)).ToList();
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:000}. {2}{3}{4}", icon, Number, Name,
                    parameters.Count == 0 ? string.Empty : "  [" + string.Join(", ", parameters) + "]",
                    string.Empty);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Refresh() { Raise("Name"); Raise("DisplayText"); }
        public void Renumber(int number) { Number = number; Raise("Number"); Raise("DisplayText"); }

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    internal sealed class StudioNavigationState
    {
        public TabItem MainTab { get; set; }
        public TabItem AdvancedTab { get; set; }
        public bool BlockMode { get; set; }
        public string BlockId { get; set; }
        public string BlockStepId { get; set; }
        public string FlowInstanceId { get; set; }
        public bool SamePosition(StudioNavigationState other) { return other != null && ReferenceEquals(MainTab, other.MainTab) && ReferenceEquals(AdvancedTab, other.AdvancedTab) && BlockMode == other.BlockMode && string.Equals(BlockId, other.BlockId, StringComparison.Ordinal) && string.Equals(BlockStepId, other.BlockStepId, StringComparison.Ordinal) && string.Equals(FlowInstanceId, other.FlowInstanceId, StringComparison.Ordinal); }
    }

    internal sealed class WorkflowParameterRow : INotifyPropertyChanged
    {
        private string _valueText;
        public WorkflowParameterRow(string name, object value)
        {
            Name = name;
            OriginalType = value == null ? typeof(string) : value.GetType();
            TypeName = OriginalType == typeof(bool) ? "Bool" : OriginalType == typeof(int) || OriginalType == typeof(long) ? "Integer" : OriginalType == typeof(double) || OriginalType == typeof(float) || OriginalType == typeof(decimal) ? "Number" : "Text";
            _valueText = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        public string Name { get; private set; }
        public Type OriginalType { get; private set; }
        public string TypeName { get; private set; }
        public string ValueText { get { return _valueText; } set { if (_valueText == value) return; _valueText = value; PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs("ValueText")); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
