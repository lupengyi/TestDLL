using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public static class PreCurrentDiagnosticAnalyzer
    {
        public static string Analyze(ProductCanProfile profile, IEnumerable<PreCurrentReadResult> sourceResults)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (sourceResults == null) throw new ArgumentNullException(nameof(sourceResults));

            List<PreCurrentReadResult> results = sourceResults.ToList();
            List<string> findings = new List<string>();

            AddMotorStatusFinding(results, findings);
            AddBusVoltageFinding(profile, results, findings);
            AddActiveLowInputFindings(results, findings);
            AddReadFailureFinding(results, findings);

            if (findings.Count == 0)
            {
                return "未从本次读取中发现明确的阻止出流状态；仍需结合实际接线和电流反馈人工确认。";
            }

            return string.Join(Environment.NewLine, findings.Select((finding, index) =>
                string.Format(CultureInfo.InvariantCulture, "{0}. {1}", index + 1, finding)));
        }

        private static void AddMotorStatusFinding(IList<PreCurrentReadResult> results, IList<string> findings)
        {
            PreCurrentReadResult result = results.FirstOrDefault(item => item.Succeeded && item.Item.Name == "Motor Status");
            if (result == null || string.IsNullOrWhiteSpace(result.TextValue)) return;

            MotorStatusInfo status;
            try
            {
                status = MotorStatusInfo.Parse(ParseStatusBytes(result.TextValue));
            }
            catch
            {
                findings.Add("Motor Status 原始数据无法解析，不能判断产品出流状态。");
                return;
            }

            switch (status.SequenceStatus)
            {
                case 3:
                    findings.Add("【阻止出流】产品状态为诊断故障(3)，产品会结束或拒绝持续出流。");
                    break;
                case 4:
                    findings.Add("【阻止出流】产品状态为参数故障(4)，请检查电流、步进、保持时间、频率和控制模式参数。");
                    break;
                case 5:
                    findings.Add("【可能阻止出流】产品状态为手动覆盖(5)，当前指令可能已被其他控制覆盖。");
                    break;
                case 1:
                    findings.Add("产品状态为运行中(1)，Ramp=" + status.RampModeDescription + "。");
                    break;
                case 2:
                    findings.Add("产品状态为成功完成(2)，Ramp=" + status.RampModeDescription + "。");
                    break;
                default:
                    findings.Add("产品尚未进入正常运行状态：Ramp=" + status.RampModeDescription + "，Status=" + status.SequenceStatusDescription + "。");
                    break;
            }

            if (status.ActiveFaults.Count > 0)
            {
                findings.Add("【已置位故障】" + string.Join("、", status.ActiveFaults));
                findings.Add(BuildFaultDirection(status.ActiveFaults));
            }
        }

        private static byte[] ParseStatusBytes(string text)
        {
            string[] tokens = text.Split(new[] { ' ', '\t', '\r', '\n', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes = new byte[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
            {
                string token = tokens[index].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? tokens[index].Substring(2)
                    : tokens[index];
                bytes[index] = byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        private static void AddBusVoltageFinding(ProductCanProfile profile, IList<PreCurrentReadResult> results, IList<string> findings)
        {
            if (profile.Model != ProductModel.C95) return;
            PreCurrentReadResult voltage = results.FirstOrDefault(item =>
                item.Succeeded && item.Item.SourceName.IndexOf("HVDC_SENSE_AI", StringComparison.OrdinalIgnoreCase) >= 0);
            if (voltage == null) return;

            const double minimum = 400;
            const double maximum = 980;
            if (voltage.Value < minimum)
            {
                findings.Add(string.Format(CultureInfo.InvariantCulture,
                    "【母线条件异常】产品内部 HVDC_SENSE_AI={0:0.###}V，低于C95 Motor Limits最小值400V。若端子实测为500V，应重点检查产品高压采样、端子到母线的连接及接触器/预充路径。",
                    voltage.Value));
            }
            else if (voltage.Value > maximum)
            {
                findings.Add(string.Format(CultureInfo.InvariantCulture,
                    "【母线条件异常】产品内部 HVDC_SENSE_AI={0:0.###}V，高于C95 Motor Limits最大值980V。",
                    voltage.Value));
            }
            else
            {
                findings.Add(string.Format(CultureInfo.InvariantCulture,
                    "产品内部母线电压={0:0.###}V，位于C95 Motor Limits 400V～980V范围内。",
                    voltage.Value));
            }
        }

        private static void AddActiveLowInputFindings(IEnumerable<PreCurrentReadResult> results, IList<string> findings)
        {
            foreach (PreCurrentReadResult result in results.Where(item => item.Succeeded && item.Item.ActiveLow && Math.Abs(item.Value) < 0.000001))
            {
                findings.Add("【故障输入有效】" + result.Item.Name + "=0（低电平有效），该硬件故障输入当前处于触发状态。");
            }
        }

        private static void AddReadFailureFinding(IEnumerable<PreCurrentReadResult> results, IList<string> findings)
        {
            string[] names = results.Where(item => !item.Succeeded).Select(item => item.Item.Name).ToArray();
            if (names.Length > 0) findings.Add("【数据不完整】以下项目读取失败：" + string.Join("、", names));
        }

        private static string BuildFaultDirection(IReadOnlyList<string> faults)
        {
            string joined = string.Join("|", faults);
            if (joined.IndexOf("过流", StringComparison.Ordinal) >= 0 || joined.IndexOf("退饱和", StringComparison.Ordinal) >= 0)
                return "【排查方向】优先检查相线短路/接线、功率模块、门极驱动以及电流采样；故障清除后重新读取，若立即复现则属于当前硬件故障。";
            if (joined.IndexOf("母线", StringComparison.Ordinal) >= 0)
                return "【排查方向】优先检查母线实际电压、高压采样、预充/接触器路径及母线故障输入。";
            if (joined.IndexOf("温度", StringComparison.Ordinal) >= 0)
                return "【排查方向】优先检查温度传感器、传感器供电和线束；冷机同时出现多个温度故障时尤其要检查公共供电或地。";
            if (joined.IndexOf("欠压", StringComparison.Ordinal) >= 0)
                return "【排查方向】优先检查门极驱动电源和上下桥臂欠压输入。";
            return "【排查方向】先清除锁存故障并重新读取；若同一故障立即复现，再按对应硬件信号排查。";
        }
    }
}
