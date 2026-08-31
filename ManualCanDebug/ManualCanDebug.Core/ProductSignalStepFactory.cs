using System;
using System.Collections.Generic;
using System.Linq;

namespace ManualCanDebug.Core
{
    public static class ProductSignalStepFactory
    {
        public static SequenceStepDefinition CreateRead(string stepName, ProductLocatorTable table, ProductLocatorSignal signal, double lowLimit, double highLimit, string compareType)
        {
            Validate(table, signal);
            return new SequenceStepDefinition(new Dictionary<string, object>
            {
                { "StepName", stepName ?? string.Empty }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANSignal" }, { "RecordingLog", true },
                { "Operation", "Read" },
                { "AddrOffset", checked((int)table.AddressOffset) }, { "TableIndex", signal.Offset }, { "DataSize", signal.DataSize },
                { "DataType", signal.DataType }, { "Endian", "Little" }, { "ResultMode", "NumericLimit" },
                { "LowLimit", lowLimit }, { "HighLimit", highLimit }, { "Comtype", string.IsNullOrWhiteSpace(compareType) ? "GELE" : compareType }, { "Unit", signal.Unit }
            });
        }

        public static SequenceStepDefinition CreateWrite(string stepName, ProductLocatorTable table, ProductLocatorSignal signal, double value)
        {
            return CreateWrite(stepName, table, signal, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static SequenceStepDefinition CreateWrite(string stepName, ProductLocatorTable table, ProductLocatorSignal signal, string valueText)
        {
            Validate(table, signal);
            if (!table.CanWrite) throw new InvalidOperationException("Locator table is not marked writable: " + table.Name);
            return new SequenceStepDefinition(new Dictionary<string, object>
            {
                { "StepName", stepName ?? string.Empty }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANSignal" }, { "RecordingLog", true },
                { "Operation", "Write" }, { "ResultMode", "Action" },
                { "AddrOffset", checked((int)table.AddressOffset) }, { "TableIndex", signal.Offset }, { "DataSize", signal.DataSize }, { "DataType", signal.DataType }, { "Endian", "Little" },
                { "ValueText", NormalizeWriteValue(signal, valueText) }, { "VerifyAfterWrite", true }
            });
        }

        public static SequenceStepDefinition CreateTableRead(string stepName, ProductLocatorTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            return new SequenceStepDefinition(new Dictionary<string, object>
            {
                { "StepName", stepName ?? string.Empty }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true },
                { "Operation", "Read" }, { "ResultMode", "Information" }, { "AddrOffset", checked((int)table.AddressOffset) }, { "TableLength", TableLength(table) }
            });
        }

        public static SequenceStepDefinition CreateTableWrite(string stepName, ProductLocatorTable table, IEnumerable<KeyValuePair<ProductLocatorSignal, string>> changes)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (!table.CanWrite) throw new InvalidOperationException("Locator table is not marked writable: " + table.Name);
            List<object> array = new List<object>();
            foreach (KeyValuePair<ProductLocatorSignal, string> change in changes ?? Enumerable.Empty<KeyValuePair<ProductLocatorSignal, string>>())
            {
                if (!table.Signals.Contains(change.Key)) throw new ArgumentException("A changed signal does not belong to the selected table.", nameof(changes));
                bool writeLast = ShouldWriteLast(change.Key.Name); array.Add(new Dictionary<string, object> { { "Name", change.Key.Name }, { "Offset", change.Key.Offset }, { "DataSize", change.Key.DataSize }, { "DataType", change.Key.DataType }, { "Endian", "Little" }, { "Value", NormalizeWriteValue(change.Key, change.Value) }, { "WriteLast", writeLast }, { "WriteFinal", writeLast && change.Key.Name.Replace("_", string.Empty).Replace(" ", string.Empty).IndexOf("NewData", StringComparison.OrdinalIgnoreCase) >= 0 } });
            }
            if (array.Count == 0) throw new InvalidOperationException("At least one table signal must be selected for writing.");
            return new SequenceStepDefinition(new Dictionary<string, object>
            {
                { "StepName", stepName ?? string.Empty }, { "RunMode", "Normal" }, { "FunctionName", "FCT_CANTable" }, { "RecordingLog", true },
                { "Operation", "Write" }, { "ResultMode", "Action" }, { "AddrOffset", checked((int)table.AddressOffset) }, { "TableLength", TableLength(table) },
                { "ChangesJson", LooseJson.Serialize(array).Trim() }, { "VerifyAfterWrite", true }
            });
        }

        private static int TableLength(ProductLocatorTable table)
        {
            return table.Signals.Count == 0 ? table.ElementSize : table.Signals.Max(signal => signal.Offset + signal.DataSize);
        }
        private static bool ShouldWriteLast(string name)
        {
            string value = (name ?? string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            return value.Contains("newdataflag") || value.Contains("startcmd") || value.Contains("changeflag") || value.Contains("reset") || value.Contains("execute");
        }
        private static string NormalizeWriteValue(ProductLocatorSignal signal, string valueText)
        {
            string type = (signal.DataType ?? string.Empty).ToLowerInvariant(); string text = (valueText ?? string.Empty).Trim(); if (type.Contains("string") || type.Contains("char") || type.Contains("bool")) return text; return NumericFormula.Evaluate(text).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Validate(ProductLocatorTable table, ProductLocatorSignal signal)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            if (!table.Signals.Contains(signal)) throw new ArgumentException("Signal does not belong to the selected Locator table.", nameof(signal));
        }
    }
}
