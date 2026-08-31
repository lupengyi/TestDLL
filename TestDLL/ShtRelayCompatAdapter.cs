using System;
using System.Globalization;

namespace CSP
{
    internal sealed class ShtRelayCompatAdapter
    {
        private readonly SHT_48SEDO_A.SHT_48SEDO_A _board = new SHT_48SEDO_A.SHT_48SEDO_A();
        public byte SlaveAddress { get; set; } = 1;

        public void Connect(string address, ushort port, string ignoredMode)
        {
            _board.connect(address, port);
        }

        public void Disconnect()
        {
            _board.disConnect();
        }

        public void WriteDO(string channels, string values)
        {
            string[] channelItems = (channels ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string[] valueItems = (values ?? string.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (channelItems.Length == 0 || channelItems.Length != valueItems.Length) throw new InvalidOperationException("Relay channels and values must have the same non-zero count.");
            for (int index = 0; index < channelItems.Length; index++)
            {
                ushort channel = ParseChannel(channelItems[index]);
                string value = valueItems[index].Trim();
                _board.WriteSingleCoil(SlaveAddress, channel, value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
        }

        private static ushort ParseChannel(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.StartsWith("OUT", StringComparison.OrdinalIgnoreCase))
            {
                int displayNumber = int.Parse(value.Substring(3), CultureInfo.InvariantCulture);
                if (displayNumber < 1 || displayNumber > 48) throw new ArgumentOutOfRangeException(nameof(text), "OUT端口必须为OUT1到OUT48。");
                return (ushort)(displayNumber - 1);
            }
            ushort raw = ushort.Parse(value, CultureInfo.InvariantCulture);
            if (raw > 47) throw new ArgumentOutOfRangeException(nameof(text), "底层通道必须为0到47。");
            return raw;
        }
    }
}
