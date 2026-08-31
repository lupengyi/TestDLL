using System;

namespace ManualCanDebug.Core
{
    public sealed class PreCurrentReadItem
    {
        public PreCurrentReadItem(
            string name,
            uint addressOffset,
            int tableIndex,
            int dataSize,
            string unit,
            string sourceName = "",
            bool activeLow = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A display name is required.", nameof(name));
            Name = name;
            AddressOffset = addressOffset;
            TableIndex = tableIndex;
            DataSize = dataSize;
            Unit = unit ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            ActiveLow = activeLow;
        }

        public string Name { get; private set; }
        public uint AddressOffset { get; private set; }
        public int TableIndex { get; private set; }
        public int DataSize { get; private set; }
        public string Unit { get; private set; }
        public string SourceName { get; private set; }
        public bool ActiveLow { get; private set; }
        public string AddressText
        {
            get { return string.Format("表0x{0:X2} / 字节{1} (0x{1:X})", AddressOffset, TableIndex); }
        }

        public string Interpret(double value)
        {
            if (!ActiveLow) return string.Empty;
            if (Math.Abs(value) < 0.000001) return "故障已触发（低电平有效：0=触发）";
            if (Math.Abs(value - 1) < 0.000001) return "故障未触发（低电平有效：1=未触发）";
            return "非标准状态值；该信号应为 0 或 1（低电平有效）";
        }
    }
}
