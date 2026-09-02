using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public sealed class FctStudioValidationResult
    {
        public FctStudioValidationResult() { Errors = new List<string>(); Warnings = new List<string>(); }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
        public bool IsValid { get { return Errors.Count == 0; } }
    }

    public static class FctStudioValidator
    {
        public static FctStudioValidationResult Validate(FctStudioProject project)
        {
            FctStudioValidationResult result = new FctStudioValidationResult();
            if (project == null) { result.Errors.Add("工程为空。"); return result; }
            List<FunctionBlockDefinition> blocks = project.Blocks ?? new List<FunctionBlockDefinition>();
            foreach (IGrouping<string, FunctionBlockDefinition> duplicate in blocks.Where(block => !string.IsNullOrWhiteSpace(block.Id)).GroupBy(block => block.Id).Where(group => group.Count() > 1)) result.Errors.Add("功能块ID重复：" + duplicate.Key);
            foreach (FunctionBlockDefinition block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Name)) result.Warnings.Add("模块库中存在未命名功能块，但未加入流程时不影响导出。");
                if (block.Steps == null || block.Steps.Count == 0) result.Warnings.Add("功能块没有小步骤：" + block.Name);
            }
            Dictionary<string, FunctionBlockDefinition> library = blocks.Where(block => !string.IsNullOrWhiteSpace(block.Id)).GroupBy(block => block.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (FunctionBlockDefinition block in blocks) ValidateModuleReferences(block, library, new HashSet<string>(StringComparer.Ordinal), result);
            if (project.Flow == null || project.Flow.Count == 0) result.Warnings.Add("当前流程为空，导出的SEQ不会包含STEP。");
            foreach (FlowBlockInstance instance in project.Flow ?? new List<FlowBlockInstance>())
            {
                if (instance.Snapshot == null && !blocks.Any(block => block.Id == instance.BlockId)) result.Errors.Add("流程引用了不存在的功能块：" + instance.DisplayName);
                FunctionBlockDefinition block = instance.Snapshot ?? blocks.FirstOrDefault(value => value.Id == instance.BlockId);
                if (block != null)
                {
                    ValidateFlowBlock(string.IsNullOrWhiteSpace(instance.DisplayName) ? block.Name : instance.DisplayName, block, instance.PreserveStepNames, result);
                    if (block.SupportedProducts != null && block.SupportedProducts.Count > 0 && !block.SupportedProducts.Contains(project.Product, StringComparer.OrdinalIgnoreCase)) result.Warnings.Add("功能块“" + instance.DisplayName + "”未声明支持产品" + project.Product + "。");
                }
            }
            if ((project.Flow ?? new List<FlowBlockInstance>()).Any(instance => instance.Enabled) && !ContainsSafeShutdown(project)) result.Warnings.Add("流程中没有显式“安全下电”步骤；正式运行仍会执行PostUUT，但建议保留可见的安全收尾功能块。");
            return result;
        }

        private static void ValidateFlowBlock(string displayName, FunctionBlockDefinition block, bool preserveStepNames, FctStudioValidationResult result)
        {
            string[] duplicateNames = (block.Steps ?? new List<BlockStepDefinition>()).Where(step => step.Enabled && !step.IsModuleReference).Select(step => step.ToStep().StepName).GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            HashSet<string> referencedTargets = new HashSet<string>((block.Steps ?? new List<BlockStepDefinition>()).Where(step => step.Enabled && !step.IsModuleReference).SelectMany(step => new[] { "TargetStepName", "TrueGoto", "FalseGoto" }.Select(key => Convert.ToString(step.ToStep().Get(key, string.Empty), CultureInfo.InvariantCulture))).Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal); string[] ambiguousNames = duplicateNames.Where(referencedTargets.Contains).ToArray(); if (ambiguousNames.Length > 0) result.Errors.Add("流程功能块“" + displayName + "”的跳转目标StepName重复：" + string.Join(", ", ambiguousNames)); else if (duplicateNames.Length > 0) result.Warnings.Add("重复动作名称将在导出时自动增加序号：" + displayName + " / " + string.Join(", ", duplicateNames));
            HashSet<string> names = new HashSet<string>((block.Steps ?? new List<BlockStepDefinition>()).Where(step => step.Enabled && !step.IsModuleReference).Select(step => step.ToStep().StepName), StringComparer.Ordinal);
            foreach (BlockStepDefinition blockStep in block.Steps ?? new List<BlockStepDefinition>())
            {
                if (!blockStep.Enabled) continue;
                if (blockStep.IsModuleReference) continue;
                SequenceStepDefinition step = blockStep.ToStep();
                if (string.IsNullOrWhiteSpace(step.FunctionName)) result.Errors.Add("流程功能块“" + displayName + "”存在没有FunctionName的步骤。");
                ValidateGenericStep(displayName, step, names, result);
                foreach (string binding in blockStep.ParameterBindings.Values) if (!(block.Parameters ?? new List<BlockParameterDefinition>()).Any(parameter => parameter.Name == binding)) result.Errors.Add("流程功能块“" + displayName + "”参数绑定不存在：" + binding);
            }
        }

        private static void ValidateModuleReferences(FunctionBlockDefinition block, IDictionary<string, FunctionBlockDefinition> library, ISet<string> stack, FctStudioValidationResult result)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.Id)) return; if (!stack.Add(block.Id)) { result.Errors.Add("功能块循环引用：" + block.Name); return; }
            foreach (BlockStepDefinition step in block.Steps ?? new List<BlockStepDefinition>()) if (step.Enabled && step.IsModuleReference) { FunctionBlockDefinition child; if (!library.TryGetValue(step.ReferencedBlockId, out child)) result.Errors.Add("功能块“" + block.Name + "”引用了不存在的标准模块：" + step.ReferencedBlockName); else if (child.Id == block.Id) result.Errors.Add("功能块不能引用自己：" + block.Name); else { foreach (string name in (step.ReferencedParameterOverrides ?? new Dictionary<string, object>()).Keys) if (!(child.Parameters ?? new List<BlockParameterDefinition>()).Any(parameter => parameter.Name == name)) result.Errors.Add("模块引用参数不存在：" + block.Name + " -> " + child.Name + "." + name); ValidateModuleReferences(child, library, stack, result); } }
            stack.Remove(block.Id);
        }

        private static void ValidateGenericStep(string blockName, SequenceStepDefinition step, ISet<string> localNames, FctStudioValidationResult result)
        {
            if (step.FunctionName == "FCT_ExecuteAction")
            {
                Require(blockName, step, "Device", result); Require(blockName, step, "Operation", result);
                string mode = Convert.ToString(step.Get("ResultMode", "Action"), CultureInfo.InvariantCulture);
                if (mode == "NumericLimit" && step.GetDouble("LowLimit") > step.GetDouble("HighLimit")) result.Errors.Add("功能块“" + blockName + "”数值LIMIT下限大于上限：" + step.StepName);
            }
            else if (step.FunctionName == "FCT_CANSignal") { Require(blockName, step, "Operation", result); Require(blockName, step, "AddrOffset", result); Require(blockName, step, "TableIndex", result); Require(blockName, step, "DataSize", result); Require(blockName, step, "DataType", result); }
            else if (step.FunctionName == "FCT_CANTable") { Require(blockName, step, "Operation", result); Require(blockName, step, "AddrOffset", result); if (step.GetInt("TableLength") <= 0) result.Errors.Add("整表STEP的TableLength必须大于0：" + blockName + "/" + step.StepName); }
            else if (step.FunctionName == "FCT_ExecuteLogic")
            {
                string operation = Convert.ToString(step.Get("Operation", string.Empty), CultureInfo.InvariantCulture);
                if (operation == "FixedLoop" && step.GetInt("Count") < 1) result.Errors.Add("固定循环次数必须大于0：" + blockName + "/" + step.StepName);
                foreach (string key in new[] { "TargetStepName", "TrueGoto", "FalseGoto" }) { string target = Convert.ToString(step.Get(key, string.Empty), CultureInfo.InvariantCulture); if (target.Length > 0 && !localNames.Contains(target)) result.Errors.Add("功能块“" + blockName + "”的跳转目标不存在：" + target); }
            }
        }

        private static void Require(string blockName, SequenceStepDefinition step, string property, FctStudioValidationResult result) { if (!step.Properties.ContainsKey(property) || step.Properties[property] == null || Convert.ToString(step.Properties[property], CultureInfo.InvariantCulture).Length == 0) result.Errors.Add("功能块“" + blockName + "”步骤“" + step.StepName + "”缺少参数" + property + "。"); }
        private static bool ContainsSafeShutdown(FctStudioProject project) { return project.Flow.Where(instance => instance.Enabled).Select(instance => instance.Snapshot ?? project.Blocks.FirstOrDefault(block => block.Id == instance.BlockId)).Where(block => block != null).SelectMany(block => block.Steps).Select(step => step.ToStep()).Any(step => step.FunctionName == "FCT_ExecuteLogic" && string.Equals(Convert.ToString(step.Get("Operation")), "SafeShutdown", StringComparison.OrdinalIgnoreCase)); }
    }
}
