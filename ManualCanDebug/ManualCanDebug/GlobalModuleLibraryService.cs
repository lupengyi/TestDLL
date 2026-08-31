using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManualCanDebug.Core;
using Newtonsoft.Json;

namespace ManualCanDebug
{
    internal static class GlobalModuleLibraryService
    {
        private static string _root;
        public static string RootPath { get { return _root; } }
        public static void Configure(string baseDirectory)
        {
            _root = Path.Combine(baseDirectory, "ModuleLibrary");
            Directory.CreateDirectory(Path.Combine(_root, "Standard"));
            Directory.CreateDirectory(Path.Combine(_root, "Product"));
            Directory.CreateDirectory(Path.Combine(_root, "Custom"));
            EnsureFaultClearStandardBlock();
            EnsureDriveReadStandardBlocks();
        }
        public static bool IsReusable(FunctionBlockDefinition block) { return block != null && !string.Equals(block.ModuleKind, "Custom", StringComparison.OrdinalIgnoreCase); }
        public static void Save(FunctionBlockDefinition block)
        {
            if (!IsReusable(block) || string.IsNullOrWhiteSpace(_root)) return; string kind = string.Equals(block.ModuleKind, "Product", StringComparison.OrdinalIgnoreCase) ? "Product" : string.Equals(block.ModuleKind, "Standard", StringComparison.OrdinalIgnoreCase) ? "Standard" : "Custom"; string directory = Path.Combine(_root, kind); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, block.Id + ".json"), JsonConvert.SerializeObject(block, Formatting.Indented));
        }
        public static void Delete(FunctionBlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(_root)) return; foreach (string kind in new[] { "Standard", "Product", "Custom" }) { string path = Path.Combine(_root, kind, block.Id + ".json"); if (File.Exists(path)) File.Delete(path); }
        }
        public static IReadOnlyList<FunctionBlockDefinition> Load()
        {
            List<FunctionBlockDefinition> result = new List<FunctionBlockDefinition>(); if (string.IsNullOrWhiteSpace(_root) || !Directory.Exists(_root)) return result.AsReadOnly(); IEnumerable<string> files = new[] { "Standard", "Product" }.SelectMany(kind => { string directory = Path.Combine(_root, kind); return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly) : new string[0]; }); foreach (string file in files) { try { FunctionBlockDefinition block = JsonConvert.DeserializeObject<FunctionBlockDefinition>(File.ReadAllText(file)); if (block == null || string.IsNullOrWhiteSpace(block.Id)) continue; if (block.Id.StartsWith("c96-chapter-", StringComparison.OrdinalIgnoreCase) || string.Equals(block.Category, "章节", StringComparison.OrdinalIgnoreCase)) continue; block.Parameters = block.Parameters ?? new List<BlockParameterDefinition>(); block.Steps = block.Steps ?? new List<BlockStepDefinition>(); block.SupportedProducts = block.SupportedProducts ?? new List<string>(); block.ModuleKind = file.IndexOf(Path.DirectorySeparatorChar + "Product" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ? "Product" : "Standard"; block.IsStandard = true; FctStudioProjectService.MigrateCurrentTableToDirectValues(block); result.Add(block); } catch { } } return result.GroupBy(value => value.Id, StringComparer.Ordinal).Select(group => group.Last()).ToList().AsReadOnly();
        }
        public static void MergeInto(FctStudioProject project)
        {
            if (project == null) return; foreach (FunctionBlockDefinition global in Load()) { int index = project.Blocks.FindIndex(block => string.Equals(block.Id, global.Id, StringComparison.Ordinal) || string.Equals(block.ModuleKind, global.ModuleKind, StringComparison.OrdinalIgnoreCase) && string.Equals(block.Name, global.Name, StringComparison.OrdinalIgnoreCase)); if (index >= 0) project.Blocks[index] = global.Clone(); else project.Blocks.Add(global.Clone()); }
        }

        private static void EnsureFaultClearStandardBlock()
        {
            const string id = "standard-clear-faults-c92-c96";
            string path = Path.Combine(_root, "Standard", id + ".json");
            if (File.Exists(path)) return;

            FunctionBlockDefinition block = new FunctionBlockDefinition
            {
                Id = id,
                Name = "清除错误",
                Category = "安全",
                ModuleKind = "Standard",
                Version = "1.0",
                Description = "C92/C96双主驱硬件故障复位：依次脉冲TM1/TM2硬件OC、共享Bus HW OV及TM1/TM2 UVLO+UVUP。每个复位端口拉高100ms后拉低，不使用FLTOVRD故障旁路。执行前必须关闭Gate、速度/电压使能并将目标电流设为0。",
                IsStandard = true,
                SupportedProducts = new List<string> { "C92", "C96" }
            };

            AddPulse(block, "TM1硬件OC清除", 4);
            AddPulse(block, "TM2硬件OC清除", 20);
            AddPulse(block, "共享Bus HW OV清除", 7);
            AddPulse(block, "TM1 UVLO+UVUP清除", 8, 9);
            AddPulse(block, "TM2 UVLO+UVUP清除", 22, 23);
            Save(block);
        }

        private static void AddPulse(FunctionBlockDefinition block, string name, params int[] offsets)
        {
            block.Steps.Add(CanWriteStep(name + " - 拉高", offsets, 1));
            block.Steps.Add(new BlockStepDefinition
            {
                StepProperties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "StepName", name + " - 保持100ms" },
                    { "RunMode", "Normal" },
                    { "FunctionName", "FCT_ExecuteLogic" },
                    { "RecordingLog", true },
                    { "Operation", "Delay" },
                    { "TimeMs", 100 },
                    { "ResultMode", "Action" }
                }
            });
            block.Steps.Add(CanWriteStep(name + " - 拉低", offsets, 0));
        }

        private static BlockStepDefinition CanWriteStep(string name, IEnumerable<int> offsets, byte value)
        {
            List<Dictionary<string, object>> changes = offsets.Select(offset => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "Name", name },
                { "Offset", offset },
                { "DataSize", 1 },
                { "DataType", "uint8" },
                { "Endian", "Little" },
                { "Value", value },
                { "WriteLast", false },
                { "WriteFinal", false }
            }).ToList();
            int tableLength = offsets.Max() + 1;
            return new BlockStepDefinition
            {
                StepProperties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "StepName", name },
                    { "RunMode", "Normal" },
                    { "FunctionName", "FCT_CANTable" },
                    { "RecordingLog", true },
                    { "Operation", "Write" },
                    { "AddrOffset", 0x3C },
                    { "TableLength", tableLength },
                    { "ChangesJson", JsonConvert.SerializeObject(changes) },
                    { "VerifyAfterWrite", true },
                    { "ResultMode", "Action" },
                    { "Product", "C96" }
                }
            };
        }

        private static void EnsureDriveReadStandardBlocks()
        {
            EnsureStandardBlock(CreateMotorFaultReadBlock("standard-tm1-motor-fault-read", "TM1读取电机故障", 0x6C));
            EnsureStandardBlock(CreateMotorFaultReadBlock("standard-tm2-motor-fault-read", "TM2读取电机故障", 0x84));
            EnsureStandardBlock(CreateThreePhaseCurrentReadBlock("standard-tm1-three-phase-current-read", "TM1读取三相电流", 0x7C));
            EnsureStandardBlock(CreateThreePhaseCurrentReadBlock("standard-tm2-three-phase-current-read", "TM2读取三相电流", 0x94));
        }

        private static void EnsureStandardBlock(FunctionBlockDefinition block)
        {
            string path = Path.Combine(_root, "Standard", block.Id + ".json");
            if (!File.Exists(path)) Save(block);
        }

        private static FunctionBlockDefinition CreateMotorFaultReadBlock(string id, string name, int addressOffset)
        {
            FunctionBlockDefinition block = StandardDriveBlock(id, name,
                "读取10字节Motor Status，解析所有故障位并判断是否无活动故障；同时显示Ramp和Status。故障存在时平台结果为FAIL并显示具体故障名称。");
            List<Dictionary<string, object>> faults = new List<Dictionary<string, object>>();
            string[][] names =
            {
                new[] { "Phase A over-current", "Phase B over-current", "Phase C over-current", "Phase A HW over-current", "Phase B HW over-current", "Phase C HW over-current", "Phase A upper temperature", "Phase A lower temperature" },
                new[] { "Phase B upper temperature", "Phase B lower temperature", "Phase C upper temperature", "Phase C lower temperature", "Motor temp 1", "Motor temp 2", "Motor temp 3 reserved", "Desat upper" },
                new[] { "Desat lower", "UV A upper", "UV B upper", "UV C upper", "UV A lower", "UV B lower", "UV C lower", "Master fault" },
                new[] { "Bus under-voltage", "Bus over-voltage", "Bus HW over-voltage", "Bus HW over-voltage latched", "All upper UV latched", "All lower UV latched", "Zero-sequence over-current", "UV upper" },
                new[] { "UV lower", "Board temperature", "Reserved byte4 bit2", "Reserved byte4 bit3", "Reserved byte4 bit4", "Reserved byte4 bit5", "Reserved byte4 bit6", "Reserved byte4 bit7" }
            };
            for (int byteIndex = 0; byteIndex < names.Length; byteIndex++)
                for (int bit = 0; bit < 8; bit++)
                    faults.Add(new Dictionary<string, object> { { "Byte", byteIndex }, { "Bit", bit }, { "Name", names[byteIndex][bit] }, { "ActiveLow", false } });
            block.Steps.Add(new BlockStepDefinition
            {
                StepProperties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "StepName", name }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANCalculatedResults" }, { "RecordingLog", true },
                    { "CalculationType", "PackedFaultStatus" }, { "AddrOffset", addressOffset }, { "TableLength", 10 }, { "PointerDepth", 1 },
                    { "FaultMapJson", JsonConvert.SerializeObject(faults) }, { "RampOffset", 8 }, { "StatusOffset", 9 },
                    { "JudgeNoFault", true }, { "NoFaultText", "No active fault bits" }, { "ResultMode", "StringLimit" }, { "Product", "C96" }
                }
            });
            return block;
        }

        private static FunctionBlockDefinition CreateThreePhaseCurrentReadBlock(string id, string name, int addressOffset)
        {
            FunctionBlockDefinition block = StandardDriveBlock(id, name,
                "读取三相电流Min/Max并计算A/B/C相RMS、三相平均实际电流及不平衡度。默认电流范围0~1000A、不平衡度0~1000A，可在流程页快速修改LIMIT。");
            Dictionary<string, object> inputs = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "PhaseA_Min", NumberInput(12) }, { "PhaseB_Min", NumberInput(16) }, { "PhaseC_Min", NumberInput(20) },
                { "PhaseA_Max", NumberInput(24) }, { "PhaseB_Max", NumberInput(28) }, { "PhaseC_Max", NumberInput(32) }
            };
            block.Steps.Add(new BlockStepDefinition
            {
                StepProperties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "StepName", name }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANCalculatedResults" }, { "RecordingLog", true },
                    { "CalculationType", "ThreePhaseCurrentRms" }, { "AddrOffset", addressOffset }, { "TableLength", 40 }, { "PointerDepth", 1 },
                    { "InputsJson", JsonConvert.SerializeObject(inputs) }, { "PublishPhases", true },
                    { "ResultMode", "NumericLimit" }, { "LowLimit", 0.0 }, { "HighLimit", 1000.0 }, { "Comtype", "GELE" }, { "Unit", "A" },
                    { "ImbalanceLowLimit", 0.0 }, { "ImbalanceHighLimit", 1000.0 }, { "Product", "C96" }
                }
            });
            return block;
        }

        private static Dictionary<string, object> NumberInput(int offset)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal) { { "Offset", offset }, { "DataSize", 4 }, { "DataType", "float32" }, { "Endian", "Little" } };
        }

        private static FunctionBlockDefinition StandardDriveBlock(string id, string name, string description)
        {
            return new FunctionBlockDefinition
            {
                Id = id, Name = name, Category = "主驱", ModuleKind = "Standard", Version = "1.0", Description = description,
                IsStandard = true, SupportedProducts = new List<string> { "C92", "C96" }
            };
        }
    }
}
