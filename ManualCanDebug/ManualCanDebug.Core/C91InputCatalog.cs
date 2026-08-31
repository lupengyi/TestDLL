using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public sealed class C91InputSignalDefinition
    {
        public C91InputSignalDefinition(int offset, string name, string valueType)
        {
            Offset = offset;
            Name = name;
            ValueType = valueType;
        }

        public int Offset { get; private set; }
        public string Name { get; private set; }
        public string ValueType { get; private set; }
        public bool ActiveLow { get { return Name.EndsWith("^", StringComparison.Ordinal); } }
    }

    public sealed class C91InputTableDefinition
    {
        public C91InputTableDefinition(string name, uint addressOffset, int byteLength, IList<C91InputSignalDefinition> signals)
        {
            Name = name;
            AddressOffset = addressOffset;
            ByteLength = byteLength;
            Signals = new ReadOnlyCollection<C91InputSignalDefinition>(signals);
        }

        public string Name { get; private set; }
        public uint AddressOffset { get; private set; }
        public int ByteLength { get; private set; }
        public IReadOnlyList<C91InputSignalDefinition> Signals { get; private set; }
        public string AddressText { get { return "0x" + AddressOffset.ToString("X2", CultureInfo.InvariantCulture); } }
    }

    public sealed class C91InputSignalResult
    {
        private C91InputSignalResult() { }

        public string TableName { get; private set; }
        public string TableOffset { get; private set; }
        public int SignalOffset { get; private set; }
        public string SignalName { get; private set; }
        public string ValueType { get; private set; }
        public string ValueText { get; private set; }
        public string RawBytes { get; private set; }
        public string Interpretation { get; private set; }

        public static C91InputSignalResult Decode(C91InputTableDefinition table, C91InputSignalDefinition signal, byte[] data)
        {
            int size = SizeOf(signal.ValueType);
            if (data == null || signal.Offset < 0 || signal.Offset + size > data.Length)
                throw new ArgumentException("C91 input data is incomplete.", nameof(data));
            byte[] raw = data.Skip(signal.Offset).Take(size).ToArray();
            string value;
            switch (signal.ValueType)
            {
                case "Float32": value = BitConverter.ToSingle(raw, 0).ToString("0.######", CultureInfo.InvariantCulture); break;
                case "UInt16": value = BitConverter.ToUInt16(raw, 0).ToString(CultureInfo.InvariantCulture); break;
                case "UInt32": value = BitConverter.ToUInt32(raw, 0).ToString(CultureInfo.InvariantCulture); break;
                default: value = raw[0].ToString(CultureInfo.InvariantCulture); break;
            }
            return new C91InputSignalResult
            {
                TableName = table.Name,
                TableOffset = table.AddressText,
                SignalOffset = signal.Offset,
                SignalName = signal.Name,
                ValueType = signal.ValueType,
                ValueText = value,
                RawBytes = HexDataParser.Format(raw),
                Interpretation = signal.ActiveLow ? (raw[0] == 0 ? "低电平有效：已触发" : "低电平有效：未触发") : string.Empty
            };
        }

        private static int SizeOf(string valueType)
        {
            switch (valueType)
            {
                case "Float32": case "UInt32": return 4;
                case "UInt16": return 2;
                default: return 1;
            }
        }
    }

    public static class C91InputCatalog
    {
        private static readonly string[] AnalogNames =
        {
            "HVDC_SELECT_AI", "ADC_TEST_AI", "INVTRA_IOUTA_CURRENT_AI", "RESA_COSHI_OC_VI", "RESA_SINHI_OC_VI",
            "INVTRA_IOUTB_CURRENT_AI", "RESA_EXC-_DIAG_AI", "VREF2_AI", "RESA_COSLO_OC_VI", "RESA_SINLO_OC_VI",
            "INVTRA_MTRATEMP2_AI", "VREF4_AI", "INVTRA_IOUTC_CURRENT_AI", "RESA_EXC+_DIAG_AI", "INVTRA_18V_HV_AI",
            "RESA_SIN_BIAS_AI", "RESA_COS_BIAS_AI", "BOARD_TEMP_AI", "INVTRA_MTRATEMP1_RTN_AI", "INVTRA_MTRATEMP2_RTN_AI",
            "INVTRA_MTRTEMP1_AI", "PSR_AI", "VCC_AI", "15V_AI", "VMUX_OUT_AI", "COOLANT_IN_TEMP_AI",
            "COOLANT_OUT_TEMP_AI", "VREF3_AI", "15V_SAFETY_AI", "5V_SAFETY_AI", "VREF4_AI", "VBATT_MOT_FB_VI",
            "SCALED_VBATT_AI", "MOT_PHB_CURR_VI", "RESA_EXCLO_DS", "RESA_EXCHI_DS", "RED_RESA_SINLO_DS",
            "RED_RESA_SINHI_DS", "RESA_SINLO_DS", "RESA_SINHI_DS", "RED_RESA_COSLO_DS", "RED_RESA_COSHI_DS",
            "RESA_COSLO_DS", "RESA_COSHI_DS", "INVTRA_MTRTEMP_RTN_AI", "INVTRA_OILTEMP_RTN_AI", "INVTRA_HVDC_SENSE2_AI",
            "CS_5V_SAFETY_AI", "VREF1_FB_AI", "INVTRA_CS_5V_SAFETY_RTN_FB_AI", "VREF1_AI", "5V_LV_UP_AI",
            "RED_INVTRA_IOUTA_CURRENT_AI", "RED_INVTRA_IOUTB_CURRENT_AI", "RED_INVTRA_IOUTC_CURRENT_AI"
        };

        private static readonly string[] DiscreteNames =
        {
            "ASC_FW_SELECT", "HVDC_OV_FLT^", "INVTRA_HV_ASC_STAT", "INVTRA_ASC_FW_FB", "INVTRA_BUF1_FW_FB",
            "INVTRA_BUF2_ASC_LO_FB", "INVTRA_BUF3_ASC_HI_FB", "INVTRA_DSATPHAHI^", "INVTRA_DSATPHBHI^",
            "INVTRA_DSATPHCHI^", "INVTRA_DSATPHALO^", "INVTRA_DSATPHBLO^", "INVTRA_DSATPHCLO^",
            "INVTRA_PHAOCFLT^", "INVTRA_PHBOCFLT^", "INVTRA_PHCOCFLT^", "INVTRA_SHUTOFF_FB", "INVTRA_UVLO^",
            "INVTRA_UVUP^", "OV_FLT^", "R2D_RES_DOS", "R2D_RES_LOT", "INVTRA_MASTER_FLT^", "CRASH_HI_AI",
            "CRASH_LO_AI", "ACUDRV_FLT^", "ALM_FLT^"
        };

        private static readonly string[] PulseNames =
        {
            "INVTRA_PWM_AHI_PI", "INVTRA_PWM_ALO_PI", "INVTRA_PWM_BHI_PI", "INVTRA_PWM_BLO_PI",
            "INVTRA_PWM_CHI_PI", "INVTRA_PWM_CLO_PI", "OPTICAL_ENCODER_A_PI", "OPTICAL_ENCODER_R_PI",
            "OPTICAL_ENCODER_B_PI", "FORK_PWM", "ATO_SYNC_1", "ATO_SYNC_2"
        };

        private static readonly string[] TemperatureNames =
        {
            "INVTRA_PH_AU_TEMP_PI", "INVTRA_PH_BU_TEMP_PI", "INVTRA_PH_CU_TEMP_PI",
            "INVTRA_PH_AL_TEMP_PI", "INVTRA_PH_BL_TEMP_PI", "INVTRA_PH_CL_TEMP_PI"
        };

        private static readonly IReadOnlyList<C91InputTableDefinition> Catalog = new ReadOnlyCollection<C91InputTableDefinition>(new List<C91InputTableDefinition>
        {
            new C91InputTableDefinition("Analog Input", 0x00, AnalogNames.Length * 4, Linear(AnalogNames, 4, "Float32")),
            new C91InputTableDefinition("Analog Counts", 0x0C, AnalogNames.Length * 2, Linear(AnalogNames, 2, "UInt16")),
            new C91InputTableDefinition("Discrete Input", 0x18, DiscreteNames.Length, Linear(DiscreteNames, 1, "Byte")),
            new C91InputTableDefinition("Pulse Input", 0x20, PulseNames.Length * 4, Linear(PulseNames, 4, "UInt32")),
            new C91InputTableDefinition("Inverter Temperature", 0x2C, TemperatureNames.Length * 4, Linear(TemperatureNames, 4, "Float32"))
        });

        public static IReadOnlyList<C91InputTableDefinition> Tables { get { return Catalog; } }

        private static IList<C91InputSignalDefinition> Linear(string[] names, int step, string valueType)
        {
            List<C91InputSignalDefinition> result = new List<C91InputSignalDefinition>();
            for (int index = 0; index < names.Length; index++) result.Add(new C91InputSignalDefinition(index * step, names[index], valueType));
            return result;
        }
    }
}
