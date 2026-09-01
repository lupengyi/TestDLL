using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ManualCanDebug.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    internal static class FctStudioProjectService
    {
        public static FctStudioProject Load(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        public static FctStudioProject Deserialize(string json)
        {
            FctStudioProject project = JsonConvert.DeserializeObject<FctStudioProject>(json);
            if (project == null) throw new FormatException("FCT Studio project is empty.");
            NormalizeProject(project);
            return project;
        }

        public static string Serialize(FctStudioProject project) { return JsonConvert.SerializeObject(project, Formatting.Indented); }

        public static void Save(string path, FctStudioProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, Serialize(project));
        }

        public static string EditorStatePath(string sequencePath)
        {
            if (string.IsNullOrWhiteSpace(sequencePath)) throw new ArgumentException("SEQ path is required.", nameof(sequencePath));
            return sequencePath + ".fctstudio.json";
        }

        public static void SaveEditorState(string sequencePath, FctStudioProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (!File.Exists(sequencePath)) throw new FileNotFoundException("SEQ file does not exist.", sequencePath);
            FctStudioEditorState state = new FctStudioEditorState { SequenceSha256 = ComputeSha256(sequencePath), Project = project };
            string path = EditorStatePath(sequencePath), directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.Indented), new UTF8Encoding(false));
        }

        public static bool TryLoadEditorState(string sequencePath, out FctStudioProject project)
        {
            project = null; string path = EditorStatePath(sequencePath);
            if (!File.Exists(sequencePath) || !File.Exists(path)) return false;
            try
            {
                FctStudioEditorState state = JsonConvert.DeserializeObject<FctStudioEditorState>(File.ReadAllText(path));
                if (state == null || state.Project == null || string.IsNullOrWhiteSpace(state.SequenceSha256) || !string.Equals(state.SequenceSha256, ComputeSha256(sequencePath), StringComparison.OrdinalIgnoreCase)) return false;
                NormalizeProject(state.Project, false); project = state.Project; return true;
            }
            catch { return false; }
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        public static FctStudioProject CreateDefault(SequenceDocument sequence, string product)
        {
            FctStudioProject project = CreateBlank(sequence, product);
            AddBlock(project, "低压上电", "电源", sequence.Steps.Where(step => step.FunctionName == "LVDC_SetSourceVoltage" || step.FunctionName == "LVDC_SetSourceCurrent" || step.FunctionName == "LVDC_SetOutput").Take(3));
            FunctionBlockDefinition highVoltage = AddBlock(project, "高压上电", "电源", sequence.Steps.Where(step => step.FunctionName == "HVDC_SetSourceVoltage" || step.FunctionName == "HVDC_SetSourceCurrent" || step.FunctionName == "HVDC_SetOutput").Take(3));
            highVoltage.SupportedProducts = new List<string> { "C91" };
            Expose(highVoltage, "Voltage", "HighVoltage", "目标高压", "Number", 600.0, "V");
            FunctionBlockDefinition resistance = AddBlock(project, "电阻模拟器设置", "RES", sequence.Steps.Where(step => step.FunctionName == "RES_SetResistance").Take(1));
            Expose(resistance, "ResValue", "Resistance", "目标电阻", "Number", 1000.0, "Ohm");
            AddBlock(project, "DMM测量", "DMM", sequence.Steps.Where(step => step.FunctionName.StartsWith("DMM_Config", StringComparison.Ordinal) || step.FunctionName == "DMM_GetMeasureValue").Take(2));
            FunctionBlockDefinition daq = AddBlock(project, "DAQ电流读取", "DAQ", sequence.Steps.Where(step => step.FunctionName == "DAQ_ReadCurrent").Take(1));
            if (daq.Steps.Count == 0) { SequenceStepDefinition template = GenericStepCatalog.CreateTemplates().First(step => step.FunctionName == "FCT_ExecuteAction" && Convert.ToString(step.Get("Device")) == "DAQ"); daq.Steps.Add(FromStep(template)); }
            AddBlock(project, "MOXA输出控制", "IO", sequence.Steps.Where(step => step.FunctionName == "MOXA_SetDO").Take(1));
            AddBlock(project, "继电器控制", "IO", sequence.Steps.Where(step => step.FunctionName == "Relay_SetDO").Take(1));
            AddBlock(project, "PLC完成信号", "PLC", sequence.Steps.Where(step => step.FunctionName == "PLC_LoadFinished").Take(1));
            FunctionBlockDefinition legacyCommunication = AddBlock(project, "产品通信初始化", "产品", sequence.Steps.Where(step => step.FunctionName == "CAN_APP2FT" || step.FunctionName == "DUT_ComucationInit" || step.FunctionName == "Test_CANCommunication").Take(3)); legacyCommunication.SupportedProducts = new List<string> { "C91" };
            project.Blocks.Add(CreateC96CommunicationBlock());
            project.Blocks.Add(CreateSafeHighVoltageBlock());
            FunctionBlockDefinition resolver = AddBlock(project, "旋变转速设置", "旋变", sequence.Steps.Where(step => step.FunctionName == "Resolver_SetSpeed").Take(1));
            Expose(resolver, "Speed", "Speed", "目标转速", "Number", 700.0, "rpm");
            FunctionBlockDefinition current = AddBlock(project, "三相出流", "主驱", sequence.Steps.Where(step => step.FunctionName == "CAN_SetDUTCurrent" || step.FunctionName == "Test_UVW_Current_RMS" || step.FunctionName == "CAN_ReadDutCurrent").Take(2));
            current.Name = "C91三相出流"; current.SupportedProducts = new List<string> { "C91" };
            Expose(current, "MaxCurrent", "TargetCurrent", "目标电流", "Number", 100.0, "A RMS");
            Expose(current, "Frequency", "Frequency", "输出频率", "Number", 60.0, "Hz");
            Expose(current, "HoldTime", "HoldTime", "保持时间", "Number", 10.0, "s");
            project.Blocks.Add(CreateCurrentTableBlock("C95三相出流", new[] { "C95" }, 0x58));
            project.Blocks.Add(CreateCurrentTableBlock("C92/C96 TM1三相出流", new[] { "C92", "C96" }, 0x68));
            project.Blocks.Add(CreateCurrentTableBlock("C92/C96 TM2三相出流", new[] { "C92", "C96" }, 0x80));
            project.Blocks.Add(CreateCurrentStopBlock("C92/C96 TM1安全停机", new[] { "C92", "C96" }, 0x68));
            project.Blocks.Add(CreateCurrentStopBlock("C92/C96 TM2安全停机", new[] { "C92", "C96" }, 0x80));
            project.Blocks.Add(CreateDriveFeedbackBlock("C92/C96 TM1状态读取", new[] { "C92", "C96" }, 0x44, 9, 0x6C, 10, 0x7C, 40, 0x98, 16));
            project.Blocks.Add(CreateDriveFeedbackBlock("C92/C96 TM2状态读取", new[] { "C92", "C96" }, 0x48, 9, 0x84, 10, 0x94, 40, 0xA8, 16));
            project.Blocks.Add(CreateAuxBlock("DCDC启动", "VCU1_DCDC_OilPump_Cmd", "{\"DCDC_Start_Cmd\":85}"));
            project.Blocks.Add(CreateAuxBlock("油泵运行", "VCU1_DCDC_OilPump_Cmd", "{\"DCAC_Steer_Start_Cmd\":85,\"DCAC_Steer_RatedCurr\":${RatedCurrent},\"DCAC_Steer_FreqCmd\":${Frequency},\"DCAC_Steer_VF_Voltage\":${VfVoltage}}"));
            project.Blocks.Add(CreateAuxBlock("气泵运行", "VCU2_AirPump_Cmd", "{\"DCAC_Air_Start_Cmd\":85,\"DCAC_Air_RatedCurr\":${RatedCurrent},\"DCAC_Air_FreqCmd\":${Frequency},\"DCAC_Air_VF_Voltage\":${VfVoltage}}"));
            project.Blocks.Add(CreateAuxFeedbackBlock("DCDC反馈读取", new[] { "DCDC_OutVoltage", "DCDC_OutCurrent", "DCDC_InVoltage", "DCDC_HeatSinkTemp", "DCDC_FaultCode", "DCDC_SwVer1", "DCDC_SwVer2", "DCDC_SwVer3", "DCDC_DebugYear", "DCDC_DebugMonth", "DCDC_DebugDay", "DCDC_DebugVer" }));
            project.Blocks.Add(CreateAuxFeedbackBlock("油泵反馈读取", new[] { "OilPump_SwModelL", "OilPump_SwModelH", "OilPump_SwVer", "OilPump_DebugYear", "OilPump_DebugMonth", "OilPump_DebugDay", "OilPump_DebugVer", "OilPump_ModuleTemp", "OilPump_RatedPower", "OilPump_RatedCurr", "OilPump_OutFreq", "OilPump_InVoltage", "OilPump_OutVoltage", "OilPump_OutCurrent", "OilPump_CtrlMode", "OilPump_InternalFault", "OilPump_FaultCode" }));
            project.Blocks.Add(CreateAuxFeedbackBlock("气泵反馈读取", new[] { "AirPump_SwModelL", "AirPump_SwModelH", "AirPump_SwVer", "AirPump_DebugYear", "AirPump_DebugMonth", "AirPump_DebugDay", "AirPump_DebugVer", "AirPump_ModuleTemp", "AirPump_RatedPower", "AirPump_RatedCurr", "AirPump_OutFreq", "AirPump_InVoltage", "AirPump_OutVoltage", "AirPump_OutCurrent", "AirPump_CtrlMode", "AirPump_InternalFault", "AirPump_FaultCode" }));
            project.Blocks.Add(CreateAuxFeedbackBlock("PDU与接触器反馈读取", new[] { "PowerIniDelay", "InitOver", "Init_FlagM0MPSuccess", "Init_FlagL0LPSuccess", "Init_FlagF0FPSuccess", "Init_FlagX1Success", "Init_FlagX2Success", "Init_FlagX3Success", "K1_Volt1", "K2_Volt2", "K3_Volt3", "FotP_Volt", "LotP_Volt", "MotP_Volt", "BUSVoltage", "PDU_ShrRlySts", "PDU_G2RlySts", "PDU_G3RlySts", "PDU_G4RlySts", "PDU_PosRlySts", "PDU_PosRlyPcgRlySts", "PDU_BodyworkRlySts", "PDU_BodyworkRlyPcgRlySts" }));
            project.Blocks.Add(CreateAuxBlock("DCDC停止", "VCU1_DCDC_OilPump_Cmd", "{\"DCDC_Start_Cmd\":170}"));
            project.Blocks.Add(CreateAuxBlock("油泵停止", "VCU1_DCDC_OilPump_Cmd", "{\"DCAC_Steer_Start_Cmd\":170}"));
            project.Blocks.Add(CreateAuxBlock("气泵停止", "VCU2_AirPump_Cmd", "{\"DCAC_Air_Start_Cmd\":170}"));
            project.Blocks.Add(CreateAuxBlock("PDU主驱高压闭合", "VCU_PDU", "{\"VCU_HghVtgCnt\":1}"));
            project.Blocks.Add(CreateAuxBlock("PDU主驱高压断开", "VCU_PDU", "{\"VCU_HghVtgCnt\":0}"));
            FunctionBlockDefinition delay = new FunctionBlockDefinition { Name = "延时", Category = "逻辑", Description = "固定延时" };
            delay.Parameters.Add(new BlockParameterDefinition { Name = "TimeMs", DisplayName = "延时时间", Type = "Integer", DefaultValue = 1000, Unit = "ms", Required = true });
            BlockStepDefinition delayStep = FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Delay" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RecordingLog", true }, { "Operation", "Delay" }, { "TimeMs", 1000 } }));
            delayStep.ParameterBindings["TimeMs"] = "TimeMs"; delay.Steps.Add(delayStep); project.Blocks.Add(delay);
            FunctionBlockDefinition shutdown = new FunctionBlockDefinition { Name = "安全下电", Category = "安全", Description = "关闭高低压、旋变、MOXA和继电器" };
            shutdown.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Safe shutdown" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RecordingLog", true }, { "Operation", "SafeShutdown" } })));
            project.Blocks.Add(shutdown);
            foreach (FunctionBlockDefinition block in project.Blocks) { block.ModuleKind = block.Category == "主驱" || block.Category == "DCDC/辅驱" || block.Category == "产品" ? "Product" : "Standard"; block.IsStandard = block.ModuleKind != "Custom"; }
            // Keep module parameters and per-flow overrides editable in Studio.
            // FctStudioCompiler resolves them to ordinary platform JSON values during export.
            return project;
        }

        public static FctStudioProject CreateBlank(SequenceDocument sequence, string product)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            return new FctStudioProject
            {
                ProjectName = "FCT Function Block Project",
                Product = product,
                ProductLocatorPath = "Config\\ProductLocators\\" + (string.Equals(product, "C92", StringComparison.OrdinalIgnoreCase) ? "C96" : product) + "_Locator.xlsx",
                AuxiliaryDbcPath = string.Equals(product, "C95", StringComparison.OrdinalIgnoreCase) || string.Equals(product, "C96", StringComparison.OrdinalIgnoreCase) ? "Config\\C95C96Auxiliary.dbc" : string.Empty,
                DriveStructure = string.Equals(product, "C92", StringComparison.OrdinalIgnoreCase) || string.Equals(product, "C96", StringComparison.OrdinalIgnoreCase) ? "DualMainDrive" : "SingleMainDrive",
                Capabilities = DefaultCapabilities(product),
                SequenceRoot = new Dictionary<string, object>(sequence.RootProperties, StringComparer.Ordinal)
            };
        }

        public static int MergeMissingStandardBlocks(FctStudioProject project, SequenceDocument sequence)
        {
            FctStudioProject defaults = CreateDefault(sequence, project.Product);
            int added = 0;
            foreach (FunctionBlockDefinition block in defaults.Blocks)
            {
                FunctionBlockDefinition existing = project.Blocks.FirstOrDefault(item => string.Equals(item.Name, block.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) continue;
                project.Blocks.Add(block); added++;
            }
            return added;
        }

        private static FunctionBlockDefinition AddBlock(FctStudioProject project, string name, string category, IEnumerable<SequenceStepDefinition> steps)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = name, Category = category };
            foreach (SequenceStepDefinition step in steps) block.Steps.Add(FromStep(step));
            project.Blocks.Add(block); return block;
        }

        private static FunctionBlockDefinition CreateAuxBlock(string name, string messageName, string signalsJson)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = name, Category = "DCDC/辅驱", SupportedProducts = new List<string> { "C95", "C96" }, Description = "通过辅助CAN DBC发送" };
            block.Parameters.Add(new BlockParameterDefinition { Name = "SendCount", DisplayName = "发送次数", Type = "Integer", DefaultValue = 10, Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "PeriodMs", DisplayName = "发送周期", Type = "Integer", DefaultValue = 100, Unit = "ms", Required = true });
            if (signalsJson.Contains("${RatedCurrent}")) block.Parameters.Add(new BlockParameterDefinition { Name = "RatedCurrent", DisplayName = "额定电流", Type = "Number", DefaultValue = 17.0, Unit = "A", Required = true });
            if (signalsJson.Contains("${Frequency}")) block.Parameters.Add(new BlockParameterDefinition { Name = "Frequency", DisplayName = "输出频率", Type = "Number", DefaultValue = 50.0, Unit = "Hz", Required = true });
            if (signalsJson.Contains("${VfVoltage}")) block.Parameters.Add(new BlockParameterDefinition { Name = "VfVoltage", DisplayName = "VF电压百分比", Type = "Number", DefaultValue = 50.0, Unit = "%", Required = true });
            string sendStepName = name + " DBC发送";
            block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object>
            {
                { "StepName", sendStepName }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true },
                { "Device", "AUXCAN" }, { "Operation", "SendDbcSignals" }, { "MessageName", messageName }, { "SignalsJson", signalsJson }, { "ResultMode", "Action" }
            })));
            BlockStepDefinition delay = FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", name + " 周期延时" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RecordingLog", true }, { "Operation", "Delay" }, { "TimeMs", 100 } })); delay.ParameterBindings["TimeMs"] = "PeriodMs"; block.Steps.Add(delay);
            BlockStepDefinition loop = FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", name + " 周期循环" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RecordingLog", true }, { "Operation", "FixedLoop" }, { "LoopId", name + "Loop" }, { "Count", 10 }, { "TargetStepName", sendStepName } })); loop.ParameterBindings["Count"] = "SendCount"; block.Steps.Add(loop);
            return block;
        }

        private static FunctionBlockDefinition CreateCurrentTableBlock(string name, IEnumerable<string> products, int addressOffset)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = name, Category = "主驱", SupportedProducts = products.ToList(), Description = "按Locator Motor Control整表写入；RMS自动换算Peak，NewData Flag最后写入" };
            if (false && addressOffset == 0x80)
            {
                string tm2Changes = "[" +
                    "{\"Offset\":0,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                    "{\"Offset\":4,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"100*1.414\",\"WriteLast\":false}," +
                    "{\"Offset\":8,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"20\",\"WriteLast\":false}," +
                    "{\"Offset\":12,\"DataSize\":2,\"DataType\":\"uint16\",\"Endian\":\"Little\",\"Value\":\"50\",\"WriteLast\":false}," +
                    "{\"Offset\":14,\"DataSize\":2,\"DataType\":\"uint16\",\"Endian\":\"Little\",\"Value\":\"10\",\"WriteLast\":false}," +
                    "{\"Offset\":16,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"60\",\"WriteLast\":false}," +
                    "{\"Offset\":20,\"DataSize\":2,\"DataType\":\"int16\",\"Endian\":\"Little\",\"Value\":\"4\",\"WriteLast\":false}," +
                    "{\"Offset\":22,\"DataSize\":2,\"DataType\":\"uint16\",\"Endian\":\"Little\",\"Value\":\"10000\",\"WriteLast\":false}," +
                    "{\"Offset\":24,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"1\",\"WriteLast\":false}," +
                    "{\"Name\":\"New Data Flag\",\"Offset\":25,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"255\",\"WriteLast\":true,\"WriteFinal\":true}," +
                    "{\"Offset\":26,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                    "{\"Offset\":30,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}]";
                block.Description = "按C96/C92 TM2 Locator的31字节Motor Control表写入；目标RMS自动换算Peak。"; block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", name + " 控制表写入" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true }, { "Operation", "Write" }, { "ResultMode", "Action" }, { "AddrOffset", addressOffset }, { "TableLength", 31 }, { "ChangesJson", tm2Changes }, { "VerifyAfterWrite", true } }))); return block;
            }
            string changes = "[" +
                "{\"Offset\":0,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":4,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"100*1.414\",\"WriteLast\":false}," +
                "{\"Offset\":8,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"20\",\"WriteLast\":false}," +
                "{\"Offset\":12,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"10\",\"WriteLast\":false}," +
                "{\"Offset\":16,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"60\",\"WriteLast\":false}," +
                "{\"Offset\":20,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"4\",\"WriteLast\":false}," +
                "{\"Offset\":21,\"DataSize\":1,\"DataType\":\"int8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":22,\"DataSize\":2,\"DataType\":\"uint16\",\"Endian\":\"Little\",\"Value\":\"50\",\"WriteLast\":false}," +
                "{\"Offset\":24,\"DataSize\":2,\"DataType\":\"uint16\",\"Endian\":\"Little\",\"Value\":\"10000\",\"WriteLast\":false}," +
                "{\"Offset\":26,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"1\",\"WriteLast\":false}," +
                "{\"Name\":\"New Data Flag\",\"Offset\":27,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"255\",\"WriteLast\":true,\"WriteFinal\":true}," +
                "{\"Offset\":28,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":29,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":30,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":34,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":35,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}]";
            block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", name + " 控制表写入" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true }, { "Operation", "Write" }, { "ResultMode", "Action" }, { "AddrOffset", addressOffset }, { "TableLength", 39 }, { "ChangesJson", changes }, { "VerifyAfterWrite", true } })));
            return block;
        }

        private static FunctionBlockDefinition CreateC96CommunicationBlock()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "C96产品通信初始化", Category = "产品", SupportedProducts = new List<string> { "C96" }, Description = "C96直接执行DUT通信初始化和CAN通信检查；不发送C91专用的APP转FT UDS序列。" };
            block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "C96 DUT Communication Init" }, { "RunMode", "Normal" }, { "FunctionName", "DUT_ComucationInit" }, { "RecordingLog", true }, { "TxID", "0x7EE" }, { "RxID", "0x7EF" } })));
            block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "C96 CAN Communication" }, { "RunMode", "Normal" }, { "FunctionName", "Test_CANCommunication" }, { "RecordingLog", true } })));
            return block;
        }

        private static FunctionBlockDefinition CreateSafeHighVoltageBlock()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "高压电源安全设置", Category = "电源", Description = "设置高压电源限流、电压和输出状态；默认0V且关闭输出。" };
            block.Parameters.Add(new BlockParameterDefinition { Name = "HighVoltage", DisplayName = "目标高压", Type = "Number", DefaultValue = 0.0, Unit = "V", Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "CurrentLimit", DisplayName = "电流限制", Type = "Number", DefaultValue = 20.0, Unit = "A", Required = true });
            block.Parameters.Add(new BlockParameterDefinition { Name = "OutputEnabled", DisplayName = "输出使能", Type = "Boolean", DefaultValue = false, Required = true });
            BlockStepDefinition current = FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Set HVDC Current Limit" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", "HVDC" }, { "Operation", "SetCurrent" }, { "Current", 20.0 }, { "ResultMode", "Action" } })); current.ParameterBindings["Current"] = "CurrentLimit"; block.Steps.Add(current);
            BlockStepDefinition voltage = FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Set HVDC Voltage" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", "HVDC" }, { "Operation", "SetVoltage" }, { "Voltage", 0.0 }, { "ResultMode", "Action" } })); voltage.ParameterBindings["Voltage"] = "HighVoltage"; block.Steps.Add(voltage);
            BlockStepDefinition output = FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Set HVDC Output" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", "HVDC" }, { "Operation", "SetOutput" }, { "Output", false }, { "ResultMode", "Action" } })); output.ParameterBindings["Output"] = "OutputEnabled"; block.Steps.Add(output);
            return block;
        }

        private static FunctionBlockDefinition CreateCurrentStopBlock(string name, IEnumerable<string> products, int addressOffset)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = name, Category = "主驱", SupportedProducts = products.ToList(), Description = "目标电流归零，并关闭Gate、速度和电压使能；NewData最后写入。" };
            string changes = "[" +
                "{\"Offset\":0,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":4,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":8,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":12,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":16,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":20,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":26,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":28,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":29,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":30,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":34,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":35,\"DataSize\":4,\"DataType\":\"float32\",\"Endian\":\"Little\",\"Value\":\"0\",\"WriteLast\":false}," +
                "{\"Offset\":27,\"DataSize\":1,\"DataType\":\"uint8\",\"Endian\":\"Little\",\"Value\":\"255\",\"WriteLast\":true}]";
            block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", name + " 控制表写入" }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true }, { "Operation", "Write" }, { "ResultMode", "Action" }, { "AddrOffset", addressOffset }, { "TableLength", 39 }, { "ChangesJson", changes }, { "VerifyAfterWrite", true } })));
            return block;
        }

        private static FunctionBlockDefinition CreateDriveFeedbackBlock(string name, IEnumerable<string> products, int resolverOffset, int resolverLength, int statusOffset, int statusLength, int currentOffset, int currentLength, int rpmOffset, int rpmLength)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = name, Category = "主驱", SupportedProducts = products.ToList(), Description = "读取旋变、Motor Status、三相电流和RPM原始表；具体信号值可再从Locator选择加入。" };
            AddTableRead(block, name + " / Resolver", resolverOffset, resolverLength);
            AddTableRead(block, name + " / Motor Status", statusOffset, statusLength);
            AddTableRead(block, name + " / Current", currentOffset, currentLength);
            AddTableRead(block, name + " / RPM", rpmOffset, rpmLength);
            return block;
        }

        private static void AddTableRead(FunctionBlockDefinition block, string stepName, int addressOffset, int length)
        {
            block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", stepName }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true }, { "Operation", "Read" }, { "ResultMode", "Information" }, { "AddrOffset", addressOffset }, { "TableLength", length } })));
        }

        private static FunctionBlockDefinition CreateAuxFeedbackBlock(string name, IEnumerable<string> signals)
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = name, Category = "DCDC/辅驱", SupportedProducts = new List<string> { "C95", "C96" }, Description = "按辅助DBC逐项读取反馈；规格未给Limit时只记录实际值。" };
            foreach (string signal in signals)
            {
                block.Steps.Add(FromStep(new SequenceStepDefinition(new Dictionary<string, object>
                {
                    { "StepName", name + " / " + signal }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true },
                    { "Device", "AUXCAN" }, { "Operation", "ReadDbcSignal" }, { "ResultMode", "Information" }, { "DbcPath", "Config\\C95C96Auxiliary.dbc" },
                    { "DeviceType", 52 }, { "Channel", 0 }, { "BaudRate", 500000 }, { "IP", "192.166.6.10" }, { "SignalName", signal }, { "TimeoutMs", 1000 }
                })));
            }
            return block;
        }

        private static BlockStepDefinition FromStep(SequenceStepDefinition step) { return new BlockStepDefinition { StepProperties = new Dictionary<string, object>(step.Properties, StringComparer.Ordinal) }; }

        private static void Expose(FunctionBlockDefinition block, string stepParameter, string blockParameter, string displayName, string type, object defaultValue, string unit)
        {
            BlockStepDefinition step = block.Steps.FirstOrDefault(item => item.StepProperties.ContainsKey(stepParameter));
            if (step == null) return;
            if (!block.Parameters.Any(parameter => parameter.Name == blockParameter)) block.Parameters.Add(new BlockParameterDefinition { Name = blockParameter, DisplayName = displayName, Type = type, DefaultValue = defaultValue, Unit = unit, Required = true });
            step.ParameterBindings[stepParameter] = blockParameter;
        }

        private static void NormalizeProject(FctStudioProject project, bool flattenParameters = false)
        {
            project.Blocks = project.Blocks ?? new List<FunctionBlockDefinition>(); project.Flow = project.Flow ?? new List<FlowBlockInstance>(); project.Breakpoints = project.Breakpoints ?? new List<string>();
            project.ProductLocatorPath = project.ProductLocatorPath ?? string.Empty; project.AuxiliaryDbcPath = project.AuxiliaryDbcPath ?? string.Empty; project.DriveStructure = string.IsNullOrWhiteSpace(project.DriveStructure) ? "SingleMainDrive" : project.DriveStructure; project.Capabilities = project.Capabilities ?? new List<string>();
            project.SequenceRoot = NormalizeDictionary(project.SequenceRoot);
            foreach (FunctionBlockDefinition block in project.Blocks)
            {
                block.Parameters = block.Parameters ?? new List<BlockParameterDefinition>(); block.Steps = block.Steps ?? new List<BlockStepDefinition>(); block.SupportedProducts = block.SupportedProducts ?? new List<string>();
                if (string.IsNullOrWhiteSpace(block.ModuleKind) || string.Equals(block.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase) && block.IsStandard) block.ModuleKind = block.IsStandard ? (block.Category == "主驱" || block.Category == "DCDC/辅驱" || block.Category == "产品" ? "Product" : "Standard") : "Custom";
                block.IsStandard = !string.Equals(block.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase);
                foreach (BlockParameterDefinition parameter in block.Parameters) parameter.DefaultValue = NormalizeValue(parameter.DefaultValue);
                foreach (BlockStepDefinition step in block.Steps) { step.StepProperties = NormalizeDictionary(step.StepProperties); step.ParameterBindings = step.ParameterBindings ?? new Dictionary<string, string>(StringComparer.Ordinal); step.ReferencedParameterOverrides = NormalizeDictionary(step.ReferencedParameterOverrides); }
                MigrateCurrentTableToDirectValues(block);
            }
            foreach (FlowBlockInstance instance in project.Flow) { instance.ParameterOverrides = NormalizeDictionary(instance.ParameterOverrides); instance.StepOverrides = NormalizeStepOverrides(instance.StepOverrides); instance.ReferenceParameterOverrides = NormalizeStepOverrides(instance.ReferenceParameterOverrides); instance.ModuleSnapshots = instance.ModuleSnapshots ?? new Dictionary<string, FunctionBlockDefinition>(StringComparer.Ordinal); foreach (FunctionBlockDefinition snapshot in instance.ModuleSnapshots.Values.Where(value => value != null)) { snapshot.Parameters = snapshot.Parameters ?? new List<BlockParameterDefinition>(); snapshot.Steps = snapshot.Steps ?? new List<BlockStepDefinition>(); snapshot.SupportedProducts = snapshot.SupportedProducts ?? new List<string>(); foreach (BlockStepDefinition step in snapshot.Steps) { step.StepProperties = NormalizeDictionary(step.StepProperties); step.ParameterBindings = step.ParameterBindings ?? new Dictionary<string, string>(StringComparer.Ordinal); step.ReferencedParameterOverrides = NormalizeDictionary(step.ReferencedParameterOverrides); } MigrateCurrentTableToDirectValues(snapshot); } if (instance.Snapshot != null) MigrateCurrentTableToDirectValues(instance.Snapshot); }
            if (flattenParameters) FlattenParameters(project);
        }

        private sealed class FctStudioEditorState
        {
            public string SequenceSha256 { get; set; }
            public FctStudioProject Project { get; set; }
        }
        internal static void MigrateCurrentTableToDirectValues(FunctionBlockDefinition block)
        {
            if (block == null || block.Name == null || block.Name.IndexOf("三相出流", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (block.Parameters != null && block.Parameters.Count > 0) return;
            Dictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.Ordinal) { { "TargetCurrent", "100" }, { "StepCurrent", "20" }, { "HoldTime", "10" }, { "Frequency", "60" } };
            foreach (BlockParameterDefinition parameter in block.Parameters ?? new List<BlockParameterDefinition>()) if (defaults.ContainsKey(parameter.Name) && parameter.DefaultValue != null) defaults[parameter.Name] = Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture);
            foreach (BlockStepDefinition step in block.Steps ?? new List<BlockStepDefinition>())
            {
                object raw; if (!step.StepProperties.TryGetValue("ChangesJson", out raw)) continue; string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty; text = text.Replace("${TargetCurrent*1.414}", defaults["TargetCurrent"] + "*1.414").Replace("${TargetCurrent}", defaults["TargetCurrent"]).Replace("${StepCurrent}", defaults["StepCurrent"]).Replace("${HoldTime}", defaults["HoldTime"]).Replace("${Frequency}", defaults["Frequency"]); step.StepProperties["ChangesJson"] = text; step.ParameterBindings.Clear();
            }
            block.Parameters.Clear();
        }
        internal static void FlattenParameters(FctStudioProject project)
        {
            if (project == null) return; foreach (FunctionBlockDefinition block in project.Blocks ?? new List<FunctionBlockDefinition>()) FlattenBlockParameters(block, null); foreach (FlowBlockInstance instance in project.Flow ?? new List<FlowBlockInstance>()) { if (instance.ParameterOverrides == null) instance.ParameterOverrides = new Dictionary<string, object>(StringComparer.Ordinal); if (instance.Snapshot != null) FlattenBlockParameters(instance.Snapshot, instance.ParameterOverrides); instance.ParameterOverrides.Clear(); }
        }
        internal static void FlattenBlockParameters(FunctionBlockDefinition block, IDictionary<string, object> overrides)
        {
            if (block == null) return; if (block.Parameters == null) block.Parameters = new List<BlockParameterDefinition>(); Dictionary<string, object> values = block.Parameters.Where(value => !string.IsNullOrWhiteSpace(value.Name)).ToDictionary(value => value.Name, value => value.DefaultValue, StringComparer.Ordinal); if (overrides != null) foreach (KeyValuePair<string, object> pair in overrides) values[pair.Key] = pair.Value; foreach (BlockStepDefinition step in block.Steps ?? new List<BlockStepDefinition>()) { if (step.ParameterBindings == null) step.ParameterBindings = new Dictionary<string, string>(StringComparer.Ordinal); foreach (KeyValuePair<string, string> binding in step.ParameterBindings.ToList()) { object value; if (values.TryGetValue(binding.Value, out value)) step.StepProperties[binding.Key] = value; } step.ParameterBindings.Clear(); } block.Parameters.Clear();
        }
        private static List<string> DefaultCapabilities(string product)
        {
            List<string> values = new List<string> { string.Equals(product, "C92", StringComparison.OrdinalIgnoreCase) || string.Equals(product, "C96", StringComparison.OrdinalIgnoreCase) ? "DualMainDrive" : "SingleMainDrive" };
            if (string.Equals(product, "C95", StringComparison.OrdinalIgnoreCase) || string.Equals(product, "C96", StringComparison.OrdinalIgnoreCase)) values.AddRange(new[] { "DCDC", "OilPump", "AirPump", "PDU" });
            return values;
        }

        private static Dictionary<string, object> NormalizeDictionary(IDictionary<string, object> source)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (source != null) foreach (KeyValuePair<string, object> pair in source) result[pair.Key] = NormalizeValue(pair.Value);
            return result;
        }
        private static Dictionary<string, Dictionary<string, object>> NormalizeStepOverrides(IDictionary<string, Dictionary<string, object>> source)
        {
            Dictionary<string, Dictionary<string, object>> result = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal); if (source != null) foreach (KeyValuePair<string, Dictionary<string, object>> pair in source) result[pair.Key] = NormalizeDictionary(pair.Value); return result;
        }
        private static object NormalizeValue(object value)
        {
            JValue primitive = value as JValue; if (primitive != null) return primitive.Value;
            JObject obj = value as JObject; if (obj != null) return obj.Properties().ToDictionary(property => property.Name, property => NormalizeValue(property.Value), StringComparer.Ordinal);
            JArray array = value as JArray; if (array != null) return array.Select(NormalizeValue).ToList();
            return value;
        }
    }
}
