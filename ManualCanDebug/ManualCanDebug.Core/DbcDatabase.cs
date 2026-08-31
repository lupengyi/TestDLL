using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ManualCanDebug.Core
{
    public sealed class DbcSignalDefinition
    {
        internal DbcSignalDefinition(string name, int startBit, int bitLength, bool littleEndian, bool signed, double factor, double offset, string unit)
        {
            Name = name;
            StartBit = startBit;
            BitLength = bitLength;
            LittleEndian = littleEndian;
            Signed = signed;
            Factor = factor;
            Offset = offset;
            Unit = unit ?? string.Empty;
            ValueDescriptions = new Dictionary<long, string>();
        }

        public string Name { get; private set; }
        public int StartBit { get; private set; }
        public int BitLength { get; private set; }
        public bool LittleEndian { get; private set; }
        public bool Signed { get; private set; }
        public double Factor { get; private set; }
        public double Offset { get; private set; }
        public string Unit { get; private set; }
        public IDictionary<long, string> ValueDescriptions { get; private set; }
    }

    public sealed class DbcMessageDefinition
    {
        internal DbcMessageDefinition(uint rawId, string name, int length)
        {
            RawId = rawId;
            Id = rawId & 0x1FFFFFFF;
            IsExtended = (rawId & 0x80000000) != 0;
            Name = name;
            Length = length;
            Signals = new List<DbcSignalDefinition>();
        }

        public uint RawId { get; private set; }
        public uint Id { get; private set; }
        public bool IsExtended { get; private set; }
        public string Name { get; private set; }
        public int Length { get; private set; }
        public IList<DbcSignalDefinition> Signals { get; private set; }
    }

    public sealed class DbcDecodedSignal
    {
        internal DbcDecodedSignal(DbcSignalDefinition definition, long rawValue, double value)
        {
            Name = definition.Name;
            RawValue = rawValue;
            Value = value;
            Unit = definition.Unit;
            string description;
            Description = definition.ValueDescriptions.TryGetValue(rawValue, out description) ? description : string.Empty;
        }

        public string Name { get; private set; }
        public long RawValue { get; private set; }
        public double Value { get; private set; }
        public string Unit { get; private set; }
        public string Description { get; private set; }
    }

    public sealed class DbcDecodedFrame
    {
        internal DbcDecodedFrame(CanFrame frame, DbcMessageDefinition message, IReadOnlyList<DbcDecodedSignal> signals)
        {
            Frame = frame;
            MessageName = message.Name;
            Signals = signals;
        }

        public CanFrame Frame { get; private set; }
        public string MessageName { get; private set; }
        public IReadOnlyList<DbcDecodedSignal> Signals { get; private set; }
    }

    public sealed class DbcDatabase
    {
        private static readonly Regex MessagePattern = new Regex(@"^BO_\s+(\d+)\s+([^\s:]+)\s*:\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex SignalPattern = new Regex(@"^\s*SG_\s+([^\s:]+)(?:\s+[mM]\d+)?\s*:\s*(\d+)\|(\d+)@([01])([+-])\s*\(([^,]+),([^\)]+)\).*?""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex ValuePattern = new Regex(@"^VAL_\s+(\d+)\s+([^\s]+)\s+(.+);", RegexOptions.Compiled);
        private static readonly Regex ValueItemPattern = new Regex(@"(-?\d+)\s+""([^""]*)""", RegexOptions.Compiled);
        private readonly List<DbcMessageDefinition> _messages;

        private DbcDatabase(List<DbcMessageDefinition> messages)
        {
            _messages = messages;
        }

        public IReadOnlyList<DbcMessageDefinition> Messages { get { return _messages.AsReadOnly(); } }

        public static DbcDatabase Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            return Parse(File.ReadAllText(path));
        }

        public static DbcDatabase Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            List<DbcMessageDefinition> messages = new List<DbcMessageDefinition>();
            DbcMessageDefinition current = null;
            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                Match messageMatch = MessagePattern.Match(rawLine);
                if (messageMatch.Success)
                {
                    current = new DbcMessageDefinition(
                        uint.Parse(messageMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                        messageMatch.Groups[2].Value,
                        int.Parse(messageMatch.Groups[3].Value, CultureInfo.InvariantCulture));
                    messages.Add(current);
                    continue;
                }

                Match signalMatch = SignalPattern.Match(rawLine);
                if (signalMatch.Success && current != null)
                {
                    current.Signals.Add(new DbcSignalDefinition(
                        signalMatch.Groups[1].Value,
                        int.Parse(signalMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                        int.Parse(signalMatch.Groups[3].Value, CultureInfo.InvariantCulture),
                        signalMatch.Groups[4].Value == "1",
                        signalMatch.Groups[5].Value == "-",
                        double.Parse(signalMatch.Groups[6].Value, CultureInfo.InvariantCulture),
                        double.Parse(signalMatch.Groups[7].Value, CultureInfo.InvariantCulture),
                        signalMatch.Groups[8].Value));
                    continue;
                }

                Match valueMatch = ValuePattern.Match(rawLine);
                if (!valueMatch.Success) continue;
                uint rawId = uint.Parse(valueMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                DbcMessageDefinition message = messages.FirstOrDefault(item => item.RawId == rawId);
                if (message == null) continue;
                DbcSignalDefinition signal = message.Signals.FirstOrDefault(item => item.Name == valueMatch.Groups[2].Value);
                if (signal == null) continue;
                foreach (Match item in ValueItemPattern.Matches(valueMatch.Groups[3].Value))
                    signal.ValueDescriptions[long.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture)] = item.Groups[2].Value;
            }

            if (messages.Count == 0) throw new FormatException("DBC does not contain any BO_ message definitions.");
            return new DbcDatabase(messages);
        }

        public DbcMessageDefinition FindMessage(string messageName)
        {
            DbcMessageDefinition message = _messages.FirstOrDefault(item => string.Equals(item.Name, messageName, StringComparison.Ordinal));
            if (message == null) throw new KeyNotFoundException("DBC message was not found: " + messageName);
            return message;
        }

        public CanFrame Encode(string messageName, IDictionary<string, double> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            DbcMessageDefinition message = FindMessage(messageName);
            byte[] data = new byte[message.Length];
            foreach (KeyValuePair<string, double> pair in values)
            {
                DbcSignalDefinition signal = message.Signals.FirstOrDefault(item => string.Equals(item.Name, pair.Key, StringComparison.Ordinal));
                if (signal == null) throw new KeyNotFoundException("DBC signal was not found in " + messageName + ": " + pair.Key);
                if (!signal.LittleEndian) throw new NotSupportedException("Motorola DBC signals are not supported by this debug panel: " + signal.Name);
                long raw = checked((long)Math.Round((pair.Value - signal.Offset) / signal.Factor, MidpointRounding.AwayFromZero));
                ulong encoded = signal.Signed ? unchecked((ulong)raw) : checked((ulong)raw);
                WriteIntelBits(data, signal.StartBit, signal.BitLength, encoded);
            }
            return new CanFrame(message.Id, data);
        }

        public DbcDecodedFrame Decode(CanFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            DbcMessageDefinition message = _messages.FirstOrDefault(item => item.Id == frame.Id);
            if (message == null) return null;
            List<DbcDecodedSignal> signals = new List<DbcDecodedSignal>();
            foreach (DbcSignalDefinition signal in message.Signals)
            {
                if (!signal.LittleEndian) continue;
                ulong unsignedRaw = ReadIntelBits(frame.Data, signal.StartBit, signal.BitLength);
                long raw = signal.Signed ? SignExtend(unsignedRaw, signal.BitLength) : checked((long)unsignedRaw);
                signals.Add(new DbcDecodedSignal(signal, raw, raw * signal.Factor + signal.Offset));
            }
            return new DbcDecodedFrame(frame, message, signals.AsReadOnly());
        }

        private static void WriteIntelBits(byte[] data, int startBit, int bitLength, ulong raw)
        {
            if (bitLength <= 0 || bitLength > 64 || startBit < 0 || startBit + bitLength > data.Length * 8)
                throw new ArgumentOutOfRangeException(nameof(bitLength), "DBC signal exceeds the message payload.");
            if (bitLength < 64 && raw >= (1UL << bitLength)) throw new ArgumentOutOfRangeException(nameof(raw), "DBC signal value exceeds its bit length.");
            for (int bit = 0; bit < bitLength; bit++)
            {
                int destination = startBit + bit;
                byte mask = (byte)(1 << (destination % 8));
                if ((raw & (1UL << bit)) != 0) data[destination / 8] |= mask;
                else data[destination / 8] &= (byte)~mask;
            }
        }

        private static ulong ReadIntelBits(byte[] data, int startBit, int bitLength)
        {
            if (startBit < 0 || bitLength <= 0 || bitLength > 64 || startBit + bitLength > data.Length * 8) return 0;
            ulong result = 0;
            for (int bit = 0; bit < bitLength; bit++)
            {
                int source = startBit + bit;
                if ((data[source / 8] & (1 << (source % 8))) != 0) result |= 1UL << bit;
            }
            return result;
        }

        private static long SignExtend(ulong value, int bitLength)
        {
            if (bitLength == 64) return unchecked((long)value);
            ulong sign = 1UL << (bitLength - 1);
            return (long)((value ^ sign) - sign);
        }
    }
}
