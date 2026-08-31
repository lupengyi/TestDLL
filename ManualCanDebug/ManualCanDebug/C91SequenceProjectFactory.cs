using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal static class C91SequenceProjectFactory
    {
        private sealed class SectionSeed { public SectionSeed(string code, string title, string firstStep, string phase) { Code = code; Title = title; FirstStep = firstStep; Phase = phase; } public string Code, Title, FirstStep, Phase; }
        public static FctStudioProject Create(SequenceDocument sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            SectionSeed[] seeds =
            {
                new SectionSeed("A", "上电准备、唤醒/休眠电流与进入FT", "Tray Number", "准备阶段"),
                new SectionSeed("B", "16V条件：电压、旋变、温度与内部信号", "Set LVDC Voltage 16V", "主驱测试"),
                new SectionSeed("C", "第一轮高压采样与过压故障", "Config DMM To DC Voltage", "主驱测试"),
                new SectionSeed("D", "24V条件：版本、CAN、旋变、温度与VREF", "Set LVDC Voltage 24V", "主驱测试"),
                new SectionSeed("E", "第二轮高压采样与过压故障", "Config DMM To DC Voltage", "主驱测试"),
                new SectionSeed("F", "32V条件：电压、旋变、温度与VREF", "Set LVDC Voltage 32V", "主驱测试"),
                new SectionSeed("G", "第三轮高压采样与过压故障", "Config DMM To DC Voltage", "主驱测试"),
                new SectionSeed("H", "热状态基线、冷却液与零转速", "VIPER Temp Test", "主驱测试"),
                new SectionSeed("I", "600Arms负载：HV450/600 × LV16/24/32", "Set HVDC Voltage 450V", "主驱测试"),
                new SectionSeed("J", "电流线性：HV618、LV24、0~900Arms", "Set HVDC Voltage 618V", "主驱测试"),
                new SectionSeed("K", "600Arms负载：HV750 × LV16/24/32", "Set HVDC Voltage 750V", "主驱测试"),
                new SectionSeed("L", "850Arms高电流负载矩阵", "Set HVDC Voltage 450V", "主驱测试"),
                new SectionSeed("M", "完成通知、重启、被动放电与下电", "LoadFinished", "安全收尾")
            };
            List<int> starts = new List<int>(); int searchFrom = 0;
            foreach (SectionSeed seed in seeds) { int found = Find(sequence.Steps, seed.FirstStep, searchFrom); if (found < 0) throw new InvalidOperationException("C91 SEQ章节起点未找到：" + seed.Code + " " + seed.FirstStep); starts.Add(found); searchFrom = found + 1; }
            Dictionary<string, object> root = new Dictionary<string, object>(sequence.RootProperties, StringComparer.Ordinal); object projectName; string name = root.TryGetValue("ProjectName", out projectName) ? Convert.ToString(projectName, CultureInfo.InvariantCulture) : "C91-01-0001";
            FctStudioProject project = new FctStudioProject { ProjectName = name, Product = "C91", SequenceRoot = root };
            for (int sectionIndex = 0; sectionIndex < seeds.Length; sectionIndex++)
            {
                int start = starts[sectionIndex], end = sectionIndex + 1 < starts.Count ? starts[sectionIndex + 1] : sequence.Steps.Count; SectionSeed seed = seeds[sectionIndex]; FunctionBlockDefinition block = new FunctionBlockDefinition { Name = seed.Code + " · " + seed.Title, Category = seed.Phase == "安全收尾" ? "安全" : seed.Phase == "准备阶段" ? "公共准备" : "主驱", ModuleKind = "Product", Version = "1.0", Description = string.Format(CultureInfo.InvariantCulture, "从C91原始SEQ按顺序导入：STEP {0}~{1}，共{2}步。", start + 1, end, end - start), IsStandard = true, SupportedProducts = new List<string> { "C91" } };
                for (int stepIndex = start; stepIndex < end; stepIndex++) block.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object>(sequence.Steps[stepIndex].Properties, StringComparer.Ordinal) }); project.Blocks.Add(block); project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = block.Name, Phase = seed.Phase, PreserveStepNames = true, Snapshot = block.Clone(), ParameterOverrides = new Dictionary<string, object>(StringComparer.Ordinal) });
            }
            return project;
        }
        private static int Find(IReadOnlyList<SequenceStepDefinition> steps, string name, int start) { for (int index = Math.Max(0, start); index < steps.Count; index++) if (string.Equals(steps[index].StepName, name, StringComparison.OrdinalIgnoreCase)) return index; return -1; }
    }
}
