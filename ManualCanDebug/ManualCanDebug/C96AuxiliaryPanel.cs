using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    public sealed class C96AuxiliaryPanel : UserControl
    {
        private readonly IAdvancedCanService _service;
        private readonly Func<bool> _ensureAuxiliaryProduct;
        private readonly DispatcherTimer _oilTimer;
        private readonly DispatcherTimer _airTimer;
        private readonly DispatcherTimer _pduTimer;
        private readonly DispatcherTimer _receiveTimer;
        private readonly ObservableCollection<AuxiliarySignalRow> _rows = new ObservableCollection<AuxiliarySignalRow>();
        private readonly Dictionary<string, AuxiliarySignalRow> _rowMap = new Dictionary<string, AuxiliarySignalRow>();
        private readonly ObservableCollection<AuxiliaryTestItemRow> _testRows = new ObservableCollection<AuxiliaryTestItemRow>();
        private readonly Dictionary<string, List<AuxiliaryTestItemRow>> _testRowsBySignal = new Dictionary<string, List<AuxiliaryTestItemRow>>();
        private readonly Dictionary<string, DbcDecodedSignal> _latestSignals = new Dictionary<string, DbcDecodedSignal>();
        private readonly Dictionary<string, string> _latestRawFrames = new Dictionary<string, string>();
        private readonly Dictionary<DispatcherTimer, TimerButtonState> _timerButtonStates = new Dictionary<DispatcherTimer, TimerButtonState>();
        private readonly SemaphoreSlim _oilGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _airGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _pduGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _receiveGate = new SemaphoreSlim(1, 1);
        private int _pduHeartbeat;

        private ComboBox _dcdcCommand, _oilCommand, _oilMode;
        private CheckBox _oilReset;
        private TextBox _oilCurrent, _oilFrequency, _oilVoltage;
        private ComboBox _airCommand, _airMode;
        private CheckBox _airReset;
        private TextBox _airCurrent, _airFrequency, _airVoltage;
        private ComboBox _sharedRelay, _mainHighVoltage, _bodywork, _chargingRelay;
        private TextBlock _status;

        public C96AuxiliaryPanel(IAdvancedCanService service, Func<bool> ensureAuxiliaryProduct)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ensureAuxiliaryProduct = ensureAuxiliaryProduct ?? throw new ArgumentNullException(nameof(ensureAuxiliaryProduct));
            _oilTimer = Timer(100, null);
            _airTimer = Timer(100, null);
            _pduTimer = Timer(100, null);
            _receiveTimer = Timer(250, async (s, e) => await ReceiveAsync());
            InitializeTestItems();
            BuildUi();
            Unloaded += (s, e) => StopAllTimers();
        }

        private void BuildUi()
        {
            Grid root = new Grid { Margin = new Thickness(5) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _status = new TextBlock
            {
                Text = "C95/C96共用通道0辅驱DBC。控制帧均为扩展帧；启动前请确认高压、负载和安全条件。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5)
            };
            Grid.SetRow(_status, 0);
            root.Children.Add(_status);

            GroupBox oilGroup = Group("DCDC / 油泵控制  0x0C079AA7（DBC，周期100ms）", BuildOilPanel());
            Grid.SetRow(oilGroup, 1);
            root.Children.Add(oilGroup);

            GroupBox airGroup = Group("气泵辅驱控制  0x0C089AA7（DBC，周期100ms）", BuildAirPanel());
            Grid.SetRow(airGroup, 2);
            root.Children.Add(airGroup);

            GroupBox pduGroup = Group("PDU继电器控制  0x0CF2503B（DBC，周期100ms）", BuildPduPanel());
            Grid.SetRow(pduGroup, 3);
            root.Children.Add(pduGroup);

            GroupBox receiveGroup = Group("DCDC / 油泵 / 气泵 / PDU反馈（DBC解析）", BuildReceivePanel());
            Grid.SetRow(receiveGroup, 4);
            root.Children.Add(receiveGroup);
            Content = root;
        }

        private UIElement BuildOilPanel()
        {
            WrapPanel row = new WrapPanel();
            row.Children.Add(Label("DCDC："));
            _dcdcCommand = StartStopCombo(); row.Children.Add(_dcdcCommand);
            row.Children.Add(Label("油泵："));
            _oilCommand = StartStopCombo(); row.Children.Add(_oilCommand);
            _oilReset = Check("复位", false); row.Children.Add(_oilReset);
            row.Children.Add(Label("模式："));
            _oilMode = ModeCombo(); row.Children.Add(_oilMode);
            row.Children.Add(Label("额定电流(A)："));
            _oilCurrent = Box("0", 70); row.Children.Add(_oilCurrent);
            row.Children.Add(Label("频率(Hz)："));
            _oilFrequency = Box("0", 80); row.Children.Add(_oilFrequency);
            row.Children.Add(Label("VF电压(%)："));
            _oilVoltage = Box("0", 80); row.Children.Add(_oilVoltage);
            row.Children.Add(Hint("资料参考：额定电流17A、反馈频率示例50Hz；VF电压和完整启动工况未说明。"));
            row.Children.Add(ActionButton("发送一次", SendOilAsync));
            AddTimerButtons(row, _oilTimer, "开始周期发送", "周期发送中 ✓", "DCDC/油泵100ms周期发送已启动", "DCDC/油泵周期发送已停止");
            return row;
        }

        private UIElement BuildAirPanel()
        {
            WrapPanel row = new WrapPanel();
            row.Children.Add(Label("气泵："));
            _airCommand = StartStopCombo(); row.Children.Add(_airCommand);
            _airReset = Check("复位", false); row.Children.Add(_airReset);
            row.Children.Add(Label("模式："));
            _airMode = ModeCombo(); row.Children.Add(_airMode);
            row.Children.Add(Label("额定电流(A)："));
            _airCurrent = Box("0", 70); row.Children.Add(_airCurrent);
            row.Children.Add(Label("频率(Hz)："));
            _airFrequency = Box("0", 80); row.Children.Add(_airFrequency);
            row.Children.Add(Label("VF电压(%)："));
            _airVoltage = Box("0", 80); row.Children.Add(_airVoltage);
            row.Children.Add(Hint("资料参考：额定电流17A、反馈频率示例50Hz；VF电压和完整启动工况未说明。"));
            row.Children.Add(ActionButton("发送一次", SendAirAsync));
            AddTimerButtons(row, _airTimer, "开始周期发送", "周期发送中 ✓", "气泵100ms周期发送已启动", "气泵周期发送已停止");
            return row;
        }

        private UIElement BuildPduPanel()
        {
            WrapPanel row = new WrapPanel();
            row.Children.Add(Label("共享继电器："));
            _sharedRelay = RelayCombo(); row.Children.Add(_sharedRelay);
            row.Children.Add(Label("主驱高压："));
            _mainHighVoltage = PowerCombo(); row.Children.Add(_mainHighVoltage);
            row.Children.Add(Label("上装高压："));
            _bodywork = PowerCombo(); row.Children.Add(_bodywork);
            row.Children.Add(Label("充电继电器："));
            _chargingRelay = RelayCombo(); row.Children.Add(_chargingRelay);
            row.Children.Add(ActionButton("单帧发送（仅验证报文）", SendPduAsync));
            AddTimerButtons(row, _pduTimer, "开始100ms周期控制", "PDU周期控制中 ✓", "PDU 100ms周期发送已启动；继电器控制请保持周期发送", "PDU周期发送已停止");
            row.Children.Add(new TextBlock { Text = "继电器实际控制请使用100ms周期控制；单帧可能在PDU确认前超时。", Foreground = Brushes.DarkOrange, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 6, 4, 4) });
            return row;
        }

        private UIElement BuildReceivePanel()
        {
            DockPanel panel = new DockPanel();
            WrapPanel buttons = new WrapPanel();
            buttons.Children.Add(ActionButton("读取当前反馈", ReceiveAsync));
            AddTimerButtons(buttons, _receiveTimer, "开始自动接收", "自动接收中 ✓", "DBC自动接收已启动（250ms刷新）", "DBC自动接收已停止");
            buttons.Children.Add(ActionButton("清空解析表", () =>
            {
                ClearReceivedValues();
                return Task.CompletedTask;
            }));
            buttons.Children.Add(ActionButton("导出测试项CSV", ExportTestItemsAsync));
            buttons.Children.Add(ActionButton("导出DBC全部信号CSV", ExportAllSignalsAsync));
            DockPanel.SetDock(buttons, Dock.Top);
            panel.Children.Add(buttons);

            TabControl tabs = new TabControl { Margin = new Thickness(3) };

            DataGrid testGrid = CreateDataGrid(_testRows);
            testGrid.Columns.Add(Column("分组", "Section", 105));
            testGrid.Columns.Add(Column("测试项", "TestItem", 250));
            testGrid.Columns.Add(Column("实际值", "Value", 125));
            testGrid.Columns.Add(Column("单位", "Unit", 65));
            testGrid.Columns.Add(Column("状态/说明", "Description", 220));
            testGrid.Columns.Add(Column("更新时间", "Time", 95));
            testGrid.Columns.Add(Column("DBC来源", "Source", 260));
            testGrid.Columns.Add(Column("RAW报文", "Raw", 210));
            tabs.Items.Add(new TabItem { Header = "按测试项实时值（不判Limit）", Content = testGrid });

            DataGrid grid = CreateDataGrid(_rows);
            grid.Columns.Add(Column("时间", "Time", 90));
            grid.Columns.Add(Column("报文", "Message", 210));
            grid.Columns.Add(Column("ID", "Id", 105));
            grid.Columns.Add(Column("信号", "Signal", 220));
            grid.Columns.Add(Column("实际值", "Value", 100));
            grid.Columns.Add(Column("单位", "Unit", 65));
            grid.Columns.Add(Column("状态/枚举", "Description", 180));
            grid.Columns.Add(Column("RAW报文", "Raw", 210));
            tabs.Items.Add(new TabItem { Header = "DBC全部信号", Content = grid });
            panel.Children.Add(tabs);
            return panel;
        }

        private static DataGrid CreateDataGrid(System.Collections.IEnumerable items)
        {
            return new DataGrid
            {
                ItemsSource = items,
                IsReadOnly = true,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
                Margin = new Thickness(3)
            };
        }

        private void InitializeTestItems()
        {
            AddDerivedTestItem("DCDC", "DCDC软件版本（DBC版本字节）", "DERIVED/DCDC_VERSION", "0x0C08A79B / SwVer1~3", "协议未提供软件型号字符串");
            AddTestItem("DCDC", "DCDC输入电压", "ACU1_DCDC_Feedback1/DCDC_InVoltage");
            AddTestItem("DCDC", "DCDC模块输出电压（停机/启动/带载）", "ACU1_DCDC_Feedback1/DCDC_OutVoltage");
            AddTestItem("DCDC", "DCDC模块输出电流（停机/启动/带载）", "ACU1_DCDC_Feedback1/DCDC_OutCurrent");
            AddTestItem("DCDC", "DCDC散热器温度", "ACU1_DCDC_Feedback1/DCDC_HeatSinkTemp");
            AddTestItem("DCDC", "DCDC状态及故障", "ACU1_DCDC_Feedback1/DCDC_FaultCode");

            AddDerivedTestItem("油泵/辅驱1", "油泵软件型号", "DERIVED/OIL_MODEL", "0x0C09A79D / SwModelH+L", "按高低字节合并为十六进制型号");
            AddTestItem("油泵/辅驱1", "油泵软件版本", "ACU5_OilPump_Feedback3/OilPump_SwVer");
            AddTestItem("油泵/辅驱1", "油泵软件非标/调试版本", "ACU5_OilPump_Feedback3/OilPump_DebugVer");
            AddTestItem("油泵/辅驱1", "油泵V相模块温度（整机确认/带载后）", "ACU3_OilPump_Feedback1/OilPump_ModuleTemp");
            AddTestItem("油泵/辅驱1", "油泵额定功率", "ACU4_OilPump_Feedback2/OilPump_RatedPower");
            AddTestItem("油泵/辅驱1", "油泵额定电流", "ACU4_OilPump_Feedback2/OilPump_RatedCurr");
            AddTestItem("油泵/辅驱1", "油泵输出频率", "ACU4_OilPump_Feedback2/OilPump_OutFreq");
            AddTestItem("油泵/辅驱1", "油泵控制器测得母线电压", "ACU3_OilPump_Feedback1/OilPump_InVoltage");
            AddTestItem("油泵/辅驱1", "油泵输出电压", "ACU3_OilPump_Feedback1/OilPump_OutVoltage");
            AddTestItem("油泵/辅驱1", "油泵输出电流有效值", "ACU3_OilPump_Feedback1/OilPump_OutCurrent", "DBC没有U/V/W三相独立电流");
            AddTestItem("油泵/辅驱1", "油泵状态及故障", "ACU3_OilPump_Feedback1/OilPump_FaultCode");
            AddTestItem("油泵/辅驱1", "油泵内部故障码", "ACU4_OilPump_Feedback2/OilPump_InternalFault");
            AddTestItem("油泵/辅驱1", "油泵控制模式", "ACU4_OilPump_Feedback2/OilPump_CtrlMode");

            AddDerivedTestItem("气泵/辅驱2", "气泵软件型号", "DERIVED/AIR_MODEL", "0x0C0AA79F / SwModelH+L", "按高低字节合并为十六进制型号");
            AddTestItem("气泵/辅驱2", "气泵软件版本", "ACU8_AirPump_Feedback3/AirPump_SwVer");
            AddTestItem("气泵/辅驱2", "气泵软件非标/调试版本", "ACU8_AirPump_Feedback3/AirPump_DebugVer");
            AddTestItem("气泵/辅驱2", "气泵V相模块温度（整机确认/带载后）", "ACU6_AirPump_Feedback1/AirPump_ModuleTemp");
            AddTestItem("气泵/辅驱2", "气泵额定功率", "ACU7_AirPump_Feedback2/AirPump_RatedPower");
            AddTestItem("气泵/辅驱2", "气泵额定电流", "ACU7_AirPump_Feedback2/AirPump_RatedCurr");
            AddTestItem("气泵/辅驱2", "气泵输出频率", "ACU7_AirPump_Feedback2/AirPump_OutFreq");
            AddTestItem("气泵/辅驱2", "气泵控制器测得母线电压", "ACU6_AirPump_Feedback1/AirPump_InVoltage");
            AddTestItem("气泵/辅驱2", "气泵输出电压", "ACU6_AirPump_Feedback1/AirPump_OutVoltage");
            AddTestItem("气泵/辅驱2", "气泵输出电流有效值", "ACU6_AirPump_Feedback1/AirPump_OutCurrent", "DBC没有U/V/W三相独立电流");
            AddTestItem("气泵/辅驱2", "气泵状态及故障", "ACU6_AirPump_Feedback1/AirPump_FaultCode");
            AddTestItem("气泵/辅驱2", "气泵内部故障码", "ACU7_AirPump_Feedback2/AirPump_InternalFault");
            AddTestItem("气泵/辅驱2", "气泵控制模式", "ACU7_AirPump_Feedback2/AirPump_CtrlMode");

            AddTestItem("PDU电压", "主负吸合前前端/电池侧母线电压", "PDU_MONITORID/BUSVoltage");
            AddTestItem("PDU电压", "主驱接触器后端电压", "PDU_MONITORID/MotP_Volt");
            AddTestItem("PDU电压", "辅驱+DCDC共享回路后端电压", "PDU_MONITORID/LotP_Volt");
            AddTestItem("PDU电压", "上装回路后端电压", "PDU_MONITORID/FotP_Volt");
            AddTestItem("PDU电压", "G2电池空调回路后端电压", "PDU_MONITORID/K1_Volt1");
            AddTestItem("PDU电压", "G3电除霜回路后端电压", "PDU_MONITORID/K2_Volt2");
            AddTestItem("PDU电压", "G4燃电回路后端电压", "PDU_MONITORID/K3_Volt3");

            AddTestItem("PDU状态", "共享辅驱+DCDC接触器状态", "PDU_RelaySts/PDU_ShrRlySts");
            AddTestItem("PDU状态", "G2接触器状态", "PDU_RelaySts/PDU_G2RlySts");
            AddTestItem("PDU状态", "G3接触器状态", "PDU_RelaySts/PDU_G3RlySts");
            AddTestItem("PDU状态", "G4接触器状态", "PDU_RelaySts/PDU_G4RlySts");
            AddTestItem("PDU状态", "主正接触器状态", "PDU_RelaySts/PDU_PosRlySts");
            AddTestItem("PDU状态", "主预充接触器状态", "PDU_RelaySts/PDU_PosRlyPcgRlySts");
            AddTestItem("PDU状态", "上装主正接触器状态", "PDU_RelaySts/PDU_BodyworkRlySts");
            AddTestItem("PDU状态", "上装预充接触器状态", "PDU_RelaySts/PDU_BodyworkRlyPcgRlySts");

            AddTestItem("PDU自检", "低压上电完成", "PDU_MONITORID/PowerIniDelay");
            AddTestItem("PDU自检", "全部回路自检完成", "PDU_MONITORID/InitOver");
            AddTestItem("PDU自检", "主驱预充自检", "PDU_MONITORID/Init_FlagM0MPSuccess");
            AddTestItem("PDU自检", "辅驱+DCDC共享回路预充自检", "PDU_MONITORID/Init_FlagL0LPSuccess");
            AddTestItem("PDU自检", "上装预充自检", "PDU_MONITORID/Init_FlagF0FPSuccess");
            AddTestItem("PDU自检", "G2回路自检", "PDU_MONITORID/Init_FlagX1Success");
            AddTestItem("PDU自检", "G3回路自检", "PDU_MONITORID/Init_FlagX2Success");
            AddTestItem("PDU自检", "G4回路自检", "PDU_MONITORID/Init_FlagX3Success");
        }

        private void AddTestItem(string section, string testItem, string signalKey, string note = "")
        {
            AuxiliaryTestItemRow row = new AuxiliaryTestItemRow(section, testItem, signalKey, note);
            _testRows.Add(row);
            List<AuxiliaryTestItemRow> rows;
            if (!_testRowsBySignal.TryGetValue(signalKey, out rows))
            {
                rows = new List<AuxiliaryTestItemRow>();
                _testRowsBySignal.Add(signalKey, rows);
            }
            rows.Add(row);
        }

        private void AddDerivedTestItem(string section, string testItem, string derivedKey, string source, string note)
        {
            AuxiliaryTestItemRow row = new AuxiliaryTestItemRow(section, testItem, source, note);
            _testRows.Add(row);
            _testRowsBySignal[derivedKey] = new List<AuxiliaryTestItemRow> { row };
        }

        private async Task SendOilAsync()
        {
            Dictionary<string, double> values = BuildOilValues();
            await RunAsync(_oilGate, () => _service.SendAuxiliaryDbcMessage("VCU1_DCDC_OilPump_Cmd", values));
        }

        private Dictionary<string, double> BuildOilValues()
        {
            return new Dictionary<string, double>
            {
                { "DCDC_Start_Cmd", CommandValue(_dcdcCommand) },
                { "DCAC_Steer_Reset", _oilReset.IsChecked == true ? 1 : 0 },
                { "DCAC_Steer_CtrlMode", _oilMode.SelectedIndex },
                { "DCAC_Steer_Reserved", 0 },
                { "DCAC_Steer_Start_Cmd", CommandValue(_oilCommand) },
                { "DCAC_Steer_RatedCurr", Number(_oilCurrent, "油泵额定电流") },
                { "DCAC_Steer_FreqCmd", Number(_oilFrequency, "油泵频率") },
                { "DCAC_Steer_VF_Voltage", Number(_oilVoltage, "油泵VF电压") }
            };
        }

        private async Task SendAirAsync()
        {
            Dictionary<string, double> values = BuildAirValues();
            await RunAsync(_airGate, () => _service.SendAuxiliaryDbcMessage("VCU2_AirPump_Cmd", values));
        }

        private Dictionary<string, double> BuildAirValues()
        {
            return new Dictionary<string, double>
            {
                { "AirPump_Reserved_B1", 0xFF },
                { "DCAC_Air_Reset", _airReset.IsChecked == true ? 1 : 0 },
                { "DCAC_Air_CtrlMode", _airMode.SelectedIndex },
                { "DCAC_Air_Reserved", 0 },
                { "DCAC_Air_Start_Cmd", CommandValue(_airCommand) },
                { "DCAC_Air_RatedCurr", Number(_airCurrent, "气泵额定电流") },
                { "DCAC_Air_FreqCmd", Number(_airFrequency, "气泵频率") },
                { "DCAC_Air_VF_Voltage", Number(_airVoltage, "气泵VF电压") }
            };
        }

        private async Task SendPduAsync()
        {
            int sharedRelay = _sharedRelay.SelectedIndex;
            int mainHighVoltage = _mainHighVoltage.SelectedIndex;
            int bodywork = _bodywork.SelectedIndex;
            int chargingRelay = _chargingRelay.SelectedIndex;
            await RunAsync(_pduGate, () => SendPduSnapshot(sharedRelay, mainHighVoltage, bodywork, chargingRelay));
        }

        private void SendPduSnapshot(int sharedRelay, int mainHighVoltage, int bodywork, int chargingRelay)
        {
            _service.SendAuxiliaryDbcMessage("VCU_PDU", BuildPduValues(sharedRelay, mainHighVoltage, bodywork, chargingRelay));
        }

        private Dictionary<string, double> BuildPduValues(int sharedRelay, int mainHighVoltage, int bodywork, int chargingRelay)
        {
            int heartbeat = Interlocked.Increment(ref _pduHeartbeat) & 0xFF;
            return new Dictionary<string, double>
            {
                { "VCU_ShrRlyCtl", sharedRelay },
                { "VCU_G2_RlyCtl", 0 }, { "VCU_G3_RlyCtl", 0 }, { "VCU_G4_RlyCtl", 0 },
                { "VCU_HghVtgCnt", mainHighVoltage },
                { "VCU_Bodywork", bodywork },
                { "VCU_DCCpstRlyCtl", chargingRelay },
                { "VCU_HrtBt", heartbeat }
            };
        }

        private async Task ReceiveAsync()
        {
            if (!await _receiveGate.WaitAsync(0)) return;
            try
            {
                if (!_ensureAuxiliaryProduct()) return;
                IReadOnlyList<DbcDecodedFrame> frames = await Task.Run(() => _service.ReceiveAuxiliaryDbcFrames());
                foreach (DbcDecodedFrame frame in frames) Apply(frame);
                string pdu = PduRelaySummary(frames); _status.Text = frames.Count == 0 ? "本次没有收到DBC中定义的反馈帧。" : "本次收到并解析 " + frames.Count + " 帧；所有原始报文及解析值已写入LOG。" + (string.IsNullOrWhiteSpace(pdu) ? string.Empty : "  " + pdu);
            }
            catch (Exception ex)
            {
                StopAllTimers();
                _status.Text = "接收失败：" + ex.Message;
            }
            finally { _receiveGate.Release(); }
        }

        private async Task RunAsync(SemaphoreSlim gate, Action action)
        {
            if (!await gate.WaitAsync(0)) return;
            try
            {
                if (!_ensureAuxiliaryProduct()) return;
                await Task.Run(action);
                _status.Text = "发送完成；原始扩展帧已写入LOG。";
            }
            catch (Exception ex)
            {
                StopAllTimers();
                _status.Text = "发送失败：" + ex.Message;
            }
            finally { gate.Release(); }
        }

        private void Apply(DbcDecodedFrame frame)
        {
            string raw = HexDataParser.Format(frame.Frame.Data);
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            _latestRawFrames[frame.MessageName] = raw;
            foreach (DbcDecodedSignal signal in frame.Signals)
            {
                string key = frame.MessageName + "/" + signal.Name;
                _latestSignals[key] = signal;
                AuxiliarySignalRow row;
                if (!_rowMap.TryGetValue(key, out row))
                {
                    row = new AuxiliarySignalRow { Message = frame.MessageName, Id = "0x" + frame.Frame.Id.ToString("X8"), Signal = signal.Name };
                    _rowMap.Add(key, row);
                    _rows.Add(row);
                }
                row.Update(time, signal.Value.ToString("0.###", CultureInfo.InvariantCulture), signal.Unit,
                    Describe(signal), raw);
                UpdateTestItemRows(key, signal, time, raw);
            }
            UpdateDerivedTestItems(time);
        }

        private static string PduRelaySummary(IEnumerable<DbcDecodedFrame> frames)
        {
            DbcDecodedFrame frame = frames == null ? null : frames.LastOrDefault(value => value.MessageName == "PDU_RelaySts"); if (frame == null) return string.Empty; DbcDecodedSignal shared = frame.Signals.FirstOrDefault(value => value.Name == "PDU_ShrRlySts"), main = frame.Signals.FirstOrDefault(value => value.Name == "PDU_PosRlySts"), precharge = frame.Signals.FirstOrDefault(value => value.Name == "PDU_PosRlyPcgRlySts"); return "PDU反馈：共享继电器=" + RelayState(shared) + "，主正=" + RelayState(main) + "，主预充=" + RelayState(precharge) + "；RAW=" + HexDataParser.Format(frame.Frame.Data);
        }

        private static string RelayState(DbcDecodedSignal signal) { if (signal == null) return "无数据"; int value = (int)signal.RawValue; return value == 1 ? "已闭合" : value == 0 ? "已断开" : value == 2 ? "错误" : "无效"; }

        private void UpdateTestItemRows(string key, DbcDecodedSignal signal, string time, string raw)
        {
            List<AuxiliaryTestItemRow> rows;
            if (!_testRowsBySignal.TryGetValue(key, out rows)) return;
            string value = FormatTestValue(signal);
            string description = Describe(signal);
            if ((signal.Name == "OilPump_InVoltage" || signal.Name == "OilPump_OutVoltage" ||
                 signal.Name == "AirPump_InVoltage" || signal.Name == "AirPump_OutVoltage") && signal.RawValue == 0x2710)
            {
                value = "无效/未更新";
                description = JoinDescription("RAW=0x2710，物理值10000V无效", description);
            }
            foreach (AuxiliaryTestItemRow row in rows)
                row.Update(value, signal.Unit, JoinDescription(description, row.Note), time, raw);
        }

        private void UpdateDerivedTestItems(string time)
        {
            UpdateCombinedModel("DERIVED/OIL_MODEL", "ACU5_OilPump_Feedback3", "OilPump_SwModelH", "OilPump_SwModelL", time);
            UpdateCombinedModel("DERIVED/AIR_MODEL", "ACU8_AirPump_Feedback3", "AirPump_SwModelH", "AirPump_SwModelL", time);

            DbcDecodedSignal v1, v2, v3;
            if (TrySignal("ACU2_DCDC_Feedback2/DCDC_SwVer1", out v1) &&
                TrySignal("ACU2_DCDC_Feedback2/DCDC_SwVer2", out v2) &&
                TrySignal("ACU2_DCDC_Feedback2/DCDC_SwVer3", out v3))
            {
                string value = string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}  (RAW {0:X2} {1:X2} {2:X2})",
                    v1.RawValue, v2.RawValue, v3.RawValue);
                UpdateDerivedRows("DERIVED/DCDC_VERSION", value, "", time, RawFor("ACU2_DCDC_Feedback2"));
            }
        }

        private void UpdateCombinedModel(string derivedKey, string message, string highName, string lowName, string time)
        {
            DbcDecodedSignal high, low;
            if (!TrySignal(message + "/" + highName, out high) || !TrySignal(message + "/" + lowName, out low)) return;
            long model = ((high.RawValue & 0xFF) << 8) | (low.RawValue & 0xFF);
            UpdateDerivedRows(derivedKey, "0x" + model.ToString("X4", CultureInfo.InvariantCulture), "", time, RawFor(message));
        }

        private void UpdateDerivedRows(string key, string value, string unit, string time, string raw)
        {
            List<AuxiliaryTestItemRow> rows;
            if (!_testRowsBySignal.TryGetValue(key, out rows)) return;
            foreach (AuxiliaryTestItemRow row in rows) row.Update(value, unit, row.Note, time, raw);
        }

        private bool TrySignal(string key, out DbcDecodedSignal signal) { return _latestSignals.TryGetValue(key, out signal); }
        private string RawFor(string message) { string raw; return _latestRawFrames.TryGetValue(message, out raw) ? raw : ""; }

        private static string FormatTestValue(DbcDecodedSignal signal)
        {
            if (signal.Name.EndsWith("FaultCode", StringComparison.Ordinal))
                return "0x" + signal.RawValue.ToString("X2", CultureInfo.InvariantCulture);
            if (signal.Name.Contains("SwVer") || signal.Name.Contains("DebugVer"))
                return signal.RawValue.ToString(CultureInfo.InvariantCulture) + " (0x" + signal.RawValue.ToString("X2", CultureInfo.InvariantCulture) + ")";
            return signal.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string JoinDescription(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first)) return second ?? "";
            if (string.IsNullOrWhiteSpace(second)) return first;
            return first + "；" + second;
        }

        private void ClearReceivedValues()
        {
            _rows.Clear();
            _rowMap.Clear();
            _latestSignals.Clear();
            _latestRawFrames.Clear();
            foreach (AuxiliaryTestItemRow row in _testRows) row.Clear();
        }

        private async Task ExportTestItemsAsync()
        {
            SaveFileDialog dialog = CreateCsvDialog("辅驱测试项");
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                _status.Text = "已取消导出测试项。";
                return;
            }

            StringBuilder csv = new StringBuilder();
            AppendCsvRow(csv, "分组", "测试项", "实际值", "单位", "状态/说明", "更新时间", "DBC来源", "RAW报文");
            foreach (AuxiliaryTestItemRow row in _testRows)
                AppendCsvRow(csv, row.Section, row.TestItem, row.Value, row.Unit, row.Description, row.Time, row.Source, row.Raw);
            await SaveCsvAsync(dialog.FileName, csv.ToString());
            _status.Text = "测试项已导出：" + dialog.FileName;
        }

        private async Task ExportAllSignalsAsync()
        {
            SaveFileDialog dialog = CreateCsvDialog("DBC全部信号");
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                _status.Text = "已取消导出DBC全部信号。";
                return;
            }

            StringBuilder csv = new StringBuilder();
            AppendCsvRow(csv, "时间", "报文", "ID", "信号", "实际值", "单位", "状态/枚举", "RAW报文");
            foreach (AuxiliarySignalRow row in _rows)
                AppendCsvRow(csv, row.Time, row.Message, row.Id, row.Signal, row.Value, row.Unit, row.Description, row.Raw);
            await SaveCsvAsync(dialog.FileName, csv.ToString());
            _status.Text = "DBC全部信号已导出：" + dialog.FileName;
        }

        private SaveFileDialog CreateCsvDialog(string contentName)
        {
            return new SaveFileDialog
            {
                Title = "导出" + contentName,
                Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = _service.ProductProfile.Model + "_" + contentName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
            };
        }

        private static Task SaveCsvAsync(string path, string content)
        {
            return Task.Run(() => File.WriteAllText(path, content, new UTF8Encoding(true)));
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

        private static string Describe(DbcDecodedSignal signal)
        {
            if (!string.IsNullOrEmpty(signal.Description)) return signal.Description;
            int code = (int)signal.RawValue;
            if (signal.Name == "DCDC_FaultCode")
                return StatusAndFaultBits(code, new[] { "输出过流", "输出过压", "整机过热", "输入欠压", "输入过压", "门极过流", "预留" });
            if (signal.Name == "OilPump_FaultCode" || signal.Name == "AirPump_FaultCode")
                return StatusAndFaultBits(code, new[] { "输入过流", "整机过热", "输入欠压", "输入过压", "输出过流", "缺相保护", "门极过流" });
            return string.Empty;
        }

        private static string StatusAndFaultBits(int code, string[] names)
        {
            List<string> values = new List<string>();
            for (int bit = 0; bit < 7; bit++) if ((code & (1 << bit)) != 0) values.Add(names[bit]);
            string faults = values.Count == 0 ? "无故障" : string.Join("；", values);
            return "运行状态=" + (((code & 0x80) != 0) ? "运行" : "停止") + "；故障=" + faults + "；RAW=0x" + code.ToString("X2");
        }

        private void StartTimer(DispatcherTimer timer, string text)
        {
            if (!_ensureAuxiliaryProduct()) return;
            if (!_service.AuxiliaryConnected) { _status.Text = "请先点击顶部“连接DCDC/辅驱 CAN”。"; return; }
            try
            {
                if (timer == _receiveTimer)
                {
                    timer.Start();
                }
                else if (timer == _oilTimer)
                {
                    Dictionary<string, double> values = BuildOilValues();
                    _service.StartAuxiliaryPeriodic("OIL", "VCU1_DCDC_OilPump_Cmd", values, 100);
                }
                else if (timer == _airTimer)
                {
                    Dictionary<string, double> values = BuildAirValues();
                    _service.StartAuxiliaryPeriodic("AIR", "VCU2_AirPump_Cmd", values, 100);
                }
                else if (timer == _pduTimer)
                {
                    int sharedRelay = _sharedRelay.SelectedIndex;
                    int mainHighVoltage = _mainHighVoltage.SelectedIndex;
                    int bodywork = _bodywork.SelectedIndex;
                    int chargingRelay = _chargingRelay.SelectedIndex;
                    _service.StartAuxiliaryPeriodic("PDU", "VCU_PDU", BuildPduValues(sharedRelay, mainHighVoltage, bodywork, chargingRelay), 100, "VCU_HrtBt");
                }
                UpdateTimerButtons(timer, true);
                _status.Text = _service.ProductProfile.Model + "：" + text + (timer == _receiveTimer ? "" : "；已锁定当前参数，修改参数后需停止并重新开始。");
            }
            catch (Exception ex)
            {
                StopTimer(timer, "周期启动失败：" + ex.Message);
            }
        }

        private void StopTimer(DispatcherTimer timer, string text)
        {
            if (timer == _receiveTimer) timer.Stop();
            else if (timer == _oilTimer) _service.StopAuxiliaryPeriodic("OIL");
            else if (timer == _airTimer) _service.StopAuxiliaryPeriodic("AIR");
            else if (timer == _pduTimer) _service.StopAuxiliaryPeriodic("PDU");
            UpdateTimerButtons(timer, false);
            _status.Text = text;
        }

        private void StopAllTimers()
        {
            foreach (DispatcherTimer timer in new[] { _oilTimer, _airTimer, _pduTimer, _receiveTimer })
            {
                if (timer == _receiveTimer) timer.Stop();
                UpdateTimerButtons(timer, false);
            }
            try { _service.StopAuxiliaryPeriodic("OIL"); } catch { }
            try { _service.StopAuxiliaryPeriodic("AIR"); } catch { }
            try { _service.StopAuxiliaryPeriodic("PDU"); } catch { }
        }

        public void StopAllActivities()
        {
            StopAllTimers();
            if (_status != null) _status.Text = "MainTest辅驱周期任务已全部停止。";
        }

        private void AddTimerButtons(WrapPanel panel, DispatcherTimer timer, string startText, string activeText, string startedStatus, string stoppedStatus)
        {
            Button start = Button(startText, (s, e) => StartTimer(timer, startedStatus));
            string stopText = startText.Contains("自动接收") ? "停止自动接收" : "停止周期发送";
            Button stop = Button(stopText, (s, e) => StopTimer(timer, stoppedStatus));
            _timerButtonStates[timer] = new TimerButtonState(start, stop, startText, activeText, stopText);
            panel.Children.Add(start);
            panel.Children.Add(stop);
        }

        private void UpdateTimerButtons(DispatcherTimer timer, bool active)
        {
            TimerButtonState state;
            if (!_timerButtonStates.TryGetValue(timer, out state)) return;

            state.Start.Content = active ? state.ActiveText : state.StartText;
            state.Start.Background = active ? Brushes.LightGreen : SystemColors.ControlBrush;
            state.Start.BorderBrush = active ? Brushes.ForestGreen : SystemColors.ActiveBorderBrush;
            state.Start.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;

            state.Stop.Content = active ? state.StopText : "已停止 ✓";
            state.Stop.Background = active ? SystemColors.ControlBrush : Brushes.Moccasin;
            state.Stop.BorderBrush = active ? SystemColors.ActiveBorderBrush : Brushes.DarkOrange;
            state.Stop.FontWeight = active ? FontWeights.Normal : FontWeights.Bold;
        }

        private Button ActionButton(string text, Func<Task> action)
        {
            Button button = Button(text, null);
            button.Click += async (s, e) =>
            {
                string original = text;
                button.Content = original + "…";
                button.Background = Brushes.LightSkyBlue;
                button.BorderBrush = Brushes.DodgerBlue;
                button.FontWeight = FontWeights.Bold;
                button.IsEnabled = false;
                try
                {
                    await action();
                    button.Content = original + " ✓";
                    button.Background = Brushes.LightGreen;
                    button.BorderBrush = Brushes.ForestGreen;
                    await Task.Delay(800);
                }
                catch (Exception ex)
                {
                    button.Content = original + " 失败";
                    button.Background = Brushes.MistyRose;
                    button.BorderBrush = Brushes.Firebrick;
                    _status.Text = original + "失败：" + ex.Message;
                    await Task.Delay(1200);
                }
                finally
                {
                    button.Content = original;
                    button.Background = SystemColors.ControlBrush;
                    button.BorderBrush = SystemColors.ActiveBorderBrush;
                    button.FontWeight = FontWeights.Normal;
                    button.IsEnabled = true;
                }
            };
            return button;
        }

        private static DispatcherTimer Timer(int milliseconds, EventHandler handler) { DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; if (handler != null) timer.Tick += handler; return timer; }
        private static double CommandValue(ComboBox box) { return box.SelectedIndex == 1 ? 0x55 : 0xAA; }
        private static double Number(TextBox box, string name) { double value; if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new FormatException(name + "不是有效数字。"); return value; }

        private static ComboBox StartStopCombo() { return Combo(new[] { "0xAA - 停止", "0x55 - 启动" }, 0, 115); }
        private static ComboBox ModeCombo() { return Combo(new[] { "0 - V/F", "1 - 同步开环" }, 0, 125); }
        private static ComboBox RelayCombo() { return Combo(new[] { "0 - 断开", "1 - 闭合", "2 - 错误", "3 - 无效" }, 0, 105); }
        private static ComboBox PowerCombo() { return Combo(new[] { "0 - 下电", "1 - 上电", "2 - 错误", "3 - 无效" }, 0, 105); }
        private static ComboBox Combo(string[] items, int selected, double width) { return new ComboBox { ItemsSource = items, SelectedIndex = selected, Width = width, Margin = new Thickness(3) }; }
        private static TextBox Box(string text, double width) { return new TextBox { Text = text, Width = width, Margin = new Thickness(3), Padding = new Thickness(3, 2, 3, 2) }; }
        private static TextBlock Label(string text) { return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) }; }
        private static TextBlock Hint(string text) { return new TextBlock { Text = text, Foreground = Brushes.DarkOrange, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 3, 8, 3), ToolTip = text }; }
        private static CheckBox Check(string text, bool value) { return new CheckBox { Content = text, IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 3, 5, 3) }; }
        private static Button Button(string text, RoutedEventHandler handler) { Button button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4), MinWidth = 78 }; if (handler != null) button.Click += handler; return button; }
        private static GroupBox Group(string header, UIElement content) { return new GroupBox { Header = header, Content = content, Margin = new Thickness(4), Padding = new Thickness(4) }; }
        private static DataGridTextColumn Column(string header, string path, double width) { return new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width }; }

        private sealed class TimerButtonState
        {
            public TimerButtonState(Button start, Button stop, string startText, string activeText, string stopText)
            {
                Start = start;
                Stop = stop;
                StartText = startText;
                ActiveText = activeText;
                StopText = stopText;
            }

            public Button Start { get; private set; }
            public Button Stop { get; private set; }
            public string StartText { get; private set; }
            public string ActiveText { get; private set; }
            public string StopText { get; private set; }
        }
    }

    internal sealed class PrecisePeriodicSender : IDisposable
    {
        private readonly object _sync = new object();
        private readonly int _periodMilliseconds;
        private readonly Action<double> _timingWarning;
        private readonly Action<Exception> _error;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private System.Threading.Timer _timer;
        private Action _action;
        private int _generation;
        private int _busy;
        private long _lastSendTicks;
        private long _lastWarningTicks;

        public PrecisePeriodicSender(int periodMilliseconds, Action<double> timingWarning, Action<Exception> error)
        {
            if (periodMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(periodMilliseconds));
            _periodMilliseconds = periodMilliseconds;
            _timingWarning = timingWarning;
            _error = error;
        }

        public void Start(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (_sync)
            {
                StopCore();
                _action = action;
                _lastSendTicks = 0;
                _lastWarningTicks = 0;
                int generation = ++_generation;
                _timer = new System.Threading.Timer(Tick, generation, 0, _periodMilliseconds);
            }
        }

        public void Stop()
        {
            lock (_sync) StopCore();
        }

        private void StopCore()
        {
            _generation++;
            System.Threading.Timer timer = _timer;
            _timer = null;
            _action = null;
            if (timer != null) timer.Dispose();
        }

        private void Tick(object state)
        {
            int generation = (int)state;
            if (generation != Volatile.Read(ref _generation)) return;
            if (Interlocked.Exchange(ref _busy, 1) != 0) return;
            try
            {
                long now = _clock.ElapsedTicks;
                long previous = Interlocked.Exchange(ref _lastSendTicks, now);
                if (previous != 0)
                {
                    double interval = (now - previous) * 1000.0 / Stopwatch.Frequency;
                    long lastWarning = Volatile.Read(ref _lastWarningTicks);
                    if (interval > 120.0 && (lastWarning == 0 || (now - lastWarning) * 1000.0 / Stopwatch.Frequency >= 1000.0))
                    {
                        Interlocked.Exchange(ref _lastWarningTicks, now);
                        if (_timingWarning != null) _timingWarning(interval);
                    }
                }

                Action action = _action;
                if (action != null && generation == Volatile.Read(ref _generation)) action();
            }
            catch (Exception ex)
            {
                Stop();
                if (_error != null) _error(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        public void Dispose() { Stop(); }
    }

    public sealed class AuxiliarySignalRow : INotifyPropertyChanged
    {
        private string _time, _value, _unit, _description, _raw;
        public string Message { get; set; }
        public string Id { get; set; }
        public string Signal { get; set; }
        public string Time { get { return _time; } }
        public string Value { get { return _value; } }
        public string Unit { get { return _unit; } }
        public string Description { get { return _description; } }
        public string Raw { get { return _raw; } }
        public event PropertyChangedEventHandler PropertyChanged;

        public void Update(string time, string value, string unit, string description, string raw)
        {
            _time = time; _value = value; _unit = unit; _description = description; _raw = raw;
            Action<string> changed = name => { PropertyChangedEventHandler handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); };
            changed("Time"); changed("Value"); changed("Unit"); changed("Description"); changed("Raw");
        }
    }

    public sealed class AuxiliaryTestItemRow : INotifyPropertyChanged
    {
        private string _value = "等待数据";
        private string _unit = "";
        private string _description;
        private string _time = "";
        private string _raw = "";

        public AuxiliaryTestItemRow(string section, string testItem, string source, string note)
        {
            Section = section;
            TestItem = testItem;
            Source = source;
            Note = note ?? "";
            _description = Note;
        }

        public string Section { get; private set; }
        public string TestItem { get; private set; }
        public string Source { get; private set; }
        public string Note { get; private set; }
        public string Value { get { return _value; } }
        public string Unit { get { return _unit; } }
        public string Description { get { return _description; } }
        public string Time { get { return _time; } }
        public string Raw { get { return _raw; } }
        public event PropertyChangedEventHandler PropertyChanged;

        public void Update(string value, string unit, string description, string time, string raw)
        {
            _value = value ?? "";
            _unit = unit ?? "";
            _description = description ?? "";
            _time = time ?? "";
            _raw = raw ?? "";
            RaiseAll();
        }

        public void Clear()
        {
            _value = "等待数据";
            _unit = "";
            _description = Note;
            _time = "";
            _raw = "";
            RaiseAll();
        }

        private void RaiseAll()
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler == null) return;
            foreach (string name in new[] { "Value", "Unit", "Description", "Time", "Raw" })
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
