using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class FunctionBlockStudioPanel : Grid
    {
        private readonly Func<FctStudioProject> _getProject;
        private readonly Func<IEnumerable<SequenceStepDefinition>> _getSteps;
        private readonly ProductLocatorRepository _locatorRepository;
        private readonly Action<string> _log;
        private readonly Action _changed;
        private readonly Func<SequenceStepDefinition, Task<string>> _executeStep;
        private readonly Func<LegacyStepExecutionResult> _getLastPlatformResult;
        private readonly Action<FunctionBlockDefinition> _openBlockEditor;
        private readonly Action _returnToFlow;
        private readonly ObservableCollection<BlockListItem> _blocks = new ObservableCollection<BlockListItem>();
        private readonly ObservableCollection<BlockStepListItem> _steps = new ObservableCollection<BlockStepListItem>();
        private readonly ObservableCollection<StudioStepParameterRow> _parameters = new ObservableCollection<StudioStepParameterRow>();
        private readonly ObservableCollection<ActionHistoryRow> _history = new ObservableCollection<ActionHistoryRow>();
        private readonly ObservableCollection<LegacyPlatformResultRow> _latestPlatformResults = new ObservableCollection<LegacyPlatformResultRow>();
        private readonly ObservableCollection<ModuleReferenceParameterRow> _moduleReferenceParameters = new ObservableCollection<ModuleReferenceParameterRow>();
        private ListBox _blockList;
        private DataGrid _stepList;
        private DataGrid _parameterGrid;
        private TextBox _blockName;
        private TextBox _blockCategory;
        private TextBox _blockVersion;
        private TextBox _blockProducts;
        private TextBox _blockDescription;
        private TextBlock _stepTitle;
        private TextBlock _functionNameText;
        private TextBox _stepNameBox;
        private TextBox _actionDescriptionBox;
        private TextBox _actionModuleBox;
        private TextBox _actionTypeBox;
        private ComboBox _resultMode;
        private ComboBox _stepRunMode;
        private CheckBox _stepRecordingLog;
        private FunctionBlockDefinition _selectedBlock;
        private BlockStepDefinition _selectedStep;
        private bool _loadingStep;
        private bool _showingSearchPlaceholder;
        private TextBox _blockSearch;
        private ComboBox _blockCategoryFilter;
        private TextBlock _blockSummary;
        private TextBlock _actionResult;
        private ListBox _historyList;
        private DataGrid _platformResultGrid;
        private DataGrid _moduleReferenceParameterGrid;
        private TextBlock _debugResultSummary;
        private TextBox _debugCanDetails;
        private RowDefinition _actionTableRow;
        private RowDefinition _editorGapRow;
        private RowDefinition _editorBodyRow;
        private RowDefinition _editorContentRow;
        private TabControl _editorTabs;
        private TabItem _actionConfigurationTab;
        private TabItem _moduleParametersTab;
        private Grid _moduleReferenceConfigurationPanel;
        private TextBox _moduleReferenceInstanceNameBox;
        private ComboBox _moduleReferenceBindingBox;
        private bool _loadingModuleReferenceConfiguration;
        private Button _toggleEditorButton;
        private Button _returnToFlowButton;
        private FunctionBlockDefinition _blockClipboard;
        private BlockStepDefinition _stepClipboard;
        private ActionConfigurationPanel _actionConfigurator;
        private Point _stepDragStart;
        private BlockStepListItem _stepDragItem;
        private bool _stepDragArmed;
        private DataGridRow _stepDropTargetRow;
        private Point _blockDragStart;
        private BlockListItem _blockDragItem;
        private FunctionBlockDefinition _moduleReferenceTargetBlock;
        private bool _blockDragArmed;
        private Button _runModuleButton;
        private StackPanel _moduleDebugToolbar;
        private TabItem _debugRecordTab;
        private bool _debugMode;
        private bool _moduleRunning;
        private readonly Action<bool> _libraryVisibilityChanged;
        private Border _libraryShell;
        private GridSplitter _librarySplitter;
        private Button _closeLibraryButton;
        private double _libraryDrawerWidth = 280d;

        public FunctionBlockStudioPanel(Func<FctStudioProject> getProject, Func<IEnumerable<SequenceStepDefinition>> getSteps, ProductLocatorRepository locatorRepository, Action<string> log, Action changed, Func<SequenceStepDefinition, Task<string>> executeStep, Func<LegacyStepExecutionResult> getLastPlatformResult, Action<FunctionBlockDefinition> openBlockEditor = null, Action returnToFlow = null, Action<bool> libraryVisibilityChanged = null)
        {
            _getProject = getProject; _getSteps = getSteps; _locatorRepository = locatorRepository; _log = log; _changed = delegate { if (GlobalModuleLibraryService.IsReusable(_selectedBlock)) GlobalModuleLibraryService.Save(_selectedBlock); changed(); }; _executeStep = executeStep; _getLastPlatformResult = getLastPlatformResult; _openBlockEditor = openBlockEditor; _returnToFlow = returnToFlow; _libraryVisibilityChanged = libraryVisibilityChanged;
            Background = PageBackground(); TextElement.SetFontFamily(this, new FontFamily("Segoe UI")); TextElement.SetFontSize(this, 13); TextElement.SetFontWeight(this, FontWeights.Normal); StudioControlTheme.Apply(Resources);
            BuildUi(); ApplyUnifiedStepAlignment(); SetLibraryDrawerOpen(false);
        }

        public bool IsLibraryDrawerOpen { get { return ColumnDefinitions.Count > 0 && ColumnDefinitions[0].Width.Value > 0; } }
        public void SetLibraryDrawerOpen(bool open)
        {
            if (ColumnDefinitions.Count < 2) return; if (!open && ColumnDefinitions[0].ActualWidth > 0) _libraryDrawerWidth = Math.Max(220d, ColumnDefinitions[0].ActualWidth); ColumnDefinitions[0].MinWidth = open ? 220d : 0d; ColumnDefinitions[0].Width = open ? new GridLength(Math.Max(220d, _libraryDrawerWidth)) : new GridLength(0); ColumnDefinitions[1].Width = open ? new GridLength(8) : new GridLength(0); if (_libraryShell != null) _libraryShell.Visibility = open ? Visibility.Visible : Visibility.Collapsed; if (_librarySplitter != null) _librarySplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed; if (open) CollapseLibraryGroups();
        }
        private void CloseLibraryDrawer_Click(object sender, RoutedEventArgs e) { SetLibraryDrawerOpen(false); if (_libraryVisibilityChanged != null) _libraryVisibilityChanged(false); }
        private void CollapseLibraryGroups() { foreach (BlockListItem item in _blocks) item.IsExpanded = false; if (_blockList == null) return; _blockList.UpdateLayout(); CollapseExpanders(_blockList); }
        private static void CollapseExpanders(DependencyObject parent) { if (parent == null) return; int count = VisualTreeHelper.GetChildrenCount(parent); for (int index = 0; index < count; index++) { DependencyObject child = VisualTreeHelper.GetChild(parent, index); Expander expander = child as Expander; if (expander != null && expander.DataContext is CollectionViewGroup) expander.IsExpanded = false; CollapseExpanders(child); } }

        private Grid BuildModuleReferenceConfigurationPanel()
        {
            Grid panel = new Grid { Margin = new Thickness(24, 18, 24, 18), VerticalAlignment = VerticalAlignment.Top };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) }); panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480) }); panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            TextBlock title = new TextBlock { Text = "模块引用配置", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumnSpan(title, 3); panel.Children.Add(title);
            TextBlock instanceLabel = new TextBlock { Text = "引用实例名称", FontSize = 13, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center, ToolTip = "当前父模块中这一条模块调用显示的名称" }; Grid.SetRow(instanceLabel, 1); panel.Children.Add(instanceLabel);
            _moduleReferenceInstanceNameBox = new TextBox { Height = 32, Padding = new Thickness(9, 5, 9, 5), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "可自由命名；同一模块的多次调用可以使用不同名称" }; _moduleReferenceInstanceNameBox.LostKeyboardFocus += (s, e) => CommitStep(); _moduleReferenceInstanceNameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitStep(); Keyboard.ClearFocus(); e.Handled = true; } }; Grid.SetRow(_moduleReferenceInstanceNameBox, 1); Grid.SetColumn(_moduleReferenceInstanceNameBox, 1); panel.Children.Add(_moduleReferenceInstanceNameBox);
            TextBlock bindingLabel = new TextBlock { Text = "绑定模块", FontSize = 13, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center, ToolTip = "决定该调用实际执行哪个模块" }; Grid.SetRow(bindingLabel, 2); panel.Children.Add(bindingLabel);
            _moduleReferenceBindingBox = new ComboBox { Height = 32, ToolTip = "可绑定标准模块、产品模块或自定义模块" }; _moduleReferenceBindingBox.SelectionChanged += ModuleReferenceBinding_SelectionChanged; Grid.SetRow(_moduleReferenceBindingBox, 2); Grid.SetColumn(_moduleReferenceBindingBox, 1); panel.Children.Add(_moduleReferenceBindingBox);
            TextBlock hint = new TextBlock { Text = "引用实例名称只影响当前调用的显示；绑定模块决定实际执行内容。重新绑定后会自动刷新“模块参数”。", Foreground = TextSecondary(), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(hint, 3); Grid.SetColumnSpan(hint, 2); panel.Children.Add(hint);
            Button apply = PrimaryButton("应用引用配置", (s, e) => { CommitStep(); LoadStep(); }); apply.HorizontalAlignment = HorizontalAlignment.Left; apply.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(apply, 3); Grid.SetColumn(apply, 2); panel.Children.Add(apply);
            return panel;
        }

        private void LoadModuleReferenceConfiguration(FunctionBlockDefinition referenced)
        {
            if (_moduleReferenceInstanceNameBox == null || _moduleReferenceBindingBox == null) return;
            _loadingModuleReferenceConfiguration = true;
            try
            {
                _moduleReferenceInstanceNameBox.Text = _selectedStep == null ? string.Empty : _selectedStep.ReferencedBlockName ?? string.Empty;
                List<FunctionBlockDefinition> candidates = (_getProject() == null ? Enumerable.Empty<FunctionBlockDefinition>() : _getProject().Blocks).Where(value => value != null && value != _selectedBlock && (_selectedBlock == null || !WouldCreateModuleCycle(value, _selectedBlock.Id, new HashSet<string>(StringComparer.Ordinal)))).OrderBy(ModuleKindOrder).ThenBy(value => value.Name).ToList();
                if (referenced != null && candidates.All(value => value.Id != referenced.Id)) candidates.Insert(0, referenced);
                List<ModuleBindingChoice> choices = candidates.Select(value => new ModuleBindingChoice(value)).ToList(); _moduleReferenceBindingBox.ItemsSource = choices; _moduleReferenceBindingBox.SelectedItem = choices.FirstOrDefault(value => referenced != null && value.Block.Id == referenced.Id);
            }
            finally { _loadingModuleReferenceConfiguration = false; }
        }

        private void ModuleReferenceBinding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingModuleReferenceConfiguration || _loadingStep || _selectedStep == null || !_selectedStep.IsModuleReference) return;
            CommitStep(); LoadStep();
        }
        private void ApplyUnifiedStepAlignment() { if (_stepList == null) return; Style cell = new Style(typeof(DataGridCell)); cell.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)); cell.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); cell.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent)); cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); cell.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); Trigger selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true }; selectedCell.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold)); selectedCell.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.Bold)); selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); selectedCell.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent)); selectedCell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); cell.Triggers.Add(selectedCell); _stepList.CellStyle = cell; Style header = new Style(typeof(DataGridColumnHeader)); header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)); header.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); _stepList.ColumnHeaderStyle = header; foreach (DataGridTextColumn column in _stepList.Columns.OfType<DataGridTextColumn>()) column.ElementStyle = MultilineCellStyle(TextPrimary(), FontWeights.Normal); }

        public void RefreshProject()
        {
            CommitBlock(); CommitStep(); _blocks.Clear();
            FctStudioProject project = _getProject();
            GlobalModuleLibraryService.MergeInto(project);
            if (project != null) foreach (FunctionBlockDefinition block in project.Blocks) _blocks.Add(CreateBlockListItem(block));
            if (_blockCategoryFilter != null) { _blockCategoryFilter.ItemsSource = new[] { "全部模块" }.Concat(_blocks.Select(item => item.Block.Category).Distinct().OrderBy(value => value)).ToArray(); _blockCategoryFilter.SelectedIndex = 0; }
            if (_blocks.Count > 0) _blockList.SelectedItem = _blocks.FirstOrDefault(item => item.Block.Name.IndexOf("高压上电", StringComparison.OrdinalIgnoreCase) >= 0) ?? _blocks[0];
            else { _selectedBlock = null; _steps.Clear(); _parameters.Clear(); }
        }
        private void SynchronizeSelectedBlockToFlow()
        {
            FctStudioProject project = _getProject(); if (project == null || _selectedBlock == null || project.Flow == null) return; foreach (FlowBlockInstance instance in project.Flow.Where(value => value.BlockId == _selectedBlock.Id)) { Dictionary<string, object> previous = new Dictionary<string, object>(instance.ParameterOverrides, StringComparer.Ordinal); instance.Snapshot = _selectedBlock.Clone(); if (string.IsNullOrWhiteSpace(instance.DisplayName)) instance.DisplayName = _selectedBlock.Name; if (string.Equals(_selectedBlock.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase)) instance.PreserveStepNames = false; instance.ParameterOverrides.Clear(); foreach (BlockParameterDefinition parameter in _selectedBlock.Parameters) { object value; instance.ParameterOverrides[parameter.Name] = previous.TryGetValue(parameter.Name, out value) ? value : parameter.DefaultValue; } }
        }
        public bool SelectBlock(string blockId)
        {
            BlockListItem item = _blocks.FirstOrDefault(value => value.Block != null && string.Equals(value.Block.Id, blockId, StringComparison.Ordinal)); if (item == null) return false; _blockList.SelectedItem = item; _blockList.ScrollIntoView(item); EnsureEditorExpanded(); return true;
        }
        internal string SelectedBlockId { get { return _selectedBlock == null ? string.Empty : _selectedBlock.Id; } }
        internal string SelectedStepId { get { return _selectedStep == null ? string.Empty : _selectedStep.Id; } }
        internal void CommitPendingChanges() { CommitStep(); CommitBlock(); }
        internal void RestoreNavigation(string blockId, string stepId) { if (!SelectBlock(blockId) || string.IsNullOrWhiteSpace(stepId)) return; BlockStepListItem row = _steps.FirstOrDefault(value => value.Step.Id == stepId); if (row != null) { _stepList.SelectedItem = row; _stepList.ScrollIntoView(row); } }
        public void ReloadActionCatalog() { if (_stepList != null) _stepList.ContextMenu = BuildStepContextMenu(); if (_actionConfigurator != null) _actionConfigurator.LoadStep(_selectedStep == null ? null : _selectedStep.ToStep(), _selectedStep == null ? null : _selectedStep.ParameterBindings); }

        private void BuildUi()
        {
            Margin = new Thickness(0); Background = PageBackground();
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 220, MaxWidth = 520 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 640 });

            _libraryShell = Surface(); Grid left = new Grid(); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            DockPanel libraryHeader = new DockPanel { Margin = new Thickness(8, 7, 7, 3) }; StackPanel headerActions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(headerActions, Dock.Right); headerActions.Children.Add(IconText("\uE700", "模块菜单")); _closeLibraryButton = IconButton("\uE711", "关闭模块库", CloseLibraryDrawer_Click); _closeLibraryButton.Width = 28; _closeLibraryButton.Height = 28; _closeLibraryButton.Padding = new Thickness(0); _closeLibraryButton.Background = Brushes.Transparent; _closeLibraryButton.BorderThickness = new Thickness(0); headerActions.Children.Add(_closeLibraryButton); libraryHeader.Children.Add(headerActions); if (_returnToFlow != null) { _returnToFlowButton = GhostButton("← 返回序列排序", (s, e) => _returnToFlow()); _returnToFlowButton.Height = 29; _returnToFlowButton.MinWidth = 104; _returnToFlowButton.Padding = new Thickness(8, 3, 8, 3); _returnToFlowButton.Margin = new Thickness(0, 0, 7, 0); _returnToFlowButton.FontSize = 11.5; _returnToFlowButton.ToolTip = "返回刚才的序列模块排序位置"; DockPanel.SetDock(_returnToFlowButton, Dock.Left); libraryHeader.Children.Add(_returnToFlowButton); } libraryHeader.Children.Add(SectionTitle("模块库")); left.Children.Add(libraryHeader);
            _blockSearch = new TextBox { Text = "搜索模块名称", Foreground = TextSecondary(), Margin = new Thickness(10, 0, 10, 5), Height = 31, Padding = new Thickness(27, 5, 7, 5), ToolTip = "搜索模块名称" }; _showingSearchPlaceholder = true; _blockSearch.GotKeyboardFocus += delegate { if (_showingSearchPlaceholder) { _showingSearchPlaceholder = false; _blockSearch.Text = string.Empty; _blockSearch.Foreground = TextPrimary(); } }; _blockSearch.LostKeyboardFocus += delegate { if (string.IsNullOrWhiteSpace(_blockSearch.Text)) { _showingSearchPlaceholder = true; _blockSearch.Text = "搜索模块名称"; _blockSearch.Foreground = TextSecondary(); } }; _blockSearch.TextChanged += BlockSearch_TextChanged; Grid.SetRow(_blockSearch, 1); left.Children.Add(_blockSearch); TextBlock searchIcon = IconText("\uE721", "搜索"); searchIcon.Margin = new Thickness(19, 0, 0, 5); searchIcon.HorizontalAlignment = HorizontalAlignment.Left; searchIcon.IsHitTestVisible = false; Grid.SetRow(searchIcon, 1); Panel.SetZIndex(searchIcon, 2); left.Children.Add(searchIcon);
            _blockCategoryFilter = new ComboBox { Margin = new Thickness(10, 0, 10, 6), Height = 32, ToolTip = "按测试领域筛选" }; _blockCategoryFilter.SelectionChanged += BlockSearch_TextChanged; Grid.SetRow(_blockCategoryFilter, 2); left.Children.Add(_blockCategoryFilter);
            ListCollectionView blockView = new ListCollectionView(_blocks); blockView.GroupDescriptions.Add(new PropertyGroupDescription("LibraryGroup")); _blockList = StudioModuleLibraryList.Create(blockView, true, SelectChildModuleFromTree); _blockList.SelectionChanged += BlockList_SelectionChanged; _blockList.PreviewMouseRightButtonDown += BlockList_RightButtonDown; _blockList.PreviewMouseLeftButtonDown += BlockList_LeftButtonDown; _blockList.PreviewMouseLeftButtonUp += BlockList_LeftButtonUp; _blockList.PreviewMouseMove += BlockList_MouseMove; _blockList.ContextMenu = BuildBlockContextMenu(); Grid.SetRow(_blockList, 3); left.Children.Add(_blockList);
            Grid libraryButtons = new Grid { Margin = new Thickness(10, 4, 10, 7) }; libraryButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); libraryButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Button newGroup = GhostButton("＋ 新建模块", NewBlock_Click); newGroup.HorizontalAlignment = HorizontalAlignment.Stretch; libraryButtons.Children.Add(newGroup); Button batchDelete = GhostButton("批量删除", BatchDeleteBlocks_Click); batchDelete.Foreground = new SolidColorBrush(Color.FromRgb(196, 52, 52)); batchDelete.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(batchDelete, 1); libraryButtons.Children.Add(batchDelete); Grid.SetRow(libraryButtons, 4); left.Children.Add(libraryButtons); _libraryShell.Child = left; Children.Add(_libraryShell);
            _librarySplitter = StudioGridSplitterFactory.Create(GridResizeDirection.Columns, "拖动调整模块库宽度；双击恢复默认宽度"); _librarySplitter.MouseDoubleClick += (s, e) => { _libraryDrawerWidth = 280d; ColumnDefinitions[0].Width = new GridLength(280); e.Handled = true; }; Grid.SetColumn(_librarySplitter, 1); Children.Add(_librarySplitter);

            Grid workspace = new Grid(); workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) }); _actionTableRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 180 }; _editorGapRow = new RowDefinition { Height = new GridLength(8) }; _editorBodyRow = new RowDefinition { Height = new GridLength(370), MinHeight = 340 }; workspace.RowDefinitions.Add(_actionTableRow); workspace.RowDefinitions.Add(_editorGapRow); workspace.RowDefinitions.Add(_editorBodyRow); Grid.SetColumn(workspace, 2); Children.Add(workspace);
            _blockName = Box(); _blockCategory = Box(); _blockVersion = Box(); _blockProducts = Box(); _blockDescription = Box();

            Border moduleHeaderShell = Surface(); Grid moduleHeader = new Grid(); moduleHeader.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); moduleHeader.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            DockPanel moduleLine = new DockPanel { Margin = new Thickness(10, 4, 9, 1) }; StackPanel moduleCommands = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(moduleCommands, Dock.Right); moduleCommands.Children.Add(ToolbarButton("\uE8C8", "复制为自定义", DuplicateAsCustom_Click, false)); moduleCommands.Children.Add(ToolbarButton("\uE713", "模块属性", BlockProperties_Click, false)); moduleCommands.Children.Add(ToolbarButton("\uE8B5", "导入", ImportSteps_Click, false)); moduleCommands.Children.Add(ToolbarButton("\uEDE1", "导出", Apply_Click, false)); moduleLine.Children.Add(moduleCommands); _blockSummary = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; moduleLine.Children.Add(_blockSummary); moduleHeader.Children.Add(moduleLine);
            DockPanel toolLine = new DockPanel { Margin = new Thickness(7, 0, 7, 3) }; _moduleDebugToolbar = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed }; _runModuleButton = ToolbarButton("\uE768", "运行模块", RunModule_Click, true); _runModuleButton.ToolTip = "按顺序执行当前模块内全部已启用动作；遇到断点时暂停"; _moduleDebugToolbar.Children.Add(_runModuleButton); Button singleStep = ToolbarButton("\uE7C5", "单步", StepModule_Click, false); singleStep.ToolTip = "只执行当前选中的一个动作"; _moduleDebugToolbar.Children.Add(singleStep); toolLine.Children.Add(_moduleDebugToolbar); Grid.SetRow(toolLine, 1); moduleHeader.Children.Add(toolLine); moduleHeaderShell.Child = moduleHeader; workspace.Children.Add(moduleHeaderShell);

            Border actionShell = Surface(); actionShell.BorderThickness = new Thickness(1, 0, 1, 1);             _stepList = new DataGrid { ItemsSource = _steps, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserReorderColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeight = 50, ColumnHeaderHeight = 42, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(251, 252, 254)), HorizontalGridLinesBrush = BorderColor(), VerticalGridLinesBrush = Brushes.Transparent, SelectionUnit = DataGridSelectionUnit.FullRow, FontSize = 13, RowStyle = StudioRowStyle(), ColumnHeaderStyle = StudioHeaderStyle(), AllowDrop = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _stepList.Columns.Add(BreakpointTemplateColumn()); _stepList.Columns.Add(StepTemplateColumn()); _stepList.Columns.Add(ActionNameTemplateColumn()); _stepList.Columns.Add(ModuleTagTemplateColumn()); _stepList.Columns.Add(StatusTemplateColumn()); Style enableStyle = new Style(typeof(CheckBox)); enableStyle.Setters.Add(new Setter(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center)); enableStyle.Setters.Add(new Setter(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center)); enableStyle.Setters.Add(new Setter(CheckBox.ForegroundProperty, Accent())); _stepList.Columns.Add(DataGridCheckHelpers.BoundCheckColumn("启用", "Enabled", 65, enableStyle)); _stepList.Columns.Add(PlatformDisplayTemplateColumn()); _stepList.Columns.Add(new DataGridTextColumn { Header = "当前值", Binding = new Binding("CurrentValue"), Width = 105, IsReadOnly = true }); _stepList.Columns.Add(new DataGridTextColumn { Header = "结果", Binding = new Binding("ExecutionResult"), Width = 80, IsReadOnly = true }); _stepList.SelectionChanged += StepList_SelectionChanged; _stepList.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(_changed)); _stepList.MouseDoubleClick += StepList_MouseDoubleClick; _stepList.PreviewMouseLeftButtonDown += StepList_LeftButtonDown; _stepList.PreviewMouseLeftButtonUp += StepList_LeftButtonUp; _stepList.PreviewMouseMove += StepList_MouseMove; _stepList.DragOver += StepList_DragOver; _stepList.DragLeave += StepList_DragLeave; _stepList.Drop += StepList_Drop; _stepList.GiveFeedback += StepList_GiveFeedback; _stepList.PreviewMouseRightButtonDown += StepList_RightButtonDown; _stepList.ContextMenu = BuildStepContextMenu(); actionShell.Child = _stepList; Grid.SetRow(actionShell, 1); workspace.Children.Add(actionShell);
            GridSplitter editorSplitter = StudioGridSplitterFactory.Create(GridResizeDirection.Rows, "拖动调整动作表与动作配置区高度；双击恢复舒适配置高度"); editorSplitter.MouseDoubleClick += (s, e) => { EnsureEditorComfortableHeight(true); e.Handled = true; }; Grid.SetRow(editorSplitter, 2); workspace.Children.Add(editorSplitter);

            Border editorShell = Surface(); Grid editor = new Grid(); editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); _editorContentRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }; editor.RowDefinitions.Add(_editorContentRow); DockPanel stepHeader = new DockPanel { Margin = new Thickness(12, 6, 7, 3) }; _toggleEditorButton = IconButton("\uE70D", "收起动作编辑区", ToggleEditor_Click); _toggleEditorButton.Width = 32; DockPanel.SetDock(_toggleEditorButton, Dock.Right); stepHeader.Children.Add(_toggleEditorButton); _functionNameText = new TextBlock { Visibility = Visibility.Collapsed }; _stepTitle = new TextBlock { Text = "当前动作：  请选择动作", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center }; stepHeader.Children.Add(_stepTitle); editor.Children.Add(stepHeader);

            _stepNameBox = new TextBox { Height = 25, Margin = new Thickness(3, 1, 3, 1), Padding = new Thickness(7, 3, 7, 3), ToolTip = "StepName；循环和条件跳转通过名称引用" }; _resultMode = new ComboBox { ItemsSource = new[] { "Action", "NumericLimit", "StringLimit", "PassFail", "Information", "Variable" }, Height = 25, Margin = new Thickness(3, 1, 3, 1) }; _resultMode.SelectionChanged += ResultMode_SelectionChanged; _stepRunMode = new ComboBox { ItemsSource = new[] { "Normal", "Skip", "Break" }, Height = 25, Margin = new Thickness(3, 1, 3, 1) }; _stepRecordingLog = new CheckBox { Content = "记录日志", Margin = new Thickness(4, 4, 4, 1), VerticalAlignment = VerticalAlignment.Center };
            _parameterGrid = new DataGrid { ItemsSource = _parameters, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.All, RowHeight = 36, ColumnHeaderHeight = 34, BorderBrush = BorderColor(), BorderThickness = new Thickness(1), FontSize = 12 }; _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "参数名", Binding = new Binding("DisplayName"), Width = 125, IsReadOnly = true }); _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "参数值", Binding = new Binding("ValueText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 125 }); _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 60, IsReadOnly = true }); _parameterGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "对外开放", Binding = new Binding("IsExposed"), Width = 75 }); _parameterGrid.Columns.Add(new DataGridTextColumn { Header = "对外名称", Binding = new Binding("BlockParameterName") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 135 });

            _editorTabs = new TabControl { BorderThickness = new Thickness(0), Background = Brushes.White, Padding = new Thickness(0), Margin = new Thickness(8, 0, 8, 4), FontSize = 12 };
            Grid basic = new Grid { Margin = new Thickness(5, 4, 5, 2) }; basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32, GridUnitType.Star) }); basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(19, GridUnitType.Star) }); basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(49, GridUnitType.Star) }); basic.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); basic.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            Grid actionInfo = FormGrid(5, 64); actionInfo.Children.Add(FormHeading("动作信息")); AddControlField(actionInfo, "动作名称", _stepNameBox, 1); _actionDescriptionBox = ReadOnlyBox("设置当前动作"); AddControlField(actionInfo, "动作描述", _actionDescriptionBox, 2); _actionModuleBox = ReadOnlyBox("标准模板 · 当前模块"); AddControlField(actionInfo, "所属模块", _actionModuleBox, 3); _actionTypeBox = ReadOnlyBox("设置"); AddControlField(actionInfo, "动作类型", _actionTypeBox, 4); AddControlField(actionInfo, "执行方式", _stepRunMode, 5); basic.Children.Add(actionInfo);
            Grid execution = FormGrid(5, 62); execution.Margin = new Thickness(8, 0, 8, 0); execution.Children.Add(FormHeading("执行配置")); AddControlField(execution, "超时时间", SmallNumberBox("0", "ms"), 1); AddControlField(execution, "重试次数", SmallNumberBox("0", "次"), 2); ComboBox failure = new ComboBox { ItemsSource = new[] { "继续执行", "停止流程", "安全下电" }, SelectedIndex = 0, Height = 21, Margin = new Thickness(3, 1, 3, 1) }; AddControlField(execution, "失败处理", failure, 3); Grid.SetRow(_stepRecordingLog, 4); Grid.SetColumn(_stepRecordingLog, 1); execution.Children.Add(_stepRecordingLog); Grid.SetColumn(execution, 1); basic.Children.Add(execution);
            Grid parameterArea = new Grid { Margin = new Thickness(3, 0, 0, 0) }; parameterArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) }); parameterArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); DockPanel parameterHeader = new DockPanel(); StackPanel parameterButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(parameterButtons, Dock.Right); parameterButtons.Children.Add(LinkText("＋ 添加参数")); parameterButtons.Children.Add(LinkText("批量导入")); parameterHeader.Children.Add(parameterButtons); parameterHeader.Children.Add(FormHeading("参数列表")); parameterArea.Children.Add(parameterHeader); Grid.SetRow(_parameterGrid, 1); parameterArea.Children.Add(_parameterGrid); Grid.SetColumn(parameterArea, 2); basic.Children.Add(parameterArea);
            StackPanel actionButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) }; Button executeNow = GhostButton("立即执行", ExecuteCurrentAction_Click); Button save = PrimaryButton("保存动作", Apply_Click); Button apply = PrimaryButton("应用", Apply_Click); apply.Background = new SolidColorBrush(Color.FromRgb(0, 151, 90)); apply.BorderBrush = apply.Background; actionButtons.Children.Add(executeNow); actionButtons.Children.Add(save); actionButtons.Children.Add(apply); Grid.SetRow(actionButtons, 1); Grid.SetColumn(actionButtons, 2); basic.Children.Add(actionButtons); _actionResult = new TextBlock { Foreground = TextSecondary(), Margin = new Thickness(4, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetRow(_actionResult, 1); Grid.SetColumnSpan(_actionResult, 2); basic.Children.Add(_actionResult);
            _editorTabs.Items.Add(new TabItem { Header = "基本设置", Content = basic }); _editorTabs.Items.Add(new TabItem { Header = "参数设置", Content = new TextBlock { Text = "在基本设置的参数列表中编辑参数。", Margin = new Thickness(12), Foreground = TextSecondary() } }); _editorTabs.Items.Add(new TabItem { Header = "条件判断", Content = new TextBlock { Text = "配置比较条件与跳转目标。", Margin = new Thickness(12), Foreground = TextSecondary() } }); Grid advancedSettings = FormGrid(2, 70); advancedSettings.Margin = new Thickness(10); AddControlField(advancedSettings, "结果类型", _resultMode, 1); _editorTabs.Items.Add(new TabItem { Header = "高级设置", Content = advancedSettings });
            Grid logSettings = new Grid { Margin = new Thickness(10) }; logSettings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 220 }); logSettings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) }); logSettings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 520 });
            _historyList = new ListBox { ItemsSource = _history, DisplayMemberPath = "SummaryText", BorderBrush = BorderColor(), FontSize = 12.5, Padding = new Thickness(3) }; _historyList.SelectionChanged += HistoryList_SelectionChanged; logSettings.Children.Add(_historyList);
            GridSplitter debugSplitter = StudioGridSplitterFactory.Create(GridResizeDirection.Columns, "拖动调整历史记录与平台结果宽度"); Grid.SetColumn(debugSplitter, 1); logSettings.Children.Add(debugSplitter);
            Grid resultPanel = new Grid(); resultPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); resultPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); resultPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _debugResultSummary = new TextBlock { Text = "执行后将在这里显示平台测试值和LIMIT判断。", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextSecondary(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }; resultPanel.Children.Add(_debugResultSummary);
            _platformResultGrid = new DataGrid { ItemsSource = _latestPlatformResults, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = true, RowHeight = 34, ColumnHeaderHeight = 34, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = BorderColor(), BorderThickness = new Thickness(1), SelectionUnit = DataGridSelectionUnit.FullRow, FontSize = 12.5, RowStyle = StudioRowStyle(), ColumnHeaderStyle = StudioHeaderStyle() };
            _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "测试项", Binding = new Binding("StepName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("StepType"), Width = 95 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "测试值", Binding = new Binding("Value"), Width = 115 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "下限", Binding = new Binding("LimitsLow"), Width = 85 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "上限", Binding = new Binding("LimitsHigh"), Width = 85 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "比较", Binding = new Binding("LimitExpression"), Width = 90 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 65 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "结果", Binding = new Binding("Status"), Width = 85 }); _platformResultGrid.Columns.Add(new DataGridTextColumn { Header = "说明", Binding = new Binding("Comment"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) }); Grid.SetRow(_platformResultGrid, 1); resultPanel.Children.Add(_platformResultGrid);
            _debugCanDetails = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, MaxHeight = 180, Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)), BorderThickness = new Thickness(0) }; Expander canDetails = new Expander { Header = "CAN详细报文（排查问题时展开）", IsExpanded = false, Content = _debugCanDetails, Margin = new Thickness(0, 6, 0, 0) }; Grid.SetRow(canDetails, 2); resultPanel.Children.Add(canDetails); Grid.SetColumn(resultPanel, 2); logSettings.Children.Add(resultPanel);
            _editorTabs.Items.Add(new TabItem { Header = "日志设置", Content = logSettings }); _editorTabs.Items.Add(new TabItem { Header = "对外变量", Content = new TextBlock { Text = "通过参数列表中的“对外开放”和“对外名称”配置。", Margin = new Thickness(12), Foreground = TextSecondary() } }); Grid.SetRow(_editorTabs, 1); editor.Children.Add(_editorTabs); editorShell.Child = editor; Grid.SetRow(editorShell, 3); workspace.Children.Add(editorShell);
            _actionConfigurator = new ActionConfigurationPanel(_locatorRepository, _executeStep, SaveConfiguredAction, _log, () => _getProject() == null ? string.Empty : _getProject().Product, () => _getProject() == null ? string.Empty : _getProject().AuxiliaryDbcPath, _getLastPlatformResult); _actionConfigurator.ExecutionRecorded += ActionConfigurator_ExecutionRecorded;
            _moduleReferenceConfigurationPanel = BuildModuleReferenceConfigurationPanel();
            _moduleReferenceParameterGrid = new DataGrid { ItemsSource = _moduleReferenceParameters, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, RowHeight = 40, ColumnHeaderHeight = 36, Margin = new Thickness(10), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = BorderColor(), FontSize = 13 };
            _moduleReferenceParameterGrid.Columns.Add(new DataGridTextColumn { Header = "参数", Binding = new Binding("DisplayName"), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star), IsReadOnly = true }); _moduleReferenceParameterGrid.Columns.Add(new DataGridTextColumn { Header = "当前值（双击修改）", Binding = new Binding("ValueText") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); _moduleReferenceParameterGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 90, IsReadOnly = true }); _moduleReferenceParameterGrid.Columns.Add(new DataGridTextColumn { Header = "说明", Binding = new Binding("Description"), Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
            Border referencePanel = Surface(); Grid referenceGrid = new Grid(); referenceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); referenceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); TextBlock referenceHint = new TextBlock { Text = "当前行引用标准模块。直接修改当前实例参数；不会修改左侧原标准模块。", Margin = new Thickness(14, 10, 14, 4), Foreground = TextSecondary(), FontSize = 13 }; referenceGrid.Children.Add(referenceHint); Grid.SetRow(_moduleReferenceParameterGrid, 1); referenceGrid.Children.Add(_moduleReferenceParameterGrid); referencePanel.Child = referenceGrid;
            _stepRecordingLog.Content = "显示在平台界面"; _stepRecordingLog.ToolTip = "对应SEQ字段 RecordingLog；取消后STEP仍执行，但不作为平台显示记录项"; _editorTabs.Items.Clear(); _actionConfigurationTab = new TabItem { Header = "动作配置", Content = _actionConfigurator }; _editorTabs.Items.Add(_actionConfigurationTab); _moduleParametersTab = new TabItem { Header = "模块参数", Content = referencePanel, Visibility = Visibility.Collapsed }; _editorTabs.Items.Add(_moduleParametersTab); _debugRecordTab = new TabItem { Header = "调试记录", Content = logSettings, Visibility = Visibility.Collapsed }; _editorTabs.Items.Add(_debugRecordTab);
        }

        public void SetDebugMode(bool enabled)
        {
            _debugMode = enabled; if (_moduleDebugToolbar != null) _moduleDebugToolbar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; if (_debugRecordTab != null) _debugRecordTab.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; if (_actionConfigurator != null) _actionConfigurator.SetDebugMode(enabled); if (_stepList != null) _stepList.ContextMenu = BuildStepContextMenu();
        }

        private void ActionConfigurator_ExecutionRecorded(ActionHistoryRow history)
        {
            if (history == null) return;
            BlockStepListItem selectedRow = _selectedStep == null ? null : _steps.FirstOrDefault(value => value.Step == _selectedStep); if (selectedRow != null) selectedRow.SetExecutionResult(history.PlatformResult, history.Result, history.Succeeded);
            _history.Insert(0, history);
            while (_history.Count > 100) _history.RemoveAt(_history.Count - 1);
            _historyList.SelectedItem = history;
            _historyList.ScrollIntoView(history);
            _editorTabs.SelectedIndex = 2;
        }

        private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActionHistoryRow history = _historyList.SelectedItem as ActionHistoryRow;
            _latestPlatformResults.Clear();
            if (history == null) { _debugResultSummary.Text = "请选择一条调试记录。"; _debugCanDetails.Text = string.Empty; return; }
            LegacyStepExecutionResult platform = history.PlatformResult;
            if (platform != null && platform.Results != null)
                foreach (LegacyPlatformResultRow row in platform.Results) _latestPlatformResults.Add(row);
            if (_latestPlatformResults.Count == 0)
                _latestPlatformResults.Add(new LegacyPlatformResultRow { StartTime = history.Time, StepName = history.Step.StepName, StepType = "Action", MeasuredValue = history.Result, Status = history.Succeeded ? "Passed" : "Failed", LimitsLow = string.Empty, LimitsHigh = string.Empty, LimitExpression = string.Empty, Unit = string.Empty, Comment = "该动作没有向平台写入测试结果" });
            string platformStatus = platform == null ? string.Empty : platform.TotalStatus;
            _debugResultSummary.Text = (history.Succeeded ? "✓ " : "✕ ") + history.Step.StepName + " · " + _latestPlatformResults.Count + " 条平台结果" + (string.IsNullOrWhiteSpace(platformStatus) ? string.Empty : " · 总状态 " + platformStatus);
            _debugResultSummary.Foreground = history.Succeeded ? new SolidColorBrush(Color.FromRgb(0, 145, 90)) : new SolidColorBrush(Color.FromRgb(205, 48, 48));
            _debugCanDetails.Text = string.IsNullOrWhiteSpace(history.Details) ? "本次执行没有产生CAN详细诊断。" : history.Details;
        }

        private void BlockList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitStep(); CommitBlock(); BlockListItem item = _blockList.SelectedItem as BlockListItem; _selectedBlock = item == null ? null : item.Block; EnsureEditorExpanded(); LoadBlock();
        }
        private void SelectChildModuleFromTree(FunctionBlockDefinition block) { if (block == null || _blockList == null) return; if (_openBlockEditor != null) { _openBlockEditor(block); return; } BlockListItem item = _blocks.FirstOrDefault(value => value.Block.Id == block.Id); if (item == null) return; _blockList.SelectedItem = item; _blockList.ScrollIntoView(item); SelectBlock(block.Id); }
        private BlockListItem CreateBlockListItem(FunctionBlockDefinition block) { FctStudioProject project = _getProject(); IEnumerable<FunctionBlockDefinition> children = Enumerable.Empty<FunctionBlockDefinition>(); if (project != null && block != null && string.Equals(block.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase)) children = (block.Steps ?? new List<BlockStepDefinition>()).Where(step => step.IsModuleReference).Select(step => project.Blocks.FirstOrDefault(candidate => candidate.Id == step.ReferencedBlockId)).Where(value => value != null).GroupBy(value => value.Id, StringComparer.Ordinal).Select(group => group.First()); return new BlockListItem(block, children); }
        private void BlockProperties_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null || !EnsureEditableBlock()) return; string oldKind = _selectedBlock.ModuleKind; FunctionBlockPropertiesWindow dialog = new FunctionBlockPropertiesWindow(_selectedBlock, new[] { _getProject() == null ? string.Empty : _getProject().Product }) { Owner = Window.GetWindow(this) }; if (dialog.ShowDialog() == true) { dialog.ApplyTo(_selectedBlock); if ((string.Equals(oldKind, "Standard", StringComparison.OrdinalIgnoreCase) || string.Equals(oldKind, "Product", StringComparison.OrdinalIgnoreCase)) && string.Equals(_selectedBlock.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase)) GlobalModuleLibraryService.Delete(_selectedBlock); BlockListItem item = _blocks.FirstOrDefault(value => value.Block == _selectedBlock); if (item != null) item.Refresh(); CollectionViewSource.GetDefaultView(_blockList.ItemsSource).Refresh(); LoadBlock(); if (item != null) _blockList.SelectedItem = item; _changed(); } }
        private void AddActionMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBlock == null || !EnsureEditableBlock()) return; Button button = sender as Button; if (button == null) return; ContextMenu menu = new ContextMenu(); menu.Items.Add(BuildHierarchicalAddMenu("添加动作", null)); menu.Items.Add(new Separator()); AddMenu(menu, "从测试项库导入...", AddFromCatalog_Click); AddMenu(menu, "从当前SEQ批量导入...", ImportSteps_Click); menu.Items.Add(new Separator()); AddMenu(menu, "空白动作配置", AddStep_Click); AddMenu(menu, "插件仪器（高级）", AddPlugin_Click); menu.PlacementTarget = button; menu.IsOpen = true;
        }
        private static void AddMenu(ContextMenu menu, string text, RoutedEventHandler handler) { MenuItem item = new MenuItem { Header = text }; item.Click += handler; menu.Items.Add(item); }
        private ContextMenu BuildBlockContextMenu() { ContextMenu menu = new ContextMenu(); AddMenu(menu, "新建模块", NewBlock_Click); menu.Items.Add(new Separator()); AddMenu(menu, "复制模块", CopyBlockToClipboard_Click); AddMenu(menu, "粘贴模块", PasteBlock_Click); AddMenu(menu, "复制为自定义模块", DuplicateAsCustom_Click); menu.Items.Add(new Separator()); AddMenu(menu, "属性 / 重命名", BlockProperties_Click); AddMenu(menu, "删除选中模块", BatchDeleteBlocks_Click); menu.Items.Add(new Separator()); AddMenu(menu, "全选模块", (s, e) => SetAllBlocksForBatch(true)); AddMenu(menu, "清空多选", (s, e) => SetAllBlocksForBatch(false)); AddMenu(menu, "删除多选模块", BatchDeleteBlocks_Click); return menu; }
        private ContextMenu BuildStepContextMenu() { ContextMenu menu = new ContextMenu(); menu.Items.Add(BuildHierarchicalAddMenu("插入动作到上面", 0)); menu.Items.Add(BuildHierarchicalAddMenu("插入动作到下面", 1)); menu.Items.Add(BuildModuleReferenceMenu("插入模块到上面", 0)); menu.Items.Add(BuildModuleReferenceMenu("插入模块到下面", 1)); menu.Items.Add(new Separator()); AddMenu(menu, "复制动作", CopyStepToClipboard_Click); AddMenu(menu, "粘贴动作", PasteStep_Click); AddMenu(menu, "删除动作/模块引用", DeleteStep_Click); AddMenu(menu, "启用 / 停用", ToggleStep_Click); menu.Items.Add(new Separator()); AddMenu(menu, "上移", MoveStepUp_Click); AddMenu(menu, "下移", MoveStepDown_Click); if (_debugMode) { menu.Items.Add(new Separator()); AddMenu(menu, "立即执行", ExecuteCurrentAction_Click); } return menu; }
        private MenuItem BuildModuleReferenceMenu(string title, int relativeOffset)
        {
            MenuItem root = new MenuItem { Header = title };
            root.Items.Add(new MenuItem { Header = "正在加载模块...", IsEnabled = false });
            root.SubmenuOpened += (s, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, root)) return;
                root.Items.Clear();
                List<FunctionBlockDefinition> candidates = _blocks.Select(value => value.Block).Where(value => value != null && value != _selectedBlock && !WouldCreateModuleCycle(value, _selectedBlock == null ? string.Empty : _selectedBlock.Id, new HashSet<string>(StringComparer.Ordinal))).OrderBy(ModuleKindOrder).ThenBy(value => value.Name).ToList();
                if (candidates.Count == 0) { root.Items.Add(new MenuItem { Header = "没有可插入的模块", IsEnabled = false }); return; }
                foreach (IGrouping<string, FunctionBlockDefinition> group in candidates.GroupBy(ModuleKindText))
                {
                    MenuItem branch = new MenuItem { Header = group.Key };
                    foreach (FunctionBlockDefinition source in group)
                    {
                        MenuItem item = new MenuItem { Header = source.Name, ToolTip = "插入模块引用；原模块保持独立，参数可在当前引用中覆盖" };
                        item.Click += (sender, args) => AddModuleReference(_selectedBlock, source, RelativeInsertIndex(relativeOffset)); branch.Items.Add(item);
                    }
                    root.Items.Add(branch);
                }
            };
            return root;
        }
        private static int ModuleKindOrder(FunctionBlockDefinition block) { return string.Equals(block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase) ? 0 : string.Equals(block.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? 1 : 2; }
        private int RelativeInsertIndex(int offset) { return _selectedStep == null || _selectedBlock == null ? (_selectedBlock == null ? 0 : _selectedBlock.Steps.Count) : Math.Max(0, Math.Min(_selectedBlock.Steps.Count, _selectedBlock.Steps.IndexOf(_selectedStep) + offset)); }
        private MenuItem BuildHierarchicalAddMenu(string header, int? relativeOffset)
        {
            MenuItem root = new MenuItem { Header = header }; foreach (string source in new[] { "仪器", "产品内部通信", "产品DBC通信", "流程逻辑" }) { MenuItem sourceMenu = new MenuItem { Header = source }; IEnumerable<ActionDescriptor> descriptors = ActionCatalog.PickerDescriptors(source); foreach (IGrouping<string, ActionDescriptor> group in descriptors.GroupBy(ActionCatalog.PickerTarget).OrderBy(value => value.Key, StringComparer.CurrentCulture)) { MenuItem targetMenu = new MenuItem { Header = group.Key }; foreach (ActionDescriptor descriptor in group.OrderBy(value => value.DisplayName, StringComparer.CurrentCulture)) { ActionDescriptor captured = descriptor; MenuItem leaf = new MenuItem { Header = descriptor.DisplayName }; leaf.Click += (s, e) => AddDescriptorShortcut(captured, relativeOffset); targetMenu.Items.Add(leaf); } sourceMenu.Items.Add(targetMenu); }
                if (source == "产品内部通信") { MenuItem locator = new MenuItem { Header = "FT/Locator内存" }; foreach (string operation in new[] { "读取", "写入" }) { string captured = operation; MenuItem leaf = new MenuItem { Header = operation }; leaf.Click += (s, e) => AddSelectionShortcut("产品内部通信", "FT/Locator内存", captured, relativeOffset); locator.Items.Add(leaf); } sourceMenu.Items.Add(locator); }
                if (source == "产品DBC通信") { MenuItem dbc = new MenuItem { Header = "辅驱 / DCDC / PDU" }; foreach (string operation in new[] { "发送一次", "开始周期发送", "停止周期发送", "读取DBC信号", "发送原始帧" }) { string captured = operation; MenuItem leaf = new MenuItem { Header = operation }; leaf.Click += (s, e) => AddSelectionShortcut("产品DBC通信", "辅驱/DCDC/PDU DBC", captured, relativeOffset); dbc.Items.Add(leaf); } sourceMenu.Items.Add(dbc); }
                if (sourceMenu.Items.Count > 0) root.Items.Add(sourceMenu); }
            MenuItem legacy = new MenuItem { Header = "原平台MAINTEST" }; MenuItem selectLegacy = new MenuItem { Header = "从测试项库选择..." }; selectLegacy.Click += AddFromCatalog_Click; legacy.Items.Add(selectLegacy); root.Items.Add(legacy); return root;
        }
        private void AddDescriptorShortcut(ActionDescriptor descriptor, int? relativeOffset = null) { if (_selectedBlock == null) return; AddStep(ActionConfigurationPanel.CreateFromDescriptor(descriptor), relativeOffset); }
        private void AddSelectionShortcut(string source, string target, string operation, int? relativeOffset = null) { if (_selectedBlock == null) return; AddStep(ActionConfigurationPanel.CreateDraft(), relativeOffset); if (_actionConfigurator != null) _actionConfigurator.SelectActionShortcut(source, target, operation); }
        private void BlockList_RightButtonDown(object sender, MouseButtonEventArgs e) { ListBoxItem item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject); if (item != null && !item.IsSelected) { _blockList.UnselectAll(); item.IsSelected = true; } }
        private void BlockList_LeftButtonDown(object sender, MouseButtonEventArgs e) { _blockDragArmed = false; _blockDragItem = null; if (StudioDragDropGuard.IsMultiSelectGesture || FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) != null || FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null || FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) != null) return; ListBoxItem item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject); if (item == null) return; BlockListItem source = item.DataContext as BlockListItem ?? item.Content as BlockListItem; if (source == null || source.Block == null) return; _moduleReferenceTargetBlock = _selectedBlock; _blockDragItem = source; _blockDragStart = e.GetPosition(_blockList); _blockDragArmed = true; }
        private void BlockList_LeftButtonUp(object sender, MouseButtonEventArgs e) { _blockDragArmed = false; _blockDragItem = null; _moduleReferenceTargetBlock = null; }
        private void BlockList_MouseMove(object sender, MouseEventArgs e) { if (!_blockDragArmed || _blockDragItem == null || _moduleReferenceTargetBlock == null || e.LeftButton != MouseButtonState.Pressed) return; Point point = e.GetPosition(_blockList); if (!StudioDragDropGuard.HasMovedEnough(_blockDragStart, point)) return; BlockListItem item = _blockDragItem; FunctionBlockDefinition target = _moduleReferenceTargetBlock; _blockDragArmed = false; if (_selectedBlock != target) SelectBlock(target.Id); DragDrop.DoDragDrop(_blockList, new DataObject(typeof(BlockListItem), item), DragDropEffects.Copy); _blockDragItem = null; }
        private void StepList_RightButtonDown(object sender, MouseButtonEventArgs e) { DataGridRow row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject); if (row != null) { row.IsSelected = true; _stepList.SelectedItem = row.Item; } }
        private void StepList_LeftButtonDown(object sender, MouseButtonEventArgs e) { _stepDragArmed = false; _stepDragItem = null; DependencyObject source = e.OriginalSource as DependencyObject; if (FindAncestor<Button>(source) != null || FindAncestor<CheckBox>(source) != null || FindAncestor<ScrollBar>(source) != null || FindAncestor<Thumb>(source) != null || FindAncestor<DataGridColumnHeader>(source) != null) return; DataGridRow row = FindAncestor<DataGridRow>(source); if (row == null) { ClearStepSelection(); return; } EnsureEditorExpanded(); if (_editorTabs != null) _editorTabs.SelectedIndex = 0; row.IsSelected = true; _stepList.SelectedItem = row.Item; _stepDragItem = row.Item as BlockStepListItem; _stepDragStart = e.GetPosition(_stepList); _stepDragArmed = _stepDragItem != null; }
        internal void ClearStepSelection() { if (_stepList == null) return; _stepList.UnselectAll(); _stepList.SelectedItem = null; }
        private void StepList_LeftButtonUp(object sender, MouseButtonEventArgs e) { _stepDragArmed = false; _stepDragItem = null; }
        private void StepList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { BlockStepListItem row = _stepList.SelectedItem as BlockStepListItem; if (row == null || !row.Step.IsModuleReference) return; FunctionBlockDefinition block = _getProject().Blocks.FirstOrDefault(value => value.Id == row.Step.ReferencedBlockId); if (block != null && _openBlockEditor != null) _openBlockEditor(block); else SelectBlock(row.Step.ReferencedBlockId); e.Handled = true; }
        private void StepList_MouseMove(object sender, MouseEventArgs e) { if (!_stepDragArmed || _stepDragItem == null || e.LeftButton != MouseButtonState.Pressed) return; Point point = e.GetPosition(_stepList); if (!StudioDragDropGuard.HasMovedEnough(_stepDragStart, point)) return; BlockStepListItem item = _stepDragItem; _stepDragArmed = false; _stepDragItem = null; DragDrop.DoDragDrop(_stepList, new DataObject(typeof(BlockStepListItem), item), DragDropEffects.Move); }
        private void StepList_GiveFeedback(object sender, GiveFeedbackEventArgs e) { if (e.Effects.HasFlag(DragDropEffects.Move)) { Mouse.SetCursor(Cursors.SizeNS); e.UseDefaultCursors = false; e.Handled = true; } }
        private void EnableCheck_Click(object sender, RoutedEventArgs e)
        {
            CheckBox box = sender as CheckBox;
            BlockStepListItem row = box == null ? null : box.DataContext as BlockStepListItem;
            if (row == null) return;
            if (!EnsureEditableBlock())
            {
                row.Enabled = !(box.IsChecked == true);
                return;
            }
            _changed();
        }
        private DataGridTemplateColumn BreakpointTemplateColumn() { FrameworkElementFactory button = new FrameworkElementFactory(typeof(System.Windows.Controls.Button)); button.SetValue(System.Windows.Controls.Button.WidthProperty, 42d); button.SetValue(System.Windows.Controls.Button.HeightProperty, 42d); button.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(0)); button.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(2, 0, 2, 0)); button.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent); button.SetValue(System.Windows.Controls.Button.BorderBrushProperty, Brushes.Transparent); button.SetValue(System.Windows.Controls.Button.ToolTipProperty, "单击添加/取消断点"); button.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(BreakpointButton_Click)); FrameworkElementFactory dot = new FrameworkElementFactory(typeof(TextBlock)); dot.SetBinding(TextBlock.TextProperty, new Binding("BreakpointGlyph")); dot.SetBinding(TextBlock.ForegroundProperty, new Binding("BreakpointBrush")); dot.SetValue(TextBlock.FontSizeProperty, 20d); dot.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); dot.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); dot.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); button.AppendChild(dot); return new DataGridTemplateColumn { Header = "断点", Width = 54, CellTemplate = new DataTemplate { VisualTree = button }, IsReadOnly = true }; }
        private void StepList_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(typeof(BlockStepListItem)) ? DragDropEffects.Move : e.Data.GetDataPresent(typeof(BlockListItem)) ? DragDropEffects.Copy : DragDropEffects.None; ShowStepDropTarget(FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)); e.Handled = true; }
        private void StepList_DragLeave(object sender, DragEventArgs e) { ClearStepDropTarget(); }
        private void ShowStepDropTarget(DataGridRow row) { if (_stepDropTargetRow == row) return; ClearStepDropTarget(); _stepDropTargetRow = row; if (row != null) { row.BorderBrush = Accent(); row.BorderThickness = new Thickness(0, 0, 0, 3); } }
        private void ClearStepDropTarget() { if (_stepDropTargetRow != null) { _stepDropTargetRow.ClearValue(DataGridRow.BorderBrushProperty); _stepDropTargetRow.ClearValue(DataGridRow.BorderThicknessProperty); _stepDropTargetRow = null; } }
        private void StepList_Drop(object sender, DragEventArgs e)
        {
            try
            {
                DataGridRow targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
                ClearStepDropTarget();

                BlockListItem module = e.Data.GetData(typeof(BlockListItem)) as BlockListItem;
                if (module != null)
                {
                    FunctionBlockDefinition targetBlock = _moduleReferenceTargetBlock ?? _selectedBlock;
                    _moduleReferenceTargetBlock = null;
                    int moduleIndex = targetBlock == null ? 0 : targetBlock.Steps.Count;
                    if (targetRow != null)
                    {
                        int rowIndex = targetRow.GetIndex();
                        if (rowIndex >= 0) moduleIndex = rowIndex;
                    }
                    AddModuleReference(targetBlock, module.Block, moduleIndex);
                    e.Handled = true;
                    return;
                }

                BlockStepListItem item = e.Data.GetData(typeof(BlockStepListItem)) as BlockStepListItem;
                if (item == null || item.Step == null || _selectedBlock == null || _selectedBlock.Steps == null)
                {
                    e.Handled = true;
                    return;
                }
                if (!EnsureEditableBlock()) { e.Handled = true; return; }

                int oldIndex = _selectedBlock.Steps.IndexOf(item.Step);
                int viewOld = _steps.IndexOf(item);
                if (oldIndex < 0)
                {
                    if (viewOld >= 0 && viewOld < _selectedBlock.Steps.Count) oldIndex = viewOld;
                    else { e.Handled = true; return; }
                }
                if (oldIndex < 0 || oldIndex >= _selectedBlock.Steps.Count) { e.Handled = true; return; }

                int targetIndex;
                if (targetRow == null) targetIndex = _selectedBlock.Steps.Count;
                else
                {
                    targetIndex = targetRow.GetIndex();
                    if (targetIndex < 0) targetIndex = _selectedBlock.Steps.Count;
                }

                if (oldIndex < targetIndex) targetIndex--;
                if (_selectedBlock.Steps.Count == 0) { e.Handled = true; return; }
                targetIndex = Math.Max(0, Math.Min(_selectedBlock.Steps.Count - 1, targetIndex));
                if (targetIndex == oldIndex) { e.Handled = true; return; }

                BlockStepDefinition step = _selectedBlock.Steps[oldIndex];
                _selectedBlock.Steps.RemoveAt(oldIndex);
                _selectedBlock.Steps.Insert(targetIndex, step);

                _steps.Clear();
                int order = 0;
                foreach (BlockStepDefinition modelStep in _selectedBlock.Steps)
                    _steps.Add(CreateStepItem(modelStep, ++order, IsBlockStepBreakpoint(modelStep)));

                BlockStepListItem selected = _steps.FirstOrDefault(value => value.Step == step);
                if (selected != null)
                {
                    _stepList.SelectedItem = selected;
                    _stepList.ScrollIntoView(selected);
                }
                _selectedStep = step;
                _changed();
            }
            catch (Exception ex)
            {
                ClearStepDropTarget();
                try { if (_log != null) _log("STEP 拖放失败：" + ex.Message); } catch { }
            }
            e.Handled = true;
        }
        private bool IsBlockStepBreakpoint(BlockStepDefinition step) { FctStudioProject project = _getProject(); if (project == null || _selectedBlock == null) return false; if (project.Breakpoints.Contains("LIB:" + _selectedBlock.Id + ":" + step.Id)) return true; return project.Flow.Any(instance => instance.BlockId == _selectedBlock.Id && project.Breakpoints.Contains(instance.Id + ":" + step.Id)); }
        private void BreakpointButton_Click(object sender, RoutedEventArgs e) { Button button = sender as Button; BlockStepListItem row = button == null ? null : button.DataContext as BlockStepListItem; if (row == null || _selectedBlock == null) return; bool enabled = !row.Breakpoint; row.Breakpoint = enabled; FctStudioProject project = _getProject(); string libraryKey = "LIB:" + _selectedBlock.Id + ":" + row.Step.Id; if (enabled && !project.Breakpoints.Contains(libraryKey)) project.Breakpoints.Add(libraryKey); if (!enabled) project.Breakpoints.Remove(libraryKey); foreach (FlowBlockInstance instance in project.Flow.Where(value => value.BlockId == _selectedBlock.Id)) { string key = instance.Id + ":" + row.Step.Id; if (enabled && !project.Breakpoints.Contains(key)) project.Breakpoints.Add(key); if (!enabled) project.Breakpoints.Remove(key); } _changed(); e.Handled = true; }
        private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject { DependencyObject current = source; while (current != null && !(current is T)) current = VisualTreeHelper.GetParent(current); return current as T; }
        private void CopyBlockToClipboard_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; _blockClipboard = _selectedBlock.Clone(); _log("已复制模块：" + _selectedBlock.Name); }
        private void PasteBlock_Click(object sender, RoutedEventArgs e) { if (_blockClipboard == null) { MessageBox.Show("请先复制一个模块。", "粘贴模块", MessageBoxButton.OK, MessageBoxImage.Information); return; } FunctionBlockDefinition copy = _blockClipboard.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Name = UniqueBlockName(copy.Name + " - 副本"); if (string.Equals(copy.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase)) { copy.ModuleKind = "Custom"; copy.IsStandard = false; } foreach (BlockStepDefinition step in copy.Steps) step.Id = Guid.NewGuid().ToString("N"); _getProject().Blocks.Add(copy); BlockListItem item = CreateBlockListItem(copy); _blocks.Add(item); CollectionViewSource.GetDefaultView(_blockList.ItemsSource).Refresh(); _blockList.SelectedItem = item; _changed(); }
        private string UniqueBlockName(string proposed) { string value = proposed; int suffix = 2; while (_getProject().Blocks.Any(block => string.Equals(block.Name, value, StringComparison.OrdinalIgnoreCase))) value = proposed + " " + suffix++; return value; }
        private void CopyStepToClipboard_Click(object sender, RoutedEventArgs e) { if (_selectedStep == null) return; _stepClipboard = _selectedStep.Clone(); _log("已复制动作：" + _selectedStep.ToStep().StepName); }
        private void PasteStep_Click(object sender, RoutedEventArgs e) { if (_stepClipboard == null) { MessageBox.Show("请先复制一个动作。", "粘贴动作", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (!EnsureEditableBlock()) return; BlockStepDefinition copy = _stepClipboard.Clone(); copy.Id = Guid.NewGuid().ToString("N"); int index = _selectedStep == null ? _selectedBlock.Steps.Count : _selectedBlock.Steps.IndexOf(_selectedStep) + 1; _selectedBlock.Steps.Insert(index, copy); _steps.Insert(index, CreateStepItem(copy, index + 1)); RefreshStepOrders(); _stepList.SelectedIndex = index; _changed(); }
        private void ToggleEditor_Click(object sender, RoutedEventArgs e) { bool collapse = _editorContentRow.Height.Value > 0; _editorContentRow.Height = collapse ? new GridLength(0) : new GridLength(1, GridUnitType.Star); _editorTabs.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible; _editorBodyRow.MinHeight = collapse ? 44 : 340; _editorBodyRow.Height = collapse ? new GridLength(44) : new GridLength(DesiredEditorHeight()); _editorGapRow.Height = new GridLength(8); _actionTableRow.Height = new GridLength(1, GridUnitType.Star); TextBlock icon = _toggleEditorButton.Content as TextBlock; if (icon != null) icon.Text = collapse ? "\uE70E" : "\uE70D"; _toggleEditorButton.ToolTip = collapse ? "展开动作编辑区" : "收起动作编辑区"; }
        private void EnsureEditorExpanded() { if (_editorContentRow == null) return; if (_editorContentRow.Height.Value == 0) { _editorContentRow.Height = new GridLength(1, GridUnitType.Star); _editorTabs.Visibility = Visibility.Visible; TextBlock icon = _toggleEditorButton.Content as TextBlock; if (icon != null) icon.Text = "\uE70D"; _toggleEditorButton.ToolTip = "收起动作编辑区"; } EnsureEditorComfortableHeight(false); }
        private void EnsureEditorComfortableHeight(bool force) { if (_editorBodyRow == null || _actionTableRow == null) return; double target = DesiredEditorHeight(); if (!force && _editorBodyRow.ActualHeight >= target - 1) return; _editorBodyRow.MinHeight = 340; _editorBodyRow.Height = new GridLength(target); _actionTableRow.Height = new GridLength(1, GridUnitType.Star); _editorGapRow.Height = new GridLength(8); }
        private double DesiredEditorHeight() { double available = Math.Max(600d, ActualHeight - 86d); return Math.Max(360d, Math.Min(420d, available * 0.44d)); }
        private bool EnsureEditableBlock()
        {
            if (_selectedBlock == null) return false; if (!string.Equals(_selectedBlock.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase)) return true; _log("标准模块模板为只读；请复制为自定义模块后再修改。"); return false;
        }
        private void BlockSearch_TextChanged(object sender, EventArgs e) { string keyword = _showingSearchPlaceholder ? string.Empty : (_blockSearch.Text ?? string.Empty).Trim(); string category = Convert.ToString(_blockCategoryFilter.SelectedItem, CultureInfo.InvariantCulture) ?? "全部模块"; ICollectionView view = CollectionViewSource.GetDefaultView(_blockList.ItemsSource); view.Filter = item => { BlockListItem row = item as BlockListItem; return row != null && (category == "全部模块" || row.Block.Category == category) && (keyword.Length == 0 || row.Block.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || row.Children.Any(child => child.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)); }; view.Refresh(); }
        private void LoadBlock()
        {
            _steps.Clear(); _parameters.Clear(); _selectedStep = null;
            if (_selectedBlock == null) return;
            bool templateReadOnly = string.Equals(_selectedBlock.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase); _blockName.Text = _selectedBlock.Name; _blockCategory.Text = _selectedBlock.Category; _blockVersion.Text = _selectedBlock.Version; _blockProducts.Text = string.Join(",", _selectedBlock.SupportedProducts ?? new List<string>()); _blockDescription.Text = _selectedBlock.Description; _blockName.IsReadOnly = templateReadOnly; _blockCategory.IsReadOnly = templateReadOnly; _blockVersion.IsReadOnly = templateReadOnly; _blockProducts.IsReadOnly = templateReadOnly; _blockDescription.IsReadOnly = templateReadOnly; if (_editorTabs != null) _editorTabs.IsEnabled = !templateReadOnly; if (_stepList != null) _stepList.IsReadOnly = true;
            _blockSummary.Text = "当前模块：  " + _selectedBlock.Name + "    [" + _selectedBlock.Category + "]    " + _selectedBlock.Steps.Count + " 个动作" + (templateReadOnly ? "    标准模块 · 模板只读" : string.Empty);
            int order = 0; foreach (BlockStepDefinition step in _selectedBlock.Steps) _steps.Add(CreateStepItem(step, ++order, IsBlockStepBreakpoint(step)));
            if (_steps.Count > 0) _stepList.SelectedIndex = 0;
        }
        private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitStep(); BlockStepListItem item = _stepList.SelectedItem as BlockStepListItem; _selectedStep = item == null ? null : item.Step; if (item != null) EnsureEditorExpanded(); LoadStep();
        }
        private void LoadStep()
        {
            _loadingStep = true; _parameters.Clear(); _moduleReferenceParameters.Clear(); if (_selectedStep == null) { _stepTitle.Text = "当前动作：  请选择动作"; _stepNameBox.Text = string.Empty; if (_moduleParametersTab != null) _moduleParametersTab.Visibility = Visibility.Collapsed; if (_actionConfigurationTab != null) _actionConfigurationTab.Content = _actionConfigurator; if (_actionConfigurator != null) { _actionConfigurator.Visibility = Visibility.Visible; _actionConfigurator.LoadStep(null, null); } if (_editorTabs != null) _editorTabs.SelectedIndex = 0; _loadingStep = false; return; }
            if (_selectedStep.IsModuleReference) { int referenceOrder = _selectedBlock == null ? 0 : _selectedBlock.Steps.IndexOf(_selectedStep) + 1; _stepTitle.Text = "当前模块引用：  " + referenceOrder.ToString("00", CultureInfo.InvariantCulture) + "  " + _selectedStep.ReferencedBlockName; _stepNameBox.Text = _selectedStep.ReferencedBlockName; FunctionBlockDefinition referenced = _getProject().Blocks.FirstOrDefault(value => value.Id == _selectedStep.ReferencedBlockId); LoadModuleReferenceConfiguration(referenced); if (referenced != null) foreach (BlockParameterDefinition parameter in referenced.Parameters) { object value; if (!(_selectedStep.ReferencedParameterOverrides ?? new Dictionary<string, object>()).TryGetValue(parameter.Name, out value)) value = parameter.DefaultValue; _moduleReferenceParameters.Add(new ModuleReferenceParameterRow(parameter, value)); } if (_moduleParametersTab != null) _moduleParametersTab.Visibility = _moduleReferenceParameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed; if (_actionConfigurationTab != null) _actionConfigurationTab.Content = _moduleReferenceConfigurationPanel; if (_actionConfigurator != null) _actionConfigurator.Visibility = Visibility.Collapsed; if (_editorTabs != null) _editorTabs.SelectedItem = _actionConfigurationTab; _loadingStep = false; return; }
            if (_moduleParametersTab != null) _moduleParametersTab.Visibility = Visibility.Collapsed; if (_actionConfigurationTab != null) _actionConfigurationTab.Content = _actionConfigurator; if (_actionConfigurator != null) _actionConfigurator.Visibility = Visibility.Visible; if (_editorTabs != null) _editorTabs.SelectedIndex = 0;
            SequenceStepDefinition step = _selectedStep.ToStep(); int order = _selectedBlock == null ? 0 : _selectedBlock.Steps.IndexOf(_selectedStep) + 1; _stepTitle.Text = "当前动作：  " + order.ToString("00", CultureInfo.InvariantCulture) + "  " + step.StepName; _stepNameBox.Text = step.StepName; _actionDescriptionBox.Text = BlockStepListItem.FriendlyActionText(step); _actionModuleBox.Text = (_selectedBlock == null ? string.Empty : ModuleKindText(_selectedBlock) + " · " + _selectedBlock.Name); _actionTypeBox.Text = step.FunctionName == "FCT_ExecuteLogic" ? "逻辑" : "设置"; _functionNameText.Text = step.FunctionName;
            bool generic = step.FunctionName.StartsWith("FCT_", StringComparison.Ordinal); _resultMode.IsEnabled = generic; _resultMode.SelectedItem = generic ? Convert.ToString(step.Get("ResultMode", "Action"), CultureInfo.InvariantCulture) : "Action";
            _stepRunMode.SelectedItem = step.RunMode; _stepRecordingLog.IsChecked = step.RecordingLog;
            foreach (KeyValuePair<string, object> pair in step.Parameters)
            {
                string binding; bool exposed = _selectedStep.ParameterBindings.TryGetValue(pair.Key, out binding);
                _parameters.Add(new StudioStepParameterRow(pair.Key, pair.Value, exposed, binding ?? pair.Key));
            }
            if (_actionConfigurator != null) _actionConfigurator.LoadStep(step, _selectedStep.ParameterBindings); _loadingStep = false;
        }

        private void ResultMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingStep || _selectedStep == null || !_resultMode.IsEnabled) return;
            string mode = Convert.ToString(_resultMode.SelectedItem, CultureInfo.InvariantCulture) ?? "Action";
            if (mode == "NumericLimit") { EnsureParameter("LowLimit", 0.0); EnsureParameter("HighLimit", 0.0); EnsureParameter("Comtype", "GELE"); EnsureParameter("Unit", string.Empty); }
            else if (mode == "StringLimit") EnsureParameter("Limit", string.Empty);
            else if (mode == "Variable") EnsureParameter("OutputVariable", "Value1");
        }
        private void EnsureParameter(string name, object value) { if (_parameters.Any(row => row.Name == name)) return; _parameters.Add(new StudioStepParameterRow(name, value, false, name)); }

        private void CommitBlock()
        {
            if (_selectedBlock == null) return; string before = FctStudioProjectService.Serialize(_getProject()); _selectedBlock.Name = _blockName.Text.Trim(); _selectedBlock.Category = _blockCategory.Text.Trim(); _selectedBlock.Version = _blockVersion.Text.Trim(); _selectedBlock.Description = _blockDescription.Text.Trim(); _selectedBlock.SupportedProducts = _blockProducts.Text.Split(new[] { ',', ';', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim().ToUpperInvariant()).Distinct().ToList();
            BlockListItem item = _blocks.FirstOrDefault(value => value.Block == _selectedBlock); if (item != null) item.Refresh();
            if (!string.Equals(before, FctStudioProjectService.Serialize(_getProject()), StringComparison.Ordinal)) _changed();
        }
        private void CommitStep()
        {
            if (_selectedStep == null) return; string before = FctStudioProjectService.Serialize(_getProject());
            if (_selectedStep.IsModuleReference)
            {
                if (_moduleReferenceParameterGrid != null) { _moduleReferenceParameterGrid.CommitEdit(DataGridEditingUnit.Cell, true); _moduleReferenceParameterGrid.CommitEdit(DataGridEditingUnit.Row, true); }
                Dictionary<string, object> editedValues = _moduleReferenceParameters.ToDictionary(value => value.Name, value => value.ConvertValue(), StringComparer.Ordinal);
                ModuleBindingChoice bindingChoice = _moduleReferenceBindingBox == null ? null : _moduleReferenceBindingBox.SelectedItem as ModuleBindingChoice; FunctionBlockDefinition binding = bindingChoice == null ? null : bindingChoice.Block;
                if (binding != null)
                {
                    _selectedStep.ReferencedBlockId = binding.Id;
                    Dictionary<string, object> next = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (BlockParameterDefinition parameter in binding.Parameters ?? new List<BlockParameterDefinition>()) { object value; next[parameter.Name] = editedValues.TryGetValue(parameter.Name, out value) ? value : parameter.DefaultValue; }
                    _selectedStep.ReferencedParameterOverrides = next;
                }
                else _selectedStep.ReferencedParameterOverrides = editedValues;
                string instanceName = _moduleReferenceInstanceNameBox == null ? string.Empty : (_moduleReferenceInstanceNameBox.Text ?? string.Empty).Trim();
                _selectedStep.ReferencedBlockName = string.IsNullOrWhiteSpace(instanceName) ? (binding == null ? _selectedStep.ReferencedBlockName : binding.Name) : instanceName;
                BlockStepListItem row = _steps.FirstOrDefault(value => value.Step == _selectedStep); if (row != null) row.Refresh(); BlockListItem treeItem = _blocks.FirstOrDefault(value => value.Block == _selectedBlock); if (treeItem != null) treeItem.RefreshChildren(_getProject().Blocks);
                if (!string.Equals(before, FctStudioProjectService.Serialize(_getProject()), StringComparison.Ordinal)) _changed();
                return;
            }
            if (_actionConfigurator != null) return;
            _parameterGrid.CommitEdit(DataGridEditingUnit.Cell, true); _parameterGrid.CommitEdit(DataGridEditingUnit.Row, true);
            SequenceStepDefinition step = _selectedStep.ToStep();
            step.StepName = string.IsNullOrWhiteSpace(_stepNameBox.Text) ? step.StepName : _stepNameBox.Text.Trim();
            step.RunMode = Convert.ToString(_stepRunMode.SelectedItem, CultureInfo.InvariantCulture) ?? "Normal"; step.RecordingLog = _stepRecordingLog.IsChecked == true;
            foreach (StudioStepParameterRow row in _parameters)
            {
                step.SetParameterFromText(row.Name, row.ValueText, row.OriginalType);
                if (row.IsExposed)
                {
                    string parameterName = string.IsNullOrWhiteSpace(row.BlockParameterName) ? row.Name : row.BlockParameterName.Trim(); _selectedStep.ParameterBindings[row.Name] = parameterName;
                    BlockParameterDefinition parameter = _selectedBlock.Parameters.FirstOrDefault(value => value.Name == parameterName);
                    if (parameter == null) _selectedBlock.Parameters.Add(new BlockParameterDefinition { Name = parameterName, DisplayName = parameterName, Type = row.TypeName, DefaultValue = step.Get(row.Name), Unit = ModuleParameterUnit(row.Name), Description = "来自动作字段：" + row.DisplayName, Required = true });
                    else parameter.DefaultValue = step.Get(row.Name);
                }
                else _selectedStep.ParameterBindings.Remove(row.Name);
            }
            if (step.FunctionName.StartsWith("FCT_", StringComparison.Ordinal)) step.Properties["ResultMode"] = Convert.ToString(_resultMode.SelectedItem, CultureInfo.InvariantCulture) ?? "Action";
            _selectedStep.StepProperties = new Dictionary<string, object>(step.Properties, StringComparer.Ordinal);
            HashSet<string> used = new HashSet<string>(_selectedBlock.Steps.SelectMany(value => value.ParameterBindings.Values), StringComparer.Ordinal); _selectedBlock.Parameters.RemoveAll(parameter => !used.Contains(parameter.Name));
            BlockStepListItem item = _steps.FirstOrDefault(value => value.Step == _selectedStep); if (item != null) item.Refresh(); if (!string.Equals(before, FctStudioProjectService.Serialize(_getProject()), StringComparison.Ordinal)) _changed();
        }

        private void NewBlock_Click(object sender, RoutedEventArgs e) { FctStudioProject project = _getProject(); FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "新功能块", Category = "自定义", ModuleKind = "Custom", IsStandard = false }; FunctionBlockPropertiesWindow dialog = new FunctionBlockPropertiesWindow(block, new[] { project == null ? string.Empty : project.Product }) { Owner = Window.GetWindow(this) }; if (dialog.ShowDialog() != true) return; dialog.ApplyTo(block); project.Blocks.Add(block); BlockListItem item = CreateBlockListItem(block); _blocks.Add(item); CollectionViewSource.GetDefaultView(_blockList.ItemsSource).Refresh(); _blockList.SelectedItem = item; _changed(); }
        private void DuplicateAsCustom_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; FunctionBlockDefinition copy = _selectedBlock.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Name = UniqueBlockName(_selectedBlock.Name + " - 自定义"); copy.ModuleKind = "Custom"; copy.IsStandard = false; foreach (BlockStepDefinition step in copy.Steps) step.Id = Guid.NewGuid().ToString("N"); _getProject().Blocks.Add(copy); BlockListItem item = CreateBlockListItem(copy); _blocks.Add(item); CollectionViewSource.GetDefaultView(_blockList.ItemsSource).Refresh(); _blockList.SelectedItem = item; _changed(); }
        private void CopyBlock_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; FunctionBlockDefinition copy = _selectedBlock.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " - 副本"; foreach (BlockStepDefinition step in copy.Steps) step.Id = Guid.NewGuid().ToString("N"); _getProject().Blocks.Add(copy); BlockListItem item = CreateBlockListItem(copy); _blocks.Add(item); _blockList.SelectedItem = item; _changed(); }
        private void DeleteBlock_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; if (MessageBox.Show("删除功能块“" + _selectedBlock.Name + "”？流程中已有的快照不会删除。", "功能块", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; FunctionBlockDefinition removed = _selectedBlock; GlobalModuleLibraryService.Delete(removed); _getProject().Blocks.Remove(removed); BlockListItem item = _blocks.First(value => value.Block == removed); _blocks.Remove(item); _selectedBlock = null; _changed(); }
        private void BatchDeleteBlocks_Click(object sender, RoutedEventArgs e)
        {
            List<BlockListItem> selected = _blockList == null ? new List<BlockListItem>() : _blockList.SelectedItems.Cast<object>().OfType<BlockListItem>().Distinct().ToList();
            if (selected.Count == 0 && _selectedBlock != null) { BlockListItem current = _blocks.FirstOrDefault(value => value.Block == _selectedBlock); if (current != null) selected.Add(current); }
            if (selected.Count == 0) { MessageBox.Show("请先在左侧使用 Ctrl 或 Shift 多选需要删除的模块。", "批量删除模块", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            int lockedCount = selected.Count(value => string.Equals(value.Block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase)); selected = selected.Where(value => !string.Equals(value.Block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase)).ToList(); if (selected.Count == 0) { MessageBox.Show("标准模块模板不能删除。可以复制为自定义模块后再修改。", "标准模块只读", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            string names = string.Join("\n", selected.Take(12).Select(value => "• " + value.Block.Name)); if (selected.Count > 12) names += "\n…以及另外 " + (selected.Count - 12) + " 个模块";
            if (MessageBox.Show("确定删除选中的 " + selected.Count + " 个模块？\n\n" + names + "\n\n流程中已经加入的快照不会被删除。" + (lockedCount > 0 ? "\n另有 " + lockedCount + " 个标准模块已自动跳过。" : string.Empty), "批量删除模块", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            foreach (BlockListItem item in selected) { GlobalModuleLibraryService.Delete(item.Block); _getProject().Blocks.Remove(item.Block); _blocks.Remove(item); }
            _selectedBlock = null; _selectedStep = null; _steps.Clear(); _parameters.Clear(); CollectionViewSource.GetDefaultView(_blockList.ItemsSource).Refresh(); if (_blocks.Count > 0) _blockList.SelectedItem = _blocks[0]; _changed();
        }
        private void SetAllBlocksForBatch(bool selected) { if (_blockList == null) return; if (selected) _blockList.SelectAll(); else _blockList.UnselectAll(); }
        private void AddStep_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; AddStep(ActionConfigurationPanel.CreateDraft()); }
        private void AddFromCatalog_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; StepSelectionWindow dialog = new StepSelectionWindow("从测试项库导入", SequenceEditing.BuildFunctionTemplates(_getSteps()), false, _locatorRepository) { Owner = Window.GetWindow(this) }; if (dialog.ShowDialog() == true) foreach (SequenceStepDefinition step in dialog.SelectedSteps) AddStep(step); }
        private void ImportSteps_Click(object sender, RoutedEventArgs e) { if (_selectedBlock == null) return; StepSelectionWindow dialog = new StepSelectionWindow("从当前SEQ批量选择STEP", _getSteps(), true) { Owner = Window.GetWindow(this) }; if (dialog.ShowDialog() == true) foreach (SequenceStepDefinition step in dialog.SelectedSteps) AddStep(step); }
        private void AddDelay_Click(object sender, RoutedEventArgs e) { AddLogic("Delay", new Dictionary<string, object> { { "TimeMs", 1000 } }); }
        private void AddLoop_Click(object sender, RoutedEventArgs e) { AddLogic("FixedLoop", new Dictionary<string, object> { { "LoopId", "Loop1" }, { "Count", 2 }, { "TargetStepName", "" } }); }
        private void AddCondition_Click(object sender, RoutedEventArgs e) { AddLogic("Condition", new Dictionary<string, object> { { "VariableName", "Value1" }, { "DataType", "Number" }, { "Compare", "GE" }, { "RightValue", "0" }, { "TrueGoto", "" }, { "FalseGoto", "" }, { "RecordResult", true } }); }
        private void AddPlugin_Click(object sender, RoutedEventArgs e) { AddStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Plugin instrument action" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", "CUSTOM" }, { "Operation", "Execute" }, { "PluginAssembly", "GenericActionPlugins\\MyInstrument.dll" }, { "PluginType", "MyCompany.MyInstrumentPlugin" }, { "ParametersJson", "{}" }, { "ResultMode", "Action" } })); }
        private void AddShutdown_Click(object sender, RoutedEventArgs e) { AddLogic("SafeShutdown", new Dictionary<string, object>()); }
        private void AddLogic(string operation, IDictionary<string, object> parameters) { Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", operation }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RecordingLog", true }, { "Operation", operation } }; foreach (KeyValuePair<string, object> pair in parameters) values[pair.Key] = pair.Value; AddStep(new SequenceStepDefinition(values)); }
        private void AddModuleReference(FunctionBlockDefinition target, FunctionBlockDefinition source, int index)
        {
            if (target == null || source == null) return; if (target.Id == source.Id || WouldCreateModuleCycle(source, target.Id, new HashSet<string>(StringComparer.Ordinal))) { MessageBox.Show("不能引用自己，也不能形成模块循环引用。", "插入模块", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            index = Math.Max(0, Math.Min(target.Steps.Count, index)); BlockStepDefinition reference = new BlockStepDefinition { ReferencedBlockId = source.Id, ReferencedBlockName = source.Name, Enabled = true }; foreach (BlockParameterDefinition parameter in source.Parameters ?? new List<BlockParameterDefinition>()) reference.ReferencedParameterOverrides[parameter.Name] = parameter.DefaultValue; target.Steps.Insert(index, reference); BlockListItem treeItem = _blocks.FirstOrDefault(value => value.Block == target); if (treeItem != null) treeItem.RefreshChildren(_getProject().Blocks);
            BlockStepListItem item; if (_selectedBlock != target) { SelectBlock(target.Id); item = _steps.FirstOrDefault(value => value.Step == reference); } else { item = CreateStepItem(reference, index + 1); _steps.Insert(index, item); RefreshStepOrders(); } if (item != null) { _stepList.SelectedItem = item; _stepList.ScrollIntoView(item); } _changed();
        }
        private BlockStepListItem CreateStepItem(BlockStepDefinition step, int order, bool breakpoint = false) { FunctionBlockDefinition referenced = step != null && step.IsModuleReference ? _getProject().Blocks.FirstOrDefault(value => value.Id == step.ReferencedBlockId) : null; return new BlockStepListItem(step, order, breakpoint, referenced); }
        private bool WouldCreateModuleCycle(FunctionBlockDefinition source, string targetId, ISet<string> visited) { if (source == null || !visited.Add(source.Id)) return false; foreach (BlockStepDefinition step in source.Steps ?? new List<BlockStepDefinition>()) if (step.IsModuleReference) { if (step.ReferencedBlockId == targetId) return true; FunctionBlockDefinition child = _getProject().Blocks.FirstOrDefault(value => value.Id == step.ReferencedBlockId); if (WouldCreateModuleCycle(child, targetId, visited)) return true; } return false; }
        private void AddStep(SequenceStepDefinition definition, int? relativeOffset = null) { if (_selectedBlock == null) return; BlockStepDefinition step = new BlockStepDefinition { StepProperties = new Dictionary<string, object>(definition.Properties, StringComparer.Ordinal) }; int index = _selectedBlock.Steps.Count; if (relativeOffset.HasValue && _selectedStep != null) index = Math.Max(0, Math.Min(_selectedBlock.Steps.Count, _selectedBlock.Steps.IndexOf(_selectedStep) + relativeOffset.Value)); _selectedBlock.Steps.Insert(index, step); BlockStepListItem item = CreateStepItem(step, index + 1); _steps.Insert(index, item); RefreshStepOrders(); _stepList.SelectedItem = item; _stepList.ScrollIntoView(item); _changed(); }
        private bool TryResolveSelectedStep(out BlockStepDefinition step, out int modelIndex, out int viewIndex)
        {
            BlockStepListItem selectedRow = _stepList == null ? null : _stepList.SelectedItem as BlockStepListItem; BlockStepDefinition candidate = selectedRow == null ? _selectedStep : selectedRow.Step; step = candidate; modelIndex = _selectedBlock == null || candidate == null ? -1 : _selectedBlock.Steps.IndexOf(candidate); viewIndex = selectedRow == null ? _steps.ToList().FindIndex(value => value.Step == candidate) : _steps.IndexOf(selectedRow); if (viewIndex < 0 && candidate != null) viewIndex = _steps.ToList().FindIndex(value => value.Step == candidate); return modelIndex >= 0;
        }
        private void CopyStep_Click(object sender, RoutedEventArgs e) { if (!EnsureEditableBlock()) return; BlockStepDefinition step; int modelIndex, viewIndex; if (!TryResolveSelectedStep(out step, out modelIndex, out viewIndex)) { LoadBlock(); return; } BlockStepDefinition copy = step.Clone(); copy.Id = Guid.NewGuid().ToString("N"); int index = modelIndex + 1; _selectedBlock.Steps.Insert(index, copy); _steps.Insert(Math.Min(index, _steps.Count), CreateStepItem(copy, index + 1)); RefreshStepOrders(); _stepList.SelectedItem = _steps[Math.Min(index, _steps.Count - 1)]; _changed(); }
        private void ToggleStep_Click(object sender, RoutedEventArgs e) { if (!EnsureEditableBlock()) return; BlockStepDefinition step; int modelIndex, viewIndex; if (!TryResolveSelectedStep(out step, out modelIndex, out viewIndex)) { LoadBlock(); return; } step.Enabled = !step.Enabled; BlockStepListItem item = viewIndex >= 0 && viewIndex < _steps.Count ? _steps[viewIndex] : _steps.FirstOrDefault(value => value.Step == step); if (item != null) item.Refresh(); _selectedStep = step; _changed(); }
        private void DeleteStep_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureEditableBlock()) return; BlockStepDefinition step; int modelIndex, viewIndex; if (!TryResolveSelectedStep(out step, out modelIndex, out viewIndex)) { LoadBlock(); return; }
            _selectedBlock.Steps.RemoveAt(modelIndex); if (viewIndex >= 0 && viewIndex < _steps.Count && _steps[viewIndex].Step == step) _steps.RemoveAt(viewIndex); else { BlockStepListItem row = _steps.FirstOrDefault(value => value.Step == step); if (row != null) _steps.Remove(row); else LoadBlock(); }
            _selectedStep = null; BlockListItem treeItem = _blocks.FirstOrDefault(value => value.Block == _selectedBlock); if (treeItem != null) treeItem.RefreshChildren(_getProject().Blocks); RefreshStepOrders(); if (_steps.Count > 0) _stepList.SelectedIndex = Math.Min(Math.Max(0, viewIndex), _steps.Count - 1); else LoadStep(); _changed();
        }
        private void MoveStepUp_Click(object sender, RoutedEventArgs e) { MoveStep(-1); }
        private void MoveStepDown_Click(object sender, RoutedEventArgs e) { MoveStep(1); }
        private void MoveStep(int offset) { if (!EnsureEditableBlock()) return; BlockStepDefinition step; int oldIndex, viewIndex; if (!TryResolveSelectedStep(out step, out oldIndex, out viewIndex)) { LoadBlock(); return; } int newIndex = oldIndex + offset; if (newIndex < 0 || newIndex >= _selectedBlock.Steps.Count) return; _selectedBlock.Steps.RemoveAt(oldIndex); _selectedBlock.Steps.Insert(newIndex, step); BlockStepListItem row = viewIndex >= 0 && viewIndex < _steps.Count ? _steps[viewIndex] : _steps.FirstOrDefault(value => value.Step == step); if (row == null) { LoadBlock(); return; } int targetViewIndex = Math.Max(0, Math.Min(_steps.Count - 1, viewIndex + offset)); _steps.Move(_steps.IndexOf(row), targetViewIndex); RefreshStepOrders(); _stepList.SelectedItem = row; _selectedStep = step; _changed(); }
        private void RefreshStepOrders() { for (int index = 0; index < _steps.Count; index++) _steps[index].SetOrder(index + 1); }
        private async void ExecuteCurrentAction_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStep == null) return;
            if (_selectedStep.IsModuleReference) { StepModule_Click(sender, e); return; }
            try
            {
                SequenceStepDefinition step = BuildStepFromEditor();
                bool highVoltage = step.FunctionName.StartsWith("HVDC_", StringComparison.Ordinal) || (step.FunctionName == "FCT_ExecuteAction" && string.Equals(Convert.ToString(step.Get("Device")), "HVDC", StringComparison.OrdinalIgnoreCase));
                if (highVoltage && MessageBox.Show("当前动作会操作高压电源。请确认接线、急停、负载和人员安全。是否执行？", "高压动作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                if (_actionConfigurator != null) { _actionConfigurator.ExecuteCurrent(); return; }
                _actionResult.Text = "正在执行：" + step.StepName;
                DateTime started = DateTime.Now;
                string result = await _executeStep(step);
                LegacyStepExecutionResult platform = _getLastPlatformResult(); bool succeeded = PlatformPassed(platform, result);
                _actionResult.Text = string.IsNullOrWhiteSpace(result) ? (platform == null || string.IsNullOrWhiteSpace(platform.TotalStatus) ? "执行完成" : platform.TotalStatus) : result;
                ActionConfigurator_ExecutionRecorded(new ActionHistoryRow(step, _actionResult.Text, succeeded, started, DateTime.Now, string.Empty, platform));
                if (!succeeded) throw new InvalidOperationException("平台判断失败，请查看测试值和LIMIT。");
            }
            catch (Exception ex) { _actionResult.Text = "执行失败：" + ex.Message; MessageBox.Show("动作执行失败：\n" + ex.Message, "手动调试", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private async void RunModule_Click(object sender, RoutedEventArgs e)
        {
            if (_moduleRunning || _selectedBlock == null) return;
            CommitStep();
            List<BlockStepListItem> enabled = _steps.Where(value => value.Step.Enabled).ToList();
            if (enabled.Count == 0) { MessageBox.Show("当前模块没有已启用的动作。", "运行模块", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            bool highVoltage = enabled.SelectMany(GetExecutableSteps).Any(step => step.FunctionName.StartsWith("HVDC_", StringComparison.Ordinal) || step.FunctionName == "FCT_ExecuteAction" && string.Equals(Convert.ToString(step.Get("Device")), "HVDC", StringComparison.OrdinalIgnoreCase));
            if (highVoltage && MessageBox.Show("当前模块包含高压操作。请确认接线、急停、负载和人员安全。是否按顺序执行？", "高压模块确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _moduleRunning = true; SetRunButton("\uE769", "运行中", Color.FromRgb(232, 145, 22));
            foreach (BlockStepListItem row in _steps) { row.SetRuntimeState(string.Empty); row.SetExecutionResult(null, string.Empty, true); }
            try
            {
                bool firstAction = true;
                foreach (BlockStepListItem row in enabled)
                {
                    if (row.Breakpoint && !firstAction) { row.SetRuntimeState("断点"); _stepList.SelectedItem = row; _stepList.ScrollIntoView(row); SetRunButton("\uE769", "已到断点", Color.FromRgb(220, 42, 42)); _actionResult.Text = "模块已在“" + row.ActionName + "”执行前暂停；可点击单步继续。"; return; }
                    firstAction = false;
                    _stepList.SelectedItem = row; _stepList.ScrollIntoView(row); row.SetRuntimeState("运行中");
                    try
                    {
                        await ExecuteModuleRowAsync(row);
                        row.SetRuntimeState("完成");
                    }
                    catch (Exception ex)
                    {
                        row.SetRuntimeState("失败"); SetRunButton("\uEA39", "运行失败", Color.FromRgb(207, 55, 55)); _actionResult.Text = "执行失败：" + ex.Message;
                        MessageBox.Show("模块在“" + row.ActionName + "”停止：\n" + ex.Message, "模块执行失败", MessageBoxButton.OK, MessageBoxImage.Error); return;
                    }
                }
                SetRunButton("\uE73E", "运行完成", Color.FromRgb(0, 151, 90)); _actionResult.Text = "模块“" + _selectedBlock.Name + "”全部执行完成。";
            }
            finally { _moduleRunning = false; _runModuleButton.IsEnabled = true; }
        }

        private async void StepModule_Click(object sender, RoutedEventArgs e)
        {
            if (_moduleRunning || _selectedBlock == null) return; CommitStep(); BlockStepListItem row = _stepList.SelectedItem as BlockStepListItem ?? _steps.FirstOrDefault(value => value.Step.Enabled); if (row == null || !row.Step.Enabled) { MessageBox.Show("请选择一个已启用的动作。", "单步", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _moduleRunning = true; row.SetRuntimeState("运行中"); _stepList.SelectedItem = row; _stepList.ScrollIntoView(row);
            try { await ExecuteModuleRowAsync(row); row.SetRuntimeState("完成"); }
            catch (Exception ex) { row.SetRuntimeState("失败"); row.SetExecutionResult(null, string.Empty, false); MessageBox.Show("单步执行失败：\n" + ex.Message, "单步", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { _moduleRunning = false; }
        }
        private IReadOnlyList<SequenceStepDefinition> GetExecutableSteps(BlockStepListItem row)
        {
            if (row == null) return new SequenceStepDefinition[0]; if (!row.Step.IsModuleReference) return new[] { row.Step.ToStep() }; FctStudioProject current = _getProject(); FunctionBlockDefinition child = current == null ? null : current.Blocks.FirstOrDefault(value => value.Id == row.Step.ReferencedBlockId); if (child == null) throw new InvalidOperationException("引用的标准模块不存在：" + row.Step.ReferencedBlockName);
            FctStudioProject temporary = new FctStudioProject { Product = current.Product, ProjectName = child.Name, Blocks = current.Blocks.Select(value => value.Clone()).ToList() }; FunctionBlockDefinition childClone = temporary.Blocks.First(value => value.Id == child.Id); FlowBlockInstance instance = new FlowBlockInstance { BlockId = childClone.Id, DisplayName = childClone.Name, Snapshot = childClone.Clone() }; foreach (BlockParameterDefinition parameter in childClone.Parameters) { object value; instance.ParameterOverrides[parameter.Name] = row.Step.ReferencedParameterOverrides != null && row.Step.ReferencedParameterOverrides.TryGetValue(parameter.Name, out value) ? value : parameter.DefaultValue; } temporary.Flow.Add(instance); return FctStudioCompiler.Compile(temporary).Document.Steps;
        }
        private async Task ExecuteModuleRowAsync(BlockStepListItem row)
        {
            IReadOnlyList<SequenceStepDefinition> steps = GetExecutableSteps(row); if (steps.Count == 0) throw new InvalidOperationException("模块引用中没有可执行STEP。"); bool multiple = steps.Count > 1;
            foreach (SequenceStepDefinition step in steps) { _actionResult.Text = "正在执行：" + step.StepName; DateTime started = DateTime.Now; string result = await _executeStep(step); LegacyStepExecutionResult platform = _getLastPlatformResult(); bool succeeded = PlatformPassed(platform, result); string display = string.IsNullOrWhiteSpace(result) ? (platform == null || string.IsNullOrWhiteSpace(platform.TotalStatus) ? "执行完成" : platform.TotalStatus) : result; ActionConfigurator_ExecutionRecorded(new ActionHistoryRow(step, display, succeeded, started, DateTime.Now, string.Empty, platform)); if (!multiple) row.SetExecutionResult(platform, display, succeeded); if (!succeeded) throw new InvalidOperationException("平台LIMIT判断失败：" + step.StepName); }
            if (multiple) row.SetExecutionResult(new LegacyStepExecutionResult("Passed", "Passed", new LegacyPlatformResultRow[0], DateTime.Now), string.Empty, true); _actionResult.Text = multiple ? "标准模块执行完成，共" + steps.Count + "个STEP。" : _actionResult.Text;
        }
        private static bool PlatformPassed(LegacyStepExecutionResult platform, string raw)
        {
            string status = platform == null ? raw : platform.TotalStatus;
            if (string.IsNullOrWhiteSpace(status)) return true;
            return status.Equals("Passed", StringComparison.OrdinalIgnoreCase) || status.Equals("Pass", StringComparison.OrdinalIgnoreCase) || status.Equals("Done", StringComparison.OrdinalIgnoreCase) || status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || status.Equals("True", StringComparison.OrdinalIgnoreCase);
        }
        private void SetRunButton(string glyph, string text, Color color)
        {
            _runModuleButton.Content = ToolbarContent(glyph, text); _runModuleButton.Background = new SolidColorBrush(color); _runModuleButton.BorderBrush = _runModuleButton.Background; _runModuleButton.Foreground = Brushes.White; _runModuleButton.IsEnabled = true;
        }
        private SequenceStepDefinition BuildStepFromEditor()
        {
            if (_actionConfigurator != null) return _actionConfigurator.BuildStep();
            _parameterGrid.CommitEdit(DataGridEditingUnit.Cell, true); _parameterGrid.CommitEdit(DataGridEditingUnit.Row, true);
            SequenceStepDefinition step = SequenceEditing.Clone(_selectedStep.ToStep()); step.StepName = string.IsNullOrWhiteSpace(_stepNameBox.Text) ? step.StepName : _stepNameBox.Text.Trim(); step.RunMode = Convert.ToString(_stepRunMode.SelectedItem, CultureInfo.InvariantCulture) ?? "Normal"; step.RecordingLog = _stepRecordingLog.IsChecked == true;
            foreach (StudioStepParameterRow row in _parameters) step.SetParameterFromText(row.Name, row.ValueText, row.OriginalType);
            if (step.FunctionName.StartsWith("FCT_", StringComparison.Ordinal)) step.Properties["ResultMode"] = Convert.ToString(_resultMode.SelectedItem, CultureInfo.InvariantCulture) ?? "Action";
            return step;
        }
        private void SaveConfiguredAction(SequenceStepDefinition step, IDictionary<string, string> bindings)
        {
            if (_selectedStep == null || _selectedBlock == null) return; string before = FctStudioProjectService.Serialize(_getProject()); _selectedStep.StepProperties = new Dictionary<string, object>(step.Properties, StringComparer.Ordinal); _selectedStep.ParameterBindings = bindings == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(bindings, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> binding in _selectedStep.ParameterBindings) { BlockParameterDefinition parameter = _selectedBlock.Parameters.FirstOrDefault(value => value.Name == binding.Value); object value = step.Get(binding.Key); if (parameter == null) _selectedBlock.Parameters.Add(new BlockParameterDefinition { Name = binding.Value, DisplayName = binding.Value, Type = value == null ? "String" : value.GetType().Name, DefaultValue = value, Unit = ModuleParameterUnit(binding.Key), Description = "来自动作字段：" + binding.Key, Required = true }); else parameter.DefaultValue = value; }
            HashSet<string> used = new HashSet<string>(_selectedBlock.Steps.SelectMany(value => value.ParameterBindings.Values), StringComparer.Ordinal); _selectedBlock.Parameters.RemoveAll(parameter => !used.Contains(parameter.Name)); BlockStepListItem item = _steps.FirstOrDefault(value => value.Step == _selectedStep); if (item != null) item.Refresh(); _stepTitle.Text = "当前动作：  " + (_selectedBlock.Steps.IndexOf(_selectedStep) + 1).ToString("00", CultureInfo.InvariantCulture) + "  " + step.StepName; if (!string.Equals(before, FctStudioProjectService.Serialize(_getProject()), StringComparison.Ordinal)) _changed(); _log("动作配置已保存：" + step.StepName);
        }
        private void AddHistoryToBlock_Click(object sender, RoutedEventArgs e) { ActionHistoryRow row = _historyList.SelectedItem as ActionHistoryRow; if (row == null || !EnsureEditableBlock()) return; AddStep(SequenceEditing.Clone(row.Step)); }
        private void Apply_Click(object sender, RoutedEventArgs e) { if (!EnsureEditableBlock()) return; CommitStep(); CommitBlock(); _log("功能块已更新：" + (_selectedBlock == null ? string.Empty : _selectedBlock.Name)); }

        private static Grid Card() { return new Grid { Background = new SolidColorBrush(Color.FromRgb(251, 252, 254)), Margin = new Thickness(0) }; }
        private static string ModuleKindText(FunctionBlockDefinition block) { return block == null ? string.Empty : string.Equals(block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase) ? "标准模块" : string.Equals(block.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品模块" : "自定义模块"; }
        private static TextBlock Title(string text) { return new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center }; }
        private static Button Button(string text, RoutedEventHandler handler) { Button button = new Button { Content = text, Margin = new Thickness(3, 2, 3, 2), Padding = new Thickness(10, 5, 10, 5), MinHeight = 30, Background = Brushes.White, BorderBrush = BorderColor(), BorderThickness = new Thickness(1) }; if (handler != null) button.Click += handler; return button; }
        private static Button PrimaryButton(string text, RoutedEventHandler handler) { Button button = Button(text, handler); button.Background = new SolidColorBrush(Color.FromRgb(24, 112, 224)); button.BorderBrush = button.Background; button.Foreground = Brushes.White; return button; }
        private static TextBox Box() { return new TextBox { Margin = new Thickness(3), Padding = new Thickness(5, 3, 5, 3) }; }
        private static TextBlock Label(string text) { return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) }; }
        private static void AddField(Grid grid, string label, TextBox box, int row, int labelColumn, int boxColumn, int span = 1) { TextBlock text = Label(label); Grid.SetRow(text, row); Grid.SetColumn(text, labelColumn); grid.Children.Add(text); Grid.SetRow(box, row); Grid.SetColumn(box, boxColumn); Grid.SetColumnSpan(box, span); grid.Children.Add(box); }
        private static void AddControlField(Grid grid, string label, FrameworkElement control, int row, int labelColumn = 0, int controlColumn = 1) { TextBlock text = Label(label); text.Foreground = TextSecondary(); text.FontSize = 12; Grid.SetRow(text, row); Grid.SetColumn(text, labelColumn); grid.Children.Add(text); Grid.SetRow(control, row); Grid.SetColumn(control, controlColumn); grid.Children.Add(control); }
        private static SolidColorBrush Accent() { return new SolidColorBrush(Color.FromRgb(24, 112, 224)); }
        private static SolidColorBrush TextPrimary() { return new SolidColorBrush(Color.FromRgb(37, 49, 67)); }
        private static SolidColorBrush TextSecondary() { return new SolidColorBrush(Color.FromRgb(104, 118, 138)); }
        private static SolidColorBrush BorderColor() { return new SolidColorBrush(Color.FromRgb(220, 228, 239)); }
        private static SolidColorBrush PageBackground() { return new SolidColorBrush(Color.FromRgb(242, 245, 249)); }
        private static Border Surface() { return new Border { Background = new SolidColorBrush(Color.FromRgb(251, 252, 254)), BorderBrush = BorderColor(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) }; }
        private static TextBlock SectionTitle(string text) { return new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center }; }
        private static TextBlock IconText(string glyph, string tooltip) { return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = TextSecondary(), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tooltip }; }
        private static TextBlock LinkText(string text) { return new TextBlock { Text = text, Foreground = Accent(), FontSize = 11, Margin = new Thickness(9, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center }; }
        private static Button GhostButton(string text, RoutedEventHandler handler) { Button button = Button(text, handler); button.Background = Brushes.White; button.BorderBrush = BorderColor(); button.FontSize = 11; button.MinHeight = 28; button.Padding = new Thickness(9, 4, 9, 4); return button; }
        private static Button CompactButton(string text, RoutedEventHandler handler) { Button button = GhostButton(text, handler); button.Width = 28; button.Padding = new Thickness(0); return button; }
        private static Button ToolbarButton(string glyph, string text, RoutedEventHandler handler, bool primary) { Button button = primary ? PrimaryButton(string.Empty, handler) : GhostButton(string.Empty, handler); button.Content = ToolbarContent(glyph, text); return button; }
        private static StackPanel ToolbarContent(string glyph, string text) { StackPanel content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }); content.Children.Add(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }); return content; }
        private static Button IconButton(string glyph, string tooltip, RoutedEventHandler handler) { Button button = CompactButton(string.Empty, handler); button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; button.ToolTip = tooltip; return button; }
        private static TextBox ReadOnlyBox(string text) { return new TextBox { Text = text, IsReadOnly = true, Height = 25, Margin = new Thickness(3, 1, 3, 1), Padding = new Thickness(7, 3, 7, 3), Foreground = TextSecondary() }; }
        private static Grid FormGrid(int fieldRows, double labelWidth) { Grid grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); for (int i = 0; i < fieldRows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) }); return grid; }
        private static TextBlock FormHeading(string text) { return new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }; }
        private static string ModuleParameterUnit(string name) { switch (name) { case "Voltage": case "SourceVoltage": return "V"; case "Current": case "SourceCurrent": case "MaxCurrent": return "A"; case "Speed": return "rpm"; case "Position": return "deg"; case "TimeMs": return "ms"; case "HoldTime": return "s"; case "Frequency": return "Hz"; case "Resistance": case "ResValue": return "ohm"; default: return string.Empty; } }
        private static Grid SmallNumberBox(string value, string unit) { Grid grid = new Grid { Margin = new Thickness(3, 1, 3, 1) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); TextBox box = new TextBox { Text = value, Height = 25, Padding = new Thickness(7, 3, 7, 3) }; grid.Children.Add(box); TextBlock unitText = new TextBlock { Text = unit, Foreground = TextSecondary(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(unitText, 1); grid.Children.Add(unitText); return grid; }
        private static Style MultilineCellStyle(Brush foreground, FontWeight weight) { Style style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.ForegroundProperty, foreground)); style.Setters.Add(new Setter(TextBlock.FontWeightProperty, weight)); style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center)); style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0))); DataTrigger selected = new DataTrigger { Binding = new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1) }, Value = true }; selected.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold)); style.Triggers.Add(selected); return style; }
        private static Style TagCellStyle() { Style style = MultilineCellStyle(Accent(), FontWeights.Normal); style.Setters.Add(new Setter(TextBlock.BackgroundProperty, new SolidColorBrush(Color.FromRgb(236, 244, 255)))); style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(5, 2, 5, 2))); style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left)); return style; }
        private static Style GreenCellStyle() { return MultilineCellStyle(new SolidColorBrush(Color.FromRgb(0, 146, 89)), FontWeights.Normal); }
        private static Style BlueCellStyle() { Style style = MultilineCellStyle(Accent(), FontWeights.SemiBold); style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center)); return style; }
        private static Style StudioRowStyle() { Style style = new Style(typeof(DataGridRow)); style.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.White)); style.Setters.Add(new Setter(DataGridRow.ForegroundProperty, TextPrimary())); style.Setters.Add(new Setter(DataGridRow.BorderThicknessProperty, new Thickness(0))); Trigger alternate = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 }; alternate.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(251, 252, 254)))); style.Triggers.Add(alternate); Trigger hover = new Trigger { Property = DataGridRow.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(244, 248, 253)))); style.Triggers.Add(hover); Trigger selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(231, 240, 255)))); selected.Setters.Add(new Setter(DataGridRow.BorderBrushProperty, Accent())); selected.Setters.Add(new Setter(DataGridRow.BorderThicknessProperty, new Thickness(2, 0, 0, 0))); selected.Setters.Add(new Setter(DataGridRow.ForegroundProperty, TextPrimary())); style.Triggers.Add(selected); return style; }
        private static Style StudioHeaderStyle() { Style style = new Style(typeof(DataGridColumnHeader)); style.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(247, 249, 252)))); style.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new SolidColorBrush(Color.FromRgb(69, 82, 102)))); style.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.SemiBold)); style.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 13d)); style.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, BorderColor())); style.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1))); style.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(10, 0, 6, 0))); return style; }
        private static DataGridTemplateColumn StepTemplateColumn() { FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); stack.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center); FrameworkElementFactory handle = new FrameworkElementFactory(typeof(TextBlock)); handle.SetValue(TextBlock.TextProperty, "\uE700"); handle.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); handle.SetValue(TextBlock.ForegroundProperty, Accent()); handle.SetValue(TextBlock.MarginProperty, new Thickness(3, 0, 7, 0)); stack.AppendChild(handle); FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(204, 218, 239))); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); border.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2)); FrameworkElementFactory number = new FrameworkElementFactory(typeof(TextBlock)); number.SetBinding(TextBlock.TextProperty, new Binding("StepNumber")); number.SetValue(TextBlock.ForegroundProperty, TextPrimary()); border.AppendChild(number); stack.AppendChild(border); return new DataGridTemplateColumn { Header = "步骤", Width = 75, CellTemplate = new DataTemplate { VisualTree = stack }, IsReadOnly = true }; }
        private static DataGridTemplateColumn ActionNameTemplateColumn() { FrameworkElementFactory title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("ActionName")); title.SetBinding(TextBlock.ToolTipProperty, new Binding("ActionName")); title.SetValue(TextBlock.ForegroundProperty, TextPrimary()); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.FontSizeProperty, 13d); title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); title.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 4, 0)); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); return new DataGridTemplateColumn { Header = "动作名称", Width = new DataGridLength(2.2, DataGridLengthUnitType.Star), CellTemplate = new DataTemplate { VisualTree = title }, IsReadOnly = true }; }
        private static DataGridTemplateColumn ModuleTagTemplateColumn() { FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.SetBinding(Border.BackgroundProperty, new Binding("ModuleTagBackground")); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); border.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3)); border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left); border.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center); FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("ModuleName")); text.SetBinding(TextBlock.ForegroundProperty, new Binding("ModuleTagForeground")); text.SetValue(TextBlock.FontSizeProperty, 11d); text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); border.AppendChild(text); return new DataGridTemplateColumn { Header = "模块类型", Width = 115, CellTemplate = new DataTemplate { VisualTree = border }, IsReadOnly = true }; }
        private static DataGridTemplateColumn StatusTemplateColumn() { FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); stack.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center); FrameworkElementFactory icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetValue(TextBlock.TextProperty, "\uE930"); icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); icon.SetBinding(TextBlock.ForegroundProperty, new Binding("StatusBrush")); icon.SetValue(TextBlock.FontSizeProperty, 12d); icon.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 6, 0)); stack.AppendChild(icon); FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("ConfigurationStatus")); text.SetValue(TextBlock.ForegroundProperty, TextPrimary()); text.SetValue(TextBlock.FontSizeProperty, 12d); stack.AppendChild(text); return new DataGridTemplateColumn { Header = "状态", Width = 145, CellTemplate = new DataTemplate { VisualTree = stack }, IsReadOnly = true }; }
        private DataGridTemplateColumn PlatformDisplayTemplateColumn() { FrameworkElementFactory host = new FrameworkElementFactory(typeof(Grid)); host.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch); host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch); FrameworkElementFactory check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(CheckBox.IsCheckedProperty, new Binding("PlatformVisible") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); check.SetBinding(CheckBox.VisibilityProperty, new Binding("DirectStepVisibility")); check.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center); check.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center); check.SetValue(CheckBox.WidthProperty, 22d); check.SetValue(CheckBox.HeightProperty, 22d); check.SetValue(CheckBox.MarginProperty, new Thickness(0)); check.SetValue(CheckBox.PaddingProperty, new Thickness(0)); check.SetValue(CheckBox.ToolTipProperty, "是否在平台界面记录显示；对应RecordingLog"); check.AddHandler(ToggleButton.ClickEvent, new RoutedEventHandler(PlatformDisplay_Click)); host.AppendChild(check); Style cell = new Style(typeof(DataGridCell)); cell.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); cell.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch)); cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); cell.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent)); cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); cell.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); Style header = new Style(typeof(DataGridColumnHeader)); header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)); header.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); return new DataGridTemplateColumn { Header = "平台显示", Width = 82, CellTemplate = new DataTemplate { VisualTree = host }, CellStyle = cell, HeaderStyle = header }; }
        private void PlatformDisplay_Click(object sender, RoutedEventArgs e) { Dispatcher.BeginInvoke(new Action(_changed)); }
        private static DataGridTemplateColumn MoreTemplateColumn() { FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock)); text.SetValue(TextBlock.TextProperty, "\uE712"); text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); text.SetValue(TextBlock.ForegroundProperty, Accent()); text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); return new DataGridTemplateColumn { Header = "", Width = 30, CellTemplate = new DataTemplate { VisualTree = text }, IsReadOnly = true }; }
    }

    internal sealed class ModuleBindingChoice
    {
        public ModuleBindingChoice(FunctionBlockDefinition block) { Block = block; }
        public FunctionBlockDefinition Block { get; private set; }
        public string DisplayText { get { string kind = string.Equals(Block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase) ? "标准模块" : string.Equals(Block.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品模块" : "自定义模块"; return Block.Name + "（" + kind + "）"; } }
        public override string ToString() { return DisplayText; }
    }

    internal sealed class BlockListItem : INotifyPropertyChanged
    {
        private bool _isBatchSelected; private bool _isExpanded;
        public BlockListItem(FunctionBlockDefinition block, IEnumerable<FunctionBlockDefinition> children = null) { Block = block; Children = new ObservableCollection<ModuleTreeChildRow>((children ?? Enumerable.Empty<FunctionBlockDefinition>()).Select(value => new ModuleTreeChildRow(value))); }
        public FunctionBlockDefinition Block { get; private set; }
        public ObservableCollection<ModuleTreeChildRow> Children { get; private set; }
        public bool HasChildren { get { return Children.Count > 0; } }
        public bool IsExpanded { get { return _isExpanded; } set { if (_isExpanded == value) return; _isExpanded = value; PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs("IsExpanded")); } }
        public void RefreshChildren(IEnumerable<FunctionBlockDefinition> library) { Children.Clear(); if (Block != null && string.Equals(Block.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase)) foreach (FunctionBlockDefinition child in (Block.Steps ?? new List<BlockStepDefinition>()).Where(step => step.IsModuleReference).Select(step => (library ?? Enumerable.Empty<FunctionBlockDefinition>()).FirstOrDefault(value => value.Id == step.ReferencedBlockId)).Where(value => value != null).GroupBy(value => value.Id, StringComparer.Ordinal).Select(group => group.First())) Children.Add(new ModuleTreeChildRow(child)); Raise("HasChildren"); if (!HasChildren) IsExpanded = false; }
        public bool IsBatchSelected { get { return _isBatchSelected; } set { if (_isBatchSelected == value) return; _isBatchSelected = value; PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs("IsBatchSelected")); } }
        public string DisplayText { get { return Prefix + Block.Name + "\n" + Block.Category + " · " + Block.Steps.Count + " 个动作"; } }
        public string LibraryGroup { get { return string.Equals(Block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase) ? "标准模块" : string.Equals(Block.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品模块" : "自定义模块"; } }
        public string TreeText { get { return Prefix + Block.Name; } }
        private string Prefix { get { return string.Equals(Block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase) ? "标准 - " : string.Equals(Block.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品 - " : string.Empty; } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
        public void Refresh() { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) foreach (string name in new[] { "DisplayText", "LibraryGroup", "TreeText", "HasChildren", "IsExpanded" }) handler(this, new PropertyChangedEventArgs(name)); }
    }
    internal sealed class BlockStepListItem : INotifyPropertyChanged
    {
        private int _order;
        private string _runtimeState = string.Empty;
        private string _currentValue = string.Empty;
        private string _executionResult = string.Empty;
        private bool _breakpoint; private readonly FunctionBlockDefinition _referencedBlock;
        public BlockStepListItem(BlockStepDefinition step, int order = 0, bool breakpoint = false, FunctionBlockDefinition referencedBlock = null) { Step = step; _order = order; _breakpoint = breakpoint; _referencedBlock = referencedBlock; }
        public BlockStepDefinition Step { get; private set; }
        public bool IsModuleReference { get { return Step.IsModuleReference; } }
        public Visibility DirectStepVisibility { get { return Step.IsModuleReference ? Visibility.Collapsed : Visibility.Visible; } }
        public bool PlatformVisible { get { return !Step.IsModuleReference && Step.ToStep().RecordingLog; } set { if (Step.IsModuleReference) return; Step.StepProperties["RecordingLog"] = value; Raise("PlatformVisible"); } }
        public string StepNumber { get { return _order <= 0 ? string.Empty : _order.ToString("00", CultureInfo.InvariantCulture); } }
        public bool Breakpoint { get { return _breakpoint; } set { if (_breakpoint == value) return; _breakpoint = value; Raise("Breakpoint"); Raise("BreakpointGlyph"); Raise("BreakpointBrush"); } }
        public string BreakpointGlyph { get { return _breakpoint ? "●" : "○"; } }
        public Brush BreakpointBrush { get { return _breakpoint ? new SolidColorBrush(Color.FromRgb(220, 42, 42)) : new SolidColorBrush(Color.FromRgb(188, 198, 212)); } }
        public void SetOrder(int order) { _order = order; Raise("StepNumber"); }
        public string ActionName { get { return Step.IsModuleReference ? Step.ReferencedBlockName : Step.ToStep().StepName; } }
        public string ActionSubTitle { get { if (Step.IsModuleReference) return ModuleName + "引用"; SequenceStepDefinition step = Step.ToStep(); return InstrumentStepCatalog.CategoryFor(step) + " · " + step.FunctionName; } }
        public string ActionDisplay { get { SequenceStepDefinition step = Step.ToStep(); return step.StepName + "\n" + InstrumentStepCatalog.CategoryFor(step); } }
        public string ModuleName { get { if (Step.IsModuleReference) { string category = _referencedBlock == null ? string.Empty : _referencedBlock.Category ?? string.Empty; if (category.IndexOf("主驱", StringComparison.OrdinalIgnoreCase) >= 0) return "主驱模块"; if (category.IndexOf("产品", StringComparison.OrdinalIgnoreCase) >= 0) return "产品模块"; if (category.IndexOf("电源", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("高压", StringComparison.OrdinalIgnoreCase) >= 0) return "电源模块"; if (category.IndexOf("温度", StringComparison.OrdinalIgnoreCase) >= 0) return "温度模块"; if (category.IndexOf("旋变", StringComparison.OrdinalIgnoreCase) >= 0) return "旋变模块"; if (category.IndexOf("冷却", StringComparison.OrdinalIgnoreCase) >= 0) return "冷却模块"; return "公共模块"; } string directCategory = InstrumentStepCatalog.CategoryFor(Step.ToStep()); if (directCategory.IndexOf("电源", StringComparison.OrdinalIgnoreCase) >= 0) return "电源"; if (directCategory.IndexOf("DMM", StringComparison.OrdinalIgnoreCase) >= 0) return "DMM"; if (directCategory.IndexOf("DAQ", StringComparison.OrdinalIgnoreCase) >= 0) return "DAQ"; if (directCategory.IndexOf("CAN", StringComparison.OrdinalIgnoreCase) >= 0 || directCategory.IndexOf("产品", StringComparison.OrdinalIgnoreCase) >= 0) return "产品"; if (Step.ToStep().FunctionName == "FCT_ExecuteLogic") return "逻辑"; return directCategory; } }
        public Brush ModuleTagBackground { get { string type = ModuleName; if (type.IndexOf("主驱", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("产品", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(232, 248, 238)); if (type.IndexOf("电源", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(255, 244, 224)); if (type.IndexOf("温度", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("旋变", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("冷却", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(243, 237, 255)); if (type.IndexOf("逻辑", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(255, 238, 244)); return new SolidColorBrush(Color.FromRgb(236, 244, 255)); } }
        public Brush ModuleTagForeground { get { string type = ModuleName; if (type.IndexOf("主驱", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("产品", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(22, 137, 74)); if (type.IndexOf("电源", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(190, 112, 16)); if (type.IndexOf("温度", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("旋变", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("冷却", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(111, 70, 180)); if (type.IndexOf("逻辑", StringComparison.OrdinalIgnoreCase) >= 0) return new SolidColorBrush(Color.FromRgb(190, 55, 105)); return new SolidColorBrush(Color.FromRgb(24, 112, 224)); } }
        public string ActionType { get { SequenceStepDefinition step = Step.ToStep(); string operation = Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture) ?? string.Empty; if (step.FunctionName == "FCT_ExecuteLogic") return "逻辑"; if (operation.StartsWith("Read", StringComparison.OrdinalIgnoreCase) || operation.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0) return "读取"; return "设置"; } }
        public string ActionDescription { get { if (Step.IsModuleReference) return "复用“" + Step.ReferencedBlockName + "” · 选中后在下方直接改当前实例参数 · 双击打开原模块"; SequenceStepDefinition step = Step.ToStep(); string required = MainTestMethodCatalog.RequiredInstrument(step); return FriendlyAction(step) + (string.IsNullOrWhiteSpace(required) ? string.Empty : " · 依赖" + required); } }
        public string CurrentValue { get { return _currentValue; } }
        public string ExecutionResult { get { return _executionResult; } }
        public static string FriendlyActionText(SequenceStepDefinition step) { return FriendlyAction(step); }
        public string ConfigurationStatus { get { if (!string.IsNullOrWhiteSpace(_runtimeState)) return _runtimeState; if (Step.IsModuleReference) return Step.Enabled ? "已引用" : "已停用"; SequenceStepDefinition step = Step.ToStep(); if (step.StepName.IndexOf("未配置", StringComparison.OrdinalIgnoreCase) >= 0) return "未配置"; if (!MainTestMethodCatalog.Contains(step.FunctionName)) return "功能不可用"; return Step.Enabled ? "已配置" : "已停用"; } }
        public Brush StatusBrush { get { string status = ConfigurationStatus; if (status == "完成" || status == "已配置" || status == "已引用") return new SolidColorBrush(Color.FromRgb(0, 151, 90)); if (status == "运行中" || status == "断点") return new SolidColorBrush(Color.FromRgb(232, 145, 22)); if (status == "已停用") return Brushes.DarkGray; return new SolidColorBrush(Color.FromRgb(210, 51, 51)); } }
        public void SetRuntimeState(string state) { _runtimeState = state ?? string.Empty; Raise("ConfigurationStatus"); Raise("StatusBrush"); }
        public void SetExecutionResult(LegacyStepExecutionResult platform, string rawResult, bool succeeded)
        {
            if (platform != null && platform.Results != null && platform.Results.Count == 1) { LegacyPlatformResultRow row = platform.Results[0]; _currentValue = row.Value ?? string.Empty; _executionResult = string.IsNullOrWhiteSpace(row.Status) ? (succeeded ? "Passed" : "Failed") : row.Status; }
            else if (platform != null && platform.Results != null && platform.Results.Count > 1) { _currentValue = string.Empty; _executionResult = string.Empty; }
            else if (string.IsNullOrWhiteSpace(rawResult)) { _currentValue = string.Empty; _executionResult = string.Empty; }
            else { _currentValue = rawResult; _executionResult = succeeded ? "Passed" : "Failed"; }
            Raise("CurrentValue"); Raise("ExecutionResult");
        }
        public bool Enabled { get { return Step.Enabled; } set { Step.Enabled = value; Raise("Enabled"); Raise("ConfigurationStatus"); Raise("StatusBrush"); } }
        public string Note { get { string binding = Step.ParameterBindings == null ? null : Step.ParameterBindings.Values.FirstOrDefault(); return string.IsNullOrWhiteSpace(binding) ? "—" : "对外变量：" + binding; } }
        public string MoreText { get { return "..."; } }
        public string DisplayText { get { if (Step.IsModuleReference) return (Step.Enabled ? "● " : "○ ") + Step.ReferencedBlockName + "\n   标准模块引用"; SequenceStepDefinition definition = Step.ToStep(); return (Step.Enabled ? "● " : "○ ") + definition.StepName + "\n   " + FriendlyAction(definition); } }
        public event PropertyChangedEventHandler PropertyChanged;
        public void Refresh() { foreach (string name in new[] { "DisplayText", "ActionName", "ActionDisplay", "ActionSubTitle", "ModuleName", "ModuleTagBackground", "ModuleTagForeground", "ActionType", "ActionDescription", "ConfigurationStatus", "StatusBrush", "Enabled", "PlatformVisible", "DirectStepVisibility", "Note", "CurrentValue", "ExecutionResult" }) Raise(name); }
        private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
        private static string FriendlyAction(SequenceStepDefinition step) { string name = step.StepName ?? string.Empty; if (name.IndexOf("HVDC", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0) return "设置 HVDC 电压 " + name.Split(' ').Last(); if (name.IndexOf("HVDC", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Output ON", StringComparison.OrdinalIgnoreCase) >= 0) return "打开 HVDC 输出"; if (name.IndexOf("HVDC", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Output OFF", StringComparison.OrdinalIgnoreCase) >= 0) return "关闭 HVDC 输出"; if (step.FunctionName == "FCT_ExecuteAction") return Convert.ToString(step.Get("Device"), CultureInfo.InvariantCulture) + " · " + Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture); if (step.FunctionName == "FCT_CANSignal") return "产品通信 · " + Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture) + "信号"; if (step.FunctionName == "FCT_CANTable") return "产品通信 · " + Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture) + "整表"; if (step.FunctionName == "FCT_ExecuteLogic") return "逻辑 · " + Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture); return InstrumentStepCatalog.CategoryFor(step); }
    }
    internal sealed class ModuleReferenceParameterRow : INotifyPropertyChanged
    {
        private string _valueText; private readonly Type _type;
        public ModuleReferenceParameterRow(BlockParameterDefinition parameter, object value) { Parameter = parameter; Name = parameter.Name; DisplayName = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName; Unit = parameter.Unit ?? string.Empty; Description = parameter.Description ?? string.Empty; _type = value == null ? typeof(string) : value.GetType(); _valueText = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture); }
        public BlockParameterDefinition Parameter { get; private set; } public string Name { get; private set; } public string DisplayName { get; private set; } public string Unit { get; private set; } public string Description { get; private set; }
        public string ValueText { get { return _valueText; } set { _valueText = value ?? string.Empty; PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs("ValueText")); } }
        public object ConvertValue() { if (_type == typeof(double) || _type == typeof(float) || _type == typeof(decimal)) return double.Parse(ValueText, CultureInfo.InvariantCulture); if (_type == typeof(int) || _type == typeof(long) || _type == typeof(short)) return int.Parse(ValueText, CultureInfo.InvariantCulture); if (_type == typeof(bool)) return bool.Parse(ValueText); return ValueText; }
        public event PropertyChangedEventHandler PropertyChanged;
    }
    internal sealed class StudioStepParameterRow : INotifyPropertyChanged
    {
        private string _valueText; private bool _isExposed; private string _blockParameterName;
        public StudioStepParameterRow(string name, object value, bool exposed, string binding) { Name = name; OriginalType = value == null ? typeof(string) : value.GetType(); TypeName = OriginalType.Name; _valueText = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture); _isExposed = exposed; _blockParameterName = binding; }
        public string Name { get; private set; } public Type OriginalType { get; private set; } public string TypeName { get; private set; }
        public string DisplayName { get { return Friendly(Name); } }
        public string ValueText { get { return _valueText; } set { _valueText = value ?? string.Empty; Raise("ValueText"); } }
        public bool IsExposed { get { return _isExposed; } set { _isExposed = value; Raise("IsExposed"); } }
        public string BlockParameterName { get { return _blockParameterName; } set { _blockParameterName = value ?? string.Empty; Raise("BlockParameterName"); } }
        public string Unit { get { return FriendlyUnit(Name); } }
        public event PropertyChangedEventHandler PropertyChanged; private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
        private static string Friendly(string name) { switch (name) { case "Voltage": return "电压"; case "Current": case "SourceCurrent": return "电流"; case "Output": return "输出开关"; case "Speed": return "转速"; case "Position": return "位置/角度"; case "TimeMs": return "延时时间(ms)"; case "Count": return "循环次数"; case "Frequency": return "频率"; case "HoldTime": return "保持时间"; case "MaxCurrent": return "目标电流"; case "LowLimit": return "下限"; case "HighLimit": return "上限"; case "Comtype": return "比较方式"; case "Unit": return "单位"; case "Device": return "仪器"; case "Operation": return "操作"; case "SignalsJson": return "DBC信号配置"; case "AddrOffset": return "表地址"; case "TableIndex": return "信号偏移"; case "DataSize": return "数据长度"; case "DataType": return "数据类型"; case "ValueText": return "设定值"; case "OutputVariable": return "保存变量名"; case "TargetStepName": return "跳转目标"; case "TrueGoto": return "条件成立跳转"; case "FalseGoto": return "条件不成立跳转"; default: return name; } }
        private static string FriendlyUnit(string name) { switch (name) { case "Voltage": return "V"; case "Current": case "SourceCurrent": case "MaxCurrent": return "A"; case "Speed": return "rpm"; case "Position": return "deg"; case "TimeMs": case "HoldTime": return "ms"; case "Frequency": return "Hz"; default: return string.Empty; } }
    }
    internal sealed class ActionHistoryRow
    {
        public ActionHistoryRow(SequenceStepDefinition step, string result) : this(step, result, true, DateTime.Now, DateTime.Now, string.Empty, null) { }
        public ActionHistoryRow(SequenceStepDefinition step, string result, bool succeeded, DateTime started, DateTime finished, string details, LegacyStepExecutionResult platformResult = null) { Step = SequenceEditing.Clone(step); Result = result ?? string.Empty; Succeeded = succeeded; Time = started; Finished = finished; Details = details ?? string.Empty; PlatformResult = platformResult; }
        public SequenceStepDefinition Step { get; private set; } public string Result { get; private set; } public DateTime Time { get; private set; } public DateTime Finished { get; private set; } public bool Succeeded { get; private set; } public string Details { get; private set; } public LegacyStepExecutionResult PlatformResult { get; private set; }
        public string SummaryText { get { return Time.ToString("HH:mm:ss.fff") + "  " + (Succeeded ? "✓" : "✕") + "  " + Step.StepName + Environment.NewLine + "   " + Math.Max(0, (Finished - Time).TotalMilliseconds).ToString("0", CultureInfo.InvariantCulture) + " ms · " + (PlatformResult == null || PlatformResult.Results == null ? 0 : PlatformResult.Results.Count) + " 条结果"; } }
        public string DisplayText { get { string first = Time.ToString("HH:mm:ss.fff") + "  " + (Succeeded ? "✓ 成功" : "✕ 失败") + "  " + Step.StepName + "  [" + Math.Max(0, (Finished - Time).TotalMilliseconds).ToString("0", CultureInfo.InvariantCulture) + " ms]"; string result = "结果：" + Result; return string.IsNullOrWhiteSpace(Details) ? first + Environment.NewLine + result : first + Environment.NewLine + result + Environment.NewLine + Details; } }
    }
}
