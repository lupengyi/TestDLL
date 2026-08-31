using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class ProductCurrentWindow : Window
    {
        private readonly Func<DutCurrentResult> _readCurrent;
        private readonly DataGrid _currentGrid;
        private readonly TextBlock _motorStatusText;
        private readonly TextBlock _readStatusText;
        private readonly Button _refreshButton;

        public ProductCurrentWindow(Func<DutCurrentResult> readCurrent, string productName, double requestedCurrent)
        {
            _readCurrent = readCurrent ?? throw new ArgumentNullException(nameof(readCurrent));
            Title = "产品电流读取 - " + productName;
            Width = 820;
            Height = 460;
            MinWidth = 700;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock description = new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "产品型号：{0}    对应设定电流：{1:0.###} A\n数值直接读取产品 Current Sense 表，只显示原始结果与计算后的 RMS，不进行合格判断。", productName, requestedCurrent),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(description, 0);
            root.Children.Add(description);

            _currentGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };
            _currentGrid.Columns.Add(new DataGridTextColumn { Header = "相位", Binding = new Binding("Name"), Width = 90 });
            _currentGrid.Columns.Add(NumberColumn("瞬时电流 (A)", "Instantaneous"));
            _currentGrid.Columns.Add(NumberColumn("最小电流 (A)", "Minimum"));
            _currentGrid.Columns.Add(NumberColumn("最大电流 (A)", "Maximum"));
            _currentGrid.Columns.Add(NumberColumn("计算 RMS (A)", "Rms"));
            Grid.SetRow(_currentGrid, 1);
            root.Children.Add(_currentGrid);

            StackPanel statusPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
            _motorStatusText = new TextBlock { Text = "Motor Status：等待读取", TextWrapping = TextWrapping.Wrap };
            _readStatusText = new TextBlock { Text = "等待读取", Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 0) };
            statusPanel.Children.Add(_motorStatusText);
            statusPanel.Children.Add(_readStatusText);
            Grid.SetRow(statusPanel, 2);
            root.Children.Add(statusPanel);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _refreshButton = MakeButton("重新读取", Refresh_Click, 110);
            buttons.Children.Add(_refreshButton);
            buttons.Children.Add(MakeButton("关闭", Close_Click, 90));
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            Content = root;
            Loaded += Window_Loaded;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            _refreshButton.IsEnabled = false;
            _readStatusText.Text = "正在读取产品电流，首次读取会等待出流后至少 6 秒...";
            try
            {
                DutCurrentResult result = await Task.Run(_readCurrent);
                _currentGrid.ItemsSource = result.Phases.ToList();
                _motorStatusText.Text = "Motor Status 原始值：" + result.MotorStatusText + "\n中文解析：" + result.MotorStatusDescription;
                _readStatusText.Text = "读取完成；原始 TX/RX 和解析值已写入主界面 LOG。";
            }
            catch (Exception ex)
            {
                _currentGrid.ItemsSource = null;
                _motorStatusText.Text = "Motor Status：读取失败";
                _readStatusText.Text = "读取失败：" + ex.Message;
            }
            finally
            {
                _refreshButton.IsEnabled = true;
            }
        }

        private static DataGridTextColumn NumberColumn(string header, string property)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property) { StringFormat = "0.###" },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            };
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = new Button { Content = text, Width = width, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(4) };
            button.Click += handler;
            return button;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
