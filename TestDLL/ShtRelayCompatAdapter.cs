using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;

namespace CSP
{
    internal sealed class ShtRelayCompatAdapter
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private ushort _transaction;
        public byte SlaveAddress { get; set; } = 1;

        public void Connect(string address, ushort port, string ignoredMode)
        {
            Disconnect(); TcpClient client = new TcpClient(); IAsyncResult pending = client.BeginConnect(address, port, null, null); if (!pending.AsyncWaitHandle.WaitOne(2000)) { client.Close(); throw new TimeoutException("继电器板连接超时：" + address + ":" + port); } client.EndConnect(pending); client.ReceiveTimeout = 2000; client.SendTimeout = 2000; _client = client; _stream = client.GetStream();
        }

        public void Disconnect()
        {
            try { if (_stream != null) _stream.Dispose(); } finally { _stream = null; if (_client != null) _client.Close(); _client = null; }
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
                WriteSingleCoil(channel, value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
                Thread.Sleep(20);
            }
        }

        private void WriteSingleCoil(ushort channel, bool enabled)
        {
            if (_stream == null || _client == null || !_client.Connected) throw new InvalidOperationException("继电器板尚未连接。"); ushort transaction = unchecked(++_transaction); byte[] request = { (byte)(transaction >> 8), (byte)transaction, 0, 0, 0, 6, SlaveAddress, 5, (byte)(channel >> 8), (byte)channel, enabled ? (byte)0xFF : (byte)0, 0 }; _stream.Write(request, 0, request.Length); byte[] response = new byte[12]; int read = 0; while (read < response.Length) { int count = _stream.Read(response, read, response.Length - read); if (count <= 0) throw new InvalidOperationException("继电器板连接已关闭。"); read += count; } if (response[0] != request[0] || response[1] != request[1] || response[7] != 5 || response[8] != request[8] || response[9] != request[9]) throw new InvalidOperationException("继电器板返回了无效的Modbus响应。");
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
