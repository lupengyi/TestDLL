using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.RegularExpressions;

namespace ManualCanDebug
{
    internal enum MainTestResultKind
    {
        None,
        NumericLimit,
        StringLimit
    }

    internal sealed class MainTestMethodSemantics
    {
        public static readonly MainTestMethodSemantics None = new MainTestMethodSemantics(MainTestResultKind.None, false);
        public MainTestMethodSemantics(MainTestResultKind resultKind, bool producesResult) { ResultKind = resultKind; ProducesResult = producesResult; }
        public MainTestResultKind ResultKind { get; private set; }
        public bool ProducesResult { get; private set; }
    }

    internal static class MainTestMethodCatalog
    {
        private static readonly Lazy<string> Source = new Lazy<string>(LoadSource);
        private static readonly Dictionary<string, MainTestMethodSemantics> Cache = new Dictionary<string, MainTestMethodSemantics>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        public static MainTestMethodSemantics Inspect(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName)) return MainTestMethodSemantics.None;
            lock (Sync)
            {
                MainTestMethodSemantics cached;
                if (Cache.TryGetValue(functionName, out cached)) return cached;
                MainTestMethodSemantics result = Analyze(functionName);
                Cache[functionName] = result;
                return result;
            }
        }

        public static bool Contains(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName)) return false;
            return Regex.IsMatch(Source.Value, @"\bpublic\s+(?:void|double|string|bool|int|float|object)\s+" + Regex.Escape(functionName) + @"\s*\(", RegexOptions.CultureInvariant);
        }

        public static string RequiredInstrument(ManualCanDebug.Core.SequenceStepDefinition step)
        {
            if (step == null) return string.Empty;
            string function = Normalize(step.FunctionName);
            string device = Convert.ToString(step.Get("Device"), System.Globalization.CultureInfo.InvariantCulture).Trim().ToUpperInvariant();
            if (function == "FCTEXECUTEACTION")
            {
                switch (device) { case "AUXCAN": return "AUXCAN"; case "RESOLVER": return "RESOLVERCAN"; case "PRODUCTCAN": return "DUTCAN"; case "LVDC": return "LVDC"; case "LVDC_KL15": return "LVDC_KL15"; case "HVDC": return "HVDC"; case "DMM": return "DMM"; case "RES": return "RES"; case "DAQ": return "DAQ"; case "DCDC_LOAD": return "DCDC_LOAD"; case "MOXA": return "RELAY_FCT/RELAY_HVMUX"; case "RELAY": return "RELAY_FCT"; case "RELAY_FCT": return "RELAY_FCT"; case "RELAY_HVMUX": return "RELAY_HVMUX"; case "PLC": return "PLC"; default: return string.Empty; }
            }
            if (function == "FCTCANSIGNAL" || function == "FCTCANTABLE" || function.Contains("DUT") || function.Contains("CANCOMMUNICATION") || function.Contains("CANAPP2FT") || function.Contains("CANFT2APP") || function.Contains("CANSENDWAKEUP")) return "DUTCAN";
            if (function.Contains("RESOLVER")) return "RESOLVERCAN";
            if (function.StartsWith("LVDCKL15", StringComparison.Ordinal)) return "LVDC_KL15";
            if (function.StartsWith("LVDC", StringComparison.Ordinal)) return "LVDC";
            if (function.StartsWith("HVDC", StringComparison.Ordinal)) return "HVDC";
            if (function.StartsWith("DMM", StringComparison.Ordinal)) return "DMM";
            if (function.StartsWith("RES", StringComparison.Ordinal)) return "RES";
            if (function.StartsWith("MOXA", StringComparison.Ordinal)) return "MOXA0/MOXA1";
            if (function.StartsWith("RELAY", StringComparison.Ordinal)) return "RELAY";
            if (function.StartsWith("PLC", StringComparison.Ordinal)) return "PLC";
            return string.Empty;
        }

        public static string BindingSummary(ManualCanDebug.Core.SequenceStepDefinition step)
        {
            if (step == null) return "MainTest：未配置";
            string required = RequiredInstrument(step);
            return "MainTest：" + step.FunctionName + (string.IsNullOrWhiteSpace(required) ? string.Empty : "  ·  依赖：" + required) + (Contains(step.FunctionName) ? "  ·  已关联" : "  ·  函数不存在");
        }

        private static MainTestMethodSemantics Analyze(string functionName)
        {
            string source = Source.Value;
            if (string.IsNullOrWhiteSpace(source)) return MainTestMethodSemantics.None;
            Match method = Regex.Match(source, @"public\s+void\s+" + Regex.Escape(functionName) + @"\s*\(\s*int\s+socketIndex\s*\)", RegexOptions.CultureInvariant);
            if (!method.Success) return MainTestMethodSemantics.None;
            Match nextMethod = Regex.Match(source, @"\r?\n\s*public\s+void\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            int searchStart = method.Index + method.Length;
            nextMethod = Regex.Match(source.Substring(searchStart), @"\r?\n\s*public\s+void\s+", RegexOptions.CultureInvariant);
            int end = nextMethod.Success ? searchStart + nextMethod.Index : source.Length;
            string body = source.Substring(method.Index, end - method.Index);
            bool numeric = body.IndexOf("AddNumericTesting", StringComparison.Ordinal) >= 0;
            bool text = body.IndexOf("AddStringTesting", StringComparison.Ordinal) >= 0;
            bool externalNumeric = numeric && body.IndexOf("\"LowLimit\"", StringComparison.Ordinal) >= 0 && body.IndexOf("\"HighLimit\"", StringComparison.Ordinal) >= 0;
            bool externalString = text && body.IndexOf("\"Limit\"", StringComparison.Ordinal) >= 0;
            if (externalNumeric) return new MainTestMethodSemantics(MainTestResultKind.NumericLimit, true);
            if (externalString) return new MainTestMethodSemantics(MainTestResultKind.StringLimit, true);
            return new MainTestMethodSemantics(MainTestResultKind.None, numeric || text);
        }

        private static string LoadSource()
        {
            Assembly assembly = typeof(MainTestMethodCatalog).Assembly;
            string[] resources = { "ManualCanDebug.TestDllMain.cs", "ManualCanDebug.TestDllMain.Generic.cs", "ManualCanDebug.TestDllMain.Generated.Instruments.cs" };
            return string.Join(Environment.NewLine, resources.Select(name => { using (Stream stream = assembly.GetManifestResourceStream(name)) using (StreamReader reader = stream == null ? null : new StreamReader(stream)) return reader == null ? string.Empty : reader.ReadToEnd(); }));
        }

        private static string Normalize(string value) { return (value ?? string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant(); }
    }
}
