using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ManualCanDebug
{
    /// <summary>Shared action configuration and debug-history shell used by module and SEQ editors.</summary>
    internal sealed class ActionConfigurationWorkbench : Grid
    {
        private readonly ObservableCollection<ActionHistoryRow> _history = new ObservableCollection<ActionHistoryRow>();
        private readonly ObservableCollection<LegacyPlatformResultRow> _results = new ObservableCollection<LegacyPlatformResultRow>();
        private readonly TextBlock _summary;
        private readonly TextBox _details;
        public ActionConfigurationWorkbench(ActionConfigurationPanel panel)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel)); Background = Brushes.White;
            TabControl tabs = new TabControl { BorderThickness = new Thickness(0), Background = Brushes.White, FontSize = 12 };
            tabs.Items.Add(new TabItem { Header = "动作配置", Content = panel });
            Grid debug = new Grid { Margin = new Thickness(8) }; debug.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); debug.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); debug.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ListBox history = new ListBox { ItemsSource = _history, DisplayMemberPath = "SummaryText", BorderBrush = Border(), Padding = new Thickness(3) }; debug.Children.Add(history);
            Grid right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) }); _summary = new TextBlock { Text = "执行后将在这里显示测试值和LIMIT判断。", FontWeight = FontWeights.SemiBold, Foreground = Secondary(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }; right.Children.Add(_summary);
            DataGrid resultGrid = new DataGrid { ItemsSource = _results, AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, RowHeight = 32, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = Border() }; resultGrid.Columns.Add(new DataGridTextColumn { Header = "测试项", Binding = new Binding("StepName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "测试值", Binding = new Binding("Value"), Width = 110 }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "下限", Binding = new Binding("LimitsLow"), Width = 75 }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "上限", Binding = new Binding("LimitsHigh"), Width = 75 }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "比较", Binding = new Binding("LimitExpression"), Width = 80 }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 60 }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "结果", Binding = new Binding("Status"), Width = 80 }); resultGrid.Columns.Add(new DataGridTextColumn { Header = "说明", Binding = new Binding("Comment"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) }); Grid.SetRow(resultGrid, 1); right.Children.Add(resultGrid);
            _details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)), BorderThickness = new Thickness(0) }; Grid.SetRow(_details, 2); right.Children.Add(_details); Grid.SetColumn(right, 2); debug.Children.Add(right);
            TabItem debugTab = new TabItem { Header = "调试记录", Content = debug }; tabs.Items.Add(debugTab); Children.Add(tabs);
            panel.ExecutionRecorded += value => { Record(value); tabs.SelectedItem = debugTab; };
            history.SelectionChanged += (s, e) => Record(history.SelectedItem as ActionHistoryRow, false);
        }
        private void Record(ActionHistoryRow history, bool append = true)
        {
            if (history == null) return; if (append) { _history.Insert(0, history); while (_history.Count > 100) _history.RemoveAt(_history.Count - 1); }
            _results.Clear(); if (history.PlatformResult != null && history.PlatformResult.Results != null) foreach (LegacyPlatformResultRow row in history.PlatformResult.Results) _results.Add(row); if (_results.Count == 0) _results.Add(new LegacyPlatformResultRow { StartTime = history.Time, StepName = history.Step.StepName, StepType = "Action", MeasuredValue = history.Result, Status = history.Succeeded ? "Passed" : "Failed", Comment = "该动作没有向平台写入测试结果" }); _summary.Text = (history.Succeeded ? "✓ " : "✕ ") + history.Step.StepName + " · " + _results.Count + " 条结果"; _summary.Foreground = history.Succeeded ? new SolidColorBrush(Color.FromRgb(0, 146, 89)) : new SolidColorBrush(Color.FromRgb(205, 48, 48)); _details.Text = history.DisplayText + (string.IsNullOrWhiteSpace(history.Details) ? string.Empty : Environment.NewLine + history.Details);
        }
        private static Brush Border() { return new SolidColorBrush(Color.FromRgb(220, 228, 239)); }
        private static Brush Secondary() { return new SolidColorBrush(Color.FromRgb(104, 118, 138)); }
    }
}
