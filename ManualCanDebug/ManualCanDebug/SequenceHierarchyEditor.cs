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
using System.Windows.Input;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class SequenceHierarchyEditor : Grid
    {
        private readonly Func<FctStudioProject> _getProject;
        private readonly Action _changed;
        private readonly Action<SequenceHierarchyRow> _configure;
        private readonly Func<SequenceHierarchyRow, Task> _runFromRow;
        private readonly Action<SequenceHierarchyCommand> _command;
        private readonly ObservableCollection<SequenceHierarchyRow> _rows = new ObservableCollection<SequenceHierarchyRow>();
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _results = new Dictionary<string, string>(StringComparer.Ordinal);
        private DataGrid _grid;
        private string _search = string.Empty;
        private bool _debugMode;
        private Point _dragStart;
        private SequenceHierarchyRow _dragRow;

        public event Action<FlowBlockInstance> SelectedInstanceChanged;

        public SequenceHierarchyEditor(Func<FctStudioProject> getProject, Action changed, Action<SequenceHierarchyRow> configure, Func<SequenceHierarchyRow, Task> runFromRow = null, Action<SequenceHierarchyCommand> command = null)
        {
            _getProject = getProject; _changed = changed; _configure = configure; _runFromRow = runFromRow; _command = command; BuildUi();
        }

        public DataGrid Grid { get { return _grid; } }
        public IEnumerable<SequenceHierarchyRow> Rows { get { return _rows; } }

        public void SetSearch(string text) { _search = (text ?? string.Empty).Trim(); Refresh(); }
        public void SetDebugMode(bool enabled) { _debugMode = enabled; foreach (SequenceHierarchyRow row in _rows) row.SetDebugMode(enabled); }

        public void Refresh()
        {
            string selectedKey = (_grid == null ? null : _grid.SelectedItem as SequenceHierarchyRow) == null ? string.Empty : ((SequenceHierarchyRow)_grid.SelectedItem).Key;
            _rows.Clear(); FctStudioProject project = _getProject(); if (project == null) return;
            Dictionary<string, FunctionBlockDefinition> library = (project.Blocks ?? new List<FunctionBlockDefinition>()).Where(value => value != null && !string.IsNullOrWhiteSpace(value.Id)).GroupBy(value => value.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            int flowNumber = 0;
            foreach (FlowBlockInstance instance in project.Flow ?? new List<FlowBlockInstance>())
            {
                flowNumber++; FunctionBlockDefinition block = instance.Snapshot; if (block == null && !string.IsNullOrWhiteSpace(instance.BlockId)) library.TryGetValue(instance.BlockId, out block); if (block == null) continue; if (instance.ModuleSnapshots == null) instance.ModuleSnapshots = new Dictionary<string, FunctionBlockDefinition>(StringComparer.Ordinal); CaptureModuleSnapshots(instance, block, library, false, new HashSet<string>(StringComparer.Ordinal)); Dictionary<string, FunctionBlockDefinition> instanceLibrary = new Dictionary<string, FunctionBlockDefinition>(library, StringComparer.Ordinal); foreach (KeyValuePair<string, FunctionBlockDefinition> pair in instance.ModuleSnapshots) if (pair.Value != null) instanceLibrary[pair.Key] = pair.Value;
                if (!Matches(instance.DisplayName, block.Name) && !ContainsMatchingStep(block, instanceLibrary, new HashSet<string>(StringComparer.Ordinal))) continue;
                string number = flowNumber.ToString("00", CultureInfo.InvariantCulture), key = instance.Id;
                SequenceHierarchyRow module = SequenceHierarchyRow.ForFlow(instance, block, number, key, _expanded.Contains(key), _changed); _rows.Add(module);
                if (module.IsExpanded) AppendBlock(project, instanceLibrary, instance, block, instance.ParameterOverrides, string.Empty, key, number, 1, new HashSet<string>(StringComparer.Ordinal));
            }
            foreach (SequenceHierarchyRow row in _rows) row.SetDebugMode(_debugMode); SequenceHierarchyRow selected = _rows.FirstOrDefault(value => value.Key == selectedKey); if (selected != null) _grid.SelectedItem = selected;
        }

        public void SelectInstance(string instanceId)
        {
            SequenceHierarchyRow row = _rows.FirstOrDefault(value => value.Instance != null && value.Instance.Id == instanceId && value.Depth == 0); if (row == null) return; _grid.SelectedItem = row; _grid.ScrollIntoView(row);
        }

        public void ExpandInstance(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return; _expanded.Add(instanceId); for (int pass = 0; pass < 12; pass++) { Refresh(); List<string> keys = _rows.Where(value => value.Instance != null && value.Instance.Id == instanceId && value.IsModule && value.HasChildren).Select(value => value.Key).ToList(); int before = _expanded.Count; foreach (string key in keys) _expanded.Add(key); if (_expanded.Count == before) break; } Refresh();
        }

        public void UpdateDebugStep(string flowInstanceId, string blockStepId, string status, string result)
        {
            string suffix = "/" + blockStepId; foreach (SequenceHierarchyRow row in _rows.Where(value => value.Instance != null && value.Instance.Id == flowInstanceId && value.Step != null && (value.Path == blockStepId || value.Path.EndsWith(suffix, StringComparison.Ordinal) || value.Step.Id == blockStepId))) { row.Status = status; row.Result = result; if (status == "运行中") { _grid.SelectedItem = row; _grid.ScrollIntoView(row); } }
            _statuses[flowInstanceId + ":" + blockStepId] = status ?? string.Empty; _results[flowInstanceId + ":" + blockStepId] = result ?? string.Empty;
        }

        private void AppendBlock(FctStudioProject project, IDictionary<string, FunctionBlockDefinition> library, FlowBlockInstance instance, FunctionBlockDefinition block, IDictionary<string, object> parameterValues, string hierarchyPath, string parentKey, string parentNumber, int depth, ISet<string> stack)
        {
            if (block == null || !stack.Add(block.Id)) return; int childNumber = 0, logicDepth = 0;
            foreach (BlockStepDefinition step in block.Steps ?? new List<BlockStepDefinition>())
            {
                SequenceStepDefinition displayDefinition = step.IsModuleReference ? null : step.ToStep(); string structureRole = displayDefinition == null ? string.Empty : Convert.ToString(displayDefinition.Get("StructureRole", string.Empty), CultureInfo.InvariantCulture); if (structureRole == "ELSE_BODY") continue; if (structureRole == "ELSE" || structureRole == "ENDIF") logicDepth = Math.Max(0, logicDepth - 1); childNumber++; string number = parentNumber + "." + childNumber.ToString("00", CultureInfo.InvariantCulture); string path = string.IsNullOrWhiteSpace(hierarchyPath) ? step.Id : hierarchyPath + "/" + step.Id; string key = instance.Id + ":" + path; int rowDepth = depth + logicDepth;
                if (step.IsModuleReference)
                {
                    if (instance.StepOverrides == null) instance.StepOverrides = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal); Dictionary<string, object> stepValues; instance.StepOverrides.TryGetValue(path, out stepValues); object referencedIdOverride; string referencedId = stepValues != null && stepValues.TryGetValue("__ReferencedBlockId", out referencedIdOverride) ? Convert.ToString(referencedIdOverride, CultureInfo.InvariantCulture) : step.ReferencedBlockId; FunctionBlockDefinition child; library.TryGetValue(referencedId ?? string.Empty, out child); if (instance.ReferenceParameterOverrides == null) instance.ReferenceParameterOverrides = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal); Dictionary<string, object> referenceValues; if (!instance.ReferenceParameterOverrides.TryGetValue(path, out referenceValues)) { referenceValues = new Dictionary<string, object>(step.ReferencedParameterOverrides ?? new Dictionary<string, object>(), StringComparer.Ordinal); instance.ReferenceParameterOverrides[path] = referenceValues; } SequenceHierarchyRow module = SequenceHierarchyRow.ForReference(project, instance, block, step, child, number, path, key, rowDepth, _expanded.Contains(key), _changed); _rows.Add(module); if (module.IsExpanded && child != null) AppendBlock(project, library, instance, child, referenceValues, path, key, number, rowDepth + 1, new HashSet<string>(stack, StringComparer.Ordinal)); continue;
                }
                SequenceHierarchyRow row = SequenceHierarchyRow.ForStep(project, instance, block, step, parameterValues, number, path, key, rowDepth, _changed); string runtimeKey = instance.Id + ":" + path, legacyRuntimeKey = instance.Id + ":" + step.Id; string value; if (_statuses.TryGetValue(runtimeKey, out value) || _statuses.TryGetValue(legacyRuntimeKey, out value)) row.Status = value; if (_results.TryGetValue(runtimeKey, out value) || _results.TryGetValue(legacyRuntimeKey, out value)) row.Result = value; _rows.Add(row); if (structureRole == "IF" || structureRole == "ELSE") logicDepth++;
            }
            stack.Remove(block.Id);
        }

        private bool ContainsMatchingStep(FunctionBlockDefinition block, IDictionary<string, FunctionBlockDefinition> library, ISet<string> stack)
        {
            if (block == null || !stack.Add(block.Id)) return false; foreach (BlockStepDefinition step in block.Steps ?? new List<BlockStepDefinition>()) { if (Matches(step.ReferencedBlockName, step.IsModuleReference ? string.Empty : step.ToStep().StepName)) return true; if (step.IsModuleReference) { FunctionBlockDefinition child; if (library.TryGetValue(step.ReferencedBlockId ?? string.Empty, out child) && ContainsMatchingStep(child, library, stack)) return true; } } return false;
        }
        private static void CaptureModuleSnapshots(FlowBlockInstance instance, FunctionBlockDefinition block, IDictionary<string, FunctionBlockDefinition> library, bool overwrite, ISet<string> visited)
        {
            if (instance == null || block == null || !visited.Add(block.Id)) return; foreach (BlockStepDefinition step in block.Steps ?? new List<BlockStepDefinition>()) if (step.IsModuleReference) { FunctionBlockDefinition child; if (!library.TryGetValue(step.ReferencedBlockId ?? string.Empty, out child) || child == null) continue; if (overwrite || !instance.ModuleSnapshots.ContainsKey(child.Id)) instance.ModuleSnapshots[child.Id] = child.Clone(); FunctionBlockDefinition captured = instance.ModuleSnapshots[child.Id] ?? child; CaptureModuleSnapshots(instance, captured, library, overwrite, visited); }
        }
        private bool Matches(params string[] values) { return string.IsNullOrWhiteSpace(_search) || values.Any(value => (value ?? string.Empty).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0); }

        private void BuildUi()
        {
            _grid = new DataGrid { ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Brush(226, 233, 242), VerticalGridLinesBrush = Brushes.Transparent, Background = Brushes.White, BorderThickness = new Thickness(0), RowHeaderWidth = 0, ColumnHeaderHeight = 40, RowHeight = double.NaN, MinRowHeight = 34, SelectionUnit = DataGridSelectionUnit.FullRow, SelectionMode = DataGridSelectionMode.Single, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, RowStyle = RowStyle(), ColumnHeaderStyle = HeaderStyle(), CellStyle = CellStyle(), AllowDrop = true };
            _grid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new Binding("Number"), Width = 88, IsReadOnly = true, ElementStyle = CenterText() });
            _grid.Columns.Add(NameColumn());
            _grid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("TypeText"), Width = 95, IsReadOnly = true, ElementStyle = CenterText() });
            _grid.Columns.Add(ValueColumn());
            _grid.Columns.Add(new DataGridTextColumn { Header = "下限", Binding = new Binding("LowLimitText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 78, ElementStyle = CenterText(), EditingElementStyle = EditorStyle() });
            _grid.Columns.Add(new DataGridTextColumn { Header = "上限", Binding = new Binding("HighLimitText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 78, ElementStyle = CenterText(), EditingElementStyle = EditorStyle() });
            _grid.Columns.Add(new DataGridComboBoxColumn { Header = "比较", SelectedItemBinding = new Binding("CompareText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { string.Empty, "GELE", "GE", "GT", "LE", "LT", "EQ", "NE" }, Width = 82, ElementStyle = CenterCombo(), EditingElementStyle = CenterCombo() });
            _grid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("UnitText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 64, ElementStyle = CenterText(), EditingElementStyle = EditorStyle() });
            _grid.Columns.Add(BreakpointColumn());
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new Binding("Enabled") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 58, ElementStyle = CenterCheck(), EditingElementStyle = CenterCheck() });
            _grid.Columns.Add(new DataGridTextColumn { Header = "测试值", Binding = new Binding("TestValue"), Width = 105, IsReadOnly = true, ElementStyle = CenterText() });
            _grid.Columns.Add(new DataGridTextColumn { Header = "结果", Binding = new Binding("DebugResult"), Width = 78, IsReadOnly = true, ElementStyle = CenterText() });
            _grid.Columns.Add(OperationColumn());
            _grid.SelectionChanged += delegate { SequenceHierarchyRow row = _grid.SelectedItem as SequenceHierarchyRow; if (row != null && row.Instance != null) { Action<FlowBlockInstance> handler = SelectedInstanceChanged; if (handler != null) handler(row.Instance); } };
            _grid.MouseDoubleClick += delegate { SequenceHierarchyRow row = _grid.SelectedItem as SequenceHierarchyRow; if (row != null && row.HasChildren) Toggle(row); };
            _grid.PreviewMouseLeftButtonDown += Grid_MouseLeftButtonDown; _grid.PreviewMouseLeftButtonUp += delegate { _dragRow = null; }; _grid.PreviewMouseMove += Grid_MouseMove; _grid.DragOver += Grid_DragOver; _grid.Drop += Grid_Drop; _grid.PreviewMouseRightButtonDown += Grid_RightButtonDown;
            Children.Add(_grid);
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(_grid); DataGridRow row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject); _dragRow = row == null ? null : row.Item as SequenceHierarchyRow; }
        private void Grid_MouseMove(object sender, MouseEventArgs e) { if (_dragRow == null || e.LeftButton != MouseButtonState.Pressed || _dragRow.Depth != 0) return; Point point = e.GetPosition(_grid); if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return; SequenceHierarchyRow moving = _dragRow; _dragRow = null; DragDrop.DoDragDrop(_grid, new DataObject(typeof(SequenceHierarchyRow), moving), DragDropEffects.Move); }
        private void Grid_DragOver(object sender, DragEventArgs e) { bool accepted = e.Data.GetDataPresent(typeof(SequenceHierarchyRow)) || e.Data.GetDataPresent(typeof(FlowLibraryRow)); e.Effects = accepted ? (e.Data.GetDataPresent(typeof(SequenceHierarchyRow)) ? DragDropEffects.Move : DragDropEffects.Copy) : DragDropEffects.None; e.Handled = true; }
        private void Grid_Drop(object sender, DragEventArgs e)
        {
            FctStudioProject project = _getProject(); if (project == null) return; DataGridRow targetVisual = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject); SequenceHierarchyRow target = targetVisual == null ? null : targetVisual.Item as SequenceHierarchyRow; int targetIndex = target == null || target.Instance == null ? project.Flow.Count : project.Flow.IndexOf(target.Instance); SequenceHierarchyRow moving = e.Data.GetData(typeof(SequenceHierarchyRow)) as SequenceHierarchyRow; if (moving != null && moving.Depth == 0 && moving.Instance != null) { int old = project.Flow.IndexOf(moving.Instance); if (old >= 0) { if (old < targetIndex) targetIndex--; project.Flow.RemoveAt(old); project.Flow.Insert(Math.Max(0, Math.Min(project.Flow.Count, targetIndex)), moving.Instance); NotifyChanged(); Refresh(); } e.Handled = true; return; } FlowLibraryRow library = e.Data.GetData(typeof(FlowLibraryRow)) as FlowLibraryRow; if (library != null && library.Block != null) { FunctionBlockDefinition snapshot = library.Block.Clone(); FlowBlockInstance instance = new FlowBlockInstance { BlockId = library.Block.Id, DisplayName = library.Block.Name, Snapshot = snapshot, Phase = string.IsNullOrWhiteSpace(library.Block.Category) ? "准备阶段" : library.Block.Category }; foreach (BlockParameterDefinition parameter in snapshot.Parameters ?? new List<BlockParameterDefinition>()) instance.ParameterOverrides[parameter.Name] = parameter.DefaultValue; Dictionary<string, FunctionBlockDefinition> sourceLibrary = project.Blocks.Where(value => value != null).GroupBy(value => value.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal); CaptureModuleSnapshots(instance, snapshot, sourceLibrary, false, new HashSet<string>(StringComparer.Ordinal)); project.Flow.Insert(Math.Max(0, Math.Min(project.Flow.Count, targetIndex)), instance); NotifyChanged(); Refresh(); e.Handled = true; }
        }
        private void Grid_RightButtonDown(object sender, MouseButtonEventArgs e) { DataGridRow visual = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject); SequenceHierarchyRow row = visual == null ? null : visual.Item as SequenceHierarchyRow; if (row == null) return; _grid.SelectedItem = row; ContextMenu menu = new ContextMenu(); if (row.IsDebugMode && _runFromRow != null) { AddMenu(menu, row.IsModule ? "执行整个模块" : "执行STEP", async (s, a) => await _runFromRow(row)); menu.Items.Add(new Separator()); } if (row.Depth == 0) { menu.Items.Add(BuildHierarchicalActionMenu("添加STEP到此模块", row, 0, true)); AddCommand(menu, "添加IF / ELSE / ENDIF", "append_if", row, 0); AddCommand(menu, "添加子模块到此模块...", "append_module", row, 0); AddCommand(menu, "新建子模块并添加...", "new_custom_child_module", row, 0); menu.Items.Add(new Separator()); AddCommand(menu, "插入模块到上面...", "insert_flow_module", row, 0); AddCommand(menu, "插入模块到下面...", "insert_flow_module", row, 1); menu.Items.Add(new Separator()); AddMenu(menu, "复制流程实例", (s, a) => DuplicateInstance(row)); AddMenu(menu, "更新到模块库最新版本", (s, a) => UpdateInstanceFromLibrary(row)); AddMenu(menu, "上移", (s, a) => MoveInstance(row, -1)); AddMenu(menu, "下移", (s, a) => MoveInstance(row, 1)); AddMenu(menu, row.Enabled ? "停用" : "启用", (s, a) => row.Enabled = !row.Enabled); AddMenu(menu, "删除流程实例", (s, a) => DeleteInstance(row)); } else { if (row.Step != null && row.Step.IsModuleReference) { menu.Items.Add(BuildHierarchicalActionMenu("添加STEP到此模块", row, 0, true)); AddCommand(menu, "添加IF / ELSE / ENDIF", "append_if", row, 0); AddCommand(menu, "添加子模块到此模块...", "append_module", row, 0); AddCommand(menu, "新建子模块并添加...", "new_custom_child_module", row, 0); menu.Items.Add(new Separator()); } menu.Items.Add(BuildHierarchicalActionMenu("插入动作到上面", row, 0)); menu.Items.Add(BuildHierarchicalActionMenu("插入动作到下面", row, 1)); AddCommand(menu, "插入IF / ELSE / ENDIF到下面", "insert_if", row, 1); AddCommand(menu, "插入模块到上面...", "insert_module", row, 0); AddCommand(menu, "插入模块到下面...", "insert_module", row, 1); menu.Items.Add(new Separator()); AddCommand(menu, "复制动作/模块引用", "copy_step", row, 0); AddCommand(menu, "粘贴到下面", "paste_step", row, 1); AddMenu(menu, "删除动作/模块引用", (s, a) => DeleteStep(row)); AddMenu(menu, "上移", (s, a) => MoveStep(row, -1)); AddMenu(menu, "下移", (s, a) => MoveStep(row, 1)); menu.Items.Add(new Separator()); if (row.Step != null) AddMenu(menu, row.Step.IsModuleReference ? "配置模块绑定" : "配置执行项", (s, a) => { if (_configure != null) _configure(row); }); if (row.BreakpointVisibility == Visibility.Visible) AddMenu(menu, row.Breakpoint ? "取消断点" : "设置断点", (s, a) => row.Breakpoint = !row.Breakpoint); AddMenu(menu, row.Enabled ? "停用" : "启用", (s, a) => row.Enabled = !row.Enabled); } _grid.ContextMenu = menu; }
        internal MenuItem BuildHierarchicalActionMenu(string header, SequenceHierarchyRow row, int relativeOffset, bool appendToModule = false)
        {
            MenuItem root = new MenuItem { Header = header };
            foreach (string source in new[] { "仪器", "产品内部通信", "产品DBC通信", "流程逻辑" })
            {
                MenuItem sourceMenu = new MenuItem { Header = source };
                IEnumerable<ActionDescriptor> descriptors = ActionCatalog.PickerDescriptors(source);
                foreach (IGrouping<string, ActionDescriptor> group in descriptors.GroupBy(ActionCatalog.PickerTarget).OrderBy(value => value.Key, StringComparer.CurrentCulture))
                {
                    MenuItem targetMenu = new MenuItem { Header = group.Key };
                    foreach (ActionDescriptor descriptor in group.OrderBy(value => value.DisplayName, StringComparer.CurrentCulture))
                    {
                        ActionDescriptor captured = descriptor; MenuItem leaf = new MenuItem { Header = descriptor.DisplayName };
                        leaf.Click += (s, e) => DispatchCommand(new SequenceHierarchyCommand(appendToModule ? "append_descriptor" : "insert_descriptor", row, relativeOffset, captured)); targetMenu.Items.Add(leaf);
                    }
                    sourceMenu.Items.Add(targetMenu);
                }
                if (source == "产品内部通信") sourceMenu.Items.Add(BuildShortcutBranch("FT/Locator内存", new[] { "读取", "写入" }, source, "FT/Locator内存", row, relativeOffset, appendToModule));
                if (source == "产品DBC通信") sourceMenu.Items.Add(BuildShortcutBranch("辅驱 / DCDC / PDU", new[] { "发送一次", "开始周期发送", "停止周期发送", "读取DBC信号", "发送原始帧" }, source, "辅驱/DCDC/PDU DBC", row, relativeOffset, appendToModule));
                if (sourceMenu.Items.Count > 0) root.Items.Add(sourceMenu);
            }
            MenuItem legacy = new MenuItem { Header = "原平台MAINTEST" }; MenuItem selectLegacy = new MenuItem { Header = "从测试项库选择..." }; selectLegacy.Click += (s, e) => DispatchCommand(new SequenceHierarchyCommand(appendToModule ? "append_action" : "insert_action", row, relativeOffset)); legacy.Items.Add(selectLegacy); root.Items.Add(legacy); return root;
        }
        private MenuItem BuildShortcutBranch(string header, IEnumerable<string> operations, string source, string target, SequenceHierarchyRow row, int relativeOffset, bool appendToModule) { MenuItem branch = new MenuItem { Header = header }; foreach (string operation in operations) { string captured = operation; MenuItem leaf = new MenuItem { Header = operation }; leaf.Click += (s, e) => DispatchCommand(new SequenceHierarchyCommand(appendToModule ? "append_shortcut" : "insert_shortcut", row, relativeOffset, source, target, captured)); branch.Items.Add(leaf); } return branch; }
        private void DispatchCommand(SequenceHierarchyCommand command) { if (_command != null) _command(command); }
        private void AddCommand(ContextMenu menu, string header, string command, SequenceHierarchyRow row, int relativeOffset) { if (_command == null) return; AddMenu(menu, header, (s, e) => _command(new SequenceHierarchyCommand(command, row, relativeOffset))); }
        private static void AddMenu(ContextMenu menu, string header, RoutedEventHandler handler) { MenuItem item = new MenuItem { Header = header }; item.Click += handler; menu.Items.Add(item); }
        private void DuplicateInstance(SequenceHierarchyRow row) { FctStudioProject project = _getProject(); if (project == null || row.Instance == null) return; FlowBlockInstance source = row.Instance; FlowBlockInstance copy = new FlowBlockInstance { BlockId = source.BlockId, DisplayName = source.DisplayName + " - 副本", Phase = source.Phase, Enabled = source.Enabled, PreserveStepNames = source.PreserveStepNames, Snapshot = source.Snapshot == null ? null : source.Snapshot.Clone(), ParameterOverrides = new Dictionary<string, object>(source.ParameterOverrides ?? new Dictionary<string, object>(), StringComparer.Ordinal), StepOverrides = CloneNested(source.StepOverrides), ReferenceParameterOverrides = CloneNested(source.ReferenceParameterOverrides), ModuleSnapshots = (source.ModuleSnapshots ?? new Dictionary<string, FunctionBlockDefinition>()).ToDictionary(pair => pair.Key, pair => pair.Value == null ? null : pair.Value.Clone(), StringComparer.Ordinal) }; int index = project.Flow.IndexOf(source) + 1; project.Flow.Insert(index, copy); NotifyChanged(); Refresh(); }
        private static Dictionary<string, Dictionary<string, object>> CloneNested(IDictionary<string, Dictionary<string, object>> source) { return (source ?? new Dictionary<string, Dictionary<string, object>>()).ToDictionary(pair => pair.Key, pair => new Dictionary<string, object>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal); }
        private void MoveInstance(SequenceHierarchyRow row, int offset) { FctStudioProject project = _getProject(); if (project == null || row.Instance == null) return; int old = project.Flow.IndexOf(row.Instance), next = old + offset; if (old < 0 || next < 0 || next >= project.Flow.Count) return; project.Flow.RemoveAt(old); project.Flow.Insert(next, row.Instance); NotifyChanged(); Refresh(); }
        private void DeleteInstance(SequenceHierarchyRow row) { FctStudioProject project = _getProject(); if (project == null || row.Instance == null) return; project.Flow.Remove(row.Instance); NotifyChanged(); Refresh(); }
        private void DeleteStep(SequenceHierarchyRow row) { if (row == null || row.Step == null || row.Block == null) return; SequenceStepDefinition definition = row.Step.IsModuleReference ? null : row.Step.ToStep(); string role = definition == null ? string.Empty : Convert.ToString(definition.Get("StructureRole", string.Empty), CultureInfo.InvariantCulture), structureId = definition == null ? string.Empty : Convert.ToString(definition.Get("StructureId", string.Empty), CultureInfo.InvariantCulture); if (role == "IF") { if (MessageBox.Show("删除整个 IF / ELSE / ENDIF 结构及其中的动作？", "删除IF", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; row.Block.Steps.RemoveAll(step => !step.IsModuleReference && Convert.ToString(step.ToStep().Get("StructureId", string.Empty), CultureInfo.InvariantCulture) == structureId); } else if (!string.IsNullOrWhiteSpace(role)) { MessageBox.Show("ELSE和ENDIF由IF结构自动管理，请从IF行删除整个结构。", "逻辑结构", MessageBoxButton.OK, MessageBoxImage.Information); return; } else row.Block.Steps.Remove(row.Step); NotifyChanged(); Refresh(); }
        private void MoveStep(SequenceHierarchyRow row, int offset) { if (row == null || row.Step == null || row.Block == null) return; if (!row.Step.IsModuleReference && !string.IsNullOrWhiteSpace(Convert.ToString(row.Step.ToStep().Get("StructureRole", string.Empty), CultureInfo.InvariantCulture))) { MessageBox.Show("IF、ELSE和ENDIF必须作为一个整体移动。", "逻辑结构", MessageBoxButton.OK, MessageBoxImage.Information); return; } int old = row.Block.Steps.IndexOf(row.Step), next = old + offset; if (old < 0 || next < 0 || next >= row.Block.Steps.Count) return; row.Block.Steps.RemoveAt(old); row.Block.Steps.Insert(next, row.Step); NotifyChanged(); Refresh(); }
        private void UpdateInstanceFromLibrary(SequenceHierarchyRow row) { FctStudioProject project = _getProject(); if (project == null || row.Instance == null) return; FunctionBlockDefinition source = project.Blocks.FirstOrDefault(value => value.Id == row.Instance.BlockId); if (source == null) return; if (MessageBox.Show("将当前SEQ实例更新为模块库“" + source.Name + "”的最新版本？\n\n实例显示名称和兼容参数会保留，新增或删除的STEP将按最新模块更新。", "更新模块实例", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; Dictionary<string, object> oldParameters = new Dictionary<string, object>(row.Instance.ParameterOverrides ?? new Dictionary<string, object>(), StringComparer.Ordinal); row.Instance.Snapshot = source.Clone(); row.Instance.ParameterOverrides.Clear(); foreach (BlockParameterDefinition parameter in source.Parameters ?? new List<BlockParameterDefinition>()) { object value; row.Instance.ParameterOverrides[parameter.Name] = oldParameters.TryGetValue(parameter.Name, out value) ? value : parameter.DefaultValue; } row.Instance.ModuleSnapshots.Clear(); Dictionary<string, FunctionBlockDefinition> library = project.Blocks.Where(value => value != null).GroupBy(value => value.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal); CaptureModuleSnapshots(row.Instance, source, library, true, new HashSet<string>(StringComparer.Ordinal)); NotifyChanged(); Refresh(); }
        private void NotifyChanged() { if (_changed != null) _changed(); }
        private static T FindAncestor<T>(DependencyObject value) where T : DependencyObject { while (value != null && !(value is T)) value = VisualTreeHelper.GetParent(value); return value as T; }

        private DataGridTemplateColumn NameColumn()
        {
            FrameworkElementFactory host = new FrameworkElementFactory(typeof(Grid)); host.SetBinding(FrameworkElement.MarginProperty, new Binding("Indent")); host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); FrameworkElementFactory columns = new FrameworkElementFactory(typeof(StackPanel)); columns.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); columns.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
            FrameworkElementFactory toggle = new FrameworkElementFactory(typeof(Button)); toggle.SetValue(Button.WidthProperty, 24d); toggle.SetValue(Button.HeightProperty, 26d); toggle.SetValue(Button.PaddingProperty, new Thickness(0)); toggle.SetValue(Button.MarginProperty, new Thickness(0, 0, 5, 0)); toggle.SetValue(Button.BackgroundProperty, Brushes.Transparent); toggle.SetValue(Button.BorderBrushProperty, Brushes.Transparent); toggle.SetBinding(Button.ContentProperty, new Binding("Chevron")); toggle.SetBinding(Button.VisibilityProperty, new Binding("ExpandVisibility")); toggle.AddHandler(Button.ClickEvent, new RoutedEventHandler(Toggle_Click)); columns.AppendChild(toggle);
            FrameworkElementFactory icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetBinding(TextBlock.TextProperty, new Binding("IconGlyph")); icon.SetBinding(TextBlock.ForegroundProperty, new Binding("IconBrush")); icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); icon.SetValue(TextBlock.FontSizeProperty, 16d); icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0)); icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); columns.AppendChild(icon);
            FrameworkElementFactory textStack = new FrameworkElementFactory(typeof(StackPanel)); textStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical); textStack.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center); FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBox)); name.SetBinding(TextBox.TextProperty, new Binding("NameText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); name.SetBinding(TextBox.IsReadOnlyProperty, new Binding("NameReadOnly")); name.SetValue(TextBox.BackgroundProperty, Brushes.Transparent); name.SetValue(TextBox.BorderThicknessProperty, new Thickness(0)); name.SetValue(TextBox.PaddingProperty, new Thickness(0)); name.SetValue(TextBox.FontSizeProperty, 13d); name.SetValue(TextBox.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center); textStack.AppendChild(name); FrameworkElementFactory detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding("BindingText")); detail.SetBinding(TextBlock.VisibilityProperty, new Binding("BindingVisibility")); detail.SetValue(TextBlock.FontSizeProperty, 10.5d); detail.SetValue(TextBlock.ForegroundProperty, Brush(101, 116, 137)); detail.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0)); textStack.AppendChild(detail); columns.AppendChild(textStack);
            FrameworkElementFactory badge = new FrameworkElementFactory(typeof(Border)); badge.SetBinding(Border.VisibilityProperty, new Binding("BadgeVisibility")); badge.SetValue(Border.BackgroundProperty, Brush(239, 243, 248)); badge.SetValue(Border.BorderBrushProperty, Brush(211, 220, 232)); badge.SetValue(Border.BorderThicknessProperty, new Thickness(1)); badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); badge.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2)); badge.SetValue(Border.MarginProperty, new Thickness(8, 0, 0, 0)); FrameworkElementFactory badgeText = new FrameworkElementFactory(typeof(TextBlock)); badgeText.SetBinding(TextBlock.TextProperty, new Binding("BadgeText")); badgeText.SetValue(TextBlock.FontSizeProperty, 10d); badgeText.SetValue(TextBlock.ForegroundProperty, Brush(83, 99, 121)); badge.AppendChild(badgeText); columns.AppendChild(badge); host.AppendChild(columns); return new DataGridTemplateColumn { Header = "功能块与执行项", Width = new DataGridLength(2.3, DataGridLengthUnitType.Star), MinWidth = 340, CellTemplate = new DataTemplate { VisualTree = host }, IsReadOnly = true };
        }

        private DataGridTemplateColumn ValueColumn()
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            panel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory fields = new FrameworkElementFactory(typeof(ItemsControl));
            fields.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("ValueFields"));
            FrameworkElementFactory wrap = new FrameworkElementFactory(typeof(WrapPanel));
            wrap.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
            fields.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(wrap));

            FrameworkElementFactory fieldBorder = new FrameworkElementFactory(typeof(Border));
            fieldBorder.SetValue(Border.MarginProperty, new Thickness(2, 0, 2, 0));
            FrameworkElementFactory fieldPanel = new FrameworkElementFactory(typeof(StackPanel));
            fieldPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            fieldPanel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory fieldBox = new FrameworkElementFactory(typeof(TextBox));
            fieldBox.SetBinding(TextBox.TextProperty, new Binding("ValueText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            fieldBox.SetBinding(TextBox.IsReadOnlyProperty, new Binding("IsReadOnly"));
            fieldBox.SetBinding(TextBox.ToolTipProperty, new Binding("DisplayName"));
            fieldBox.SetValue(TextBox.WidthProperty, 58d);
            fieldBox.SetValue(TextBox.HeightProperty, 26d);
            fieldBox.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            fieldBox.SetValue(TextBox.StyleProperty, InlineValueEditorStyle());
            Style textVisibility = new Style(typeof(TextBox), InlineValueEditorStyle());
            DataTrigger hideText = new DataTrigger { Binding = new Binding("IsOnOff"), Value = true };
            hideText.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            textVisibility.Triggers.Add(hideText);
            fieldBox.SetValue(FrameworkElement.StyleProperty, textVisibility);
            fieldPanel.AppendChild(fieldBox);

            FrameworkElementFactory onOff = new FrameworkElementFactory(typeof(ComboBox));
            onOff.SetValue(ComboBox.ItemsSourceProperty, new[] { "OFF", "ON" });
            onOff.SetBinding(ComboBox.SelectedItemProperty, new Binding("ValueText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            onOff.SetBinding(ComboBox.ToolTipProperty, new Binding("DisplayName"));
            onOff.SetValue(ComboBox.WidthProperty, 72d);
            onOff.SetValue(ComboBox.HeightProperty, 26d);
            onOff.SetValue(ComboBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
            onOff.SetValue(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            onOff.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            Style onOffStyle = new Style(typeof(ComboBox));
            onOffStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            onOffStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            onOffStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            onOffStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(205, 216, 231)));
            onOffStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            DataTrigger showOnOff = new DataTrigger { Binding = new Binding("IsOnOff"), Value = true };
            showOnOff.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            onOffStyle.Triggers.Add(showOnOff);
            DataTrigger onColor = new DataTrigger { Binding = new Binding("ValueText"), Value = "ON" };
            onColor.Setters.Add(new Setter(Control.ForegroundProperty, Brush(24, 128, 72)));
            onColor.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            onOffStyle.Triggers.Add(onColor);
            DataTrigger offColor = new DataTrigger { Binding = new Binding("ValueText"), Value = "OFF" };
            offColor.Setters.Add(new Setter(Control.ForegroundProperty, Brush(140, 90, 40)));
            onOffStyle.Triggers.Add(offColor);
            onOff.SetValue(FrameworkElement.StyleProperty, onOffStyle);
            fieldPanel.AppendChild(onOff);

            FrameworkElementFactory unit = new FrameworkElementFactory(typeof(TextBlock));
            unit.SetBinding(TextBlock.TextProperty, new Binding("Unit"));
            unit.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 0, 0));
            unit.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            Style unitStyle = new Style(typeof(TextBlock));
            DataTrigger hideUnit = new DataTrigger { Binding = new Binding("IsOnOff"), Value = true };
            hideUnit.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            unitStyle.Triggers.Add(hideUnit);
            unit.SetValue(FrameworkElement.StyleProperty, unitStyle);
            fieldPanel.AppendChild(unit);

            fieldBorder.AppendChild(fieldPanel);
            fields.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = fieldBorder });
            panel.AppendChild(fields);

            FrameworkElementFactory summary = new FrameworkElementFactory(typeof(TextBlock));
            summary.SetBinding(TextBlock.TextProperty, new Binding("ValueSummary"));
            summary.SetBinding(TextBlock.VisibilityProperty, new Binding("SummaryVisibility"));
            summary.SetValue(TextBlock.ForegroundProperty, Brush(74, 102, 143));
            summary.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(summary);

            return new DataGridTemplateColumn { Header = "当前SEQ值", Width = new DataGridLength(1.6, DataGridLengthUnitType.Star), MinWidth = 280, CellTemplate = new DataTemplate { VisualTree = panel }, IsReadOnly = true };
        }

        private DataGridTemplateColumn BreakpointColumn()
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button)); button.SetValue(Button.WidthProperty, 34d); button.SetValue(Button.HeightProperty, 30d); button.SetValue(Button.BackgroundProperty, Brushes.Transparent); button.SetValue(Button.BorderBrushProperty, Brushes.Transparent); button.SetBinding(Button.ContentProperty, new Binding("BreakpointGlyph")); button.SetBinding(Button.ForegroundProperty, new Binding("BreakpointBrush")); button.SetBinding(Button.VisibilityProperty, new Binding("BreakpointVisibility")); button.AddHandler(Button.ClickEvent, new RoutedEventHandler(Breakpoint_Click)); return new DataGridTemplateColumn { Header = "断点", Width = 56, CellTemplate = new DataTemplate { VisualTree = button }, IsReadOnly = true };
        }

        private DataGridTemplateColumn OperationColumn()
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button)); button.SetBinding(Button.ContentProperty, new Binding("OperationText")); button.SetBinding(Button.VisibilityProperty, new Binding("OperationVisibility")); button.SetValue(Button.MinWidthProperty, 62d); button.SetValue(Button.HeightProperty, 28d); button.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Center); button.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center); button.SetValue(Button.PaddingProperty, new Thickness(8, 3, 8, 3)); button.SetValue(Button.BackgroundProperty, Brushes.White); button.SetValue(Button.ForegroundProperty, Brush(24, 112, 224)); button.SetValue(Button.BorderBrushProperty, Brush(151, 184, 232)); button.SetValue(Button.BorderThicknessProperty, new Thickness(1)); button.AddHandler(Button.ClickEvent, new RoutedEventHandler(Configure_Click)); return new DataGridTemplateColumn { Header = "操作", Width = 78, CellTemplate = new DataTemplate { VisualTree = button }, IsReadOnly = true };
        }

        private void Toggle_Click(object sender, RoutedEventArgs e) { SequenceHierarchyRow row = (sender as FrameworkElement) == null ? null : ((FrameworkElement)sender).DataContext as SequenceHierarchyRow; Toggle(row); e.Handled = true; }
        private void Toggle(SequenceHierarchyRow row) { if (row == null || !row.HasChildren) return; if (_expanded.Contains(row.Key)) _expanded.Remove(row.Key); else _expanded.Add(row.Key); Refresh(); SequenceHierarchyRow next = _rows.FirstOrDefault(value => value.Key == row.Key); if (next != null) { _grid.SelectedItem = next; _grid.ScrollIntoView(next); } }
        private void Breakpoint_Click(object sender, RoutedEventArgs e) { SequenceHierarchyRow row = (sender as FrameworkElement) == null ? null : ((FrameworkElement)sender).DataContext as SequenceHierarchyRow; if (row != null) row.Breakpoint = !row.Breakpoint; e.Handled = true; }
        private void Configure_Click(object sender, RoutedEventArgs e) { SequenceHierarchyRow row = (sender as FrameworkElement) == null ? null : ((FrameworkElement)sender).DataContext as SequenceHierarchyRow; if (row != null && _configure != null) _configure(row); e.Handled = true; }

        private static Style HeaderStyle() { Style style = new Style(typeof(DataGridColumnHeader)); style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(247, 249, 252))); style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(62, 76, 96))); style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)); style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(221, 229, 239))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1))); return style; }
        private static Style CellStyle() { Style style = new Style(typeof(DataGridCell)); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); return style; }
        private static Style RowStyle() { Style style = new Style(typeof(DataGridRow)); style.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.White)); style.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 34d)); DataTrigger module = new DataTrigger { Binding = new Binding("IsModule"), Value = true }; module.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brush(248, 250, 253))); module.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 40d)); style.Triggers.Add(module); DataTrigger disabled = new DataTrigger { Binding = new Binding("Enabled"), Value = false }; disabled.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brush(239, 242, 246))); disabled.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Brush(130, 142, 157))); style.Triggers.Add(disabled); Trigger selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brush(231, 240, 255))); selected.Setters.Add(new Setter(DataGridRow.BorderBrushProperty, Brush(24, 112, 224))); selected.Setters.Add(new Setter(DataGridRow.BorderThicknessProperty, new Thickness(3, 0, 0, 0))); style.Triggers.Add(selected); return style; }
        private static Style CenterText() { Style style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)); return style; }
        private static Style EditorStyle() { Style style = new Style(typeof(TextBox)); style.Setters.Add(new Setter(TextBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)); style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(4, 2, 4, 2))); return style; }
        private static Style CenterCombo() { Style style = new Style(typeof(ComboBox)); style.Setters.Add(new Setter(ComboBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)); style.Setters.Add(new Setter(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center)); return style; }
        private static Style CenterCheck() { Style style = new Style(typeof(CheckBox)); style.Setters.Add(new Setter(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center)); style.Setters.Add(new Setter(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center)); return style; }
        private static Style InlineValueEditorStyle() { Style style = new Style(typeof(TextBox)); style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(6, 3, 6, 3))); style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(TextBox.BackgroundProperty, Brushes.White)); style.Setters.Add(new Setter(TextBox.BorderBrushProperty, Brush(205, 216, 231))); style.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(1))); DataTrigger invalid = new DataTrigger { Binding = new Binding("IsValid"), Value = false }; invalid.Setters.Add(new Setter(TextBox.BorderBrushProperty, Brush(210, 51, 51))); invalid.Setters.Add(new Setter(TextBox.BackgroundProperty, Brush(255, 240, 240))); style.Triggers.Add(invalid); Trigger readOnly = new Trigger { Property = TextBox.IsReadOnlyProperty, Value = true }; readOnly.Setters.Add(new Setter(TextBox.BackgroundProperty, Brush(242, 245, 249))); readOnly.Setters.Add(new Setter(TextBox.BorderBrushProperty, Brush(226, 232, 240))); style.Triggers.Add(readOnly); return style; }
        internal static SolidColorBrush Brush(byte r, byte g, byte b) { SolidColorBrush value = new SolidColorBrush(Color.FromRgb(r, g, b)); value.Freeze(); return value; }
    }

    internal sealed class SequenceHierarchyRow : INotifyPropertyChanged
    {
        private readonly Action _changed; private readonly FctStudioProject _project; private IDictionary<string, object> _parameterValues; private string _status = "已启用", _result = string.Empty;
        private SequenceHierarchyRow(FctStudioProject project, FlowBlockInstance instance, FunctionBlockDefinition block, BlockStepDefinition step, string number, string path, string key, int depth, Action changed) { _project = project; Instance = instance; Block = block; Step = step; Number = number; Path = path; Key = key; Depth = depth; _changed = changed; ValueFields = new ObservableCollection<HierarchyValueField>(); }
        public static SequenceHierarchyRow ForFlow(FlowBlockInstance instance, FunctionBlockDefinition block, string number, string key, bool expanded, Action changed) { SequenceHierarchyRow row = new SequenceHierarchyRow(null, instance, block, null, number, string.Empty, key, 0, changed) { IsModule = true, HasChildren = true, IsExpanded = expanded, TypeText = "模块", IconGlyph = "\uE8F1", IconBrush = SequenceHierarchyEditor.Brush(24, 112, 224), BindingText = "绑定：" + block.Name, BadgeText = string.Empty, ValueSummary = (block.Steps == null ? 0 : block.Steps.Count).ToString(CultureInfo.InvariantCulture) }; return row; }
        public static SequenceHierarchyRow ForReference(FctStudioProject project, FlowBlockInstance instance, FunctionBlockDefinition owner, BlockStepDefinition step, FunctionBlockDefinition child, string number, string path, string key, int depth, bool expanded, Action changed) { string kind = child == null ? string.Empty : child.ModuleKind; SequenceHierarchyRow row = new SequenceHierarchyRow(project, instance, owner, step, number, path, key, depth, changed) { IsModule = true, HasChildren = child != null && child.Steps.Count > 0, IsExpanded = expanded, ReferencedBlock = child, TypeText = "模块引用", IconGlyph = "\uE8F1", IconBrush = SequenceHierarchyEditor.Brush(96, 86, 210), BindingText = "绑定：" + (child == null ? step.ReferencedBlockId : child.Name), BadgeText = string.Equals(kind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品模块" : string.Equals(kind, "Custom", StringComparison.OrdinalIgnoreCase) ? "自定义模块" : string.Empty, ValueSummary = child == null || child.Steps == null ? "0" : child.Steps.Count.ToString(CultureInfo.InvariantCulture) }; return row; }
        public static SequenceHierarchyRow ForStep(FctStudioProject project, FlowBlockInstance instance, FunctionBlockDefinition block, BlockStepDefinition step, IDictionary<string, object> parameterValues, string number, string path, string key, int depth, Action changed)
        {
            SequenceHierarchyRow row = new SequenceHierarchyRow(project, instance, block, step, number, path, key, depth, changed); row._parameterValues = parameterValues; SequenceStepDefinition definition = step.ToStep(); row.TypeText = FriendlyType(definition); row.IconGlyph = IconFor(definition); row.IconBrush = IconBrushFor(definition); row.BuildFields(definition); row.Status = row.Enabled ? "已启用" : "已停用"; return row;
        }
        public FctStudioProject Project { get { return _project; } } public FlowBlockInstance Instance { get; private set; } public FunctionBlockDefinition Block { get; private set; } public FunctionBlockDefinition ReferencedBlock { get; private set; } public BlockStepDefinition Step { get; private set; } public string Number { get; private set; } public string Path { get; private set; } public string Key { get; private set; } public int Depth { get; private set; } public Thickness Indent { get { return new Thickness(Depth * 22, 0, 0, 0); } } public bool IsModule { get; private set; } public bool HasChildren { get; private set; } public bool IsExpanded { get; private set; } public string Chevron { get { return IsExpanded ? "⌄" : "›"; } } public Visibility ExpandVisibility { get { return HasChildren ? Visibility.Visible : Visibility.Hidden; } } public string TypeText { get; private set; } public string IconGlyph { get; private set; } public Brush IconBrush { get; private set; } public string BindingText { get; private set; } public Visibility BindingVisibility { get { return string.IsNullOrWhiteSpace(BindingText) ? Visibility.Collapsed : Visibility.Visible; } } public string BadgeText { get; private set; } public Visibility BadgeVisibility { get { return string.IsNullOrWhiteSpace(BadgeText) ? Visibility.Collapsed : Visibility.Visible; } } public ObservableCollection<HierarchyValueField> ValueFields { get; private set; } public string ValueSummary { get; private set; } public Visibility SummaryVisibility { get { return ValueFields.Count == 0 && !string.IsNullOrWhiteSpace(ValueSummary) ? Visibility.Visible : Visibility.Collapsed; } }
        public string NameText
        {
            get
            {
                if (Depth == 0) return Instance == null ? string.Empty : Instance.DisplayName;
                if (Step == null) return string.Empty;
                if (Step.IsModuleReference) return Step.ReferencedBlockName;
                Dictionary<string, object> overrides = StepValues(false);
                object overrideName;
                if (overrides != null && overrides.TryGetValue("StepName", out overrideName) && overrideName != null && !string.IsNullOrWhiteSpace(Convert.ToString(overrideName, CultureInfo.InvariantCulture)))
                    return Convert.ToString(overrideName, CultureInfo.InvariantCulture);
                return Step.ToStep().StepName;
            }
            set
            {
                string text = (value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                if (Depth == 0 && Instance != null) Instance.DisplayName = text;
                else if (Step != null && Step.IsModuleReference) Step.ReferencedBlockName = text;
                else if (Step != null)
                {
                    SetProperty("StepName", text);
                    if (Step.StepProperties != null) Step.StepProperties["StepName"] = text;
                }
                Raise("NameText");
                Changed();
            }
        }
        public bool NameReadOnly { get { string role = Step == null || Step.IsModuleReference ? string.Empty : Convert.ToString(Step.ToStep().Get("StructureRole", string.Empty), CultureInfo.InvariantCulture); return role == "ELSE" || role == "ENDIF"; } }
        public bool Enabled { get { if (Depth == 0) return Instance != null && Instance.Enabled; if (Step == null) return true; Dictionary<string, object> values = StepValues(false); object value; return values != null && values.TryGetValue("__Enabled", out value) ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : Step.Enabled; } set { if (Depth == 0 && Instance != null) Instance.Enabled = value; else { Dictionary<string, object> values = StepValues(true); values["__Enabled"] = value; } Status = value ? "已启用" : "已停用"; Raise("Enabled"); Changed(); } }
        public string LowLimitText { get { return GetText("LowLimit"); } set { SetNumericOrText("LowLimit", value); } } public string HighLimitText { get { return GetText("HighLimit"); } set { SetNumericOrText("HighLimit", value); } } public string CompareText { get { return GetText("Comtype"); } set { SetProperty("Comtype", value ?? string.Empty); } } public string UnitText { get { return GetText("Unit"); } set { SetProperty("Unit", value ?? string.Empty); } }
        public Visibility BreakpointVisibility { get { return Step != null && !Step.IsModuleReference ? Visibility.Visible : Visibility.Collapsed; } } public string BreakpointGlyph { get { return Breakpoint ? "●" : "○"; } } public Brush BreakpointBrush { get { return Breakpoint ? SequenceHierarchyEditor.Brush(220, 42, 42) : SequenceHierarchyEditor.Brush(163, 176, 194); } } public bool Breakpoint { get { return _project != null && Step != null && (_project.Breakpoints.Contains(Instance.Id + ":" + Path) || _project.Breakpoints.Contains(Instance.Id + ":" + Step.Id)); } set { if (_project == null || Step == null) return; string pathKey = Instance.Id + ":" + Path, legacyKey = Instance.Id + ":" + Step.Id; _project.Breakpoints.Remove(pathKey); _project.Breakpoints.Remove(legacyKey); if (value) _project.Breakpoints.Add(pathKey); Raise("Breakpoint"); Raise("BreakpointGlyph"); Raise("BreakpointBrush"); Changed(); } }
        public string Status { get { return Enabled ? _status : "已停用"; } set { _status = value ?? string.Empty; Raise("Status"); Raise("DebugResult"); } } public string DebugResult { get { string value = Status; return value == "已启用" || value == "待运行" ? string.Empty : value; } } public string Result { get { return _result; } set { _result = value ?? string.Empty; Raise("Result"); Raise("TestValue"); } } public string TestValue { get { return _result; } } public string Products { get { FunctionBlockDefinition source = ReferencedBlock ?? Block; return source == null || source.SupportedProducts == null || source.SupportedProducts.Count == 0 ? (Instance == null ? string.Empty : "全部") : string.Join("/", source.SupportedProducts); } } public bool IsDebugMode { get; set; } public string OperationText { get { if (Step == null) return string.Empty; if (Step.IsModuleReference) return "绑定…"; string role = Convert.ToString(Step.ToStep().Get("StructureRole", string.Empty), CultureInfo.InvariantCulture); return role == "ELSE" || role == "ENDIF" ? string.Empty : "配置…"; } } public Visibility OperationVisibility { get { return string.IsNullOrWhiteSpace(OperationText) ? Visibility.Collapsed : Visibility.Visible; } } public bool HasComplexConfiguration { get; private set; }
        public void SetDebugMode(bool enabled) { IsDebugMode = enabled; Raise("IsDebugMode"); Raise("OperationText"); Raise("OperationVisibility"); }
        public void ApplyModuleBinding(FunctionBlockDefinition target)
        {
            if (target == null || Step == null || !Step.IsModuleReference || Instance == null) return; Dictionary<string, object> values = StepValues(true); values["__ReferencedBlockId"] = target.Id; values["__ReferencedBlockName"] = target.Name; if (Instance.ReferenceParameterOverrides == null) Instance.ReferenceParameterOverrides = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal); Dictionary<string, object> parameters = new Dictionary<string, object>(StringComparer.Ordinal); foreach (BlockParameterDefinition parameter in target.Parameters ?? new List<BlockParameterDefinition>()) parameters[parameter.Name] = parameter.DefaultValue; Instance.ReferenceParameterOverrides[Path] = parameters; if (Instance.ModuleSnapshots == null) Instance.ModuleSnapshots = new Dictionary<string, FunctionBlockDefinition>(StringComparer.Ordinal); CaptureBindingSnapshot(target, new HashSet<string>(StringComparer.Ordinal)); ReferencedBlock = target; BindingText = "绑定：" + target.Name; BadgeText = string.Equals(target.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? "产品模块" : string.Equals(target.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase) ? "自定义模块" : string.Empty; HasChildren = target.Steps != null && target.Steps.Count > 0; Raise("BindingText"); Raise("BadgeText"); Raise("BadgeVisibility"); Raise("Products"); Raise("HasChildren"); Changed();
        }
        private void CaptureBindingSnapshot(FunctionBlockDefinition block, ISet<string> visited) { if (block == null || !visited.Add(block.Id)) return; Instance.ModuleSnapshots[block.Id] = block.Clone(); if (_project == null) return; foreach (BlockStepDefinition reference in block.Steps.Where(value => value.IsModuleReference)) { FunctionBlockDefinition child = _project.Blocks.FirstOrDefault(value => value.Id == reference.ReferencedBlockId); CaptureBindingSnapshot(child, visited); } }
        public SequenceStepDefinition BuildEffectiveStep()
        {
            if (Step == null || Step.IsModuleReference) return null; SequenceStepDefinition result = SequenceEditing.Clone(Step.ToStep()); foreach (KeyValuePair<string, string> binding in Step.ParameterBindings ?? new Dictionary<string, string>()) { object value; if (_parameterValues != null && _parameterValues.TryGetValue(binding.Value, out value)) result.Properties[binding.Key] = value; } Dictionary<string, object> overrides = StepValues(false); if (overrides != null) foreach (KeyValuePair<string, object> pair in overrides.Where(value => !value.Key.StartsWith("__", StringComparison.Ordinal))) result.Properties[pair.Key] = pair.Value; return result;
        }
        public void ApplyConfiguredStep(SequenceStepDefinition configured)
        {
            if (configured == null || Step == null || Step.IsModuleReference) return;
            Dictionary<string, object> values = StepValues(true);
            foreach (KeyValuePair<string, object> pair in configured.Properties)
            {
                values[pair.Key] = pair.Value;
                if (Step.StepProperties != null) Step.StepProperties[pair.Key] = pair.Value;
            }
            BuildFields(configured);
            Raise("NameText"); Raise("ValueFields"); Raise("ValueSummary"); Raise("SummaryVisibility"); Raise("LowLimitText"); Raise("HighLimitText"); Raise("CompareText"); Raise("UnitText");
            Changed();
        }

        /// <summary>
        /// After DCDC_LOAD SetMode changes (CC/CV/CR/CP), retarget the next setpoint step (0A/0V/...).
        /// </summary>
        public bool SyncDcdcLoadFollowOnSetpoint(SequenceStepDefinition modeStep)
        {
            if (modeStep == null || Block == null || Step == null) return false;
            string device = Convert.ToString(modeStep.Get("Device"), CultureInfo.InvariantCulture);
            string operation = Convert.ToString(modeStep.Get("Operation"), CultureInfo.InvariantCulture);
            if (!string.Equals(device, "DCDC_LOAD", StringComparison.OrdinalIgnoreCase) || !string.Equals(operation, "SetMode", StringComparison.OrdinalIgnoreCase)) return false;
            string family = ActionConfigurationPanel.DcdcModeFamily(Convert.ToString(modeStep.Get("Mode"), CultureInfo.InvariantCulture));
            string targetOperation = ActionConfigurationPanel.DcdcSetpointOperation(family);
            string field = ActionConfigurationPanel.DcdcSetpointField(family);
            int index = Block.Steps.IndexOf(Step);
            if (index < 0) return false;
            for (int i = index + 1; i < Block.Steps.Count && i <= index + 3; i++)
            {
                BlockStepDefinition next = Block.Steps[i];
                if (next == null || next.IsModuleReference) continue;
                SequenceStepDefinition def = next.ToStep();
                if (!string.Equals(Convert.ToString(def.Get("Device"), CultureInfo.InvariantCulture), "DCDC_LOAD", StringComparison.OrdinalIgnoreCase)) continue;
                string nextOp = Convert.ToString(def.Get("Operation"), CultureInfo.InvariantCulture);
                if (!string.Equals(nextOp, "SetCurrent", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(nextOp, "SetVoltage", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(nextOp, "SetResistance", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(nextOp, "SetPower", StringComparison.OrdinalIgnoreCase)) continue;
                if (next.StepProperties == null) next.StepProperties = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (string remove in new[] { "Current", "Voltage", "Resistance", "Power" }) next.StepProperties.Remove(remove);
                next.StepProperties["Device"] = "DCDC_LOAD";
                next.StepProperties["Operation"] = targetOperation;
                next.StepProperties[field] = 0.0;
                next.StepProperties["ResultMode"] = "Action";
                string oldName = Convert.ToString(next.StepProperties.ContainsKey("StepName") ? next.StepProperties["StepName"] : def.StepName, CultureInfo.InvariantCulture);
                next.StepProperties["StepName"] = ActionConfigurationPanel.DcdcSetpointStepName(oldName, family);
                if (Instance != null && Instance.StepOverrides != null)
                {
                    string nextPath = string.IsNullOrWhiteSpace(Path) ? next.Id : Path.Substring(0, Path.LastIndexOf('/') >= 0 ? Path.LastIndexOf('/') + 1 : 0) + next.Id;
                    // Prefer match by adjacent hierarchy row refresh; also write override under known sibling paths ending with next.Id
                    foreach (string key in Instance.StepOverrides.Keys.Where(value => value.EndsWith("/" + next.Id, StringComparison.Ordinal) || value == next.Id).ToList())
                    {
                        Dictionary<string, object> ov = Instance.StepOverrides[key];
                        foreach (string remove in new[] { "Current", "Voltage", "Resistance", "Power" }) ov.Remove(remove);
                        ov["Device"] = "DCDC_LOAD";
                        ov["Operation"] = targetOperation;
                        ov[field] = 0.0;
                        ov["StepName"] = next.StepProperties["StepName"];
                    }
                }
                return true;
            }
            return false;
        }
        private void BuildFields(SequenceStepDefinition definition)
        {
            ValueFields.Clear(); ValueSummary = string.Empty;
            SequenceStepDefinition effectiveDefinition = SequenceEditing.Clone(definition); Dictionary<string, object> effectiveOverrides = StepValues(false); if (effectiveOverrides != null) foreach (KeyValuePair<string, object> pair in effectiveOverrides.Where(pair => !pair.Key.StartsWith("__", StringComparison.Ordinal))) effectiveDefinition.Properties[pair.Key] = pair.Value;
            Dictionary<string, BlockParameterDefinition> parameters = (Block.Parameters ?? new List<BlockParameterDefinition>()).Where(value => !string.IsNullOrWhiteSpace(value.Name)).GroupBy(value => value.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal); foreach (KeyValuePair<string, string> binding in Step.ParameterBindings ?? new Dictionary<string, string>()) { if (IsLimitKey(binding.Key)) continue; BlockParameterDefinition parameter; parameters.TryGetValue(binding.Value, out parameter); object value = ResolveParameter(binding.Value, parameter == null ? null : parameter.DefaultValue); bool onOff = IsOnOffField(binding.Key, parameter == null ? null : parameter.Type, parameter == null ? null : parameter.Unit); if (onOff) value = Convert.ToBoolean(NormalizeBool(value), CultureInfo.InvariantCulture) ? "ON" : "OFF"; ValueFields.Add(HierarchyValueField.ForParameter(parameter == null ? binding.Value : parameter.DisplayName, value, onOff ? string.Empty : (parameter == null ? string.Empty : parameter.Unit), false, next => { if (_parameterValues != null) _parameterValues[binding.Value] = onOff ? NormalizeBool(next) : next; Changed(); }, onOff)); }
            if (ValueFields.Count == 0)
            {
                string[] keys = { "Voltage", "Current", "Output", "TargetCurrent", "StepCurrent", "Frequency", "HoldTime", "TimeMs", "Speed", "Position", "Resistance", "ResValue", "Power", "Value", "Count", "TimeoutMs", "PeriodMs" }; foreach (string key in keys) { object value; if (!definition.Properties.TryGetValue(key, out value) || value == null) continue; object effective = EffectiveValue(key, value); bool onOff = IsOnOffField(key, null, UnitFor(key, definition)); if (onOff) effective = Convert.ToBoolean(NormalizeBool(effective), CultureInfo.InvariantCulture) ? "ON" : "OFF"; ValueFields.Add(HierarchyValueField.ForParameter(key, effective, onOff ? string.Empty : UnitFor(key, definition), false, next => { SetProperty(key, onOff ? NormalizeBool(next) : next); }, onOff)); if (ValueFields.Count >= 4) break; }
            }
            bool batchConfiguration = effectiveDefinition.Properties.Keys.Any(key => key == "ChangesJson" || key == "SignalChecksJson" || key == "SignalsJson" || key == "ParametersJson" || key == "DataHex"); bool hasInlineJudgment = effectiveDefinition.Properties.ContainsKey("LowLimit") || effectiveDefinition.Properties.ContainsKey("HighLimit") || effectiveDefinition.Properties.ContainsKey("Comtype") || effectiveDefinition.Properties.ContainsKey("Limit"); HasComplexConfiguration = batchConfiguration || ValueFields.Count > 4 || ValueFields.Count == 0 && !hasInlineJudgment; if (ValueFields.Count == 0) ValueSummary = batchConfiguration ? ActionSummary(effectiveDefinition) + "，" + ComplexSummary(effectiveDefinition) : ActionSummary(effectiveDefinition);
        }
        private object ResolveParameter(string name, object defaultValue) { object value; return _parameterValues != null && _parameterValues.TryGetValue(name, out value) ? value : defaultValue; }
        private object EffectiveValue(string key, object fallback) { Dictionary<string, object> values = StepValues(false); object value; return values != null && values.TryGetValue(key, out value) ? value : fallback; }
        private Dictionary<string, object> StepValues(bool create) { if (Instance == null || string.IsNullOrWhiteSpace(Path)) return null; if (Instance.StepOverrides == null) Instance.StepOverrides = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal); Dictionary<string, object> values; if (!Instance.StepOverrides.TryGetValue(Path, out values) && create) { values = new Dictionary<string, object>(StringComparer.Ordinal); Instance.StepOverrides[Path] = values; } return values; }
        private string GetText(string key) { if (Step == null || Step.IsModuleReference) return string.Empty; string parameter; if (Step.ParameterBindings != null && Step.ParameterBindings.TryGetValue(key, out parameter)) { object value; if (_parameterValues != null && _parameterValues.TryGetValue(parameter, out value)) return Convert.ToString(value, CultureInfo.InvariantCulture); } Dictionary<string, object> values = StepValues(false); object result; if (values != null && values.TryGetValue(key, out result)) return Convert.ToString(result, CultureInfo.InvariantCulture); return Step.StepProperties != null && Step.StepProperties.TryGetValue(key, out result) ? Convert.ToString(result, CultureInfo.InvariantCulture) : string.Empty; }
        private void SetProperty(string key, object value) { if (Step == null) return; string parameter; if (Step.ParameterBindings != null && Step.ParameterBindings.TryGetValue(key, out parameter) && _parameterValues != null) _parameterValues[parameter] = value; else StepValues(true)[key] = value; Raise(key + "Text"); Changed(); }
        private void SetNumericOrText(string key, string text) { if (string.IsNullOrWhiteSpace(text)) { SetProperty(key, string.Empty); return; } double number; SetProperty(key, double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? (object)number : text); }
        private static bool IsLimitKey(string key) { return key == "LowLimit" || key == "HighLimit" || key == "Comtype" || key == "Unit" || key == "Limit"; }
        private static bool IsOnOffField(string key, string type, string unit)
        {
            if (string.Equals(key, "Output", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "OutputEnabled", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "boolean", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(unit, "ON/OFF", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static object NormalizeBool(object value)
        {
            if (value is bool) return value;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            bool parsed;
            if (bool.TryParse(text, out parsed)) return parsed;
            if (text == "1" || string.Equals(text, "ON", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "YES", StringComparison.OrdinalIgnoreCase) || text == "开") return true;
            if (text == "0" || string.Equals(text, "OFF", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "NO", StringComparison.OrdinalIgnoreCase) || text == "关") return false;
            return false;
        }
        private static string UnitFor(string key, SequenceStepDefinition step) { string unit = Convert.ToString(step.Get("Unit", string.Empty), CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(unit)) return unit; if (string.Equals(key, "Output", StringComparison.OrdinalIgnoreCase)) return "ON/OFF"; if (key.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0) return "V"; if (key.IndexOf("Current", StringComparison.OrdinalIgnoreCase) >= 0) return "A"; if (key.IndexOf("Frequency", StringComparison.OrdinalIgnoreCase) >= 0) return "Hz"; if (key.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Period", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0) return "ms"; return string.Empty; }
        private static string ComplexSummary(SequenceStepDefinition step) { string json = Convert.ToString(step.Get("SignalChecksJson", step.Get("SignalsJson", string.Empty)), CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(json)) { try { Newtonsoft.Json.Linq.JToken token = Newtonsoft.Json.Linq.JToken.Parse(json); return "已选 " + (token.Type == Newtonsoft.Json.Linq.JTokenType.Array ? token.Count() : token.Children<Newtonsoft.Json.Linq.JProperty>().Count()) + " 项"; } catch { } } return "复杂配置"; }
        internal static string ActionSummary(SequenceStepDefinition step)
        {
            string device = Convert.ToString(step.Get("Device", string.Empty), CultureInfo.InvariantCulture), operation = Convert.ToString(step.Get("Operation", string.Empty), CultureInfo.InvariantCulture);
            string structureRole = Convert.ToString(step.Get("StructureRole", string.Empty), CultureInfo.InvariantCulture); if (structureRole == "IF") return "如果 " + Convert.ToString(step.Get("VariableName", "选择变量"), CultureInfo.InvariantCulture) + " " + FriendlyCompare(Convert.ToString(step.Get("Compare", "GT"), CultureInfo.InvariantCulture)) + " " + Convert.ToString(step.Get("RightValue", "0"), CultureInfo.InvariantCulture); if (structureRole == "ELSE") return "条件不成立时执行"; if (structureRole == "ENDIF") return "IF结束";
            if (device == "RELAY_FCT" || device == "RELAY_HVMUX")
            {
                if (operation == "SetDO") return RelayIoSummary(Convert.ToString(step.Get("Channels", string.Empty), CultureInfo.InvariantCulture), Convert.ToString(step.Get("Values", string.Empty), CultureInfo.InvariantCulture));
                if (operation == "SelectFctMux") return "选择FCT测试功能";
                if (operation == "DisableFctMux") return "关闭FCT测试选择";
                if (operation == "Select15") return "选择高压测量通道";
                if (operation == "Disable15") return "关闭高压测量通道";
            }
            ActionDescriptor descriptor = ActionCatalog.Find("仪器", device, operation, step.FunctionName); if (descriptor != null)
            {
                List<string> settings = new List<string>(); foreach (ActionFieldSpec field in descriptor.Fields) { object value = step.Get(field.Name); if (value == null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture))) continue; settings.Add(field.Label + "=" + ShortValue(value) + field.Unit); if (settings.Count >= 3) break; } return settings.Count == 0 ? descriptor.DisplayName : descriptor.DisplayName + "：" + string.Join("，", settings);
            }
            if (step.FunctionName == "FCT_CANSignal") return operation.Equals("Write", StringComparison.OrdinalIgnoreCase) ? "写入产品信号" : "读取产品信号";
            if (step.FunctionName == "FCT_CANTable") return operation.Equals("Write", StringComparison.OrdinalIgnoreCase) ? "写入产品数据表" : "读取产品数据表";
            if (step.FunctionName == "FCT_ExecuteLogic") return operation == "SafeShutdown" ? "执行安全下电" : operation == "Stop" ? "停止流程" : string.IsNullOrWhiteSpace(operation) ? "执行流程逻辑" : "执行逻辑：" + operation;
            return string.IsNullOrWhiteSpace(operation) ? "执行该动作" : "执行：" + operation;
        }
        private static string RelayIoSummary(string channelsText, string valuesText)
        {
            string[] channels = (channelsText ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray(), values = (valuesText ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim() == "1" ? "1" : "0").ToArray(); int count = Math.Min(channels.Length, values.Length); if (count == 0) return "设置继电器IO";
            List<Tuple<string, string>> settings = new List<Tuple<string, string>>(); for (int index = 0; index < count; index++) settings.Add(Tuple.Create(ToBoardPort(channels[index]), values[index]));
            if (count > 6)
            {
                int on = settings.Count(value => value.Item2 == "1"), off = count - on; if (on == 0 || off == 0) return "设置" + count.ToString(CultureInfo.InvariantCulture) + "路：全部=" + (on == 0 ? "0" : "1"); return "设置" + count.ToString(CultureInfo.InvariantCulture) + "路：1=" + on.ToString(CultureInfo.InvariantCulture) + "路，0=" + off.ToString(CultureInfo.InvariantCulture) + "路";
            }
            return string.Join("；", settings.GroupBy(value => value.Item2).Select(group => string.Join("、", group.Select(value => value.Item1)) + "=" + group.Key));
        }
        private static string ToBoardPort(string channel)
        {
            string text = (channel ?? string.Empty).Trim().ToUpperInvariant(); int number; if (text.StartsWith("OUT", StringComparison.Ordinal) && int.TryParse(text.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number > 0) return "Y" + Convert.ToString(number - 1, 8).PadLeft(2, '0'); return text;
        }
        private static string ShortValue(object value)
        {
            if (value is bool) return (bool)value ? "ON" : "OFF"; string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty; int separator = text.IndexOf(" - ", StringComparison.Ordinal); if (separator > 0) text = text.Substring(0, separator).Trim(); return text.Length <= 32 ? text : text.Substring(0, 29) + "...";
        }
        private static string FriendlyCompare(string value) { switch ((value ?? string.Empty).ToUpperInvariant()) { case "GT": return ">"; case "GE": return "≥"; case "LT": return "<"; case "LE": return "≤"; case "NE": return "≠"; default: return "="; } }
        private static string FriendlyType(SequenceStepDefinition step) { string role = Convert.ToString(step.Get("StructureRole", string.Empty), CultureInfo.InvariantCulture); if (role == "IF") return "IF"; if (role == "ELSE") return "ELSE"; if (role == "ENDIF") return "ENDIF"; string operation = Convert.ToString(step.Get("Operation", string.Empty), CultureInfo.InvariantCulture); if (operation == "Delay" || step.StepName.IndexOf("等待", StringComparison.OrdinalIgnoreCase) >= 0) return "等待"; if (step.Properties.ContainsKey("LowLimit") || step.Properties.ContainsKey("HighLimit") || step.Properties.ContainsKey("SignalChecksJson")) return "测量"; if (step.FunctionName == "FCT_ExecuteLogic") return "逻辑"; if (step.FunctionName == "FCT_CANTable" || step.FunctionName == "FCT_CANSignal") return "产品"; return "动作"; }
        private static string IconFor(SequenceStepDefinition step) { string type = FriendlyType(step); return type == "等待" ? "\uE916" : type == "测量" ? "\uE9D2" : type == "逻辑" ? "\uE8F2" : type == "产品" ? "\uE968" : "\uE768"; }
        private static Brush IconBrushFor(SequenceStepDefinition step) { string type = FriendlyType(step); return type == "等待" ? SequenceHierarchyEditor.Brush(232, 145, 22) : type == "测量" ? SequenceHierarchyEditor.Brush(24, 112, 224) : type == "逻辑" ? SequenceHierarchyEditor.Brush(208, 70, 104) : SequenceHierarchyEditor.Brush(32, 170, 101); }
        private void Changed() { if (_changed != null) _changed(); }
        private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class HierarchyValueField : INotifyPropertyChanged
    {
        private readonly Action<object> _set; private readonly Type _type; private string _text; private bool _isValid = true;
        private HierarchyValueField(string name, object value, string unit, bool readOnly, Action<object> set, bool isOnOff)
        {
            DisplayName = string.IsNullOrWhiteSpace(name) ? "参数" : name;
            IsOnOff = isOnOff;
            _type = isOnOff ? typeof(string) : (value == null ? typeof(string) : value.GetType());
            if (isOnOff)
            {
                bool on = value is bool && (bool)value
                    || string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), "ON", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), "True", StringComparison.OrdinalIgnoreCase)
                    || Convert.ToString(value, CultureInfo.InvariantCulture) == "1";
                _text = on ? "ON" : "OFF";
                Unit = string.Empty;
            }
            else
            {
                _text = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
                Unit = unit ?? string.Empty;
            }
            IsReadOnly = readOnly;
            _set = set;
        }
        public static HierarchyValueField ForParameter(string name, object value, string unit, bool readOnly, Action<object> set, bool isOnOff = false) { return new HierarchyValueField(name, value, unit, readOnly, set, isOnOff); }
        public string DisplayName { get; private set; }
        public string Unit { get; private set; }
        public bool IsReadOnly { get; private set; }
        public bool IsOnOff { get; private set; }
        public bool IsValid { get { return _isValid; } private set { if (_isValid == value) return; _isValid = value; Raise("IsValid"); } }
        public string ValueText
        {
            get { return _text; }
            set
            {
                string next = value ?? string.Empty;
                if (IsOnOff)
                {
                    bool on = string.Equals(next, "ON", StringComparison.OrdinalIgnoreCase) || string.Equals(next, "True", StringComparison.OrdinalIgnoreCase) || next == "1";
                    next = on ? "ON" : "OFF";
                }
                _text = next;
                object converted;
                if (!TryConvert(_text, out converted)) { IsValid = false; Raise("ValueText"); return; }
                IsValid = true;
                if (!IsReadOnly && _set != null) _set(IsOnOff ? (object)string.Equals(_text, "ON", StringComparison.OrdinalIgnoreCase) : converted);
                Raise("ValueText");
            }
        }
        private bool TryConvert(string text, out object value)
        {
            value = text;
            if (IsOnOff) { value = string.Equals(text, "ON", StringComparison.OrdinalIgnoreCase); return true; }
            if (_type == typeof(bool)) { bool parsed; if (bool.TryParse(text, out parsed)) { value = parsed; return true; } if (text == "1" || string.Equals(text, "ON", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "YES", StringComparison.OrdinalIgnoreCase)) { value = true; return true; } if (text == "0" || string.Equals(text, "OFF", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "NO", StringComparison.OrdinalIgnoreCase)) { value = false; return true; } return false; }
            if (_type == typeof(int) || _type == typeof(short) || _type == typeof(long)) { int parsed; if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return false; value = parsed; return true; }
            if (_type == typeof(double) || _type == typeof(float) || _type == typeof(decimal)) { double parsed; if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false; value = parsed; return true; }
            return true;
        }
        private void Raise(string name) { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class SequenceHierarchyCommand
    {
        public SequenceHierarchyCommand(string command, SequenceHierarchyRow row, int relativeOffset) { Command = command; Row = row; RelativeOffset = relativeOffset; }
        public SequenceHierarchyCommand(string command, SequenceHierarchyRow row, int relativeOffset, ActionDescriptor descriptor) : this(command, row, relativeOffset) { Descriptor = descriptor; }
        public SequenceHierarchyCommand(string command, SequenceHierarchyRow row, int relativeOffset, string source, string target, string operation) : this(command, row, relativeOffset) { Source = source; Target = target; Operation = operation; }
        public string Command { get; private set; }
        public SequenceHierarchyRow Row { get; private set; }
        public int RelativeOffset { get; private set; }
        public ActionDescriptor Descriptor { get; private set; }
        public string Source { get; private set; }
        public string Target { get; private set; }
        public string Operation { get; private set; }
    }
}
