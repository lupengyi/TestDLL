using System;
using System.Globalization;

namespace ManualCanDebug.Core
{
    public static class NumericFormula
    {
        public static double Evaluate(string text)
        {
            Parser parser = new Parser(text);
            double value = parser.Expression();
            parser.SkipWhite();
            if (!parser.End) throw new FormatException("算式中存在无法识别的内容：" + text);
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("算式结果不是有效数字：" + text);
            return value;
        }

        private sealed class Parser
        {
            private readonly string _text; private int _index;
            public Parser(string text) { if (string.IsNullOrWhiteSpace(text)) throw new FormatException("写入值不能为空。"); _text = text; }
            public bool End { get { return _index >= _text.Length; } }
            public void SkipWhite() { while (!End && char.IsWhiteSpace(_text[_index])) _index++; }
            public double Expression() { double value = Term(); while (true) { SkipWhite(); if (Take('+')) value += Term(); else if (Take('-')) value -= Term(); else return value; } }
            private double Term() { double value = Factor(); while (true) { SkipWhite(); if (Take('*')) value *= Factor(); else if (Take('/')) { double divisor = Factor(); if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException("算式不能除以0。"); value /= divisor; } else return value; } }
            private double Factor() { SkipWhite(); if (Take('+')) return Factor(); if (Take('-')) return -Factor(); if (Take('(')) { double value = Expression(); SkipWhite(); if (!Take(')')) throw new FormatException("算式缺少右括号：" + _text); return value; } return Number(); }
            private double Number() { SkipWhite(); int start = _index; if (_index + 2 <= _text.Length && _text[_index] == '0' && (_text[_index + 1] == 'x' || _text[_index + 1] == 'X')) { _index += 2; int hexStart = _index; while (!End && Uri.IsHexDigit(_text[_index])) _index++; if (_index == hexStart) throw new FormatException("十六进制数字无效：" + _text); return ulong.Parse(_text.Substring(hexStart, _index - hexStart), NumberStyles.HexNumber, CultureInfo.InvariantCulture); } while (!End && (char.IsDigit(_text[_index]) || _text[_index] == '.')) _index++; if (!End && (_text[_index] == 'e' || _text[_index] == 'E')) { _index++; if (!End && (_text[_index] == '+' || _text[_index] == '-')) _index++; while (!End && char.IsDigit(_text[_index])) _index++; } if (start == _index) throw new FormatException("算式需要数字：" + _text); double value; if (!double.TryParse(_text.Substring(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new FormatException("数字格式无效：" + _text); return value; }
            private bool Take(char value) { SkipWhite(); if (End || _text[_index] != value) return false; _index++; return true; }
        }
    }
}
