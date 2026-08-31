using System;
using System.Globalization;

namespace ManualCanDebug.Core
{
    public sealed class PreCurrentReadResult
    {
        private PreCurrentReadResult(PreCurrentReadItem item, bool succeeded, double value, string textValue, string interpretation, string error)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Succeeded = succeeded;
            Value = value;
            TextValue = textValue ?? string.Empty;
            Interpretation = interpretation ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public PreCurrentReadItem Item { get; private set; }
        public bool Succeeded { get; private set; }
        public double Value { get; private set; }
        public string TextValue { get; private set; }
        public string Interpretation { get; private set; }
        public string Error { get; private set; }

        public static PreCurrentReadResult Success(PreCurrentReadItem item, double value)
        {
            return Success(item, value, string.Empty);
        }

        public static PreCurrentReadResult Success(PreCurrentReadItem item, double value, string interpretation)
        {
            return new PreCurrentReadResult(item, true, value, string.Empty, interpretation, string.Empty);
        }

        public static PreCurrentReadResult SuccessText(PreCurrentReadItem item, string value)
        {
            return SuccessText(item, value, string.Empty);
        }

        public static PreCurrentReadResult SuccessText(PreCurrentReadItem item, string value, string interpretation)
        {
            return new PreCurrentReadResult(item, true, 0, value, interpretation, string.Empty);
        }

        public static PreCurrentReadResult Failure(PreCurrentReadItem item, string error)
        {
            return new PreCurrentReadResult(item, false, 0, string.Empty, string.Empty, error);
        }

        public string FormatValue()
        {
            if (!Succeeded) return "读取失败：" + Error;
            if (!string.IsNullOrEmpty(TextValue)) return TextValue;

            string valueText = Value.ToString("0.###", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Item.Unit) ? valueText : valueText + " " + Item.Unit;
        }
    }
}
