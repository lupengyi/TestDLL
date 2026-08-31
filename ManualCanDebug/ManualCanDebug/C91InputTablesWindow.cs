using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class C91InputTablesWindow : Window
    {
        private readonly Func<IReadOnlyList<C91InputSignalResult>> _reader;
        private readonly DataGrid _grid;
        private readonly TextBlock _status;
        private readonly Button _readButton;
        private List<C91InputSignalResult> _results = new List<C91InputSignalResult>();

        public C91InputTablesWindow(Func<IReadOnlyList<C91InputSignalResult>> reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Title = "C91 Locator 全部输入信号";
            Width = 1220;
            Height = 720;
            MinWidth = 900;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock
            {
                Text = "按C91 Locator读取0x00模拟量、0x0C ADC计数、0x18离散量、0x20脉冲量和0x2C逆变器温度，共155个信号。保留RAW字节并显示解析值；名称带^的信号按低电平有效解释。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true, SelectionMode = DataGridSelectionMode.Extended };
            AddColumn("表", "TableName", 150);
            AddColumn("表偏移", "TableOffset", 70);
            AddColumn("字段偏移", "SignalOffset", 75);
            AddColumn("信号名", "SignalName", 260);
            AddColumn("类型", "ValueType", 80);
            AddColumn("解析值", "ValueText", 130);
            AddColumn("RAW", "RawBytes", 150);
            AddColumn("说明", "Interpretation", 180);
            Grid.SetRow(_grid, 1);
            root.Children.Add(_grid);

            DockPanel footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _readButton = MakeButton("重新读取", async (s, e) => await ReadAsync(), 100);
            buttons.Children.Add(_readButton);
            buttons.Children.Add(MakeButton("复制全部", CopyAll, 100));
            buttons.Children.Add(MakeButton("关闭", (s, e) => Close(), 80));
            DockPanel.SetDock(buttons, Dock.Right);
            _status = new TextBlock { Text = "等待读取", VerticalAlignment = VerticalAlignment.Center };
            footer.Children.Add(buttons);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            Loaded += async (s, e) => await ReadAsync();
        }

        private void AddColumn(string header, string property, double width)
        {
            _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(property), Width = width });
        }

        private async Task ReadAsync()
        {
            _readButton.IsEnabled = false;
            _status.Text = "正在读取C91五张输入表...";
            try
            {
                IReadOnlyList<C91InputSignalResult> values = await Task.Run(_reader);
                _results = values.ToList();
                _grid.ItemsSource = _results;
                _status.Text = "读取完成：" + _results.Count.ToString(CultureInfo.InvariantCulture) + "个信号";
            }
            catch (Exception ex)
            {
                _status.Text = "读取失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "C91读取", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _readButton.IsEnabled = true; }
        }

        private void CopyAll(object sender, RoutedEventArgs e)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("表\t表偏移\t字段偏移\t信号名\t类型\t解析值\tRAW\t说明");
            foreach (C91InputSignalResult item in _results)
                text.AppendLine(string.Join("\t", item.TableName, item.TableOffset, item.SignalOffset.ToString(CultureInfo.InvariantCulture), item.SignalName, item.ValueType, item.ValueText, item.RawBytes, item.Interpretation));
            if (_results.Count > 0) Clipboard.SetText(text.ToString());
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = new Button { Content = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) };
            button.Click += handler;
            return button;
        }
    }
}
