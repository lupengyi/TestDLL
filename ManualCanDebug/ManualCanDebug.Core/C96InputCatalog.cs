using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public enum C96InputValueType { Float32, UInt16, Byte }

    public sealed class C96InputSignalDefinition
    {
        public C96InputSignalDefinition(int offset, string name, string portName, C96InputValueType valueType)
        {
            Offset = offset; Name = name; PortName = portName ?? string.Empty; ValueType = valueType;
        }
        public int Offset { get; private set; }
        public string Name { get; private set; }
        public string PortName { get; private set; }
        public C96InputValueType ValueType { get; private set; }
        public int Size { get { return ValueType == C96InputValueType.Float32 ? 4 : ValueType == C96InputValueType.UInt16 ? 2 : 1; } }
        public bool ActiveLow { get { return Name.IndexOf('^') >= 0; } }
    }

    public sealed class C96InputTableDefinition
    {
        public C96InputTableDefinition(string name, uint addressOffset, IList<C96InputSignalDefinition> signals)
        {
            Name = name; AddressOffset = addressOffset; Signals = new ReadOnlyCollection<C96InputSignalDefinition>(signals);
            ByteLength = signals.Count == 0 ? 0 : signals.Max(signal => signal.Offset + signal.Size);
        }
        public string Name { get; private set; }
        public uint AddressOffset { get; private set; }
        public IReadOnlyList<C96InputSignalDefinition> Signals { get; private set; }
        public int ByteLength { get; private set; }
        public string AddressText { get { return "0x" + AddressOffset.ToString("X2", CultureInfo.InvariantCulture); } }
    }

    public sealed class C96InputSignalResult
    {
        private C96InputSignalResult() { }
        public string TableName { get; private set; }
        public string TableAddress { get; private set; }
        public int SignalOffset { get; private set; }
        public string SignalName { get; private set; }
        public string PortName { get; private set; }
        public string ValueType { get; private set; }
        public string ValueText { get; private set; }
        public string RawBytes { get; private set; }
        public string Interpretation { get; private set; }

        public static C96InputSignalResult Decode(C96InputTableDefinition table, C96InputSignalDefinition signal, byte[] data)
        {
            if (data == null || signal.Offset + signal.Size > data.Length) throw new ArgumentException("C96 input data is incomplete.", nameof(data));
            byte[] raw = data.Skip(signal.Offset).Take(signal.Size).ToArray();
            string value = signal.ValueType == C96InputValueType.Float32
                ? BitConverter.ToSingle(raw, 0).ToString("0.######", CultureInfo.InvariantCulture)
                : signal.ValueType == C96InputValueType.UInt16
                    ? BitConverter.ToUInt16(raw, 0).ToString(CultureInfo.InvariantCulture)
                    : raw[0].ToString(CultureInfo.InvariantCulture);
            return new C96InputSignalResult
            {
                TableName = table.Name,
                TableAddress = table.AddressText,
                SignalOffset = signal.Offset,
                SignalName = signal.Name,
                PortName = signal.PortName,
                ValueType = signal.ValueType.ToString(),
                ValueText = value,
                RawBytes = HexDataParser.Format(raw),
                Interpretation = signal.ActiveLow ? (raw[0] == 0 ? "低电平有效：已触发" : "低电平有效：未触发") : string.Empty
            };
        }
    }

    public static class C96InputCatalog
    {
        private static readonly string[] AnalogNames =
        {
            "15V_AI","15V_SAFETY_AI","5V_LV_UP_AI","5V_SAFETY_AI","ADC_TEST_AI","BOARD_TEMP_AI","COOLANT_IN_TEMP_AI","COOLANT_OUT_TEMP_AI","HVDC_SENSE_AI","HWID_AI","INVTRA_18V_HV_AI","INVTRA_IOUTA_CURRENT_AI","INVTRA_IOUTB_CURRENT_AI","INVTRA_IOUTC_CURRENT_AI","INVTRA_MTRATEMP1_AI","INVTRA_MTRATEMP1_RTN_AI","INVTRA_MTRATEMP2_AI","INVTRA_MTRATEMP2_RTN_AI","RED_INVTRB_IOUTA_CURRENT_AI","RED_INVTRB_IOUTB_CURRENT_AI","RED_INVTRB_IOUTC_CURRENT_AI","INVTRB_MTRATEMP1_AI","INVTRB_MTRATEMP1_RTN_AI","INVTRB_MTRATEMP2_AI","INVTRB_MTRATEMP2_RTN_AI","KL15_AI","RED_INVTRA_IOUTA_CURRENT_AI","RED_INVTRA_IOUTB_CURRENT_AI","RED_INVTRA_IOUTC_CURRENT_AI","INVTRB_IOUTA_CURRENT_AI","INVTRB_IOUTB_CURRENT_AI","INVTRB_IOUTC_CURRENT_AI","RED_RESA_COSHI_DS","RED_RESA_COSLO_DS","RED_RESA_SINHI_DS","RED_RESA_SINLO_DS","RED_RESB_COSHI_DS","RED_RESB_COSLO_DS","RED_RESB_SINHI_DS","RED_RESB_SINLO_DS","RESA_COSHI_DS","RESA_COSHI_OC_VI","RESA_COSLO_DS","RESA_COSLO_OC_VI","RESA_EXC-_DIAG_AI","RESA_EXC+_DIAG_AI","RESA_SINHI_DS","RESA_SINHI_OC_VI","RESA_SINLO_DS","RESA_SINLO_OC_VI","RESB_COSHI_DS","RESB_COSHI_OC_VI","RESB_COSLO_DS","RESB_COSLO_OC_VI","RESB_EXC-_DIAG_AI","RESB_EXC+_DIAG_AI","RESB_SINHI_DS","RESB_SINHI_OC_VI","RESB_SINLO_DS","RESB_SINLO_OC_VI","SCALED_VBATT_AI","VID_AI","VMUX_OUT_AI"
        };
        private static readonly string[] AnalogPorts =
        {
            "AN40","P01.4","AN37/P40.7","P01.3","AN5","AN30","AN9","AN43","AN6","AN26/P40.2","AN24/P40.0","AN2","AN10","AN18/P40.11","AN46","AN29/P40.14","P34.3","AN28/P40.13","AN17/P40.10","AN25/P40.1","AN41","AN8","AN39/P40.9","AN47","AN16","AN36/P40.6","AN34","P00.10","P33.5","AN33/P40.5","P00.11","P33.6","AN14","AN15","AN44","AN45","AN7","P33.4","P00.9","AN35","AN12","AN3","AN13","P33.2","AN11","AN19/P40.12","AN20","AN4","AN21","P33.0","P00.8","AN42","P00.7","AN23","AN1","AN0","P00.2","AN31","P00.1","AN22","P00.3","AN27/P40.3","AN38/P40.8"
        };
        private static readonly string[] DiscreteNames =
        {
            "HVDC_OV_FLT^","INVTRA_BUF1_FW_FB","INVTRA_BUF2_ASC_LO_FB","INVTRA_BUF3_ASC_HI_FB","INVTRA_DSATLO^","INVTRA_DSATUP^","INVTRA_MASTER_FLT^","INVTRA_PHAOCFLT^","INVTRA_PHBOCFLT^","INVTRA_PHCOCFLT^","INVTRA_SHUTOFF_FB","INVTRA_UVLO_FLT^F","INVTRA_UVUP_FLT^F","INVTRB_BUF1_FW_FB","INVTRB_BUF2_ASC_LO_FB","INVTRB_BUF3_ASC_HI_FB","INVTRB_DSATLO^","INVTRB_DSATUP^","INVTRB_MASTER_FLT^","INVTRB_PHAOCFLT^","INVTRB_PHBOCFLT^","INVTRB_PHCOCFLT^","INVTRB_SHUTOFF_FB","INVTRB_UVLO_FLT^F","INVTRB_UVUP_FLT^F","OV_FLT^"
        };
        private static readonly string[] PulseNames =
        {
            "INVTRA_PWM_AHI_PI_Duty_Cycle","INVTRA_PWM_AHI_PI_Frequency","INVTRA_PWM_ALO_PI_Duty_Cycle","INVTRA_PWM_ALO_PI_Frequency","INVTRA_PWM_BHI_PI_Duty_Cycle","INVTRA_PWM_BHI_PI_Frequency","INVTRA_PWM_BLO_PI_Duty_Cycle","INVTRA_PWM_BLO_PI_Frequency","INVTRA_PWM_CHI_PI_Duty_Cycle","INVTRA_PWM_CHI_PI_Frequency","INVTRA_PWM_CLO_PI_Duty_Cycle","INVTRA_PWM_CLO_PI_Frequency","INVTRB_PWM_AHI_PI_Duty_Cycle","INVTRB_PWM_AHI_PI_Frequency","INVTRB_PWM_ALO_PI_Duty_Cycle","INVTRB_PWM_ALO_PI_Frequency","INVTRB_PWM_BHI_PI_Duty_Cycle","INVTRB_PWM_BHI_PI_Frequency","INVTRB_PWM_BLO_PI_Duty_Cycle","INVTRB_PWM_BLO_PI_Frequency","INVTRB_PWM_CHI_PI_Duty_Cycle","INVTRB_PWM_CHI_PI_Frequency","INVTRB_PWM_CLO_PI_Duty_Cycle","INVTRB_PWM_CLO_PI_Frequency"
        };
        private static readonly string[] TemperatureNames =
        {
            "INVTRA_PH_AL_TEMP_PI","INVTRA_PH_AU_TEMP_PI","INVTRA_PH_BL_TEMP_PI","INVTRA_PH_BU_TEMP_PI","INVTRA_PH_CL_TEMP_PI","INVTRA_PH_CU_TEMP_PI","INVTRB_PH_AL_TEMP_PI","INVTRB_PH_AU_TEMP_PI","INVTRB_PH_BL_TEMP_PI","INVTRB_PH_BU_TEMP_PI","INVTRB_PH_CL_TEMP_PI","INVTRB_PH_CU_TEMP_PI"
        };
        private static readonly string[] FrequencyNames =
        {
            "RESA_EXC+_DIAG_Frequency","RESA_EXC-_DIAG_Frequency","RESB_EXC+_DIAG_Frequency","RESB_EXC-_DIAG_Frequency"
        };

        private static readonly IReadOnlyList<C96InputTableDefinition> Catalog = new ReadOnlyCollection<C96InputTableDefinition>(new List<C96InputTableDefinition>
        {
            new C96InputTableDefinition("Analog Inputs", 0x00, Linear(AnalogNames, AnalogPorts, 4, C96InputValueType.Float32)),
            new C96InputTableDefinition("Analog Counts", 0x0C, Linear(AnalogNames, AnalogPorts, 2, C96InputValueType.UInt16)),
            new C96InputTableDefinition("Discrete Inputs", 0x18, Linear(DiscreteNames, null, 4, C96InputValueType.Byte)),
            new C96InputTableDefinition("Pulse Inputs", 0x20, Linear(PulseNames, null, 2, C96InputValueType.UInt16)),
            new C96InputTableDefinition("Phase Temperatures", 0x2C, Linear(TemperatureNames, null, 4, C96InputValueType.Float32)),
            new C96InputTableDefinition("Resolver Frequencies", 0xB0, Linear(FrequencyNames, null, 2, C96InputValueType.UInt16))
        });

        public static IReadOnlyList<C96InputTableDefinition> Tables { get { return Catalog; } }

        private static IList<C96InputSignalDefinition> Linear(string[] names, string[] ports, int step, C96InputValueType type)
        {
            List<C96InputSignalDefinition> result = new List<C96InputSignalDefinition>();
            for (int index = 0; index < names.Length; index++)
                result.Add(new C96InputSignalDefinition(index * step, names[index], ports == null ? string.Empty : ports[index], type));
            return result;
        }
    }
}
