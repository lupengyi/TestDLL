using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManualCanDebug.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    public interface IAdvancedCanService
    {
        ProductCanProfile ProductProfile { get; }
        bool AuxiliaryConnected { get; }
        IReadOnlyList<C91InputSignalResult> ReadAllC91InputTables();
        IReadOnlyList<C95InputSignalResult> ReadAllC95InputTables();
        IReadOnlyList<C95TableReadResult> ReadAllC95Tables();
        ProductResolverData ReadProductResolverData();
        DutCurrentResult ReadProductCurrent();
        byte[] ReadMotorStatus();
        double ReadDutValue(uint addressOffset, int tableIndex, int dataSize);
        IReadOnlyList<C96InputSignalResult> ReadAllC96InputTables();
        C96DriveSnapshot ReadC96DriveSnapshot(C96Drive drive);
        void SendC96MotorControl(C96Drive drive, C96MotorControlCommand settings);
        void SetC96AutoPwm(C96Drive drive, bool enabled);
        void SetC96ExpectedLoad(C96Drive drive, byte loadType);
        void SetC96RunIn(C96Drive drive, ushort frequencyHz, float maximumTemperature, bool activate);
        void PulseC96UvFaultReset(C96Drive drive, bool includeUpper = true);
        void PulseC96OverCurrentFaultReset(C96Drive drive);
        void PulseC96BusHardwareOverVoltageFaultReset();
        void PulseC96AllHardwareFaultResets(C96Drive drive);
        CanFrame SendAuxiliaryDbcMessage(string messageName, IDictionary<string, double> values);
        void StartAuxiliaryPeriodic(string key, string messageName, IDictionary<string, double> values, int periodMs, string heartbeatSignal = null);
        void StopAuxiliaryPeriodic(string key);
        IReadOnlyList<DbcDecodedFrame> ReceiveAuxiliaryDbcFrames();
        void ReportAuxiliaryTimingWarning(string senderName, double intervalMilliseconds);
    }

    internal sealed class MainTestAdvancedCanService : IAdvancedCanService
    {
        private readonly object _executionLock = new object();
        private readonly Func<SequenceStepDefinition, Task<string>> _execute;
        private readonly Func<LegacyStepExecutionResult> _lastResult;
        private readonly Func<ProductCanProfile> _profile;
        private readonly Func<string, bool> _instrumentReady;
        private readonly Action<string> _log;
        private readonly DbcDatabase _auxiliaryDbc;

        public MainTestAdvancedCanService(Func<SequenceStepDefinition, Task<string>> execute, Func<LegacyStepExecutionResult> lastResult, Func<ProductCanProfile> profile, Func<string, bool> instrumentReady, string auxiliaryDbcPath, Action<string> log)
        {
            _execute = execute; _lastResult = lastResult; _profile = profile; _instrumentReady = instrumentReady; _log = log;
            _auxiliaryDbc = DbcDatabase.Load(auxiliaryDbcPath);
        }

        public ProductCanProfile ProductProfile { get { return _profile(); } }
        public bool AuxiliaryConnected { get { return _instrumentReady("AUXCAN"); } }

        public IReadOnlyList<C91InputSignalResult> ReadAllC91InputTables()
        {
            if (ProductProfile.Model != ProductModel.C91) throw new InvalidOperationException("当前产品不是C91。"); List<C91InputSignalResult> results = new List<C91InputSignalResult>(); foreach (C91InputTableDefinition table in C91InputCatalog.Tables) { byte[] data = ReadTable(table.AddressOffset, table.ByteLength, table.Name); foreach (C91InputSignalDefinition signal in table.Signals) results.Add(C91InputSignalResult.Decode(table, signal, data)); } return results.AsReadOnly();
        }

        public IReadOnlyList<C95InputSignalResult> ReadAllC95InputTables()
        {
            if (ProductProfile.Model != ProductModel.C95) throw new InvalidOperationException("当前产品不是C95。"); List<C95InputSignalResult> results = new List<C95InputSignalResult>(); foreach (C95InputTableDefinition table in C95InputCatalog.Tables) { byte[] data = ReadTable(table.AddressOffset, table.ByteLength, table.Name); foreach (C95InputSignalDefinition signal in table.Signals) results.Add(C95InputSignalResult.Decode(table, signal, data)); } return results.AsReadOnly();
        }

        public IReadOnlyList<C95TableReadResult> ReadAllC95Tables()
        {
            if (ProductProfile.Model != ProductModel.C95) throw new InvalidOperationException("当前产品不是C95。"); List<C95TableReadResult> results = new List<C95TableReadResult>(); foreach (C95TableDefinition table in C95AllTableCatalog.Tables) { try { byte[] data = table.HasDefinedLength ? ReadTable(table.AddressOffset, table.ByteLength, table.Name, table.PointerDepth) : new byte[0]; results.Add(C95TableReadResult.Success(table, "MainTest", data)); } catch (Exception ex) { results.Add(C95TableReadResult.Failure(table, ex.Message)); } } return results.AsReadOnly();
        }

        public ProductResolverData ReadProductResolverData()
        {
            ProductCanProfile profile = ProductProfile; byte[] data = ReadTable(profile.ResolverDataOffset, profile.ResolverDataLength, profile.Model + " Resolver"); byte[] pointer = new byte[4]; return ProductResolverData.Parse(profile, 0, 0, CanProtocol.BuildAddressRead(profile.ResolverDataOffset), pointer, CanProtocol.BuildTableRead(0, profile.ResolverDataLength), data);
        }

        public byte[] ReadMotorStatus() { ProductCanProfile profile = ProductProfile; if (profile.IsDualDrive) throw new InvalidOperationException("双驱产品请使用TM1/TM2读取页。"); return ReadTable(profile.MotorStatusOffset, profile.MotorStatusLength, profile.Model + " Motor Status"); }
        public DutCurrentResult ReadProductCurrent() { ProductCanProfile profile = ProductProfile; if (profile.IsDualDrive) throw new InvalidOperationException("双驱产品请使用TM1/TM2读取页。"); return DutCurrentResult.Parse(ReadTable(profile.CurrentSenseResultOffset, 36, profile.Model + " Current"), ReadMotorStatus()); }
        public double ReadDutValue(uint addressOffset, int tableIndex, int dataSize) { byte[] data = ReadTable(addressOffset, tableIndex + dataSize, "DUT Value").Skip(tableIndex).Take(dataSize).ToArray(); if (dataSize == 4) return BitConverter.ToSingle(data, 0); if (dataSize == 1) return data[0]; throw new ArgumentOutOfRangeException(nameof(dataSize)); }

        public IReadOnlyList<C96InputSignalResult> ReadAllC96InputTables()
        {
            RequireDualDrive();
            List<C96InputSignalResult> results = new List<C96InputSignalResult>();
            foreach (C96InputTableDefinition table in C96InputCatalog.Tables)
            {
                byte[] data = ReadTable(table.AddressOffset, table.ByteLength, table.Name);
                foreach (C96InputSignalDefinition signal in table.Signals) results.Add(C96InputSignalResult.Decode(table, signal, data));
            }
            return results.AsReadOnly();
        }

        public C96DriveSnapshot ReadC96DriveSnapshot(C96Drive drive)
        {
            RequireDualDrive(); C96DriveProfile profile = C96DriveProfile.For(drive);
            byte[] resolverRaw = ReadTable(profile.ResolverOffset, profile.ResolverLength, drive + " Resolver");
            byte[] statusRaw = ReadTable(profile.MotorStatusOffset, profile.MotorStatusLength, drive + " Motor Status");
            byte[] currentRaw = ReadTable(profile.CurrentResultOffset, profile.CurrentResultLength, drive + " Current");
            byte[] rpmRaw = ReadTable(profile.RpmOffset, profile.RpmLength, drive + " RPM");
            return new C96DriveSnapshot(drive, C96ResolverResult.Parse(drive, resolverRaw), C96CurrentResult.Parse(drive, currentRaw), C96MotorStatusInfo.Parse(drive, statusRaw), BitConverter.ToSingle(rpmRaw, 0), BitConverter.ToSingle(rpmRaw, 4), BitConverter.ToSingle(rpmRaw, 8), HexDataParser.Format(rpmRaw));
        }

        public void SendC96MotorControl(C96Drive drive, C96MotorControlCommand settings)
        {
            RequireDualDrive(); C96DriveProfile profile = C96DriveProfile.For(drive); byte[] payload = drive == C96Drive.TM2 ? CanProtocol.BuildC96Tm2MotorControlPayload(settings) : CanProtocol.BuildC96MotorControlWrite(0, settings).Skip(8).ToArray(); WriteTable(profile.MotorControlOffset, payload, 27, drive + " Motor Control");
        }
        public void SetC96AutoPwm(C96Drive drive, bool enabled) { C96DriveProfile profile = C96DriveProfile.For(drive); WriteTable(profile.AutoPwmOffset, new[] { enabled ? (byte)1 : (byte)0 }, -1, drive + " Auto PWM"); }
        public void SetC96ExpectedLoad(C96Drive drive, byte loadType) { if (loadType > 2) throw new ArgumentOutOfRangeException(nameof(loadType)); C96DriveProfile profile = C96DriveProfile.For(drive); WriteTable(profile.ExpectedLoadOffset, new[] { loadType, (byte)0xFF }, 1, drive + " Expected Load"); }
        public void SetC96RunIn(C96Drive drive, ushort frequencyHz, float maximumTemperature, bool activate) { C96DriveProfile profile = C96DriveProfile.For(drive); byte[] data = new byte[8]; Buffer.BlockCopy(BitConverter.GetBytes(frequencyHz), 0, data, 0, 2); Buffer.BlockCopy(BitConverter.GetBytes(maximumTemperature), 0, data, 2, 4); data[6] = activate ? (byte)1 : (byte)0; data[7] = 0xFF; WriteTable(profile.RunInCommandOffset, data, 7, drive + " Run-in"); }
        public void PulseC96UvFaultReset(C96Drive drive, bool includeUpper = true) { int index = C96FtEnables.UvloResetIndex(drive); Pulse(C96FtEnables.TableOffset, index, includeUpper ? new byte[] { 1, 1 } : new byte[] { 1 }, drive + " UV reset"); }
        public void PulseC96OverCurrentFaultReset(C96Drive drive) { Pulse(C96FtEnables.TableOffset, C96FtEnables.OverCurrentResetIndex(drive), new byte[] { 1 }, drive + " OC reset"); }
        public void PulseC96BusHardwareOverVoltageFaultReset() { Pulse(C96FtEnables.TableOffset, C96FtEnables.SharedBusOverVoltageResetIndex, new byte[] { 1 }, "Bus HW OV reset"); }
        public void PulseC96AllHardwareFaultResets(C96Drive drive) { PulseC96OverCurrentFaultReset(drive); PulseC96BusHardwareOverVoltageFaultReset(); PulseC96UvFaultReset(drive, true); }

        public CanFrame SendAuxiliaryDbcMessage(string messageName, IDictionary<string, double> values)
        {
            RequireInstrument("AUXCAN"); CanFrame encoded = _auxiliaryDbc.Encode(messageName, values); string dataHex = HexDataParser.Format(encoded.Data); _log("MainTest AUX TX 0x" + encoded.Id.ToString("X8", CultureInfo.InvariantCulture) + ": " + dataHex + " [" + messageName + "]"); Execute(Step(messageName, "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "AUXCAN" }, { "Operation", "SendDbcSignals" }, { "MessageName", messageName }, { "SignalsJson", JsonConvert.SerializeObject(values) }, { "CanId", encoded.Id.ToString("X", CultureInfo.InvariantCulture) }, { "DataHex", dataHex }, { "ResultMode", "Action" } })); return encoded;
        }
        public void StartAuxiliaryPeriodic(string key, string messageName, IDictionary<string, double> values, int periodMs, string heartbeatSignal = null) { RequireInstrument("AUXCAN"); CanFrame encoded = _auxiliaryDbc.Encode(messageName, values); string dataHex = HexDataParser.Format(encoded.Data); _log("MainTest AUX PERIODIC START " + periodMs + "ms 0x" + encoded.Id.ToString("X8", CultureInfo.InvariantCulture) + ": " + dataHex + " [" + messageName + "]"); Execute(Step("Start " + messageName, "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "AUXCAN" }, { "Operation", "StartPeriodicDbc" }, { "PeriodicKey", key }, { "MessageName", messageName }, { "SignalsJson", JsonConvert.SerializeObject(values) }, { "CanId", encoded.Id.ToString("X", CultureInfo.InvariantCulture) }, { "DataHex", dataHex }, { "PeriodMs", periodMs }, { "HeartbeatSignal", heartbeatSignal ?? string.Empty }, { "ResultMode", "Action" } })); }
        public void StopAuxiliaryPeriodic(string key) { if (!AuxiliaryConnected) return; Execute(Step("Stop " + key, "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "AUXCAN" }, { "Operation", "StopPeriodicDbc" }, { "PeriodicKey", key }, { "ResultMode", "Action" } })); }
        public IReadOnlyList<DbcDecodedFrame> ReceiveAuxiliaryDbcFrames()
        {
            RequireInstrument("AUXCAN"); LegacyStepExecutionResult execution = Execute(Step("Receive auxiliary CAN", "FCT_ExecuteAction", new Dictionary<string, object> { { "Device", "AUXCAN" }, { "Operation", "ReceiveRaw" }, { "ResultMode", "Information" } })); string json = ResultValue(execution); JArray values = JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); List<DbcDecodedFrame> result = new List<DbcDecodedFrame>(); foreach (JObject item in values.OfType<JObject>()) { uint id = Convert.ToUInt32((string)item["Id"] ?? "0", 16); byte[] data = HexDataParser.Parse((string)item["Data"] ?? string.Empty); DbcDecodedFrame decoded = _auxiliaryDbc.Decode(new CanFrame(id, data)); if (decoded != null) result.Add(decoded); } return result.AsReadOnly();
        }
        public void ReportAuxiliaryTimingWarning(string senderName, double intervalMilliseconds) { if (intervalMilliseconds > 150) _log(senderName + " 周期发送间隔偏大：" + intervalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + "ms"); }

        private byte[] ReadTable(uint addressOffset, int length, string name, int pointerDepth = 1)
        {
            LegacyStepExecutionResult result = Execute(Step(name, "FCT_CANTable", new Dictionary<string, object> { { "Operation", "Read" }, { "AddrOffset", addressOffset }, { "TableLength", length }, { "PointerDepth", pointerDepth }, { "SignalChecksJson", "[]" }, { "ResultMode", "Information" } })); byte[] bytes = HexDataParser.ParseBuffer(ResultValue(result)); if (bytes.Length < length) throw new InvalidOperationException(name + " returned " + bytes.Length + "/" + length + " bytes."); return bytes.Take(length).ToArray();
        }
        private void WriteTable(uint addressOffset, byte[] data, int writeLastIndex, string name)
        {
            JArray changes = new JArray(); for (int index = 0; index < data.Length; index++) changes.Add(new JObject { ["Offset"] = index, ["DataSize"] = 1, ["DataType"] = "uint8", ["Endian"] = "Little", ["Value"] = data[index], ["WriteLast"] = index == writeLastIndex }); Execute(Step(name, "FCT_CANTable", new Dictionary<string, object> { { "Operation", "Write" }, { "AddrOffset", addressOffset }, { "TableLength", data.Length }, { "ChangesJson", changes.ToString(Formatting.None) }, { "VerifyAfterWrite", true }, { "ResultMode", "Action" } }));
        }
        private void Pulse(uint offset, int index, byte[] high, string name) { byte[] highTable = new byte[index + high.Length]; Array.Copy(high, 0, highTable, index, high.Length); WritePartial(offset, index, high, name + " High"); Thread.Sleep(100); WritePartial(offset, index, new byte[high.Length], name + " Low"); }
        private void WritePartial(uint offset, int index, byte[] data, string name) { JArray changes = new JArray(); for (int i = 0; i < data.Length; i++) changes.Add(new JObject { ["Offset"] = index + i, ["DataSize"] = 1, ["DataType"] = "uint8", ["Endian"] = "Little", ["Value"] = data[i] }); Execute(Step(name, "FCT_CANTable", new Dictionary<string, object> { { "Operation", "Write" }, { "AddrOffset", offset }, { "TableLength", index + data.Length }, { "ChangesJson", changes.ToString(Formatting.None) }, { "VerifyAfterWrite", true }, { "ResultMode", "Action" } })); }
        private LegacyStepExecutionResult Execute(SequenceStepDefinition step) { lock (_executionLock) { Task.Run(() => _execute(step)).GetAwaiter().GetResult(); LegacyStepExecutionResult result = _lastResult(); if (result == null) throw new InvalidOperationException("MainTest did not return a STEP result snapshot."); return result; } }
        private static SequenceStepDefinition Step(string name, string function, IDictionary<string, object> values) { Dictionary<string, object> properties = new Dictionary<string, object> { { "StepName", name }, { "RunMode", "Normal" }, { "FunctionName", function }, { "RecordingLog", true } }; foreach (KeyValuePair<string, object> pair in values) properties[pair.Key] = pair.Value; return new SequenceStepDefinition(properties); }
        private static string ResultValue(LegacyStepExecutionResult result) { LegacyPlatformResultRow row = result.Results.LastOrDefault(); if (row == null) throw new InvalidOperationException("MainTest STEP did not publish a platform result."); return row.Value ?? string.Empty; }
        private void RequireDualDrive() { if (!ProductProfile.IsDualDrive) throw new InvalidOperationException("当前产品不是C92/C96双驱产品。"); RequireInstrument("DUTCAN"); }
        private void RequireInstrument(string name) { if (!_instrumentReady(name)) throw new InvalidOperationException("请先在仪器中心勾选并初始化 " + name + "。"); }
    }
}
