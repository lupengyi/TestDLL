using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ManualCanDebug.Core
{
    public sealed class CanDebugService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly CanChannel _product;
        private readonly CanChannel _resolver;
        private readonly CanChannel _auxiliary;
        private readonly DbcDatabase _auxiliaryDbc;
        private uint _firstAddress;
        private uint _productTxId = 0x7EE;
        private uint _productRxId = 0x7EF;
        private ProductCanProfile _productProfile = ProductCanProfile.For(ProductModel.C95);
        private DateTime? _currentStartTime;
        private float _lastRequestedCurrentRms;
        private bool _currentSenseResetDone;
        private int? _resolverPolePairsOverride;

        public CanDebugService(string runtimeDirectory, string productDbcPath, string resolverDbcPath)
            : this(runtimeDirectory, productDbcPath, resolverDbcPath, null)
        {
        }

        public CanDebugService(string runtimeDirectory, string productDbcPath, string resolverDbcPath, string auxiliaryDbcPath)
        {
            _product = new CanChannel(CanChannelConfig.ProductDefaults(), runtimeDirectory, productDbcPath);
            _resolver = new CanChannel(CanChannelConfig.ResolverDefaults(), runtimeDirectory, resolverDbcPath);
            if (!string.IsNullOrWhiteSpace(auxiliaryDbcPath))
            {
                _auxiliary = new CanChannel(CanChannelConfig.AuxiliaryDefaults(), runtimeDirectory, auxiliaryDbcPath);
                _auxiliaryDbc = DbcDatabase.Load(auxiliaryDbcPath);
            }
        }

        public event Action<string> Log;

        public bool ProductConnected { get { return _product.IsConnected; } }
        public bool ResolverConnected { get { return _resolver.IsConnected; } }
        public bool AuxiliaryConnected { get { return _auxiliary != null && _auxiliary.IsConnected; } }
        public uint ProductTxId { get { return _productTxId; } }
        public uint ProductRxId { get { return _productRxId; } }
        public uint FirstAddress { get { return _firstAddress; } }
        public ProductCanProfile ProductProfile { get { return _productProfile; } }
        public float LastRequestedCurrentRms { get { return _lastRequestedCurrentRms; } }
        public int? ResolverPolePairsOverride { get { return _resolverPolePairsOverride; } }

        public void SetProductModel(ProductModel model)
        {
            ProductCanProfile profile = ProductCanProfile.For(model);
            if (_productProfile.Model == profile.Model) return;
            _productProfile = profile;
            _firstAddress = 0;
            _currentStartTime = null;
            _currentSenseResetDone = false;
            WriteLog("产品型号已切换为 " + profile.DisplayName + "；请重新执行 DUT 通信初始化。");
        }

        public void SetProductIds(uint txId, uint rxId)
        {
            _productTxId = txId;
            _productRxId = rxId;
            WriteLog(string.Format("Product IDs set: TX=0x{0:X} RX=0x{1:X}", txId, rxId));
        }

        public void ConnectProduct()
        {
            lock (_sync)
            {
                _product.Connect();
                WriteLog("Product CAN connected: CANFDNET-400U-TCP device 52, channel 2, classic CAN 500000, IP 192.166.6.10");
            }
        }

        public void ConnectResolver()
        {
            lock (_sync)
            {
                _resolver.Connect();
                WriteLog("Resolver CAN connected: CANFDNET-400U-TCP device 52, channel 1, classic CAN 500000, IP 192.166.6.10");
            }
        }

        public void ConnectAuxiliary()
        {
            lock (_sync)
            {
                RequireAuxiliaryChannel().Connect();
                WriteLog("C95/C96 DCDC/Auxiliary CAN connected: CANFDNET-400U-TCP device 52, channel 0, classic CAN 500000");
            }
        }

        public void DisconnectAll()
        {
            lock (_sync)
            {
                _resolver.Disconnect();
                _product.Disconnect();
                if (_auxiliary != null) _auxiliary.Disconnect();
                WriteLog("All CAN channels disconnected");
            }
        }

        public void SendRaw(CanBus bus, uint id, byte[] data)
        {
            CanChannel channel = GetChannel(bus);
            byte[] frameData = CanProtocol.NormalizeClassicFrame(data);
            channel.Send(id, frameData);
            WriteLog(string.Format("{0} TX 0x{1:X}: {2}", bus, id, HexDataParser.Format(frameData)));
        }

        public IReadOnlyList<CanFrame> Receive(CanBus bus, uint filterId)
        {
            List<CanFrame> frames = GetChannel(bus).Receive(filterId);
            foreach (CanFrame frame in frames)
            {
                WriteLog(string.Format("{0} RX 0x{1:X}: {2}", bus, frame.Id, HexDataParser.Format(frame.Data)));
            }

            return frames.AsReadOnly();
        }

        public CanFrame SendAuxiliaryDbcMessage(string messageName, IDictionary<string, double> values)
        {
            RequireAuxiliaryProduct();
            CanFrame frame = RequireAuxiliaryDatabase().Encode(messageName, values);
            lock (_sync)
            {
                RequireAuxiliaryChannel().Send(frame.Id, frame.Data);
            }
            WriteLog(string.Format("Auxiliary DBC TX {0} 0x{1:X8}: {2}", messageName, frame.Id, HexDataParser.Format(frame.Data)));
            return frame;
        }

        public IReadOnlyList<DbcDecodedFrame> ReceiveAuxiliaryDbcFrames()
        {
            RequireAuxiliaryProduct();
            List<CanFrame> frames;
            lock (_sync)
            {
                frames = RequireAuxiliaryChannel().ReceiveAll();
            }
            List<DbcDecodedFrame> decodedFrames = new List<DbcDecodedFrame>();
            foreach (CanFrame frame in frames)
            {
                DbcDecodedFrame decoded = RequireAuxiliaryDatabase().Decode(frame);
                if (decoded == null) continue;
                decodedFrames.Add(decoded);
                WriteLog(string.Format("Auxiliary DBC RX {0} 0x{1:X8}: {2}", decoded.MessageName, frame.Id, HexDataParser.Format(frame.Data)));
                WriteLog("  " + string.Join("; ", decoded.Signals.Select(signal => string.Format(CultureInfo.InvariantCulture,
                    "{0}={1:0.###}{2}{3}", signal.Name, signal.Value, string.IsNullOrEmpty(signal.Unit) ? string.Empty : " " + signal.Unit,
                    string.IsNullOrEmpty(signal.Description) ? string.Empty : " (" + signal.Description + ")"))));
            }
            return decodedFrames.AsReadOnly();
        }

        public void ReportAuxiliaryTimingWarning(string senderName, double intervalMilliseconds)
        {
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "Auxiliary timing warning: {0} actual TX interval={1:0.0}ms, expected=100ms.", senderName, intervalMilliseconds));
        }

        public void EnterFtMode()
        {
            if (_productProfile.FtEntryRequests.Count == 0)
            {
                WriteLog(_productProfile.Model + " Product CAN: FT mode assumed; no APP-to-FT UDS sequence is defined for this model.");
                return;
            }

            const uint udsTxId = 0x18DAF0FA;
            const uint udsRxId = 0x18DAFAF0;
            foreach (FtUdsRequest step in _productProfile.FtEntryRequests)
            {
                string response = string.Empty;
                int result = _product.SendUds(udsTxId, udsRxId, step.Request, ref response, step.Expected);
                WriteLog(string.Format("{0} APP->FT UDS TX={1}; expected={2}; result={3}; RX={4}",
                    _productProfile.Model, step.Request, step.Expected, result, response));
            }

            Thread.Sleep(1000);
            _firstAddress = 0;
            WriteLog(_productProfile.Model + " APP-to-FT UDS sequence completed; execute DUT Communication Init next.");
        }

        public byte[] InitializeDut(uint txId, uint rxId)
        {
            SetProductIds(txId, rxId);
            byte[] response = DutWriteRead(CanProtocol.BuildDutCommunicationInit());
            if (response.Length < 4)
            {
                throw new InvalidOperationException("DUT communication initialization returned fewer than four bytes.");
            }

            _firstAddress = BitConverter.ToUInt32(response.Take(4).ToArray(), 0);
            WriteLog(string.Format("DUT communication initialized, first address=0x{0:X8}", _firstAddress));
            return response;
        }

        public byte[] TestProductCommunication()
        {
            byte[] response = DutWriteRead(CanProtocol.BuildProductCommunicationTest());
            WriteLog("Product CAN communication test completed");
            return response;
        }

        public IReadOnlyList<PreCurrentReadResult> ReadPreCurrentStatus()
        {
            List<PreCurrentReadResult> results = PreCurrentStatusReader.ReadAll(_productProfile.PreCurrentReadItems, ReadDutValue).ToList();
            PreCurrentReadItem motorStatusItem = new PreCurrentReadItem(
                "Motor Status",
                _productProfile.MotorStatusOffset,
                0,
                _productProfile.MotorStatusLength,
                string.Empty,
                "FT_Motor_Status_Data");
            try
            {
                MotorStatusInfo status = MotorStatusInfo.Parse(ReadMotorStatus());
                results.Add(PreCurrentReadResult.SuccessText(motorStatusItem, status.RawText, status.Summary));
            }
            catch (Exception ex)
            {
                results.Add(PreCurrentReadResult.Failure(motorStatusItem, ex.Message));
            }

            string diagnosis = PreCurrentDiagnosticAnalyzer.Analyze(_productProfile, results);
            results.Add(PreCurrentReadResult.SuccessText(
                new PreCurrentReadItem("综合诊断结论", _productProfile.MotorStatusOffset, 0, _productProfile.MotorStatusLength, string.Empty, "通信表自动解析"),
                diagnosis,
                "提示性诊断，不自动禁止或发送指令"));

            foreach (PreCurrentReadResult result in results)
            {
                string interpretation = string.IsNullOrEmpty(result.Interpretation) ? string.Empty : "；" + result.Interpretation;
                WriteLog(string.Format(
                    "读取{0} [{1}，{2}]：{3}{4}",
                    result.Item.Name,
                    result.Item.SourceName,
                    result.Item.AddressText,
                    result.FormatValue(),
                    interpretation));
            }

            WriteLog("综合诊断：" + diagnosis.Replace(Environment.NewLine, " | "));

            return results.AsReadOnly();
        }

        public double ReadDutValue(uint addressOffset, int tableIndex, int dataSize)
        {
            if (dataSize == 4)
            {
                return ReadFloat(addressOffset, tableIndex, dataSize);
            }

            if (dataSize == 1)
            {
                return ReadByte(addressOffset, tableIndex);
            }

            throw new ArgumentOutOfRangeException(nameof(dataSize), "Only one-byte and four-byte sequence reads are supported.");
        }

        public void SetDutCurrent(float maxCurrent, float stepCurrent, float holdTime, float frequency)
        {
            if (_productProfile.IsDualDrive)
                throw new InvalidOperationException(_productProfile.Model + " is dual drive. Use its Control tab and select TM1 or TM2 explicitly.");
            EnsureFirstAddress();
            byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + _productProfile.MotorControlOffset));
            if (addressResponse.Length < 4)
            {
                throw new InvalidOperationException("DUT current table address was not returned.");
            }

            uint tableAddress = BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0);
            byte[] txData = CanProtocol.BuildDutCurrentWrite(
                tableAddress,
                maxCurrent,
                stepCurrent,
                holdTime,
                frequency,
                _productProfile.NewDataFlag);
            DutWriteRead(txData);
            _lastRequestedCurrentRms = maxCurrent;
            _currentStartTime = DateTime.Now;
            _currentSenseResetDone = false;
            WriteLog(string.Format(
                "DUT current command sent ({0}): requested RMS={1:0.###}A, command peak={2:0.###}A, step={3:0.###}A, hold={4:0.###}s, freq={5:0.###}Hz, NewData=0x{6:X2}",
                _productProfile.Model,
                maxCurrent,
                maxCurrent * 1.414f,
                stepCurrent,
                holdTime,
                frequency,
                _productProfile.NewDataFlag));
        }

        public byte[] ReadMotorStatus()
        {
            if (_productProfile.IsDualDrive)
                throw new InvalidOperationException(_productProfile.Model + " has independent TM1/TM2 motor status tables. Use the dual-drive Read tab.");
            return ReadTableBytes(_productProfile.MotorStatusOffset, _productProfile.MotorStatusLength);
        }

        public ProductResolverData ReadProductResolverData()
        {
            if (_productProfile.IsDualDrive)
                throw new InvalidOperationException(_productProfile.Model + " has independent TM1/TM2 resolver tables and a different speed/angle order. Use its Read tab.");
            EnsureFirstAddress();

            byte[] addressRequest = CanProtocol.BuildAddressRead(_firstAddress + _productProfile.ResolverDataOffset);
            byte[] pointerResponse = DutWriteRead(addressRequest);
            if (pointerResponse.Length < 4) throw new InvalidOperationException("产品未返回完整的旋变表指针。");
            uint tableAddress = BitConverter.ToUInt32(pointerResponse.Take(4).ToArray(), 0);
            byte[] dataRequest = CanProtocol.BuildTableRead(tableAddress, _productProfile.ResolverDataLength);
            byte[] data = DutWriteRead(dataRequest);
            if (data.Length < _productProfile.ResolverDataLength) throw new InvalidOperationException(string.Format("产品旋变数据返回不完整：{0}/{1} bytes。", data.Length, _productProfile.ResolverDataLength));

            ProductResolverData result = ProductResolverData.Parse(_productProfile, _firstAddress, tableAddress, addressRequest, pointerResponse, dataRequest, data);
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0}产品内部旋变：位置={1:0.######}°，速度/频率={2:0.######}，故障状态={3}，表偏移=0x{4:X2}，表指针={5}，RAW={6}",
                result.Model,
                result.PositionDegrees,
                result.VelocityFrequency,
                result.HasFaultStatus ? result.FaultCode + "（" + result.FaultDescription + "）" : result.FaultDescription,
                result.AddressOffset,
                result.TableAddressText,
                result.RawDataText));
            return result;
        }

        public IReadOnlyList<C95InputSignalResult> ReadAllC95InputTables()
        {
            if (_productProfile.Model != ProductModel.C95)
                throw new InvalidOperationException("C95 Input Tables can only be read while the product model is C95.");

            List<C95InputSignalResult> results = new List<C95InputSignalResult>();
            foreach (C95InputTableDefinition table in C95InputCatalog.Tables)
            {
                byte[] data = ReadTableBytes(table.AddressOffset, table.ByteLength);
                WriteLog(string.Format("C95整表读取 {0} ({1})：{2} bytes", table.Name, table.AddressText, data.Length));
                foreach (C95InputSignalDefinition signal in table.Signals)
                {
                    C95InputSignalResult result = C95InputSignalResult.Decode(table, signal, data);
                    results.Add(result);
                    string interpretation = string.IsNullOrEmpty(result.Interpretation) ? string.Empty : "；" + result.Interpretation;
                    WriteLog(string.Format(
                        "C95输入 [{0} {1} +0x{2:X}] {3} ({4}) = {5}；RAW={6}{7}",
                        result.TableName,
                        result.TableAddress,
                        result.SignalOffset,
                        result.SignalName,
                        result.PortName,
                        result.ValueText,
                        result.RawBytes,
                        interpretation));
                }
            }

            WriteLog(string.Format("C95 Input Tables 全部读取完成：5个表，{0}个信号。", results.Count));
            return results.AsReadOnly();
        }

        public IReadOnlyList<C91InputSignalResult> ReadAllC91InputTables()
        {
            if (_productProfile.Model != ProductModel.C91)
                throw new InvalidOperationException("C91输入表只能在产品型号为C91时读取。");

            List<C91InputSignalResult> results = new List<C91InputSignalResult>();
            foreach (C91InputTableDefinition table in C91InputCatalog.Tables)
            {
                byte[] data = ReadTableBytes(table.AddressOffset, table.ByteLength);
                WriteLog(string.Format("C91整表读取 {0} ({1})：{2} bytes", table.Name, table.AddressText, data.Length));
                foreach (C91InputSignalDefinition signal in table.Signals)
                {
                    C91InputSignalResult result = C91InputSignalResult.Decode(table, signal, data);
                    results.Add(result);
                    WriteLog(string.Format(
                        "C91输入 [{0} {1} +0x{2:X}] {3} ({4}) = {5}；RAW={6}{7}",
                        result.TableName,
                        result.TableOffset,
                        result.SignalOffset,
                        result.SignalName,
                        result.ValueType,
                        result.ValueText,
                        result.RawBytes,
                        string.IsNullOrEmpty(result.Interpretation) ? string.Empty : "；" + result.Interpretation));
                }
            }

            WriteLog(string.Format("C91 Input Tables 全部读取完成：5个表，{0}个信号。", results.Count));
            return results.AsReadOnly();
        }

        public IReadOnlyList<C96InputSignalResult> ReadAllC96InputTables()
        {
            RequireDualDrive();
            List<C96InputSignalResult> results = new List<C96InputSignalResult>();
            foreach (C96InputTableDefinition table in C96InputCatalog.Tables)
            {
                byte[] data = ReadTableBytes(table.AddressOffset, table.ByteLength);
                WriteLog(string.Format("{0} table read {1} ({2}): {3} bytes; RAW={4}", _productProfile.Model, table.Name, table.AddressText, data.Length, HexDataParser.Format(data)));
                foreach (C96InputSignalDefinition signal in table.Signals)
                {
                    C96InputSignalResult result = C96InputSignalResult.Decode(table, signal, data);
                    results.Add(result);
                    WriteLog(string.Format("{0} input [{1} {2} +0x{3:X}] {4} ({5}) = {6}; RAW={7}{8}",
                        _productProfile.Model, result.TableName, result.TableAddress, result.SignalOffset, result.SignalName, result.PortName,
                        result.ValueText, result.RawBytes, string.IsNullOrEmpty(result.Interpretation) ? string.Empty : "; " + result.Interpretation));
                }
            }
            WriteLog(string.Format("{0} current-value input read complete: {1} tables, {2} signals.", _productProfile.Model, C96InputCatalog.Tables.Count, results.Count));
            return results.AsReadOnly();
        }

        public C96DriveSnapshot ReadC96DriveSnapshot(C96Drive drive)
        {
            RequireDualDrive();
            C96DriveProfile profile = C96DriveProfile.For(drive);
            byte[] resolverRaw = ReadTableBytes(profile.ResolverOffset, profile.ResolverLength);
            byte[] statusRaw = ReadTableBytes(profile.MotorStatusOffset, profile.MotorStatusLength);
            byte[] currentRaw = ReadTableBytes(profile.CurrentResultOffset, profile.CurrentResultLength);
            byte[] rpmRaw = ReadTableBytes(profile.RpmOffset, profile.RpmLength);
            C96ResolverResult resolver = C96ResolverResult.Parse(drive, resolverRaw);
            C96MotorStatusInfo status = C96MotorStatusInfo.Parse(drive, statusRaw);
            C96CurrentResult current = C96CurrentResult.Parse(drive, currentRaw);
            C96DriveSnapshot result = new C96DriveSnapshot(drive, resolver, current, status,
                BitConverter.ToSingle(rpmRaw, 0), BitConverter.ToSingle(rpmRaw, 4), BitConverter.ToSingle(rpmRaw, 8), HexDataParser.Format(rpmRaw));

            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} resolver: speed={2:0.###}rpm, angle={3:0.###}deg, fault={4}-{5}; RAW={6}",
                _productProfile.Model, drive, resolver.SpeedRpm, resolver.AngleDegrees, resolver.FaultCode, resolver.FaultDescription, resolver.RawBytes));
            WriteLog(string.Format("{0} {1} Motor Status: {2}; RAW={3}", _productProfile.Model, drive, status.Summary, status.RawText));
            foreach (DutPhaseCurrent phase in current.Phases)
                WriteLog(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1} current {2}: instant={3:0.###}A, min={4:0.###}A, max={5:0.###}A, calculated RMS={6:0.###}A",
                    _productProfile.Model, drive, phase.Name, phase.Instantaneous, phase.Minimum, phase.Maximum, phase.Rms));
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} current reported RMS={2:0.###}A; RAW={3}", _productProfile.Model, drive, current.ReportedRms, current.RawBytes));
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} RPM: current={2:0.###}, max={3:0.###}, min={4:0.###}; RAW={5}",
                _productProfile.Model, drive, result.Rpm, result.RpmMaximum, result.RpmMinimum, result.RpmRaw));
            return result;
        }

        public void SendC96MotorControl(C96Drive drive, C96MotorControlCommand settings)
        {
            RequireDualDrive();
            EnsureFirstAddress();
            C96DriveProfile profile = C96DriveProfile.For(drive);
            byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + profile.MotorControlOffset));
            if (addressResponse.Length < 4) throw new InvalidOperationException(_productProfile.Model + " motor-control table address was not returned.");
            uint tableAddress = BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0);
            byte[] command = CanProtocol.BuildC96MotorControlWrite(tableAddress, settings);
            DutWriteRead(command);
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} control sent: target={2:0.###}Arms ({3:0.###}Apeak), step={4:0.###}Apeak, hold={5:0.###}s, output={6:0.###}Hz, mode={7}, ramp={8}ms, base={9}Hz, gate={10}, resetFaults={11}, speedEnable={12}, speed={13:0.###}rpm, voltageEnable={14}, voltage={15:0.###}V, NewData=0xFF",
                _productProfile.Model, drive, settings.TargetCurrentRms, settings.TargetCurrentRms * 1.414f, settings.StepPeakAmps,
                settings.HoldSeconds, settings.OutputFrequencyHz, settings.Mode, settings.RampTimeMs,
                settings.BaseFrequencyHz, settings.GateEnable, settings.ResetMotorFaults,
                settings.SpeedControlEnable, settings.SpeedSetpointRpm, settings.VoltageControlEnable, settings.VoltageSetpoint));
        }

        public void SetC96AutoPwm(C96Drive drive, bool enabled)
        {
            RequireDualDrive();
            C96DriveProfile profile = C96DriveProfile.For(drive);
            WriteTableBytes(profile.AutoPwmOffset, 0, new[] { enabled ? (byte)1 : (byte)0 });
            WriteLog(string.Format("{0} {1} Auto PWM {2}; table offset=0x{3:X2}", _productProfile.Model, drive, enabled ? "enabled" : "disabled", profile.AutoPwmOffset));
        }

        public void SetC96ExpectedLoad(C96Drive drive, byte loadType)
        {
            RequireDualDrive();
            if (loadType > 2) throw new ArgumentOutOfRangeException(nameof(loadType), "Expected load must be 0=Inductor, 1=EME, or 2=Motor.");
            C96DriveProfile profile = C96DriveProfile.For(drive);
            WriteTableBytes(profile.ExpectedLoadOffset, 0, new[] { loadType, (byte)0xFF });
            WriteLog(string.Format("{0} {1} expected load set to {2}; NewData=0xFF", _productProfile.Model, drive, loadType));
        }

        public void SetC96RunIn(C96Drive drive, ushort frequencyHz, float maximumTemperature, bool activate)
        {
            RequireDualDrive();
            C96DriveProfile profile = C96DriveProfile.For(drive);
            byte[] payload = new byte[8];
            Array.Copy(BitConverter.GetBytes(frequencyHz), 0, payload, 0, 2);
            Array.Copy(BitConverter.GetBytes(maximumTemperature), 0, payload, 2, 4);
            payload[6] = activate ? (byte)1 : (byte)0;
            payload[7] = 0xFF;
            WriteTableBytes(profile.RunInCommandOffset, 0, payload);
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} run-in command: frequency={2}Hz, maxTemp={3:0.###}C, activate={4}, NewData=0xFF",
                _productProfile.Model, drive, frequencyHz, maximumTemperature, activate));
        }

        /// <summary>
        /// Pulse FT_Enables UVLO (and optionally UVUP) High then Low to clear hardware UV latches.
        /// Locator: first PSR power-on cycle needs a High level on FLTRST_UVLO / FLTRST_UVUP.
        /// </summary>
        public void PulseC96UvFaultReset(C96Drive drive, bool includeUpper = true)
        {
            RequireDualDrive();
            int uvloIndex = C96FtEnables.UvloResetIndex(drive);
            string uvloName = C96FtEnables.UvloSignalName(drive);
            string signalNames = uvloName;
            byte[] high = new byte[] { 1 };
            byte[] low = new byte[] { 0 };
            if (includeUpper)
            {
                if (C96FtEnables.UvupResetIndex(drive) != uvloIndex + 1)
                    throw new InvalidOperationException("UVLO and UVUP reset ports are not adjacent in FT_Enables.");
                signalNames += " + " + C96FtEnables.UvupSignalName(drive);
                high = new byte[] { 1, 1 };
                low = new byte[] { 0, 0 };
            }

            PulseFtEnableBytes(uvloIndex, signalNames, high, low);

            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} UV fault reset pulsed: {2} (FT_Enables 0x{3:X2}, High 100ms then Low)",
                _productProfile.Model,
                drive,
                signalNames,
                C96FtEnables.TableOffset));
        }

        public void PulseC96OverCurrentFaultReset(C96Drive drive)
        {
            RequireDualDrive();
            int index = C96FtEnables.OverCurrentResetIndex(drive);
            string signalName = C96FtEnables.OverCurrentResetSignalName(drive);
            PulseFtEnableBytes(index, signalName, new byte[] { 1 }, new byte[] { 0 });
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} hardware OC reset pulsed: {2} (FT_Enables 0x{3:X2}, High 100ms then Low)",
                _productProfile.Model, drive, signalName, C96FtEnables.TableOffset));
        }

        public void PulseC96BusHardwareOverVoltageFaultReset()
        {
            RequireDualDrive();
            PulseFtEnableBytes(C96FtEnables.SharedBusOverVoltageResetIndex,
                C96FtEnables.SharedBusOverVoltageResetSignalName, new byte[] { 1 }, new byte[] { 0 });
            WriteLog(string.Format(CultureInfo.InvariantCulture,
                "{0} shared Bus HW OV reset pulsed: {1} (FT_Enables 0x{2:X2}, High 100ms then Low)",
                _productProfile.Model, C96FtEnables.SharedBusOverVoltageResetSignalName, C96FtEnables.TableOffset));
        }

        public void PulseC96AllHardwareFaultResets(C96Drive drive)
        {
            RequireDualDrive();
            PulseC96OverCurrentFaultReset(drive);
            PulseC96BusHardwareOverVoltageFaultReset();
            PulseC96UvFaultReset(drive, true);
            WriteLog(string.Format("{0} {1} combined hardware fault reset complete: OC + shared Bus HW OV + UVLO/UVUP.",
                _productProfile.Model, drive));
        }

        private void PulseFtEnableBytes(int tableIndex, string signalNames, byte[] high, byte[] low)
        {
            WriteTableBytes(C96FtEnables.TableOffset, tableIndex, high);
            WriteLog(string.Format("{0} FT_Enables +{1}: {2}=High", _productProfile.Model, tableIndex, signalNames));
            try
            {
                Thread.Sleep(100);
            }
            finally
            {
                WriteTableBytes(C96FtEnables.TableOffset, tableIndex, low);
                WriteLog(string.Format("{0} FT_Enables +{1}: {2}=Low", _productProfile.Model, tableIndex, signalNames));
            }
        }

        public IReadOnlyList<C95TableReadResult> ReadAllC95Tables()
        {
            if (_productProfile.Model != ProductModel.C95)
                throw new InvalidOperationException("C95全表读取只能在产品型号为C95时执行。");
            EnsureFirstAddress();

            List<C95TableReadResult> results = new List<C95TableReadResult>();
            foreach (C95TableDefinition table in C95AllTableCatalog.Tables)
            {
                try
                {
                    byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + table.AddressOffset));
                    if (addressResponse.Length < 4) throw new InvalidOperationException("产品未返回4字节表指针。");
                    uint pointer = BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0);
                    string pointerText = "0x" + pointer.ToString("X8");

                    if (table.PointerDepth == 2)
                    {
                        byte[] secondPointerResponse = DutWriteRead(CanProtocol.BuildTableRead(pointer, 4));
                        if (secondPointerResponse.Length < 4) throw new InvalidOperationException("MPI二级指针未返回完整地址。");
                        uint secondPointer = BitConverter.ToUInt32(secondPointerResponse.Take(4).ToArray(), 0);
                        pointerText += " -> 0x" + secondPointer.ToString("X8");
                        pointer = secondPointer;
                    }

                    byte[] data = table.HasDefinedLength
                        ? DutWriteRead(CanProtocol.BuildTableRead(pointer, table.ByteLength)).Take(table.ByteLength).ToArray()
                        : new byte[0];
                    if (table.HasDefinedLength && data.Length < table.ByteLength)
                        throw new InvalidOperationException(string.Format("表数据不完整：{0}/{1} bytes。", data.Length, table.ByteLength));

                    C95TableReadResult result = C95TableReadResult.Success(table, pointerText, data);
                    results.Add(result);
                    WriteLog(string.Format("C95全表 [{0} {1}] 指针={2}，{3}，RAW={4}", table.Name, table.AddressText, pointerText, result.Status, result.RawBytes));
                    foreach (C95TableFieldResult field in C95TableFieldDecoder.Decode(result))
                    {
                        string interpretation = string.IsNullOrEmpty(field.Interpretation) ? string.Empty : "；" + field.Interpretation;
                        WriteLog(string.Format(
                            "C95全表字段 [{0} {1} +0x{2:X}] {3} ({4}) = {5}；RAW={6}{7}",
                            field.TableName,
                            field.TableAddress,
                            field.FieldOffset,
                            field.FieldName,
                            field.DataType,
                            field.ValueText,
                            field.RawBytes,
                            interpretation));
                    }
                }
                catch (Exception ex)
                {
                    results.Add(C95TableReadResult.Failure(table, ex.Message));
                    WriteLog(string.Format("C95全表 [{0} {1}] 读取失败：{2}", table.Name, table.AddressText, ex.Message));
                }
            }

            WriteLog(string.Format("C95全表读取结束：地址项{0}个，成功{1}个，失败{2}个；未定义长度{3}个。",
                results.Count,
                results.Count(result => result.Succeeded),
                results.Count(result => !result.Succeeded),
                results.Count(result => result.Succeeded && !result.Table.HasDefinedLength)));
            return results.AsReadOnly();
        }

        public DutCurrentResult ReadProductCurrent()
        {
            if (_productProfile.IsDualDrive)
                throw new InvalidOperationException(_productProfile.Model + " is dual drive. Use its Read tab to read TM1 and TM2 independently.");
            EnsureFirstAddress();
            if (_currentStartTime.HasValue && !_currentSenseResetDone)
            {
                Thread.Sleep(1000);
                WriteTableBytes(_productProfile.CurrentSenseCommandOffset, 4, new byte[] { 0x00 });
                Thread.Sleep(100);
                WriteTableBytes(_productProfile.CurrentSenseCommandOffset, 4, new byte[] { 0x01 });
                _currentSenseResetDone = true;
            }

            while (_currentStartTime.HasValue && (DateTime.Now - _currentStartTime.Value).TotalSeconds < 6)
            {
                Thread.Sleep(100);
            }

            byte[] currentData = ReadTableBytes(_productProfile.CurrentSenseResultOffset, 36);
            byte[] motorStatus = ReadMotorStatus();
            DutCurrentResult result = DutCurrentResult.Parse(currentData, motorStatus);
            foreach (DutPhaseCurrent phase in result.Phases)
            {
                WriteLog(string.Format(
                    "产品电流 {0} 相：瞬时={1:0.###}A，最小={2:0.###}A，最大={3:0.###}A，RMS={4:0.###}A",
                    phase.Name,
                    phase.Instantaneous,
                    phase.Minimum,
                    phase.Maximum,
                    phase.Rms));
            }

            WriteLog(string.Format(
                "产品电流读取完成：设定={0:0.###}A，Motor Status={1}（{2}）",
                _lastRequestedCurrentRms,
                result.MotorStatusText,
                result.MotorStatusDescription));
            return result;
        }

        public byte[] ReadTableBytes(uint addressOffset, int length)
        {
            EnsureFirstAddress();
            byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + addressOffset));
            if (addressResponse.Length < 4) throw new InvalidOperationException("DUT table address was not returned.");
            uint tableAddress = BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0);
            byte[] data = DutWriteRead(CanProtocol.BuildTableRead(tableAddress, length));
            if (data.Length < length) throw new InvalidOperationException(string.Format("DUT table response is incomplete: {0}/{1} bytes.", data.Length, length));
            return data.Take(length).ToArray();
        }

        public void WriteTableBytes(uint addressOffset, int tableIndex, byte[] data)
        {
            if (tableIndex < 0) throw new ArgumentOutOfRangeException(nameof(tableIndex));
            if (data == null || data.Length == 0) throw new ArgumentException("Write data cannot be empty.", nameof(data));
            EnsureFirstAddress();
            byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + addressOffset));
            if (addressResponse.Length < 4) throw new InvalidOperationException("DUT table address was not returned.");
            uint tableAddress = BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0);
            int writeLength = tableIndex + data.Length;
            byte[] currentData = DutWriteRead(CanProtocol.BuildTableRead(tableAddress, writeLength));
            if (currentData.Length < writeLength) throw new InvalidOperationException("DUT table value could not be read before writing.");
            byte[] updatedData = currentData.Take(writeLength).ToArray();
            Array.Copy(data, 0, updatedData, tableIndex, data.Length);
            DutWriteRead(CanProtocol.BuildTableWrite(tableAddress, updatedData));
        }

        private void RequireDualDrive()
        {
            if (!_productProfile.IsDualDrive)
                throw new InvalidOperationException("Select product model C92 or C96 and run DUT Communication Init before using dual-drive functions.");
        }

        private void RequireAuxiliaryProduct()
        {
            if (!_productProfile.SupportsAuxiliary)
                throw new InvalidOperationException("Select product model C95 or C96 before using DCDC/auxiliary functions.");
        }

        public void InitializeResolver()
        {
            SendRaw(CanBus.Resolver, 0x80000001, new byte[8]);
            Thread.Sleep(100);
        }

        public void SetResolverSpeed(double speed)
        {
            SendResolverSignal("2147483649_mode_switch", 0, true);
            Thread.Sleep(50);
            SendResolverSignal("2505419280_Polarpair", _resolverPolePairsOverride ?? 6, false);
            SendResolverSignal("2505419280_Speed", speed, true);
            Thread.Sleep(500);
        }

        public void SetResolverPosition(double position)
        {
            SendResolverSignal("2505419280_Polarpair", _resolverPolePairsOverride ?? 1, true);
            Thread.Sleep(50);
            SendResolverSignal("2147483649_mode_switch", 1, false);
            SendResolverSignal("2147483649_Position", position, true);
            Thread.Sleep(500);
        }

        public void SetResolverPolePairs(double polePairs)
        {
            int value = CanProtocol.ValidateResolverPolePairs(polePairs);
            SendResolverSignal("2505419280_Polarpair", value, true);
            _resolverPolePairsOverride = value;
            WriteLog(string.Format("Resolver pole pairs set to {0}; subsequent speed and position commands will reuse this value.", value));
        }

        public void StopResolver()
        {
            SendResolverSignal("2505419280_Speed", 0, true);
            Thread.Sleep(100);
        }

        public void SendProductSignal(string signalName, double value, bool sendFlag)
        {
            int result = _product.SendDbcSignal(signalName, value, sendFlag);
            WriteLog(string.Format("Product DBC TX {0}={1} send={2} result={3}", signalName, value, sendFlag, result));
        }

        public void SendResolverSignal(string signalName, double value, bool sendFlag)
        {
            int result = _resolver.SendDbcSignal(signalName, value, sendFlag);
            WriteLog(string.Format("Resolver DBC TX {0}={1} send={2} result={3}", signalName, value, sendFlag, result));
        }

        private byte[] DutWriteRead(byte[] txData)
        {
            if (txData == null || txData.Length == 0) throw new ArgumentException("DUT data cannot be empty.", nameof(txData));
            int expectedLength = txData.Length > 5 ? txData[5] : 0;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                _product.ClearBuffer();
                int frameCount = (txData.Length + 7) / 8;
                byte[] response = new byte[0];
                for (int i = 0; i < frameCount; i++)
                {
                    int offset = i * 8;
                    int length = Math.Min(8, txData.Length - offset);
                    byte[] framePayload = new byte[length];
                    Array.Copy(txData, offset, framePayload, 0, length);
                    byte[] frame = CanProtocol.NormalizeClassicFrame(framePayload);
                    uint id = i == 0 ? _productTxId : _productRxId;
                    _product.Send(id, frame);
                    WriteLog(string.Format("Product TX attempt {0} 0x{1:X}: {2}", attempt, id, HexDataParser.Format(frame)));
                    Thread.Sleep(60);
                }

                if (frameCount != 1)
                {
                    return response;
                }

                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(1);
                    List<CanFrame> frames = _product.Receive(_productRxId);
                    foreach (CanFrame frame in frames)
                    {
                        response = response.Concat(frame.Data).ToArray();
                        WriteLog(string.Format("Product RX 0x{0:X}: {1}", frame.Id, HexDataParser.Format(frame.Data)));
                    }

                    if (expectedLength == 0 || response.Length >= expectedLength) break;
                }

                if (expectedLength == 0 || response.Length >= expectedLength)
                {
                    return response;
                }

                WriteLog(string.Format("Product RX timeout on attempt {0}: received {1}/{2} bytes", attempt, response.Length, expectedLength));
            }

            return new byte[0];
        }

        private double ReadFloat(uint addressOffset, int tableIndex, int dataSize)
        {
            EnsureFirstAddress();
            byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + addressOffset));
            if (addressResponse.Length < 4) throw new InvalidOperationException("DUT address read returned no address.");
            byte[] tableRead = CanProtocol.BuildTableRead(BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0), tableIndex + dataSize);

            if (addressOffset == 72 && tableIndex == 4)
            {
                List<float> values = new List<float>();
                for (int i = 0; i < 40; i++)
                {
                    byte[] response = DutWriteRead(tableRead);
                    if (response.Length < tableIndex + dataSize) throw new InvalidOperationException("Resolver speed response is incomplete.");
                    values.Add(BitConverter.ToSingle(response.Skip(tableIndex).Take(dataSize).ToArray(), 0) * 10);
                }

                values.Sort();
                return Math.Round(values.Skip(8).Take(20).Average(), 0);
            }

            byte[] data = DutWriteRead(tableRead);
            if (data.Length < tableIndex + dataSize) throw new InvalidOperationException("DUT float response is incomplete.");
            double value = BitConverter.ToSingle(data.Skip(tableIndex).Take(dataSize).ToArray(), 0);
            if (addressOffset == 72 && tableIndex == 0 && value > 360) value -= 360;
            return Math.Round(value, 3);
        }

        private double ReadByte(uint addressOffset, int tableIndex)
        {
            EnsureFirstAddress();
            byte[] addressResponse = DutWriteRead(CanProtocol.BuildAddressRead(_firstAddress + addressOffset));
            if (addressResponse.Length < 4) throw new InvalidOperationException("DUT address read returned no address.");
            byte[] data = DutWriteRead(CanProtocol.BuildTableRead(BitConverter.ToUInt32(addressResponse.Take(4).ToArray(), 0), tableIndex + 1));
            if (data.Length <= tableIndex) throw new InvalidOperationException("DUT byte response is incomplete.");
            return data[tableIndex];
        }

        private CanChannel GetChannel(CanBus bus)
        {
            if (bus == CanBus.Product) return _product;
            if (bus == CanBus.Resolver) return _resolver;
            return RequireAuxiliaryChannel();
        }

        private CanChannel RequireAuxiliaryChannel()
        {
            if (_auxiliary == null) throw new InvalidOperationException("C96 auxiliary DBC channel is not configured.");
            return _auxiliary;
        }

        private DbcDatabase RequireAuxiliaryDatabase()
        {
            if (_auxiliaryDbc == null) throw new InvalidOperationException("C96 auxiliary DBC was not loaded.");
            return _auxiliaryDbc;
        }

        private void EnsureFirstAddress()
        {
            if (_firstAddress == 0) throw new InvalidOperationException("Run DUT Communication Init first.");
        }

        private void WriteLog(string message)
        {
            Action<string> handler = Log;
            if (handler != null) handler(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
        }

        public void Dispose()
        {
            DisconnectAll();
        }
    }
}
