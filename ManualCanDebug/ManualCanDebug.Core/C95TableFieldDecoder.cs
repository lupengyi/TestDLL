using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ManualCanDebug.Core
{
    public sealed class C95TableFieldResult
    {
        public string Category { get; internal set; }
        public string TableName { get; internal set; }
        public string TableAddress { get; internal set; }
        public string PointerAddress { get; internal set; }
        public int FieldOffset { get; internal set; }
        public string FieldName { get; internal set; }
        public string DataType { get; internal set; }
        public string RawBytes { get; internal set; }
        public string ValueText { get; internal set; }
        public string Interpretation { get; internal set; }
        public string Status { get; internal set; }
    }

    public static class C95TableFieldDecoder
    {
        private sealed class FieldDef
        {
            public FieldDef(int offset, int size, string name, string type, string note = "") { Offset = offset; Size = size; Name = name; Type = type; Note = note; }
            public int Offset; public int Size; public string Name; public string Type; public string Note;
        }

        private static readonly string[] EnableNames =
        {
            "INVTRA_FLT_RST_OC", "INVTRA_ACT_SC_SEL^", "INVTRA_TRQ_SCRTY_DIS^", "INVTRA_ENSW", "INVTRA_ACT_SC^", "INVTRA_MTR_DIS^",
            "INVTRA_FLTRST_OV", "INVTRA_FLTRST_UV", "INVTRA_FLTOVRD_OV", "INVTRA_FLTOVRD_OC", "PWR_HOLD_ON", "15VSEPIC_DIS",
            "L3_ASC_TEST", "L3_FS_SAFESTATE", "INVTRA_MTRATEMP1_EN", "INVTRA_MTRATEMP1_RTN_EN", "INVTRA_MTRATEMP2_EN", "INVTRA_MTRATEMP2_RTN_EN",
            "INVTRA_FLTRST_UVLO", "INVTRA_FLTRST_UVUP", "SV_FLTRST^", "SPEED_9V_EN", "5V_VCC1_EN", "5V_VCC2_EN", "OIL_TEMP_SEL",
            "MOT_PWR_ON", "SV_HSD_ST1", "SV_HSD_ST2", "MOT1_EN", "MOT2_EN", "MOT1_CAL", "MOT2_CAL", "SV_HSD_IN1", "SV_HSD_IN2"
        };

        private static readonly string[] PulseOutputNames =
        {
            "ADC_TEST_CMD", "INVTRA_PWM_AHI_C", "INVTRA_PWM_ALO_C", "INVTRA_PWM_BHI_C", "INVTRA_PWM_BLO_C", "INVTRA_PWM_CHI_C", "INVTRA_PWM_CLO_C",
            "RES_EXCLO_C", "RES_EXCHI_C", "MOT1_PWM_AHI", "MOT1_PWM_ALO", "MOT1_PWM_BHI", "MOT1_PWM_BLO", "MOT1_PWM_CHI", "MOT1_PWM_CLO",
            "MOT2_PWM_AHI", "MOT2_PWM_ALO", "MOT2_PWM_BHI", "MOT2_PWM_BLO", "MOT2_PWM_CHI", "MOT2_PWM_CLO", "SV_HSD_IN1", "SV_HSD_IN2", "SV_LSD_IN1", "SV_LSD_IN2"
        };

        private static readonly string[][] FaultNames =
        {
            new[] { "A相过流", "B相过流", "C相过流", "A相硬件过流", "B相硬件过流", "C相硬件过流", "母线欠压", "母线过压" },
            new[] { "母线硬件过压", "板温故障", "AL1相温度故障", "BL1相温度故障", "CL1相温度故障", "AU1相温度故障", "BU1相温度故障", "CU1相温度故障" },
            new[] { "电机1温度故障", "HI退饱和故障", "电机2温度故障", "CHI退饱和故障", "LO退饱和故障", "BLO退饱和故障", "CLO退饱和故障", "上桥臂欠压" },
            new[] { "下桥臂欠压", "主故障置位", "电机超速", "保留位3", "保留位4", "保留位5", "保留位6", "保留位7" }
        };

        public static IReadOnlyList<C95TableFieldResult> Decode(C95TableReadResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!result.Succeeded) return One(result, 0, "读取失败", "Error", new byte[0], result.Error, result.Error);
            if (!result.Table.HasDefinedLength) return One(result, 0, "Locator未定义字段", "PointerOnly", new byte[0], result.PointerAddress, result.Table.Note);

            C95InputTableDefinition input = FindInputDefinition(result.Table.AddressOffset);
            if (input != null)
            {
                List<C95TableFieldResult> fields = new List<C95TableFieldResult>();
                foreach (C95InputSignalDefinition signal in input.Signals)
                {
                    C95InputSignalResult decoded = C95InputSignalResult.Decode(input, signal, result.Data);
                    fields.Add(Create(result, signal.Offset, signal.SignalSize(), signal.Name, signal.ValueType.ToString(), decoded.RawBytes, decoded.ValueText, decoded.Interpretation));
                }
                return fields.AsReadOnly();
            }

            switch (result.Table.AddressOffset)
            {
                case 0x38: return Bytes(result, new[] { "Re_Init_Min_Max_Tables_Flag" });
                case 0x3C: return Bytes(result, EnableNames);
                case 0x40: return UInt32s(result, PulseOutputNames);
                case 0x44: return Definitions(result, new[] { F(0, 4, "position", "Float32", "degrees"), F(4, 4, "velocity", "Float32", "frequency"), F(8, 1, "faults", "ResolverFault") });
                case 0x48: return Bytes(result, new[] { "FT_Mode_N" });
                case 0x4C: return Bytes(result, new[] { "FT_Requested_Mode" });
                case 0x50: return Definitions(result, new[] { F(0, 4, "Code_version", "VersionUInt32") });
                case 0x54: return Definitions(result, new[] { F(0, 4, "Main_sw_version", "MainVersion") });
                case 0x58: return Definitions(result, new[] { F(0,4,"Iqs_Start","Float32"), F(4,4,"Iqs_End","Float32"), F(8,4,"Iqs_Step_Size","Float32"), F(12,4,"Max_Value_Hold_Time","Float32"), F(16,4,"Output_Frequency","Float32"), F(20,1,"Motor_Control_Test_Mode","UInt8"), F(21,1,"unused","UInt8"), F(22,2,"On_Off_Ramp_Time","UInt16"), F(24,2,"Motor_Base_Freq","UInt16"), F(26,1,"MCP_Motor_Gate_Enable","UInt8"), F(27,1,"New_Data_Flag","UInt8"), F(28,1,"Reset_Motor_Faults","UInt8"), F(29,1,"Speed_Control_Enable","UInt8"), F(30,4,"Speed_Setpt","Float32"), F(34,1,"Voltage_Control_Enable","UInt8"), F(35,1,"padding","UInt8"), F(36,4,"Voltage_Setpt","Float32") });
                case 0x5C: return MotorStatus(result);
                case 0x60: return Floats(result, new[] { "FTM_KID", "FTM_KIQ", "FTM_KPD", "FTM_KPQ" });
                case 0x64: return Floats(result, new[] { "PwrMod_Temp_Max", "PwrMod_Temp_Min", "Motor_Temp_Max", "Motor_Temp_Min", "Board_Temp_Max", "Board_Temp_Min", "Max_Phase_Current", "Vbus_Max", "Vbus_Min" });
                case 0x68: return Floats(result, new[] { "Motor_RPM", "Motor_RPM_Max", "Motor_RPM_Min" });
                case 0x6C: return Bytes(result, new[] { "PhaseA_DC", "PhaseB_DC", "PhaseC_DC", "Phase_Change_Flag", "Current_Min_Max_Reset_Flag" });
                case 0x70: return Floats(result, new[] { "PhaseA_Current", "PhaseB_Current", "PhaseC_Current", "PhaseA_Min_Current", "PhaseB_Min_Current", "PhaseC_Min_Current", "PhaseA_Max_Current", "PhaseB_Max_Current", "PhaseC_Max_Current" });
                case 0x74: return Bytes(result, new[] { "Hardware_version" });
                case 0x78: return Bytes(result, new[] { "Variant_ID" });
                case 0x7C: return UInt16s(result, new[] { "BUCKH", "BUCKL", "VCCL_1", "VCCL_2" });
                case 0x80: return Mpi(result);
                case 0x84: return Definitions(result, new[] { F(0, 4, "Write_MPI_to_Flash", "UInt32") });
                case 0x8C: return Bytes(result, new[] { "Solenoid_Control_Status" });
                case 0x98: return Bytes(result, new[] { "INVTRA_Auto_output_PWM_flag", "BLDC_Auto_output_PWM_flag", "Solenoid_Auto_output_PWM_flag", "Vehicle_CAN_message_flag", "INSTR_CAN_message_flag", "PT_CAN_message_flag", "TCU_Mot_Auto_output_PWM_flag" });
                case 0x9C: return Definitions(result, new[] { F(0,2,"ft_runin_frequency","UInt16"), F(2,4,"ft_runin_max_phase_temp","Float32"), F(6,1,"ft_activate_runin_sequence","UInt8"), F(7,1,"ft_runin_new_data","UInt8") });
                case 0xA0: return Definitions(result, new[] { F(0,1,"runin_status","RunInStatus") });
                case 0xA8: return Floats(result, new[] { "INVTRA_PWM_Frequency", "INVTRA_PWM_AHI_Duty", "INVTRA_PWM_BHI_Duty", "INVTRA_PWM_CHI_Duty" });
                default: return Bytes(result, null);
            }
        }

        private static IReadOnlyList<C95TableFieldResult> MotorStatus(C95TableReadResult result)
        {
            MotorStatusInfo status = MotorStatusInfo.Parse(result.Data);
            List<C95TableFieldResult> fields = new List<C95TableFieldResult>
            {
                Create(result, 0, 1, "Ramp_Mode", "Enum", Hex(result.Data,0,1), result.Data[0].ToString(CultureInfo.InvariantCulture), status.RampModeDescription),
                Create(result, 1, 1, "Status", "Enum", Hex(result.Data,1,1), result.Data[1].ToString(CultureInfo.InvariantCulture), status.SequenceStatusDescription),
                Create(result, 2, 1, "unused_2", "UInt8", Hex(result.Data,2,1), result.Data[2].ToString(CultureInfo.InvariantCulture), ""),
                Create(result, 3, 1, "unused_3", "UInt8", Hex(result.Data,3,1), result.Data[3].ToString(CultureInfo.InvariantCulture), "")
            };
            for (int index = 0; index < 4; index++)
            {
                byte value = result.Data[4 + index];
                List<string> active = new List<string>();
                for (int bit = 0; bit < 8; bit++) if ((value & (1 << bit)) != 0) active.Add(FaultNames[index][bit]);
                fields.Add(Create(result, 4 + index, 1, "Fault_Status_Byte" + (4 + index), "BitField", Hex(result.Data,4 + index,1), "0x" + value.ToString("X2"), active.Count == 0 ? "无故障位" : string.Join("、", active)));
            }
            return fields.AsReadOnly();
        }

        private static IReadOnlyList<C95TableFieldResult> Mpi(C95TableReadResult result)
        {
            return Definitions(result, new[]
            {
                F(0,33,"Human_Readable_Mark_In_Label","Ascii"), F(33,3,"Reserve_21","Hex"), F(36,10,"Customer_Part_Number","Ascii"), F(46,10,"Reserve_2E","Hex"),
                F(56,15,"HW_Assembly_Part_Number","Ascii"), F(71,10,"Reserve_47","Hex"), F(81,8,"End_Model","Ascii"), F(89,8,"Reserve_59","Hex"), F(97,8,"Base_Model","Ascii"),
                F(105,17,"Reserve_69","Hex"), F(122,3,"UPS_Julian_Day","Ascii"), F(125,2,"UPS_Calendar_Year_Last_Digit","Ascii"), F(127,4,"UPS_Sequence_Number","Ascii"),
                F(131,4,"Reserve_83","UInt32"), F(135,4,"BW_FT_Mode_Password","Hex"), F(139,16,"Reserve_8B","Hex"), F(155,1,"Manufacturing_Site","UInt8"), F(156,2,"MFG_Cal_Enable","UInt16"),
                F(158,4,"MFG_Cal_INVTR_Phase_A_Current_Gain","Float32"), F(162,4,"MFG_Cal_INVTR_Phase_A_Current_offset","Float32"), F(166,4,"MFG_Cal_INVTR_Phase_B_Current_Gain","Float32"),
                F(170,4,"MFG_Cal_INVTR_Phase_B_Current_offset","Float32"), F(174,4,"MFG_Cal_INVTR_Phase_C_Current_Gain","Float32"), F(178,4,"MFG_Cal_INVTR_Phase_C_Current_offset","Float32"),
                F(182,4,"MFG_Cal_HV_Gain_L","Float32"), F(186,4,"MFG_Cal_HV_offset_L","Float32"), F(190,4,"MFG_Cal_HV_Gain_H","Float32"), F(194,4,"MFG_Cal_HV_offset_H","Float32"),
                F(198,4,"MFG_Cal_INVTR_PH_AU_TEMP_offset","Float32"), F(202,4,"MFG_Cal_INVTR_PH_BU_TEMP_offset","Float32"), F(206,4,"MFG_Cal_INVTR_PH_CU_TEMP_offset","Float32"),
                F(210,4,"MFG_Cal_INVTR_PH_CL_TEMP_offset","Float32"), F(214,4,"MFG_Cal_INVTR_PH_BL_TEMP_offset","Float32"), F(218,4,"MFG_Cal_INVTR_PH_AL_TEMP_offset","Float32"),
                F(222,4,"MFG_Cal_INVTR_COOLANT_IN_TEMP_offset","Float32"), F(226,4,"MFG_Cal_INVTR_COOLANT_OUT_TEMP_offset","Float32"), F(230,4,"MFG_MPI_CRC","UInt32"), F(234,4,"Reserve_EA","Hex")
            });
        }

        private static IReadOnlyList<C95TableFieldResult> Definitions(C95TableReadResult result, IEnumerable<FieldDef> definitions)
        {
            List<C95TableFieldResult> fields = new List<C95TableFieldResult>();
            foreach (FieldDef definition in definitions)
            {
                if (definition.Offset + definition.Size > result.Data.Length)
                {
                    fields.Add(Create(result, definition.Offset, 0, definition.Name, definition.Type, "", "", "数据越界"));
                    continue;
                }
                byte[] raw = result.Data.Skip(definition.Offset).Take(definition.Size).ToArray();
                string interpretation = definition.Note;
                string value;
                switch (definition.Type)
                {
                    case "Float32": value = BitConverter.ToSingle(raw, 0).ToString("0.######", CultureInfo.InvariantCulture); break;
                    case "UInt16": value = BitConverter.ToUInt16(raw, 0).ToString(CultureInfo.InvariantCulture); break;
                    case "UInt32": value = BitConverter.ToUInt32(raw, 0).ToString(CultureInfo.InvariantCulture); break;
                    case "UInt8": value = raw[0].ToString(CultureInfo.InvariantCulture); break;
                    case "Ascii": value = Encoding.ASCII.GetString(raw).TrimEnd('\0', ' ', (char)0xFF); break;
                    case "VersionUInt32": value = "0x" + BitConverter.ToUInt32(raw, 0).ToString("X8"); break;
                    case "MainVersion": value = HexDataParser.Format(raw); interpretation = string.Format("Variant=0x{0:X2}, Phase=0x{1:X2}, Major={2}, Minor={3}", raw[3], raw[2], raw[1], raw[0]); break;
                    case "ResolverFault": value = raw[0].ToString(CultureInfo.InvariantCulture); interpretation = raw[0] == 0 ? "信号丢失" : raw[0] == 1 ? "信号降级" : raw[0] == 2 ? "跟踪丢失" : raw[0] == 3 ? "无故障" : "未知"; break;
                    case "RunInStatus": value = raw[0].ToString(CultureInfo.InvariantCulture); interpretation = raw[0] == 0 ? "IDLE" : raw[0] == 1 ? "RUNNING" : raw[0] == 2 ? "ERROR_OVERTEMP" : raw[0] == 3 ? "ERROR_INVALID_FREQ" : "未知"; break;
                    default: value = HexDataParser.Format(raw); break;
                }
                fields.Add(Create(result, definition.Offset, definition.Size, definition.Name, definition.Type, HexDataParser.Format(raw), value, interpretation));
            }
            return fields.AsReadOnly();
        }

        private static IReadOnlyList<C95TableFieldResult> Floats(C95TableReadResult result, string[] names) { return Definitions(result, names.Select((name,index) => F(index*4,4,name,"Float32"))); }
        private static IReadOnlyList<C95TableFieldResult> UInt32s(C95TableReadResult result, string[] names) { return Definitions(result, names.Select((name,index) => F(index*4,4,name,"UInt32"))); }
        private static IReadOnlyList<C95TableFieldResult> UInt16s(C95TableReadResult result, string[] names) { return Definitions(result, names.Select((name,index) => F(index*2,2,name,"UInt16"))); }
        private static IReadOnlyList<C95TableFieldResult> Bytes(C95TableReadResult result, string[] names)
        {
            return Definitions(result, Enumerable.Range(0, result.Data.Length).Select(index => F(index,1,names != null && index < names.Length ? names[index] : "Byte_0x" + index.ToString("X2"),"UInt8")));
        }

        private static C95InputTableDefinition FindInputDefinition(uint offset)
        {
            uint baseOffset;
            if (offset <= 0x08) baseOffset=0x00; else if (offset>=0x0C && offset<=0x14) baseOffset=0x0C; else if (offset==0x18 || offset==0x1C) baseOffset=0x18;
            else if (offset>=0x20 && offset<=0x28) baseOffset=0x20; else if (offset>=0x2C && offset<=0x34) baseOffset=0x2C; else return null;
            return C95InputCatalog.Tables.First(table => table.AddressOffset == baseOffset);
        }

        private static FieldDef F(int offset, int size, string name, string type, string note = "") { return new FieldDef(offset,size,name,type,note); }
        private static IReadOnlyList<C95TableFieldResult> One(C95TableReadResult result, int offset, string name, string type, byte[] raw, string value, string interpretation)
        { return new ReadOnlyCollection<C95TableFieldResult>(new List<C95TableFieldResult> { Create(result,offset,raw.Length,name,type,HexDataParser.Format(raw),value,interpretation) }); }
        private static C95TableFieldResult Create(C95TableReadResult result, int offset, int size, string name, string type, string raw, string value, string interpretation)
        {
            return new C95TableFieldResult { Category=result.Table.Category, TableName=result.Table.Name, TableAddress=result.Table.AddressText, PointerAddress=result.PointerAddress, FieldOffset=offset, FieldName=name, DataType=type, RawBytes=raw, ValueText=value, Interpretation=interpretation ?? "", Status=result.Status };
        }
        private static string Hex(byte[] data, int offset, int size) { return HexDataParser.Format(data.Skip(offset).Take(size).ToArray()); }
    }

    internal static class C95InputSignalDefinitionExtensions
    {
        public static int SignalSize(this C95InputSignalDefinition signal) { return signal.Size; }
    }
}
