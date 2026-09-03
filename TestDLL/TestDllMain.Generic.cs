using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CSP
{
    public partial class TestDllMain
    {
        private readonly object _fctGenericLocker = new object();
        private readonly Dictionary<int, Dictionary<string, object>> _fctVariables = new Dictionary<int, Dictionary<string, object>>();
        private readonly Dictionary<int, Dictionary<string, int>> _fctLoopCounters = new Dictionary<int, Dictionary<string, int>>();
        private Instruments.CAN.CANWrapper _fctAuxCan;
        private readonly Dictionary<string, object> _fctActionPlugins = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        // Stations register their periodic senders under station-prefixed keys, so this map is written
        // from several test threads at once and must not be a bare Dictionary.
        private readonly Dictionary<string, Timer> _fctAuxPeriodicSenders = new Dictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);
        private readonly object _fctAuxSendLocker = new object();
        private readonly HashSet<string> _fctInitializedInstrumentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _fctInstrumentSelectionJson = string.Empty;
        private static readonly object _fctCanDiagnosticLocker = new object();
        private static string _fctCanDiagnosticLogPath;
        private static bool _fctCanDiagnosticsEnabled;

        public void FCT_SetCanDiagnostics(bool enabled)
        {
            _fctCanDiagnosticsEnabled = enabled;
            if (enabled) FCT_CanDiagnostic("Detailed CAN diagnostics enabled by debug host.");
        }

        public string FCT_GetCanDiagnosticLogPath()
        {
            return FCT_CanDiagnosticPath();
        }

        private static string FCT_CanDiagnosticPath()
        {
            if (!string.IsNullOrWhiteSpace(_fctCanDiagnosticLogPath)) return _fctCanDiagnosticLogPath;
            string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
            string folder = Path.Combine(baseFolder, "Logs");
            Directory.CreateDirectory(folder);
            _fctCanDiagnosticLogPath = Path.Combine(folder, "MainTest_CAN_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            return _fctCanDiagnosticLogPath;
        }

        private static void FCT_CanDiagnostic(string message, Exception exception = null)
        {
            if (!_fctCanDiagnosticsEnabled) return;
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " [T" + Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture) + "] " + message;
                if (exception != null) line += Environment.NewLine + exception;
                lock (_fctCanDiagnosticLocker)
                    File.AppendAllText(FCT_CanDiagnosticPath(), line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string FCT_FileIdentity(string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists) return path + " [MISSING]";
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                return path + " [" + file.Length.ToString(CultureInfo.InvariantCulture) + " bytes, fileVersion=" + (version.FileVersion ?? "") + ", modified=" + file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "]";
            }
            catch (Exception ex) { return path + " [identity failed: " + ex.Message + "]"; }
        }

        public void FCT_SetInstrumentSelection(string instrumentsJson)
        {
            _fctInstrumentSelectionJson = string.IsNullOrWhiteSpace(instrumentsJson) ? string.Empty : instrumentsJson;
            FCT_CanDiagnostic("Instrument selection received: " + _fctInstrumentSelectionJson);
        }

        private string FCT_LoadInstrumentSelectionFromConfigCore()
        {
            try
            {
                string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
                string assemblyParent = Directory.GetParent(assemblyFolder) == null ? assemblyFolder : Directory.GetParent(assemblyFolder).FullName;
                string[] candidates =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "InstrumentConfig.json"),
                    Path.Combine(assemblyParent, "Config", "InstrumentConfig.json"),
                    Path.Combine(assemblyFolder, "Config", "InstrumentConfig.json")
                };
                string path = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(path)) return string.Empty;
                JArray source = JArray.Parse(File.ReadAllText(path));
                JArray selected = new JArray(source.OfType<JObject>().Where(item => (bool?)item["Initialize"] == true).Select(item => item.DeepClone()));
                if (selected.Count == 0) return string.Empty;
                string json = selected.ToString(Formatting.None);
                FCT_CanDiagnostic("Instrument selection loaded from platform config: " + path + "; selected=" + string.Join(",", selected.OfType<JObject>().Select(item => (string)item["Name"] ?? string.Empty)));
                return json;
            }
            catch (Exception ex)
            {
                FCT_CanDiagnostic("Load platform instrument selection failed; legacy ProcessSetup will be used.", ex);
                return string.Empty;
            }
        }

        private double FCT_InitializeConfiguredInstrumentsCore()
        {
            string current = string.Empty;
            try
            {
                string selectionJson = _fctInstrumentSelectionJson;
                FCT_CleanupConfiguredInstrumentsCore();
                _fctInstrumentSelectionJson = selectionJson;
                JArray instruments = JArray.Parse(string.IsNullOrWhiteSpace(selectionJson) ? "[]" : selectionJson);
                if (instruments.Count == 0) throw new InvalidOperationException("No instruments were selected.");
                FCT_CanDiagnostic("Initialize selected instruments start. BaseDirectory=" + AppDomain.CurrentDomain.BaseDirectory + "; TestDLL=" + Assembly.GetExecutingAssembly().Location);
                foreach (JObject item in instruments.OfType<JObject>())
                {
                    current = ((string)item["Name"] ?? string.Empty).Trim().ToUpperInvariant();
                    string resource = (string)item["Resource"] ?? string.Empty;
                    string parameter = (string)item["Parameter"] ?? string.Empty;
                    _fctInitializedInstrumentNames.Add(current);
                    FCT_CanDiagnostic("Initialize instrument " + current + ": Resource=" + resource + "; Parameter=" + parameter);
                    FCT_InitializeSelectedInstrument(current, resource, parameter);
                }
                return 0;
            }
            catch (Exception ex)
            {
                FCT_CanDiagnostic("Initialize selected instrument failed: " + current, ex);
                try { FCT_CleanupConfiguredInstrumentsCore(); } catch { }
                throw new InvalidOperationException("Initialize selected instrument failed: " + current + ". " + ex.Message, ex);
            }
        }

        private double FCT_PrepareConfiguredInstrumentsCore(int socketIndex)
        {
            FCT_ResetGenericRuntimeCore(socketIndex);
            if (_fctInitializedInstrumentNames.Contains("DUTCAN") && MyCAN != null)
            {
                try { MyCAN.StartTraceLog(Path.Combine(Path.GetTempPath(), "FCT_CAN_" + DateTime.Now.ToString("yyyyMMdd") + ".asc")); } catch { }
            }
            if (_fctInitializedInstrumentNames.Contains("RESOLVERCAN") && Resolver != null)
            {
                Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true);
            }
            return 0;
        }

        private double FCT_FinishConfiguredInstrumentsCore(int socketIndex)
        {
            FCT_ResetGenericRuntimeCore(socketIndex);
            FCT_StopAllAuxPeriodic();
            if (_fctInitializedInstrumentNames.Contains("RESOLVERCAN") && Resolver != null) try { Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true); } catch { }
            if (_fctInitializedInstrumentNames.Contains("HVDC")) try { HVDC.SetSourceVoltage(0); HVDC.SetOutput(false); } catch { }
            if (_fctInitializedInstrumentNames.Contains("LVDC")) try { LVDC.SetOutput(false); } catch { }
            if (_fctInitializedInstrumentNames.Contains("LVDC_KL15")) try { LVDC_KL15.SetOutput(false); } catch { }
            if (_fctInitializedInstrumentNames.Contains("DCDC_LOAD") && DcdcLoad != null) try { DcdcLoad.LoadOff(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("RELAY_FCT")) try { RelayFctBoard.WriteDO(string.Join(",", Enumerable.Range(0, 48)), string.Join(",", Enumerable.Repeat("0", 48))); } catch { }
            if (_fctInitializedInstrumentNames.Contains("RELAY_HVMUX")) try { RelayHvMux.WriteDO(string.Join(",", Enumerable.Range(1, 15).Select(index => "OUT" + index)), string.Join(",", Enumerable.Repeat("0", 15))); } catch { }
            return 0;
        }

        private double FCT_CleanupConfiguredInstrumentsCore()
        {
            FCT_CanDiagnostic("Cleanup selected instruments: " + string.Join(",", _fctInitializedInstrumentNames));
            FCT_StopAllAuxPeriodic();
            if (_fctInitializedInstrumentNames.Contains("DUTCAN") && MyCAN != null) try { MyCAN.CloseCANDevice(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("RESOLVERCAN") && Resolver != null) try { Resolver.CloseCANDevice(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("AUXCAN") && _fctAuxCan != null) try { _fctAuxCan.CloseCANDevice(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("LVDC")) try { LVDC.DisconnectDevice(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("LVDC_KL15")) try { LVDC_KL15.DisconnectDevice(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("HVDC")) try { HVDC.DisconnectDevice(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("DMM") || _fctInitializedInstrumentNames.Contains("DMM_HV")) try { DMM.CloseSession(); } catch { }
            if (_fctInitializedInstrumentNames.Contains("DMM_LV")) try { DMM_LV.CloseSession(); } catch { }
            if (DcdcLoad != null) { try { DcdcLoad.LoadOff(); } catch { } try { DcdcLoad.Disconnect(); } catch { } try { DcdcLoad.Dispose(); } catch { } DcdcLoad = null; }
            try { RelayFctBoard.Disconnect(); } catch { }
            try { RelayHvMux.Disconnect(); } catch { }
            MyCAN = null; Resolver = null; _fctAuxCan = null; _fctInitializedInstrumentNames.Clear(); _fctInstrumentSelectionJson = string.Empty;
            return 0;
        }

        public string FCT_GetInitializedInstruments()
        {
            return string.Join(",", _fctInitializedInstrumentNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        private void FCT_InitializeSelectedInstrument(string name, string resource, string parameter)
        {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string executableFolder = Path.GetDirectoryName(assemblyFolder);
            switch (name)
            {
                case "DUTCAN":
                    MyCAN = FCT_OpenSelectedCan(assemblyFolder, executableFolder, resource, parameter, 2, "Flywheel_900A_Z405.dbc");
                    break;
                case "RESOLVERCAN":
                    Resolver = FCT_OpenSelectedCan(assemblyFolder, executableFolder, resource, parameter, 1, "Resolver.dbc");
                    Resolver.SendMessage(0x80000001, new byte[8]);
                    Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true);
                    break;
                case "AUXCAN":
                    _fctAuxCan = FCT_OpenSelectedCan(assemblyFolder, executableFolder, resource, parameter, 0, "C95C96Auxiliary.dbc");
                    break;
                case "RES": case "RES_1": RES.ConnectDevice(resource, parameter); RES.SetResistance(1100, 1); RES.SetResistance(1100, 2); break;
                case "RES_2": RES_2.ConnectDevice(resource, parameter); RES_2.SetResistance(1100, 1); RES_2.SetResistance(1100, 2); break;
                case "RES_3": RES_3.ConnectDevice(resource, parameter); RES_3.SetResistance(1100, 1); RES_3.SetResistance(1100, 2); break;
                case "LVDC": LVDC.ConnectDevice(resource, parameter); LVDC.SetOutput(false); break;
                case "LVDC_KL15": LVDC_KL15.ConnectDevice(resource, parameter); LVDC_KL15.SetOutput(false); break;
                case "HVDC":
                    HVDC.ConnectDevice(resource, parameter);
                    FCT_KewellEnsureSourceCvMode(HVDC, null, soft: true);
                    FCT_KewellSetOutput(HVDC, false, null);
                    try { HVDC.SetSourceVoltage(0); } catch { }
                    break;
                case "DMM": case "DMM_HV": DMM.OpenSession(resource); DMM.InitDMM(); DMM.ConfigDMMforDC(1000, 0.01); break;
                case "DMM_LV": DMM_LV.OpenSession(resource); DMM_LV.InitDMM(); DMM_LV.ConfigDMMforDC(100, 0.001); break;
                case "RELAY": Relay.connect(resource, FCT_SelectedPort(parameter, 502)); break;
                case "RELAY_FCT": RelayFctBoardSlave = FCT_SelectedSlave(parameter, 1); RelayFctBoard.SlaveAddress = RelayFctBoardSlave; RelayFctBoard.Connect(resource, (ushort)FCT_SelectedPortAt(parameter, 502), "sht"); break;
                case "RELAY_HVMUX": RelayHvMuxSlave = FCT_SelectedSlave(parameter, 1); RelayHvMux.SlaveAddress = RelayHvMuxSlave; RelayHvMux.Connect(resource, (ushort)FCT_SelectedPortAt(parameter, 502), "sht"); break;
                case "PLC": MyPLC.Connect(FCT_SelectedPort(parameter, 30), resource); break;
                case "DAQ": break;
                case "DCDC_LOAD":
                    DcdcLoad = new AN23600E.Driver.An23600eDriver(resource, FCT_SelectedPort(parameter, 2101));
                    DcdcLoad.Connect();
                    DcdcLoad.Session.CheckErrorsAfterCommand = false;
                    DcdcLoad.ClearStatus();
                    DcdcLoad.LoadOff();
                    FCT_CanDiagnostic("AN23600E connected: " + DcdcLoad.GetIdentity());
                    break;
                default: throw new InvalidOperationException("Unsupported instrument: " + name);
            }
        }

        private Instruments.CAN.CANWrapper FCT_OpenSelectedCan(string assemblyFolder, string executableFolder, string ip, string parameter, ushort fallbackChannel, string dbcFile)
        {
            string[] values = (parameter ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            uint deviceType = values.Length > 0 ? uint.Parse(values[0], CultureInfo.InvariantCulture) : 52;
            ushort channel = values.Length > 1 ? ushort.Parse(values[1], CultureInfo.InvariantCulture) : fallbackChannel;
            uint baudRate = values.Length > 2 ? uint.Parse(values[2], CultureInfo.InvariantCulture) : 500000;
            int port = values.Length > 3 ? int.Parse(values[3], CultureInfo.InvariantCulture) : 8000;
            int deviceIndex = values.Length > 4 ? int.Parse(values[4], CultureInfo.InvariantCulture) : 0;
            string wrapperPath = typeof(Instruments.CAN.CANWrapper).Assembly.Location;
            string providerPath = Path.Combine(assemblyFolder, "Instruments.CAN.ZLG_CAN.dll");
            string nativePath = Path.Combine(assemblyFolder, "zlgcan.dll");
            string kernelPath = Path.Combine(assemblyFolder, "kerneldlls", "CANFDNET.dll");
            string dbcPath = Path.Combine(executableFolder, "Config", dbcFile);
            string actualIp = string.IsNullOrWhiteSpace(ip) ? "192.166.6.10" : ip;
            FCT_CanDiagnostic("CAN runtime: " + FCT_FileIdentity(wrapperPath));
            FCT_CanDiagnostic("CAN provider: " + FCT_FileIdentity(providerPath));
            FCT_CanDiagnostic("CAN native: " + FCT_FileIdentity(nativePath));
            FCT_CanDiagnostic("CAN kernel: " + FCT_FileIdentity(kernelPath));
            FCT_CanDiagnostic("CAN open request: DeviceType=" + deviceType + "; DeviceIndex=" + deviceIndex + "; Channel=" + channel + "; BaudRate=" + baudRate + "; IP=" + actualIp + "; Port=" + port + "; DBC=" + dbcPath);
            try
            {
                Instruments.CAN.CANWrapper wrapper = new Instruments.CAN.CANWrapper(providerPath);
                wrapper.DBC_ReadDBCTxt(dbcPath);
                wrapper.SetValue("IP", actualIp);
                wrapper.SetValue("PORT", port);
                wrapper.OpenCANDevice(deviceType, channel, baudRate);
                FCT_CanDiagnostic("CAN open succeeded: DeviceType=" + deviceType + "; Channel=" + channel);
                return wrapper;
            }
            catch (Exception ex)
            {
                FCT_CanDiagnostic("CAN open failed: DeviceType=" + deviceType + "; Channel=" + channel, ex);
                throw;
            }
        }

        private static int FCT_SelectedPort(string parameter, int fallback)
        {
            string first = (parameter ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            int value; return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        public void FCT_ExecuteAction(int socketIndex)
        {
            string device = FCT_InputString(socketIndex, "Device", string.Empty).Trim().ToUpperInvariant();
            string operation = FCT_InputString(socketIndex, "Operation", string.Empty).Trim();
            FCT_ExecuteConfiguredAction(socketIndex, device, operation);
        }

        private void FCT_ExecuteConfiguredAction(int socketIndex, string configuredDevice, string configuredOperation)
        {
            lock (_fctGenericLocker)
            {
                string device = (configuredDevice ?? string.Empty).Trim().ToUpperInvariant();
                string operation = (configuredOperation ?? string.Empty).Trim();
                FCT_Log(socketIndex, "ACTION " + device + "." + operation + " START");
                object result = null;
                switch (device)
                {
                    case "LVDC": result = FCT_Lvdc(socketIndex, operation, LVDC); break;
                    case "LVDC_KL15": result = FCT_Lvdc(socketIndex, operation, LVDC_KL15); break;
                    case "HVDC": result = FCT_Hvdc(socketIndex, operation, HVDC); break;
                    case "DMM": case "DMM_HV": result = FCT_Dmm(socketIndex, operation, DMM); break;
                    case "DMM_LV": result = FCT_Dmm(socketIndex, operation, DMM_LV); break;
                    case "RES": case "RES_1": FCT_Res(socketIndex, RES); break;
                    case "RES_2": FCT_Res(socketIndex, RES_2); break;
                    case "RES_3": FCT_Res(socketIndex, RES_3); break;
                    case "DAQ": result = FCT_ReadDaq(socketIndex); break;
                    case "DCDC_LOAD": result = FCT_DcdcLoad(socketIndex, operation, DcdcLoad); break;
                    case "MOXA": MOXA_SetDO(socketIndex); break;
                    case "RELAY": Relay_SetDO(socketIndex); break;
                    case "RELAY_FCT": FCT_ShtRelay(socketIndex, operation, RelayFctBoard, RelayFctBoardSlave); break;
                    case "RELAY_HVMUX": FCT_ShtRelay(socketIndex, operation, RelayHvMux, RelayHvMuxSlave); break;
                    case "PLC": PLC_LoadFinished(socketIndex); break;
                    case "RESOLVER": result = FCT_Resolver(socketIndex, operation); break;
                    case "PRODUCTCAN": result = FCT_ProductCan(socketIndex, operation); break;
                    case "AUXCAN": result = FCT_AuxCan(socketIndex, operation); break;
                    case "FLOW": result = FCT_FlowAction(socketIndex, operation); break;
                    default: result = FCT_InvokeActionPlugin(socketIndex, device, operation); break;
                }
                FCT_SaveOutputVariable(socketIndex, result);
                FCT_RecordResult(socketIndex, result);
                FCT_Log(socketIndex, "ACTION " + device + "." + operation + " END" + (result == null ? string.Empty : " => " + Convert.ToString(result, CultureInfo.InvariantCulture)));
            }
        }

        private void FCT_ExecuteGeneratedDriverMethod(int socketIndex, string assemblyPath, string typeName, string methodName)
        {
            lock (_fctGenericLocker) FCT_ExecuteGeneratedDriverMethodCore(socketIndex, assemblyPath, typeName, methodName);
        }

        private void FCT_ExecuteGeneratedDriverMethodCore(int socketIndex, string assemblyPath, string typeName, string methodName)
        {
            {
                if (!Path.IsPathRooted(assemblyPath)) assemblyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), assemblyPath);
                string key = assemblyPath + "|" + typeName; object instance;
                if (!_fctActionPlugins.TryGetValue(key, out instance))
                {
                    Type type = Assembly.LoadFrom(assemblyPath).GetType(typeName, true);
                    ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes); if (constructor == null) throw new InvalidOperationException("Driver type requires constructor parameters and cannot be generated automatically: " + typeName);
                    instance = constructor.Invoke(new object[0]); _fctActionPlugins[key] = instance;
                }
                MethodInfo method = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(value => string.Equals(value.Name, methodName, StringComparison.OrdinalIgnoreCase) && !value.ContainsGenericParameters && !value.GetParameters().Any(parameter => parameter.IsOut || parameter.ParameterType.IsByRef)).OrderBy(value => value.GetParameters().Length).FirstOrDefault();
                if (method == null) throw new MissingMethodException(typeName, methodName);
                ParameterInfo[] parameters = method.GetParameters(); object[] values = new object[parameters.Length];
                for (int index = 0; index < parameters.Length; index++) values[index] = FCT_ConvertGeneratedParameter(FCT_InputString(socketIndex, parameters[index].Name, parameters[index].HasDefaultValue && parameters[index].DefaultValue != null ? Convert.ToString(parameters[index].DefaultValue, CultureInfo.InvariantCulture) : string.Empty), parameters[index].ParameterType);
                FCT_Log(socketIndex, "GENERATED " + typeName + "." + methodName + " START"); object result = method.Invoke(instance, values); FCT_SaveOutputVariable(socketIndex, result); FCT_RecordResult(socketIndex, result); FCT_Log(socketIndex, "GENERATED " + typeName + "." + methodName + " END" + (result == null ? string.Empty : " => " + Convert.ToString(result, CultureInfo.InvariantCulture)));
            }
        }

        private static object FCT_ConvertGeneratedParameter(string text, Type targetType)
        {
            Type actual = Nullable.GetUnderlyingType(targetType) ?? targetType; if (actual == typeof(string)) return text ?? string.Empty; if (actual == typeof(bool)) { bool value; if (bool.TryParse(text, out value)) return value; return text == "1" || string.Equals(text, "ON", StringComparison.OrdinalIgnoreCase); } if (actual.IsEnum) return Enum.Parse(actual, text, true); if (actual == typeof(byte[])) return FCT_ParseHexBytes(text); return Convert.ChangeType(string.IsNullOrWhiteSpace(text) && actual.IsValueType ? "0" : text, actual, CultureInfo.InvariantCulture);
        }

        private object FCT_DcdcLoad(int socketIndex, string operation) { return FCT_DcdcLoad(socketIndex, operation, DcdcLoad); }
        private object FCT_DcdcLoad(int socketIndex, string operation, AN23600E.Driver.An23600eDriver load)
        {
            if (load == null || !load.IsConnected) throw new InvalidOperationException("DCDC_LOAD is not initialized.");
            switch ((operation ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "SETMODE":
                {
                    string modeText = FCT_InputString(socketIndex, "Mode", "Ccm");
                    string modeCode = ResolveDcdcLoadModeCode(modeText);
                    load.SetMode((AN23600E.Driver.Enums.LoadMode)Enum.Parse(typeof(AN23600E.Driver.Enums.LoadMode), string.IsNullOrWhiteSpace(modeCode) ? "Ccm" : modeCode, true));
                    return null;
                }
                case "SETCURRENT": load.SetStaticCurrent(FCT_InputDouble(socketIndex, "Current", 0)); return null;
                case "SETVOLTAGE": load.SetStaticVoltage(FCT_InputDouble(socketIndex, "Voltage", 0)); return null;
                case "SETRESISTANCE": load.SetStaticResistance(FCT_InputDouble(socketIndex, "Resistance", 1)); return null;
                case "SETPOWER": load.SetStaticPower(FCT_InputDouble(socketIndex, "Power", 0)); return null;
                case "OUTPUTON": load.LoadOn(); return null;
                case "OUTPUTOFF": load.LoadOff(); return null;
                case "READVOLTAGE": return load.MeasureVoltage();
                case "READCURRENT": return load.MeasureCurrent();
                case "READPOWER": return load.MeasurePower();
                case "READPROTECTION": return (int)load.GetProtectionStatus();
                case "CLEARPROTECTION": load.LoadOff(); load.ClearProtection(); return null;
                case "RESET": load.LoadOff(); load.Reset(); return null;
                default: throw new InvalidOperationException("Unsupported DCDC_LOAD operation: " + operation);
            }
        }

        private static string ResolveDcdcLoadModeCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Ccm";
            string value = text.Trim();
            int separator = value.IndexOf(" - ", StringComparison.Ordinal);
            if (separator > 0) value = value.Substring(0, separator).Trim();
            string key = value.Replace("（", "").Replace("）", "").Replace("(", "").Replace(")", "").Replace("·", " ").Replace("　", " ");
            while (key.Contains("  ")) key = key.Replace("  ", " ");
            key = key.Trim().ToUpperInvariant();
            switch (key)
            {
                case "CCL": case "CC L": case "CC低": case "CC 低": case "CC低量程": case "CC 低量程": return "Ccl";
                case "CCM": case "CC M": case "CC中": case "CC 中": case "CC中量程": case "CC 中量程": return "Ccm";
                case "CCH": case "CC H": case "CC高": case "CC 高": case "CC高量程": case "CC 高量程": return "Cch";
                case "CVL": case "CV L": case "CV低": case "CV 低": case "CV低量程": case "CV 低量程": return "Cvl";
                case "CVM": case "CV M": case "CV中": case "CV 中": case "CV中量程": case "CV 中量程": return "Cvm";
                case "CVH": case "CV H": case "CV高": case "CV 高": case "CV高量程": case "CV 高量程": return "Cvh";
                case "CRL": case "CR L": case "CR低": case "CR 低": case "CR低量程": case "CR 低量程": return "Crl";
                case "CRM": case "CR M": case "CR中": case "CR 中": case "CR中量程": case "CR 中量程": return "Crm";
                case "CRH": case "CR H": case "CR高": case "CR 高": case "CR高量程": case "CR 高量程": return "Crh";
                case "CPL": case "CP L": case "CP低": case "CP 低": case "CP低量程": case "CP 低量程": return "Cpl";
                case "CPM": case "CP M": case "CP中": case "CP 中": case "CP中量程": case "CP 中量程": return "Cpm";
                case "CPH": case "CP H": case "CP高": case "CP 高": case "CP高量程": case "CP 高量程": return "Cph";
                default: return value;
            }
        }

        private void FCT_ShtRelay(int socketIndex, string operation, ShtRelayCompatAdapter board, byte configuredSlave)
        {
            if (board == null) throw new InvalidOperationException("继电器板在当前工位没有实例。请在仪器中心为该工位分配并填写连接资源。");
            byte slave = (byte)Math.Max(0, Math.Min(255, (int)FCT_InputDouble(socketIndex, "Slave", configuredSlave)));
            board.SlaveAddress = slave;
            if ((operation ?? string.Empty).Equals("SelectFctMux", StringComparison.OrdinalIgnoreCase))
            {
                string selectionText = FCT_InputString(socketIndex, "Selection", "1"); int selection; string numberText = (selectionText ?? string.Empty).Split(new[] { ' ', '-', '：', ':' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(); if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out selection)) selection = (int)FCT_InputDouble(socketIndex, "Selection", 1);
                if (selection < 1 || selection > 13) throw new ArgumentOutOfRangeException("Selection", "FCT功能选择必须为IO表已定义的1到13。");
                // 功能板OUT1..4=A0..A3，OUT5=E。E=H时全部Q输出低；E=L时地址对应Q输出高。
                // 地址0对应未使用的Q0安全位；J1..J36使用地址1..13。
                board.WriteDO("OUT5", "1");
                board.WriteDO("OUT1,OUT2,OUT3,OUT4", string.Join(",", Enumerable.Range(0, 4).Select(bit => (selection & (1 << bit)) != 0 ? "1" : "0")));
                Thread.Sleep(Math.Max(20, (int)FCT_InputDouble(socketIndex, "SwitchDelayMs", 50)));
                board.WriteDO("OUT5", "0");
                return;
            }
            if ((operation ?? string.Empty).Equals("DisableFctMux", StringComparison.OrdinalIgnoreCase))
            {
                board.WriteDO("OUT5", "1");
                board.WriteDO("OUT1,OUT2,OUT3,OUT4", "0,0,0,0");
                Thread.Sleep(Math.Max(20, (int)FCT_InputDouble(socketIndex, "SwitchDelayMs", 50)));
                return;
            }
            if ((operation ?? string.Empty).Equals("Select15", StringComparison.OrdinalIgnoreCase))
            {
                string selectionText = FCT_InputString(socketIndex, "Selection", "1"); string numberText = selectionText.Split(new[] { ' ', '-', '/', '：', ':' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(); int selection; if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out selection)) selection = (int)FCT_InputDouble(socketIndex, "Selection", 1);
                if (selection < 1 || selection > 15) throw new ArgumentOutOfRangeException("Selection", "高压测量通道必须为1到15。");
                // 48路板前6点构成锁存式15选1：OUT1..4=A0..A3, OUT5=E, OUT6=LE。
                // 必须先禁止输出并解除锁存，再改地址，最后使能并锁存，避免切换瞬间误接通。
                board.WriteDO("OUT5,OUT6", "1,1");
                board.WriteDO("OUT1,OUT2,OUT3,OUT4", string.Join(",", Enumerable.Range(0, 4).Select(bit => (selection & (1 << bit)) != 0 ? "1" : "0")));
                Thread.Sleep(Math.Max(20, (int)FCT_InputDouble(socketIndex, "SwitchDelayMs", 50)));
                board.WriteDO("OUT5", "0");
                return;
            }
            if ((operation ?? string.Empty).Equals("Disable15", StringComparison.OrdinalIgnoreCase))
            {
                board.WriteDO("OUT5,OUT6", "1,0");
                board.WriteDO("OUT1,OUT2,OUT3,OUT4", "0,0,0,0");
                Thread.Sleep(Math.Max(20, (int)FCT_InputDouble(socketIndex, "SwitchDelayMs", 50)));
                return;
            }
            if (!(operation ?? string.Empty).Equals("SetDO", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsupported SHT relay operation: " + operation);
            string[] channelTexts = FCT_InputString(socketIndex, "Channels", FCT_InputString(socketIndex, "Channel", "0")).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string[] valueTexts = FCT_InputString(socketIndex, "Values", FCT_InputString(socketIndex, "Value", "0")).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (channelTexts.Length == 0 || channelTexts.Length != valueTexts.Length) throw new InvalidOperationException("Relay channels and values must have the same non-zero count.");
            if (ReferenceEquals(board, RelayHvMux)) foreach (string channelText in channelTexts) { string normalized = channelText.Trim().ToUpperInvariant().Replace("OUT", string.Empty); int channel; if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out channel) || channel < 1 || channel > 15) throw new ArgumentOutOfRangeException("Channels", "高压继电器卡只允许OUT1到OUT15。"); }
            board.WriteDO(string.Join(",", channelTexts), string.Join(",", valueTexts));
        }

        private static int FCT_SelectedPortAt(string parameter, int fallback)
        {
            string[] values = (parameter ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            int result; return values.Length > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static byte FCT_SelectedSlave(string parameter, byte fallback)
        {
            string[] values = (parameter ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            byte result; return values.Length > 1 && byte.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        public void FCT_CANSignal(int socketIndex)
        {
            lock (_fctGenericLocker) { FCT_CANSignalCore(socketIndex); }
        }

        private void FCT_CANSignalCore(int socketIndex)
        {
            {
                string operation = FCT_InputString(socketIndex, "Operation", "Read");
                uint addressOffset = (uint)FCT_InputDouble(socketIndex, "AddrOffset", 0);
                int tableIndex = (int)FCT_InputDouble(socketIndex, "TableIndex", 0);
                int dataSize = (int)FCT_InputDouble(socketIndex, "DataSize", 4);
                string dataType = FCT_InputString(socketIndex, "DataType", dataSize == 4 ? "float32" : "uint8");
                bool bigEndian = FCT_InputString(socketIndex, "Endian", "Little").Equals("Big", StringComparison.OrdinalIgnoreCase);
                object result;
                if (operation.Equals("Write", StringComparison.OrdinalIgnoreCase))
                {
                    string valueText = FCT_InputString(socketIndex, "ValueText", string.Empty);
                    if (valueText.Length == 0) valueText = FCT_InputDouble(socketIndex, "Value", 0).ToString(CultureInfo.InvariantCulture);
                    byte[] bytes = FCT_EncodeValue(dataType, dataSize, valueText, bigEndian);
                    FCT_Log(socketIndex, string.Format(CultureInfo.InvariantCulture, "CAN SIGNAL WRITE table=0x{0:X} offset=0x{1:X} type={2} data={3}", addressOffset, tableIndex, dataType, BitConverter.ToString(bytes).Replace("-", " ")));
                    uint tableAddress = FCT_GetTableAddress(addressOffset);
                    FCT_WriteAddressBytes(tableAddress + (uint)tableIndex, bytes);
                    if (FCT_InputBool(socketIndex, "VerifyAfterWrite", true))
                    {
                        byte[] actual = FCT_ReadAddressBytes(tableAddress + (uint)tableIndex, dataSize);
                        if (!actual.SequenceEqual(bytes)) throw new InvalidOperationException("CAN signal write verification failed.");
                    }
                    result = valueText;
                }
                else
                {
                    uint tableAddress = FCT_GetTableAddress(addressOffset);
                    byte[] bytes = FCT_ReadAddressBytes(tableAddress + (uint)tableIndex, dataSize);
                    result = FCT_DecodeValue(dataType, bytes, bigEndian);
                    FCT_Log(socketIndex, string.Format(CultureInfo.InvariantCulture, "CAN SIGNAL READ table=0x{0:X} offset=0x{1:X} type={2} raw={3} value={4}", addressOffset, tableIndex, dataType, BitConverter.ToString(bytes).Replace("-", " "), Convert.ToString(result, CultureInfo.InvariantCulture)));
                }
                FCT_SaveOutputVariable(socketIndex, result);
                FCT_RecordResult(socketIndex, result);
            }
        }

        public void FCT_CANCalculatedResults(int socketIndex)
        {
            lock (_fctGenericLocker) { FCT_CANCalculatedResultsCore(socketIndex); }
        }

        private void FCT_CANCalculatedResultsCore(int socketIndex)
        {
            {
                uint addressOffset = (uint)FCT_InputDouble(socketIndex, "AddrOffset", 0); int tableLength = (int)FCT_InputDouble(socketIndex, "TableLength", 0); string calculation = FCT_InputString(socketIndex, "CalculationType", string.Empty); if (FCT_InputBool(socketIndex, "AutoProductProfile", false)) FCT_ResolveCalculatedResultProfile(socketIndex, calculation, ref addressOffset, ref tableLength); if (tableLength <= 0 || tableLength > 65535) throw new ArgumentOutOfRangeException("TableLength"); uint tableAddress = FCT_GetTableAddress(addressOffset, (int)FCT_InputDouble(socketIndex, "PointerDepth", 1)); byte[] table = FCT_ReadAddressBytes(tableAddress, tableLength); string stepName = FCT_InputString(socketIndex, "StepName", "Calculated CAN result"); FCT_Log(socketIndex, "CAN CALCULATED READ product=" + FCT_InputString(socketIndex, "Product", "") + " drive=" + FCT_InputString(socketIndex, "DriveTarget", "") + " table=0x" + addressOffset.ToString("X") + " length=" + tableLength + " raw=" + BitConverter.ToString(table).Replace("-", " "));
                if (calculation.Equals("ThreePhaseCurrentRms", StringComparison.OrdinalIgnoreCase))
                {
                    JObject inputs = JObject.Parse(FCT_InputString(socketIndex, "InputsJson", "{}")); double a = FCT_PeakPairRms(table, inputs, "PhaseA_Min", "PhaseA_Max"); double b = FCT_PeakPairRms(table, inputs, "PhaseB_Min", "PhaseB_Max"); double c = FCT_PeakPairRms(table, inputs, "PhaseC_Min", "PhaseC_Max"); double average = (a + b + c) / 3.0; double imbalance = new[] { a, b, c }.Max() - new[] { a, b, c }.Min(); double low = FCT_InputDouble(socketIndex, "LowLimit", double.MinValue); double high = FCT_InputDouble(socketIndex, "HighLimit", double.MaxValue); string compare = FCT_InputString(socketIndex, "Comtype", "GELE"); string unit = FCT_InputString(socketIndex, "Unit", "A"); bool publishPhases = FCT_InputBool(socketIndex, "PublishPhases", true); if (publishPhases) { MySequenceManage.AddNumericTesting(socketIndex, stepName + " / PhaseA RMS", a, compare, low, high, unit, ""); MySequenceManage.AddNumericTesting(socketIndex, stepName + " / PhaseB RMS", b, compare, low, high, unit, ""); MySequenceManage.AddNumericTesting(socketIndex, stepName + " / PhaseC RMS", c, compare, low, high, unit, ""); } MySequenceManage.AddNumericTesting(socketIndex, stepName + " / Actual Current", average, compare, low, high, unit, "Three-phase average RMS"); MySequenceManage.AddNumericTesting(socketIndex, stepName + " / Current Imbalance", imbalance, "GELE", FCT_InputDouble(socketIndex, "ImbalanceLowLimit", 0), FCT_InputDouble(socketIndex, "ImbalanceHighLimit", double.MaxValue), unit, "Max phase RMS minus min phase RMS"); FCT_Log(socketIndex, string.Format(CultureInfo.InvariantCulture, "THREE PHASE RMS A={0:0.###} B={1:0.###} C={2:0.###} AVG={3:0.###} DIFF={4:0.###}", a, b, c, average, imbalance)); return;
                }
                if (calculation.Equals("PackedFaultStatus", StringComparison.OrdinalIgnoreCase))
                {
                    string adaptiveProduct = FCT_InputString(socketIndex, "Product", string.Empty).Trim().ToUpperInvariant(); bool singleDriveProfile = FCT_InputBool(socketIndex, "AutoProductProfile", false) && (adaptiveProduct == "C91" || adaptiveProduct == "C95"); JArray mapping = singleDriveProfile ? FCT_SingleDriveFaultMap() : JArray.Parse(FCT_InputString(socketIndex, "FaultMapJson", "[]")); List<string> active = new List<string>(); foreach (JObject item in mapping.OfType<JObject>()) { int byteIndex = (int?)item["Byte"] ?? -1; int bit = (int?)item["Bit"] ?? -1; bool activeLow = (bool?)item["ActiveLow"] == true; if (byteIndex < 0 || byteIndex >= table.Length || bit < 0 || bit > 7) continue; bool set = (table[byteIndex] & (1 << bit)) != 0; if (activeLow ? !set : set) active.Add((string)item["Name"] ?? ("Byte" + byteIndex + "Bit" + bit)); } int rampOffset = singleDriveProfile ? 0 : (int)FCT_InputDouble(socketIndex, "RampOffset", -1); int statusOffset = singleDriveProfile ? 1 : (int)FCT_InputDouble(socketIndex, "StatusOffset", -1); string noFault = FCT_InputString(socketIndex, "NoFaultText", "No active fault bits"); string summary = active.Count == 0 ? noFault : string.Join("; ", active); if (rampOffset >= 0 && rampOffset < table.Length) summary += "; Ramp=" + table[rampOffset].ToString(CultureInfo.InvariantCulture); if (statusOffset >= 0 && statusOffset < table.Length) summary += "; Status=" + table[statusOffset].ToString(CultureInfo.InvariantCulture); if (FCT_InputBool(socketIndex, "JudgeNoFault", true)) MySequenceManage.AddStringTesting(socketIndex, stepName, summary, string.Empty, noFault + (rampOffset >= 0 ? "; Ramp=" + table[rampOffset].ToString(CultureInfo.InvariantCulture) : string.Empty) + (statusOffset >= 0 ? "; Status=" + table[statusOffset].ToString(CultureInfo.InvariantCulture) : string.Empty), "RAW=" + BitConverter.ToString(table).Replace("-", " ")); else MySequenceManage.AddCustomString(socketIndex, stepName, summary, "RAW=" + BitConverter.ToString(table).Replace("-", " ")); FCT_Log(socketIndex, "FAULT SUMMARY " + summary); return;
                }
                throw new InvalidOperationException("Unsupported CalculationType: " + calculation);
            }
        }

        private static JArray FCT_SingleDriveFaultMap()
        {
            string[][] names = { new[] { "A相过流", "B相过流", "C相过流", "A相硬件过流", "B相硬件过流", "C相硬件过流", "母线欠压", "母线过压" }, new[] { "母线硬件过压", "板温故障", "AL1相温度故障", "BL1相温度故障", "CL1相温度故障", "AU1相温度故障", "BU1相温度故障", "CU1相温度故障" }, new[] { "电机1温度故障", "HI退饱和故障", "电机2温度故障", "CHI退饱和故障", "LO退饱和故障", "BLO退饱和故障", "CLO退饱和故障", "上桥臂欠压" }, new[] { "下桥臂欠压", "主故障置位", "电机超速", "保留位3", "保留位4", "保留位5", "保留位6", "保留位7" } };
            JArray result = new JArray(); for (int byteIndex = 0; byteIndex < names.Length; byteIndex++) for (int bit = 0; bit < 8; bit++) result.Add(new JObject { ["Byte"] = byteIndex + 4, ["Bit"] = bit, ["Name"] = names[byteIndex][bit] }); return result;
        }

        private void FCT_ResolveCalculatedResultProfile(int socketIndex, string calculation, ref uint addressOffset, ref int tableLength)
        {
            string product = FCT_InputString(socketIndex, "Product", string.Empty).Trim().ToUpperInvariant(); string drive = FCT_InputString(socketIndex, "DriveTarget", "TM1").Trim().ToUpperInvariant(); bool current = calculation.Equals("ThreePhaseCurrentRms", StringComparison.OrdinalIgnoreCase);
            switch (product)
            {
                case "C91": addressOffset = current ? 0x74u : 0x64u; tableLength = current ? 36 : 9; drive = "MAIN"; break;
                case "C95": addressOffset = current ? 0x70u : 0x5Cu; tableLength = current ? 36 : 8; drive = "MAIN"; break;
                case "C92":
                case "C96": bool tm2 = drive == "TM2"; addressOffset = current ? (tm2 ? 0x94u : 0x7Cu) : (tm2 ? 0x84u : 0x6Cu); tableLength = current ? 40 : 10; break;
                default: throw new InvalidOperationException("Auto product profile does not support product: " + product);
            }
            FCT_CanDiagnostic("Calculated result profile resolved: Product=" + product + "; Drive=" + drive + "; Calculation=" + calculation + "; Offset=0x" + addressOffset.ToString("X") + "; Length=" + tableLength);
        }

        private static double FCT_PeakPairRms(byte[] table, JObject inputs, string minimumName, string maximumName) { double minimum = FCT_MappedNumber(table, inputs[minimumName] as JObject, minimumName); double maximum = FCT_MappedNumber(table, inputs[maximumName] as JObject, maximumName); return (Math.Abs(minimum) + Math.Abs(maximum)) / 2.828; }
        private static double FCT_MappedNumber(byte[] table, JObject definition, string name) { if (definition == null) throw new InvalidOperationException("Input mapping is missing: " + name); int offset = (int?)definition["Offset"] ?? -1; int size = (int?)definition["DataSize"] ?? 4; string type = (string)definition["DataType"] ?? "float32"; bool bigEndian = string.Equals((string)definition["Endian"], "Big", StringComparison.OrdinalIgnoreCase); if (offset < 0 || size <= 0 || offset + size > table.Length) throw new InvalidOperationException("Input mapping exceeds table: " + name); return Convert.ToDouble(FCT_DecodeValue(type, table.Skip(offset).Take(size).ToArray(), bigEndian), CultureInfo.InvariantCulture); }

        public void FCT_CANTable(int socketIndex)
        {
            lock (_fctGenericLocker) { FCT_CANTableCore(socketIndex); }
        }

        private void FCT_CANTableCore(int socketIndex)
        {
            {
                string operation = FCT_InputString(socketIndex, "Operation", "Read");
                uint addressOffset = (uint)FCT_InputDouble(socketIndex, "AddrOffset", 0);
                int tableLength = (int)FCT_InputDouble(socketIndex, "TableLength", 0);
                if (tableLength <= 0 || tableLength > 65535) throw new ArgumentOutOfRangeException("TableLength");
                uint tableAddress = FCT_GetTableAddress(addressOffset, (int)FCT_InputDouble(socketIndex, "PointerDepth", 1));
                byte[] table = FCT_ReadAddressBytes(tableAddress, tableLength);
                FCT_Log(socketIndex, string.Format(CultureInfo.InvariantCulture, "CAN TABLE {0} table=0x{1:X} length={2} BEFORE={3}", operation.ToUpperInvariant(), addressOffset, tableLength, BitConverter.ToString(table).Replace("-", " ")));
                if (operation.Equals("Write", StringComparison.OrdinalIgnoreCase))
                {
                    string changesJson = FCT_InputString(socketIndex, "ChangesJson", "[]");
                    JArray changes = JArray.Parse(changesJson);
                    if (addressOffset == 0x80 && tableLength == 31)
                    {
                        Dictionary<int, string> tm2Defaults = new Dictionary<int, string> { { 0x00, "0" }, { 0x08, "20" }, { 0x0C, "50" }, { 0x0E, "10" }, { 0x10, "60" }, { 0x14, "4" }, { 0x16, "10000" }, { 0x18, "1" }, { 0x19, "255" }, { 0x1A, "0" }, { 0x1E, "0" } }; foreach (JObject change in changes.OfType<JObject>()) { int offset = (int?)change["Offset"] ?? -1; string fixedValue; if (tm2Defaults.TryGetValue(offset, out fixedValue)) change["Value"] = fixedValue; if (offset == 0x19) { change["WriteLast"] = true; change["WriteFinal"] = true; } }
                        FCT_Log(socketIndex, "C96/C92 TM2 31-byte Motor Control fixed fields normalized; target current at offset 0x04 preserved.");
                    }
                    List<Tuple<int, byte[], bool>> writeLast = new List<Tuple<int, byte[], bool>>();
                    bool[] verifyMask = new bool[table.Length];
                    foreach (JObject change in changes.OfType<JObject>())
                    {
                        int offset = (int?)change["Offset"] ?? -1;
                        int size = (int?)change["DataSize"] ?? 0;
                        string type = (string)change["DataType"] ?? "uint8";
                        string value = Convert.ToString(change["Value"], CultureInfo.InvariantCulture) ?? string.Empty;
                        bool bigEndian = string.Equals((string)change["Endian"], "Big", StringComparison.OrdinalIgnoreCase);
                        byte[] encoded; try { encoded = FCT_EncodeValue(type, size, value, bigEndian); } catch (Exception ex) { string signalName = (string)change["Name"] ?? "offset 0x" + offset.ToString("X"); throw new InvalidOperationException("CAN table value is invalid: " + signalName + ", type=" + type + ", value=" + value + ", size=" + size, ex); }
                        if (offset < 0 || offset + encoded.Length > table.Length) throw new IndexOutOfRangeException("Table change exceeds table length: " + offset);
                        if ((bool?)change["WriteLast"] == true)
                        {
                            string signalName = (string)change["Name"] ?? string.Empty;
                            bool finalTrigger = (bool?)change["WriteFinal"] == true || signalName.Replace("_", string.Empty).IndexOf("NewData", StringComparison.OrdinalIgnoreCase) >= 0 || encoded.Any(valueByte => valueByte == 0xFF);
                            Array.Clear(table, offset, encoded.Length);
                            writeLast.Add(Tuple.Create(offset, encoded, finalTrigger));
                        }
                        else { Buffer.BlockCopy(encoded, 0, table, offset, encoded.Length); for (int verifyIndex = 0; verifyIndex < encoded.Length; verifyIndex++) verifyMask[offset + verifyIndex] = true; }
                    }
                    FCT_WriteAddressBytes(tableAddress, table);
                    if (writeLast.Count > 0) FCT_Log(socketIndex, "CAN TABLE trigger fields were armed low in the base write before final trigger transmission.");
                    foreach (Tuple<int, byte[], bool> trigger in writeLast.OrderBy(value => value.Item3 ? 1 : 0)) { FCT_WriteAddressBytes(tableAddress + (uint)trigger.Item1, trigger.Item2); Buffer.BlockCopy(trigger.Item2, 0, table, trigger.Item1, trigger.Item2.Length); FCT_Log(socketIndex, "CAN TABLE TRIGGER " + (trigger.Item3 ? "FINAL" : "LAST") + " offset=0x" + trigger.Item1.ToString("X") + " data=" + BitConverter.ToString(trigger.Item2).Replace("-", " ")); }
                    FCT_Log(socketIndex, "CAN TABLE WRITE AFTER=" + BitConverter.ToString(table).Replace("-", " "));
                    if (FCT_InputBool(socketIndex, "VerifyAfterWrite", true))
                    {
                        byte[] actual = FCT_ReadAddressBytes(tableAddress, tableLength);
                        List<int> mismatches = Enumerable.Range(0, tableLength).Where(index => verifyMask[index] && actual[index] != table[index]).ToList();
                        if (mismatches.Count > 0) { string detail = string.Join(", ", mismatches.Take(16).Select(index => "0x" + index.ToString("X") + ":expected=" + table[index].ToString("X2") + "/actual=" + actual[index].ToString("X2"))); FCT_Log(socketIndex, "CAN TABLE VERIFY FAILED " + detail); throw new InvalidOperationException("CAN table write verification failed at " + detail); }
                        FCT_Log(socketIndex, "CAN TABLE VERIFY PASSED fields=" + verifyMask.Count(value => value) + "; trigger fields intentionally ignored after send.");
                    }
                }
                string hex = BitConverter.ToString(table).Replace("-", " ");
                FCT_SaveOutputVariable(socketIndex, hex);
                JArray checks = new JArray(); try { checks = JArray.Parse(FCT_InputString(socketIndex, "SignalChecksJson", "[]")); } catch { }
                if (checks.Count > 0 && !operation.Equals("Write", StringComparison.OrdinalIgnoreCase))
                {
                    string parentName = FCT_InputString(socketIndex, "StepName", "Read table");
                    foreach (JObject check in checks.OfType<JObject>())
                    {
                        string name = (string)check["Name"] ?? "Signal"; int offset = (int?)check["Offset"] ?? -1; int size = (int?)check["DataSize"] ?? 0; string type = (string)check["DataType"] ?? "uint8"; bool bigEndian = string.Equals((string)check["Endian"], "Big", StringComparison.OrdinalIgnoreCase); if (offset < 0 || size <= 0 || offset + size > table.Length) throw new IndexOutOfRangeException("Table read assertion exceeds table length: " + name);
                        byte[] bytes = table.Skip(offset).Take(size).ToArray(); object value = FCT_DecodeValue(type, bytes, bigEndian); string resultMode = (string)check["ResultMode"] ?? "Information"; string resultName = parentName + " / " + name; string unit = (string)check["Unit"] ?? string.Empty; FCT_Log(socketIndex, "CAN TABLE SIGNAL " + name + " raw=" + BitConverter.ToString(bytes).Replace("-", " ") + " value=" + Convert.ToString(value, CultureInfo.InvariantCulture));
                        if (resultMode.Equals("NumericLimit", StringComparison.OrdinalIgnoreCase)) MySequenceManage.AddNumericTesting(socketIndex, resultName, Convert.ToDouble(value, CultureInfo.InvariantCulture), (string)check["Comtype"] ?? "GELE", (double?)check["LowLimit"] ?? double.MinValue, (double?)check["HighLimit"] ?? double.MaxValue, unit, string.Empty);
                        else if (resultMode.Equals("StringLimit", StringComparison.OrdinalIgnoreCase)) MySequenceManage.AddStringTesting(socketIndex, resultName, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, string.Empty, (string)check["Limit"] ?? string.Empty, string.Empty);
                        else MySequenceManage.AddCustomString(socketIndex, resultName, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, unit);
                    }
                }
                else FCT_RecordResult(socketIndex, hex);
            }
        }

        public void FCT_ExecuteLogic(int socketIndex)
        {
            string operation = FCT_InputString(socketIndex, "Operation", string.Empty);
            FCT_Log(socketIndex, "LOGIC " + operation);
            switch (operation.ToUpperInvariant())
            {
                case "DELAY":
                    Thread.Sleep(Math.Max(0, (int)FCT_InputDouble(socketIndex, "TimeMs", 0)));
                    break;
                case "SETVARIABLE":
                    FCT_SetVariable(socketIndex, FCT_InputString(socketIndex, "VariableName", string.Empty), FCT_InputString(socketIndex, "ValueText", string.Empty));
                    break;
                case "GOTO":
                    MySequenceManage.GotoByStepName(socketIndex, FCT_InputString(socketIndex, "TargetStepName", string.Empty));
                    break;
                case "FIXEDLOOP":
                    FCT_RunFixedLoop(socketIndex);
                    break;
                case "CONDITION":
                    FCT_RunCondition(socketIndex);
                    break;
                case "LABEL":
                    break;
                case "SAFESHUTDOWN":
                    FCT_GenericSafeShutdown();
                    break;
                case "STOP":
                    MySequenceManage.StopTest(socketIndex);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported logic operation: " + operation);
            }
        }

        public string FCT_GetRuntimeSnapshot(int socketIndex)
        {
            lock (_fctGenericLocker)
            {
                JObject snapshot = new JObject();
                Dictionary<string, object> variables; if (_fctVariables.TryGetValue(socketIndex, out variables)) snapshot["Variables"] = JObject.FromObject(variables); else snapshot["Variables"] = new JObject();
                Dictionary<string, int> loops; if (_fctLoopCounters.TryGetValue(socketIndex, out loops)) snapshot["Loops"] = JObject.FromObject(loops); else snapshot["Loops"] = new JObject();
                return snapshot.ToString(Newtonsoft.Json.Formatting.None);
            }
        }

        private object FCT_Lvdc(int socketIndex, string operation) { return FCT_Lvdc(socketIndex, operation, LVDC); }
        private object FCT_Lvdc(int socketIndex, string operation, Instruments.PowerSupply.IT6xxxC supply)
        {
            if (supply == null) throw new InvalidOperationException("低压电源在当前工位没有实例。请在仪器中心为该工位分配并填写连接资源。");
            switch (operation.ToUpperInvariant())
            {
                case "SETVOLTAGE": supply.SetSourceVoltage(FCT_InputDouble(socketIndex, "Voltage", 0)); return null;
                case "SETCURRENT": supply.SetSourceCurrent(FCT_InputDouble(socketIndex, "Current", 0)); return null;
                case "SETOUTPUT": supply.SetOutput(FCT_InputBool(socketIndex, "Output", false)); return null;
                case "READVOLTAGE": double voltage; supply.GetActPower(out voltage); return voltage;
                case "READCURRENT": double current; supply.GetActCurrent(out current); return current;
                default: throw new InvalidOperationException("Unsupported LVDC operation: " + operation);
            }
        }

        private object FCT_Hvdc(int socketIndex, string operation) { return FCT_Hvdc(socketIndex, operation, HVDC); }
        private object FCT_Hvdc(int socketIndex, string operation, Instruments.PowerSupply.Kewell_C3000 supply)
        {
            if (supply == null) throw new InvalidOperationException("高压电源在当前工位没有实例。请在仪器中心为该工位分配并填写连接资源。");
            switch (operation.ToUpperInvariant())
            {
                case "SETVOLTAGE":
                {
                    // Kewell holding 0x07D0: Source/CV(0) vs Load(20). Prefer Source before voltage write.
                    FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
                    double setVoltage = FCT_InputDouble(socketIndex, "Voltage", FCT_InputDouble(socketIndex, "SourceVoltage", 0));
                    supply.SetSourceVoltage(setVoltage);
                    Thread.Sleep(300);
                    double setting;
                    bool readOk = false;
                    try
                    {
                        supply.GetSettingVoltage(out setting);
                        readOk = setting > -900;
                    }
                    catch (Exception ex)
                    {
                        setting = double.NaN;
                        FCT_Log(socketIndex, "HVDC GetSettingVoltage failed: " + ex.Message);
                    }
                    if (readOk && Math.Abs(setting - setVoltage) > Math.Max(1.0, Math.Abs(setVoltage) * 0.1 + 0.2))
                    {
                        FCT_Log(socketIndex, "HVDC voltage mismatch after first write: requested=" + setVoltage.ToString(CultureInfo.InvariantCulture) + "V readback=" + setting.ToString(CultureInfo.InvariantCulture) + "V; retrying");
                        FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
                        supply.SetSourceVoltage(setVoltage);
                        Thread.Sleep(300);
                        try
                        {
                            supply.GetSettingVoltage(out setting);
                            readOk = setting > -900;
                        }
                        catch (Exception ex)
                        {
                            readOk = false;
                            FCT_Log(socketIndex, "HVDC GetSettingVoltage retry failed: " + ex.Message);
                        }
                    }
                    if (readOk)
                        FCT_Log(socketIndex, "HVDC setpoint voltage=" + setting.ToString(CultureInfo.InvariantCulture) + "V (requested " + setVoltage.ToString(CultureInfo.InvariantCulture) + "V)");
                    else
                        FCT_Log(socketIndex, "HVDC SetSourceVoltage sent " + setVoltage.ToString(CultureInfo.InvariantCulture) + "V (setpoint readback unavailable; check front-panel Remote/CV)");
                    return null;
                }
                case "SETCURRENT":
                {
                    FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
                    double setCurrent = FCT_InputDouble(socketIndex, "Current", FCT_InputDouble(socketIndex, "SourceCurrent", 0));
                    supply.SetSourceCurrent(setCurrent);
                    Thread.Sleep(200);
                    try
                    {
                        double setting;
                        supply.GetSettingCurrent(out setting);
                        FCT_Log(socketIndex, "HVDC current limit=" + setting.ToString(CultureInfo.InvariantCulture) + "A (requested " + setCurrent.ToString(CultureInfo.InvariantCulture) + "A)");
                    }
                    catch (Exception ex) { FCT_Log(socketIndex, "HVDC GetSettingCurrent failed: " + ex.Message); }
                    return null;
                }
                case "SETPOWER":
                {
                    FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
                    double setPower = FCT_InputDouble(socketIndex, "Power", FCT_InputDouble(socketIndex, "SourcePower", 0));
                    supply.SetSourcePower(setPower);
                    Thread.Sleep(200);
                    try
                    {
                        double setting;
                        supply.GetSettingPower(out setting);
                        FCT_Log(socketIndex, "HVDC power limit=" + setting.ToString(CultureInfo.InvariantCulture) + "W (requested " + setPower.ToString(CultureInfo.InvariantCulture) + "W)");
                    }
                    catch (Exception ex) { FCT_Log(socketIndex, "HVDC GetSettingPower failed: " + ex.Message); }
                    return null;
                }
                case "SETOUTPUT":
                {
                    FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
                    bool output = FCT_InputBool(socketIndex, "Output", false);
                    if (output)
                    {
                        try
                        {
                            double iLimit;
                            supply.GetSettingCurrent(out iLimit);
                            if (iLimit > -900 && iLimit < 0.05)
                                FCT_Log(socketIndex, "HVDC warning: current limit≈" + iLimit.ToString(CultureInfo.InvariantCulture) + "A before Output ON — set Current first if output stays 0V");
                        }
                        catch { }
                    }
                    // Output is holding 0x07D1 only. Do not rewrite mode after Output ON —
                    // some firmwares drop the output latch when 0x07D0 is written again.
                    supply.SetOutput(output);
                    if (output) Thread.Sleep(300);
                    FCT_Log(socketIndex, "HVDC output=" + (output ? "ON" : "OFF"));
                    return null;
                }
                case "READVOLTAGE":
                    double voltage;
                    supply.GetActVoltage(out voltage);
                    return voltage;
                case "READCURRENT":
                    double current;
                    supply.GetActCurrent(out current);
                    return current;
                case "READPOWER":
                    double power;
                    supply.GetActPower(out power);
                    return power;
                default: throw new InvalidOperationException("Unsupported HVDC operation: " + operation);
            }
        }

        /// <summary>
        /// Kewell holding 0x07D0: 0=Source/CV, 20=Load. Voltage setpoint only applies in Source/CV.
        /// </summary>
        private void FCT_KewellEnsureSourceCvMode(Instruments.PowerSupply.Kewell_C3000 supply, int? socketIndex, bool soft = false)
        {
            if (supply == null) throw new InvalidOperationException("HVDC instance is null.");
            try
            {
                supply.SetSourceOrLoadMode(true);
                Thread.Sleep(250);
                if (socketIndex.HasValue) FCT_Log(socketIndex.Value, "HVDC SetSourceOrLoadMode(Source/CV)");
            }
            catch (Exception ex)
            {
                if (soft)
                {
                    if (socketIndex.HasValue) FCT_Log(socketIndex.Value, "HVDC SetSourceOrLoadMode soft-fail: " + ex.Message);
                    return;
                }
                throw new InvalidOperationException("高压源切换恒压(Source/CV)模式失败：" + ex.Message, ex);
            }
        }

        private void FCT_KewellSetOutput(Instruments.PowerSupply.Kewell_C3000 supply, bool on, int? socketIndex)
        {
            if (supply == null) throw new InvalidOperationException("HVDC instance is null.");
            FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
            supply.SetOutput(on);
            if (on) Thread.Sleep(200);
            // Do not rewrite Source/CV after Output ON — keeps the output latch stable.
            if (!on) FCT_KewellEnsureSourceCvMode(supply, socketIndex, soft: true);
            if (socketIndex.HasValue) FCT_Log(socketIndex.Value, "HVDC output=" + (on ? "ON" : "OFF"));
        }

        private object FCT_Dmm(int socketIndex, string operation) { return FCT_Dmm(socketIndex, operation, DMM); }
        private object FCT_Dmm(int socketIndex, string operation, Instruments.DMM.KeySight34461A meter)
        {
            if (meter == null) throw new InvalidOperationException("万用表在当前工位没有实例。请在仪器中心为该工位分配并填写连接资源。");
            switch (operation.ToUpperInvariant())
            {
                case "INIT": meter.InitDMM(); return null;
                case "RESET": meter.RST(); return null;
                case "IDENTIFY": string identity; meter.IDN(out identity); return identity;
                case "CONFIGMEASURE":
                {
                    string typeText = FCT_InputString(socketIndex, "MeasureType", "DCVoltage");
                    int separator = typeText.IndexOf(" - ", StringComparison.Ordinal); if (separator > 0) typeText = typeText.Substring(0, separator).Trim();
                    Instruments.DMM.MeasureTypes measureType; if (!Enum.TryParse(typeText, true, out measureType)) throw new InvalidOperationException("Unsupported DMM measure type: " + typeText);
                    meter.ConfigDMMforMeasure(measureType); return null;
                }
                case "CONFIGDCVOLTAGE": meter.ConfigDMMforDC(FCT_InputDouble(socketIndex, "Range", 1000), FCT_InputDouble(socketIndex, "Solution", 0.01)); return null;
                case "CONFIGDCCURRENT": meter.ConfigDMMforDCCurrent(FCT_InputDouble(socketIndex, "Range", 3), FCT_InputDouble(socketIndex, "Solution", 0.00001)); return null;
                case "CONFIGACVOLTAGE": meter.ConfigDMMforAC(FCT_InputDouble(socketIndex, "Range", 1000), FCT_InputDouble(socketIndex, "Solution", 0.01)); return null;
                case "CONFIGACCURRENT": meter.ConfigDMMforACCurrent(FCT_InputDouble(socketIndex, "Range", 3), FCT_InputDouble(socketIndex, "Solution", 0.00001)); return null;
                case "CONFIGRESISTANCE": meter.ConfigDMMForRES(FCT_InputDouble(socketIndex, "Range", -1), FCT_InputDouble(socketIndex, "Solution", -1)); return null;
                case "CONFIGFREQUENCY": meter.ConfigDMMforFREQ(FCT_InputDouble(socketIndex, "Range", -1), FCT_InputDouble(socketIndex, "Solution", -1)); return null;
                case "CONFIGCALCULATEMAXIMUM": meter.ConfigDMMforCALulate(); return null;
                case "READ": return meter.GetMeasureValue();
                case "CLOSE": meter.CloseSession(); return null;
                default: throw new InvalidOperationException("Unsupported DMM operation: " + operation);
            }
        }

        /// <summary>Station-aware programmable resistance, mirroring the legacy <c>RES_SetResistance</c> STEP.</summary>
        private void FCT_Res(int socketIndex, Instruments.Other.NGI_ProgramResistance resistance)
        {
            if (ReferenceEquals(resistance, RES)) { RES_SetResistance(socketIndex); return; }
            if (resistance == null) throw new InvalidOperationException("程控电阻在当前工位没有实例。请在仪器中心为该工位分配并填写连接资源。");
            resistance.SetResistance(FCT_InputDouble(socketIndex, "ResValue", 1000), (int)FCT_InputDouble(socketIndex, "Channel", 1));
        }

        private object FCT_ReadDaq(int socketIndex)
        {
            int channel = (int)FCT_InputDouble(socketIndex, "Channel", 0);
            string hardware = FCT_InputString(socketIndex, "Hardware", "PCI6229");
            if (hardware.Equals("NI9227", StringComparison.OrdinalIgnoreCase))
            {
                string physical = FCT_InputString(socketIndex, "PhysicalChannel", "cDAQ1Mod1/ai" + channel);
                double secondaryAmps = FCT_ReadNi9227Current(physical);
                double ratio = FCT_InputDouble(socketIndex, "Ratio", 1.0);
                return secondaryAmps * ratio + FCT_InputDouble(socketIndex, "Offset", 0);
            }
            double aiValue = PCI6320.ReadValue("Dev1/ai" + channel, -10, 10, string.Empty);
            double scale = FCT_InputDouble(socketIndex, "Scale", 1500.0 / 68.0);
            double offset = FCT_InputDouble(socketIndex, "Offset", 0);
            return aiValue * scale + offset;
        }

        private static double FCT_ReadNi9227Current(string physicalChannel)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value => value.GetName().Name == "NationalInstruments.DAQmx") ?? Assembly.Load("NationalInstruments.DAQmx");
            Type taskType = assembly.GetType("NationalInstruments.DAQmx.Task", true); object task = Activator.CreateInstance(taskType);
            try
            {
                object channels = taskType.GetProperty("AIChannels").GetValue(task, null);
                MethodInfo create = channels.GetType().GetMethods().Where(value => value.Name == "CreateCurrentChannel").OrderBy(value => value.GetParameters().Length).First();
                ParameterInfo[] parameters = create.GetParameters(); object[] args = new object[parameters.Length]; int doubleIndex = 0;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type type = parameters[i].ParameterType;
                    if (type == typeof(string)) args[i] = i == 0 ? physicalChannel : string.Empty;
                    else if (type == typeof(double)) args[i] = doubleIndex++ == 0 ? -5.0 : doubleIndex == 2 ? 5.0 : 0.0;
                    else if (type.IsEnum) args[i] = Enum.Parse(type, type.Name.IndexOf("CurrentUnits", StringComparison.OrdinalIgnoreCase) >= 0 ? "Amps" : "Default", true);
                    else args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
                }
                create.Invoke(channels, args);
                object stream = taskType.GetProperty("Stream").GetValue(task, null); Type readerType = assembly.GetType("NationalInstruments.DAQmx.AnalogSingleChannelReader", true); object reader = Activator.CreateInstance(readerType, stream);
                return Convert.ToDouble(readerType.GetMethod("ReadSingleSample").Invoke(reader, null), CultureInfo.InvariantCulture);
            }
            finally { IDisposable disposable = task as IDisposable; if (disposable != null) disposable.Dispose(); }
        }

        private object FCT_Resolver(int socketIndex, string operation)
        {
            if (Resolver == null) throw new InvalidOperationException("旋变模拟器未初始化。请在仪器中心勾选 RESOLVERCAN 并执行初始化。");
            switch (operation.ToUpperInvariant())
            {
                case "INIT": Resolver_Init(socketIndex); break;
                case "SETSPEED": Resolver.DBC_SendSignalValue("2147483649_mode_switch", 0, true); Thread.Sleep(50); Resolver.DBC_SendSignalValue("2505419280_Polarpair", FCT_InputDouble(socketIndex, "PolePairs", 6), false); Resolver.DBC_SendSignalValue("2505419280_Speed", FCT_InputDouble(socketIndex, "Speed", 0), true); Thread.Sleep(500); break;
                case "SETPOSITION": Resolver.DBC_SendSignalValue("2505419280_Polarpair", FCT_InputDouble(socketIndex, "PolePairs", 1), true); Thread.Sleep(50); Resolver.DBC_SendSignalValue("2147483649_mode_switch", 1, false); Resolver.DBC_SendSignalValue("2147483649_Position", FCT_InputDouble(socketIndex, "Position", 0), true); Thread.Sleep(500); break;
                case "SETPOLEPAIRS": Resolver.DBC_SendSignalValue("2505419280_Polarpair", FCT_InputDouble(socketIndex, "PolePairs", 6), true); break;
                case "SENDDBCSIGNAL": Resolver.DBC_SendSignalValue(FCT_InputString(socketIndex, "SignalName", string.Empty), FCT_InputDouble(socketIndex, "Value", 0), FCT_InputBool(socketIndex, "SendFlag", true)); break;
                case "STOP": Resolver_Stop(socketIndex); break;
                default: throw new InvalidOperationException("Unsupported resolver operation: " + operation);
            }
            return null;
        }

        private object FCT_ProductCan(int socketIndex, string operation)
        {
            if (MyCAN == null) throw new InvalidOperationException("产品CAN未初始化。请在仪器中心勾选 DUTCAN 并执行初始化。");
            switch (operation.ToUpperInvariant())
            {
                case "COMMUNICATIONINIT": DUT_ComucationInit(socketIndex); return null;
                case "ENTERFT": CAN_APP2FT(socketIndex); return null;
                case "WAKEUP": CAN_SendWakeUpMessage(socketIndex); return null;
                case "COMMUNICATIONTEST": Test_CANCommunication(socketIndex); return true;
                case "SENDDBCSIGNAL": MyCAN.DBC_SendSignalValue(FCT_InputString(socketIndex, "SignalName", string.Empty), FCT_InputDouble(socketIndex, "Value", 0), FCT_InputBool(socketIndex, "SendFlag", true)); return true;
                case "SENDRAW": { uint id = FCT_ParseCanId(FCT_InputString(socketIndex, "CanId", "0")); byte[] data = FCT_ParseHexBytes(FCT_InputString(socketIndex, "DataHex", string.Empty)); MyCAN.SendMessage(id, data); return BitConverter.ToString(data).Replace("-", " "); }
                case "RECEIVERAW": { uint filter = FCT_ParseCanId(FCT_InputString(socketIndex, "FilterId", "0")); List<Instruments.CAN.CANMessage> messages = new List<Instruments.CAN.CANMessage>(); MyCAN.ReceiveMessage(out messages); JArray frames = new JArray(); foreach (Instruments.CAN.CANMessage message in messages.Where(value => filter == 0 || (value.ID & 0x1FFFFFFF) == (filter & 0x1FFFFFFF))) frames.Add(new JObject { ["Id"] = (message.ID & 0x1FFFFFFF).ToString("X", CultureInfo.InvariantCulture), ["Data"] = BitConverter.ToString(message.DATA ?? new byte[0]).Replace("-", " ") }); return frames.ToString(Formatting.None); }
                default: throw new InvalidOperationException("Unsupported ProductCAN operation: " + operation);
            }
        }

        private static uint FCT_ParseCanId(string text) { string value = (text ?? string.Empty).Trim(); if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2); return uint.Parse(string.IsNullOrWhiteSpace(value) ? "0" : value, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
        private static byte[] FCT_ParseHexBytes(string text) { return (text ?? string.Empty).Split(new[] { ' ', ',', '-', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(value => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray(); }

        private object FCT_FlowAction(int socketIndex, string operation)
        {
            if (operation.Equals("Delay", StringComparison.OrdinalIgnoreCase)) { Thread.Sleep(Math.Max(0, (int)FCT_InputDouble(socketIndex, "TimeMs", 0))); return null; }
            throw new InvalidOperationException("Unsupported flow action: " + operation);
        }

        private object FCT_InvokeActionPlugin(int socketIndex, string device, string operation)
        {
            string assemblyPath = FCT_InputString(socketIndex, "PluginAssembly", string.Empty);
            string typeName = FCT_InputString(socketIndex, "PluginType", string.Empty);
            if (assemblyPath.Length == 0 || typeName.Length == 0) throw new InvalidOperationException("Unsupported generic device and no plugin was configured: " + device);
            if (!Path.IsPathRooted(assemblyPath)) assemblyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), assemblyPath);
            string key = assemblyPath + "|" + typeName; object plugin;
            if (!_fctActionPlugins.TryGetValue(key, out plugin))
            {
                Type type = Assembly.LoadFrom(assemblyPath).GetType(typeName, true);
                plugin = Activator.CreateInstance(type); _fctActionPlugins[key] = plugin;
            }
            MethodInfo execute = plugin.GetType().GetMethod("Execute", new[] { typeof(string), typeof(string) });
            if (execute == null) throw new MissingMethodException(typeName, "Execute(string operation, string parametersJson)");
            return execute.Invoke(plugin, new object[] { operation, FCT_InputString(socketIndex, "ParametersJson", "{}") });
        }

        private object FCT_AuxCan(int socketIndex, string operation)
        {
            if (operation.Equals("Disconnect", StringComparison.OrdinalIgnoreCase)) { FCT_StopAllAuxPeriodic(); if (_fctAuxCan != null) { try { _fctAuxCan.CloseCANDevice(); } catch { } } _fctAuxCan = null; return null; }
            if (operation.Equals("StopPeriodicDbc", StringComparison.OrdinalIgnoreCase)) { string key = FCT_InputString(socketIndex, "PeriodicKey", FCT_InputString(socketIndex, "MessageName", "AUX")); FCT_StopAuxPeriodic(key); FCT_Log(socketIndex, "AUX DBC PERIODIC STOP key=" + key); return true; }
            FCT_EnsureAuxCan(socketIndex);
            Instruments.CAN.CANWrapper aux = _fctAuxCan;
            if (operation.Equals("SendDbcSignals", StringComparison.OrdinalIgnoreCase))
            {
                string rawHex = FCT_InputString(socketIndex, "DataHex", string.Empty);
                if (!string.IsNullOrWhiteSpace(rawHex))
                {
                    uint rawId = FCT_ParseCanId(FCT_InputString(socketIndex, "CanId", "0"));
                    byte[] rawData = FCT_ParseHexBytes(rawHex);
                    aux.SendMessage(rawId, rawData);
                    FCT_Log(socketIndex, "AUX DBC RAW TX ID=0x" + rawId.ToString("X", CultureInfo.InvariantCulture) + " DATA=" + BitConverter.ToString(rawData).Replace("-", " "));
                    return true;
                }
                JObject signals = JObject.Parse(FCT_InputString(socketIndex, "SignalsJson", "{}"));
                FCT_SendAuxSignals(aux, signals);
                return true;
            }
            if (operation.Equals("StartPeriodicDbc", StringComparison.OrdinalIgnoreCase))
            {
                string key = FCT_InputString(socketIndex, "PeriodicKey", FCT_InputString(socketIndex, "MessageName", "AUX")); int period = Math.Max(20, (int)FCT_InputDouble(socketIndex, "PeriodMs", 100)); string rawHex = FCT_InputString(socketIndex, "DataHex", string.Empty); FCT_StopAuxPeriodic(key);
                if (!string.IsNullOrWhiteSpace(rawHex))
                {
                    uint rawId = FCT_ParseCanId(FCT_InputString(socketIndex, "CanId", "0")); byte[] rawData = FCT_ParseHexBytes(rawHex); int failures = 0; Timer timer = null;
                    timer = new Timer(_ => { try { lock (_fctAuxSendLocker) aux.SendMessage(rawId, rawData); failures = 0; } catch (Exception ex) { if (Interlocked.Increment(ref failures) >= 3) { FCT_StopAuxPeriodic(key); FCT_CanDiagnostic("AUX RAW PERIODIC AUTO STOP key=" + key + " after 3 failures", ex); } } }, null, 0, period);
                    _fctAuxPeriodicSenders[key] = timer; FCT_Log(socketIndex, "AUX RAW PERIODIC START key=" + key + " period=" + period + " ID=0x" + rawId.ToString("X", CultureInfo.InvariantCulture) + " DATA=" + BitConverter.ToString(rawData).Replace("-", " ")); return true;
                }
                JObject signals = JObject.Parse(FCT_InputString(socketIndex, "SignalsJson", "{}")); if (!signals.Properties().Any()) throw new InvalidOperationException("SignalsJson must contain at least one DBC signal."); string heartbeatSignal = FCT_InputString(socketIndex, "HeartbeatSignal", string.Empty); int heartbeat = heartbeatSignal.Length > 0 && signals[heartbeatSignal] != null ? (int)signals[heartbeatSignal] : 0; int dbcFailures = 0; Timer dbcTimer = null; dbcTimer = new Timer(_ => { try { if (heartbeatSignal.Length > 0) signals[heartbeatSignal] = Interlocked.Increment(ref heartbeat) & 0xFF; FCT_SendAuxSignals(aux, signals); dbcFailures = 0; } catch (Exception ex) { if (Interlocked.Increment(ref dbcFailures) >= 3) { FCT_StopAuxPeriodic(key); FCT_CanDiagnostic("AUX DBC PERIODIC AUTO STOP key=" + key + " after 3 failures", ex); } } }, null, 0, period); _fctAuxPeriodicSenders[key] = dbcTimer; FCT_Log(socketIndex, "AUX DBC PERIODIC START key=" + key + " period=" + period); return true;
            }
            if (operation.Equals("ReadDbcSignal", StringComparison.OrdinalIgnoreCase))
            {
                string signalName = FCT_InputString(socketIndex, "SignalName", string.Empty); if (signalName.Length == 0) throw new InvalidOperationException("SignalName is required."); int timeout = Math.Max(20, (int)FCT_InputDouble(socketIndex, "TimeoutMs", 1000)); DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout); Exception last = null; do { try { double value = 0; aux.DBC_ReceiveSingal(signalName, ref value); FCT_Log(socketIndex, "AUX DBC READ " + signalName + "=" + value.ToString(CultureInfo.InvariantCulture)); return value; } catch (Exception ex) { last = ex; Thread.Sleep(20); } } while (DateTime.UtcNow < deadline); throw new TimeoutException("DBC signal read timeout: " + signalName, last);
            }
            if (operation.Equals("SendRaw", StringComparison.OrdinalIgnoreCase))
            {
                uint id = Convert.ToUInt32(FCT_InputString(socketIndex, "CanId", "0"), 16);
                byte[] bytes = FCT_InputString(socketIndex, "DataHex", string.Empty).Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(value => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
                aux.SendMessage(id, bytes); return true;
            }
            if (operation.Equals("ReceiveRaw", StringComparison.OrdinalIgnoreCase))
            {
                List<Instruments.CAN.CANMessage> messages = new List<Instruments.CAN.CANMessage>();
                lock (_fctAuxSendLocker) aux.ReceiveMessage(out messages);
                List<Instruments.CAN.CANMessage> latest = messages
                    .GroupBy(message => message.ID & 0x1FFFFFFF)
                    .Select(group => group.Last())
                    .Take(64)
                    .ToList();
                JArray frames = new JArray();
                foreach (Instruments.CAN.CANMessage message in latest)
                {
                    byte[] data = message.DATA ?? new byte[0];
                    frames.Add(new JObject { ["Id"] = (message.ID & 0x1FFFFFFF).ToString("X", CultureInfo.InvariantCulture), ["Data"] = BitConverter.ToString(data).Replace("-", " ") });
                }
                FCT_Log(socketIndex, "AUX RX snapshot: received=" + messages.Count + "; latestIds=" + latest.Count);
                return frames.ToString(Formatting.None);
            }
            if (operation.Equals("Connect", StringComparison.OrdinalIgnoreCase)) return true;
            throw new InvalidOperationException("Unsupported AUXCAN operation: " + operation);
        }

        private void FCT_EnsureAuxCan(int socketIndex)
        {
            if (_fctAuxCan != null) return;
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string executableFolder = Path.GetDirectoryName(assemblyFolder);
            string driverPath = FCT_InputString(socketIndex, "DriverPath", Path.Combine(assemblyFolder, "Instruments.CAN.ZLG_CAN.dll"));
            string dbcPath = FCT_InputString(socketIndex, "DbcPath", Path.Combine(executableFolder, "Config", "C95C96Auxiliary.dbc"));
            if (!Path.IsPathRooted(dbcPath)) dbcPath = Path.Combine(executableFolder, dbcPath);
            _fctAuxCan = new Instruments.CAN.CANWrapper(driverPath);
            _fctAuxCan.DBC_ReadDBCTxt(dbcPath);
            _fctAuxCan.SetValue("IP", FCT_InputString(socketIndex, "IP", "192.166.6.10"));
            _fctAuxCan.SetValue("PORT", (int)FCT_InputDouble(socketIndex, "Port", 8000));
            _fctAuxCan.OpenCANDevice((uint)FCT_InputDouble(socketIndex, "DeviceType", 52), (ushort)FCT_InputDouble(socketIndex, "Channel", 0), (uint)FCT_InputDouble(socketIndex, "BaudRate", 500000));
        }

        private void FCT_SendAuxSignals(Instruments.CAN.CANWrapper aux, JObject signals)
        {
            List<JProperty> items = signals.Properties().ToList(); if (items.Count == 0) throw new InvalidOperationException("SignalsJson must contain at least one DBC signal."); lock (_fctAuxSendLocker) for (int index = 0; index < items.Count; index++) { double value = Convert.ToDouble(items[index].Value, CultureInfo.InvariantCulture); bool send = index == items.Count - 1; int result = aux.DBC_SendSignalValue(items[index].Name, value, send); if (result < 0) throw new InvalidOperationException("DBC signal send failed: " + items[index].Name + ", result=" + result.ToString(CultureInfo.InvariantCulture)); }
        }
        private void FCT_StopAuxPeriodic(string key)
        {
            Timer timer;
            if (_fctAuxPeriodicSenders.TryGetValue(key ?? string.Empty, out timer))
            {
                _fctAuxPeriodicSenders.Remove(key ?? string.Empty);
                try { timer.Dispose(); } catch { }
            }
        }
        private void FCT_StopAuxPeriodicByPrefix(string prefix) { if (string.IsNullOrEmpty(prefix)) { FCT_StopAllAuxPeriodic(); return; } foreach (string key in _fctAuxPeriodicSenders.Keys.Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()) FCT_StopAuxPeriodic(key); }
        private void FCT_StopAllAuxPeriodic() { foreach (string key in _fctAuxPeriodicSenders.Keys.ToList()) FCT_StopAuxPeriodic(key); }

        private void FCT_RecordResult(int socketIndex, object result)
        {
            string mode = FCT_InputString(socketIndex, "ResultMode", "Action");
            if (mode.Equals("Action", StringComparison.OrdinalIgnoreCase) || mode.Equals("Variable", StringComparison.OrdinalIgnoreCase)) return;
            string stepName = FCT_InputString(socketIndex, "StepName", "Generic Step");
            string comment = FCT_InputString(socketIndex, "Comment", string.Empty);
            if (mode.Equals("NumericLimit", StringComparison.OrdinalIgnoreCase))
            {
                MySequenceManage.AddNumericTesting(socketIndex, stepName, Convert.ToDouble(result, CultureInfo.InvariantCulture), FCT_InputString(socketIndex, "Comtype", "GELE"), FCT_InputDouble(socketIndex, "LowLimit", double.MinValue), FCT_InputDouble(socketIndex, "HighLimit", double.MaxValue), FCT_InputString(socketIndex, "Unit", string.Empty), comment);
            }
            else if (mode.Equals("StringLimit", StringComparison.OrdinalIgnoreCase))
            {
                MySequenceManage.AddStringTesting(socketIndex, stepName, Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty, string.Empty, FCT_InputString(socketIndex, "Limit", string.Empty), comment);
            }
            else if (mode.Equals("PassFail", StringComparison.OrdinalIgnoreCase))
            {
                MySequenceManage.AddPassFailTesting(socketIndex, stepName, Convert.ToBoolean(result, CultureInfo.InvariantCulture), comment);
            }
            else if (mode.Equals("Information", StringComparison.OrdinalIgnoreCase))
            {
                MySequenceManage.AddCustomString(socketIndex, stepName, Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty, comment);
            }
            else throw new InvalidOperationException("Unsupported ResultMode: " + mode);
        }

        private void FCT_SaveOutputVariable(int socketIndex, object result)
        {
            string name = FCT_InputString(socketIndex, "OutputVariable", string.Empty);
            if (name.Length > 0 && result != null) FCT_SetVariable(socketIndex, name, result);
        }

        private void FCT_RunFixedLoop(int socketIndex)
        {
            string loopId = FCT_InputString(socketIndex, "LoopId", FCT_InputString(socketIndex, "StepName", "Loop"));
            int count = Math.Max(0, (int)FCT_InputDouble(socketIndex, "Count", 1));
            string target = FCT_InputString(socketIndex, "TargetStepName", string.Empty);
            Dictionary<string, int> counters = FCT_Loops(socketIndex);
            int current; counters.TryGetValue(loopId, out current); current++;
            if (current < count) { counters[loopId] = current; MySequenceManage.GotoByStepName(socketIndex, target); }
            else counters.Remove(loopId);
        }

        private void FCT_RunCondition(int socketIndex)
        {
            string variableName = FCT_InputString(socketIndex, "VariableName", string.Empty);
            object left = variableName.Length > 0 ? FCT_GetVariable(socketIndex, variableName) : (object)FCT_InputString(socketIndex, "LeftValue", string.Empty);
            string right = FCT_InputString(socketIndex, "RightValue", string.Empty);
            string compare = FCT_InputString(socketIndex, "Compare", "EQ");
            bool numeric = FCT_InputString(socketIndex, "DataType", "Number").Equals("Number", StringComparison.OrdinalIgnoreCase);
            bool passed = numeric ? FCT_CompareNumber(Convert.ToDouble(left, CultureInfo.InvariantCulture), Convert.ToDouble(right, CultureInfo.InvariantCulture), compare) : FCT_CompareString(Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty, right, compare);
            if (FCT_InputBool(socketIndex, "RecordResult", true)) MySequenceManage.AddPassFailTesting(socketIndex, FCT_InputString(socketIndex, "StepName", "Condition"), passed, FCT_InputString(socketIndex, "Comment", string.Empty));
            string target = FCT_InputString(socketIndex, passed ? "TrueGoto" : "FalseGoto", string.Empty);
            if (target.Length > 0) MySequenceManage.GotoByStepName(socketIndex, target);
        }

        private static bool FCT_CompareNumber(double left, double right, string compare)
        {
            switch ((compare ?? string.Empty).ToUpperInvariant()) { case "GT": return left > right; case "GE": return left >= right; case "LT": return left < right; case "LE": return left <= right; case "NE": return Math.Abs(left - right) > 1e-9; default: return Math.Abs(left - right) <= 1e-9; }
        }
        private static bool FCT_CompareString(string left, string right, string compare)
        {
            switch ((compare ?? string.Empty).ToUpperInvariant()) { case "NE": return !string.Equals(left, right, StringComparison.Ordinal); case "CONTAINS": return left.Contains(right); case "STARTSWITH": return left.StartsWith(right, StringComparison.Ordinal); default: return string.Equals(left, right, StringComparison.Ordinal); }
        }

        private uint FCT_GetTableAddress(uint addressOffset, int pointerDepth = 1)
        {
            uint requestAddress = FirstAddress + addressOffset;
            byte[] addressBytes = BitConverter.GetBytes(requestAddress);
            byte[] response;
            DUT_WriteRead(new[] { addressBytes[3], addressBytes[2], addressBytes[1], addressBytes[0], (byte)0, (byte)4, (byte)0xFF, (byte)0 }, out response);
            if (response == null || response.Length < 4) throw new InvalidOperationException("Product did not return a table address for offset 0x" + addressOffset.ToString("X"));
            uint address = BitConverter.ToUInt32(response, 0);
            if (pointerDepth == 2)
            {
                byte[] second = FCT_ReadAddressBytes(address, 4);
                if (second.Length < 4) throw new InvalidOperationException("Product did not return the second-level table pointer for offset 0x" + addressOffset.ToString("X"));
                address = BitConverter.ToUInt32(second, 0);
            }
            else if (pointerDepth != 1) throw new ArgumentOutOfRangeException("PointerDepth");
            return address;
        }

        private byte[] FCT_ReadAddressBytes(uint address, int length)
        {
            List<byte> result = new List<byte>(length);
            int offset = 0;
            while (offset < length)
            {
                int count = Math.Min(240, length - offset);
                byte[] addressBytes = BitConverter.GetBytes(address + (uint)offset);
                byte[] response;
                DUT_WriteRead(new[] { addressBytes[3], addressBytes[2], addressBytes[1], addressBytes[0], (byte)0, (byte)count, (byte)0xFF, (byte)0 }, out response);
                if (response == null || response.Length < count) throw new InvalidOperationException("Product returned fewer table bytes than requested.");
                result.AddRange(response.Take(count)); offset += count;
            }
            return result.ToArray();
        }

        private void FCT_WriteAddressBytes(uint address, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = Math.Min(240, bytes.Length - offset);
                byte[] addressBytes = BitConverter.GetBytes(address + (uint)offset);
                byte[] command = new[] { addressBytes[3], addressBytes[2], addressBytes[1], addressBytes[0], (byte)0, (byte)count, (byte)0, (byte)0 }.Concat(bytes.Skip(offset).Take(count)).ToArray();
                byte[] response; DUT_WriteRead(command, out response); offset += count;
            }
        }

        private static byte[] FCT_EncodeValue(string dataType, int dataSize, string valueText, bool bigEndian)
        {
            string type = (dataType ?? string.Empty).ToLowerInvariant(); byte[] bytes;
            if (type.Contains("float") && dataSize == 8) bytes = BitConverter.GetBytes(double.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("float")) bytes = BitConverter.GetBytes(float.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("uint64")) bytes = BitConverter.GetBytes(ulong.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("int64")) bytes = BitConverter.GetBytes(long.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("uint32")) bytes = BitConverter.GetBytes(uint.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("int32")) bytes = BitConverter.GetBytes(int.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("uint16")) bytes = BitConverter.GetBytes(ushort.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("int16")) bytes = BitConverter.GetBytes(short.Parse(valueText, CultureInfo.InvariantCulture));
            else if (type.Contains("uint8") || type.Contains("unsigned char") || type == "byte") bytes = new[] { byte.Parse(valueText, CultureInfo.InvariantCulture) };
            else if (type.Contains("int8") || type.Contains("signed char") || type == "sbyte") bytes = new[] { unchecked((byte)sbyte.Parse(valueText, CultureInfo.InvariantCulture)) };
            else if (type.Contains("bool")) bytes = new[] { bool.Parse(valueText) ? (byte)1 : (byte)0 };
            else if (type.Contains("string") || type == "char") bytes = Encoding.ASCII.GetBytes(valueText ?? string.Empty);
            else bytes = new[] { byte.Parse(valueText, CultureInfo.InvariantCulture) };
            if (dataSize > 0 && bytes.Length != dataSize) Array.Resize(ref bytes, dataSize);
            if (bigEndian) Array.Reverse(bytes); return bytes;
        }

        private static object FCT_DecodeValue(string dataType, byte[] source, bool bigEndian)
        {
            byte[] bytes = (byte[])source.Clone(); if (bigEndian) Array.Reverse(bytes); string type = (dataType ?? string.Empty).ToLowerInvariant();
            if (type.Contains("float") && bytes.Length == 8) return BitConverter.ToDouble(bytes, 0);
            if (type.Contains("float")) return BitConverter.ToSingle(bytes, 0);
            if (type.Contains("uint64")) return BitConverter.ToUInt64(bytes, 0); if (type.Contains("int64")) return BitConverter.ToInt64(bytes, 0);
            if (type.Contains("uint32")) return BitConverter.ToUInt32(bytes, 0); if (type.Contains("int32")) return BitConverter.ToInt32(bytes, 0);
            if (type.Contains("uint16")) return BitConverter.ToUInt16(bytes, 0); if (type.Contains("int16")) return BitConverter.ToInt16(bytes, 0);
            if (type.Contains("uint8") || type.Contains("unsigned char") || type == "byte") return bytes[0]; if (type.Contains("int8") || type.Contains("signed char") || type == "sbyte") return unchecked((sbyte)bytes[0]);
            if (type.Contains("bool")) return bytes[0] != 0; if (type.Contains("string") || type == "char") return Encoding.ASCII.GetString(bytes).TrimEnd('\0'); return bytes[0];
        }

        private string FCT_InputString(int socketIndex, string name, string defaultValue)
        {
            try { string value = MySequenceManage.GetInputStringValue(socketIndex, name); return value ?? defaultValue; }
            catch { return defaultValue; }
        }
        private double FCT_InputDouble(int socketIndex, string name, double defaultValue)
        {
            try { return MySequenceManage.GetInputDoubleValue(socketIndex, name); }
            catch { return defaultValue; }
        }
        private bool FCT_InputBool(int socketIndex, string name, bool defaultValue)
        {
            try { return MySequenceManage.GetInputBoolValue(socketIndex, name); }
            catch
            {
                try
                {
                    string text = MySequenceManage.GetInputStringValue(socketIndex, name);
                    if (string.IsNullOrWhiteSpace(text)) return defaultValue;
                    bool parsed;
                    if (bool.TryParse(text, out parsed)) return parsed;
                    if (text == "1" || text.Equals("ON", StringComparison.OrdinalIgnoreCase) || text.Equals("YES", StringComparison.OrdinalIgnoreCase)) return true;
                    if (text == "0" || text.Equals("OFF", StringComparison.OrdinalIgnoreCase) || text.Equals("NO", StringComparison.OrdinalIgnoreCase)) return false;
                    double number;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return Math.Abs(number) > double.Epsilon;
                }
                catch { }
                return defaultValue;
            }
        }
        private Dictionary<string, object> FCT_Variables(int socketIndex) { Dictionary<string, object> values; if (!_fctVariables.TryGetValue(socketIndex, out values)) _fctVariables[socketIndex] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); return values; }
        private Dictionary<string, int> FCT_Loops(int socketIndex) { Dictionary<string, int> values; if (!_fctLoopCounters.TryGetValue(socketIndex, out values)) _fctLoopCounters[socketIndex] = values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); return values; }
        private void FCT_SetVariable(int socketIndex, string name, object value) { if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("VariableName is required."); FCT_Variables(socketIndex)[name] = value; }
        private object FCT_GetVariable(int socketIndex, string name) { object value; if (!FCT_Variables(socketIndex).TryGetValue(name, out value)) throw new KeyNotFoundException("Runtime variable was not found: " + name); return value; }

        private void FCT_GenericSafeShutdown()
        {
            FCT_StopAllAuxPeriodic();
            try { HVDC.SetSourceVoltage(0); } catch { } try { FCT_KewellSetOutput(HVDC, false, null); } catch { } try { LVDC.SetOutput(false); } catch { } try { LVDC_KL15.SetOutput(false); } catch { }
            try { Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true); } catch { }
            try { if (_fctAuxCan != null) _fctAuxCan.CloseCANDevice(); } catch { } _fctAuxCan = null;
            try { RelayFctBoard.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0"); } catch { }
            try { RelayHvMux.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0"); } catch { }
            foreach (ushort channel in new ushort[] { 0, 4, 8, 12 }) try { Relay.WriteSingleCoil(1, channel, false); } catch { }
            foreach (object plugin in _fctActionPlugins.Values) { try { MethodInfo shutdown = plugin.GetType().GetMethod("SafeShutdown", Type.EmptyTypes); if (shutdown != null) shutdown.Invoke(plugin, null); } catch { } try { IDisposable disposable = plugin as IDisposable; if (disposable != null) disposable.Dispose(); } catch { } } _fctActionPlugins.Clear();
        }

        partial void FCT_ResetGenericRuntimeCore(int socketIndex)
        {
            lock (_fctGenericLocker) { _fctVariables.Remove(socketIndex); _fctLoopCounters.Remove(socketIndex); FCT_StopAllAuxPeriodic(); }
        }
        private void FCT_Log(int socketIndex, string message) { try { MySequenceManage.DisplayStringToUI(socketIndex, "[FCT] " + message); } catch { } }
    }
}
