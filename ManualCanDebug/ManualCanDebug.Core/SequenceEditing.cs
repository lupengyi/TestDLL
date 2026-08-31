using System;
using System.Collections.Generic;
using System.Linq;

namespace ManualCanDebug.Core
{
    public static class SequenceEditing
    {
        public static SequenceStepDefinition Clone(SequenceStepDefinition source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new SequenceStepDefinition(new Dictionary<string, object>(source.Properties, StringComparer.Ordinal));
        }

        public static IReadOnlyList<SequenceStepDefinition> BuildFunctionTemplates(IEnumerable<SequenceStepDefinition> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            return steps
                .Where(step => !string.IsNullOrWhiteSpace(step.FunctionName))
                .GroupBy(TemplateKey, StringComparer.Ordinal)
                .Select(group => Clone(group.First()))
                .OrderBy(step => InstrumentStepCatalog.CategoryFor(step), StringComparer.Ordinal)
                .ThenBy(step => step.FunctionName, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private static string TemplateKey(SequenceStepDefinition step)
        {
            if (step.FunctionName == "FCT_ExecuteAction") return step.FunctionName + "|" + Convert.ToString(step.Get("Device")) + "|" + Convert.ToString(step.Get("Operation"));
            if (step.FunctionName == "FCT_ExecuteLogic") return step.FunctionName + "|" + Convert.ToString(step.Get("Operation"));
            if (step.FunctionName == "FCT_CANSignal" || step.FunctionName == "FCT_CANTable") return step.FunctionName + "|" + Convert.ToString(step.Get("Operation")) + "|" + Convert.ToString(step.Get("AddrOffset")) + "|" + Convert.ToString(step.Get("TableIndex"));
            return step.FunctionName;
        }
    }

    public static class InstrumentStepCatalog
    {
        public static string CategoryFor(SequenceStepDefinition step)
        {
            if (step == null) return "组合测试";
            if (step.FunctionName == "FCT_ExecuteAction")
            {
                string device = Convert.ToString(step.Get("Device")) ?? string.Empty;
                if (device.Length > 0) return device.ToUpperInvariant() + " 通用动作";
            }
            if (step.FunctionName == "FCT_ExecuteLogic") return "逻辑控制";
            if (step.FunctionName == "FCT_CANSignal" || step.FunctionName == "FCT_CANTable") return "产品Locator";
            return CategoryFor(step.FunctionName);
        }
        public static string CategoryFor(string functionName)
        {
            string name = functionName ?? string.Empty;
            if (name.StartsWith("LVDC_", StringComparison.Ordinal)) return "LVDC低压电源";
            if (name.StartsWith("HVDC_", StringComparison.Ordinal)) return "HVDC高压电源";
            if (name.StartsWith("DMM_", StringComparison.Ordinal)) return "DMM万用表";
            if (name.StartsWith("RES_", StringComparison.Ordinal)) return "RES电阻模拟器";
            if (name.StartsWith("Resolver_", StringComparison.Ordinal)) return "旋变模拟器";
            if (name.StartsWith("MOXA_", StringComparison.Ordinal)) return "MOXA IO";
            if (name.StartsWith("Relay_", StringComparison.Ordinal)) return "继电器卡";
            if (name.StartsWith("PLC_", StringComparison.Ordinal)) return "PLC";
            if (name.StartsWith("DAQ_", StringComparison.Ordinal)) return "DAQ";
            if (name.StartsWith("CAN_", StringComparison.Ordinal) || name.StartsWith("DUT_", StringComparison.Ordinal) || name.StartsWith("Test_CAN", StringComparison.Ordinal)) return "产品CAN";
            if (name.StartsWith("Test_Delay", StringComparison.Ordinal)) return "流程控制";
            return "组合测试";
        }
    }
}
