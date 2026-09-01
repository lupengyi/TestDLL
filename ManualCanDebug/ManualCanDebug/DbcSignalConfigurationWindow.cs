using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ManualCanDebug
{
    internal sealed class DbcSignalConfigurationWindow : Window
    {
        private readonly IList<DbcSignalEditRow> _sourceRows;
        private readonly ObservableCollection<DbcSignalEditRow> _rows;
        private readonly bool _readMode;
        private readonly ListCollectionView _view;
        private readonly DataGrid _grid;
        private readonly TextBox _search;
        private readonly TextBlock _footer;
        private ToggleButton _allFilter;
        private ToggleButton _selectedFilter;
        private string _filter = "全部";
        private bool _guard;

        public DbcSignalConfigurationWindow(IEnumerable<DbcSignalEditRow> rows, string mode, string messageName)
        {
            _sourceRows = (rows ?? Enumerable.Empty<DbcSignalEditRow>()).ToList(); _rows = new ObservableCollection<DbcSignalEditRow>(_sourceRows.Select(value => value.CloneForEditing())); _readMode = string.Equals(mode, "读取DBC信号", StringComparison.Ordinal);
            Title = "DBC信号配置"; Width = 1180; Height = 680; MinWidth = 980; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.CanResize; Background = Brushes.Transparent; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; StudioControlTheme.Apply(Resources);
            Border shell = new Border { Background = Brushes.White, BorderBrush = BorderColor(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.2, Color = Colors.SlateGray } };
            Grid root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            DockPanel header = new DockPanel { Background = Brushes.White }; header.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }; Button close = new Button { Content = "×", Width = 48, Height = 40, Margin = new Thickness(0, 7, 7, 7), FontSize = 22, Foreground = Text(92, 105, 123), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand }; close.Click += (s, e) => DialogResult = false; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); TextBlock meta = new TextBlock { Text = (messageName ?? string.Empty) + "  ·  " + mode, Foreground = Text(105, 118, 137), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 16, 0) }; DockPanel.SetDock(meta, Dock.Right); header.Children.Add(meta); header.Children.Add(new TextBlock { Text = "DBC信号配置", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Text(32, 43, 59), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 12, 0) }); root.Children.Add(header);
            Grid body = new Grid { Margin = new Thickness(20, 14, 20, 12), Background = Brushes.White }; body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            DockPanel tools = new DockPanel { LastChildFill = false }; _search = new TextBox { Width = 320, Height = 34, Padding = new Thickness(34, 6, 10, 6), Text = "搜索DBC信号", Foreground = Text(130, 142, 158), HorizontalAlignment = HorizontalAlignment.Left }; _search.GotKeyboardFocus += (s, e) => { if (_search.Text == "搜索DBC信号") { _search.Text = string.Empty; _search.Foreground = Text(37, 49, 67); } }; _search.LostKeyboardFocus += (s, e) => { if (string.IsNullOrWhiteSpace(_search.Text)) { _search.Text = "搜索DBC信号"; _search.Foreground = Text(130, 142, 158); } }; _search.TextChanged += (s, e) => _view.Refresh(); tools.Children.Add(_search); StackPanel filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) }; _allFilter = FilterButton("全部"); _selectedFilter = FilterButton("已选"); filters.Children.Add(_allFilter); filters.Children.Add(_selectedFilter); _allFilter.IsChecked = true; SetFilterVisual(_allFilter, true); tools.Children.Add(filters); body.Children.Add(tools);
            _grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, RowHeight = 40, ColumnHeaderHeight = 38, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = BorderColor(), BorderThickness = new Thickness(1), SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Style centerCheck = new Style(typeof(CheckBox)); centerCheck.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center)); centerCheck.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center)); _grid.Columns.Add(new DataGridCheckBoxColumn { Header = _readMode ? "读取" : "使用", Binding = new Binding("Use") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 72, ElementStyle = centerCheck, EditingElementStyle = centerCheck }); _grid.Columns.Add(new DataGridTextColumn { Header = "信号", Binding = new Binding("Name"), Width = 300, IsReadOnly = true }); _grid.Columns.Add(new DataGridTextColumn { Header = _readMode ? "当前值" : "实际值（可编辑）", Binding = new Binding("ValueText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 150, IsReadOnly = _readMode }); _grid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 90, IsReadOnly = true }); _grid.Columns.Add(new DataGridTextColumn { Header = "原始范围", Binding = new Binding("RawRange"), Width = 165, IsReadOnly = true }); _grid.Columns.Add(new DataGridTextColumn { Header = "枚举说明", Binding = new Binding("EnumText"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true }); _view = new ListCollectionView(_rows); _view.Filter = FilterRow; _grid.ItemsSource = _view; Grid.SetRow(_grid, 1); body.Children.Add(_grid); Grid.SetRow(body, 1); root.Children.Add(body);
            DockPanel footerPanel = new DockPanel { Background = Brushes.White }; _footer = new TextBlock { Foreground = Text(66, 79, 97), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) }; footerPanel.Children.Add(_footer); StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 18, 14) }; DockPanel.SetDock(buttons, Dock.Right); Button cancel = SecondaryButton("取消"); cancel.Click += (s, e) => DialogResult = false; Button apply = PrimaryButton("应用配置"); apply.Click += Apply; buttons.Children.Add(cancel); buttons.Children.Add(apply); footerPanel.Children.Insert(0, buttons); Grid.SetRow(footerPanel, 2); root.Children.Add(footerPanel); shell.Child = root; Content = shell;
            foreach (DbcSignalEditRow row in _rows) row.PropertyChanged += RowChanged; RefreshFooter();
        }

        private ToggleButton FilterButton(string text) { ToggleButton button = new ToggleButton { Content = text, MinWidth = 94, Height = 34, Background = Brushes.White, Foreground = Text(52, 67, 88), BorderBrush = BorderColor(), BorderThickness = new Thickness(1), Margin = new Thickness(0) }; button.Click += (s, e) => { _filter = text; SetFilterVisual(_allFilter, ReferenceEquals(button, _allFilter)); SetFilterVisual(_selectedFilter, ReferenceEquals(button, _selectedFilter)); _view.Refresh(); }; return button; }
        private static void SetFilterVisual(ToggleButton button, bool selected) { if (button == null) return; button.IsChecked = selected; button.Background = selected ? new SolidColorBrush(Color.FromRgb(24, 112, 224)) : Brushes.White; button.Foreground = selected ? Brushes.White : Text(52, 67, 88); }
        private bool FilterRow(object value) { DbcSignalEditRow row = value as DbcSignalEditRow; if (row == null) return false; string search = _search.Text == "搜索DBC信号" ? string.Empty : (_search.Text ?? string.Empty).Trim(); return (search.Length == 0 || (row.Name + " " + row.Unit + " " + row.RawRange + " " + row.EnumText).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) && (_filter == "全部" || row.Use); }
        private void RowChanged(object sender, PropertyChangedEventArgs e) { DbcSignalEditRow row = sender as DbcSignalEditRow; if (_readMode && !_guard && e.PropertyName == "Use" && row != null && row.Use) { _guard = true; foreach (DbcSignalEditRow other in _rows.Where(value => value != row && value.Use)) other.Use = false; _guard = false; } RefreshFooter(); if (_filter == "已选") _view.Refresh(); }
        private void RefreshFooter() { _footer.Text = "已选 " + _rows.Count(value => value.Use) + " / " + _rows.Count + (_readMode ? "  ·  读取模式仅允许选择一个信号" : ""); }
        private void Apply(object sender, RoutedEventArgs e) { _grid.CommitEdit(DataGridEditingUnit.Cell, true); _grid.CommitEdit(DataGridEditingUnit.Row, true); List<DbcSignalEditRow> selected = _rows.Where(value => value.Use).ToList(); if (selected.Count == 0) { MessageBox.Show(this, "请至少选择一个DBC信号。", "DBC信号配置", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (_readMode && selected.Count != 1) { MessageBox.Show(this, "读取DBC信号时只能选择一个信号。", "DBC信号配置", MessageBoxButton.OK, MessageBoxImage.Information); return; } foreach (DbcSignalEditRow edit in _rows) { DbcSignalEditRow original = _sourceRows.FirstOrDefault(value => ReferenceEquals(value.Signal, edit.Signal) || value.Name == edit.Name); if (original != null) original.CopyConfigurationFrom(edit); } DialogResult = true; }
        private static Button PrimaryButton(string text) { return new Button { Content = text, MinWidth = 116, Height = 34, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(24, 112, 224)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(24, 112, 224)), BorderThickness = new Thickness(1) }; }
        private static Button SecondaryButton(string text) { return new Button { Content = text, MinWidth = 94, Height = 34, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0), Background = Brushes.White, Foreground = Text(52, 67, 88), BorderBrush = BorderColor(), BorderThickness = new Thickness(1) }; }
        private static SolidColorBrush Text(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
        private static SolidColorBrush BorderColor() { return new SolidColorBrush(Color.FromRgb(211, 221, 234)); }
    }
}
