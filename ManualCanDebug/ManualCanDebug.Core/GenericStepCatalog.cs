using System.Collections.Generic;

namespace ManualCanDebug.Core
{
    public static class GenericStepCatalog
    {
        public static IReadOnlyList<SequenceStepDefinition> CreateTemplates()
        {
            List<SequenceStepDefinition> steps = new List<SequenceStepDefinition>();
            Add(steps, "LVDC 设置电压", "LVDC", "SetVoltage", new Dictionary<string, object> { { "Voltage", 24.0 } });
            Add(steps, "LVDC 设置电流", "LVDC", "SetCurrent", new Dictionary<string, object> { { "Current", 5.0 } });
            Add(steps, "LVDC 输出开关", "LVDC", "SetOutput", new Dictionary<string, object> { { "Output", true } });
            AddRead(steps, "LVDC 读取电压", "LVDC", "ReadVoltage", "V"); AddRead(steps, "LVDC 读取电流", "LVDC", "ReadCurrent", "A");
            Add(steps, "HVDC 设置电压", "HVDC", "SetVoltage", new Dictionary<string, object> { { "Voltage", 600.0 } });
            Add(steps, "HVDC 设置电流", "HVDC", "SetCurrent", new Dictionary<string, object> { { "Current", 5.0 } });
            Add(steps, "HVDC 输出开关", "HVDC", "SetOutput", new Dictionary<string, object> { { "Output", true } });
            AddRead(steps, "HVDC 读取电压", "HVDC", "ReadVoltage", "V"); AddRead(steps, "HVDC 读取电流", "HVDC", "ReadCurrent", "A");
            Add(steps, "DMM 配置直流电压", "DMM", "ConfigDCVoltage", new Dictionary<string, object> { { "Range", 1000.0 }, { "Solution", 0.01 } });
            Add(steps, "DMM 配置直流电流", "DMM", "ConfigDCCurrent", new Dictionary<string, object> { { "Range", 3.0 }, { "Solution", 0.00001 } });
            AddRead(steps, "DMM 读取", "DMM", "Read", string.Empty);
            AddRead(steps, "DAQ 读取", "DAQ", "Read", "A", new Dictionary<string, object> { { "Channel", 0 }, { "Scale", 22.058823529 }, { "Offset", 0.0 } });
            Add(steps, "产品进入FT", "PRODUCTCAN", "EnterFT", new Dictionary<string, object>());
            Add(steps, "产品通信初始化", "PRODUCTCAN", "CommunicationInit", new Dictionary<string, object> { { "TxID", "2030" }, { "RxID", "2031" } });
            Add(steps, "产品唤醒", "PRODUCTCAN", "Wakeup", new Dictionary<string, object>());
            Add(steps, "辅助CAN DBC发送", "AUXCAN", "SendDbcSignals", new Dictionary<string, object> { { "MessageName", "VCU1_DCDC_OilPump_Cmd" }, { "SignalsJson", "{}" } });
            Add(steps, "辅助CAN DBC周期发送启动", "AUXCAN", "StartPeriodicDbc", new Dictionary<string, object> { { "MessageName", "VCU1_DCDC_OilPump_Cmd" }, { "PeriodicKey", "VCU1_DCDC_OilPump_Cmd" }, { "PeriodMs", 100 }, { "SignalsJson", "{}" } });
            Add(steps, "辅助CAN DBC周期发送停止", "AUXCAN", "StopPeriodicDbc", new Dictionary<string, object> { { "PeriodicKey", "VCU1_DCDC_OilPump_Cmd" } });
            AddRead(steps, "辅助CAN DBC信号读取", "AUXCAN", "ReadDbcSignal", string.Empty, new Dictionary<string, object> { { "MessageName", "ACU1_DCDC_Feedback1" }, { "SignalName", "DCDC_OutVoltage" }, { "TimeoutMs", 1000 } });
            Add(steps, "插件仪器动作", "CUSTOM", "Execute", new Dictionary<string, object> { { "PluginAssembly", "GenericActionPlugins\\MyInstrument.dll" }, { "PluginType", "MyCompany.MyInstrumentPlugin" }, { "ParametersJson", "{}" } });
            return steps.AsReadOnly();
        }

        private static void AddRead(List<SequenceStepDefinition> steps, string name, string device, string operation, string unit, IDictionary<string, object> extra = null)
        {
            Dictionary<string, object> values = extra == null ? new Dictionary<string, object>() : new Dictionary<string, object>(extra);
            values["ResultMode"] = "NumericLimit"; values["LowLimit"] = 0.0; values["HighLimit"] = 0.0; values["Comtype"] = "GELE"; values["Unit"] = unit; Add(steps, name, device, operation, values);
        }
        private static void Add(List<SequenceStepDefinition> steps, string name, string device, string operation, IDictionary<string, object> extra)
        {
            Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", name }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteAction" }, { "RecordingLog", true }, { "Device", device }, { "Operation", operation }, { "ResultMode", extra != null && extra.ContainsKey("ResultMode") ? extra["ResultMode"] : "Action" } };
            if (extra != null) foreach (KeyValuePair<string, object> pair in extra) values[pair.Key] = pair.Value;
            steps.Add(new SequenceStepDefinition(values));
        }
    }
}
