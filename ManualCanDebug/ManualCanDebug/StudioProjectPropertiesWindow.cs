using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class StudioProjectPropertiesWindow : Window
    {
        private readonly TextBox _projectName = Box(); private readonly TextBox _product = Box(); private readonly TextBox _station = Box(); private readonly TextBox _version = Box(); private readonly TextBox _serialLength = Box(); private readonly TextBox _logPath = Box();
        public StudioProjectPropertiesWindow(FctStudioProject project)
        {
            Title = "工程属性"; Width = 600; Height = 390; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Grid root = new Grid { Margin = new Thickness(18) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _projectName.Text = project.ProjectName; _product.Text = project.Product; _station.Text = Value(project, "StationName", "DEBUG"); _version.Text = Value(project, "SequenceVersion", "FCT-STUDIO-1"); _serialLength.Text = Value(project, "SerialNumberLen", "0"); _logPath.Text = Value(project, "LogFilePath", "D:\\LogfilePath");
            Add(root, "工程名称", _projectName, 0); Add(root, "产品型号", _product, 1); Add(root, "StationName", _station, 2); Add(root, "SequenceVersion", _version, 3); Add(root, "SerialNumberLen", _serialLength, 4); Add(root, "LogFilePath", _logPath, 5);
            TextBlock note = new TextBlock { Text = "这些字段会进入正式SEQ根节点；功能块、断点和UI信息不会进入正式SEQ。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; Grid.SetRow(note, 6); Grid.SetColumnSpan(note, 2); root.Children.Add(note);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Button ok = new Button { Content = "确定", MinWidth = 90, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) }; ok.Click += Ok_Click; Button cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) }; cancel.Click += (s, e) => DialogResult = false; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 7); Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons); Content = root;
        }
        public void ApplyTo(FctStudioProject project) { project.ProjectName = _projectName.Text.Trim(); project.Product = _product.Text.Trim().ToUpperInvariant(); project.SequenceRoot["StationName"] = _station.Text.Trim(); project.SequenceRoot["SequenceVersion"] = _version.Text.Trim(); int length; if (!int.TryParse(_serialLength.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out length)) throw new FormatException("SerialNumberLen必须是整数。"); project.SequenceRoot["SerialNumberLen"] = length; project.SequenceRoot["LogFilePath"] = _logPath.Text.Trim(); }
        private void Ok_Click(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(_projectName.Text)) { MessageBox.Show(this, "工程名称不能为空。", "工程属性", MessageBoxButton.OK, MessageBoxImage.Information); return; } DialogResult = true; }
        private static TextBox Box() { return new TextBox { Margin = new Thickness(4), Padding = new Thickness(6, 4, 6, 4) }; }
        private static void Add(Grid grid, string label, TextBox box, int row) { TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }; Grid.SetRow(text, row); grid.Children.Add(text); Grid.SetRow(box, row); Grid.SetColumn(box, 1); grid.Children.Add(box); }
        private static string Value(FctStudioProject project, string key, string fallback) { object value; return project.SequenceRoot != null && project.SequenceRoot.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback; }
    }
}
