using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public enum C96Drive
    {
        TM1,
        TM2
    }

    public sealed class C96DriveProfile
    {
        private C96DriveProfile(C96Drive drive, uint resolverOffset, uint motorControlOffset, uint motorStatusOffset,
            uint currentCommandOffset, uint currentResultOffset, uint rpmOffset, uint expectedLoadOffset,
            uint autoPwmOffset, uint runInCommandOffset, uint runInStatusOffset)
        {
            Drive = drive;
            ResolverOffset = resolverOffset;
            MotorControlOffset = motorControlOffset;
            MotorStatusOffset = motorStatusOffset;
            CurrentCommandOffset = currentCommandOffset;
            CurrentResultOffset = currentResultOffset;
            RpmOffset = rpmOffset;
            ExpectedLoadOffset = expectedLoadOffset;
            AutoPwmOffset = autoPwmOffset;
            RunInCommandOffset = runInCommandOffset;
            RunInStatusOffset = runInStatusOffset;
        }

        public C96Drive Drive { get; private set; }
        public uint ResolverOffset { get; private set; }
        public uint MotorControlOffset { get; private set; }
        public uint MotorStatusOffset { get; private set; }
        public uint CurrentCommandOffset { get; private set; }
        public uint CurrentResultOffset { get; private set; }
        public uint RpmOffset { get; private set; }
        public uint ExpectedLoadOffset { get; private set; }
        public uint AutoPwmOffset { get; private set; }
        public uint RunInCommandOffset { get; private set; }
        public uint RunInStatusOffset { get; private set; }
        public int ResolverLength { get { return 9; } }
        public int MotorControlLength { get { return 39; } }
        public int MotorStatusLength { get { return 10; } }
        public int CurrentResultLength { get { return 40; } }
        public int RpmLength { get { return 16; } }

        public static C96DriveProfile For(C96Drive drive)
        {
            if (drive == C96Drive.TM1)
                return new C96DriveProfile(drive, 0x44, 0x68, 0x6C, 0x78, 0x7C, 0x98, 0xBC, 0xD4, 0xDC, 0xE0);
            if (drive == C96Drive.TM2)
                return new C96DriveProfile(drive, 0x48, 0x80, 0x84, 0x90, 0x94, 0xA8, 0xC0, 0xD8, 0xE4, 0xE8);
            throw new ArgumentOutOfRangeException(nameof(drive));
        }
    }

    /// <summary>
    /// FT_Enables (0x3C) UV fault-reset ports from C92/C96 Locator.
    /// First PSR power-on cycle needs a High pulse then Low to clear UVLO/UVUP latches.
    /// </summary>
    public static class C96FtEnables
    {
        public const uint TableOffset = 0x3C;
        public const int SharedBusOverVoltageResetIndex = 7;
        public const string SharedBusOverVoltageResetSignalName = "INVTRA_FLTRST_OV";

        public static int OverCurrentResetIndex(C96Drive drive)
        {
            if (drive == C96Drive.TM1) return 4;
            if (drive == C96Drive.TM2) return 20;
            throw new ArgumentOutOfRangeException(nameof(drive));
        }

        public static string OverCurrentResetSignalName(C96Drive drive)
        {
            if (drive == C96Drive.TM1) return "INVTRA_FLT_RST_OC";
            if (drive == C96Drive.TM2) return "INVTRB_FLT_RST_OC";
            throw new ArgumentOutOfRangeException(nameof(drive));
        }

        public static int UvloResetIndex(C96Drive drive)
        {
            if (drive == C96Drive.TM1) return 8;
            if (drive == C96Drive.TM2) return 22;
            throw new ArgumentOutOfRangeException(nameof(drive));
        }

        public static int UvupResetIndex(C96Drive drive)
        {
            if (drive == C96Drive.TM1) return 9;
            if (drive == C96Drive.TM2) return 23;
            throw new ArgumentOutOfRangeException(nameof(drive));
        }

        public static string UvloSignalName(C96Drive drive)
        {
            return drive == C96Drive.TM1 ? "INVTRA_FLTRST_UVLO" : "INVTRB_FLTRST_UVLO";
        }

        public static string UvupSignalName(C96Drive drive)
        {
            return drive == C96Drive.TM1 ? "INVTRA_FLTRST_UVUP" : "INVTRB_FLTRST_UVUP";
        }
    }

    public sealed class C96MotorControlCommand
    {
        public C96MotorControlCommand(float startCurrentRms, float targetCurrentRms, float stepPeakAmps,
            float holdSeconds, float outputFrequencyHz, byte mode, ushort rampTimeMs, ushort baseFrequencyHz,
            bool gateEnable, bool resetMotorFaults, bool speedControlEnable, float speedSetpointRpm,
            bool voltageControlEnable, float voltageSetpoint)
        {
            StartCurrentRms = startCurrentRms;
            TargetCurrentRms = targetCurrentRms;
            StepPeakAmps = stepPeakAmps;
            HoldSeconds = holdSeconds;
            OutputFrequencyHz = outputFrequencyHz;
            Mode = mode;
            RampTimeMs = rampTimeMs;
            BaseFrequencyHz = baseFrequencyHz;
            GateEnable = gateEnable;
            ResetMotorFaults = resetMotorFaults;
            SpeedControlEnable = speedControlEnable;
            SpeedSetpointRpm = speedSetpointRpm;
            VoltageControlEnable = voltageControlEnable;
            VoltageSetpoint = voltageSetpoint;
        }

        public float StartCurrentRms { get; private set; }
        public float TargetCurrentRms { get; private set; }
        public float StepPeakAmps { get; private set; }
        public float HoldSeconds { get; private set; }
        public float OutputFrequencyHz { get; private set; }
        public byte Mode { get; private set; }
        public ushort RampTimeMs { get; private set; }
        public ushort BaseFrequencyHz { get; private set; }
        public bool GateEnable { get; private set; }
        public bool ResetMotorFaults { get; private set; }
        public bool SpeedControlEnable { get; private set; }
        public float SpeedSetpointRpm { get; private set; }
        public bool VoltageControlEnable { get; private set; }
        public float VoltageSetpoint { get; private set; }
    }

    public sealed class C96ResolverResult
    {
        private C96ResolverResult(C96Drive drive, float speedRpm, float angleDegrees, byte faultCode, byte[] raw)
        {
            Drive = drive;
            SpeedRpm = speedRpm;
            AngleDegrees = angleDegrees;
            FaultCode = faultCode;
            RawBytes = HexDataParser.Format(raw);
        }

        public C96Drive Drive { get; private set; }
        public float SpeedRpm { get; private set; }
        public float AngleDegrees { get; private set; }
        public byte FaultCode { get; private set; }
        public string RawBytes { get; private set; }
        public string FaultDescription
        {
            get
            {
                switch (FaultCode)
                {
                    case 0: return "Loss of Signal";
                    case 1: return "Degradation of Signal";
                    case 2: return "Loss of Tracking";
                    case 3: return "No Fault";
                    default: return "Unknown(" + FaultCode.ToString(CultureInfo.InvariantCulture) + ")";
                }
            }
        }

        public static C96ResolverResult Parse(C96Drive drive, byte[] data)
        {
            if (data == null || data.Length < 9) throw new ArgumentException("Dual-drive resolver data must contain nine bytes.", nameof(data));
            return new C96ResolverResult(drive, BitConverter.ToSingle(data, 0), BitConverter.ToSingle(data, 4), data[8], data.Take(9).ToArray());
        }
    }

    public sealed class C96CurrentResult
    {
        private C96CurrentResult(C96Drive drive, IList<DutPhaseCurrent> phases, float reportedRms, byte[] raw)
        {
            Drive = drive;
            Phases = new ReadOnlyCollection<DutPhaseCurrent>(phases);
            ReportedRms = reportedRms;
            RawBytes = HexDataParser.Format(raw);
        }

        public C96Drive Drive { get; private set; }
        public IReadOnlyList<DutPhaseCurrent> Phases { get; private set; }
        public float ReportedRms { get; private set; }
        public string RawBytes { get; private set; }

        public static C96CurrentResult Parse(C96Drive drive, byte[] data)
        {
            if (data == null || data.Length < 40) throw new ArgumentException("Dual-drive current result must contain ten floats.", nameof(data));
            float[] values = Enumerable.Range(0, 10).Select(index => BitConverter.ToSingle(data, index * 4)).ToArray();
            return new C96CurrentResult(drive, new List<DutPhaseCurrent>
            {
                new DutPhaseCurrent("A", values[0], values[3], values[6]),
                new DutPhaseCurrent("B", values[1], values[4], values[7]),
                new DutPhaseCurrent("C", values[2], values[5], values[8])
            }, values[9], data.Take(40).ToArray());
        }
    }

    public sealed class C96MotorStatusInfo
    {
        private static readonly string[][] FaultNames =
        {
            new[] { "Phase A over-current", "Phase B over-current", "Phase C over-current", "Phase A HW over-current", "Phase B HW over-current", "Phase C HW over-current", "Phase A upper temperature", "Phase A lower temperature" },
            new[] { "Phase B upper temperature", "Phase B lower temperature", "Phase C upper temperature", "Phase C lower temperature", "Motor temp 1", "Motor temp 2", "Motor temp 3 reserved", "Desat upper" },
            new[] { "Desat lower", "UV A upper", "UV B upper", "UV C upper", "UV A lower", "UV B lower", "UV C lower", "Master fault" },
            new[] { "Bus under-voltage", "Bus over-voltage", "Bus HW over-voltage", "Bus HW over-voltage latched", "All upper UV latched", "All lower UV latched", "Zero-sequence over-current", "UV upper" },
            new[] { "UV lower", "Board temperature", "Reserved byte4 bit2", "Reserved byte4 bit3", "Reserved byte4 bit4", "Reserved byte4 bit5", "Reserved byte4 bit6", "Reserved byte4 bit7" }
        };

        private C96MotorStatusInfo(C96Drive drive, byte[] raw, IList<string> activeFaults)
        {
            Drive = drive;
            Raw = raw;
            RawText = HexDataParser.Format(raw);
            RampMode = raw[8];
            SequenceStatus = raw[9];
            ActiveFaults = new ReadOnlyCollection<string>(activeFaults);
            FaultDescription = activeFaults.Count == 0 ? "No active fault bits" : string.Join("; ", activeFaults);
            Summary = string.Format(CultureInfo.InvariantCulture, "Ramp={0}; Status={1}; Fault={2}", RampMode, SequenceStatus, FaultDescription);
        }

        public C96Drive Drive { get; private set; }
        public byte[] Raw { get; private set; }
        public string RawText { get; private set; }
        public byte RampMode { get; private set; }
        public byte SequenceStatus { get; private set; }
        public IReadOnlyList<string> ActiveFaults { get; private set; }
        public string FaultDescription { get; private set; }
        public string Summary { get; private set; }

        public static C96MotorStatusInfo Parse(C96Drive drive, byte[] data)
        {
            if (data == null || data.Length < 10) throw new ArgumentException("Dual-drive motor status must contain ten bytes.", nameof(data));
            List<string> faults = new List<string>();
            for (int byteIndex = 0; byteIndex < FaultNames.Length; byteIndex++)
                for (int bit = 0; bit < 8; bit++)
                    if ((data[byteIndex] & (1 << bit)) != 0) faults.Add(FaultNames[byteIndex][bit]);
            return new C96MotorStatusInfo(drive, data.Take(10).ToArray(), faults);
        }
    }

    public sealed class C96DriveSnapshot
    {
        public C96DriveSnapshot(C96Drive drive, C96ResolverResult resolver, C96CurrentResult current,
            C96MotorStatusInfo motorStatus, float rpm, float rpmMaximum, float rpmMinimum, string rpmRaw)
        {
            Drive = drive;
            Resolver = resolver;
            Current = current;
            MotorStatus = motorStatus;
            Rpm = rpm;
            RpmMaximum = rpmMaximum;
            RpmMinimum = rpmMinimum;
            RpmRaw = rpmRaw;
        }

        public C96Drive Drive { get; private set; }
        public C96ResolverResult Resolver { get; private set; }
        public C96CurrentResult Current { get; private set; }
        public C96MotorStatusInfo MotorStatus { get; private set; }
        public float Rpm { get; private set; }
        public float RpmMaximum { get; private set; }
        public float RpmMinimum { get; private set; }
        public string RpmRaw { get; private set; }
    }
}
