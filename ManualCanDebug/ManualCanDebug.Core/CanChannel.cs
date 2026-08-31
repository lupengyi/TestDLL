using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Instruments.CAN;

namespace ManualCanDebug.Core
{
    internal sealed class CanChannel : IDisposable
    {
        private readonly CanChannelConfig _config;
        private readonly string _runtimeDirectory;
        private readonly string _dbcPath;
        private CANWrapper _wrapper;

        public CanChannel(CanChannelConfig config, string runtimeDirectory, string dbcPath)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _runtimeDirectory = runtimeDirectory ?? throw new ArgumentNullException(nameof(runtimeDirectory));
            _dbcPath = dbcPath ?? throw new ArgumentNullException(nameof(dbcPath));
        }

        public bool IsConnected
        {
            get { return _wrapper != null; }
        }

        public void Connect()
        {
            if (IsConnected) return;

            string providerPath = Path.Combine(_runtimeDirectory, "Instruments.CAN.ZLG_CAN.dll");
            if (!File.Exists(providerPath))
            {
                throw new FileNotFoundException("ZLG CAN provider was not found.", providerPath);
            }

            if (!File.Exists(_dbcPath))
            {
                throw new FileNotFoundException("DBC file was not found.", _dbcPath);
            }

            CANWrapper wrapper = new CANWrapper(providerPath);
            wrapper.SetValue("IP", _config.Ip);
            wrapper.SetValue("PORT", _config.Port.ToString(CultureInfo.InvariantCulture));
            wrapper.DBC_ReadDBCTxt(_dbcPath);
            if (_config.UseCanFd)
            {
                wrapper.OpenCANDevice_FD(_config.DeviceType, _config.Channel, _config.FdBaudRate);
            }
            else
            {
                wrapper.OpenCANDevice(_config.DeviceType, _config.Channel, _config.BaudRate);
            }
            _wrapper = wrapper;
        }

        public void Disconnect()
        {
            if (_wrapper == null) return;
            try
            {
                _wrapper.CloseCANDevice();
            }
            finally
            {
                _wrapper = null;
            }
        }

        public void ClearBuffer()
        {
            RequireConnected().ClearTxRxBuffer();
        }

        public void Send(uint id, byte[] data)
        {
            RequireConnected().SendMessage(id, data);
        }

        public List<CanFrame> Receive(uint id)
        {
            List<CANMessage> messages = new List<CANMessage>();
            // CANFDNET-400U-TCP can return an empty list when the provider-side
            // ID filter is used, even though the frame is present in the RX queue.
            // Read the queue first and filter in managed code instead.
            RequireConnected().ReceiveMessage(out messages);
            List<CanFrame> result = new List<CanFrame>();
            if (messages == null) return result;
            foreach (CANMessage message in messages)
            {
                if (message.ID != id) continue;
                byte[] data = message.DATA ?? new byte[0];
                result.Add(new CanFrame(message.ID, data));
            }

            return result;
        }

        public List<CanFrame> ReceiveAll()
        {
            List<CANMessage> messages = new List<CANMessage>();
            RequireConnected().ReceiveMessage(out messages);
            if (messages == null) return new List<CanFrame>();
            Dictionary<uint, CanFrame> latestById = new Dictionary<uint, CanFrame>();
            foreach (CANMessage message in messages)
            {
                byte[] data = message.DATA ?? new byte[0];
                if (data.Length > 8) data = data.Take(8).ToArray();
                uint id = message.ID & 0x1FFFFFFF;
                latestById[id] = new CanFrame(id, data);
            }
            return latestById.Values.ToList();
        }

        public int SendDbcSignal(string signalName, double value, bool sendFlag)
        {
            return RequireConnected().DBC_SendSignalValue(signalName, value, sendFlag);
        }

        public int SendUds(uint txId, uint rxId, string request, ref string response, string expected)
        {
            return RequireConnected().UDS_Request(txId, rxId, request, ref response, expected);
        }

        private CANWrapper RequireConnected()
        {
            if (_wrapper == null) throw new InvalidOperationException(_config.Name + " is not connected.");
            return _wrapper;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
