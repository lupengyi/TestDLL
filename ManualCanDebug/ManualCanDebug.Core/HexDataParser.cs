using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ManualCanDebug.Core
{
    public static class HexDataParser
    {
        public static byte[] Parse(string text)
        {
            byte[] result = ParseBuffer(text);
            if (result.Length > 8)
            {
                throw new ArgumentException("Classic CAN data must contain one to eight bytes.", nameof(text));
            }

            return result;
        }

        public static byte[] ParseBuffer(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Hex data cannot be empty.");

            string[] tokens = Regex.Split(text.Trim(), @"[\s,;_\-]+")
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(token => token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token.Substring(2) : token)
                .ToArray();

            if (tokens.Length == 0) throw new FormatException("Hex data cannot be empty.");

            byte[] result = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Length != 2)
                {
                    throw new FormatException("Each byte must contain exactly two hexadecimal digits.");
                }

                result[i] = byte.Parse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return result;
        }

        public static string Format(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return string.Join(" ", data.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }
    }
}
