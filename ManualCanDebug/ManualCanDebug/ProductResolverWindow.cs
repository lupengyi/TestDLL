using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class ProductResolverWindow : Window
    {
        private readonly Func<ProductResolverData> _reader;
        private readonly TextBlock _position;
        private readonly TextBlock _velocity;
        private readonly TextBlock _fault;
        private readonly TextBox _frames;
        private readonly TextBlock _status;
        private readonly Button _refresh;
        private ProductResolverData _result;

        public ProductResolverWindow(ProductCanProfile profile, Func<ProductResolverData> reader)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Title = profile.Model + " 产品内部旋变数据";
            Width = 900;
            Height = 570;
            MinWidth = 720;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock description = new TextBlock
            {
                Text = string.Format("读取{0}产品FT_Resolver_Data（地址表偏移0x{1:X2}，共{2}字节）：位置Float32、速度/频率Float32{3}。仅执行读取，不发送旋变控制指令。",
                    profile.Model, profile.ResolverDataOffset, profile.ResolverDataLength, profile.ResolverDataLength > 8 ? "、故障状态UInt8" : string.Empty),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(description);

            Grid values = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int index = 0; index < 3; index++) values.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddValue(values, 0, "旋变位置：", out _position);
            AddValue(values, 1, "旋变速度/频率：", out _velocity);
            AddValue(values, 2, "旋变故障状态：", out _fault);
            Grid.SetRow(values, 1);
            root.Children.Add(values);

            GroupBox reportGroup = new GroupBox { Header = "完整读取报文（可选择、可复制）", Padding = new Thickness(8) };
            _frames = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas")
            };
            reportGroup.Content = _frames;
            Grid.SetRow(reportGroup, 2);
            root.Children.Add(reportGroup);

            DockPanel footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _refresh = MakeButton("重新读取", RefreshClick, 110);
            buttons.Children.Add(_refresh);
            buttons.Children.Add(MakeButton("复制报文和数值", CopyClick, 140));
            buttons.Children.Add(MakeButton("关闭", (s, e) => Close(), 80));
            DockPanel.SetDock(buttons, Dock.Right);
            _status = new TextBlock { Text = "等待读取", VerticalAlignment = VerticalAlignment.Center };
            footer.Children.Add(buttons);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            Loaded += async (s, e) => await RefreshAsync();
        }

        private static void AddValue(Grid grid, int row, string label, out TextBlock value)
        {
            TextBlock name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(name, row);
            grid.Children.Add(name);
            value = new TextBlock { Text = "--", FontSize = 16, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }

        private async void RefreshClick(object sender, RoutedEventArgs e) { await RefreshAsync(); }

        private async Task RefreshAsync()
        {
            _refresh.IsEnabled = false;
            _status.Text = "正在读取产品内部旋变表...";
            try
            {
                _result = await Task.Run(_reader);
                _position.Text = _result.PositionDegrees.ToString("0.######", CultureInfo.InvariantCulture) + " °";
                _velocity.Text = _result.VelocityFrequency.ToString("0.######", CultureInfo.InvariantCulture);
                _fault.Text = _result.HasFaultStatus
                    ? _result.FaultCode.ToString(CultureInfo.InvariantCulture) + " - " + _result.FaultDescription
                    : _result.FaultDescription;
                _frames.Text = BuildText(_result);
                _status.Text = "读取完成；同样的实际TX/RX帧和解析值已写入主LOG。";
            }
            catch (Exception ex)
            {
                _result = null;
                _position.Text = _velocity.Text = _fault.Text = "读取失败";
                _frames.Text = ex.Message;
                _status.Text = "读取失败：" + ex.Message;
            }
            finally { _refresh.IsEnabled = true; }
        }

        private void CopyClick(object sender, RoutedEventArgs e)
        {
            if (_result != null) Clipboard.SetText(BuildText(_result));
        }

        private static string BuildText(ProductResolverData result)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("产品内部旋变实际值");
            text.AppendLine("位置 = " + result.PositionDegrees.ToString("0.######", CultureInfo.InvariantCulture) + " °");
            text.AppendLine("速度/频率 = " + result.VelocityFrequency.ToString("0.######", CultureInfo.InvariantCulture));
            text.AppendLine("产品型号 = " + result.Model);
            text.AppendLine("故障状态 = " + (result.HasFaultStatus ? result.FaultCode.ToString(CultureInfo.InvariantCulture) + " - " + result.FaultDescription : result.FaultDescription));
            text.AppendLine();
            text.AppendLine(string.Format("1. 查找FT_Resolver_Data表地址（FirstAddress + 0x{0:X2}）", result.AddressOffset));
            text.AppendLine("TX 0x7EE: " + result.AddressRequestText);
            text.AppendLine("RX 0x7EF: " + result.PointerResponseText + "  -> 表地址 " + result.TableAddressText);
            text.AppendLine("2. 读取旋变表" + result.DataLength.ToString(CultureInfo.InvariantCulture) + "字节");
            text.AppendLine("TX 0x7EE: " + result.DataRequestText);
            text.AppendLine("RX 0x7EF（合并数据）: " + result.RawDataText);
            return text.ToString();
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, double width)
        {
            Button button = new Button { Content = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) };
            button.Click += handler;
            return button;
        }
    }
}
