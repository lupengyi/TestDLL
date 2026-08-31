using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public sealed class C95TableDefinition
    {
        public C95TableDefinition(string category, string name, uint addressOffset, int byteLength, int pointerDepth, string note)
        {
            Category = category;
            Name = name;
            AddressOffset = addressOffset;
            ByteLength = byteLength;
            PointerDepth = pointerDepth;
            Note = note ?? string.Empty;
        }

        public string Category { get; private set; }
        public string Name { get; private set; }
        public uint AddressOffset { get; private set; }
        public int ByteLength { get; private set; }
        public int PointerDepth { get; private set; }
        public string Note { get; private set; }
        public bool HasDefinedLength { get { return ByteLength > 0; } }
        public string AddressText { get { return "0x" + AddressOffset.ToString("X2", CultureInfo.InvariantCulture); } }
    }

    public sealed class C95TableReadResult
    {
        private C95TableReadResult(C95TableDefinition table, bool succeeded, string pointerAddress, byte[] data, string error)
        {
            Table = table;
            Succeeded = succeeded;
            PointerAddress = pointerAddress ?? string.Empty;
            Data = data ?? new byte[0];
            Error = error ?? string.Empty;
        }

        public C95TableDefinition Table { get; private set; }
        public bool Succeeded { get; private set; }
        public string PointerAddress { get; private set; }
        public byte[] Data { get; private set; }
        public string Error { get; private set; }
        public string Status { get { return Succeeded ? (Table.HasDefinedLength ? "读取成功" : "仅指针（Locator未定义长度）") : "读取失败"; } }
        public string RawBytes { get { return HexDataParser.Format(Data); } }
        public string DecodedValues { get { return BuildDecodedValues(); } }
        public string ValuePreview
        {
            get
            {
                string text = DecodedValues;
                return text.Length <= 220 ? text : text.Substring(0, 220) + "...";
            }
        }

        public static C95TableReadResult Success(C95TableDefinition table, string pointerAddress, byte[] data)
        {
            return new C95TableReadResult(table, true, pointerAddress, data, string.Empty);
        }

        public static C95TableReadResult Failure(C95TableDefinition table, string error)
        {
            return new C95TableReadResult(table, false, string.Empty, null, error);
        }

        private string BuildDecodedValues()
        {
            if (!Succeeded) return Error;
            if (!Table.HasDefinedLength) return Table.Note;
            C95InputTableDefinition inputDefinition = FindInputDefinition();
            if (inputDefinition != null)
            {
                return string.Join("; ", inputDefinition.Signals.Select(signal =>
                {
                    C95InputSignalResult value = C95InputSignalResult.Decode(inputDefinition, signal, Data);
                    return signal.Name + "=" + value.ValueText + (string.IsNullOrEmpty(value.Interpretation) ? string.Empty : "(" + value.Interpretation + ")");
                }));
            }
            if (Data.Length == 1) return Data[0].ToString(CultureInfo.InvariantCulture) + " (0x" + Data[0].ToString("X2", CultureInfo.InvariantCulture) + ")";
            if (Data.Length == 2) return BitConverter.ToUInt16(Data, 0).ToString(CultureInfo.InvariantCulture);
            if (Data.Length == 4)
            {
                return string.Format(CultureInfo.InvariantCulture, "UInt32={0}; Float32={1:0.######}", BitConverter.ToUInt32(Data, 0), BitConverter.ToSingle(Data, 0));
            }
            if (Table.AddressOffset == 0x5C) return MotorStatusInfo.Parse(Data).Summary;
            switch (Table.AddressOffset)
            {
                case 0x40: return UInt32Values(Data, "PulseOutput");
                case 0x44: return string.Format(CultureInfo.InvariantCulture, "position={0:0.######} deg; velocity={1:0.######}; faults={2}", BitConverter.ToSingle(Data, 0), BitConverter.ToSingle(Data, 4), Data[8]);
                case 0x58: return MixedValues(Data, new[] { "IqsStart:F:0", "IqsEnd:F:4", "IqsStep:F:8", "HoldTime:F:12", "Frequency:F:16", "TestMode:B:20", "RampTime:U:22", "BaseFreq:U:24", "GateEnable:B:26", "NewData:B:27", "ResetFault:B:28", "SpeedEnable:B:29", "SpeedSet:F:30", "VoltageEnable:B:34", "VoltageSet:F:36" });
                case 0x60: return FloatValues(Data, new[] { "FTM_KID", "FTM_KIQ", "FTM_KPD", "FTM_KPQ" });
                case 0x64: return FloatValues(Data, new[] { "PwrMod_Temp_Max", "PwrMod_Temp_Min", "Motor_Temp_Max", "Motor_Temp_Min", "Board_Temp_Max", "Board_Temp_Min", "Max_Phase_Current", "Vbus_Max", "Vbus_Min" });
                case 0x68: return FloatValues(Data, new[] { "Motor_RPM", "Motor_RPM_Max", "Motor_RPM_Min" });
                case 0x6C: return ByteValues(Data, new[] { "PhaseA_DC", "PhaseB_DC", "PhaseC_DC", "Phase_Change_Flag", "Current_Min_Max_Reset_Flag" });
                case 0x70: return FloatValues(Data, new[] { "PhaseA_Current", "PhaseB_Current", "PhaseC_Current", "PhaseA_Min_Current", "PhaseB_Min_Current", "PhaseC_Min_Current", "PhaseA_Max_Current", "PhaseB_Max_Current", "PhaseC_Max_Current" });
                case 0x7C: return UInt16Values(Data, new[] { "BUCKH", "BUCKL", "VCCL_1", "VCCL_2" });
                case 0x98: return ByteValues(Data, new[] { "INVTRA_Auto_PWM", "BLDC_Auto_PWM", "Solenoid_Auto_PWM", "Vehicle_CAN", "INSTR_CAN", "PT_CAN", "TCU_Mot_Auto_PWM" });
                case 0x9C: return MixedValues(Data, new[] { "runin_frequency:U:0", "runin_max_phase_temp:F:2", "activate_runin:B:6", "runin_new_data:B:7" });
                case 0xA8: return FloatValues(Data, new[] { "PWM_Frequency", "PWM_AHI_Duty", "PWM_BHI_Duty", "PWM_CHI_Duty" });
                default: return ByteValues(Data, null);
            }
        }

        private C95InputTableDefinition FindInputDefinition()
        {
            uint baseOffset;
            if (Table.AddressOffset <= 0x08) baseOffset = 0x00;
            else if (Table.AddressOffset >= 0x0C && Table.AddressOffset <= 0x14) baseOffset = 0x0C;
            else if (Table.AddressOffset == 0x18 || Table.AddressOffset == 0x1C) baseOffset = 0x18;
            else if (Table.AddressOffset >= 0x20 && Table.AddressOffset <= 0x28) baseOffset = 0x20;
            else if (Table.AddressOffset >= 0x2C && Table.AddressOffset <= 0x34) baseOffset = 0x2C;
            else return null;
            return C95InputCatalog.Tables.First(table => table.AddressOffset == baseOffset);
        }

        private static string FloatValues(byte[] data, string[] names)
        {
            return string.Join("; ", Enumerable.Range(0, data.Length / 4).Select(index =>
                (names != null && index < names.Length ? names[index] : "+0x" + (index * 4).ToString("X2")) + "=" + BitConverter.ToSingle(data, index * 4).ToString("0.######", CultureInfo.InvariantCulture)));
        }

        private static string UInt32Values(byte[] data, string prefix)
        {
            return string.Join("; ", Enumerable.Range(0, data.Length / 4).Select(index => prefix + index + "=" + BitConverter.ToUInt32(data, index * 4).ToString(CultureInfo.InvariantCulture)));
        }

        private static string UInt16Values(byte[] data, string[] names)
        {
            return string.Join("; ", Enumerable.Range(0, data.Length / 2).Select(index =>
                (names != null && index < names.Length ? names[index] : "+0x" + (index * 2).ToString("X2")) + "=" + BitConverter.ToUInt16(data, index * 2).ToString(CultureInfo.InvariantCulture)));
        }

        private static string ByteValues(byte[] data, string[] names)
        {
            return string.Join("; ", Enumerable.Range(0, data.Length).Select(index =>
                (names != null && index < names.Length ? names[index] : "+0x" + index.ToString("X2")) + "=" + data[index].ToString(CultureInfo.InvariantCulture)));
        }

        private static string MixedValues(byte[] data, string[] definitions)
        {
            return string.Join("; ", definitions.Select(definition =>
            {
                string[] parts = definition.Split(':');
                int offset = int.Parse(parts[2], CultureInfo.InvariantCulture);
                string value = parts[1] == "F" ? BitConverter.ToSingle(data, offset).ToString("0.######", CultureInfo.InvariantCulture)
                    : parts[1] == "U" ? BitConverter.ToUInt16(data, offset).ToString(CultureInfo.InvariantCulture)
                    : data[offset].ToString(CultureInfo.InvariantCulture);
                return parts[0] + "=" + value;
            }));
        }
    }

    public static class C95AllTableCatalog
    {
        private static readonly IReadOnlyList<C95TableDefinition> Catalog = new ReadOnlyCollection<C95TableDefinition>(new List<C95TableDefinition>
        {
            T("输入表", "FT_Analog_Inputs", 0x00, 228), T("输入表", "FT_Analog_Input_Max", 0x04, 228), T("输入表", "FT_Analog_Input_Min", 0x08, 228),
            T("输入表", "FT_Ana_In_Counts", 0x0C, 114), T("输入表", "FT_Ana_In_Counts_Max", 0x10, 114), T("输入表", "FT_Ana_In_Counts_Min", 0x14, 114),
            T("输入表", "FT_Discrete_Inputs", 0x18, 49), T("输入表", "FT_Discrete_Inputs_Transition", 0x1C, 49),
            T("输入表", "FT_Pulse_Inputs", 0x20, 80), T("输入表", "FT_Pulse_Inputs_Max", 0x24, 80), T("输入表", "FT_Pulse_Inputs_Min", 0x28, 80),
            T("输入表", "FT_Phase_Temp_Inputs", 0x2C, 24), T("输入表", "FT_Phase_Temp_Inputs_Max", 0x30, 24), T("输入表", "FT_Phase_Temp_Inputs_Min", 0x34, 24),
            T("输入表", "FT_Re_Init_Min_Max_Tables_Flag", 0x38, 1),
            T("输出表", "FT_Enables", 0x3C, 34), T("输出表", "FT_Pulse_Outputs", 0x40, 100),
            T("旋变/模式", "FT_Resolver_Data", 0x44, 9), T("旋变/模式", "FT_Mode_N", 0x48, 1), T("旋变/模式", "FT_Requested_Mode", 0x4C, 1),
            T("版本", "FT_Code_Version", 0x50, 4), T("版本", "FT_Main_SW_Version", 0x54, 4),
            T("电机", "FT_Motor_Control_Data", 0x58, 40), T("电机", "FT_Motor_Status_Data", 0x5C, 8), T("电机", "FT_Motor_Gain_Settings", 0x60, 16),
            T("电机", "FT_Motor_Limits", 0x64, 36), T("电机", "FT_Motor_RPM_Out", 0x68, 12),
            T("电流", "FT_Current_Sense_Test_Cmd_Data", 0x6C, 5), T("电流", "FT_Current_Sense_Test_Result_Data", 0x70, 36),
            T("版本", "FT_Hardware_Version", 0x74, 1), T("版本", "FT_Variant_Type", 0x78, 1),
            T("其它", "FT_C3PS_TDE_Data", 0x7C, 8), new C95TableDefinition("MPI", "FT_MPI_Pointer / MPI全部数据", 0x80, 238, 2, "二级指针"),
            T("MPI", "FT_Write_MPI_to_Flash", 0x84, 4),
            U("新增控制", "FT_Solenoid_Control", 0x88), T("新增控制", "FT_Solenoid_Control_Status", 0x8C, 1),
            U("新增控制", "FT_BLDC_Control", 0x90), U("新增控制", "FT_BLDC_Control_Status", 0x94),
            T("自动输出", "FT_Auto_Output_PWM_Mode", 0x98, 7), T("RunIn", "FT_Ph_Current_RunIn_Cmd", 0x9C, 8), T("RunIn", "FT_Ph_Current_RunIn_Status", 0xA0, 1),
            U("自动输出", "FT_BLDC_Control_PWM_Settings", 0xA4), T("自动输出", "FT_INVTRA_Control_PWM_Settings", 0xA8, 16)
        });

        public static IReadOnlyList<C95TableDefinition> Tables { get { return Catalog; } }

        private static C95TableDefinition T(string category, string name, uint offset, int length)
        {
            return new C95TableDefinition(category, name, offset, length, 1, string.Empty);
        }

        private static C95TableDefinition U(string category, string name, uint offset)
        {
            return new C95TableDefinition(category, name, offset, 0, 1, "Locator只列出表名，没有字段结构和总长度；已读取表指针，不猜测长度。 ");
        }
    }
}
