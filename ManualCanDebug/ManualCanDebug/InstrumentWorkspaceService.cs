using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace ManualCanDebug
{
    internal sealed class InstrumentWorkspaceDocument
    {
        public InstrumentWorkspaceDocument()
        {
            Version = InstrumentWorkspaceService.SchemaVersion; StationCount = 1; Instruments = new List<ProjectInstrumentDefinition>(); Stations = new List<StationInstrumentDefinition>();
        }
        public int Version { get; set; }
        public int StationCount { get; set; }
        public List<ProjectInstrumentDefinition> Instruments { get; set; }
        public List<StationInstrumentDefinition> Stations { get; set; }
    }

    internal sealed class ProjectInstrumentDefinition : INotifyPropertyChanged
    {
        private string _displayName, _driverName, _resource, _parameter, _usage, _category;
        private bool _exclusiveAccess = true;
        private int _lockTimeoutMs = 30000;
        public ProjectInstrumentDefinition() { Id = Guid.NewGuid().ToString("N"); GeneratedMethods = new ObservableCollection<GeneratedInstrumentMethod>(); }
        public string Id { get; set; }
        public string Device { get; set; }
        public string DisplayName { get { return _displayName; } set { _displayName = value ?? string.Empty; Raise("DisplayName"); } }
        public string DriverName { get { return _driverName; } set { _driverName = value ?? string.Empty; Raise("DriverName"); } }
        public string Resource { get { return _resource; } set { _resource = value ?? string.Empty; Raise("Resource"); } }
        public string Parameter { get { return _parameter; } set { _parameter = value ?? string.Empty; Raise("Parameter"); } }
        public string Usage { get { return _usage; } set { _usage = value ?? "Independent"; Raise("Usage"); Raise("UsageText"); } }
        public int ChannelCount { get; set; }
        public string Category { get { return _category; } set { _category = value ?? string.Empty; Raise("Category"); } }
        public string DriverAssemblyPath { get; set; }
        public string DriverTypeName { get; set; }
        public bool ExclusiveAccess { get { return _exclusiveAccess; } set { _exclusiveAccess = value; Raise("ExclusiveAccess"); } }
        public int LockTimeoutMs { get { return _lockTimeoutMs; } set { _lockTimeoutMs = value <= 0 ? 30000 : value; Raise("LockTimeoutMs"); } }
        public ObservableCollection<GeneratedInstrumentMethod> GeneratedMethods { get; set; }
        public string UsageText { get { return string.Equals(Usage, "Shared", StringComparison.OrdinalIgnoreCase) ? "共用仪器" : "独立仪器（每工位1台）"; } }
        public bool IsShared { get { return string.Equals(Usage, "Shared", StringComparison.OrdinalIgnoreCase); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class GeneratedInstrumentMethod : INotifyPropertyChanged
    {
        private bool _selected; private string _displayName, _functionName;
        public string Device { get; set; }
        public string Operation { get; set; }
        public string DriverMethod { get; set; }
        public string DriverAssemblyPath { get; set; }
        public string DriverTypeName { get; set; }
        public bool UseDirectReflection { get; set; }
        public bool ReturnsValue { get; set; }
        public bool Selected { get { return _selected; } set { _selected = value; Raise("Selected"); Raise("ResultText"); } }
        public string DisplayName { get { return _displayName; } set { _displayName = value ?? string.Empty; Raise("DisplayName"); } }
        public string FunctionName { get { return _functionName; } set { _functionName = value ?? string.Empty; Raise("FunctionName"); } }
        public List<InstrumentActionFieldDefinition> Fields { get; set; }
        public string ResultText { get { return Selected && !string.IsNullOrWhiteSpace(FunctionName) ? "生成成功" : "未生成"; } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class StationInstrumentDefinition
    {
        public StationInstrumentDefinition() { IndependentDevices = new List<StationInstrumentInstance>(); SharedBindings = new List<StationSharedBinding>(); }
        public int StationNumber { get; set; }
        public List<StationInstrumentInstance> IndependentDevices { get; set; }
        public List<StationSharedBinding> SharedBindings { get; set; }
        public string PowerInstrumentId { get; set; }
        public string PowerChannelGroup { get; set; }
        public string PlcInstrumentId { get; set; }
        public int PlcDbOffset { get; set; }
        public string StationName { get { return "工位" + StationNumber.ToString("00", CultureInfo.InvariantCulture); } }
    }

    /// <summary>
    /// Binds one shared instrument to one station and records how MainTest must translate a
    /// station-neutral request into the physical channel, PLC data-block window or switch path
    /// that belongs to this station.
    /// </summary>
    internal sealed class StationSharedBinding : INotifyPropertyChanged
    {
        private string _channel, _pathIndex, _note;
        private int _channelOffset, _dbOffset;
        public string InstrumentId { get; set; }
        public string Device { get; set; }
        public string Channel { get { return _channel; } set { _channel = value ?? string.Empty; Raise("Channel"); Raise("Summary"); } }
        public int ChannelOffset { get { return _channelOffset; } set { _channelOffset = value; Raise("ChannelOffset"); Raise("Summary"); } }
        public int DbOffset { get { return _dbOffset; } set { _dbOffset = value; Raise("DbOffset"); Raise("Summary"); } }
        public string PathIndex { get { return _pathIndex; } set { _pathIndex = value ?? string.Empty; Raise("PathIndex"); Raise("Summary"); } }
        public string Note { get { return _note; } set { _note = value ?? string.Empty; Raise("Note"); } }
        public string Summary
        {
            get
            {
                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Channel)) parts.Add("通道 " + Channel);
                if (ChannelOffset != 0) parts.Add("通道偏移 +" + ChannelOffset.ToString(CultureInfo.InvariantCulture));
                if (DbOffset != 0) parts.Add("DB+" + DbOffset.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(PathIndex)) parts.Add("路径 " + PathIndex);
                return parts.Count == 0 ? "无映射" : string.Join("，", parts);
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class StationInstrumentInstance
    {
        public StationInstrumentInstance() { Enabled = true; }
        public string TemplateDevice { get; set; }
        public string InstanceName { get; set; }
        public string Resource { get; set; }
        public string Parameter { get; set; }
        public bool Enabled { get; set; }
    }

    internal sealed class DriverDiscoveryItem
    {
        public string AssemblyName { get; set; }
        public string Path { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public string LoadError { get; set; }
        public List<DriverMethodDiscovery> Methods { get; set; }
        public string ShortTypeName { get { return string.IsNullOrWhiteSpace(TypeName) ? string.Empty : TypeName.Substring(TypeName.LastIndexOf('.') + 1); } }
    }

    internal sealed class DriverMethodDiscovery
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<DriverParameterDiscovery> Parameters { get; set; }
    }
    internal sealed class DriverParameterDiscovery
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string DefaultValue { get; set; }
    }

    internal sealed class WorkspaceConflict
    {
        public string Resource { get; set; }
        public string Message { get; set; }
        public override string ToString() { return Resource + "：" + Message; }
    }

    internal sealed class InstrumentWorkspaceService
    {
        /// <summary>Version 5 introduced instrument categories, driver binding paths, per-instrument
        /// concurrency policy and generic shared-instrument station bindings.</summary>
        public const int SchemaVersion = 5;

        private readonly string _baseDirectory;
        private readonly string _workspaceRoot;
        private readonly string _configPath;
        private readonly string _generatedSourcePath;
        public InstrumentWorkspaceService(string baseDirectory)
        {
            _baseDirectory = baseDirectory;
            _workspaceRoot = LocateWorkspaceRoot(baseDirectory);
            _configPath = Path.Combine(_workspaceRoot, "Config", "InstrumentWorkspace.json");
            _generatedSourcePath = Path.Combine(_workspaceRoot, "TestDLL", "TestDllMain.Generated.Instruments.cs");
        }

        public string ConfigPath { get { return _configPath; } }
        public string GeneratedSourcePath { get { return _generatedSourcePath; } }
        public string WorkspaceRoot { get { return _workspaceRoot; } }
        public string DriverDirectory { get { return Path.Combine(_workspaceRoot, "DLLs"); } }

        public InstrumentWorkspaceDocument Load()
        {
            InstrumentWorkspaceDocument document = null;
            try { if (File.Exists(_configPath)) document = JsonConvert.DeserializeObject<InstrumentWorkspaceDocument>(File.ReadAllText(_configPath)); } catch { }
            if (document == null || document.Version < 4) document = CreateDefault();
            if (document.Version < SchemaVersion) { MigrateToVersion5(document); document.Version = SchemaVersion; }
            Normalize(document);
            ReconcileWithCurrentProject(document);
            return document;
        }

        public void Save(InstrumentWorkspaceDocument document)
        {
            Normalize(document);
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, JsonConvert.SerializeObject(document, Formatting.Indented));
            SyncCurrentProjectConfig(document);
            string runtimeConfig = Path.Combine(_baseDirectory, "Config", "InstrumentWorkspace.json");
            try { Directory.CreateDirectory(Path.GetDirectoryName(runtimeConfig)); File.WriteAllText(runtimeConfig, JsonConvert.SerializeObject(document, Formatting.Indented)); } catch { }
            foreach (string path in RuntimeTopologyTargets()) { try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonConvert.SerializeObject(document, Formatting.Indented)); } catch { } }
        }

        /// <summary>
        /// MainTest resolves the station topology relative to its own assembly, so the saved document
        /// has to reach every folder the platform may launch CSP.TestDLL.dll from.
        /// </summary>
        private IEnumerable<string> RuntimeTopologyTargets()
        {
            yield return Path.Combine(_baseDirectory, "LegacyRuntime", "Config", "InstrumentWorkspace.json");
            yield return Path.Combine(_workspaceRoot, "TestDLL", "bin", "Config", "InstrumentWorkspace.json");
        }

        private static void MigrateToVersion5(InstrumentWorkspaceDocument document)
        {
            foreach (StationInstrumentDefinition station in document.Stations ?? new List<StationInstrumentDefinition>())
            {
                station.SharedBindings = station.SharedBindings ?? new List<StationSharedBinding>();
                if (!string.IsNullOrWhiteSpace(station.PlcInstrumentId) && !station.SharedBindings.Any(v => v.InstrumentId == station.PlcInstrumentId))
                    station.SharedBindings.Add(new StationSharedBinding { InstrumentId = station.PlcInstrumentId, Device = "PLC", DbOffset = station.PlcDbOffset });
                if (!string.IsNullOrWhiteSpace(station.PowerInstrumentId) && !station.SharedBindings.Any(v => v.InstrumentId == station.PowerInstrumentId))
                    station.SharedBindings.Add(new StationSharedBinding { InstrumentId = station.PowerInstrumentId, Device = "LVDC", Channel = station.PowerChannelGroup });
            }
        }

        private void SyncCurrentProjectConfig(InstrumentWorkspaceDocument document)
        {
            string path = Path.Combine(_workspaceRoot, "Config", "InstrumentConfig.json"); if (!File.Exists(path)) return;
            try
            {
                Newtonsoft.Json.Linq.JArray values = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (ProjectInstrumentDefinition instrument in document.Instruments)
                {
                    Newtonsoft.Json.Linq.JObject row = values.OfType<Newtonsoft.Json.Linq.JObject>().FirstOrDefault(item => string.Equals((string)item["Name"], instrument.Device, StringComparison.OrdinalIgnoreCase));
                    if (row == null) continue; row["Resource"] = instrument.Resource ?? string.Empty; row["Parameter"] = instrument.Parameter ?? string.Empty;
                }
                File.WriteAllText(path, values.ToString());
            }
            catch { }
        }

        public List<DriverDiscoveryItem> ScanDrivers()
        {
            List<DriverDiscoveryItem> result = new List<DriverDiscoveryItem>();
            if (!Directory.Exists(DriverDirectory)) return result;
            foreach (string file in Directory.GetFiles(DriverDirectory, "*.dll").OrderBy(Path.GetFileName))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!IsInstrumentAssembly(name)) continue;
                List<DriverMethodDiscovery> methods = new List<DriverMethodDiscovery>(); string typeName = string.Empty; string loadError = string.Empty;
                try
                {
                    Assembly assembly = Assembly.LoadFrom(file);
                    Type driverType = assembly.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract).OrderByDescending(t => t.GetConstructor(Type.EmptyTypes) != null).ThenBy(t => t.FullName).FirstOrDefault();
                    if (driverType != null)
                    {
                        typeName = driverType.FullName;
                        methods = driverType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && !m.ContainsGenericParameters && !m.GetParameters().Any(p => p.IsOut || p.ParameterType.IsByRef))
                            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(m => m.GetParameters().Length).First()).OrderBy(m => m.Name)
                            .Select(m => new DriverMethodDiscovery { Name = m.Name, ReturnType = m.ReturnType.FullName, Parameters = m.GetParameters().Select(p => new DriverParameterDiscovery { Name = p.Name, TypeName = p.ParameterType.FullName, DefaultValue = p.HasDefaultValue && p.DefaultValue != null ? Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) : DefaultForType(p.ParameterType) }).ToList() }).ToList();
                    }
                    else loadError = "程序集内没有可实例化的公开驱动类型。";
                }
                catch (Exception ex) { loadError = ex.Message; }
                result.Add(new DriverDiscoveryItem { AssemblyName = name, Path = file, TypeName = typeName, Methods = methods, Category = ClassifyDriver(name, typeName), LoadError = loadError });
            }
            return result;
        }

        public const string CategoryPower = "电源";
        public const string CategoryCommunication = "通信";
        public const string CategoryMeasurement = "测量采集";
        public const string CategorySwitching = "开关切换";
        public const string CategoryLoad = "负载";
        public const string CategoryController = "控制器";
        public const string CategoryOther = "其他";
        public static readonly string[] Categories = { CategoryPower, CategoryCommunication, CategoryMeasurement, CategorySwitching, CategoryLoad, CategoryController, CategoryOther };

        /// <summary>Classifies a discovered driver assembly so the instrument center can group it.</summary>
        public static string ClassifyDriver(string assemblyName, string typeName)
        {
            string value = ((assemblyName ?? string.Empty) + " " + (typeName ?? string.Empty)).ToUpperInvariant();
            if (value.Contains("POWERSUPPLY") || value.Contains("IT6") || value.Contains("KEWELL") || value.Contains("EA_PS")) return CategoryPower;
            if (value.Contains("CAN") || value.Contains("MOXA") || value.Contains("VISA")) return CategoryCommunication;
            if (value.Contains("DMM") || value.Contains("DAQ") || value.Contains("34461") || value.Contains("6229") || value.Contains("9227")) return CategoryMeasurement;
            if (value.Contains("SHT_48SEDO") || value.Contains("RELAY") || value.Contains("MUX")) return CategorySwitching;
            if (value.Contains("LOAD") || value.Contains("AN23600") || value.Contains("RESISTANCE")) return CategoryLoad;
            if (value.Contains("PLC") || value.Contains("S7")) return CategoryController;
            return CategoryOther;
        }

        /// <summary>Classifies a configured instrument, preferring its logical device name.</summary>
        public static string ClassifyDevice(string device, string driverName)
        {
            switch ((device ?? string.Empty).ToUpperInvariant())
            {
                case "LVDC": case "LVDC_KL15": case "HVDC": return CategoryPower;
                case "DUTCAN": case "AUXCAN": case "RESOLVERCAN": case "RESOLVER": case "PRODUCTCAN": return CategoryCommunication;
                case "DMM": case "DMM_HV": case "DMM_LV": case "DAQ": return CategoryMeasurement;
                case "RELAY": case "RELAY_FCT": case "RELAY_HVMUX": return CategorySwitching;
                case "DCDC_LOAD": case "RES": case "RES_1": case "RES_2": case "RES_3": return CategoryLoad;
                case "PLC": return CategoryController;
            }
            return ClassifyDriver(driverName, device);
        }

        public List<WorkspaceConflict> Validate(InstrumentWorkspaceDocument document)
        {
            List<WorkspaceConflict> conflicts = new List<WorkspaceConflict>();
            Dictionary<string, ProjectInstrumentDefinition> byId = document.Instruments.Where(v => !string.IsNullOrWhiteSpace(v.Id)).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());

            // Two stations must never drive the same physical channel or PLC window of one shared instrument.
            var bindings = document.Stations.SelectMany(station => (station.SharedBindings ?? new List<StationSharedBinding>()).Select(binding => new { Station = station, Binding = binding })).ToList();
            foreach (var group in bindings.Where(v => !string.IsNullOrWhiteSpace(v.Binding.Channel)).GroupBy(v => v.Binding.InstrumentId + "|" + v.Binding.Channel, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                conflicts.Add(new WorkspaceConflict { Resource = DescribeBinding(byId, group.First().Binding) + " 通道 " + group.First().Binding.Channel, Message = "同一通道被多个工位占用：" + string.Join("、", group.Select(v => v.Station.StationName)) });
            foreach (var group in bindings.Where(v => v.Binding.DbOffset != 0 || string.Equals(v.Binding.Device, "PLC", StringComparison.OrdinalIgnoreCase)).GroupBy(v => v.Binding.InstrumentId + "|" + v.Binding.DbOffset.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                conflicts.Add(new WorkspaceConflict { Resource = DescribeBinding(byId, group.First().Binding) + " DB+" + group.First().Binding.DbOffset, Message = "DB偏移重复：" + string.Join("、", group.Select(v => v.Station.StationName)) });
            foreach (var group in bindings.Where(v => !string.IsNullOrWhiteSpace(v.Binding.PathIndex)).GroupBy(v => v.Binding.InstrumentId + "|" + v.Binding.PathIndex, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                conflicts.Add(new WorkspaceConflict { Resource = DescribeBinding(byId, group.First().Binding) + " 路径 " + group.First().Binding.PathIndex, Message = "切换路径重复：" + string.Join("、", group.Select(v => v.Station.StationName)) });
            foreach (var binding in bindings.Where(v => !byId.ContainsKey(v.Binding.InstrumentId ?? string.Empty)))
                conflicts.Add(new WorkspaceConflict { Resource = binding.Station.StationName, Message = "共用仪器连接指向了已删除的仪器定义。" });

            foreach (StationInstrumentDefinition station in document.Stations)
            {
                foreach (string duplicate in station.IndependentDevices.GroupBy(v => v.TemplateDevice, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key))
                    conflicts.Add(new WorkspaceConflict { Resource = station.StationName, Message = "独立仪器重复：" + duplicate });
                foreach (StationInstrumentInstance instance in station.IndependentDevices.Where(v => v.Enabled && string.IsNullOrWhiteSpace(v.Resource)))
                    conflicts.Add(new WorkspaceConflict { Resource = station.StationName + " / " + instance.TemplateDevice, Message = "独立仪器没有填写连接资源，该工位无法初始化这台仪器。" });
            }

            // Every independent template should exist on every station, otherwise a SEQ written for one
            // station silently fails on another.
            foreach (ProjectInstrumentDefinition template in document.Instruments.Where(v => !v.IsShared))
            {
                List<string> missing = document.Stations.Where(s => !s.IndependentDevices.Any(v => string.Equals(v.TemplateDevice, template.Device, StringComparison.OrdinalIgnoreCase))).Select(s => s.StationName).ToList();
                if (missing.Count > 0 && missing.Count < document.Stations.Count) conflicts.Add(new WorkspaceConflict { Resource = template.DisplayName, Message = "以下工位尚未分配该独立仪器：" + string.Join("、", missing) });
            }
            return conflicts;
        }

        private static string DescribeBinding(Dictionary<string, ProjectInstrumentDefinition> byId, StationSharedBinding binding)
        {
            ProjectInstrumentDefinition instrument; return byId.TryGetValue(binding.InstrumentId ?? string.Empty, out instrument) ? instrument.DisplayName : (binding.Device ?? "共用仪器");
        }

        public int GenerateMethods(InstrumentWorkspaceDocument document)
        {
            List<ProjectInstrumentDefinition> instruments = document.Instruments.Where(i => i.GeneratedMethods != null).ToList();
            List<GeneratedInstrumentMethod> methods = instruments.SelectMany(i => i.GeneratedMethods.Where(m => m.Selected)).GroupBy(m => SanitizeIdentifier(m.FunctionName), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            StringBuilder source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// Generated by FCT Engineering Studio instrument center. Do not edit by hand.");
            source.AppendLine("namespace CSP");
            source.AppendLine("{");
            source.AppendLine("    public partial class TestDllMain");
            source.AppendLine("    {");
            foreach (GeneratedInstrumentMethod method in methods.OrderBy(m => m.FunctionName, StringComparer.OrdinalIgnoreCase))
            {
                source.Append("        public void ").Append(SanitizeIdentifier(method.FunctionName)).AppendLine("(int socketIndex)");
                source.AppendLine("        {");
                if (method.UseDirectReflection) source.Append("            FCT_ExecuteGeneratedDriverMethod(socketIndex, \"").Append(Escape(method.DriverAssemblyPath)).Append("\", \"").Append(Escape(method.DriverTypeName)).Append("\", \"").Append(Escape(method.DriverMethod)).AppendLine("\");");
                else source.Append("            FCT_ExecuteConfiguredAction(socketIndex, \"").Append(Escape(method.Device)).Append("\", \"").Append(Escape(method.Operation)).AppendLine("\");");
                source.AppendLine("        }");
                source.AppendLine();
            }
            source.AppendLine("    }");
            source.AppendLine("}");
            File.WriteAllText(_generatedSourcePath, source.ToString(), Encoding.UTF8);

            List<InstrumentActionDefinition> definitions = ActionCatalog.LoadDefinitions().Where(d => !(string.Equals(d.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase) && (d.FunctionName ?? string.Empty).StartsWith("UI_", StringComparison.OrdinalIgnoreCase))).ToList();
            HashSet<string> generatedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectInstrumentDefinition instrument in instruments)
            {
                foreach (GeneratedInstrumentMethod method in instrument.GeneratedMethods.Where(m => m.Selected))
                {
                    if (!generatedFunctions.Add(SanitizeIdentifier(method.FunctionName))) continue;
                    definitions.Add(new InstrumentActionDefinition
                    {
                        Source = "仪器", Target = instrument.DisplayName, Device = method.Device, Operation = method.Operation,
                        DisplayName = method.DisplayName, ReturnsValue = method.ReturnsValue, BindingMode = "MainTest",
                        FunctionName = SanitizeIdentifier(method.FunctionName), Fields = CloneFields(method.Fields)
                    });
                }
            }
            ActionCatalog.SaveDefinitions(definitions);
            string canonicalActions = Path.Combine(_workspaceRoot, "Config", "InstrumentActions.json");
            File.WriteAllText(canonicalActions, JsonConvert.SerializeObject(definitions, Formatting.Indented));
            return methods.Count;
        }

        /// <summary>
        /// Compiles TestDLL after the instrument center regenerated its wrappers, so the operator never
        /// has to open Visual Studio. Returns the MSBuild log; <paramref name="succeeded"/> reports the result.
        /// </summary>
        public string BuildTestDll(out bool succeeded)
        {
            succeeded = false;
            string project = Path.Combine(_workspaceRoot, "TestDLL", "TestDLL.csproj");
            if (!File.Exists(project)) return "找不到 TestDLL 工程文件：" + project;
            string msbuild = LocateMsBuild();
            if (msbuild == null) return "本机没有找到 MSBuild.exe。请安装 Visual Studio 生成工具后重试，或手动编译 TestDLL。";
            try
            {
                System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(msbuild, "\"" + project + "\" /t:Build /p:Configuration=Debug /nologo /v:minimal")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = _workspaceRoot
                };
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(300000)) { try { process.Kill(); } catch { } return "编译超时（5 分钟）。" + Environment.NewLine + output; }
                    succeeded = process.ExitCode == 0;
                    return string.IsNullOrWhiteSpace(output) ? (succeeded ? "编译成功。" : "编译失败，MSBuild 没有输出。") : output.Trim();
                }
            }
            catch (Exception ex) { return "调用 MSBuild 失败：" + ex.Message; }
        }

        /// <summary>Copies the freshly built CSP.TestDLL.dll next to every runtime that loads it.</summary>
        public string PublishTestDll()
        {
            string source = Path.Combine(_workspaceRoot, "TestDLL", "bin", "Debug", "CSP.TestDLL.dll");
            if (!File.Exists(source)) source = Path.Combine(_workspaceRoot, "TestDLL", "bin", "CSP.TestDLL.dll");
            if (!File.Exists(source)) return "没有找到编译输出 CSP.TestDLL.dll，已跳过热替换。";
            List<string> copied = new List<string>(); List<string> locked = new List<string>();
            foreach (string target in new[] { Path.Combine(_baseDirectory, "CSP.TestDLL.dll"), Path.Combine(_baseDirectory, "LegacyRuntime", "CSP.TestDLL.dll") })
            {
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(target))) continue;
                    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(source, target, true); copied.Add(target);
                }
                // The platform keeps CSP.TestDLL.dll loaded while a project is initialised, so a locked
                // file is expected and only means the operator has to re-initialise before it takes effect.
                catch (Exception ex) { locked.Add(target + "（" + ex.Message + "）"); }
            }
            StringBuilder result = new StringBuilder();
            if (copied.Count > 0) result.AppendLine("已更新：" + string.Join("、", copied));
            if (locked.Count > 0) result.AppendLine("以下位置正在被占用，请先安全下电并重新初始化项目：" + string.Join("、", locked));
            return result.Length == 0 ? "无需热替换。" : result.ToString().Trim();
        }

        private static string LocateMsBuild()
        {
            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string vswhere = Path.Combine(programFiles ?? string.Empty, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (File.Exists(vswhere))
            {
                try
                {
                    System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(vswhere, "-latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe")
                    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                    using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
                    {
                        string found = process.StandardOutput.ReadToEnd(); process.WaitForExit(20000);
                        string first = (found ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(File.Exists);
                        if (first != null) return first;
                    }
                }
                catch { }
            }
            string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319", "MSBuild.exe");
            return File.Exists(framework) ? framework : null;
        }

        public static string SanitizeIdentifier(string value)
        {
            string input = string.IsNullOrWhiteSpace(value) ? "GeneratedInstrumentAction" : value.Trim();
            StringBuilder result = new StringBuilder();
            foreach (char ch in input) result.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            if (result.Length == 0 || char.IsDigit(result[0])) result.Insert(0, '_');
            return result.ToString();
        }

        private InstrumentWorkspaceDocument CreateDefault()
        {
            InstrumentWorkspaceDocument document = new InstrumentWorkspaceDocument { Version = 4, StationCount = 1 };
            List<ProjectInstrumentDefinition> actual = LoadCurrentProjectInstruments();
            foreach (ProjectInstrumentDefinition item in actual) document.Instruments.Add(item);
            if (document.Instruments.Count == 0)
            {
                document.Instruments.Add(NewInstrument("PLC", "PLC", "Instruments.PLCS7", string.Empty, "30", "Shared", 1));
                document.Instruments.Add(NewInstrument("LVDC", "LVDC", "Instruments.PowerSupply.ITECH_IT6XXXC", string.Empty, string.Empty, "Independent", 1));
                foreach (Tuple<string, string, string> item in DefaultIndependentTemplates()) document.Instruments.Add(NewInstrument(item.Item1, item.Item2, item.Item3, string.Empty, string.Empty, "Independent", 1));
            }
            StationInstrumentDefinition station = new StationInstrumentDefinition
            {
                StationNumber = 1,
                IndependentDevices = document.Instruments.Where(i => !i.IsShared).Select(i => new StationInstrumentInstance { TemplateDevice = i.Device, InstanceName = i.DisplayName + "-01", Resource = i.Resource, Parameter = i.Parameter }).ToList(),
                SharedBindings = document.Instruments.Where(i => i.IsShared).Select(i => new StationSharedBinding { InstrumentId = i.Id, Device = i.Device, Channel = i.ChannelCount >= 2 ? "CH1" : string.Empty }).ToList()
            };
            document.Stations.Add(station);
            return document;
        }

        private List<ProjectInstrumentDefinition> LoadCurrentProjectInstruments()
        {
            List<ProjectInstrumentDefinition> result = new List<ProjectInstrumentDefinition>();
            string path = Path.Combine(_workspaceRoot, "Config", "InstrumentConfig.json");
            if (!File.Exists(path)) return result;
            try
            {
                Newtonsoft.Json.Linq.JArray items = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (Newtonsoft.Json.Linq.JObject item in items.OfType<Newtonsoft.Json.Linq.JObject>())
                {
                    string device = ((string)item["Name"] ?? string.Empty).Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(device)) continue;
                    string mode = (string)item["Mode"] ?? string.Empty;
                    string driver = ResolveDriverName(device, mode);
                    bool shared = device == "PLC";
                    int channels = device == "LVDC" || device == "LVDC_KL15" ? 1 : device == "PLC" ? 1 : 1;
                    result.Add(NewInstrument(device, device, driver, (string)item["Resource"] ?? string.Empty, (string)item["Parameter"] ?? string.Empty, shared ? "Shared" : "Independent", channels));
                }
            }
            catch { }
            return result;
        }

        private void ReconcileWithCurrentProject(InstrumentWorkspaceDocument document)
        {
            List<ProjectInstrumentDefinition> actual = LoadCurrentProjectInstruments(); if (actual.Count == 0) return;
            List<ProjectInstrumentDefinition> unified = new List<ProjectInstrumentDefinition>();
            foreach (ProjectInstrumentDefinition source in actual)
            {
                ProjectInstrumentDefinition existing = document.Instruments.FirstOrDefault(value => string.Equals(value.Device, source.Device, StringComparison.OrdinalIgnoreCase));
                // Usage, category and concurrency policy are user decisions made in the instrument center;
                // only the connection facts come back from the platform instrument list.
                if (existing == null) existing = source; else { existing.DriverName = source.DriverName; existing.Resource = source.Resource; existing.Parameter = source.Parameter; if (string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = source.DisplayName; if (string.IsNullOrWhiteSpace(existing.Category)) existing.Category = source.Category; if (existing.ChannelCount <= 0) existing.ChannelCount = source.ChannelCount; if (existing.GeneratedMethods == null || existing.GeneratedMethods.Count == 0) existing.GeneratedMethods = source.GeneratedMethods; }
                unified.Add(existing);
            }
            foreach (ProjectInstrumentDefinition extra in document.Instruments.Where(value => !unified.Any(item => string.Equals(item.Device, value.Device, StringComparison.OrdinalIgnoreCase)))) unified.Add(extra);
            document.Instruments = unified;
            HashSet<string> independent = new HashSet<string>(unified.Where(value => !value.IsShared).Select(value => value.Device), StringComparer.OrdinalIgnoreCase);
            foreach (StationInstrumentDefinition station in document.Stations)
            {
                station.IndependentDevices = station.IndependentDevices.Where(value => independent.Contains(value.TemplateDevice)).GroupBy(value => value.TemplateDevice, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
                foreach (ProjectInstrumentDefinition template in unified.Where(value => !value.IsShared && !station.IndependentDevices.Any(item => string.Equals(item.TemplateDevice, value.Device, StringComparison.OrdinalIgnoreCase)))) station.IndependentDevices.Add(new StationInstrumentInstance { TemplateDevice = template.Device, InstanceName = template.DisplayName + "-" + station.StationNumber.ToString("00", CultureInfo.InvariantCulture), Resource = template.Resource, Parameter = template.Parameter });
            }
        }

        private static string ResolveDriverName(string device, string mode)
        {
            switch (device)
            {
                case "DUTCAN": case "AUXCAN": case "RESOLVERCAN": return "Instruments.CAN.CANWrapper";
                case "LVDC": case "LVDC_KL15": return "Instruments.PowerSupply.ITECH_IT6XXXC";
                case "HVDC": return "Instruments.PowerSupply.Kewell_C3000";
                case "DMM": case "DMM_HV": case "DMM_LV": return "Instruments.DMM.KeySight34461A";
                case "RES": case "RES_1": case "RES_2": case "RES_3": return "Instruments.Other.NGI_ProgramResistance";
                case "DAQ": return "NI-9227";
                case "RELAY_FCT": case "RELAY_HVMUX": return "SHT_48SEDO_A";
                case "DCDC_LOAD": return "Instruments.Load.AN23600E";
                case "PLC": return "Instruments.PLCS7";
                default: return mode ?? string.Empty;
            }
        }

        private ProjectInstrumentDefinition NewInstrument(string name, string device, string driver, string resource, string parameter, string usage, int channels)
        {
            ProjectInstrumentDefinition value = new ProjectInstrumentDefinition { DisplayName = name, Device = device, DriverName = driver, Resource = resource, Parameter = parameter, Usage = usage, ChannelCount = channels, Category = ClassifyDevice(device, driver) };
            value.GeneratedMethods = CreateMethods(device, name);
            return value;
        }

        private static ObservableCollection<GeneratedInstrumentMethod> CreateMethods(string device, string name)
        {
            ObservableCollection<GeneratedInstrumentMethod> methods = new ObservableCollection<GeneratedInstrumentMethod>();
            foreach (ActionDescriptor descriptor in ActionCatalog.AllDescriptors.Where(d => string.Equals(d.Device, device, StringComparison.OrdinalIgnoreCase) && !(string.Equals(d.BindingMode, "MainTest", StringComparison.OrdinalIgnoreCase) && (d.FunctionName ?? string.Empty).StartsWith("UI_", StringComparison.OrdinalIgnoreCase))))
            {
                string function = "UI_" + SanitizeIdentifier(device) + "_" + SanitizeIdentifier(descriptor.Operation);
                methods.Add(new GeneratedInstrumentMethod { Device = device, Operation = descriptor.Operation, DriverMethod = descriptor.Operation, DisplayName = descriptor.DisplayName, FunctionName = function, ReturnsValue = descriptor.ReturnsValue, Selected = true, Fields = descriptor.Fields.Select(f => new InstrumentActionFieldDefinition { Name = f.Name, Label = f.Label, Type = f.Type, DefaultValue = Convert.ToString(f.DefaultValue, CultureInfo.InvariantCulture), Unit = f.Unit, Options = f.Options == null ? string.Empty : string.Join("|", f.Options) }).ToList() });
            }
            return methods;
        }

        private static List<Tuple<string, string, string>> DefaultIndependentTemplates()
        {
            return new List<Tuple<string, string, string>>
            {
                Tuple.Create("DUTCAN", "DUTCAN", "Instruments.CAN.CANWrapper"), Tuple.Create("HVDC", "HVDC", "Instruments.PowerSupply.Kewell_C3000"),
                Tuple.Create("DMM_HV", "高压万用表", "Instruments.DMM.KeySight34461A"), Tuple.Create("DMM_LV", "低压万用表", "Instruments.DMM.KeySight34461A"), Tuple.Create("DAQ", "DAQ", "NI-9227"),
                Tuple.Create("RELAY_FCT", "RELAY_FCT", "SHT_48SEDO_A"), Tuple.Create("RELAY_HVMUX", "RELAY_HVMUX", "SHT_48SEDO_A"),
                Tuple.Create("DCDC_LOAD", "DCDC_LOAD", "Instruments.Load.AN23600E"), Tuple.Create("RES_1", "程控电阻卡1", "Instruments.Other.NGI_ProgramResistance"), Tuple.Create("RES_2", "程控电阻卡2", "Instruments.Other.NGI_ProgramResistance"), Tuple.Create("RES_3", "程控电阻卡3", "Instruments.Other.NGI_ProgramResistance")
            };
        }

        private static void Normalize(InstrumentWorkspaceDocument document)
        {
            document.Instruments = document.Instruments ?? new List<ProjectInstrumentDefinition>(); document.Stations = document.Stations ?? new List<StationInstrumentDefinition>();
            document.Version = SchemaVersion;
            foreach (ProjectInstrumentDefinition instrument in document.Instruments)
            {
                if (string.IsNullOrWhiteSpace(instrument.Id)) instrument.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(instrument.Category)) instrument.Category = ClassifyDevice(instrument.Device, instrument.DriverName);
                if (instrument.LockTimeoutMs <= 0) instrument.LockTimeoutMs = 30000;
                instrument.GeneratedMethods = instrument.GeneratedMethods ?? CreateMethods(instrument.Device, instrument.DisplayName);
                foreach (GeneratedInstrumentMethod method in instrument.GeneratedMethods) method.Fields = method.Fields ?? new List<InstrumentActionFieldDefinition>();
            }
            document.StationCount = Math.Max(1, Math.Min(12, document.StationCount));
            HashSet<string> instrumentIds = new HashSet<string>(document.Instruments.Select(v => v.Id), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ProjectInstrumentDefinition> byId = document.Instruments.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= document.StationCount; i++)
            {
                StationInstrumentDefinition station = document.Stations.FirstOrDefault(s => s.StationNumber == i);
                if (station == null) { station = new StationInstrumentDefinition { StationNumber = i }; document.Stations.Add(station); }
                station.IndependentDevices = station.IndependentDevices ?? new List<StationInstrumentInstance>();
                station.SharedBindings = (station.SharedBindings ?? new List<StationSharedBinding>()).Where(v => instrumentIds.Contains(v.InstrumentId ?? string.Empty)).GroupBy(v => v.InstrumentId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                foreach (StationSharedBinding binding in station.SharedBindings) { ProjectInstrumentDefinition owner; if (byId.TryGetValue(binding.InstrumentId, out owner)) binding.Device = owner.Device; }
                // Keep the legacy PLC fields aligned so older readers and the visual canvas stay correct.
                StationSharedBinding plc = station.SharedBindings.FirstOrDefault(v => string.Equals(v.Device, "PLC", StringComparison.OrdinalIgnoreCase));
                station.PlcInstrumentId = plc == null ? string.Empty : plc.InstrumentId; station.PlcDbOffset = plc == null ? 0 : plc.DbOffset;
            }
            document.Stations.RemoveAll(s => s.StationNumber < 1 || s.StationNumber > document.StationCount);
        }

        private static bool IsInstrumentAssembly(string name)
        {
            string value = (name ?? string.Empty).ToUpperInvariant();
            return value.StartsWith("INSTRUMENTS.") || value.Contains("SHT_48SEDO") || value.Contains("AN23600") || value.Contains("S7") || value.Contains("NI_");
        }
        private static string DefaultForType(Type type) { if (type == typeof(bool)) return "false"; if (type.IsEnum) return Enum.GetNames(type).FirstOrDefault() ?? string.Empty; if (type == typeof(string)) return string.Empty; if (type.IsValueType) return "0"; return string.Empty; }
        private static string LocateWorkspaceRoot(string baseDirectory)
        {
            DirectoryInfo current = new DirectoryInfo(baseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TestDLL.sln")) && Directory.Exists(Path.Combine(current.FullName, "DLLs"))) return current.FullName;
                current = current.Parent;
            }
            return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        }
        private static List<InstrumentActionFieldDefinition> CloneFields(IEnumerable<InstrumentActionFieldDefinition> fields) { return (fields ?? Enumerable.Empty<InstrumentActionFieldDefinition>()).Select(f => new InstrumentActionFieldDefinition { Name = f.Name, Label = f.Label, Type = f.Type, DefaultValue = f.DefaultValue, Unit = f.Unit, Options = f.Options }).ToList(); }
        private static string Escape(string value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }
}
