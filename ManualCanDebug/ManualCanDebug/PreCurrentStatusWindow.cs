using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class PreCurrentStatusWindow : Window
    {
        private readonly Func<IReadOnlyList<PreCurrentReadResult>> _readStatuses;
        private readonly DataGrid _valueGrid;
        private readonly TextBlock _statusText;
        private readonly TextBox _detailTextBox;
        private readonly Button _refreshButton;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;

        public PreCurrentStatusWindow(Func<IReadOnlyList<PreCurrentReadResult>> readStatuses, ProductCanProfile profile, string currentText)
        {
            _readStatuses = readStatuses ?? throw new ArgumentNullException(nameof(readStatuses));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            Title = "出流前产品状态 - " + profile.DisplayName;
            Width = 1100;
            Height = 520;
            MinWidth = 900;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock description = new TextBlock
            {
                Text = string.Format("产品型号：{0}    当前指令：{1}\n以下数值按当前型号地址配置从产品内部读取，仅供人工确认；工具不判断是否合格。{2} 故障输入名称带 ^，为低电平有效：0=触发、1=未触发。\nMotor Status 参考：出流过程中通常为 Ramp=保持(2)、Status=运行中(1)；完成后通常为 Ramp=完成(4)、Status=成功完成(2)。请结合原始字节和中文解析人工确认。",
                    profile.DisplayName,
                    currentText,
                    profile.Model == ProductModel.C95 ? "C95高压来源为HVDC_SENSE_AI（Analog字节44）。" : "C91高压、Battery、PSR和板温使用原SEQ地址。"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(description, 0);
            root.Children.Add(description);

            _valueGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };
            _valueGrid.Columns.Add(new DataGridTextColumn { Header = "项目", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _valueGrid.Columns.Add(new DataGridTextColumn { Header = "表格来源", Binding = new Binding("Source"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star) });
            _valueGrid.Columns.Add(new DataGridTextColumn { Header = "读取地址", Binding = new Binding("Address"), Width = 160 });
            _valueGrid.Columns.Add(new DataGridTextColumn { Header = "读取值", Binding = new Binding("ValueText"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            _valueGrid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new Binding("Unit"), Width = 80 });
            _valueGrid.Columns.Add(new DataGridTextColumn { Header = "状态含义", Binding = new Binding("Status"), Width = new DataGridLength(2.8, DataGridLengthUnitType.Star) });
            _valueGrid.SelectionChanged += ValueGrid_SelectionChanged;
            Grid.SetRow(_valueGrid, 1);
            root.Children.Add(_valueGrid);

            _detailTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_detailTextBox, 2);
            root.Children.Add(_detailTextBox);

            _statusText = new TextBlock
            {
                Text = "等待读取",
                Margin = new Thickness(0, 10, 0, 6),
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(_statusText, 3);
            root.Children.Add(_statusText);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _refreshButton = MakeButton("重新读取", Refresh_Click, 110);
            Button copyButton = MakeButton("复制状态详情", CopyStatus_Click, 130);
            _confirmButton = MakeButton("确认并发送当前指令", Confirm_Click, 180);
            _cancelButton = MakeButton("取消", Cancel_Click, 90);
            buttons.Children.Add(_refreshButton);
            buttons.Children.Add(copyButton);
            buttons.Children.Add(_confirmButton);
            buttons.Children.Add(_cancelButton);
            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);

            Content = root;
            Loaded += Window_Loaded;
        }

        public bool Confirmed { get; private set; }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshValuesAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshValuesAsync();
        }

        private async Task RefreshValuesAsync()
        {
            SetBusy(true);
            _statusText.Text = "正在从产品读取状态...";
            try
            {
                IReadOnlyList<PreCurrentReadResult> results = await Task.Run(_readStatuses);
                List<DisplayRow> rows = results.Select(CreateRow).ToList();
                _valueGrid.ItemsSource = rows;
                DisplayRow motorStatus = rows.FirstOrDefault(row => row.Name == "Motor Status");
                DisplayRow diagnosis = rows.FirstOrDefault(row => row.Name == "综合诊断结论");
                _valueGrid.SelectedItem = diagnosis ?? motorStatus ?? rows.FirstOrDefault();
                int failedCount = results.Count(result => !result.Succeeded);
                _statusText.Text = failedCount == 0
                    ? "读取完成，请人工确认以上数值。"
                    : string.Format(CultureInfo.InvariantCulture, "读取完成，其中 {0} 项读取失败；请查看读取状态并人工决定是否继续。", failedCount);
                if (motorStatus != null && motorStatus.Status.IndexOf("诊断故障", StringComparison.Ordinal) >= 0)
                {
                    _statusText.Text += " 产品当前上报诊断故障，正常情况下不会持续出流，请查看上方完整故障位。";
                }
            }
            catch (Exception ex)
            {
                _valueGrid.ItemsSource = null;
                _statusText.Text = "读取过程失败：" + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ValueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DisplayRow row = _valueGrid.SelectedItem as DisplayRow;
            _detailTextBox.Text = row == null ? string.Empty : row.ToCopyText();
        }

        private void CopyStatus_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<DisplayRow> rows = (_valueGrid.ItemsSource as IEnumerable<DisplayRow>) ?? Enumerable.Empty<DisplayRow>();
            StringBuilder text = new StringBuilder();
            text.AppendLine("项目\t表格来源\t读取地址\t读取值\t单位\t状态含义");
            foreach (DisplayRow row in rows) text.AppendLine(row.ToCopyText());
            if (text.Length > 0) Clipboard.SetText(text.ToString());
        }

        private static DisplayRow CreateRow(PreCurrentReadResult result)
        {
            return new DisplayRow
            {
                Name = result.Item.Name,
                Source = result.Item.SourceName,
                Address = result.Item.AddressText,
                ValueText = result.Succeeded
                    ? (string.IsNullOrEmpty(result.TextValue) ? result.Value.ToString("0.###", CultureInfo.InvariantCulture) : result.TextValue)
                    : "读取失败",
                Unit = result.Item.Unit,
                Status = result.Succeeded
                    ? (string.IsNullOrEmpty(result.Interpretation) ? "读取成功" : result.Interpretation)
                    : result.Error
            };
        }

        private void SetBusy(bool busy)
        {
            _refreshButton.IsEnabled = !busy;
            _confirmButton.IsEnabled = !busy;
            _cancelButton.IsEnabled = !busy;
            _valueGrid.IsEnabled = !busy;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = new Button
            {
                Content = text,
                Width = width,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(4)
            };
            button.Click += handler;
            return button;
        }

        private sealed class DisplayRow
        {
            public string Name { get; set; }
            public string Source { get; set; }
            public string Address { get; set; }
            public string ValueText { get; set; }
            public string Unit { get; set; }
            public string Status { get; set; }

            public string ToCopyText()
            {
                return string.Join("\t", Name, Source, Address, ValueText, Unit, Status);
            }
        }
    }
}
