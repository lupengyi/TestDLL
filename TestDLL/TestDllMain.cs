using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CSP;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Net.Sockets;
using System.Net;
using System.Globalization;

namespace CSP
{
    public partial class TestDllMain
    {
        partial void FCT_ResetGenericRuntimeCore(int socketIndex);
        private SequenceManage MySequenceManage = SequenceManage.GetInstance();
        private SeqRunTimeState MySeqRunTimeState = SeqRunTimeState.GetInstance(1);
        private InstrumentManage MyInstrumentManage = InstrumentManage.GetInstance();
        
        //Creat object for all instuments
        private Instruments.DMM.KeySight34461A DMM = new Instruments.DMM.KeySight34461A();
        private Instruments.DMM.KeySight34461A DMM_LV = new Instruments.DMM.KeySight34461A();
        private Instruments.PowerSupply.IT6xxxC LVDC = new Instruments.PowerSupply.IT6xxxC();
        private Instruments.PowerSupply.IT6xxxC LVDC_KL15 = new Instruments.PowerSupply.IT6xxxC();
        private Instruments.PowerSupply.Kewell_C3000 HVDC = new Instruments.PowerSupply.Kewell_C3000();
        private Instruments.Other.NGI_ProgramResistance RES = new Instruments.Other.NGI_ProgramResistance();
        private Instruments.Other.NGI_ProgramResistance RES_2 = new Instruments.Other.NGI_ProgramResistance();
        private Instruments.Other.NGI_ProgramResistance RES_3 = new Instruments.Other.NGI_ProgramResistance();
        private Instruments.CAN.CANWrapper MyCAN = null;
        private Instruments.CAN.CANWrapper Resolver = null;
        // Legacy names are retained for old validated STEP functions; the implementation is now SHT_48SEDO_A.
        private ShtRelayCompatAdapter RelayFctBoard = new ShtRelayCompatAdapter();
        private ShtRelayCompatAdapter RelayHvMux = new ShtRelayCompatAdapter();
        private Instruments.DAQ.PCI6229 PCI6320 = new Instruments.DAQ.PCI6229();
        private SHT_48SEDO_A.SHT_48SEDO_A Relay = new SHT_48SEDO_A.SHT_48SEDO_A();
        private byte RelayFctBoardSlave = 1;
        private byte RelayHvMuxSlave = 1;
        private AN23600E.Driver.An23600eDriver DcdcLoad = null;
        private Instruments.PLCS7.S7Net MyPLC = new Instruments.PLCS7.S7Net();
        //private Instruments.PLC.SoketS7 MyPLC = new Instruments.PLC.SoketS7();

        //Global Veriable
        private object HVDC_Locker = new object();
        private int LoopCount = 0;
        private DateTime StartCurrentDatetime = DateTime.Now;
        private double SettingCurrent = 0;
        private double SettingCycleTime = 0;
        private double[] ActCurrent = new double[6];
        private double[] DUTCurrent = new double[6];
        private double[] OldTemps_VIPER = new double[6];
        private int HeartBeat = 0;

        public double ProcessSetup()
        {
            if (string.IsNullOrWhiteSpace(_fctInstrumentSelectionJson)) _fctInstrumentSelectionJson = FCT_LoadInstrumentSelectionFromConfigCore();
            if (!string.IsNullOrWhiteSpace(_fctInstrumentSelectionJson)) return FCT_InitializeConfiguredInstrumentsCore();
            string instrumentErrorMessage = "";
            try
            {
                Console.WriteLine("start to run process setup ......");

                instrumentErrorMessage = "电阻模拟器通讯错误";
                RES.ConnectDevice(MyInstrumentManage.InstrumentList["RES"].Setting["Resource"].ToString(), "");
                RES.SetResistance(1100, 1);
                RES.SetResistance(1100, 2);
                //MyInstrumentManage.InstrumentList["RES"].ObjectReference = RES;

                //
                instrumentErrorMessage = "CAN卡通讯错误";
                string folderName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                MyCAN = new Instruments.CAN.CANWrapper(Path.Combine(folderName, "Instruments.CAN.ZLG_CAN.dll"));
                string exeFolder = Path.GetDirectoryName(folderName);
                MyCAN.DBC_ReadDBCTxt(Path.Combine(exeFolder, "Config", "Flywheel_900A_Z405.dbc"));
                MyCAN.SetValue("IP", "192.168.0.127");
                MyCAN.SetValue("PORT", 8000);
                MyCAN.OpenCANDevice(48, 0, 500000);
                //MyCAN.OpenCANDevice_FD(48, 0, "500000,2000000");
                //MyCAN.OpenCANDevice_FD(41, 0, "500000,2000000");
                //MyCAN = new Instruments.CAN.CANWrapper(Path.Combine(folderName, "Instruments.CAN.Peak_CAN.dll"));
                //MyCAN.OpenCANDevice(0x51, 0, 5000);

                instrumentErrorMessage = "旋变模拟器通讯错误";
                Resolver = new Instruments.CAN.CANWrapper(Path.Combine(folderName, "Instruments.CAN.ZLG_CAN.dll"));
                Resolver.SetValue("IP", "192.168.0.127");
                Resolver.SetValue("PORT", 8000);
                Resolver.OpenCANDevice(48, 1, 500000);
                //Resolver.OpenCANDevice_FD(48, 1, "500000,2000000");
                //Resolver.OpenCANDevice_FD(41, 1, "500000,2000000");

                string dbcPath = Path.GetDirectoryName(folderName);
                Resolver.DBC_ReadDBCTxt(Path.Combine(dbcPath, "Config", "Resolver.dbc"));
                Resolver.SendMessage(0x80000001, new byte[8]);
                Resolver.DBC_SendSignalValue("2505419280_Polarpair", 6, false);
                Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true);

                instrumentErrorMessage = "低压电源通讯错误";
                LVDC.ConnectDevice(MyInstrumentManage.InstrumentList["LVDC"].Setting["Resource"].ToString(), "");
                LVDC.SetOutput(false);

                instrumentErrorMessage = "KL15低压电源通讯错误";
                LVDC_KL15.ConnectDevice(MyInstrumentManage.InstrumentList["LVDC_KL15"].Setting["Resource"].ToString(), "");
                LVDC_KL15.SetOutput(false);

                instrumentErrorMessage = "高压电源通讯错误";
                HVDC.ConnectDevice(MyInstrumentManage.InstrumentList["HVDC"].Setting["Resource"].ToString(), "");
                HVDC.SetSourceVoltage(0);

                instrumentErrorMessage = "MOXA卡通讯错误";
                RelayFctBoard.Connect(MyInstrumentManage.InstrumentList["RELAY_FCT"].Setting["Resource"].ToString(), 502, "sht");
                RelayHvMux.Connect(MyInstrumentManage.InstrumentList["RELAY_HVMUX"].Setting["Resource"].ToString(), 502, "sht");

                RelayFctBoard.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");
                RelayHvMux.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");

                instrumentErrorMessage = "万用表通讯错误";
                DMM.OpenSession(MyInstrumentManage.InstrumentList["DMM"].Setting["Resource"].ToString());
                DMM.InitDMM();
                DMM.ConfigDMMforDC(1000, 0.01);
                double d = DMM.GetMeasureValue();

                instrumentErrorMessage = "继电器卡通讯错误";
                Relay.connect(MyInstrumentManage.InstrumentList["RELAY"].Setting["Resource"].ToString(), 502);
                Relay.WriteSingleCoil(1, 0, true);
                Relay.WriteSingleCoil(1, 4, true);
                Relay.WriteSingleCoil(1, 8, true);
                Relay.WriteSingleCoil(1, 12, true);

                //RelayFctBoard.WriteDO("13,15", "1,1");
                ////RelayHvMux.WriteDO("1,0,6", "1,1,1");
                RelayHvMux.WriteDO("1,4,7", "1,1,1");
                RelayHvMux.WriteDO("1,4,7", "0,0,0");

                instrumentErrorMessage = "PLC通讯错误";
                MyPLC.Connect(30, "10.231.138.100");
                //MyPLC.Connect(30, "TCPIP0::10.231.138.100::102::SOCKET");
                //MyPLC.DBWriteWord(101, 10, 1);
                MyPLC.DBWrite(101, 10, 2, new byte[] { 0, 1 });

                if (MySeqRunTimeState.MESIsOn)
                {
                    //instrumentErrorMessage = "MES通讯错误";
                    //MES_Connect("10.231.137.4", 6000);
                }

                Console.WriteLine("run process setup end......");
                return 0;
            }
            catch (Exception ex)
            {
                throw new Exception(instrumentErrorMessage);
            }
        }

        public double AutomationLoop()
        {
            Thread.Sleep(1000);
            if (!string.IsNullOrWhiteSpace(_fctInstrumentSelectionJson) && !_fctInitializedInstrumentNames.Contains("PLC")) return 0;

            if (HeartBeat == 0)
            {
                MyPLC.DBWrite(101, 0, 2, new byte[] { 0, 0 });
                HeartBeat = 1;
            }
            else
            {
                MyPLC.DBWrite(101, 0, 2, new byte[] { 0, 1 });
                HeartBeat = 0;
            }

            //if (MySeqRunTimeState.MESIsOn)
            if(PLC_ReadByte(788) == 0)
            {
                MySeqRunTimeState.MESIsOn = true;

                string modeNameByPLC = PLC_ReadString(586, 32);
                modeNameByPLC = modeNameByPLC.Replace("\0", "");

                if (MySeqRunTimeState.ProjectName[0] != modeNameByPLC)
                {
                    MySequenceManage.ChangeProjectName(0, modeNameByPLC);
                }

                string seqNameByPLC = PLC_ReadString(686, 32);
                seqNameByPLC = seqNameByPLC.Replace("\0", "");

                string tempSeqPath = ResolveRuntimeSequencePath(seqNameByPLC);
                bool modeNameIsOK = !string.IsNullOrWhiteSpace(tempSeqPath) && File.Exists(tempSeqPath);

                if (MySeqRunTimeState.SequenceRunning[0] == false)
                {
                    if (modeNameIsOK && !string.Equals(MySeqRunTimeState.SequencePath[0], tempSeqPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MySequenceManage.ChangeSequence(0, tempSeqPath);
                        Thread.Sleep(1000);
                    }

                    if (PLC_ReadInt(52) == 1 && modeNameIsOK)
                    {    
                        MyPLC.DBWrite(101, 6, 2, new byte[] { 0, 1 });

                        string sn = PLC_ReadString(58, 13).Substring(2, 11);
                        sn = sn.Replace("\0", "");
                        MySequenceManage.SetSerialNumber(0, sn);

                        MySequenceManage.StartTest(0);
                    }
                }
            }
            else
            {
                MySeqRunTimeState.MESIsOn = false;

                if (MySeqRunTimeState.SequenceRunning[0] == false && PLC_ReadInt(52) == 1)
                {
                    MyPLC.DBWrite(101, 6, 2, new byte[] { 0, 1 });

                    //string sn = PLC_ReadString(58, 13).Substring(2, 11);
                    //sn = sn.Replace("\0", "");
                    //MySequenceManage.SetSerialNumber(0, sn);

                    MySequenceManage.StartTest(0);
                }
            }
            if (PLC_ReadByte(786) == 1)
            {
                MySequenceManage.StopTest(0);
            }

            return 0;
        }

        private static string ResolveRuntimeSequencePath(string sequenceName)
        {
            string name = (sequenceName ?? string.Empty).Trim(); if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name += ".json";
            if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return string.Empty;
            string assemblyDirectory = Path.GetDirectoryName(typeof(TestDllMain).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sequence", name),
                Path.Combine(assemblyDirectory, "Sequence", name),
                Path.Combine(assemblyDirectory, "..", "Sequence", name)
            };
            return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sequence", name);
        }

        private static string ResolveWritableRuntimeDirectory(string relativePath)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            try { Directory.CreateDirectory(path); return path; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FCT Engineering Studio", relativePath); Directory.CreateDirectory(path); return path;
            }
        }

        public double ProcessCleanup()
        {
            if (_fctInitializedInstrumentNames.Count > 0) return FCT_CleanupConfiguredInstrumentsCore();
            // ProcessCleanup may be called once by an explicit safe shutdown and again from Dispose.
            // Keep the legacy path idempotent so already released instruments do not raise first-chance NREs.
            try { if (MyCAN != null) MyCAN.CloseCANDevice(); } catch { }
            try { if (Resolver != null) Resolver.CloseCANDevice(); } catch { }
            try { if (LVDC != null) LVDC.DisconnectDevice(); } catch { }
            try { if (LVDC_KL15 != null) LVDC_KL15.DisconnectDevice(); } catch { }
            try { if (HVDC != null) HVDC.DisconnectDevice(); } catch { }
            return 0;
        }

        public double PreUUT(int socketIndex)
        {
            if (_fctInitializedInstrumentNames.Count > 0) return FCT_PrepareConfiguredInstrumentsCore(socketIndex);
            FCT_ResetGenericRuntimeCore(socketIndex);
            int testTime = MySeqRunTimeState.TestTime;
            double resDouble = -999;

            Relay.WriteSingleCoil(1, 0, true);
            Relay.WriteSingleCoil(1, 4, true);
            Relay.WriteSingleCoil(1, 8, true);
            Relay.WriteSingleCoil(1, 12, true);

            RES.SetResistance(1000, 1);
            RES.SetResistance(1000, 2);

            //HVDC.ConnectDevice(MyInstrumentManage.InstrumentList["HVDC"].Setting["Resource"].ToString(), "");
            HVDC.SetSourceVoltage(1);
            HVDC.SetOutput(true);
            Thread.Sleep(888);
            HVDC.SetOutput(false);

            RelayFctBoard.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,1");
            RelayHvMux.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0");
            
            string traceDirectory = ResolveWritableRuntimeDirectory(Path.Combine("Logs", "CanTrace"));
            MyCAN.StartTraceLog(Path.Combine(traceDirectory, DateTime.Now.ToString("yyyyMMdd") + ".asc"));

            Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true);

            return resDouble;
        }

        public double PostUUT(int socketIndex)
        {
            if (_fctInitializedInstrumentNames.Count > 0) return FCT_FinishConfiguredInstrumentsCore(socketIndex);
            FCT_ResetGenericRuntimeCore(socketIndex);
            //Console.WriteLine("" + this.MySeqRunTimeState.LogFileFullPath[socketIndex]);
            HVDC.SetSourceVoltage(0);
            Thread.Sleep(666);
            HVDC.SetOutput(false);
            LVDC.SetOutput(false);
            LVDC_KL15.SetOutput(false);

            RelayHvMux.WriteDO("5", "1");
            Thread.Sleep(16666);

            RelayFctBoard.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");
            RelayHvMux.WriteDO("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15", "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");
            
            //
            Relay.WriteSingleCoil(1, 0, false);
            Relay.WriteSingleCoil(1, 4, false);
            Relay.WriteSingleCoil(1, 8, false);
            Relay.WriteSingleCoil(1, 12, false);
            
            MyCAN.StopTraceLog();

            return 0;
        }

        public void TestingFinally(int socketIndex)     
        {
            if (MySeqRunTimeState.MESIsOn)
            {
                string path = MySeqRunTimeState.LogFileFullPath[0];

                try
                {
                    MES_Connect("10.231.137.4", 6000);

                    string result = MES_SendMessage(path);
                    if (result.Trim().ToLower() != "ok")
                    {
                        Thread.Sleep(200);
                        result = MES_SendMessage(path);
                        if (result.Trim().ToLower() != "ok")
                        {
                            Thread.Sleep(200);
                            result = MES_SendMessage(path);
                        }
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    MES_Disconnect();
                }

                
            }
            if (!string.IsNullOrWhiteSpace(_fctInstrumentSelectionJson) && !_fctInitializedInstrumentNames.Contains("PLC")) return;
            if (MySeqRunTimeState.SequenceFailed[0])
            {
                MyPLC.DBWrite(101, 6, 2, new byte[] { 0, 4 });
            }
            else
            {
                MyPLC.DBWrite(101, 6, 2, new byte[] { 0, 2 });
            }
        }

        //=========================================================================================================================

        #region LVDC Power Supply function List
        public void LVDC_ConnectDevice(int socketIndex)
        {
            try
            {
                string resourceName = MySequenceManage.GetInputStringValue(socketIndex, "resourceName");
                LVDC.ConnectDevice(resourceName, "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void LVDC_GetActVoltage(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                double actVoltage = -999;
                LVDC.GetActPower(out actVoltage);
                MySequenceManage.AddNumericTesting(socketIndex, stepName, actVoltage, comareType, lowLimit, highLimit, "V", "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void LVDC_GetActCurrent(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "GetActCurrent");
                double actCurrent = -999;
                LVDC.GetActCurrent(out actCurrent);
                MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, "A", "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void LVDC_SetSourceCurrent(int socketIndex)
        {
            try
            {
                double dCurrent = MySequenceManage.GetInputDoubleValue(socketIndex, "Current");
                LVDC.SetSourceCurrent(dCurrent);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void LVDC_SetSourceVoltage(int socketIndex)
        {
            try
            {
                double voltage = MySequenceManage.GetInputDoubleValue(socketIndex, "Voltage");
                LVDC.SetSourceVoltage(voltage);
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void LVDC_SetOutput(int socketIndex)
        {
            try
            {
                bool output = MySequenceManage.GetInputBoolValue(socketIndex, "Output");
                LVDC.SetOutput(output);
                if (output) Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void LVDC_KL15_ConnectDevice(int socketIndex) { try { LVDC_KL15.ConnectDevice(MySequenceManage.GetInputStringValue(socketIndex, "resourceName"), ""); } catch (Exception ex) { throw new Exception("", ex); } }
        public void LVDC_KL15_SetSourceVoltage(int socketIndex) { try { LVDC_KL15.SetSourceVoltage(MySequenceManage.GetInputDoubleValue(socketIndex, "Voltage")); Thread.Sleep(1000); } catch (Exception ex) { throw new Exception("", ex); } }
        public void LVDC_KL15_SetSourceCurrent(int socketIndex) { try { LVDC_KL15.SetSourceCurrent(MySequenceManage.GetInputDoubleValue(socketIndex, "Current")); } catch (Exception ex) { throw new Exception("", ex); } }
        public void LVDC_KL15_SetOutput(int socketIndex) { try { bool output = MySequenceManage.GetInputBoolValue(socketIndex, "Output"); LVDC_KL15.SetOutput(output); if (output) Thread.Sleep(2000); } catch (Exception ex) { throw new Exception("", ex); } }
        public void LVDC_KL15_GetActVoltage(int socketIndex) { try { double value; LVDC_KL15.GetActVoltage(out value); MySequenceManage.AddNumericTesting(socketIndex, MySequenceManage.GetInputStringValue(socketIndex, "StepName"), value, MySequenceManage.GetInputStringValue(socketIndex, "Comtype"), MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit"), MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit"), "V", "KL15"); } catch (Exception ex) { throw new Exception("", ex); } }
        public void LVDC_KL15_GetActCurrent(int socketIndex) { try { double value; LVDC_KL15.GetActCurrent(out value); MySequenceManage.AddNumericTesting(socketIndex, MySequenceManage.GetInputStringValue(socketIndex, "StepName"), value, MySequenceManage.GetInputStringValue(socketIndex, "Comtype"), MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit"), MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit"), "A", "KL15"); } catch (Exception ex) { throw new Exception("", ex); } }
        #endregion Power Supply function List

        #region Kewell C3000 Power Supply function List
        public void HVDC_ConnectDevice(int socketIndex)
        {
            try
            {
                string resourceName = MySequenceManage.GetInputStringValue(socketIndex, "resourceName");
                HVDC.ConnectDevice(resourceName, "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void HVDC_GetActVoltage(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                double actVoltage = -999;
                HVDC.GetActPower(out actVoltage);
                MySequenceManage.AddNumericTesting(socketIndex, stepName, actVoltage, comareType, lowLimit, highLimit, "V", "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void HVDC_GetActCurrent(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "GetActCurrent");
                double actCurrent = -999;
                HVDC.GetActCurrent(out actCurrent);
                MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, "A", "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void HVDC_SetSourceCurrent(int socketIndex)
        {
            try
            {
                double dCurrent = MySequenceManage.GetInputDoubleValue(socketIndex, "SourceCurrent");
                HVDC.SetSourceCurrent(dCurrent);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void HVDC_SetSourceVoltage(int socketIndex)
        {
            try
            {
                double voltage = MySequenceManage.GetInputDoubleValue(socketIndex, "Voltage");
                HVDC.SetSourceVoltage(voltage);
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }

        public void HVDC_SetOutput(int socketIndex)
        {
            try
            {
                bool output = MySequenceManage.GetInputBoolValue(socketIndex, "Output");
                HVDC.SetOutput(output);
                if (output) Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion Power Supply function List

        #region DMM function List
        public void DMM_InitDMM(int socketIndex)
        {
            try
            {
                string resouce = MySequenceManage.GetInputStringValue(socketIndex, "resouce");
                DMM.InitDMM();
                DMM.OpenSession(resouce);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_CloseSession(int socketIndex)
        {
            try
            {
                DMM.CloseSession();
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_ConfigDMMforMeasure(int socketIndex)
        {
            try
            {
                Instruments.DMM.MeasureTypes measureTypes;
                string Function = MySequenceManage.GetInputStringValue(socketIndex, "Function");
                switch (Function)
                {
                    case "DCVoltage":
                        measureTypes = Instruments.DMM.MeasureTypes.DCVoltage;
                        break;
                    case "DCCurrent":
                        measureTypes = Instruments.DMM.MeasureTypes.DCCurrent;
                        break;
                    case "ACVoltage":
                        measureTypes = Instruments.DMM.MeasureTypes.ACVoltage;
                        break;
                    case "ACCurrent":
                        measureTypes = Instruments.DMM.MeasureTypes.ACCurrent;
                        break;
                    default:
                        measureTypes = Instruments.DMM.MeasureTypes.DCVoltage;
                        break;
                }
                DMM.ConfigDMMforMeasure(measureTypes);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_GetMeasureValue(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                //int timeout = Convert.ToInt16(MySequenceManage.GetInputDoubleValue(socketIndex, "timeout"));
                //int factor = Convert.ToInt16(MySequenceManage.GetInputDoubleValue(socketIndex, "factor"));
                double value = DMM.GetMeasureValue() * 1f;
                value = Math.Round(value, 8 - ((int)value).ToString().Length);
                MySequenceManage.AddNumericTesting(socketIndex, stepName, value, comareType, lowLimit, highLimit, Unit, "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_ConfigDMMforDCVol(int socketIndex)
        {
            try
            {
                double range = MySequenceManage.GetInputDoubleValue(socketIndex, "Range");
                double solution = MySequenceManage.GetInputDoubleValue(socketIndex, "Solution");
                DMM.ConfigDMMforDC(range, solution);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_ConfigDMMforDCCur(int socketIndex)
        {
            try
            {
                double range = MySequenceManage.GetInputDoubleValue(socketIndex, "Range");
                double solution = MySequenceManage.GetInputDoubleValue(socketIndex, "Solution");
                DMM.ConfigDMMforDCCurrent(range, solution);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_ConfigDMMforACVol(int socketIndex)
        {
            try
            {
                double range = MySequenceManage.GetInputDoubleValue(socketIndex, "Range");
                double solution = MySequenceManage.GetInputDoubleValue(socketIndex, "Solution");
                DMM.ConfigDMMforAC(range, solution);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void DMM_ConfigDMMforACCur(int socketIndex)
        {
            try
            {
                double range = MySequenceManage.GetInputDoubleValue(socketIndex, "Range");
                double solution = MySequenceManage.GetInputDoubleValue(socketIndex, "Solution");
                DMM.ConfigDMMforACCurrent(range, solution);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion DMM function List

        #region CAN function List
        public void CAN_SetValue(int socketIndex)
        {
            try
            {
                string parm = MySequenceManage.GetInputStringValue(socketIndex, "parm");
                Object obj = MySequenceManage.GetInputStringValue(socketIndex, "obj");
                MyCAN.SetValue(parm, obj);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void CAN_OpenCANDevice(int socketIndex)
        {
            try
            {
                uint deviceType = Convert.ToUInt32(MySequenceManage.GetInputStringValue(socketIndex, "deviceType"));
                ushort canName = Convert.ToUInt16(MySequenceManage.GetInputStringValue(socketIndex, "canName"));
                uint baudRate = Convert.ToUInt32(MySequenceManage.GetInputStringValue(socketIndex, "baudRate"));
                MyCAN.OpenCANDevice(deviceType, canName, baudRate);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void CAN_OpenCANDevice_FD(int socketIndex)
        {
            try
            {
                uint deviceType = Convert.ToUInt32(MySequenceManage.GetInputStringValue(socketIndex, "deviceType"));
                ushort canName = Convert.ToUInt16(MySequenceManage.GetInputStringValue(socketIndex, "canName"));
                string baudRate = MySequenceManage.GetInputStringValue(socketIndex, "baudRate");
                MyCAN.OpenCANDevice_FD(deviceType, canName, baudRate);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void CAN_CloseCANDevice(int socketIndex)
        {
            try
            {
                MyCAN.CloseCANDevice();
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void CAN_SendWakeUpMessage(int socketIndex)
        {
            try
            {
                MyCAN.SendMessage(0x50F, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
            }
            catch (Exception)
            {
            }
        }
        public void CAN_SetDUTCurrent(int socketIndex)
        {
            try
            {
                float maxCurrent = (float)MySequenceManage.GetInputDoubleValue(socketIndex, "MaxCurrent");
                float stepCurrent = (float)MySequenceManage.GetInputDoubleValue(socketIndex, "StepCurrent");
                float holdTime = (float)MySequenceManage.GetInputDoubleValue(socketIndex, "HoldTime");
                float frequency = (float)MySequenceManage.GetInputDoubleValue(socketIndex, "Frequency");
                
                //Wait Cycle time 
                for (int i = 0; i < 2000; i++)
                {
                    Thread.Sleep(100);
                    if ((DateTime.Now - StartCurrentDatetime).TotalSeconds > SettingCycleTime)
                    {
                        Console.WriteLine("---------------------------------------");
                        Thread.Sleep(100);
                        DUT_WriteByte(0x70, 4, 0x00);
                        Thread.Sleep(100);
                        DUT_WriteByte(0x70, 4, 0x01);
                        Thread.Sleep(100);
                        Console.WriteLine("=======================================");
                        break;
                    }
                }

                for (int i = 0; i < 2000; i++)
                {
                    Thread.Sleep(100);
                    byte[] motorStatus = new byte[9];
                    DUT_ReadMultiByte(0x64, 9, out motorStatus);
                    if (motorStatus[0] != 2) break;
                }
                SetCurrentCount = 0;
                SettingCurrent = maxCurrent;
                DUT_SetDUTCurrent(maxCurrent * 1.414f, stepCurrent, holdTime, frequency);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void CAN_WriteSignalValue(int socketIndex)
        {
            try
            {
                uint addrOffset = (uint)(MySequenceManage.GetInputDoubleValue(socketIndex, "AddrOffset"));
                int tableIndex = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "TableIndex");
                int dataSize = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "DataSize");
                float fValue = (float)MySequenceManage.GetInputDoubleValue(socketIndex, "Value");

                DUT_WriteFloat(addrOffset, tableIndex, dataSize, fValue);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void CAN_ReadSignalValue(int socketIndex)
        {
            try
            {
                
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                uint addrOffset = (uint)(MySequenceManage.GetInputDoubleValue(socketIndex, "AddrOffset"));
                int tableIndex = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "TableIndex");
                int dataSize = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "DataSize");

                double value = -999;
                if (dataSize == 4) value = DUT_ReadFloat(addrOffset, tableIndex, dataSize);
                if (dataSize == 1) value = DUT_ReadByte(addrOffset, tableIndex, dataSize);
                
                MySequenceManage.AddNumericTesting(socketIndex, stepName, value, comareType, lowLimit, highLimit, Unit, "");
            }
            catch (Exception)
            {
                
            }
        }
        public void DUT_ComucationInit(int socketIndex)
        {
            try
            {
                if (MyCAN == null) throw new InvalidOperationException("DUTCAN is not initialized. Select DUTCAN in Instrument Center and execute ProcessSetup first.");
                TxID = FCT_ParseConfiguredCanId(FCT_InputString(socketIndex, "TxID", "2030"), 0x7EE);
                RxID = FCT_ParseConfiguredCanId(FCT_InputString(socketIndex, "RxID", "2031"), 0x7EF);

                byte[] txData = { 0xFF, 0xFA, 0x55, 0xA9, 0x00, 0x04, 0xFF, 0x00 };
                FCT_CanDiagnostic("DUT_ComucationInit START: socket=" + socketIndex + "; TX=0x" + TxID.ToString("X") + "; RX=0x" + RxID.ToString("X") + "; request=" + BitConverter.ToString(txData).Replace("-", " "));
                byte[] rxData = new byte[0];
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length >= 4)
                {
                    FirstAddress = BitConverter.ToUInt32(rxData.Take(4).ToArray(), 0);
                    FCT_CanDiagnostic("DUT_ComucationInit SUCCESS: FirstAddress=0x" + FirstAddress.ToString("X8") + "; RX=" + BitConverter.ToString(rxData).Replace("-", " "));
                    MySequenceManage.AddCustomString(socketIndex, FCT_InputString(socketIndex, "StepName", "DUT Communication Init"), "FirstAddress=0x" + FirstAddress.ToString("X8"), "RX=" + BitConverter.ToString(rxData).Replace("-", " "));
                }
                else
                {
                    FCT_CanDiagnostic("DUT_ComucationInit FAILED: final response length=" + rxData.Length + "; response=" + BitConverter.ToString(rxData).Replace("-", " "));
                    throw new InvalidOperationException("当前产品CAN通道已打开，但通信初始化返回不足4字节。请检查实际选择的MAINCAN/DUTCAN及详细日志：" + FCT_CanDiagnosticPath());
                }
            }
            catch (Exception ex)
            {
                FCT_CanDiagnostic("DUT_ComucationInit EXCEPTION", ex);
                throw new InvalidOperationException("DUT_ComucationInit failed: " + ex.Message, ex);
            }
        }
        private static uint FCT_ParseConfiguredCanId(string text, uint defaultValue)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0) return defaultValue;
            uint parsed;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed) ? parsed : defaultValue;
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed) ? parsed : defaultValue;
        }
        public void CAN_FT2APP(int socketIndex)
        {
            try
            {
                if (MyCAN == null) throw new InvalidOperationException("Product CAN is not initialized. Select MAINCAN/DUTCAN in Instrument Center and execute ProcessSetup first.");
                // Mirror CAN_APP2FT UDS path; write FF FF FF FF to DID EEEE to unlock / exit FT (ZLG comment: 解锁).
                string rxString = "";
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "10 03", ref rxString, "50 03");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "27 01", ref rxString, "67 01");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "27 02 FF FF FF FF", ref rxString, "67 02");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "2E EE EE FF FF FF FF", ref rxString, "6E EE");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "11 01", ref rxString, "61 01");
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("CAN_FT2APP failed: " + ex.Message, ex);
            }
        }
        public void CAN_APP2FT(int socketIndex)
        {
            try
            {
                if (MyCAN == null) throw new InvalidOperationException("DUTCAN is not initialized. Select DUTCAN in Instrument Center and execute ProcessSetup first.");
                string rxString = "";
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "10 03", ref rxString, "50 03");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "27 01", ref rxString, "67 01");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "27 02 FF FF FF FF", ref rxString, "67 02");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "2E EE EE AA 55 AA 55", ref rxString, "6E EE");
                MyCAN.UDS_Request(0x18DAF0FA, 0x18DAFAF0, "11 01", ref rxString, "61 01");
                Thread.Sleep(1000);
                //DUT_WriteBytes(0xAC, 0x75, new byte[] { 0xAA, 0x55, 0xAA, 0x55 });
                //DUT_Execute(0xB0, 0x00);
                //Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("CAN_APP2FT failed: " + ex.Message, ex);
            }
        }

        private int SetCurrentCount = 0;

        private uint FirstAddress;
        private uint TxID;
        private uint RxID;
        private void DUT_SetDUTCurrent(float maxCurrent, float stepCurrent, float holdTime, float frequency)
        {
            try
            {
                //SettingCurrent = Math.Round(maxCurrent, 1);
                SettingCycleTime = holdTime;
                StartCurrentDatetime = DateTime.Now;

                byte[] rxData = new byte[0];
                byte[] valueBytes = null;
                uint addr = FirstAddress + 0x60;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    txData = new byte[40];

                    txData[0] = rxData[3];
                    txData[1] = rxData[2];
                    txData[2] = rxData[1];
                    txData[3] = rxData[0];
                    txData[4] = 0x00;
                    txData[5] = 0x20;
                    txData[6] = 0x00;
                    txData[7] = 0x00;
                    txData[8] = 0x00;
                    txData[9] = 0x00;
                    txData[10] = 0x00;
                    txData[11] = 0x00;
                    valueBytes = BitConverter.GetBytes(maxCurrent);
                    txData[12] = valueBytes[0];
                    txData[13] = valueBytes[1];
                    txData[14] = valueBytes[2];
                    txData[15] = valueBytes[3];
                    valueBytes = BitConverter.GetBytes(stepCurrent);
                    txData[16] = valueBytes[0];
                    txData[17] = valueBytes[1];
                    txData[18] = valueBytes[2];
                    txData[19] = valueBytes[3];
                    valueBytes = BitConverter.GetBytes(holdTime);
                    txData[20] = valueBytes[0];
                    txData[21] = valueBytes[1];
                    txData[22] = valueBytes[2];
                    txData[23] = valueBytes[3];
                    valueBytes = BitConverter.GetBytes(frequency);
                    txData[24] = valueBytes[0];
                    txData[25] = valueBytes[1];
                    txData[26] = valueBytes[2];
                    txData[27] = valueBytes[3];
                    txData[28] = 0x04;  //Mode
                    txData[29] = 0x00;
                    txData[30] = 0x32;  //Ramp time
                    txData[31] = 0x00;
                    txData[32] = 0x10;  //base Frequence
                    txData[33] = 0x27;
                    txData[34] = 0x01;  //1
                    txData[35] = 0x01;  //1

                    DUT_WriteRead(txData, out rxData);

                    SetCurrentCount++;
                    byte[] motorStatus = new byte[9];
                    DUT_ReadMultiByte(0x64, 9, out motorStatus);
                    if (motorStatus[0] != 2 && SetCurrentCount <= 6)
                    {
                        DUT_SetDUTCurrent(maxCurrent, stepCurrent, holdTime, frequency);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        private void DUT_WriteFloat(uint addrOffset, int tableIndex, int dataSize, float fValue)
        {
            try
            {
                byte[] rxData = new byte[0];
                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    //Read Old Value
                    txData = new byte[] { rxData[3], rxData[2], rxData[1], rxData[0], 0x00, (byte)(tableIndex + dataSize), 0xFF, 0x00 };
                    DUT_WriteRead(txData, out rxData);
                    if (rxData.Length > 0)
                    {
                        //Modify Value
                        txData[6] = 0x00;
                        byte[] valueBytes = BitConverter.GetBytes(fValue);
                        rxData[tableIndex + 0] = valueBytes[3];
                        rxData[tableIndex + 1] = valueBytes[2];
                        rxData[tableIndex + 2] = valueBytes[1];
                        rxData[tableIndex + 3] = valueBytes[0];
                        txData = txData.Concat(rxData).ToArray();

                        //Write Value
                        DUT_WriteRead(txData, out rxData);
                    }
                }
            }
            catch (Exception ex)
            {
                return;
            }
        }
        private void DUT_WriteByte(uint addrOffset, int tableIndex, byte byteValue)
        {
            try
            {
                byte[] rxData = new byte[0];
                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    //Read Old Value
                    txData = new byte[] { rxData[3], rxData[2], rxData[1], rxData[0], 0x00, (byte)(tableIndex + 1), 0xFF, 0x00 };
                    DUT_WriteRead(txData, out rxData);
                    if (rxData.Length > 0)
                    {
                        //Modify Value
                        txData[6] = 0x00;
                        rxData[tableIndex] = byteValue;
                        txData = txData.Concat(rxData).ToArray();

                        //Write Value
                        DUT_WriteRead(txData, out rxData);
                    }
                }
            }
            catch (Exception ex)
            {
                return;
            }
        }
        private void DUT_WriteBytes(uint addrOffset, int tableIndex, byte[] byteValues)
        {
            try
            {
                byte[] rxData = new byte[0];

                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);

                if (rxData.Length > 0)
                {
                    uint secondAddress = BitConverter.ToUInt32(rxData.Take(4).ToArray(), 0);
                    secondAddress = secondAddress + (uint)tableIndex;
                    addrBytes = BitConverter.GetBytes(secondAddress);
                    txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, (byte)byteValues.Length, 0x00, 0x00 };
                    txData = txData.Concat(byteValues).ToArray();

                    DUT_WriteRead(txData, out rxData);
                }
            }
            catch (Exception)
            {
                return;
            }
        }
        private void DUT_Execute(uint addrOffset, int tableIndex)
        {
            try
            {
                byte[] rxData = new byte[0];

                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);

                if (rxData.Length > 0)
                {
                    uint secondAddress = BitConverter.ToUInt32(rxData.Take(4).ToArray(), 0);
                    secondAddress = secondAddress + (uint)tableIndex;
                    addrBytes = BitConverter.GetBytes(secondAddress);
                    txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x00, 0x00, 0xFF };

                    DUT_WriteRead(txData, out rxData);
                }
            }
            catch (Exception)
            {
                return;
            }
        }
        private float DUT_ReadMultiFloat(uint addrOffset, int dataLen, out float[] values)
        {
            try
            {
                float resDouble = 0;
                values = new float[dataLen];

                byte[] rxData = new byte[0];
                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    txData = new byte[] { rxData[3], rxData[2], rxData[1], rxData[0], 0x00, (byte)(dataLen * 4), 0xFF, 0x00 };
                    DUT_WriteRead(txData, out rxData);
                    if (rxData.Length >= dataLen * 4)
                    {
                        for (int i = 0; i < dataLen; i++)
                        {
                            values[i] = BitConverter.ToSingle(rxData, i * 4);
                        }
                    }
                    else
                    {
                        DUT_WriteRead(txData, out rxData);
                        if (rxData.Length >= dataLen * 4)
                        {
                            for (int i = 0; i < dataLen; i++)
                            {
                                values[i] = BitConverter.ToSingle(rxData, i * 4);
                            }
                        }
                        else
                        {
                            DUT_WriteRead(txData, out rxData);
                            if (rxData.Length >= dataLen * 4)
                            {
                                for (int i = 0; i < dataLen; i++)
                                {
                                    values[i] = BitConverter.ToSingle(rxData, i * 4);
                                }
                            }
                        }
                    }
                }

                return resDouble;
            }
            catch (Exception ex)
            {
                values = new float[0];
                return -999;
            }
        }
        private float DUT_ReadMultiByte(uint addrOffset, int dataLen, out byte[] values)
        {
            try
            {
                float resDouble = 0;
                values = new byte[dataLen];

                byte[] rxData = new byte[0];
                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    txData = new byte[] { rxData[3], rxData[2], rxData[1], rxData[0], 0x00, (byte)(dataLen), 0xFF, 0x00 };
                    DUT_WriteRead(txData, out rxData);
                    if (rxData.Length >= dataLen)
                    {
                        for (int i = 0; i < dataLen; i++)
                        {
                            values[i] = rxData[i];
                        }
                    }
                    else
                    {
                        DUT_WriteRead(txData, out rxData);
                        if (rxData.Length >= dataLen)
                        {
                            for (int i = 0; i < dataLen; i++)
                            {
                                values[i] = rxData[i];
                            }
                        }
                        else
                        {
                            DUT_WriteRead(txData, out rxData);
                            if (rxData.Length >= dataLen)
                            {
                                for (int i = 0; i < dataLen; i++)
                                {
                                    values[i] = rxData[i];
                                }
                            }
                        }
                    }
                }

                return resDouble;
            }
            catch (Exception ex)
            {
                values = new byte[0];
                return -999;
            }
        }
        private float DUT_ReadFloat(uint addrOffset, int tableIndex, int dataSize)
        {
            try
            {
                float resDouble = -888;

                byte[] rxData = new byte[0];
                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    txData = new byte[] { rxData[3], rxData[2], rxData[1], rxData[0], 0x00, (byte)(tableIndex + dataSize), 0xFF, 0x00 };

                    if (addrOffset == 72 && tableIndex == 4)
                    {
                        resDouble = 0;
                        float[] fArray = new float[40];
                        for (int i = 0; i < 40; i++)
                        {
                            DUT_WriteRead(txData, out rxData);
                            if (rxData.Length > 0)
                            {
                                fArray[i] = BitConverter.ToSingle(rxData.Skip(tableIndex).Take(dataSize).ToArray(), 0) * 10;
                            }
                        }
                        Array.Sort(fArray);
                        resDouble = fArray.Skip(8).Take(20).ToArray().Average();
                        resDouble = (float)Math.Round(resDouble, 0);
                    }
                    else if (addrOffset == 72 && tableIndex == 0)
                    {
                        DUT_WriteRead(txData, out rxData);
                        resDouble = BitConverter.ToSingle(rxData.Skip(tableIndex).Take(dataSize).ToArray(), 0);
                        if (resDouble > 360)
                        {
                            resDouble = resDouble - 360;

                        }
                    }
                    else
                    {
                        DUT_WriteRead(txData, out rxData);
                        if (rxData.Length > 0)
                        {
                            resDouble = BitConverter.ToSingle(rxData.Skip(tableIndex).Take(dataSize).ToArray(), 0);
                            resDouble = (float)Math.Round(resDouble, 3);
                        }
                    }
                }

                return resDouble;
            }
            catch (Exception ex)
            {
                return -999;
            }
        }
        private byte DUT_ReadByte(uint addrOffset, int tableIndex, int dataSize)
        {
            try
            {
                byte resByte = 0;

                byte[] rxData = new byte[0];
                uint addr = FirstAddress + addrOffset;
                byte[] addrBytes = BitConverter.GetBytes(addr);
                byte[] txData = new byte[] { addrBytes[3], addrBytes[2], addrBytes[1], addrBytes[0], 0x00, 0x04, 0xFF, 0x00 };
                DUT_WriteRead(txData, out rxData);
                if (rxData.Length > 0)
                {
                    txData = new byte[] { rxData[3], rxData[2], rxData[1], rxData[0], 0x00, (byte)(tableIndex + dataSize), 0xFF, 0x00 };
                    DUT_WriteRead(txData, out rxData);
                    if (rxData.Length > 0)
                    {
                        resByte = rxData.Skip(tableIndex).Take(1).ToArray()[0];
                    }
                }

                return resByte;
            }
            catch (Exception)
            {
                return 0xFF;
            }
        }
        private void DUT_WriteRead(byte[] txData, out byte[] rxData)
        {
            try
            {
                rxData = new byte[4];
                byte[] tempRxBytes = new byte[0];
                for (int i = 0; i < 3; i++)
                {
                    FCT_CanDiagnostic("DUT_WriteRead attempt " + (i + 1) + "/3 START");
                    bool received = DUT_WriteRead_Once(txData, out tempRxBytes);
                    FCT_CanDiagnostic("DUT_WriteRead attempt " + (i + 1) + "/3 END: matched=" + received + "; bytes=" + tempRxBytes.Length + "; data=" + BitConverter.ToString(tempRxBytes).Replace("-", " "));
                    if (received) break;
                }
                rxData = (byte[])tempRxBytes.Clone();
            }
            catch (Exception ex)
            {
                FCT_CanDiagnostic("DUT_WriteRead EXCEPTION", ex);
                throw new Exception("DUT write/read failed. Diagnostic log: " + FCT_CanDiagnosticPath(), ex);
            }
        }
        private bool DUT_WriteRead_Once(byte[] txData, out byte[] rxData)
        {
            try
            {
                bool dataReceived = false;
                rxData = new byte[0];
                List<Instruments.CAN.CANMessage> rxDataList = new List<Instruments.CAN.CANMessage>();

                FCT_CanDiagnostic("ClearTxRxBuffer START");
                MyCAN.ClearTxRxBuffer();
                FCT_CanDiagnostic("ClearTxRxBuffer END");

                int frameCount = 0;
                if((txData.Length % 8) == 0)
                {
                    frameCount = txData.Length / 8;
                }
                else
                {
                    frameCount = txData.Length / 8 + 1;
                }

                for (int i = 0; i < frameCount; i++)
                {
                    int offset = i * 8;
                    int payloadLength = Math.Min(8, txData.Length - offset);
                    byte[] frame = new byte[8];
                    Array.Copy(txData, offset, frame, 0, payloadLength);
                    uint frameId = i == 0 ? TxID : RxID;
                    FCT_CanDiagnostic((i == 0 ? "TX frame " : "TX continuation ") + (i + 1) + "/" + frameCount + ": ID=0x" + frameId.ToString("X") + "; DLC=8; PAYLOAD=" + payloadLength + "; DATA=" + BitConverter.ToString(frame).Replace("-", " "));
                    MyCAN.SendMessage(frameId, frame);
                    Thread.Sleep(60);
                }

                if (frameCount == 1)    //read responce
                {
                    for (int i = 0; i < 200; i++)
                    {
                        Thread.Sleep(1);
                        List<Instruments.CAN.CANMessage> queue = new List<Instruments.CAN.CANMessage>();
                        queue = FCT_ReceiveCanMessages(MyCAN);
                        if (queue.Count > 0) FCT_CanDiagnostic("RX poll " + (i + 1) + ": queue count=" + queue.Count);
                        foreach (Instruments.CAN.CANMessage item in queue)
                        {
                            byte[] bytes = item.DATA ?? new byte[0];
                            bool idMatched = (item.ID & 0x1FFFFFFF) == (RxID & 0x1FFFFFFF);
                            FCT_CanDiagnostic("RX frame: ID=0x" + item.ID.ToString("X") + "; DLC=" + bytes.Length + "; DATA=" + BitConverter.ToString(bytes).Replace("-", " ") + "; expected=0x" + RxID.ToString("X") + "; matched=" + idMatched);
                            if (!idMatched) continue;
                            rxData = rxData.Concat(bytes).ToArray();
                        }
                        if (rxData.Length >= txData[5])
                        {
                            dataReceived = true;
                            break;
                        }
                    }
                }
                else
                {
                    dataReceived = true;
                }

                return dataReceived;
            }
            catch (Exception ex)
            {
                FCT_CanDiagnostic("DUT_WriteRead_Once EXCEPTION", ex);
                throw new Exception("DUT single write/read failed. Diagnostic log: " + FCT_CanDiagnosticPath(), ex);
            }
        }
        #endregion CAN function List

        #region PLC Function List 
        public void PLC_LoadFinished(int socketIndex)
        {
            try
            {
                MyPLC.DBWrite(101, 4, 2, new Byte[] { 0, 1});
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        private byte PLC_ReadByte(int addr)
        {
            byte retInt = 0;
            byte[] rxBytes = new byte[0];
            MyPLC.DBRead(101, addr, 1, out rxBytes);
            if (rxBytes.Length >= 1)
            {
                retInt = rxBytes[0];
            }
            return retInt;
        }

        private int PLC_ReadInt(int addr)
        {
            int retInt = -1;
            byte[] rxBytes = new byte[0];
            MyPLC.DBRead(101, addr, 2, out rxBytes);
            if (rxBytes.Length >= 2)
            {
                retInt = rxBytes[0] * 0x100 + rxBytes[1];
            }
            return retInt;
        }
        private string PLC_ReadString(int addr, int len)
        {
            string retString = "";
            byte[] rxBytes = new byte[0];
            MyPLC.DBRead(101, addr, len, out rxBytes);
            if (rxBytes.Length >= len)
            {
                retString = ASCIIEncoding.ASCII.GetString(rxBytes);
            }
            return retString;
        }
        private void PLC_WriteInt(int addr, int intValue)
        {
            try
            {
                byte[] txBytes = new byte[2];
                txBytes[1] = (byte)(intValue % 256);
                txBytes[0] = (byte)(intValue / 256);
                MyPLC.DBWrite(101, addr, 2, txBytes);
            }
            catch (Exception)
            {
                byte[] txBytes = new byte[2];
                txBytes[0] = (byte)(intValue % 256);
                txBytes[1] = (byte)(intValue / 256);
                MyPLC.DBWrite(101, addr, 2, txBytes);
            }
        }
        #endregion

        #region RES function List
        public void RES_SetResistance(int socketIndex)
        {
            try
            {
                double resValue = MySequenceManage.GetInputDoubleValue(socketIndex, "ResValue");
                int channel = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "Channel");
                RES.SetResistance(resValue, channel);
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion RES function List

        #region Resolver function List
        public void Resolver_Init(int socketIndex)
        {
            try
            {
                Resolver.SendMessage(0x80000001, new byte[8]);
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void Resolver_SetSpeed(int socketIndex)
        {
            try
            {
                Resolver.DBC_SendSignalValue("2147483649_mode_switch", 0, true);
                Thread.Sleep(50);

                double speed = MySequenceManage.GetInputDoubleValue(socketIndex, "Speed");
                Resolver.DBC_SendSignalValue("2505419280_Polarpair", 6, false);
                Resolver.DBC_SendSignalValue("2505419280_Speed", speed, true);
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void Resolver_SetPosition(int socketIndex)
        {
            try
            {
                Resolver.DBC_SendSignalValue("2505419280_Polarpair", 1, true);
                Thread.Sleep(50);

                double speed = MySequenceManage.GetInputDoubleValue(socketIndex, "Position");
                Resolver.DBC_SendSignalValue("2147483649_mode_switch", 1, false);
                Resolver.DBC_SendSignalValue("2147483649_Position", speed, true);
                Thread.Sleep(500);
                
                
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        public void Resolver_Stop(int socketIndex)
        {
            try
            {
                Resolver.DBC_SendSignalValue("2505419280_Speed", 0, true);
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion Resolver function List

        #region DAQ function List
        private double[] k = { 1, 1.004, 1.006 };
        private double[] b = { 0, 0, 0 };
        public void DAQ_ReadCurrent(int socketIndex)
        {
            try
            {
                int channel = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "Channel");
                double aiValue = PCI6320.ReadValue($"Dev1/ai{channel}", -10, 10, "");
                double actCurrent = aiValue / 68f * 1500 * k[channel] + b[channel];

                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                uint addrOffset = (uint)(MySequenceManage.GetInputDoubleValue(socketIndex, "AddrOffset"));
                int tableIndex = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "TableIndex");
                int dataSize = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "DataSize");

                MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, Unit, "");
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion DAQ function List

        #region MOXA function List
        public void MOXA_SetDO(int socketIndex)
        {
            try
            {
                int moxaIndex = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "MoxaIndex");
                string channels = MySequenceManage.GetInputStringValue(socketIndex, "Channels");
                string values = MySequenceManage.GetInputStringValue(socketIndex, "Values");
                if(moxaIndex == 0)
                    RelayFctBoard.WriteDO(channels, values);
                else
                    RelayHvMux.WriteDO(channels, values);

                Thread.Sleep(60);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion MOXA function List

        #region Relay function List
        public void Relay_SetDO(int socketIndex)
        {
            try
            {
                int channel = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "Channel");
                int value = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "Value");

                Relay.WriteSingleCoil(1, (ushort)channel, value == 1);

                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                throw new Exception("", ex);
            }
        }
        #endregion Relay function List

        #region MES Function List
        
        private Socket socket;
        private byte[] buffer = new byte[1024];
        private bool MES_Connect(string ipAddress, int Port)
        {
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(IPAddress.Parse(ipAddress), Port);
                return true;
            }
            catch (Exception ex)
            {
                return false;
                throw;
            }

        }
        private void MES_Disconnect()
        {
            socket.Close();

        }
        private string MES_SendMessage(string message)
        {
            try
            {
                byte[] data = ASCIIEncoding.UTF8.GetBytes(message);
                socket.Send(data);
                socket.ReceiveTimeout = 2000;
                int ReadBytes = socket.Receive(buffer);
                string response = ASCIIEncoding.UTF8.GetString(buffer, 0, ReadBytes);
                return response;
            }
            catch (Exception ex)
            {
                return null;
                throw;
            }
        }
        #endregion
        
        #region Test Function List
        private float[] OldViperTemps = new float[6];
        private float[] NewViperTemps = new float[6];
        public void Test_SoftwareVersion(int socketIndex)
        {
            try
            {
                byte[] rxData = new byte[0];
                byte[] bytes = new byte[4];
                string responceString = "00 00 00 00";
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                string limit = MySequenceManage.GetInputStringValue(socketIndex, "Limit");

                DUT_ReadMultiByte(92, 4, out bytes);

                responceString = "";
                foreach (byte byteItem in bytes)
                {
                    responceString += $"{byteItem:X2} ";
                }
                responceString = responceString.Trim();

                MySequenceManage.AddStringTesting(socketIndex, stepName, responceString, "", limit, "");

                if (responceString != limit)
                {
                    MySequenceManage.GotoByStepName(0, "Set HVDC Voltage 0V PostUUT");
                }
            }
            catch (Exception)
            {

            }
        }
        public void Test_CANCommunication(int socketIndex)
        {
            try
            {
                if (MyCAN == null) throw new InvalidOperationException("DUTCAN is not initialized. Select DUTCAN in Instrument Center and execute ProcessSetup first.");
                byte[] txData = { 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02 };
                byte[] rxData = new byte[0];
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                string limit = MySequenceManage.GetInputStringValue(socketIndex, "Limit");

                bool useRelaySwitch = _fctInitializedInstrumentNames.Count == 0 || _fctInitializedInstrumentNames.Contains("RELAY_FCT");
                if (useRelaySwitch) { RelayFctBoard.WriteDO("13,14", "0,1"); Thread.Sleep(60); }

                MyCAN.SendMessage(TxID, txData);
                Thread.Sleep(60);
                List<Instruments.CAN.CANMessage> rxDataList = new List<Instruments.CAN.CANMessage>();
                rxDataList = FCT_ReceiveCanMessages(MyCAN);
                foreach (Instruments.CAN.CANMessage item in rxDataList)
                {
                    if ((item.ID & 0x1FFFFFFF) != (RxID & 0x1FFFFFFF)) continue;
                    rxData = rxData.Concat(item.DATA).ToArray();
                }

                if (useRelaySwitch) { RelayFctBoard.WriteDO("13,14", "1,0"); Thread.Sleep(60); }

                string responceString = "";
                foreach (byte byteItem in rxData)
                {
                    responceString += $"{byteItem:X2} ";
                }
                responceString = responceString.Trim();

                MySequenceManage.AddStringTesting(socketIndex, stepName, responceString, "", limit, "");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Test_CANCommunication failed: " + ex.Message, ex);
            }
        }
        public void Test_DUT_Communication(int socketIndex)
        {
            try
            {
                byte[] rxData = new byte[0];
                string responceString = "00 00 00 00 00 00 00 00";
                List<Instruments.CAN.CANMessage> rxDataList = new List<Instruments.CAN.CANMessage>();
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");

                RelayFctBoard.WriteDO("12,13", "0,1");
                MyCAN.ClearTxRxBuffer();

                MyCAN.SendMessage(TxID, new byte[] { 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02 });
                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(1);
                    rxDataList = FCT_ReceiveCanMessages(MyCAN);
                    foreach (Instruments.CAN.CANMessage item in rxDataList)
                    {
                        if ((item.ID & 0x1FFFFFFF) != (RxID & 0x1FFFFFFF)) continue;
                        responceString = "";
                        foreach (byte byteItem in item.DATA)
                        {
                            responceString += $"{byteItem:X2} ";
                        }
                        responceString = responceString.Trim();
                    }
                }

                MySequenceManage.AddStringTesting(socketIndex, stepName, responceString, "", "02 02 02 02 02 02 02 02", "");

                RelayFctBoard.WriteDO("12,13", "1,0");
            }
            catch (Exception)
            {

            }
        }
        public void Test_VIPER_TempTest(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                uint addrOffset = (uint)(MySequenceManage.GetInputDoubleValue(socketIndex, "AddrOffset"));
                int dataLen = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "DataLen");
                string[] stepNames = { "Initial Viper A_U Temp", "Initial Viper A_L Temp", "Initial Viper B_U Temp", "Initial Viper B_L Temp", "Initial Viper C_U Temp", "Initial Viper C_L Temp" };

                float[] values = null;
                string stepName = "";
                DUT_ReadMultiFloat(addrOffset, dataLen, out values);
                for (int i = 0; i < dataLen; i++)
                {
                    OldViperTemps[i] = values[i];
                    stepName = MySequenceManage.GetInputStringValue(socketIndex, $"StepName{i}");
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, values[i], comareType, lowLimit, highLimit, Unit, "");
                }

                stepName = MySequenceManage.GetInputStringValue(socketIndex, $"StepName6");
                MySequenceManage.AddNumericTesting(socketIndex, "11007 Difference between any VIPER temp", (values.Max() - values.Min()), comareType, 0, 6, Unit, "");
            }
            catch (Exception)
            {

            }
        }
        public void Test_VIPER_Delta_Temp(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                int dataLen = 6;
                int baseStepNameNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseStepNameNum");

                string[] initialStepNames = { "Initial Viper A_U Temp", "Initial Viper A_L Temp", "Initial Viper B_U Temp", "Initial Viper B_L Temp", "Initial Viper C_U Temp", "Initial Viper C_L Temp" };
                string[] deltaStepNames = { "Delta Viper A_U Temp", "Delta Viper A_L Temp", "Delta Viper B_U Temp", "Delta Viper B_L Temp", "Delta Viper C_U Temp", "Delta Viper C_L Temp" };

                
                for (int i = 0; i < dataLen; i++)
                {
                    MySequenceManage.AddNumericTesting(socketIndex, $"{baseStepNameNum + 1 + i} {deltaStepNames[i]}", NewViperTemps[i] - OldViperTemps[i], comareType, lowLimit, highLimit, Unit, "");
                    OldViperTemps[i] = NewViperTemps[i];
                }
                MySequenceManage.AddNumericTesting(socketIndex, $"{baseStepNameNum + 1} Difference between any VIPER temp", NewViperTemps.Max() - NewViperTemps.Min(), comareType, 0, 15, Unit, "");
            }
            catch (Exception)
            {

            }
        }
        public void Test_VIPER_Initial_Temp(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                int baseStepNameNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseStepNameNum");

                string[] initialStepNames = { "Initial Viper A_U Temp", "Initial Viper A_L Temp", "Initial Viper B_U Temp", "Initial Viper B_L Temp", "Initial Viper C_U Temp", "Initial Viper C_L Temp" };
                string[] deltaStepNames = { "Delta Viper A_U Temp", "Delta Viper A_L Temp", "Delta Viper B_U Temp", "Delta Viper B_L Temp", "Delta Viper C_U Temp", "Delta Viper C_L Temp" };
                float[] values = null;
                DUT_ReadMultiFloat(0x2C, 6, out values);
                for (int i = 0; i < 6; i++)
                {
                    MySequenceManage.AddNumericTesting(socketIndex, $"{baseStepNameNum + 1 + i} {initialStepNames[i]}", values[i], comareType, lowLimit, highLimit, Unit, "");
                }
            }
            catch (Exception)
            {

            }
        }
        public void Test_ZeroRotation_ZeroTorque(int socketIndex)
        {
            try
            {
                int baseNum = 21000;
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                string[] stepNames = { "PhaseA_Min_Current", "PhaseB_Min_Current", "PhaseC_Min_Current", "PhaseA_Max_Current", "PhaseB_Max_Current", "PhaseC_Max_Current" };

                float[] tempValues = null;
                DUT_ReadMultiFloat(0x2C, 6, out tempValues);
                for (int i = 0; i < 6; i++)
                {
                    OldTemps_VIPER[i] = tempValues[i];
                    string stepName = $"{baseNum + i + 1} DUT {stepNames[i]}";
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, tempValues[i], comareType, 20, 70, Unit, "");
                }

                DUT_SetDUTCurrent(0.01f, 0.01f, 60, 100);

                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(6000);
                    double actVoltage = -999;
                    HVDC.GetActPower(out actVoltage);
                }

                DUT_ReadMultiFloat(0x2C, 6, out tempValues);
                baseNum = 22000;
                for (int i = 0; i < 6; i++)
                {
                    string stepName = $"{baseNum + i + 1} DUT {stepNames[i]}";
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, tempValues[i], comareType, 20, 80, Unit, "");
                }

                baseNum = 23000;
                for (int i = 0; i < 6; i++)
                {
                    string stepName = $"{baseNum + i + 1} DUT {stepNames[i]}";
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, tempValues[i]- OldTemps_VIPER[i], comareType, -1, 10, Unit, "");
                }


            }
            catch (Exception)
            {

            }
        }
        public void Test_UVW_Current_RMS(int socketIndex)
        {
            try
            {
                Thread.Sleep(1000);
                DUT_WriteByte(0x70, 4, 0x00);
                Thread.Sleep(100);
                DUT_WriteByte(0x70, 4, 0x01);

                //Wait Cycle time 
                for (int i = 0; i < 2000; i++)
                {
                    Thread.Sleep(100);
                    if ((DateTime.Now - StartCurrentDatetime).TotalSeconds >= 6)
                    {
                        break;
                    }
                }

                double dutCurrent = -999;
                HVDC.GetActCurrent(out dutCurrent);

                int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                string[] stepNames = { "PhaseA_Rms_Current", "PhaseB_Rms_Current", "PhaseC_Rms_Current" };

                //
                float[] currentValues = null;
                DUT_ReadMultiFloat(0x74, 9, out currentValues);
                currentValues = currentValues.Skip(3).ToArray();
                for (int i = 0; i < 3; i++)
                {
                    string stepName = $"{baseNum + i + 1} DUT {stepNames[i]} {SettingCurrent}A";

                    double rmsCurrent = (Math.Abs(currentValues[i]) + currentValues[i + 3]) / 2.828;
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, rmsCurrent, comareType, lowLimit, highLimit,  Unit, "");
                }



                //Read and check Motor Status of DUT
                byte[] motorStatus = new byte[9];
                DUT_ReadMultiByte(0x64, 9, out motorStatus);
                string resutlStr = "";
                foreach (byte status in motorStatus)
                {
                    resutlStr += $"{status:X2} ";
                }
                resutlStr = resutlStr.Trim();
                MySequenceManage.AddStringTesting(socketIndex, $"{baseNum + 8} Motor Status", resutlStr, "", "02 01 00 00 00 00 00 00 00", "");

                for (int i = 0; i < 3; i++)
                {
                    double aiValue = PCI6320.ReadValue($"Dev1/ai{i}", -10, 10, "");
                    double actCurrent = aiValue / 32.93f * 5000 * k[i] + b[i];
                    actCurrent = Math.Round(actCurrent, 1);

                    string stepName = $"{baseNum + i + 7} Device {stepNames[i]} {SettingCurrent}A";
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, Unit, "");
                }

                Thread.Sleep(1000);

                float[] values = null;
                DUT_ReadMultiFloat(0x2C, 6, out values);
                for (int i = 0; i < 6; i++)
                {
                    if (values.Length > i) NewViperTemps[i] = values[i];
                }

                for (int i = 0; i < 660; i++)
                {
                    Thread.Sleep(100);
                    DUT_ReadMultiByte(0x64, 9, out motorStatus);
                    if (motorStatus[0] != 2) break;
                }

                Thread.Sleep(5000);
                DUT_ReadMultiByte(0x64, 9, out motorStatus);
                resutlStr = "";
                foreach (byte status in motorStatus)
                {
                    resutlStr += $"{status:X2} ";
                }
                resutlStr = resutlStr.Trim();
                MySequenceManage.AddStringTesting(socketIndex, $"{baseNum + 8} Motor Status", resutlStr, "", "04 02 00 00 00 00 00 00 00", "");
            }
            catch (Exception)
            {

            }
        }
        public void Test_PassiveDischarge(int socketIndex)
        {
            try
            {
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");

                RelayHvMux.WriteDO("4,7", "0,0");
                double actVoltage = 0;
                double workMode = 1;
                DateTime startTime = DateTime.Now;

                MyCAN.DBC_SendSignalValue("s00_mcuEnable_1", 1, false);
                MyCAN.DBC_SendSignalValue("s08_ctrlMode_1", 3, true);
                Thread.Sleep(100);
                MyCAN.DBC_ReceiveSingal("s07_rapidDischg_1", ref workMode);

                MySequenceManage.AddNumericTesting(socketIndex, "010201 Work Mode", workMode, "GELE", 1, 1, "", "");
                
                List<double> dList = new List<double>();
                for (int i = 0; i < 130; i++)
                {
                    Thread.Sleep(1000);
                    actVoltage = DMM.GetMeasureValue();
                    
                    MySequenceManage.AddNumericTesting(socketIndex, $"{180001 + i} Current Discharge Voltage_{i}", actVoltage, "GELE", -1, 800, "V", "");

                    if (actVoltage < 60) break;
                }
                RelayHvMux.WriteDO("4,7", "1,1");
                MySequenceManage.AddNumericTesting(socketIndex, stepName, (DateTime.Now - startTime).TotalMinutes, "GELE", 0, 2, "min", "");
            }
            catch (Exception)
            {

            }
        }
        public void Test_DelayMs(int socketIndex)
        {
            try
            {
                int timeMs = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "TimeMs");    //20000
                Thread.Sleep(timeMs);
            }
            catch (Exception)
            {

            }
        }
        public void Test_WakeupCurrentByCAN(int socketIndex)
        {
            try
            {
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");

                Task t = Task.Run(() =>
                {
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(60);
                        MyCAN.SendMessage(0x50F, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
                    }
                });
                
                Thread.Sleep(600);
                double value = DMM.GetMeasureValue() * 1f;
                MySequenceManage.AddNumericTesting(socketIndex, stepName, value, comareType, lowLimit, highLimit, Unit, "");
                Thread.Sleep(600);
            }
            catch (Exception)
            {

            }
        }
        public void Test_GetTrayNumber(int socketIndex)
        {
            string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");

            string trayNumber = PLC_ReadString(314, 32);
            trayNumber = trayNumber.Substring(2);
            trayNumber = trayNumber.Replace("\0", "");

            MySequenceManage.AddStringTesting(socketIndex, stepName, trayNumber, "", trayNumber, "");
        }

        public void Test_GetWaterTemp(int socketIndex)
        {
            try
            {
                double demoValue = 0;

                //int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");

                //
                byte[] rxBytes = null;
                MyPLC.DBRead(101, 574, 4, out rxBytes);
                demoValue = BitConverter.ToSingle(rxBytes.Reverse().ToArray(), 0);
                demoValue = Math.Round(demoValue, 1);

                MySequenceManage.AddNumericTesting(socketIndex, stepName, demoValue, comareType, lowLimit, highLimit, Unit, "");

                if (demoValue < lowLimit || demoValue > highLimit)
                {
                    MySequenceManage.GotoByStepName(0, "Set HVDC Voltage 0V PostUUT");
                }
            }
            catch (Exception)
            {

            }
        }
        public void Test_GetWaterFlow(int socketIndex)
        {
            try
            {
                double demoValue = 0;

                //int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");

                //
                int dbNumber = 101; int byteOffset = 578;
                try { dbNumber = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "DbNumber"); } catch { }
                try { byteOffset = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "ByteOffset"); } catch { }
                if (dbNumber <= 0) dbNumber = 101; if (byteOffset < 0) byteOffset = 578;
                byte[] rxBytes = null;
                MyPLC.DBRead(dbNumber, byteOffset, 4, out rxBytes);
                demoValue = BitConverter.ToSingle(rxBytes.Reverse().ToArray(), 0);
                demoValue = Math.Round(demoValue, 1);

                MySequenceManage.AddNumericTesting(socketIndex, stepName, demoValue, comareType, lowLimit, highLimit, Unit, "");

                if (demoValue < lowLimit)
                {
                    MySequenceManage.GotoByStepName(0, "Set HVDC Voltage 0V PostUUT");
                }
            }
            catch (Exception)
            {

            }
        }
        private Random DemoRandom = new Random();
        public void Test_GetDemoValue(int socketIndex)
        {
            try
            {
                double demoValue = 0;

                //int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                string stepName = MySequenceManage.GetInputStringValue(socketIndex, "StepName");
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");

                //
                Thread.Sleep(100);
                if (lowLimit == highLimit)
                {
                    demoValue = lowLimit;
                }
                else
                {
                    double diff = highLimit - lowLimit;
                    double min = lowLimit + diff * 0.4;
                    double max = lowLimit + diff * 0.6;
                    demoValue = DemoRandom.NextDouble() * (max - min) + min;
                    demoValue = Math.Round(demoValue, 8 - ((int)demoValue).ToString().Length);
                }

                MySequenceManage.AddNumericTesting(socketIndex, stepName, demoValue, comareType, lowLimit, highLimit, Unit, "");
            }
            catch (Exception)
            {

            }
        }
        public void Test_UVW_Current_New(int socketIndex)
        {
            try
            {
                Thread.Sleep(1000);
                DUT_WriteByte(0x70, 4, 0x00);
                Thread.Sleep(100);
                DUT_WriteByte(0x70, 4, 0x01);

                //Wait Cycle time 
                for (int i = 0; i < 2000; i++)
                {
                    Thread.Sleep(100);
                    if ((DateTime.Now - StartCurrentDatetime).TotalSeconds >= 6)
                    {
                        break;
                    }
                }

                double dutCurrent = -999;
                HVDC.GetActCurrent(out dutCurrent);

                int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                string[] stepNames = { "PhaseA_Min_Current", "PhaseB_Min_Current", "PhaseC_Min_Current", "PhaseA_Max_Current", "PhaseB_Max_Current", "PhaseC_Max_Current", "PhaseA_Rms_Current", "PhaseB_Rms_Current", "PhaseC_Rms_Current" };

                //
                float[] currentValues = null;
                DUT_ReadMultiFloat(0x74, 9, out currentValues);
                currentValues = currentValues.Skip(3).ToArray();
                for (int i = 0; i < 6; i++)
                {
                    string stepName = $"{baseNum + i + 1} DUT {stepNames[i]} {SettingCurrent}A";
                    if (i > 2)
                    {
                        MySequenceManage.AddNumericTesting(socketIndex, stepName, currentValues[i], comareType, lowLimit * 1.414, highLimit * 1.414, Unit, "");
                    }
                    else
                    {
                        MySequenceManage.AddNumericTesting(socketIndex, stepName, currentValues[i], comareType, highLimit * -1.414, lowLimit * -1.414, Unit, "");
                    }

                    currentValues[i] = Math.Abs(currentValues[i]);
                }

                //MySequenceManage.AddNumericTesting(socketIndex, $"{baseNum + 7} Difference between any Phase Current", (currentValues.Max() - currentValues.Min()), comareType, 0, highLimit - lowLimit, Unit, "");

                //Read and check Motor Status of DUT
                byte[] motorStatus = new byte[9];
                DUT_ReadMultiByte(0x64, 9, out motorStatus);
                string resutlStr = "";
                foreach (byte status in motorStatus)
                {
                    resutlStr += $"{status:X2} ";
                }
                resutlStr = resutlStr.Trim();
                MySequenceManage.AddStringTesting(socketIndex, $"{baseNum + 8} Motor Status", resutlStr, "", "02 01 00 00 00 00 00 00 00", "");

                for (int i = 0; i < 3; i++)
                {
                    double aiValue = PCI6320.ReadValue($"Dev1/ai{i}", -10, 10, "");
                    double actCurrent = aiValue / 32.33f * 5000 * k[i] + b[i];
                    actCurrent = Math.Round(actCurrent, 1);

                    string stepName = $"{baseNum + i + 7} Device {stepNames[i + 6]} {SettingCurrent}A";
                    MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, Unit, "");
                }

                for (int i = 0; i < 660; i++)
                {
                    Thread.Sleep(100);
                    DUT_ReadMultiByte(0x64, 9, out motorStatus);
                    if (motorStatus[0] != 2) break;
                }
            }
            catch (Exception)
            {

            }
        }
        [Obsolete]
        public void Test_UVW_Current(int socketIndex)
        {
            try
            {
                Thread.Sleep(1000);
                DUT_WriteByte(0x70, 4, 0x00);
                Thread.Sleep(100);
                DUT_WriteByte(0x70, 4, 0x01);

                //Wait Cycle time 
                for (int i = 0; i < 2000; i++)
                {
                    Thread.Sleep(100);
                    if ((DateTime.Now - StartCurrentDatetime).TotalSeconds >= 6)
                    {
                        break;
                    }
                }

                double actCurrent = -999;
                HVDC.GetActCurrent(out actCurrent);

                int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                string[] stepNames = { "PhaseA_Min_Current", "PhaseB_Min_Current", "PhaseC_Min_Current", "PhaseA_Max_Current", "PhaseB_Max_Current", "PhaseC_Max_Current" };

                //
                float[] currentValues = null;
                DUT_ReadMultiFloat(0x74, 9, out currentValues);
                currentValues = currentValues.Skip(3).ToArray();
                for (int i = 0; i < 6; i++)
                {
                    string stepName = $"{baseNum + i + 1} DUT {stepNames[i]} {SettingCurrent}A";
                    if (i > 2)
                    {
                        MySequenceManage.AddNumericTesting(socketIndex, stepName, currentValues[i], comareType, lowLimit * 1.414, highLimit * 1.414, Unit, "");
                    }
                    else
                    {
                        MySequenceManage.AddNumericTesting(socketIndex, stepName, currentValues[i], comareType, highLimit * -1.414, lowLimit * -1.414,  Unit, "");
                    }

                    currentValues[i] = Math.Abs(currentValues[i]);
                }

                MySequenceManage.AddNumericTesting(socketIndex, $"{baseNum + 7} Difference between any Phase Current", (currentValues.Max() - currentValues.Min()), comareType, 0, highLimit - lowLimit, Unit, "");

                //Read and check Motor Status of DUT
                byte[] motorStatus = new byte[9];
                DUT_ReadMultiByte(0x64, 9, out motorStatus);
                string resutlStr = "";
                foreach (byte status in motorStatus)
                {
                    resutlStr += $"{status:X2} ";
                }
                resutlStr = resutlStr.Trim();
                MySequenceManage.AddStringTesting(socketIndex, $"{baseNum + 8} Motor Status", resutlStr, "", "02 01 00 00 00 00 00 00 00", "");

                //for (int i = 0; i < 3; i++)
                //{
                //    double aiValue = PCI6320.ReadValue($"Dev1/ai{i}", -10, 10, "");
                //    double actCurrent = aiValue / 68f * 1500 * k[i] + b[i];

                //    string stepName = $"{baseNum + i + 11} Device {stepNames[i]} {SettingCurrent}A";
                //    MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, Unit, "");
                //}
            }
            catch (Exception)
            {

            }
        }
        [Obsolete]
        public void Test_UVW_Current_Continue(int socketIndex)
        {
            try
            {
                int baseNum = (int)MySequenceManage.GetInputDoubleValue(socketIndex, "BaseNum");    //20000
                double lowLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "LowLimit");
                double highLimit = MySequenceManage.GetInputDoubleValue(socketIndex, "HighLimit");
                string comareType = MySequenceManage.GetInputStringValue(socketIndex, "Comtype");
                string Unit = MySequenceManage.GetInputStringValue(socketIndex, "Unit");
                string[] stepNames = { "PhaseA_Min_Current", "PhaseB_Min_Current", "PhaseC_Min_Current", "PhaseA_Max_Current", "PhaseB_Max_Current", "PhaseC_Max_Current" };

                for (int times = 0; times < 26; times++)
                {
                    Thread.Sleep(5000);
                    if ((SettingCycleTime - (DateTime.Now - StartCurrentDatetime).TotalSeconds) < 5)
                    {
                        break;
                    }

                    //Read and check current of DUT
                    float[] currentValues = new float[9];
                    DUT_ReadMultiFloat(0x74, 9, out currentValues);
                    currentValues = currentValues.Skip(3).ToArray();
                    for (int i = 0; i < 6; i++)
                    {
                        string stepName = $"{baseNum + i + 1} DUT {stepNames[i]} {SettingCurrent}A";
                        if (i > 2)
                        {
                            MySequenceManage.AddNumericTesting(socketIndex, stepName, currentValues[i], comareType, lowLimit * 1.414, highLimit * 1.414, Unit, "");
                        }
                        else
                        {
                            MySequenceManage.AddNumericTesting(socketIndex, stepName, currentValues[i], comareType, highLimit * -1.414, lowLimit * -1.414, Unit, "");
                        }
                    }

                    //Read and check Motor Status of DUT
                    byte[] motorStatus = new byte[9];
                    DUT_ReadMultiByte(0x64, 9, out motorStatus);
                    string resutlStr = "";
                    foreach (byte status in motorStatus)
                    {
                        resutlStr += $"{status:X2} ";
                    }
                    resutlStr = resutlStr.Trim();
                    MySequenceManage.AddStringTesting(socketIndex, "Motor Status", resutlStr, "", "02 01 00 00 00 00 00 00", "");

                    for (int i = 0; i < 3; i++)
                    {
                        double aiValue = PCI6320.ReadValue($"Dev1/ai{i}", -10, 10, "");
                        double actCurrent = aiValue / 68f * 1500 * k[i] + b[i];

                        string stepName = $"{baseNum + i + 11} Device {stepNames[i]} {SettingCurrent}A";
                        MySequenceManage.AddNumericTesting(socketIndex, stepName, actCurrent, comareType, lowLimit, highLimit, Unit, "");
                    }
                }
            }
            catch (Exception)
            {

            }
        }
        #endregion

    }
}
