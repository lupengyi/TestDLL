using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ManualCanDebug.Core
{
    public sealed class FctStudioProject
    {
        public FctStudioProject()
        {
            FormatVersion = 1;
            ProjectName = "FCT Studio Project";
            ProductLocatorPath = string.Empty;
            AuxiliaryDbcPath = string.Empty;
            DriveStructure = "SingleMainDrive";
            Capabilities = new List<string>();
            Blocks = new List<FunctionBlockDefinition>();
            Flow = new List<FlowBlockInstance>();
            Breakpoints = new List<string>();
            SequenceRoot = new Dictionary<string, object>(StringComparer.Ordinal);
        }
        public int FormatVersion { get; set; }
        public string ProjectName { get; set; }
        public string Product { get; set; }
        public string ProductLocatorPath { get; set; }
        public string AuxiliaryDbcPath { get; set; }
        public string DriveStructure { get; set; }
        public List<string> Capabilities { get; set; }
        public List<FunctionBlockDefinition> Blocks { get; set; }
        public List<FlowBlockInstance> Flow { get; set; }
        public List<string> Breakpoints { get; set; }
        public Dictionary<string, object> SequenceRoot { get; set; }
    }

    public sealed class FunctionBlockDefinition
    {
        public FunctionBlockDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "New Function Block";
            Category = "自定义";
            ModuleKind = string.Empty;
            Version = "1.0";
            Description = string.Empty;
            SupportedProducts = new List<string>();
            Parameters = new List<BlockParameterDefinition>();
            Steps = new List<BlockStepDefinition>();
        }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string ModuleKind { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public bool IsStandard { get; set; }
        public List<string> SupportedProducts { get; set; }
        public List<BlockParameterDefinition> Parameters { get; set; }
        public List<BlockStepDefinition> Steps { get; set; }
        public FunctionBlockDefinition Clone()
        {
            return new FunctionBlockDefinition
            {
                Id = Id, Name = Name, Category = Category, ModuleKind = ModuleKind, Version = Version, Description = Description, IsStandard = IsStandard,
                SupportedProducts = new List<string>(SupportedProducts ?? new List<string>()),
                Parameters = (Parameters ?? new List<BlockParameterDefinition>()).Select(parameter => parameter.Clone()).ToList(),
                Steps = (Steps ?? new List<BlockStepDefinition>()).Select(step => step.Clone()).ToList()
            };
        }
    }

    public sealed class BlockParameterDefinition
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public object DefaultValue { get; set; }
        public string Unit { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public BlockParameterDefinition Clone() { return new BlockParameterDefinition { Name = Name, DisplayName = DisplayName, Type = Type, DefaultValue = DefaultValue, Unit = Unit, Description = Description, Required = Required }; }
    }

    public sealed class BlockStepDefinition
    {
        public BlockStepDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            StepProperties = new Dictionary<string, object>(StringComparer.Ordinal);
            ParameterBindings = new Dictionary<string, string>(StringComparer.Ordinal);
            ReferencedParameterOverrides = new Dictionary<string, object>(StringComparer.Ordinal);
            Enabled = true;
        }
        public string Id { get; set; }
        public bool Enabled { get; set; }
        public string ReferencedBlockId { get; set; }
        public string ReferencedBlockName { get; set; }
        public bool IsModuleReference { get { return !string.IsNullOrWhiteSpace(ReferencedBlockId); } }
        public Dictionary<string, object> ReferencedParameterOverrides { get; set; }
        public Dictionary<string, object> StepProperties { get; set; }
        public Dictionary<string, string> ParameterBindings { get; set; }
        public SequenceStepDefinition ToStep() { return new SequenceStepDefinition(StepProperties ?? new Dictionary<string, object>()); }
        public BlockStepDefinition Clone()
        {
            return new BlockStepDefinition { Id = Id, Enabled = Enabled, ReferencedBlockId = ReferencedBlockId, ReferencedBlockName = ReferencedBlockName, ReferencedParameterOverrides = new Dictionary<string, object>(ReferencedParameterOverrides ?? new Dictionary<string, object>(), StringComparer.Ordinal), StepProperties = new Dictionary<string, object>(StepProperties ?? new Dictionary<string, object>(), StringComparer.Ordinal), ParameterBindings = new Dictionary<string, string>(ParameterBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal) };
        }
    }

    public sealed class FlowBlockInstance
    {
        public FlowBlockInstance()
        {
            Id = Guid.NewGuid().ToString("N");
            Enabled = true;
            ParameterOverrides = new Dictionary<string, object>(StringComparer.Ordinal);
        }
        public string Id { get; set; }
        public string BlockId { get; set; }
        public string DisplayName { get; set; }
        public string Phase { get; set; }
        public bool Enabled { get; set; }
        public bool PreserveStepNames { get; set; }
        public Dictionary<string, object> ParameterOverrides { get; set; }
        public FunctionBlockDefinition Snapshot { get; set; }
    }

    public sealed class FctStudioCompileResult
    {
        public FctStudioCompileResult(SequenceDocument document, IEnumerable<CompiledStepTrace> trace, IEnumerable<string> warnings)
        {
            Document = document;
            Trace = trace.ToList().AsReadOnly();
            Warnings = warnings.ToList().AsReadOnly();
        }
        public SequenceDocument Document { get; private set; }
        public IReadOnlyList<CompiledStepTrace> Trace { get; private set; }
        public IReadOnlyList<string> Warnings { get; private set; }
    }

    public sealed class CompiledStepTrace
    {
        public string FlowInstanceId { get; set; }
        public string BlockId { get; set; }
        public string BlockStepId { get; set; }
        public int SequenceIndex { get; set; }
        public string StepName { get; set; }
    }

    public static class FctStudioCompiler
    {
        public static FctStudioCompileResult Compile(FctStudioProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            FctStudioValidationResult validation = FctStudioValidator.Validate(project);
            if (!validation.IsValid) throw new InvalidOperationException("FCT Studio工程检查失败：\n" + string.Join("\n", validation.Errors));
            List<SequenceStepDefinition> output = new List<SequenceStepDefinition>();
            List<CompiledStepTrace> trace = new List<CompiledStepTrace>();
            List<string> warnings = new List<string>(validation.Warnings);
            Dictionary<string, FunctionBlockDefinition> library = (project.Blocks ?? new List<FunctionBlockDefinition>()).Where(block => !string.IsNullOrWhiteSpace(block.Id)).GroupBy(block => block.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            int flowNumber = 0;
            foreach (FlowBlockInstance instance in project.Flow ?? new List<FlowBlockInstance>())
            {
                flowNumber++;
                if (!instance.Enabled) continue;
                FunctionBlockDefinition block = instance.Snapshot;
                if (block == null && !string.IsNullOrWhiteSpace(instance.BlockId)) library.TryGetValue(instance.BlockId, out block);
                if (block == null) throw new InvalidOperationException("Flow references a missing block: " + instance.BlockId);
                string prefix = string.Format(CultureInfo.InvariantCulture, "{0:000}_{1}", flowNumber, SanitizeName(string.IsNullOrWhiteSpace(instance.DisplayName) ? block.Name : instance.DisplayName));
                CompileBlock(project, instance.Id, block, instance.ParameterOverrides, instance.PreserveStepNames, prefix, library, new HashSet<string>(StringComparer.Ordinal), output, trace);
            }
            Dictionary<string, object> root = new Dictionary<string, object>(project.SequenceRoot ?? new Dictionary<string, object>(), StringComparer.Ordinal);
            root["ProjectName"] = string.IsNullOrWhiteSpace(project.ProjectName) ? "FCT Studio Project" : project.ProjectName;
            if (!root.ContainsKey("StationName")) root["StationName"] = "DEBUG";
            if (!root.ContainsKey("SequenceVersion")) root["SequenceVersion"] = "FCT-STUDIO-1";
            if (!root.ContainsKey("UIDisplayType")) root["UIDisplayType"] = "All";
            if (!root.ContainsKey("SerialNumberLen")) root["SerialNumberLen"] = 0;
            if (!root.ContainsKey("LogFilePath")) root["LogFilePath"] = "D:\\LogfilePath";
            return new FctStudioCompileResult(new SequenceDocument(root, output), trace, warnings);
        }

        private static void CompileBlock(FctStudioProject project, string flowInstanceId, FunctionBlockDefinition block, IDictionary<string, object> overrides, bool preserveStepNames, string prefix, IDictionary<string, FunctionBlockDefinition> library, ISet<string> stack, IList<SequenceStepDefinition> output, IList<CompiledStepTrace> trace)
        {
            if (!stack.Add(block.Id)) throw new InvalidOperationException("功能块循环引用：" + block.Name);
            Dictionary<string, object> values = ResolveBlockValues(block, overrides);
            List<BlockStepDefinition> directSteps = block.Steps.Where(step => step.Enabled && !step.IsModuleReference).ToList();
            Dictionary<string, string> localNames = directSteps.Select(step => step.ToStep()).GroupBy(step => step.StepName, StringComparer.Ordinal).ToDictionary(group => group.Key, group => preserveStepNames ? group.Key : string.Equals(project.Product, "C96", StringComparison.OrdinalIgnoreCase) && PlatformVisible(group.First()) ? ShortPlatformName(prefix, group.Key) : prefix + " / " + group.Key, StringComparer.Ordinal);
            foreach (BlockStepDefinition blockStep in block.Steps.Where(step => step.Enabled))
            {
                if (blockStep.IsModuleReference)
                {
                    FunctionBlockDefinition child; if (!library.TryGetValue(blockStep.ReferencedBlockId, out child)) throw new InvalidOperationException("引用的标准模块不存在：" + blockStep.ReferencedBlockName);
                    CompileBlock(project, flowInstanceId, child, blockStep.ReferencedParameterOverrides, false, prefix + "-" + SanitizeName(child.Name), library, stack, output, trace); continue;
                }
                SequenceStepDefinition step = SequenceEditing.Clone(blockStep.ToStep());
                string calculation = Convert.ToString(step.Get("CalculationType"), CultureInfo.InvariantCulture); bool adaptiveCalculatedResult = step.FunctionName == "FCT_CANCalculatedResults" && (calculation == "PackedFaultStatus" || calculation == "ThreePhaseCurrentRms");
                if (adaptiveCalculatedResult) { int originalOffset = step.GetInt("AddrOffset", -1); string drive = Convert.ToString(step.Get("DriveTarget"), CultureInfo.InvariantCulture); if (string.IsNullOrWhiteSpace(drive)) drive = originalOffset == 0x84 || originalOffset == 0x94 ? "TM2" : "TM1"; step.Properties["AutoProductProfile"] = true; step.Properties["Product"] = project.Product; step.Properties["DriveTarget"] = drive; } else step.Properties.Remove("Product");
                ApplyBindings(step, blockStep.ParameterBindings, values); ApplyInterpolations(step, values); string originalName = step.StepName; step.StepName = localNames.ContainsKey(originalName) ? localNames[originalName] : prefix + " / " + originalName; RewriteLocalTargets(step, localNames); output.Add(step); trace.Add(new CompiledStepTrace { FlowInstanceId = flowInstanceId, BlockId = block.Id, BlockStepId = blockStep.Id, SequenceIndex = output.Count - 1, StepName = step.StepName });
            }
            stack.Remove(block.Id);
        }

        private static Dictionary<string, object> ResolveBlockValues(FunctionBlockDefinition block, IDictionary<string, object> overrides)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (BlockParameterDefinition parameter in block.Parameters ?? new List<BlockParameterDefinition>()) result[parameter.Name] = parameter.DefaultValue;
            if (overrides != null) foreach (KeyValuePair<string, object> pair in overrides) result[pair.Key] = pair.Value;
            foreach (BlockParameterDefinition parameter in block.Parameters.Where(parameter => parameter.Required)) if (!result.ContainsKey(parameter.Name) || result[parameter.Name] == null || Convert.ToString(result[parameter.Name], CultureInfo.InvariantCulture).Length == 0) throw new InvalidOperationException("Required block parameter is missing: " + block.Name + "." + parameter.Name);
            return result;
        }

        private static void ApplyBindings(SequenceStepDefinition step, IDictionary<string, string> bindings, IDictionary<string, object> values)
        {
            if (bindings == null) return;
            foreach (KeyValuePair<string, string> binding in bindings)
            {
                object value;
                if (!values.TryGetValue(binding.Value, out value)) throw new InvalidOperationException("Block parameter binding was not found: " + binding.Value);
                step.Properties[binding.Key] = value;
            }
        }

        private static bool PlatformVisible(SequenceStepDefinition step) { object value; return step.Properties.TryGetValue("RecordingLog", out value) && value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        private static string ShortPlatformName(string prefix, string originalName) { string number = new string((prefix ?? string.Empty).TakeWhile(char.IsDigit).ToArray()); if (number.Length > 2) number = number.TrimStart('0').PadLeft(2, '0'); string name = (originalName ?? string.Empty).Trim(); if (name.Length > 44) name = name.Substring(0, 44); return (number + " " + name).Trim(); }

        private static void RewriteLocalTargets(SequenceStepDefinition step, IDictionary<string, string> localNames)
        {
            foreach (string key in new[] { "TargetStepName", "TrueGoto", "FalseGoto" })
            {
                object value;
                if (!step.Properties.TryGetValue(key, out value) || value == null) continue;
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                string mapped;
                if (localNames.TryGetValue(text, out mapped)) step.Properties[key] = mapped;
            }
        }

        private static void ApplyInterpolations(SequenceStepDefinition step, IDictionary<string, object> values)
        {
            foreach (string key in step.Properties.Keys.ToList())
            {
                string text = step.Properties[key] as string; if (text == null || text.IndexOf("${", StringComparison.Ordinal) < 0) continue;
                text = Regex.Replace(text, @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\*(?<factor>[-+]?[0-9]+(?:\.[0-9]+)?))?\}", match =>
                {
                    object value; if (!values.TryGetValue(match.Groups["name"].Value, out value)) throw new InvalidOperationException("Block interpolation parameter was not found: " + match.Groups["name"].Value);
                    if (!match.Groups["factor"].Success) return Convert.ToString(value, CultureInfo.InvariantCulture);
                    return (Convert.ToDouble(value, CultureInfo.InvariantCulture) * double.Parse(match.Groups["factor"].Value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                });
                step.Properties[key] = text;
            }
        }

        private static string SanitizeName(string name)
        {
            string result = (name ?? string.Empty).Trim().Replace("/", "-").Replace("\\", "-");
            return result.Length == 0 ? "Block" : result;
        }
    }
}
