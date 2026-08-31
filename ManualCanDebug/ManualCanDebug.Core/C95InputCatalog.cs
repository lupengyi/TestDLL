using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public enum C95InputValueType
    {
        Float32,
        UInt16,
        Byte,
        PulseUInt32
    }

    public sealed class C95InputSignalDefinition
    {
        public C95InputSignalDefinition(int offset, string name, string portName, C95InputValueType valueType, string comment)
        {
            Offset = offset;
            Name = name;
            PortName = portName ?? string.Empty;
            ValueType = valueType;
            Comment = comment ?? string.Empty;
        }

        public int Offset { get; private set; }
        public string Name { get; private set; }
        public string PortName { get; private set; }
        public C95InputValueType ValueType { get; private set; }
        public string Comment { get; private set; }
        public bool ActiveLow { get { return Name.IndexOf('^') >= 0; } }
        public int Size { get { return ValueType == C95InputValueType.Byte ? 1 : ValueType == C95InputValueType.UInt16 ? 2 : 4; } }
    }

    public sealed class C95InputTableDefinition
    {
        public C95InputTableDefinition(string name, uint addressOffset, C95InputValueType valueType, IList<C95InputSignalDefinition> signals)
        {
            Name = name;
            AddressOffset = addressOffset;
            ValueType = valueType;
            Signals = new ReadOnlyCollection<C95InputSignalDefinition>(signals);
            ByteLength = signals.Count == 0 ? 0 : signals.Max(signal => signal.Offset + signal.Size);
        }

        public string Name { get; private set; }
        public uint AddressOffset { get; private set; }
        public C95InputValueType ValueType { get; private set; }
        public IReadOnlyList<C95InputSignalDefinition> Signals { get; private set; }
        public int ByteLength { get; private set; }
        public string AddressText { get { return "0x" + AddressOffset.ToString("X2", CultureInfo.InvariantCulture); } }
    }

    public sealed class C95InputSignalResult
    {
        private C95InputSignalResult(C95InputTableDefinition table, C95InputSignalDefinition signal, byte[] raw, double numericValue, string valueText, string interpretation)
        {
            TableName = table.Name;
            TableAddress = table.AddressText;
            SignalOffset = signal.Offset;
            SignalName = signal.Name;
            PortName = signal.PortName;
            DataType = signal.ValueType.ToString();
            RawBytes = HexDataParser.Format(raw);
            NumericValue = numericValue;
            ValueText = valueText;
            Interpretation = interpretation;
            Comment = signal.Comment;
        }

        public string TableName { get; private set; }
        public string TableAddress { get; private set; }
        public int SignalOffset { get; private set; }
        public string SignalName { get; private set; }
        public string PortName { get; private set; }
        public string DataType { get; private set; }
        public string RawBytes { get; private set; }
        public double NumericValue { get; private set; }
        public string ValueText { get; private set; }
        public string Interpretation { get; private set; }
        public string Comment { get; private set; }

        public static C95InputSignalResult Decode(C95InputTableDefinition table, C95InputSignalDefinition signal, byte[] tableData)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            if (tableData == null) throw new ArgumentNullException(nameof(tableData));
            if (signal.Offset < 0 || signal.Offset + signal.Size > tableData.Length) throw new ArgumentException("Signal bytes are outside the returned table data.", nameof(tableData));

            byte[] raw = tableData.Skip(signal.Offset).Take(signal.Size).ToArray();
            double numericValue;
            string valueText;
            string interpretation = string.Empty;
            switch (signal.ValueType)
            {
                case C95InputValueType.Float32:
                    numericValue = BitConverter.ToSingle(raw, 0);
                    valueText = numericValue.ToString("0.###", CultureInfo.InvariantCulture);
                    break;
                case C95InputValueType.UInt16:
                    numericValue = BitConverter.ToUInt16(raw, 0);
                    valueText = numericValue.ToString(CultureInfo.InvariantCulture);
                    break;
                case C95InputValueType.Byte:
                    numericValue = raw[0];
                    valueText = raw[0].ToString(CultureInfo.InvariantCulture);
                    if (signal.ActiveLow) interpretation = raw[0] == 0 ? "低电平有效：已触发" : raw[0] == 1 ? "低电平有效：未触发" : "非标准离散值";
                    break;
                case C95InputValueType.PulseUInt32:
                    uint pulse = BitConverter.ToUInt32(raw, 0);
                    ushort low = BitConverter.ToUInt16(raw, 0);
                    ushort high = BitConverter.ToUInt16(raw, 2);
                    numericValue = pulse;
                    valueText = pulse.ToString(CultureInfo.InvariantCulture);
                    interpretation = string.Format(CultureInfo.InvariantCulture, "High16={0}（频率），Low16={1}（占空比）", high, low);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return new C95InputSignalResult(table, signal, raw, numericValue, valueText, interpretation);
        }
    }

    public static class C95InputCatalog
    {
        private static readonly string[] AnalogRows =
        {
            "INVTRA_IOUTA_CURRENT_AI|AN2|", "INVTRA_IOUTB_CURRENT_AI|AN10|", "INVTRA_IOUTC_CURRENT_AI|AN18/P40.11|",
            "INVTRA_MTRTEMP1_AI|AN46|", "INVTRA_MTRATEMP2_AI|P34.3|Port name changed", "INVTRA_MTRATEMP1_RTN_AI|AN29/P40.14|Add",
            "INVTRA_MTRATEMP2_RTN_AI|AN28/P40.13|Add", "INVTRA_18V_HV_AI|AN24/P40.0|", "RED_INVTRA_IOUTA_CURRENT_AI|AN34|Add",
            "RED_INVTRA_IOUTB_CURRENT_AI|P00.10|Add", "RED_INVTRA_IOUTC_CURRENT_AI|P33.5|Add", "HVDC_SENSE_AI|AN6|",
            "KL15_AI|AN36/P40.6|Add", "15V_SAFETY_AI|P01.4|", "5V_SAFETY_AI|P01.3|", "15V_AI|AN40|",
            "5V_VCC1_AI|AN22|Add", "5V_VCC2_AI|P34.1|Add", "5V_LV_UP_AI|AN37/P40.7|Add", "SPEED_9V_AI|P33.3|Add",
            "SCALED_VBATT_AI|P00.3|", "VMUX_OUT_AI|AN38/P40.8|", "RES_SINHI_OC_VI|AN4|", "RES_SINLO_OC_VI|P33.0|",
            "RES_COSHI_OC_VI|AN3|", "RES_COSLO_OC_VI|P33.2|Port name changed", "RES_SIN_BIAS_AI|AN28/P40.13|Add",
            "RES_COS_BIAS_AI|AN29/P40.14|Add", "RES_EXC+_DIAG_AI|AN19/P40.12|", "RES_EXC-_DIAG_AI|AN11|",
            "RES_SINHI_DS|AN20|", "RES_SINLO_DS|AN21|", "RES_COSHI_DS|AN12|", "RES_COSLO_DS|AN13|",
            "RES_EXCHI_DS|P00.8|", "RES_EXCLO_DS|P00.7|", "RED_RES_SINHI_DS|AN44|", "RED_RES_SINLO_DS|AN45|",
            "RED_RES_COSHI_DS|AN14|", "RED_RES_COSLO_DS|AN15|", "MOT1_PHA_CURR_VI|AN0|Add", "MOT1_PHB_CURR_VI|AN8|Add",
            "MOT1_PHC_CURR_VI|AN16|Add", "MOT2_PHA_CURR_VI|P33.1|Add", "MOT2_PHB_CURR_VI|P00.6|Add",
            "MOT2_PHC_CURR_VI|AN38/P40.8|Add", "VBATT_MOT_P_FB_VI|AN9|Add", "ADC_TEST_AI|AN5|",
            "BOARD_TEMP_AI|AN30|", "COOLANT_IN_TEMP_AI|AN9|Port name changed", "COOLANT_OUT_TEMP_AI|AN43|",
            "OIL_TEMP_AI|AN42|Add", "OIL_TEMP_RTN_AI|AN47|Add", "LPS_IN_AI|AN43|Add", "SPEED_IN_AI|P33.6|Add",
            "HWID_AI|AN26/P40.2|Add", "VID_AI|AN27/P40.3|Add"
        };

        private static readonly string[] DiscreteRows =
        {
            "INVTRA_BUF1_FW_FB|P10.4|", "INVTRA_BUF2_ASC_LO_FB|P20.9|", "INVTRA_BUF3_ASC_HI_FB|P22.10|",
            "INVTRA_PHAOCFLT^|P32.2|Port name changed", "INVTRA_PHBOCFLT^|P21.4|Port name changed", "INVTRA_PHCOCFLT^|P22.4|Port name changed",
            "INVTRA_UVLO_FLT^F|P32.6|", "INVTRA_UVUP_FLT^F|P14.10|Port name changed", "INVTRA_DSATUP^|P11.7/P02.0|",
            "INVTRA_DSATLO^|P20.3|", "INVTRA_UVLO_FLT^F|NA|", "INVTRA_MASTER_FLT^|P11.0|", "INVTRA_SHUTOFF_FB|AN32/P40.4|",
            "HVDC_OV_FLT^|P23.7|Port name changed", "OV_FLT^|P15.5|Port name changed", "MOT1_ACUDRV_FLT^|P21.0|Add",
            "MOT2_ACUDRV_FLT^|P11.5/P00.9|Add", "SV_HSD_OUT1_FB_VI|P32.3|", "SV_HSD_OUT2_FB_VI|P23.2|",
            "DM_PTO_FB1|P14.1|Add", "DM_PTO_FB2|AN37/P40.7|Add", "DM_PTO_FB3|AN39/P40.9|Add", "DM_PTO_FB4|AN33/P40.5|Add",
            "HSD_1_STB|na|Add", "HSD_1_STG|na|Add", "HSD_1_OPEN|na|Add", "HSD_2_STB|na|Add", "HSD_2_STG|na|Add",
            "HSD_2_OPEN|na|Add", "LSD_1_STB|na|Add", "LSD_1_STG|na|Add", "LSD_1_OPEN|na|Add", "LSD_2_STB|na|Add",
            "LSD_2_STG|na|Add", "LSD_2_OPEN|na|Add", "A3944_UV|na|Add", "A3944_Over_Temperature|na|Add",
            "BLDC1_Hall_Supply_Error|na|Add", "BLDC1_Hall_Error|na|Add", "BLDC1_STB|na|Add", "BLDC1_STG|na|Add", "BLDC1_OPEN|na|Add",
            "BLDC2_Hall_Supply_Error|na|Add", "BLDC2_Hall_Error|na|Add", "BLDC2_STB|na|Add", "BLDC2_STG|na|Add", "BLDC2_OPEN|na|Add",
            "SV_HSD_ST1|P02.1|Add", "SV_HSD_ST2|P02.3|Add"
        };

        private static readonly string[] PulseRows =
        {
            "INVTRA_PWM_AHI_PI|P02.8|", "INVTRA_PWM_ALO_PI|P02.11|", "INVTRA_PWM_BHI_PI|P02.9|", "INVTRA_PWM_BLO_PI|P11.11|",
            "INVTRA_PWM_CHI_PI|P02.10|", "INVTRA_PWM_CLO_PI|P20.7|", "RES_EXC+_DIAG|P00.5|Port name changed", "RES_EXC-_DIAG|P14.9|",
            "MTR_ASC_SPEED|P22.1|Port name changed", "MTR_FW_SPEED|P32.7|Port name changed", "SPEED_IN_PI|P32.6|Add", "C3PS CLKOUT|P01.7|Add",
            "DS1_IN_PI/SENT|P00.2|Add", "DS2_IN_PI/SENT|P00.4|Add", "MOT1_BLDC_IF1|P00.12|Add", "MOT1_BLDC_IF2|P10.0|Add",
            "MOT1_BLDC_IF3|P23.0|Add", "MOT2_BLDC_IF1|P15.5|Add", "MOT2_BLDC_IF2|P11.1|Add", "MOT2_BLDC_IF3|P14.8|Add"
        };

        private static readonly string[] TemperatureRows =
        {
            "INVTRA_PH_AU_TEMP_PI|P23.4|Port name changed", "INVTRA_PH_BU_TEMP_PI|P15.7|", "INVTRA_PH_CU_TEMP_PI|P20.8|Port name changed",
            "INVTRA_PH_AL_TEMP_PI|P01.6|Port name changed", "INVTRA_PH_BL_TEMP_PI|P32.4|Port name changed", "INVTRA_PH_CL_TEMP_PI|P23.3|"
        };

        private static readonly IReadOnlyList<C95InputTableDefinition> Catalog = BuildCatalog();

        public static IReadOnlyList<C95InputTableDefinition> Tables { get { return Catalog; } }

        private static IReadOnlyList<C95InputTableDefinition> BuildCatalog()
        {
            return new ReadOnlyCollection<C95InputTableDefinition>(new List<C95InputTableDefinition>
            {
                Build("Analog Input Table", 0x00, C95InputValueType.Float32, AnalogRows, 4),
                Build("Ana In Counts Table", 0x0C, C95InputValueType.UInt16, AnalogRows, 2),
                Build("Discrete Input Table", 0x18, C95InputValueType.Byte, DiscreteRows, 1),
                Build("Pulse Input Table", 0x20, C95InputValueType.PulseUInt32, PulseRows, 4),
                Build("Inverter Temperature Table", 0x2C, C95InputValueType.Float32, TemperatureRows, 4)
            });
        }

        private static C95InputTableDefinition Build(string tableName, uint addressOffset, C95InputValueType valueType, string[] rows, int stride)
        {
            List<C95InputSignalDefinition> signals = new List<C95InputSignalDefinition>();
            for (int index = 0; index < rows.Length; index++)
            {
                string[] fields = rows[index].Split('|');
                signals.Add(new C95InputSignalDefinition(index * stride, fields[0], fields.Length > 1 ? fields[1] : string.Empty, valueType, fields.Length > 2 ? fields[2] : string.Empty));
            }
            return new C95InputTableDefinition(tableName, addressOffset, valueType, signals);
        }
    }
}
