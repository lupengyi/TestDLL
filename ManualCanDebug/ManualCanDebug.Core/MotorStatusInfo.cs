using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ManualCanDebug.Core
{
    public sealed class MotorStatusInfo
    {
        private static readonly string[][] FaultNames =
        {
            new[] { "A相过流", "B相过流", "C相过流", "A相硬件过流", "B相硬件过流", "C相硬件过流", "母线欠压", "母线过压" },
            new[] { "母线硬件过压", "板温故障", "AL1相温度故障", "BL1相温度故障", "CL1相温度故障", "AU1相温度故障", "BU1相温度故障", "CU1相温度故障" },
            new[] { "电机1温度故障", "HI退饱和故障", "电机2温度故障", "CHI退饱和故障", "LO退饱和故障", "BLO退饱和故障", "CLO退饱和故障", "上桥臂欠压" },
            new[] { "下桥臂欠压", "主故障置位", "电机超速", "保留位3", "保留位4", "保留位5", "保留位6", "保留位7" }
        };

        private MotorStatusInfo(byte[] raw, string rampModeDescription, string sequenceStatusDescription, IList<string> activeFaults)
        {
            Raw = raw;
            RawText = HexDataParser.Format(raw);
            RampModeDescription = rampModeDescription;
            SequenceStatusDescription = sequenceStatusDescription;
            RampMode = raw[0];
            SequenceStatus = raw[1];
            ActiveFaults = new ReadOnlyCollection<string>(activeFaults);
            FaultDescription = activeFaults.Count == 0 ? "无故障位" : string.Join("、", activeFaults);
            Summary = string.Format("Ramp={0}；Status={1}；Fault={2}", rampModeDescription, sequenceStatusDescription, FaultDescription);
        }

        public byte[] Raw { get; private set; }
        public string RawText { get; private set; }
        public string RampModeDescription { get; private set; }
        public string SequenceStatusDescription { get; private set; }
        public string FaultDescription { get; private set; }
        public byte RampMode { get; private set; }
        public byte SequenceStatus { get; private set; }
        public IReadOnlyList<string> ActiveFaults { get; private set; }
        public string Summary { get; private set; }

        public static MotorStatusInfo Parse(byte[] status)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (status.Length < 8) throw new ArgumentException("Motor Status must contain at least eight bytes.", nameof(status));

            List<string> faults = new List<string>();
            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                byte value = status[4 + byteIndex];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((value & (1 << bit)) != 0) faults.Add(FaultNames[byteIndex][bit]);
                }
            }

            return new MotorStatusInfo(
                (byte[])status.Clone(),
                DecodeRampMode(status[0]),
                DecodeSequenceStatus(status[1]),
                faults);
        }

        private static string DecodeRampMode(byte value)
        {
            switch (value)
            {
                case 0: return "初始化(0)";
                case 1: return "上升斜坡(1)";
                case 2: return "保持(2)";
                case 3: return "下降斜坡(3)";
                case 4: return "完成(4)";
                case 5: return "新电流上升(5)";
                case 6: return "新电流下降(6)";
                default: return "未知(" + value + ")";
            }
        }

        private static string DecodeSequenceStatus(byte value)
        {
            switch (value)
            {
                case 0: return "初始化(0)";
                case 1: return "运行中(1)";
                case 2: return "成功完成(2)";
                case 3: return "诊断故障(3)";
                case 4: return "参数故障(4)";
                case 5: return "手动覆盖(5)";
                default: return "未知(" + value + ")";
            }
        }
    }
}
