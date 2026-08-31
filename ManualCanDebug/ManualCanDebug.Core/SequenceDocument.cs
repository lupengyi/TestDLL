using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ManualCanDebug.Core
{
    public sealed class SequenceDocument
    {
        public SequenceDocument(IDictionary<string, object> root, IList<SequenceStepDefinition> steps)
        {
            RootProperties = new Dictionary<string, object>(root, StringComparer.Ordinal);
            Steps = new List<SequenceStepDefinition>(steps).AsReadOnly();
        }

        public IDictionary<string, object> RootProperties { get; private set; }
        public IReadOnlyList<SequenceStepDefinition> Steps { get; private set; }

        public static SequenceDocument Parse(string text)
        {
            Dictionary<string, object> root = LooseJson.Parse(text) as Dictionary<string, object>;
            if (root == null) throw new FormatException("SEQ root must be a JSON object.");
            object rawSteps;
            List<object> stepValues = root.TryGetValue("StepList", out rawSteps) ? rawSteps as List<object> : null;
            if (stepValues == null) throw new FormatException("SEQ does not contain a StepList array.");
            List<SequenceStepDefinition> steps = new List<SequenceStepDefinition>();
            foreach (object item in stepValues)
            {
                Dictionary<string, object> values = item as Dictionary<string, object>;
                if (values == null) throw new FormatException("Every StepList item must be an object.");
                steps.Add(new SequenceStepDefinition(values));
            }
            root.Remove("StepList");
            return new SequenceDocument(root, steps);
        }

        public string ToJson(IEnumerable<SequenceStepDefinition> steps)
        {
            Dictionary<string, object> root = new Dictionary<string, object>(RootProperties, StringComparer.Ordinal);
            List<object> outputSteps = new List<object>();
            foreach (SequenceStepDefinition step in steps) outputSteps.Add(step.Properties);
            root["StepList"] = outputSteps;
            return LooseJson.Serialize(root);
        }
    }

    public sealed class SequenceStepDefinition
    {
        public SequenceStepDefinition(IDictionary<string, object> properties)
        {
            Properties = new Dictionary<string, object>(properties, StringComparer.Ordinal);
        }

        public IDictionary<string, object> Properties { get; private set; }
        public string StepName { get { return Text("StepName", "Unnamed Step"); } set { Properties["StepName"] = value ?? string.Empty; } }
        public string FunctionName { get { return Text("FunctionName", string.Empty); } }
        public string RunMode { get { return Text("RunMode", "Normal"); } set { Properties["RunMode"] = value ?? "Normal"; } }
        public bool RecordingLog
        {
            get { object value; return !Properties.TryGetValue("RecordingLog", out value) || Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
            set { Properties["RecordingLog"] = value; }
        }

        public IEnumerable<KeyValuePair<string, object>> Parameters
        {
            get
            {
                foreach (KeyValuePair<string, object> pair in Properties)
                    if (pair.Key != "StepName" && pair.Key != "RunMode" && pair.Key != "FunctionName" && pair.Key != "RecordingLog") yield return pair;
            }
        }

        public object Get(string name, object defaultValue = null) { object value; return Properties.TryGetValue(name, out value) ? value : defaultValue; }
        public double GetDouble(string name, double defaultValue = 0)
        {
            object value;
            if (!Properties.TryGetValue(name, out value) || value == null) return defaultValue;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public int GetInt(string name, int defaultValue = 0)
        {
            object value;
            if (!Properties.TryGetValue(name, out value) || value == null) return defaultValue;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        public void SetParameterFromText(string name, string text, Type originalType)
        {
            if (originalType == typeof(bool))
            {
                bool value;
                if (!bool.TryParse(text, out value) && text != "0" && text != "1") throw new FormatException(name + " must be true/false or 0/1.");
                Properties[name] = value || text == "1";
            }
            else if (originalType == typeof(int) || originalType == typeof(long))
            {
                long value;
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new FormatException(name + " must be an integer.");
                Properties[name] = originalType == typeof(int) ? (object)checked((int)value) : value;
            }
            else if (originalType == typeof(double) || originalType == typeof(float) || originalType == typeof(decimal))
            {
                double value;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new FormatException(name + " must be a number.");
                Properties[name] = value;
            }
            else Properties[name] = text ?? string.Empty;
        }

        private string Text(string name, string defaultValue) { object value; return Properties.TryGetValue(name, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : defaultValue; }
    }

    internal static class LooseJson
    {
        public static object Parse(string text) { return new Parser(text ?? throw new ArgumentNullException(nameof(text))).ParseDocument(); }
        public static string Serialize(object value) { StringBuilder text = new StringBuilder(); Write(value, text, 0); text.AppendLine(); return text.ToString(); }

        private static void Write(object value, StringBuilder text, int depth)
        {
            if (value == null) { text.Append("null"); return; }
            string stringValue = value as string;
            if (stringValue != null) { WriteString(stringValue, text); return; }
            if (value is bool) { text.Append((bool)value ? "true" : "false"); return; }
            IDictionary<string, object> dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                text.AppendLine("{"); int index = 0;
                foreach (KeyValuePair<string, object> pair in dictionary)
                {
                    Indent(text, depth + 1); WriteString(pair.Key, text); text.Append(": "); Write(pair.Value, text, depth + 1);
                    if (++index < dictionary.Count) text.Append(','); text.AppendLine();
                }
                Indent(text, depth); text.Append('}'); return;
            }
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable != null)
            {
                List<object> items = new List<object>(); foreach (object item in enumerable) items.Add(item);
                text.AppendLine("[");
                for (int i = 0; i < items.Count; i++) { Indent(text, depth + 1); Write(items[i], text, depth + 1); if (i + 1 < items.Count) text.Append(','); text.AppendLine(); }
                Indent(text, depth); text.Append(']'); return;
            }
            text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(string value, StringBuilder text)
        {
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break; case '\\': text.Append("\\\\"); break;
                    case '\r': text.Append("\\r"); break; case '\n': text.Append("\\n"); break; case '\t': text.Append("\\t"); break;
                    default: if (c < 32) text.Append("\\u" + ((int)c).ToString("X4")); else text.Append(c); break;
                }
            }
            text.Append('"');
        }

        private static void Indent(StringBuilder text, int depth) { text.Append(' ', depth * 2); }

        private sealed class Parser
        {
            private readonly string _text; private int _index;
            public Parser(string text) { _text = text; }
            public object ParseDocument() { object value = ParseValue(); Skip(); if (_index != _text.Length) throw Error("Unexpected content"); return value; }
            private object ParseValue()
            {
                Skip(); if (_index >= _text.Length) throw Error("Unexpected end"); char c = _text[_index];
                if (c == '{') return ParseObject(); if (c == '[') return ParseArray(); if (c == '"') return ParseString();
                if (c == '-' || char.IsDigit(c)) return ParseNumber();
                if (Take("true")) return true; if (Take("false")) return false; if (Take("null")) return null;
                throw Error("Invalid value");
            }
            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal); _index++; Skip();
                while (_index < _text.Length && _text[_index] != '}')
                {
                    string key = ParseString(); Skip(); Require(':'); result[key] = ParseValue(); Skip();
                    if (_index < _text.Length && _text[_index] == ',') { _index++; Skip(); if (_index < _text.Length && _text[_index] == '}') break; }
                    else break;
                }
                Require('}'); return result;
            }
            private List<object> ParseArray()
            {
                List<object> result = new List<object>(); _index++; Skip();
                while (_index < _text.Length && _text[_index] != ']')
                {
                    result.Add(ParseValue()); Skip();
                    if (_index < _text.Length && _text[_index] == ',') { _index++; Skip(); if (_index < _text.Length && _text[_index] == ']') break; }
                    else break;
                }
                Require(']'); return result;
            }
            private string ParseString()
            {
                Skip(); Require('"'); StringBuilder value = new StringBuilder();
                while (_index < _text.Length)
                {
                    char c = _text[_index++]; if (c == '"') return value.ToString();
                    if (c != '\\') { value.Append(c); continue; }
                    if (_index >= _text.Length) throw Error("Invalid escape"); c = _text[_index++];
                    switch (c)
                    {
                        case '"': value.Append('"'); break; case '\\': value.Append('\\'); break; case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break; case 'f': value.Append('\f'); break; case 'n': value.Append('\n'); break; case 'r': value.Append('\r'); break; case 't': value.Append('\t'); break;
                        case 'u': if (_index + 4 > _text.Length) throw Error("Invalid unicode escape"); value.Append((char)int.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); _index += 4; break;
                        default: throw Error("Invalid escape");
                    }
                }
                throw Error("Unterminated string");
            }
            private object ParseNumber()
            {
                int start = _index; if (_text[_index] == '-') _index++; while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
                bool real = false; if (_index < _text.Length && _text[_index] == '.') { real = true; _index++; while (_index < _text.Length && char.IsDigit(_text[_index])) _index++; }
                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E')) { real = true; _index++; if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++; while (_index < _text.Length && char.IsDigit(_text[_index])) _index++; }
                string number = _text.Substring(start, _index - start); if (real) return double.Parse(number, CultureInfo.InvariantCulture);
                long integer = long.Parse(number, CultureInfo.InvariantCulture); return integer >= int.MinValue && integer <= int.MaxValue ? (object)(int)integer : integer;
            }
            private void Skip()
            {
                while (_index < _text.Length)
                {
                    if (char.IsWhiteSpace(_text[_index])) { _index++; continue; }
                    if (_text[_index] == '/' && _index + 1 < _text.Length && _text[_index + 1] == '/') { _index += 2; while (_index < _text.Length && _text[_index] != '\n') _index++; continue; }
                    if (_text[_index] == '/' && _index + 1 < _text.Length && _text[_index + 1] == '*') { _index += 2; while (_index + 1 < _text.Length && !(_text[_index] == '*' && _text[_index + 1] == '/')) _index++; _index = Math.Min(_text.Length, _index + 2); continue; }
                    break;
                }
            }
            private bool Take(string value) { if (_index + value.Length > _text.Length || string.CompareOrdinal(_text, _index, value, 0, value.Length) != 0) return false; _index += value.Length; return true; }
            private void Require(char value) { Skip(); if (_index >= _text.Length || _text[_index] != value) throw Error("Expected '" + value + "'"); _index++; }
            private FormatException Error(string message) { return new FormatException(message + " at character " + _index.ToString(CultureInfo.InvariantCulture) + "."); }
        }
    }
}
