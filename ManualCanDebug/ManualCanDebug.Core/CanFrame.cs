using System;

namespace ManualCanDebug.Core
{
    public sealed class CanFrame
    {
        public CanFrame(uint id, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length > 8) throw new ArgumentException("Classic CAN data cannot exceed eight bytes.", nameof(data));
            Id = id;
            Data = (byte[])data.Clone();
        }

        public uint Id { get; private set; }
        public byte[] Data { get; private set; }
    }
}
