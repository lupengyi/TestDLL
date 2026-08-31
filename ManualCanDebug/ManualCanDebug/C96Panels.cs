using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ManualCanDebug.Core;
using Microsoft.Win32;

namespace ManualCanDebug
{
    internal sealed class C96ReadPanel : UserControl
    {
        private const int AutoReceiveIntervalMs = 500;

        private readonly IAdvancedCanService _service;
        private readonly Action _selectProduct;
        private readonly ProductModel _productModel;
        private readonly string _productName;
        private readonly DataGrid _inputs;
        private readonly TextBox _summary;
        private readonly TextBlock _status;
        private readonly DispatcherTimer _autoReceiveTimer;
        private readonly Button _autoReceiveStartButton;
        private readonly Button _autoReceiveStopButton;
        private List<C96InputSignalResult> _inputResults = new List<C96InputSignalResult>();
        private bool _autoReceiveBusy;

        public C96ReadPanel(IAdvancedCanService service, Action selectProduct, ProductModel productModel)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _selectProduct = selectProduct ?? throw new ArgumentNullException(nameof(selectProduct));
            if (productModel != ProductModel.C92 && productModel != ProductModel.C96) throw new ArgumentOutOfRangeException(nameof(productModel));
            _productModel = productModel;
            _productName = productModel.ToString();
            Grid root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            WrapPanel buttons = new WrapPanel();
            buttons.Children.Add(MakeButton("切换到 " + _productName, (s, e) => _selectProduct(), 120));
            buttons.Children.Add(MakeButton("读取全部输入信号", async (s, e) => await ReadInputsAsync(), 150));
            buttons.Children.Add(MakeButton("读取 TM1", async (s, e) => await ReadDriveAsync(C96Drive.TM1), 110));
            buttons.Children.Add(MakeButton("读取 TM2", async (s, e) => await ReadDriveAsync(C96Drive.TM2), 110));
            buttons.Children.Add(MakeButton("一次读取双驱", async (s, e) => await ReadBothAsync(), 130));
            buttons.Children.Add(MakeButton("复制本页数据", CopyAll, 120));
            buttons.Children.Add(MakeButton("导出输入信号CSV", async (s, e) => await ExportInputsAsync(), 150));
            _autoReceiveStartButton = MakeButton("自动接收", (s, e) => StartAutoReceive(), 110);
            _autoReceiveStopButton = MakeButton("停止自动接收", (s, e) => StopAutoReceive("自动接收已停止。"), 130);
            buttons.Children.Add(_autoReceiveStartButton);
            buttons.Children.Add(_autoReceiveStopButton);
            root.Children.Add(buttons);

            _inputs = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended };
            AddColumn("表", "TableName", 150); AddColumn("表偏移", "TableAddress", 70); AddColumn("字段偏移", "SignalOffset", 75);
            AddColumn("信号", "SignalName", 270); AddColumn("端口", "PortName", 110); AddColumn("类型", "ValueType", 80);
            AddColumn("实际值", "ValueText", 120); AddColumn("RAW", "RawBytes", 150); AddColumn("说明", "Interpretation", 170);
            Grid.SetRow(_inputs, 1); root.Children.Add(_inputs);

            GroupBox summaryGroup = new GroupBox { Header = _productName + " 双驱解析结果（TM1 / TM2）", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(6) };
            _summary = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") };
            summaryGroup.Content = _summary; Grid.SetRow(summaryGroup, 2); root.Children.Add(summaryGroup);
            _status = new TextBlock { Text = "先选择 " + _productName + " 并执行 DUT 通信初始化。原始 TX/RX 同时写入主界面 LOG。", Margin = new Thickness(4, 8, 4, 0), Foreground = Brushes.DimGray };
            Grid.SetRow(_status, 3); root.Children.Add(_status); Content = root;

            _autoReceiveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoReceiveIntervalMs) };
            _autoReceiveTimer.Tick += async (s, e) => await AutoReceiveTickAsync();
            Unloaded += (s, e) => StopAutoReceive(null);
            UpdateAutoReceiveButtons(false);
        }

        private async Task ReadInputsAsync()
        {
            if (!RequireProduct()) return;
            _status.Text = "正在读取 " + _productName + " 六张当前值输入表...";
            try { _inputResults = (await Task.Run(_service.ReadAllC96InputTables)).ToList(); _inputs.ItemsSource = _inputResults; _status.Text = "读取完成：6 张表，" + _inputResults.Count + " 个解析信号。"; }
            catch (Exception ex) { ShowError(ex); }
        }

        private async Task ReadDriveAsync(C96Drive drive)
        {
            if (!RequireProduct()) return;
            _status.Text = "正在读取 " + _productName + " " + drive + "...";
            try { _summary.Text = FormatSnapshot(await Task.Run(() => _service.ReadC96DriveSnapshot(drive))); _status.Text = drive + " 读取完成。"; }
            catch (Exception ex) { ShowError(ex); }
        }

        private async Task ReadBothAsync()
        {
            if (!RequireProduct()) return;
            _status.Text = "正在依次读取 TM1 和 TM2...";
            try
            {
                C96DriveSnapshot[] values = await Task.Run(() => new[] { _service.ReadC96DriveSnapshot(C96Drive.TM1), _service.ReadC96DriveSnapshot(C96Drive.TM2) });
                _summary.Text = FormatSnapshot(values[0]) + Environment.NewLine + Environment.NewLine + FormatSnapshot(values[1]);
                _status.Text = _productName + " 双驱读取完成。";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void StartAutoReceive()
        {
            if (!RequireProduct()) return;
            if (_autoReceiveTimer.IsEnabled) return;
            _autoReceiveTimer.Start();
            UpdateAutoReceiveButtons(true);
            _status.Text = _productName + " 自动接收已启动（每 " + AutoReceiveIntervalMs + "ms 读取全部输入信号）。";
            _ = AutoReceiveTickAsync();
        }

        private void StopAutoReceive(string statusText)
        {
            _autoReceiveTimer.Stop();
            UpdateAutoReceiveButtons(false);
            if (!string.IsNullOrEmpty(statusText)) _status.Text = statusText;
        }

        public void StopAllActivities() { StopAutoReceive("旧双驱自动接收已停止；CAN资源已交还MainTest。"); }

        private async Task AutoReceiveTickAsync()
        {
            if (_autoReceiveBusy || !_autoReceiveTimer.IsEnabled) return;
            if (_service.ProductProfile.Model != _productModel)
            {
                StopAutoReceive("产品型号已切换，自动接收已停止。");
                return;
            }

            _autoReceiveBusy = true;
            try
            {
                List<C96InputSignalResult> results = (await Task.Run(_service.ReadAllC96InputTables)).ToList();
                if (!_autoReceiveTimer.IsEnabled) return;
                _inputResults = results;
                _inputs.ItemsSource = _inputResults;
                _status.Text = _productName + " 自动接收中… 最近刷新 " + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + "（6 张表，" + _inputResults.Count + " 个信号）";
            }
            catch (Exception ex)
            {
                StopAutoReceive("自动接收失败并已停止：" + ex.Message);
            }
            finally
            {
                _autoReceiveBusy = false;
            }
        }

        private void UpdateAutoReceiveButtons(bool active)
        {
            _autoReceiveStartButton.Content = active ? "自动接收中 ✓" : "自动接收";
            _autoReceiveStartButton.Background = active ? Brushes.LightGreen : SystemColors.ControlBrush;
            _autoReceiveStartButton.BorderBrush = active ? Brushes.ForestGreen : SystemColors.ActiveBorderBrush;
            _autoReceiveStartButton.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            _autoReceiveStartButton.IsEnabled = !active;

            _autoReceiveStopButton.Content = active ? "停止自动接收" : "已停止 ✓";
            _autoReceiveStopButton.Background = active ? SystemColors.ControlBrush : Brushes.Moccasin;
            _autoReceiveStopButton.BorderBrush = active ? SystemColors.ActiveBorderBrush : Brushes.DarkOrange;
            _autoReceiveStopButton.FontWeight = active ? FontWeights.Normal : FontWeights.Bold;
            _autoReceiveStopButton.IsEnabled = active;
        }

        private bool RequireProduct()
        {
            if (_service.ProductProfile.Model == _productModel) return true;
            MessageBox.Show(Window.GetWindow(this), "请先切换到 " + _productName + "，并重新执行 DUT 通信初始化。", _productName + " 读取", MessageBoxButton.OK, MessageBoxImage.Information); return false;
        }

        private void ShowError(Exception ex) { _status.Text = "读取失败：" + ex.Message; MessageBox.Show(Window.GetWindow(this), ex.Message, _productName + " 读取", MessageBoxButton.OK, MessageBoxImage.Error); }

        private void CopyAll(object sender, RoutedEventArgs e)
        {
            StringBuilder text = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_summary.Text)) text.AppendLine(_summary.Text).AppendLine();
            text.AppendLine("表\t表偏移\t字段偏移\t信号\t端口\t类型\t实际值\tRAW\t说明");
            foreach (C96InputSignalResult item in _inputResults) text.AppendLine(string.Join("\t", item.TableName, item.TableAddress, item.SignalOffset.ToString(CultureInfo.InvariantCulture), item.SignalName, item.PortName, item.ValueType, item.ValueText, item.RawBytes, item.Interpretation));
            Clipboard.SetText(text.ToString());
        }

        private async Task ExportInputsAsync()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "导出" + _productName + "输入信号表",
                Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = _productName + "_输入信号_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                _status.Text = "已取消导出。";
                return;
            }

            StringBuilder csv = new StringBuilder();
            AppendCsvRow(csv, "表", "表偏移", "字段偏移", "信号", "端口", "类型", "实际值", "RAW", "说明");
            foreach (C96InputSignalResult item in _inputResults)
                AppendCsvRow(csv, item.TableName, item.TableAddress, item.SignalOffset.ToString(CultureInfo.InvariantCulture),
                    item.SignalName, item.PortName, item.ValueType, item.ValueText, item.RawBytes, item.Interpretation);

            string content = csv.ToString();
            try
            {
                await Task.Run(() => File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true)));
                _status.Text = "已导出 " + _inputResults.Count + " 个输入信号：" + dialog.FileName;
            }
            catch (Exception ex)
            {
                _status.Text = "导出失败：" + ex.Message;
                MessageBox.Show(Window.GetWindow(this), ex.Message, _productName + " 导出", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void AppendCsvRow(StringBuilder csv, params string[] values)
        {
            csv.AppendLine(string.Join(",", values.Select(CsvCell)));
        }

        private static string CsvCell(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"").Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private string FormatSnapshot(C96DriveSnapshot value)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("========== " + _productName + " " + value.Drive + " ==========");
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Resolver: speed={0:0.###} rpm; angle={1:0.###} deg; fault={2}-{3}; RAW={4}", value.Resolver.SpeedRpm, value.Resolver.AngleDegrees, value.Resolver.FaultCode, value.Resolver.FaultDescription, value.Resolver.RawBytes));
            text.AppendLine("Motor Status: " + value.MotorStatus.Summary + "; RAW=" + value.MotorStatus.RawText);
            foreach (DutPhaseCurrent phase in value.Current.Phases) text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Current {0}: instant={1:0.###} A; min={2:0.###} A; max={3:0.###} A; calculated RMS={4:0.###} A", phase.Name, phase.Instantaneous, phase.Minimum, phase.Maximum, phase.Rms));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Current reported RMS={0:0.###} A; RAW={1}", value.Current.ReportedRms, value.Current.RawBytes));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "RPM: current={0:0.###}; max={1:0.###}; min={2:0.###}; RAW={3}", value.Rpm, value.RpmMaximum, value.RpmMinimum, value.RpmRaw));
            return text.ToString().TrimEnd();
        }

        private void AddColumn(string header, string property, double width) { _inputs.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width }); }
        private static Button MakeButton(string text, RoutedEventHandler handler, double width) { Button button = new Button { Content = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) }; button.Click += handler; return button; }
    }

    internal sealed class C96ControlPanel : UserControl
    {
        private readonly IAdvancedCanService _service; private readonly Action _selectProduct; private readonly ProductModel _productModel; private readonly string _productName; private readonly ComboBox _drive;
        private readonly TextBox _startCurrent, _targetCurrent, _stepCurrent, _holdSeconds, _outputFrequency, _rampMs, _baseFrequency, _speed, _voltage, _runInFrequency, _runInMaxTemp;
        private readonly ComboBox _mode, _expectedLoad; private readonly CheckBox _gate, _resetFaults, _speedEnable, _voltageEnable; private readonly TextBlock _status;

        public C96ControlPanel(IAdvancedCanService service, Action selectProduct, ProductModel productModel)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service)); _selectProduct = selectProduct ?? throw new ArgumentNullException(nameof(selectProduct));
            if (productModel != ProductModel.C92 && productModel != ProductModel.C96) throw new ArgumentOutOfRangeException(nameof(productModel));
            _productModel = productModel; _productName = productModel.ToString();
            StackPanel root = new StackPanel { Margin = new Thickness(10) };
            WrapPanel top = new WrapPanel(); top.Children.Add(MakeButton("切换到 " + _productName, (s, e) => _selectProduct(), 120)); top.Children.Add(MakeLabel("目标驱动："));
            _drive = new ComboBox { Width = 100, Margin = new Thickness(4), ItemsSource = new[] { C96Drive.TM1, C96Drive.TM2 }, SelectedIndex = 0 }; top.Children.Add(_drive); top.Children.Add(MakeLabel("所有控制只发给选中的驱动。")); root.Children.Add(Group(_productName + " 双驱目标", top));

            Grid p = new Grid(); for (int i = 0; i < 4; i++) p.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(155) : new GridLength(190) });
            _startCurrent = AddField(p, 0, 0, "起始电流 (A RMS)", "0"); _targetCurrent = AddField(p, 0, 1, "目标电流 (A RMS)", "0");
            _stepCurrent = AddField(p, 1, 0, "步进电流 (A Peak)", "20"); _holdSeconds = AddField(p, 1, 1, "最大值保持 (s)", "10");
            _outputFrequency = AddField(p, 2, 0, "输出频率 (Hz)", "60"); _rampMs = AddField(p, 2, 1, "开关斜坡时间 (ms)", "50");
            _baseFrequency = AddField(p, 3, 0, "电机基频 (Hz)", "10000"); _speed = AddField(p, 3, 1, "速度设定 (rpm)", "0"); _voltage = AddField(p, 4, 0, "电压设定 (V)", "0");
            AddRow(p, 5); TextBlock modeLabel = MakeLabel("Motor Control Mode"); Grid.SetRow(modeLabel, 5); p.Children.Add(modeLabel);
            _mode = new ComboBox { Margin = new Thickness(4), Width = 180, ItemsSource = new[] { "0 - Default", "1 - Open Loop", "2 - Current Loop", "3 - Open Loop Sequence", "4 - Current Loop Sequence" }, SelectedIndex = 4 }; Grid.SetRow(_mode, 5); Grid.SetColumn(_mode, 1); p.Children.Add(_mode);
            StackPanel flags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _gate = Check("Gate Enable", true); _resetFaults = Check("Reset Motor Faults", false); _speedEnable = Check("Speed Enable", false); _voltageEnable = Check("Voltage Enable", false);
            flags.Children.Add(_gate); flags.Children.Add(_resetFaults); flags.Children.Add(_speedEnable); flags.Children.Add(_voltageEnable); Grid.SetRow(flags, 5); Grid.SetColumn(flags, 2); Grid.SetColumnSpan(flags, 2); p.Children.Add(flags);
            root.Children.Add(Group("Motor Control 39 字节结构（NewData 固定 0xFF）", p));

            WrapPanel send = new WrapPanel(); send.Children.Add(MakeButton("发送所选驱动控制", async (s, e) => await SendControlAsync(false), 170)); send.Children.Add(MakeButton("所选驱动安全停机", async (s, e) => await SendControlAsync(true), 170)); root.Children.Add(send);
            WrapPanel helpers = new WrapPanel(); helpers.Children.Add(MakeLabel("Expected Load：")); _expectedLoad = new ComboBox { Width = 150, Margin = new Thickness(4), ItemsSource = new[] { "0 - Inductor", "1 - EME", "2 - Motor" }, SelectedIndex = 0 }; helpers.Children.Add(_expectedLoad);
            helpers.Children.Add(MakeButton("写入 Load", async (s, e) => await WriteExpectedLoadAsync(), 110)); helpers.Children.Add(MakeButton("Auto PWM 开", async (s, e) => await SetAutoPwmAsync(true), 110)); helpers.Children.Add(MakeButton("Auto PWM 关", async (s, e) => await SetAutoPwmAsync(false), 110)); root.Children.Add(Group("所选驱动辅助控制", helpers));
            WrapPanel uvReset = new WrapPanel();
            uvReset.Children.Add(MakeButton("清所选驱动 UVLO", async (s, e) => await PulseUvFaultResetAsync(false), 150));
            uvReset.Children.Add(MakeButton("清所选驱动 UVLO+UVUP", async (s, e) => await PulseUvFaultResetAsync(true), 180));
            uvReset.Children.Add(MakeButton("清所选驱动硬件 OC", async (s, e) => await PulseOcFaultResetAsync(), 170));
            uvReset.Children.Add(MakeButton("清共享 Bus HW OV", async (s, e) => await PulseBusOverVoltageResetAsync(), 165));
            uvReset.Children.Add(MakeButton("安全清 OC+OV+UV", async (s, e) => await PulseAllHardwareFaultResetsAsync(), 170));
            uvReset.Children.Add(MakeLabel("写 FT_Enables：对应FLTRST端口拉高100ms再拉低；不使用FLTOVRD故障旁路。"));
            root.Children.Add(Group("所选驱动硬件故障清除（C92/C96）", uvReset));
            WrapPanel runIn = new WrapPanel(); runIn.Children.Add(MakeLabel("Run-in 频率 (Hz)")); _runInFrequency = Box("10000", 90); runIn.Children.Add(_runInFrequency); runIn.Children.Add(MakeLabel("最高温度 (℃)")); _runInMaxTemp = Box("110", 90); runIn.Children.Add(_runInMaxTemp); runIn.Children.Add(MakeButton("启动 Run-in", async (s, e) => await SendRunInAsync(true), 120)); runIn.Children.Add(MakeButton("停止 Run-in", async (s, e) => await SendRunInAsync(false), 120)); root.Children.Add(Group("所选驱动 Phase Current Run-in", runIn));
            _status = new TextBlock { Text = "先切换 " + _productName + " 并执行 DUT 通信初始化。所有 TX/RX 与参数写入主界面 LOG。", Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray }; root.Children.Add(_status);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private C96Drive SelectedDrive { get { return (C96Drive)_drive.SelectedItem; } }
        private async Task SendControlAsync(bool stop)
        {
            if (!RequireProduct()) return;
            try
            {
                C96Drive drive = SelectedDrive;
                C96MotorControlCommand command = stop ? new C96MotorControlCommand(0, 0, 0, 0, 0, 0, ParseUInt16(_rampMs.Text), ParseUInt16(_baseFrequency.Text), false, false, false, 0, false, 0)
                    : new C96MotorControlCommand(ParseFloat(_startCurrent.Text), ParseFloat(_targetCurrent.Text), ParseFloat(_stepCurrent.Text), ParseFloat(_holdSeconds.Text), ParseFloat(_outputFrequency.Text), (byte)_mode.SelectedIndex, ParseUInt16(_rampMs.Text), ParseUInt16(_baseFrequency.Text), _gate.IsChecked == true, _resetFaults.IsChecked == true, _speedEnable.IsChecked == true, ParseFloat(_speed.Text), _voltageEnable.IsChecked == true, ParseFloat(_voltage.Text));
                if (!stop && command.GateEnable && Math.Abs(command.TargetCurrentRms) > 0.001 && MessageBox.Show(Window.GetWindow(this), "即将向 " + _productName + " " + drive + " 发送带 Gate Enable 的非零电流命令。确认继续？", _productName + " 双驱控制", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                await RunAsync(drive, stop ? "安全停机" : "Motor Control", () => _service.SendC96MotorControl(drive, command));
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async Task WriteExpectedLoadAsync()
        {
            if (!RequireProduct()) return;
            C96Drive drive = SelectedDrive;
            byte loadType = (byte)_expectedLoad.SelectedIndex;
            await RunAsync(drive, "Expected Load", () => _service.SetC96ExpectedLoad(drive, loadType));
        }

        private async Task SetAutoPwmAsync(bool enabled)
        {
            if (!RequireProduct()) return;
            C96Drive drive = SelectedDrive;
            await RunAsync(drive, "Auto PWM", () => _service.SetC96AutoPwm(drive, enabled));
        }

        private async Task SendRunInAsync(bool activate)
        {
            if (!RequireProduct()) return;
            try
            {
                C96Drive drive = SelectedDrive;
                ushort frequencyHz = ParseUInt16(_runInFrequency.Text);
                float maximumTemperature = ParseFloat(_runInMaxTemp.Text);
                await RunAsync(drive, "Run-in", () => _service.SetC96RunIn(drive, frequencyHz, maximumTemperature, activate));
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async Task PulseUvFaultResetAsync(bool includeUpper)
        {
            if (!RequireProduct()) return;
            C96Drive drive = SelectedDrive;
            string action = includeUpper ? "UVLO+UVUP 清故障" : "UVLO 清故障";
            if (!ConfirmHardwareReset(drive, action)) return;
            await RunAsync(drive, action, () => _service.PulseC96UvFaultReset(drive, includeUpper));
        }

        private async Task PulseOcFaultResetAsync()
        {
            if (!RequireProduct()) return;
            C96Drive drive = SelectedDrive;
            const string action = "硬件OC清故障";
            if (!ConfirmHardwareReset(drive, action)) return;
            await RunAsync(drive, action, () => _service.PulseC96OverCurrentFaultReset(drive));
        }

        private async Task PulseBusOverVoltageResetAsync()
        {
            if (!RequireProduct()) return;
            C96Drive drive = SelectedDrive;
            const string action = "共享Bus HW OV清故障";
            if (!ConfirmHardwareReset(drive, action)) return;
            await RunAsync(drive, action, _service.PulseC96BusHardwareOverVoltageFaultReset);
        }

        private async Task PulseAllHardwareFaultResetsAsync()
        {
            if (!RequireProduct()) return;
            C96Drive drive = SelectedDrive;
            const string action = "OC+Bus HW OV+UV组合清故障";
            if (!ConfirmHardwareReset(drive, action)) return;
            await RunAsync(drive, action, () => _service.PulseC96AllHardwareFaultResets(drive));
        }

        private bool ConfirmHardwareReset(C96Drive drive, string action)
        {
            string warning = "即将向 " + _productName + " " + drive + " 的FT_Enables发送" + action
                + "脉冲（High 100ms后Low）。\n请确认目标电流=0、Gate Enable关闭、速度/电压使能关闭，且母线电压处于设备允许复位范围。\n\n确认继续？";
            return MessageBox.Show(Window.GetWindow(this), warning, _productName + " 硬件故障清除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private async Task RunAsync(C96Drive drive, string action, Action work)
        {
            if (!RequireProduct()) return;
            _status.Text = "正在执行 " + drive + " " + action + "...";
            try
            {
                await Task.Run(work);
                _status.Text = drive + " " + action + " 已发送；请查看 LOG。";
            }
            catch (Exception ex) { ShowError(ex); }
        }
        private bool RequireProduct() { if (_service.ProductProfile.Model == _productModel) return true; MessageBox.Show(Window.GetWindow(this), "请先切换到 " + _productName + "，并重新执行 DUT 通信初始化。", _productName + " 控制", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
        private void ShowError(Exception ex) { _status.Text = "执行失败：" + ex.Message; MessageBox.Show(Window.GetWindow(this), ex.Message, _productName + " 控制", MessageBoxButton.OK, MessageBoxImage.Error); }
        private static float ParseFloat(string text) { float value; if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) throw new FormatException("参数不是有效数字：" + text); return value; }
        private static ushort ParseUInt16(string text) { ushort value; if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new FormatException("参数不是 0~65535 的整数：" + text); return value; }
        private static TextBox AddField(Grid grid, int row, int pair, string label, string value) { AddRow(grid, row); TextBlock name = MakeLabel(label); Grid.SetRow(name, row); Grid.SetColumn(name, pair * 2); grid.Children.Add(name); TextBox box = Box(value, 170); Grid.SetRow(box, row); Grid.SetColumn(box, pair * 2 + 1); grid.Children.Add(box); return box; }
        private static void AddRow(Grid grid, int row) { while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        private static GroupBox Group(string header, UIElement content) { return new GroupBox { Header = header, Margin = new Thickness(4), Padding = new Thickness(6), Content = content }; }
        private static TextBlock MakeLabel(string text) { return new TextBlock { Text = text, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center }; }
        private static TextBox Box(string text, double width) { return new TextBox { Text = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(4) }; }
        private static CheckBox Check(string text, bool value) { return new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(7, 4, 7, 4), VerticalAlignment = VerticalAlignment.Center }; }
        private static Button MakeButton(string text, RoutedEventHandler handler, double width) { Button button = new Button { Content = text, Width = width, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) }; button.Click += handler; return button; }
    }
}
