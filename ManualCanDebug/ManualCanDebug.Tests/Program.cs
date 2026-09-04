using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ManualCanDebug.Core;

namespace ManualCanDebug.Tests
{
    internal static class Program
    {
        private static int _assertions;

        private static void Main(string[] args)
        {
            if (args != null && args.Length == 3 && args[0] == "--inspect-locator")
            {
                ProductLocatorDefinition locator = ProductLocatorParser.Parse(args[1], args[2]);
                Console.WriteLine("PRODUCT {0} TABLES {1} SIGNALS {2}", locator.Product, locator.Tables.Count, locator.SignalCount);
                foreach (ProductLocatorTable table in locator.Tables) Console.WriteLine("0x{0:X2} {1} {2} signals write={3}", table.AddressOffset, table.Name, table.Signals.Count, table.CanWrite);
                return;
            }
            if (args != null && args.Length == 2 && args[0] == "--inspect-assembly")
            {
                Assembly assembly = Assembly.LoadFrom(args[1]);
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies()) Console.WriteLine("REFERENCE " + reference.FullName);
                foreach (Type type in assembly.GetExportedTypes())
                {
                    Console.WriteLine("TYPE " + type.FullName);
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) Console.WriteLine("  " + method);
                    foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) Console.WriteLine("  PROPERTY " + property.PropertyType + " " + property.Name);
                }
                return;
            }
            Run("Parse mixed hex separators", ParseMixedHexSeparators);
            Run("Reject malformed hex", RejectMalformedHex);
            Run("Reject more than eight bytes", RejectMoreThanEightBytes);
            Run("Parses multi-frame table buffers", ParsesMultiFrameTableBuffers);
            Run("Catalog preserves sequence order", CatalogPreservesSequenceOrder);
            Run("Catalog contains resolver presets", CatalogContainsResolverPresets);
            Run("Parses and exports tolerant SEQ", ParsesAndExportsTolerantSequence);
            Run("Clones STEP definitions independently", ClonesStepDefinitionsIndependently);
            Run("Builds distinct instrument STEP templates", BuildsDistinctInstrumentStepTemplates);
            Run("Builds Locator product read and write STEP", BuildsLocatorProductReadAndWriteStep);
            Run("Evaluates direct write-value formulas", EvaluatesDirectWriteValueFormulas);
            Run("Compiles function blocks to unchanged SEQ schema", CompilesFunctionBlocksToUnchangedSeqSchema);
            Run("Calculated motor results follow current product profile", CalculatedMotorResultsFollowCurrentProductProfile);
            Run("Standard module references expand to platform STEP", StandardModuleReferencesExpandToPlatformSteps);
            Run("Exports only enabled flow instances", ExportsOnlyEnabledFlowInstances);
            Run("Generic STEP catalog covers core instruments", GenericStepCatalogCoversCoreInstruments);
            Run("Studio validator rejects broken loop targets", StudioValidatorRejectsBrokenLoopTargets);
            Run("Allows repeated non-jump STEP names", AllowsRepeatedNonJumpStepNames);
            Run("Compiles structured IF ELSE ENDIF", CompilesStructuredIfElseEndIf);
            Run("Compiles block parameter expressions", CompilesBlockParameterExpressions);
            Run("Builds DUT communication init frame", BuildsDutCommunicationInitFrame);
            Run("Builds product communication test frame", BuildsProductCommunicationTestFrame);
            Run("Builds wakeup frame", BuildsWakeupFrame);
            Run("Uses actual CAN card connection settings", UsesActualCanCardConnectionSettings);
            Run("C91 profile uses original SEQ addresses", C91ProfileUsesOriginalSeqAddresses);
            Run("C95 profile uses locator addresses", C95ProfileUsesLocatorAddresses);
            Run("C91 profile contains FT entry sequence", C91ProfileContainsFtEntrySequence);
            Run("Pre-current read continues after one item fails", PreCurrentReadContinuesAfterOneItemFails);
            Run("Pre-current result formats parsed values", PreCurrentResultFormatsParsedValues);
            Run("Every current step opens pre-current guide", EveryCurrentStepOpensPreCurrentGuide);
            Run("Current steps include matching product-current reads", CurrentStepsIncludeMatchingProductCurrentReads);
            Run("Builds C91 effective-to-peak current command", BuildsC91EffectiveToPeakCurrentCommand);
            Run("Builds C95 current command with locator flag", BuildsC95CurrentCommandWithLocatorFlag);
            Run("Parses product phase current RMS", ParsesProductPhaseCurrentRms);
            Run("Pads short classic CAN protocol frames", PadsShortClassicCanProtocolFrames);
            Run("Decodes active-low fault inputs", DecodesActiveLowFaultInputs);
            Run("Decodes C95 motor status and fault bytes", DecodesC95MotorStatusAndFaultBytes);
            Run("Diagnoses C95 output blocking conditions", DiagnosesC95OutputBlockingConditions);
            Run("C95 Input Tables catalog covers the complete sheet", C95InputTablesCatalogCoversCompleteSheet);
            Run("C95 Input Tables values decode by table type", C95InputTablesValuesDecodeByTableType);
            Run("C91 Input Tables catalog covers locator inputs", C91InputTablesCatalogCoversLocatorInputs);
            Run("C95 all-table catalog covers every address entry", C95AllTableCatalogCoversEveryAddressEntry);
            Run("Parses C95 product resolver data and frames", ParsesC95ProductResolverDataAndFrames);
            Run("Parses C91 product resolver data with fault byte", ParsesC91ProductResolverDataWithFaultByte);
            Run("Expands C95 all-table reads into parsed fields", ExpandsC95AllTableReadsIntoParsedFields);
            Run("C96 profile exposes both independent drives", C96ProfileExposesBothIndependentDrives);
            Run("C92 reuses C96 dual-drive locator", C92ReusesC96DualDriveLocator);
            Run("C96 FT_Enables UV reset offsets match locator", C96FtEnablesUvResetOffsetsMatchLocator);
            Run("Builds C96 dual-drive motor control payload", BuildsC96DualDriveMotorControlPayload);
            Run("C96 input catalog covers locator current-value tables", C96InputCatalogCoversLocatorCurrentValueTables);
            Run("Parses C96 current resolver and motor status", ParsesC96CurrentResolverAndMotorStatus);
            Run("Validates resolver pole-pair setting", ValidatesResolverPolePairSetting);
            Run("Parses and decodes auxiliary DBC frames", ParsesAndDecodesAuxiliaryDbcFrames);
            Run("Packs auxiliary DBC control signals", PacksAuxiliaryDbcControlSignals);
            Run("Packs PDU relay command frame", PacksPduRelayCommandFrame);
            Run("C95 and C96 share auxiliary functions", C95AndC96ShareAuxiliaryFunctions);

            if (args != null && args.Length > 0)
            {
                SequenceDocument actual = SequenceDocument.Parse(System.IO.File.ReadAllText(args[0]));
                Assert(actual.Steps.Count == 551, "actual FT1 SEQ must load all 551 STEP instances");
                SequenceDocument actualRoundTrip = SequenceDocument.Parse(actual.ToJson(actual.Steps));
                Assert(actualRoundTrip.Steps.Count == 551, "actual FT1 SEQ export must preserve all 551 STEP instances");
                string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]), ".."));
                string extendedDllPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LegacyRuntime", "CSP.TestDLL.dll");
                string testDllPath = System.IO.File.Exists(extendedDllPath) ? extendedDllPath : System.IO.Path.Combine(root, "DLLs", "CSP.TestDLL.dll");
                Assembly testDll = Assembly.LoadFrom(testDllPath);
                Type mainType = testDll.GetType("CSP.TestDllMain", true);
                HashSet<string> publicMethods = new HashSet<string>(mainType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(method => method.Name), StringComparer.Ordinal);
                string[] missing = actual.Steps.Select(step => step.FunctionName).Distinct(StringComparer.Ordinal).Where(name => !publicMethods.Contains(name)).OrderBy(name => name).ToArray();
                Assert(missing.Length == 0, "CSP.TestDLL is missing SEQ functions: " + string.Join(", ", missing));
                Assert(actual.Steps.All(step => publicMethods.Contains(step.FunctionName)), "not every STEP instance maps to an original TestDllMain method");
                if (System.IO.File.Exists(extendedDllPath))
                {
                    Assert(new[] { "FCT_ExecuteAction", "FCT_ExecuteLogic", "FCT_CANSignal", "FCT_CANTable" }.All(publicMethods.Contains), "extended TestDLL generic entry points are incomplete");
                    Console.WriteLine("PASS: extended CSP.TestDLL keeps all old methods and four generic entry points");
                }
                string locatorDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "ProductLocators");
                if (System.IO.Directory.Exists(locatorDirectory))
                {
                    ProductLocatorDefinition c91 = ProductLocatorParser.Parse("C91", System.IO.Path.Combine(locatorDirectory, "C91_Locator.xlsx"));
                    ProductLocatorDefinition c95 = ProductLocatorParser.Parse("C95", System.IO.Path.Combine(locatorDirectory, "C95_Locator.xlsx"));
                    ProductLocatorDefinition c96 = ProductLocatorParser.Parse("C96", System.IO.Path.Combine(locatorDirectory, "C96_Locator.xlsx"));
                    Assert(c91.Tables.Count >= 25 && c91.SignalCount >= 400, "C91 Locator parsing is incomplete");
                    Assert(c95.Tables.Count >= 20 && c95.SignalCount >= 500, "C95 Locator parsing is incomplete");
                    Assert(c96.Tables.Count >= 50 && c96.SignalCount >= 700, "C96 Locator parsing is incomplete");
                    Assert(c95.Tables.SelectMany(table => table.Signals).Any(signal => signal.Name.IndexOf("HVDC", StringComparison.OrdinalIgnoreCase) >= 0), "C95 Locator HVDC signals are missing");
                    Assert(c96.Tables.Any(table => table.Name.IndexOf("TM2", StringComparison.OrdinalIgnoreCase) >= 0), "C96 Locator TM2 tables are missing");
                    Console.WriteLine("PASS: Locator parser loaded C91/C95/C96 ({0}/{1}/{2} signals)", c91.SignalCount, c95.SignalCount, c96.SignalCount);
                }
                Console.WriteLine("PASS: actual SEQ loaded {0} steps from {1}", actual.Steps.Count, args[0]);
                Console.WriteLine("PASS: all {0} STEP instances map to CSP.TestDllMain methods", actual.Steps.Count);
            }

            Console.WriteLine("PASS: {0} assertions", _assertions);
        }

        private static void ParsesAndDecodesAuxiliaryDbcFrames()
        {
            DbcDatabase database = DbcDatabase.Parse(
                "BO_ 2349377434 ACU1_DCDC_Feedback1: 8 DCDC\n" +
                " SG_ DCDC_OutVoltage : 0|16@1+ (0.1,-1000) [-1000|5553.5] \"V\" Vector__XXX\n" +
                " SG_ DCDC_FaultCode : 56|8@1+ (1,0) [0|255] \"\" Vector__XXX\n");
            DbcDecodedFrame decoded = database.Decode(new CanFrame(0x0C08A79A, new byte[] { 0x8C, 0x27, 0, 0, 0, 0, 0, 0x81 }));
            Assert(decoded != null && decoded.MessageName == "ACU1_DCDC_Feedback1", "DCDC feedback message was not found by extended CAN ID");
            Assert(Math.Abs(decoded.Signals.Single(signal => signal.Name == "DCDC_OutVoltage").Value - 12.4) < 0.001, "DCDC output voltage scaling is incorrect");
            Assert(decoded.Signals.Single(signal => signal.Name == "DCDC_FaultCode").RawValue == 0x81, "DCDC fault byte is incorrect");
        }

        private static void ParsesAndExportsTolerantSequence()
        {
            string source = "{ // comment\n\"ProjectName\":\"Demo\",\"StepList\":[{\"StepName\":\"Old\",\"FunctionName\":\"Resolver_SetSpeed\",\"Speed\":700,\"StepName\":\"Set 700\",},],}";
            SequenceDocument document = SequenceDocument.Parse(source);
            Assert(document.Steps.Count == 1, "tolerant SEQ parser did not load StepList");
            Assert(document.Steps[0].StepName == "Set 700" && document.Steps[0].GetDouble("Speed") == 700, "duplicate keys or parameters were not preserved as platform-compatible last values");
            document.Steps[0].SetParameterFromText("Speed", "3500", typeof(int));
            string exported = document.ToJson(document.Steps);
            SequenceDocument reparsed = SequenceDocument.Parse(exported);
            Assert(reparsed.Steps[0].GetInt("Speed") == 3500, "exported SEQ did not preserve edited parameter type/value");
            Assert(exported.IndexOf("//", StringComparison.Ordinal) < 0, "exported SEQ must be strict JSON without comments");
        }

        private static void ClonesStepDefinitionsIndependently()
        {
            SequenceStepDefinition source = new SequenceStepDefinition(new Dictionary<string, object>
            {
                { "StepName", "Set LVDC" }, { "FunctionName", "LVDC_SetSourceVoltage" }, { "Voltage", 24.0 }
            });
            SequenceStepDefinition clone = SequenceEditing.Clone(source);
            clone.SetParameterFromText("Voltage", "26", typeof(double));
            Assert(source.GetDouble("Voltage") == 24, "editing a cloned STEP changed the source template");
            Assert(clone.GetDouble("Voltage") == 26, "cloned STEP parameter edit was not retained");
        }

        private static void BuildsDistinctInstrumentStepTemplates()
        {
            SequenceStepDefinition[] steps =
            {
                new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "LVDC 24V" }, { "FunctionName", "LVDC_SetSourceVoltage" }, { "Voltage", 24 } }),
                new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "LVDC 26V" }, { "FunctionName", "LVDC_SetSourceVoltage" }, { "Voltage", 26 } }),
                new SequenceStepDefinition(new Dictionary<string, object> { { "StepName", "Resolver 700" }, { "FunctionName", "Resolver_SetSpeed" }, { "Speed", 700 } })
            };
            IReadOnlyList<SequenceStepDefinition> templates = SequenceEditing.BuildFunctionTemplates(steps);
            Assert(templates.Count == 2, "test-item library must contain one template per developed FunctionName");
            Assert(InstrumentStepCatalog.CategoryFor("LVDC_SetSourceVoltage") == "LVDC低压电源", "LVDC action category is incorrect");
            Assert(InstrumentStepCatalog.CategoryFor("Resolver_SetSpeed") == "旋变模拟器", "resolver action category is incorrect");
        }

        private static void BuildsLocatorProductReadAndWriteStep()
        {
            ProductLocatorSignal signal = new ProductLocatorSignal(44, "HVDC_SENSE_AI", "float32", 4, "V", "bus voltage");
            ProductLocatorTable table = new ProductLocatorTable("FT_Motor_Control_Data", 0x58, 4, "Motor", new[] { signal });
            SequenceStepDefinition read = ProductSignalStepFactory.CreateRead("Read bus", table, signal, 400, 900, "GELE");
            SequenceStepDefinition write = ProductSignalStepFactory.CreateWrite("Write bus", table, signal, 12.5);
            Assert(read.FunctionName == "FCT_CANSignal" && Convert.ToString(read.Get("Operation")) == "Read" && read.GetInt("AddrOffset") == 0x58 && read.GetInt("TableIndex") == 44 && read.GetInt("DataSize") == 4, "Locator read STEP protocol parameters are incorrect");
            Assert(read.GetDouble("LowLimit") == 400 && read.GetDouble("HighLimit") == 900 && Convert.ToString(read.Get("Unit")) == "V", "Locator read STEP limits/unit are incorrect");
            Assert(write.FunctionName == "FCT_CANSignal" && Convert.ToString(write.Get("Operation")) == "Write" && Convert.ToString(write.Get("ValueText")) == "12.5", "Locator write STEP value is incorrect");
            SequenceStepDefinition tableRead = ProductSignalStepFactory.CreateTableRead("Read table", table);
            SequenceStepDefinition tableWrite = ProductSignalStepFactory.CreateTableWrite("Write table", table, new[] { new KeyValuePair<ProductLocatorSignal, string>(signal, "22.5") });
            Assert(tableRead.FunctionName == "FCT_CANTable" && tableRead.GetInt("TableLength") == 48, "Locator whole-table read length is incorrect");
            Assert(tableWrite.FunctionName == "FCT_CANTable" && Convert.ToString(tableWrite.Get("ChangesJson")).IndexOf("HVDC_SENSE_AI", StringComparison.Ordinal) >= 0 && Convert.ToString(tableWrite.Get("ChangesJson")).IndexOf("22.5", StringComparison.Ordinal) >= 0, "Locator whole-table write changes are incorrect");
            ProductLocatorSignal trigger = new ProductLocatorSignal(48, "New Data Flag", "uint8", 1, "", "trigger");
            ProductLocatorTable triggerTable = new ProductLocatorTable("FT_Motor_Control_Data", 0x58, 1, "Motor", new[] { signal, trigger });
            SequenceStepDefinition triggerWrite = ProductSignalStepFactory.CreateTableWrite("Write with trigger", triggerTable, new[] { new KeyValuePair<ProductLocatorSignal, string>(trigger, "255") });
            Assert(Convert.ToString(triggerWrite.Get("ChangesJson")).IndexOf("\"WriteLast\": true", StringComparison.OrdinalIgnoreCase) >= 0, "trigger signal was not marked for last-write ordering");
            Assert(Convert.ToString(triggerWrite.Get("ChangesJson")).IndexOf("\"WriteFinal\": true", StringComparison.OrdinalIgnoreCase) >= 0, "New Data trigger was not marked as the final write");
        }

        private static void CompilesFunctionBlocksToUnchangedSeqSchema()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "HV Start", Category = "Power" };
            block.Parameters.Add(new BlockParameterDefinition { Name = "TargetVoltage", DisplayName = "目标电压", Type = "Number", DefaultValue = 600.0, Required = true });
            BlockStepDefinition step = new BlockStepDefinition
            {
                StepProperties = new Dictionary<string, object> { { "StepName", "Set voltage" }, { "RunMode", "Normal" }, { "FunctionName", "HVDC_SetSourceVoltage" }, { "RecordingLog", true }, { "Voltage", 0.0 } },
                ParameterBindings = new Dictionary<string, string> { { "Voltage", "TargetVoltage" } }
            };
            block.Steps.Add(step);
            FctStudioProject project = new FctStudioProject { ProjectName = "Compiled Demo" };
            project.Blocks.Add(block);
            project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = "HV 600V", ParameterOverrides = new Dictionary<string, object> { { "TargetVoltage", 618.0 } }, Snapshot = block.Clone() });
            FctStudioCompileResult compiled = FctStudioCompiler.Compile(project);
            Assert(compiled.Document.Steps.Count == 1, "function block did not flatten to one platform STEP");
            Assert(compiled.Document.Steps[0].FunctionName == "HVDC_SetSourceVoltage" && compiled.Document.Steps[0].GetDouble("Voltage") == 618, "block parameter binding did not compile");
            string json = compiled.Document.ToJson(compiled.Document.Steps);
            Assert(json.IndexOf("BlockId", StringComparison.Ordinal) < 0 && json.IndexOf("Breakpoint", StringComparison.Ordinal) < 0 && json.IndexOf("Snapshot", StringComparison.Ordinal) < 0, "editor-only metadata leaked into platform SEQ");
            Assert(SequenceDocument.Parse(json).Steps.Count == 1, "compiled platform SEQ did not round-trip");
        }

        private static void GenericStepCatalogCoversCoreInstruments()
        {
            IReadOnlyList<SequenceStepDefinition> templates = GenericStepCatalog.CreateTemplates();
            HashSet<string> devices = new HashSet<string>(templates.Select(step => Convert.ToString(step.Get("Device"))), StringComparer.OrdinalIgnoreCase);
            Assert(new[] { "LVDC", "HVDC", "DMM", "DAQ", "PRODUCTCAN", "AUXCAN", "CUSTOM" }.All(devices.Contains), "generic STEP catalog is missing a core instrument category");
            HashSet<string> auxiliaryOperations = new HashSet<string>(templates.Where(step => string.Equals(Convert.ToString(step.Get("Device")), "AUXCAN", StringComparison.OrdinalIgnoreCase)).Select(step => Convert.ToString(step.Get("Operation"))), StringComparer.OrdinalIgnoreCase);
            Assert(new[] { "SendDbcSignals", "StartPeriodicDbc", "StopPeriodicDbc", "ReadDbcSignal" }.All(auxiliaryOperations.Contains), "generic STEP catalog is missing DBC sequence operations");
            Assert(SequenceEditing.BuildFunctionTemplates(templates).Count == templates.Count, "generic actions with one FunctionName were incorrectly collapsed in the test-item library");
        }

        private static void StudioValidatorRejectsBrokenLoopTargets()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "Broken Loop" };
            block.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "Loop" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "FixedLoop" }, { "Count", 2 }, { "TargetStepName", "Missing" } } });
            FctStudioProject project = new FctStudioProject(); project.Blocks.Add(block); project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, Snapshot = block.Clone(), DisplayName = block.Name });
            FctStudioValidationResult validation = FctStudioValidator.Validate(project);
            Assert(!validation.IsValid && validation.Errors.Any(error => error.IndexOf("跳转目标不存在", StringComparison.Ordinal) >= 0), "studio validator accepted a broken loop target");
        }

        private static void AllowsRepeatedNonJumpStepNames()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "继电器/PDU" };
            block.Steps.Add(RelayIoStep("设置IO（Y00-Y57）", "OUT5,OUT6", "1,1"));
            block.Steps.Add(RelayIoStep("设置IO（Y00-Y57）", "OUT1", "1"));
            block.Steps.Add(RelayIoStep("设置IO（Y00-Y57）", "OUT5", "0"));
            FctStudioProject project = new FctStudioProject { Product = "C96" };
            project.Blocks.Add(block);
            project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = block.Name, Snapshot = block.Clone() });

            FctStudioValidationResult validation = FctStudioValidator.Validate(project);
            Assert(validation.IsValid, "repeated ordinary IO actions were rejected");
            Assert(validation.Warnings.Any(warning => warning.IndexOf("自动增加序号", StringComparison.Ordinal) >= 0), "repeated action-name warning was not emitted");

            FctStudioCompileResult compiled = FctStudioCompiler.Compile(project);
            Assert(compiled.Document.Steps.Select(step => step.StepName).Distinct(StringComparer.Ordinal).Count() == 3, "repeated IO actions did not receive unique exported names");
            Assert(compiled.Document.Steps[0].StepName.EndsWith("(01)", StringComparison.Ordinal) && compiled.Document.Steps[1].StepName.EndsWith("(02)", StringComparison.Ordinal) && compiled.Document.Steps[2].StepName.EndsWith("(03)", StringComparison.Ordinal), "repeated IO action numbering is incorrect");
            Assert(Convert.ToString(compiled.Document.Steps[0].Get("Channels"), CultureInfo.InvariantCulture) == "OUT5,OUT6" && Convert.ToString(compiled.Document.Steps[0].Get("Values"), CultureInfo.InvariantCulture) == "1,1", "first IO action changed unrelated channel state");
            Assert(Convert.ToString(compiled.Document.Steps[1].Get("Channels"), CultureInfo.InvariantCulture) == "OUT1" && Convert.ToString(compiled.Document.Steps[1].Get("Values"), CultureInfo.InvariantCulture) == "1", "second IO action changed unrelated channel state");
            Assert(Convert.ToString(compiled.Document.Steps[2].Get("Channels"), CultureInfo.InvariantCulture) == "OUT5" && Convert.ToString(compiled.Document.Steps[2].Get("Values"), CultureInfo.InvariantCulture) == "0", "third IO action changed unrelated channel state");

            block.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "跳转" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "Goto" }, { "TargetStepName", "设置IO（Y00-Y57）" } } });
            project.Flow[0].Snapshot = block.Clone();
            FctStudioValidationResult ambiguous = FctStudioValidator.Validate(project);
            Assert(!ambiguous.IsValid && ambiguous.Errors.Any(error => error.IndexOf("跳转目标StepName重复", StringComparison.Ordinal) >= 0), "ambiguous jump target with repeated STEP names was accepted");
        }

        private static BlockStepDefinition RelayIoStep(string name, string channels, string values)
        {
            return new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", name }, { "FunctionName", "FCT_ExecuteAction" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Device", "RELAY_FCT" }, { "Operation", "SetDO" }, { "ResultMode", "Action" }, { "Channels", channels }, { "Values", values }, { "Slave", 1 } } };
        }

        private static void CompilesStructuredIfElseEndIf()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "Logic" };
            block.Steps.Add(LogicStep("IF 母线电压 > 100", "Condition", "IF", new Dictionary<string, object> { { "VariableName", "母线电压" }, { "DataType", "Number" }, { "Compare", "GT" }, { "RightValue", "100" }, { "FalseGoto", "ELSE_BODY" } }));
            block.Steps.Add(LogicStep("True action", "Delay", string.Empty, new Dictionary<string, object> { { "TimeMs", 1 } }));
            block.Steps.Add(LogicStep("ELSE", "Goto", "ELSE", new Dictionary<string, object> { { "TargetStepName", "ENDIF" } }));
            block.Steps.Add(LogicStep("ELSE_BODY", "Label", "ELSE_BODY", null));
            block.Steps.Add(LogicStep("False action", "Delay", string.Empty, new Dictionary<string, object> { { "TimeMs", 1 } }));
            block.Steps.Add(LogicStep("ENDIF", "Label", "ENDIF", null));
            FctStudioProject project = new FctStudioProject(); project.Blocks.Add(block); project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = block.Name, Snapshot = block.Clone() });
            FctStudioCompileResult compiled = FctStudioCompiler.Compile(project);
            Assert(compiled.Document.Steps.Count == 6, "structured IF did not preserve executable markers");
            Assert(compiled.Document.Steps.All(step => !step.Properties.ContainsKey("StructureRole") && !step.Properties.ContainsKey("StructureId")), "structured editor metadata leaked into SEQ");
            Assert(compiled.Document.Steps.Any(step => Convert.ToString(step.Get("Operation"), CultureInfo.InvariantCulture) == "Label"), "ENDIF label was not compiled");
        }

        private static BlockStepDefinition LogicStep(string name, string operation, string role, IDictionary<string, object> extras)
        {
            Dictionary<string, object> values = new Dictionary<string, object> { { "StepName", name }, { "RunMode", "Normal" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RecordingLog", true }, { "Operation", operation } }; if (!string.IsNullOrWhiteSpace(role)) { values["StructureRole"] = role; values["StructureId"] = "probe"; } foreach (KeyValuePair<string, object> pair in extras ?? new Dictionary<string, object>()) values[pair.Key] = pair.Value; return new BlockStepDefinition { StepProperties = values };
        }

        private static void EvaluatesDirectWriteValueFormulas()
        {
            Assert(Math.Abs(NumericFormula.Evaluate("100*1.414") - 141.4) < 0.000001, "current coefficient formula was not evaluated");
            Assert(Math.Abs(NumericFormula.Evaluate("(100+20)*1.414") - 169.68) < 0.000001, "parenthesized formula was not evaluated");
            ProductLocatorSignal signal = new ProductLocatorSignal(4, "Iqs_End", "float32", 4, string.Empty, string.Empty);
            ProductLocatorTable table = new ProductLocatorTable("FT_Motor_Control_Data_TM1", 0x68, 39, "Motor", new List<ProductLocatorSignal> { signal });
            SequenceStepDefinition step = ProductSignalStepFactory.CreateTableWrite("Write", table, new[] { new KeyValuePair<ProductLocatorSignal, string>(signal, "100*1.414") });
            Assert(Convert.ToString(step.Get("ChangesJson"), CultureInfo.InvariantCulture).IndexOf("141.4", StringComparison.Ordinal) >= 0, "table STEP did not store the calculated actual value");
        }

        private static void ExportsOnlyEnabledFlowInstances()
        {
            FunctionBlockDefinition unusedDraft = new FunctionBlockDefinition { Name = "Unused draft" };
            unusedDraft.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "Duplicate" }, { "FunctionName", "" } } });
            unusedDraft.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "Duplicate" }, { "FunctionName", "" } } });
            FunctionBlockDefinition active = new FunctionBlockDefinition { Name = "Active" };
            active.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "One" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "Delay" }, { "TimeMs", 1 }, { "Product", "C96" } } });
            active.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "Two" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "Delay" }, { "TimeMs", 1 } } });
            FctStudioProject project = new FctStudioProject(); project.Blocks.Add(unusedDraft); project.Blocks.Add(active); project.Flow.Add(new FlowBlockInstance { BlockId = active.Id, DisplayName = "Only this", Snapshot = active.Clone() });
            FctStudioCompileResult compiled = FctStudioCompiler.Compile(project);
            Assert(compiled.Document.Steps.Count == 2, "unused library modules leaked into exported SEQ");
            Assert(compiled.Document.Steps.All(step => step.StepName.Contains("Only this") || step.StepName.Contains("Only_this")), "exported STEP did not come from the visible flow instance");
            Assert(compiled.Document.Steps.All(step => !step.Properties.ContainsKey("Product")), "editor-only Product metadata leaked into platform SEQ");
        }

        private static void CalculatedMotorResultsFollowCurrentProductProfile()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "TM2 current" };
            block.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "Read current" }, { "FunctionName", "FCT_CANCalculatedResults" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "CalculationType", "ThreePhaseCurrentRms" }, { "AddrOffset", 0x94 }, { "TableLength", 40 }, { "DriveTarget", "TM2" } } });
            FctStudioProject project = new FctStudioProject { Product = "C92" }; project.Blocks.Add(block); project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = block.Name, Snapshot = block.Clone() });
            SequenceStepDefinition compiled = FctStudioCompiler.Compile(project).Document.Steps.Single();
            Assert(Convert.ToBoolean(compiled.Get("AutoProductProfile"), CultureInfo.InvariantCulture), "calculated result did not enable automatic product profile");
            Assert(Convert.ToString(compiled.Get("Product"), CultureInfo.InvariantCulture) == "C92", "compiled result did not use the current project product");
            Assert(Convert.ToString(compiled.Get("DriveTarget"), CultureInfo.InvariantCulture) == "TM2", "compiled result did not retain the drive target");
        }

        private static void StandardModuleReferencesExpandToPlatformSteps()
        {
            FunctionBlockDefinition child = new FunctionBlockDefinition { Name = "低压上电", ModuleKind = "Standard", IsStandard = true };
            child.Parameters.Add(new BlockParameterDefinition { Name = "DelayMs", DisplayName = "等待时间", Type = "int", DefaultValue = 1, Unit = "ms" }); BlockStepDefinition childStep = new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "设置24V" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "Delay" }, { "TimeMs", 1 } } }; childStep.ParameterBindings["TimeMs"] = "DelayMs"; child.Steps.Add(childStep);
            FunctionBlockDefinition parent = new FunctionBlockDefinition { Name = "上电章节", ModuleKind = "Standard", IsStandard = true };
            parent.Steps.Add(new BlockStepDefinition { ReferencedBlockId = child.Id, ReferencedBlockName = child.Name, ReferencedParameterOverrides = new Dictionary<string, object> { { "DelayMs", 5 } } });
            parent.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "完成等待" }, { "FunctionName", "FCT_ExecuteLogic" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "Delay" }, { "TimeMs", 1 } } });
            FctStudioProject project = new FctStudioProject { Product = "C96" }; project.Blocks.Add(child); project.Blocks.Add(parent); project.Flow.Add(new FlowBlockInstance { BlockId = parent.Id, DisplayName = parent.Name, Snapshot = parent.Clone() });
            FctStudioCompileResult compiled = FctStudioCompiler.Compile(project); Assert(compiled.Document.Steps.Count == 2, "referenced standard module did not expand"); Assert(compiled.Document.Steps[0].StepName.EndsWith("设置24V", StringComparison.Ordinal) && compiled.Document.Steps[1].StepName.EndsWith("完成等待", StringComparison.Ordinal), "referenced module STEP order or compact platform naming is incorrect"); Assert(compiled.Document.Steps[0].GetInt("TimeMs") == 5, "referenced module parameter override was not applied"); Assert(compiled.Document.Steps.All(step => !string.IsNullOrWhiteSpace(step.FunctionName)), "module reference leaked into platform JSON");
            child.Steps.Add(new BlockStepDefinition { ReferencedBlockId = parent.Id, ReferencedBlockName = parent.Name }); FctStudioValidationResult invalid = FctStudioValidator.Validate(project); Assert(!invalid.IsValid && invalid.Errors.Any(error => error.IndexOf("循环引用", StringComparison.Ordinal) >= 0), "module reference cycle was not rejected");
        }

        private static void CompilesBlockParameterExpressions()
        {
            FunctionBlockDefinition block = new FunctionBlockDefinition { Name = "Current" };
            block.Parameters.Add(new BlockParameterDefinition { Name = "Rms", DefaultValue = 100.0, Required = true });
            block.Steps.Add(new BlockStepDefinition { StepProperties = new Dictionary<string, object> { { "StepName", "Write" }, { "FunctionName", "FCT_CANTable" }, { "RunMode", "Normal" }, { "RecordingLog", true }, { "Operation", "Write" }, { "AddrOffset", 0x58 }, { "TableLength", 32 }, { "ChangesJson", "{\"Value\":\"${Rms*1.414}\"}" } } });
            FctStudioProject project = new FctStudioProject(); project.Blocks.Add(block); project.Flow.Add(new FlowBlockInstance { BlockId = block.Id, DisplayName = block.Name, Snapshot = block.Clone(), ParameterOverrides = new Dictionary<string, object> { { "Rms", 100.0 } } });
            SequenceStepDefinition compiled = FctStudioCompiler.Compile(project).Document.Steps.Single();
            Assert(Convert.ToString(compiled.Get("ChangesJson"), CultureInfo.InvariantCulture).IndexOf("141.4", StringComparison.Ordinal) >= 0, "block arithmetic interpolation did not convert RMS to peak");
        }

        private static void PacksAuxiliaryDbcControlSignals()
        {
            DbcDatabase database = DbcDatabase.Parse(
                "BO_ 2349308583 VCU1_DCDC_OilPump_Cmd: 8 VCU\n" +
                " SG_ DCAC_Steer_FreqCmd : 32|16@1+ (0.1,-1000) [-1000|5553.5] \"Hz\" Vector__XXX\n" +
                " SG_ DCAC_Steer_Start_Cmd : 16|8@1+ (1,0) [0|255] \"\" Vector__XXX\n" +
                " SG_ DCAC_Steer_Reset : 8|1@1+ (1,0) [0|1] \"\" Vector__XXX\n" +
                " SG_ DCDC_Start_Cmd : 0|8@1+ (1,0) [0|255] \"\" Vector__XXX\n");
            CanFrame frame = database.Encode("VCU1_DCDC_OilPump_Cmd", new Dictionary<string, double>
            {
                { "DCDC_Start_Cmd", 0x55 },
                { "DCAC_Steer_Reset", 1 },
                { "DCAC_Steer_Start_Cmd", 0x55 },
                { "DCAC_Steer_FreqCmd", 60 }
            });
            Assert(frame.Id == 0x0C079AA7, "DBC extended message ID was not normalized");
            Assert(frame.Data[0] == 0x55 && frame.Data[1] == 0x01 && frame.Data[2] == 0x55, "DCDC/oil-pump command bytes are incorrect");
            Assert(frame.Data[4] == 0x68 && frame.Data[5] == 0x29, "oil-pump frequency scaling is incorrect");
        }

        private static void PacksPduRelayCommandFrame()
        {
            DbcDatabase database = DbcDatabase.Parse("BO_ 2364690491 VCU_PDU: 8 VCU\n SG_ VCU_HrtBt : 56|8@1+ (1,0) [0|255] \"\" Vector__XXX\n SG_ VCU_HghVtgCnt : 16|2@1+ (1,0) [0|3] \"\" Vector__XXX\n SG_ VCU_ShrRlyCtl : 0|2@1+ (1,0) [0|3] \"\" Vector__XXX\n");
            CanFrame frame = database.Encode("VCU_PDU", new Dictionary<string, double> { { "VCU_ShrRlyCtl", 1 }, { "VCU_HghVtgCnt", 1 }, { "VCU_HrtBt", 0x0E } });
            Assert(frame.Id == 0x0CF2503B, "PDU command extended ID is incorrect");
            Assert(frame.Data.SequenceEqual(new byte[] { 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x0E }), "PDU shared/main relay command payload is incorrect");
        }

        private static void C95AndC96ShareAuxiliaryFunctions()
        {
            Assert(ProductCanProfile.For(ProductModel.C95).SupportsAuxiliary, "C95 must support DCDC and auxiliary functions");
            Assert(ProductCanProfile.For(ProductModel.C96).SupportsAuxiliary, "C96 must retain DCDC and auxiliary functions");
            Assert(!ProductCanProfile.For(ProductModel.C91).SupportsAuxiliary, "C91 must not expose the C95/C96 auxiliary protocol");
            Assert(!ProductCanProfile.For(ProductModel.C92).SupportsAuxiliary, "C92 specification does not include the C95/C96 auxiliary protocol");
        }

        private static void C95AllTableCatalogCoversEveryAddressEntry()
        {
            IReadOnlyList<C95TableDefinition> tables = C95AllTableCatalog.Tables;
            Assert(tables.Count == 43, "all 43 C95 address-table entries must be visible");
            Assert(tables.Select(table => table.AddressOffset).Distinct().Count() == 43, "C95 table offsets must be unique");
            Assert(tables.First().AddressOffset == 0x00 && tables.Last().AddressOffset == 0xA8, "C95 table range must cover 0x00 through 0xA8");
            Assert(tables.Single(table => table.AddressOffset == 0x80).PointerDepth == 2, "MPI must use the documented pointer-to-pointer read");
            Assert(tables.Single(table => table.AddressOffset == 0x80).ByteLength == 238, "MPI byte length must cover 0x00 through 0xED");
            Assert(tables.Count(table => !table.HasDefinedLength) == 4, "four added control tables have no structure length in the locator");
            Assert(tables.Single(table => table.AddressOffset == 0x00).ByteLength == 228, "analog input table length is incomplete");
            Assert(tables.Single(table => table.AddressOffset == 0x70).ByteLength == 36, "current-sense result table length is incomplete");
            byte[] analog = new byte[228];
            Array.Copy(BitConverter.GetBytes(500.25f), 0, analog, 44, 4);
            C95TableReadResult decoded = C95TableReadResult.Success(tables[0], "0x40000000", analog);
            Assert(decoded.DecodedValues.IndexOf("HVDC_SENSE_AI=500.25", StringComparison.Ordinal) >= 0, "all-table result must expose named signal values");
        }

        private static void ParsesC95ProductResolverDataAndFrames()
        {
            byte[] data = BitConverter.GetBytes(225.5f)
                .Concat(BitConverter.GetBytes(700.25f))
                .Concat(new byte[] { 3 })
                .ToArray();
            ProductResolverData result = ProductResolverData.Parse(
                0x8016A648,
                0x40001234,
                CanProtocol.BuildAddressRead(0x8016A68C),
                BitConverter.GetBytes(0x40001234),
                CanProtocol.BuildTableRead(0x40001234, 9),
                data);
            Assert(Math.Abs(result.PositionDegrees - 225.5) < 0.001, "resolver position parse is incorrect");
            Assert(Math.Abs(result.VelocityFrequency - 700.25) < 0.001, "resolver velocity parse is incorrect");
            Assert(result.FaultCode == 3 && result.FaultDescription.IndexOf("无故障", StringComparison.Ordinal) >= 0, "resolver fault parse is incorrect");
            Assert(result.AddressRequestText == "80 16 A6 8C 00 04 FF 00", "resolver address request frame is incorrect");
            Assert(result.DataRequestText == "40 00 12 34 00 09 FF 00", "resolver data request frame is incorrect");
        }

        private static void ParsesC91ProductResolverDataWithFaultByte()
        {
            byte[] data = BitConverter.GetBytes(135.0f).Concat(BitConverter.GetBytes(0.0f)).Concat(new byte[] { 3 }).ToArray();
            ProductResolverData result = ProductResolverData.Parse(
                ProductCanProfile.For(ProductModel.C91),
                0x8016A648,
                0x40001234,
                CanProtocol.BuildAddressRead(0x8016A690),
                BitConverter.GetBytes(0x40001234),
                CanProtocol.BuildTableRead(0x40001234, 9),
                data);
            Assert(result.Model == ProductModel.C91, "resolver result model is incorrect");
            Assert(result.HasFaultStatus && result.FaultCode == 3, "C91 resolver fault byte is incorrect");
            Assert(result.RawDataText.Split(' ').Length == 9, "C91 resolver raw length is incorrect");
        }

        private static void C91InputTablesCatalogCoversLocatorInputs()
        {
            IReadOnlyList<C91InputTableDefinition> tables = C91InputCatalog.Tables;
            Assert(tables.Count == 5, "C91 input page must contain five tables");
            Assert(tables.Sum(table => table.Signals.Count) == 155, "C91 input signal count is incomplete");
            Assert(tables.Single(table => table.AddressOffset == 0x00).ByteLength == 220, "C91 analog table length is incorrect");
            Assert(tables.Single(table => table.AddressOffset == 0x0C).ByteLength == 110, "C91 count table length is incorrect");
            Assert(tables.Single(table => table.AddressOffset == 0x18).Signals.Any(signal => signal.Offset == 1 && signal.Name == "HVDC_OV_FLT^"), "C91 HVDC fault input is missing");
            Assert(tables.Single(table => table.AddressOffset == 0x2C).Signals.Count == 6, "C91 phase-temperature inputs are incomplete");
            C91InputTableDefinition analog = tables.Single(table => table.AddressOffset == 0x00);
            byte[] analogData = new byte[analog.ByteLength];
            Array.Copy(BitConverter.GetBytes(500.25f), 0, analogData, 184, 4);
            C91InputSignalResult hv = C91InputSignalResult.Decode(analog, analog.Signals.Single(signal => signal.Offset == 184), analogData);
            Assert(hv.SignalName == "INVTRA_HVDC_SENSE2_AI" && hv.ValueText == "500.25", "C91 high-voltage input decoding is incorrect");
            C91InputTableDefinition discrete = tables.Single(table => table.AddressOffset == 0x18);
            C91InputSignalResult fault = C91InputSignalResult.Decode(discrete, discrete.Signals.Single(signal => signal.Offset == 1), new byte[discrete.ByteLength]);
            Assert(fault.Interpretation.IndexOf("已触发", StringComparison.Ordinal) >= 0, "C91 active-low input decoding is incorrect");
        }

        private static void C91ProfileContainsFtEntrySequence()
        {
            ProductCanProfile c91 = ProductCanProfile.For(ProductModel.C91);
            ProductCanProfile c95 = ProductCanProfile.For(ProductModel.C95);
            Assert(c91.ResolverDataLength == 9, "C91 resolver-data length is incorrect");
            Assert(c95.ResolverDataLength == 9, "C95 resolver-data length is incorrect");
            Assert(c91.FtEntryRequests.Count == 5, "C91 APP-to-FT sequence must contain five UDS requests");
            Assert(c91.FtEntryRequests[0].Request == "10 03" && c91.FtEntryRequests[4].Request == "11 01", "C91 APP-to-FT request order is incorrect");
            Assert(c95.FtEntryRequests.Count == 0, "C95 must retain the current already-in-FT behavior");
            Assert(!c91.SupportsLocatorPages && c95.SupportsLocatorPages, "Locator page capability must follow the selected model");
        }

        private static void ExpandsC95AllTableReadsIntoParsedFields()
        {
            C95TableDefinition analogTable = C95AllTableCatalog.Tables.Single(table => table.AddressOffset == 0x00);
            byte[] analog = new byte[analogTable.ByteLength];
            Array.Copy(BitConverter.GetBytes(500.25f), 0, analog, 44, 4);
            IReadOnlyList<C95TableFieldResult> analogFields = C95TableFieldDecoder.Decode(C95TableReadResult.Success(analogTable, "0x40000000", analog));
            Assert(analogFields.Count == 57, "analog table must expand to all 57 named signals");
            Assert(analogFields.Any(field => field.FieldName == "HVDC_SENSE_AI" && field.ValueText == "500.25"), "named HVDC value is missing from all-table fields");

            C95TableDefinition statusTable = C95AllTableCatalog.Tables.Single(table => table.AddressOffset == 0x5C);
            IReadOnlyList<C95TableFieldResult> statusFields = C95TableFieldDecoder.Decode(C95TableReadResult.Success(statusTable, "0x40001000", new byte[] { 4, 3, 0, 0, 0x1C, 0x85, 0x02, 0 }));
            Assert(statusFields.Count == 8, "motor status must expand to eight bytes");
            Assert(statusFields.Single(field => field.FieldOffset == 4).Interpretation.IndexOf("C相过流", StringComparison.Ordinal) >= 0, "motor fault byte 4 was not parsed");

            int allFieldCount = 0;
            foreach (C95TableDefinition table in C95AllTableCatalog.Tables)
            {
                C95TableReadResult tableResult = C95TableReadResult.Success(table, "0x40000000", new byte[table.ByteLength]);
                IReadOnlyList<C95TableFieldResult> fields = C95TableFieldDecoder.Decode(tableResult);
                Assert(fields.All(field => field.Interpretation != "数据越界"), "field definition exceeds table length: " + table.Name);
                allFieldCount += fields.Count;
            }
            Assert(allFieldCount > 680, "all-table parser did not expand the complete locator field set");
        }

        private static void C96ProfileExposesBothIndependentDrives()
        {
            ProductCanProfile product = ProductCanProfile.For(ProductModel.C96);
            C96DriveProfile tm1 = C96DriveProfile.For(C96Drive.TM1);
            C96DriveProfile tm2 = C96DriveProfile.For(C96Drive.TM2);
            Assert(product.IsDualDrive, "C96 must be marked as dual drive");
            Assert(tm1.MotorControlOffset == 0x68 && tm1.MotorStatusOffset == 0x6C, "C96 TM1 motor offsets are incorrect");
            Assert(tm1.ResolverOffset == 0x44 && tm1.CurrentCommandOffset == 0x78 && tm1.CurrentResultOffset == 0x7C, "C96 TM1 read offsets are incorrect");
            Assert(tm2.MotorControlOffset == 0x80 && tm2.MotorStatusOffset == 0x84, "C96 TM2 motor offsets are incorrect");
            Assert(tm2.ResolverOffset == 0x48 && tm2.CurrentCommandOffset == 0x90 && tm2.CurrentResultOffset == 0x94, "C96 TM2 read offsets are incorrect");
            Assert(tm1.MotorControlLength == 39 && tm2.MotorControlLength == 39 && tm1.MotorStatusLength == 10 && tm2.CurrentResultLength == 40, "C96 dual-drive table lengths are incorrect");
        }

        private static void C92ReusesC96DualDriveLocator()
        {
            ProductCanProfile product = ProductCanProfile.For(ProductModel.C92);
            C96DriveProfile tm1 = C96DriveProfile.For(C96Drive.TM1);
            C96DriveProfile tm2 = C96DriveProfile.For(C96Drive.TM2);
            Assert(product.IsDualDrive, "C92 must be marked as dual drive");
            Assert(product.MotorControlOffset == 0x68 && product.MotorStatusOffset == 0x6C, "C92 TM1 profile offsets are incorrect");
            Assert(product.ResolverDataOffset == 0x44 && product.CurrentSenseCommandOffset == 0x78 && product.CurrentSenseResultOffset == 0x7C, "C92 TM1 read offsets are incorrect");
            Assert(tm2.MotorControlOffset == 0x80 && tm2.MotorStatusOffset == 0x84 && tm2.ResolverOffset == 0x48, "C92 TM2 reused locator offsets are incorrect");
            Assert(product.DisplayName.IndexOf("C92", StringComparison.Ordinal) >= 0, "C92 must have a distinct display name");
        }

        private static void C96FtEnablesUvResetOffsetsMatchLocator()
        {
            Assert(C96FtEnables.TableOffset == 0x3C, "FT_Enables table offset must be 0x3C");
            Assert(C96FtEnables.UvloResetIndex(C96Drive.TM1) == 8 && C96FtEnables.UvupResetIndex(C96Drive.TM1) == 9, "TM1 UV reset offsets are incorrect");
            Assert(C96FtEnables.UvloResetIndex(C96Drive.TM2) == 22 && C96FtEnables.UvupResetIndex(C96Drive.TM2) == 23, "TM2 UV reset offsets are incorrect");
            Assert(C96FtEnables.OverCurrentResetIndex(C96Drive.TM1) == 4 && C96FtEnables.OverCurrentResetIndex(C96Drive.TM2) == 20, "TM1/TM2 hardware OC reset offsets are incorrect");
            Assert(C96FtEnables.SharedBusOverVoltageResetIndex == 7, "shared Bus HW OV reset offset is incorrect");
            Assert(C96FtEnables.SharedBusOverVoltageResetSignalName == "INVTRA_FLTRST_OV", "shared Bus HW OV reset signal name is incorrect");
            Assert(C96FtEnables.UvloSignalName(C96Drive.TM2) == "INVTRB_FLTRST_UVLO", "TM2 UVLO signal name is incorrect");
            Assert(C96FtEnables.UvupSignalName(C96Drive.TM2) == "INVTRB_FLTRST_UVUP", "TM2 UVUP signal name is incorrect");
        }


        private static void BuildsC96DualDriveMotorControlPayload()
        {
            C96MotorControlCommand settings = new C96MotorControlCommand(0, 100, 20, 10, 60, 4, 50, 10000, true, true, false, 3000, false, 0);
            byte[] command = CanProtocol.BuildC96MotorControlWrite(0x12345678, settings);
            Assert(command.Length == 47, "C96 motor-control command must contain an 8-byte header and 39-byte payload");
            Assert(command.Take(8).SequenceEqual(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x00, 0x27, 0x00, 0x00 }), "C96 motor-control header is incorrect");
            Assert(Math.Abs(BitConverter.ToSingle(command, 12) - 141.4f) < 0.01, "C96 target RMS was not converted to peak current");
            Assert(BitConverter.ToUInt16(command, 30) == 50 && BitConverter.ToUInt16(command, 32) == 10000, "C96 ramp or base frequency is incorrect");
            Assert(command[34] == 1 && command[35] == 0xFF && command[36] == 1, "C96 gate, NewData, or reset-fault field is incorrect");
            Assert(command[37] == 0 && Math.Abs(BitConverter.ToSingle(command, 38) - 3000f) < 0.001, "C96 speed fields are incorrect");
            byte[] tm2 = CanProtocol.BuildC96Tm2MotorControlPayload(settings); Assert(tm2.Length == 39, "C96 TM2 motor-control payload must match the hardware-proven 39-byte command"); Assert(Math.Abs(BitConverter.ToSingle(tm2, 4) - 141.4f) < 0.01 && Math.Abs(BitConverter.ToSingle(tm2, 12) - 10f) < 0.001 && BitConverter.ToUInt16(tm2, 22) == 50 && BitConverter.ToUInt16(tm2, 24) == 10000 && tm2[26] == 1 && tm2[27] == 0xFF, "C96 TM2 current/hold/ramp/base-frequency/gate/NewData offsets are incorrect"); Assert(Math.Abs(BitConverter.ToSingle(tm2, 30) - 3000f) < 0.001 && tm2[29] == 0, "C96 TM2 speed-control offsets are incorrect");
        }

        private static void C96InputCatalogCoversLocatorCurrentValueTables()
        {
            IReadOnlyList<C96InputTableDefinition> tables = C96InputCatalog.Tables;
            Assert(tables.Count == 6, "C96 read page must include six current-value input tables");
            Assert(tables.Sum(table => table.Signals.Count) == 192, "C96 current-value input signal count is incomplete");
            Assert(tables.Single(table => table.AddressOffset == 0x00).ByteLength == 252, "C96 analog table length is incorrect");
            Assert(tables.Single(table => table.AddressOffset == 0x0C).ByteLength == 126, "C96 analog-count table length is incorrect");
            Assert(tables.Single(table => table.AddressOffset == 0x18).ByteLength == 101, "C96 discrete table must preserve the locator's four-byte offsets");
            Assert(tables.Single(table => table.AddressOffset == 0x20).ByteLength == 48, "C96 pulse table length is incorrect");
            Assert(tables.Single(table => table.AddressOffset == 0x2C).ByteLength == 48, "C96 phase-temperature table length is incorrect");
            Assert(tables.Single(table => table.AddressOffset == 0xB0).ByteLength == 8, "C96 resolver-frequency table length is incorrect");
        }

        private static void ParsesC96CurrentResolverAndMotorStatus()
        {
            float[] currents = { 1, 2, 3, -140, -141, -142, 142, 143, 144, 100.25f };
            C96CurrentResult current = C96CurrentResult.Parse(C96Drive.TM2, currents.SelectMany(BitConverter.GetBytes).ToArray());
            Assert(current.Drive == C96Drive.TM2 && current.Phases.Count == 3, "C96 current result drive or phase count is incorrect");
            Assert(Math.Abs(current.ReportedRms - 100.25) < 0.001, "C96 reported RMS was not decoded");

            C96ResolverResult resolver = C96ResolverResult.Parse(C96Drive.TM1, BitConverter.GetBytes(700f).Concat(BitConverter.GetBytes(225f)).Concat(new byte[] { 3 }).ToArray());
            Assert(Math.Abs(resolver.SpeedRpm - 700) < 0.001 && Math.Abs(resolver.AngleDegrees - 225) < 0.001, "C96 resolver speed or angle is incorrect");
            Assert(resolver.FaultCode == 3, "C96 resolver fault code is incorrect");

            C96MotorStatusInfo status = C96MotorStatusInfo.Parse(C96Drive.TM1, new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 2, 3 });
            Assert(status.RampMode == 2 && status.SequenceStatus == 3, "C96 motor status byte order is incorrect");
            Assert(status.ActiveFaults.Count == 1, "C96 motor fault bytes were not decoded");
        }

        private static void ValidatesResolverPolePairSetting()
        {
            Assert(CanProtocol.ValidateResolverPolePairs(1) == 1, "one pole pair must be accepted");
            Assert(CanProtocol.ValidateResolverPolePairs(6) == 6, "six pole pairs must be accepted");
            Assert(CanProtocol.ValidateResolverPolePairs(255) == 255, "255 pole pairs must be accepted");
            AssertThrows<ArgumentOutOfRangeException>(() => CanProtocol.ValidateResolverPolePairs(0));
            AssertThrows<ArgumentOutOfRangeException>(() => CanProtocol.ValidateResolverPolePairs(256));
            AssertThrows<ArgumentException>(() => CanProtocol.ValidateResolverPolePairs(6.5));
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: {0}: {1}", name, ex.Message);
                Environment.Exit(1);
            }
        }

        private static void ParseMixedHexSeparators()
        {
            byte[] actual = HexDataParser.Parse("0x02, 02- AF");
            Assert(actual.SequenceEqual(new byte[] { 0x02, 0x02, 0xAF }), "mixed separators did not parse");
        }

        private static void RejectMalformedHex()
        {
            AssertThrows<FormatException>(() => HexDataParser.Parse("02 GG"));
        }

        private static void RejectMoreThanEightBytes()
        {
            AssertThrows<ArgumentException>(() => HexDataParser.Parse("01 02 03 04 05 06 07 08 09"));
        }

        private static void ParsesMultiFrameTableBuffers()
        {
            string text = string.Join(" ", Enumerable.Range(0, 192).Select(value => (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));
            byte[] actual = HexDataParser.ParseBuffer(text);
            Assert(actual.Length == 192, "table buffer length was truncated");
            Assert(actual[0] == 0 && actual[191] == 191, "table buffer content did not parse correctly");
        }

        private static void CatalogPreservesSequenceOrder()
        {
            var names = CanSequenceCatalog.OrderedSteps.Select(step => step.Name).ToArray();
            Assert(names[0] == "Enter FT Mode", "first step is not Enter FT Mode");
            Assert(names[1] == "Exit FT Mode", "second step is not Exit FT Mode");
            Assert(names[2] == "DUT Communication Init", "third step is not DUT Communication Init");
            Assert(names[3] == "CAN Communication", "fourth step is not CAN Communication");
        }

        private static void CatalogContainsResolverPresets()
        {
            Assert(CanSequenceCatalog.OrderedSteps.Any(step => step.Name == "Set Speed 700 RPM" && step.Value == 700), "700 RPM preset missing");
            Assert(CanSequenceCatalog.OrderedSteps.Any(step => step.Name == "Set Speed 3500 RPM" && step.Value == 3500), "3500 RPM preset missing");
            Assert(CanSequenceCatalog.OrderedSteps.Any(step => step.Name == "Set Speed 7000 RPM" && step.Value == 7000), "7000 RPM preset missing");
            Assert(CanSequenceCatalog.OrderedSteps.Any(step => step.Name == "Set Position 225" && step.Value == 225), "225 position preset missing");
            Assert(CanSequenceCatalog.OrderedSteps.Any(step => step.Name == "Set Position 315" && step.Value == 315), "315 position preset missing");
        }

        private static void BuildsDutCommunicationInitFrame()
        {
            Assert(HexDataParser.Format(CanProtocol.BuildDutCommunicationInit()) == "FF FA 55 A9 00 04 FF 00", "DUT init frame differs from TestDllMain");
        }

        private static void BuildsProductCommunicationTestFrame()
        {
            Assert(HexDataParser.Format(CanProtocol.BuildProductCommunicationTest()) == "02 02 02 02 02 02 02 02", "CAN communication test frame differs from SEQ");
        }

        private static void BuildsWakeupFrame()
        {
            Assert(HexDataParser.Format(CanProtocol.BuildWakeupFrame()) == "FF FF FF FF FF FF FF FF", "wakeup frame differs from TestDllMain");
        }

        private static void UsesActualCanCardConnectionSettings()
        {
            CanChannelConfig product = CanChannelConfig.ProductDefaults();
            CanChannelConfig resolver = CanChannelConfig.ResolverDefaults();

            Assert(product.DeviceType == 52 && resolver.DeviceType == 52, "CAN-FD TCP device type must be 52");
            Assert(product.Ip == "192.166.6.10" && resolver.Ip == "192.166.6.10", "CAN card IP must match the working ZCANPRO configuration");
            Assert(!product.UseCanFd && !resolver.UseCanFd, "The manual tool must open these channels in classic CAN mode");
            Assert(product.BaudRate == 500000 && resolver.BaudRate == 500000, "CAN baud rate is incorrect");
        }

        private static void C91ProfileUsesOriginalSeqAddresses()
        {
            ProductCanProfile profile = ProductCanProfile.For(ProductModel.C91);
            IReadOnlyList<PreCurrentReadItem> items = profile.PreCurrentReadItems;

            Assert(items.Count == 6, "pre-current guide must contain six product values");
            AssertReadItem(items[0], "产品母线高压", 0, 184, 4, "V");
            AssertReadItem(items[1], "Battery 电压", 0, 128, 4, "V");
            AssertReadItem(items[2], "PSR 电压", 0, 84, 4, "V");
            AssertReadItem(items[3], "HVDC_OV_FLT", 24, 1, 1, "");
            AssertReadItem(items[4], "OV_FLT", 24, 19, 1, "");
            AssertReadItem(items[5], "产品板温", 0, 68, 4, "℃");
            Assert(profile.MotorControlOffset == 0x60, "C91 motor-control offset is incorrect");
            Assert(profile.MotorStatusOffset == 0x64 && profile.MotorStatusLength == 9, "C91 motor-status layout is incorrect");
            Assert(profile.ResolverDataOffset == 0x48, "C91 resolver-data offset is incorrect");
            Assert(profile.CurrentSenseCommandOffset == 0x70 && profile.CurrentSenseResultOffset == 0x74, "C91 current-sense offsets are incorrect");
            Assert(profile.NewDataFlag == 0x01, "C91 new-data flag must remain 0x01");
        }

        private static void C95ProfileUsesLocatorAddresses()
        {
            ProductCanProfile profile = ProductCanProfile.For(ProductModel.C95);
            IReadOnlyList<PreCurrentReadItem> items = profile.PreCurrentReadItems;

            Assert(items.Count == 5, "C95 guide must contain the five available product values");
            AssertReadItem(items[0], "产品母线高压", 0, 44, 4, "V");
            AssertReadItem(items[1], "Battery 电压", 0, 80, 4, "V");
            AssertReadItem(items[2], "HVDC_OV_FLT", 24, 13, 1, "");
            AssertReadItem(items[3], "OV_FLT", 24, 14, 1, "");
            AssertReadItem(items[4], "产品板温", 0, 192, 4, "℃");
            Assert(items[0].SourceName == "FT_Analog_Inputs / HVDC_SENSE_AI", "C95 HV source label is missing");
            Assert(items[0].AddressText.IndexOf("44", StringComparison.Ordinal) >= 0, "C95 HV address text must show byte offset 44");
            Assert(items[2].ActiveLow && items[3].ActiveLow, "C95 discrete fault inputs must be marked active-low");
            Assert(profile.MotorControlOffset == 0x58, "C95 motor-control offset is incorrect");
            Assert(profile.MotorStatusOffset == 0x5C && profile.MotorStatusLength == 8, "C95 motor-status layout is incorrect");
            Assert(profile.ResolverDataOffset == 0x44, "C95 resolver-data offset is incorrect");
            Assert(profile.CurrentSenseCommandOffset == 0x6C && profile.CurrentSenseResultOffset == 0x70, "C95 current-sense offsets are incorrect");
            Assert(profile.NewDataFlag == 0xFF, "C95 new-data flag must be 0xFF");
        }

        private static void PreCurrentReadContinuesAfterOneItemFails()
        {
            List<int> tableIndexes = new List<int>();
            IReadOnlyList<PreCurrentReadResult> results = PreCurrentStatusReader.ReadAll(ProductCanProfile.For(ProductModel.C91).PreCurrentReadItems, (addressOffset, tableIndex, dataSize) =>
            {
                tableIndexes.Add(tableIndex);
                if (tableIndex == 128) throw new InvalidOperationException("read failed");
                return tableIndex + 0.5;
            });

            Assert(tableIndexes.SequenceEqual(new[] { 184, 128, 84, 1, 19, 68 }), "a failed item stopped or reordered later reads");
            Assert(results.Count == 6, "one result must be returned for every requested value");
            Assert(results[0].Succeeded && results[0].Value == 184.5, "first successful value is incorrect");
            Assert(!results[1].Succeeded && results[1].Error == "read failed", "failed item did not preserve its error");
            Assert(results.Skip(2).All(result => result.Succeeded), "later values were not read after one failure");
        }

        private static void PreCurrentResultFormatsParsedValues()
        {
            PreCurrentReadResult voltage = PreCurrentReadResult.Success(
                new PreCurrentReadItem("产品母线高压", 0, 184, 4, "V"), 618.3);
            PreCurrentReadResult fault = PreCurrentReadResult.Success(
                new PreCurrentReadItem("OV_FLT", 24, 19, 1, ""), 1);
            PreCurrentReadResult failure = PreCurrentReadResult.Failure(
                new PreCurrentReadItem("PSR 电压", 0, 84, 4, "V"), "read failed");

            Assert(voltage.FormatValue() == "618.3 V", "voltage display format is incorrect");
            Assert(fault.FormatValue() == "1", "fault display format is incorrect");
            Assert(failure.FormatValue() == "读取失败：read failed", "failure display format is incorrect");

            PreCurrentReadResult motorStatus = PreCurrentReadResult.SuccessText(
                new PreCurrentReadItem("Motor Status", 0x5C, 0, 8, ""), "02 01 00 00 00 00 00 00");
            Assert(motorStatus.FormatValue() == "02 01 00 00 00 00 00 00", "text result display format is incorrect");
        }

        private static void EveryCurrentStepOpensPreCurrentGuide()
        {
            CanSequenceStep zeroCurrent = new CanSequenceStep("Set DUT Current 0 A", "CAN_SetDUTCurrent", 0.01);
            CanSequenceStep oneHundredAmps = new CanSequenceStep("Set DUT Current 100 A", "CAN_SetDUTCurrent", 100);
            CanSequenceStep communication = new CanSequenceStep("CAN Communication", "Test_CANCommunication");

            Assert(CanSequenceRules.RequiresPreCurrentGuide(zeroCurrent), "zero-current step must open the read guide");
            Assert(CanSequenceRules.RequiresPreCurrentGuide(oneHundredAmps), "every output-current step must open the read guide");
            Assert(!CanSequenceRules.RequiresPreCurrentGuide(communication), "non-current step must not open the read guide");
        }

        private static void CurrentStepsIncludeMatchingProductCurrentReads()
        {
            CanSequenceStep[] steps = CanSequenceCatalog.OrderedSteps.ToArray();
            CanSequenceStep zero = steps.Single(step => step.Name == "Set DUT Current 0 A");
            Assert(Math.Abs(zero.StepCurrent - 0.01) < 0.000001, "0 A step current must be 0.01 A");

            for (int current = 0; current <= 900; current += 100)
            {
                int setIndex = Array.FindIndex(steps, step => step.Name == string.Format("Set DUT Current {0} A", current));
                Assert(setIndex >= 0, "current set step is missing: " + current);
                Assert(setIndex + 1 < steps.Length, "current read step is missing: " + current);
                Assert(steps[setIndex + 1].FunctionName == "CAN_ReadDutCurrent", "product-current read must follow set step: " + current);
                Assert(Math.Abs(steps[setIndex + 1].Value - (current == 0 ? 0.01 : current)) < 0.000001, "read step current label is incorrect");
            }
        }

        private static void BuildsC91EffectiveToPeakCurrentCommand()
        {
            byte[] command = CanProtocol.BuildDutCurrentWrite(0x12345678, 100, 20, 10, 60, ProductCanProfile.For(ProductModel.C91).NewDataFlag);
            Assert(command.Length == 40, "current command must contain five CAN frames");
            Assert(command.Take(8).SequenceEqual(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x00, 0x20, 0x00, 0x00 }), "current command header is incorrect");
            Assert(Math.Abs(BitConverter.ToSingle(command, 12) - 141.4f) < 0.01, "100 A RMS was not converted to 141.4 A peak");
            Assert(Math.Abs(BitConverter.ToSingle(command, 16) - 20f) < 0.001, "step current must not be multiplied by 1.414");
            Assert(command[35] == 0x01, "C91 new-data flag is incorrect");
        }

        private static void BuildsC95CurrentCommandWithLocatorFlag()
        {
            byte[] command = CanProtocol.BuildDutCurrentWrite(0x01020304, 100, 20, 10, 60, ProductCanProfile.For(ProductModel.C95).NewDataFlag);
            Assert(command[28] == 0x04, "C95 motor-control mode must be 0x04");
            Assert(command[30] == 0x32 && command[31] == 0x00, "C95 ramp time must be 50");
            Assert(command[32] == 0x10 && command[33] == 0x27, "C95 base frequency must be 10000");
            Assert(command[34] == 0x01, "C95 gate enable must be one");
            Assert(command[35] == 0xFF, "C95 new-data flag must be 0xFF");
        }

        private static void ParsesProductPhaseCurrentRms()
        {
            float[] values = { 1, 2, 3, -140, -141, -142, 142, 143, 144 };
            byte[] data = values.SelectMany(BitConverter.GetBytes).ToArray();
            DutCurrentResult result = DutCurrentResult.Parse(data, new byte[] { 0x02, 0x01, 0, 0, 0, 0, 0, 0 });

            Assert(result.Phases.Count == 3, "three product phases must be returned");
            Assert(result.Phases[0].Name == "A" && Math.Abs(result.Phases[0].Rms - 99.717) < 0.001, "phase A RMS is incorrect");
            Assert(result.Phases[1].Name == "B" && Math.Abs(result.Phases[1].Rms - 100.424) < 0.001, "phase B RMS is incorrect");
            Assert(result.Phases[2].Name == "C" && Math.Abs(result.Phases[2].Rms - 101.132) < 0.001, "phase C RMS is incorrect");
            Assert(result.MotorStatusText == "02 01 00 00 00 00 00 00", "motor-status raw text is incorrect");
            Assert(result.MotorStatusDescription.IndexOf("运行", StringComparison.Ordinal) >= 0, "motor-status description is incorrect");
        }

        private static void PadsShortClassicCanProtocolFrames()
        {
            byte[] padded = CanProtocol.NormalizeClassicFrame(new byte[] { 1, 2, 3, 4, 5 });
            Assert(padded.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 0, 0, 0 }), "short protocol frame was not padded to eight bytes");
            Assert(CanProtocol.NormalizeClassicFrame(new byte[8]).Length == 8, "eight-byte protocol frame changed length");
            AssertThrows<ArgumentException>(() => CanProtocol.NormalizeClassicFrame(new byte[9]));
        }

        private static void DecodesActiveLowFaultInputs()
        {
            PreCurrentReadItem item = new PreCurrentReadItem("HVDC_OV_FLT", 0x18, 13, 1, "", "FT_Discrete_Inputs / HVDC_OV_FLT^", true);
            PreCurrentReadResult normal = PreCurrentReadResult.Success(item, 1, item.Interpret(1));
            PreCurrentReadResult fault = PreCurrentReadResult.Success(item, 0, item.Interpret(0));

            Assert(normal.Interpretation.IndexOf("未触发", StringComparison.Ordinal) >= 0, "active-low value 1 must mean not asserted");
            Assert(fault.Interpretation.IndexOf("已触发", StringComparison.Ordinal) >= 0, "active-low value 0 must mean asserted");
        }

        private static void DiagnosesC95OutputBlockingConditions()
        {
            ProductCanProfile profile = ProductCanProfile.For(ProductModel.C95);
            List<PreCurrentReadResult> results = new List<PreCurrentReadResult>
            {
                PreCurrentReadResult.Success(profile.PreCurrentReadItems[0], 32.165),
                PreCurrentReadResult.Success(profile.PreCurrentReadItems[2], 0, profile.PreCurrentReadItems[2].Interpret(0)),
                PreCurrentReadResult.SuccessText(
                    new PreCurrentReadItem("Motor Status", 0x5C, 0, 8, "", "FT_Motor_Status_Data"),
                    "04 03 00 00 1C 85 02 00",
                    MotorStatusInfo.Parse(new byte[] { 4, 3, 0, 0, 0x1C, 0x85, 0x02, 0 }).Summary)
            };

            string diagnosis = PreCurrentDiagnosticAnalyzer.Analyze(profile, results);
            Assert(diagnosis.IndexOf("阻止出流", StringComparison.Ordinal) >= 0, "diagnostic fault must be identified as blocking output");
            Assert(diagnosis.IndexOf("400V", StringComparison.Ordinal) >= 0, "C95 minimum bus voltage must be included");
            Assert(diagnosis.IndexOf("C相过流", StringComparison.Ordinal) >= 0, "active packed fault must be decoded");
            Assert(diagnosis.IndexOf("HVDC_OV_FLT", StringComparison.Ordinal) >= 0, "active-low input fault must be included");
        }

        private static void C95InputTablesCatalogCoversCompleteSheet()
        {
            IReadOnlyList<C95InputTableDefinition> tables = C95InputCatalog.Tables;
            Assert(tables.Count == 5, "C95 Input Tables page must contain five current-value tables");
            Assert(tables.Sum(table => table.Signals.Count) == 189, "C95 Input Tables page signal count is incomplete");
            Assert(tables[0].AddressOffset == 0x00 && tables[0].ByteLength == 228, "analog input table layout is incorrect");
            Assert(tables[1].AddressOffset == 0x0C && tables[1].ByteLength == 114, "analog count table layout is incorrect");
            Assert(tables[2].AddressOffset == 0x18 && tables[2].ByteLength == 49, "discrete input table layout is incorrect");
            Assert(tables[3].AddressOffset == 0x20 && tables[3].ByteLength == 80, "pulse input table layout is incorrect");
            Assert(tables[4].AddressOffset == 0x2C && tables[4].ByteLength == 24, "phase temperature table layout is incorrect");
            Assert(tables[0].Signals.Any(signal => signal.Offset == 44 && signal.Name == "HVDC_SENSE_AI"), "C95 HVDC analog definition is missing");
            Assert(tables[1].Signals.Any(signal => signal.Offset == 22 && signal.Name == "HVDC_SENSE_AI"), "C95 HVDC ADC-count definition is missing");
        }

        private static void C95InputTablesValuesDecodeByTableType()
        {
            C95InputSignalDefinition analog = C95InputCatalog.Tables[0].Signals[0];
            byte[] analogBytes = new byte[228];
            Array.Copy(BitConverter.GetBytes(32.165f), 0, analogBytes, analog.Offset, 4);
            C95InputSignalResult analogResult = C95InputSignalResult.Decode(C95InputCatalog.Tables[0], analog, analogBytes);
            Assert(Math.Abs(analogResult.NumericValue - 32.165) < 0.001, "float32 input did not decode");

            C95InputSignalDefinition count = C95InputCatalog.Tables[1].Signals[0];
            C95InputSignalResult countResult = C95InputSignalResult.Decode(C95InputCatalog.Tables[1], count, new byte[] { 0x34, 0x12 }.Concat(new byte[112]).ToArray());
            Assert(countResult.NumericValue == 0x1234, "uint16 ADC count did not decode");

            C95InputSignalDefinition discrete = C95InputCatalog.Tables[2].Signals[3];
            byte[] discreteBytes = new byte[49];
            discreteBytes[3] = 0;
            C95InputSignalResult discreteResult = C95InputSignalResult.Decode(C95InputCatalog.Tables[2], discrete, discreteBytes);
            Assert(discreteResult.Interpretation.IndexOf("触发", StringComparison.Ordinal) >= 0, "active-low discrete input was not interpreted");
        }

        private static void DecodesC95MotorStatusAndFaultBytes()
        {
            MotorStatusInfo running = MotorStatusInfo.Parse(new byte[] { 2, 1, 0, 0, 0, 0, 0, 0 });
            Assert(running.RampModeDescription.IndexOf("保持", StringComparison.Ordinal) >= 0, "ramp mode 2 must decode as hold");
            Assert(running.SequenceStatusDescription.IndexOf("运行", StringComparison.Ordinal) >= 0, "status 1 must decode as running");
            Assert(running.FaultDescription == "无故障位", "zero fault bytes must decode as no fault bits");

            MotorStatusInfo fault = MotorStatusInfo.Parse(new byte[] { 4, 3, 0, 0, 0x41, 0x02, 0x80, 0x04 });
            Assert(fault.FaultDescription.IndexOf("A相过流", StringComparison.Ordinal) >= 0, "byte 4 bit 0 was not decoded");
            Assert(fault.FaultDescription.IndexOf("母线欠压", StringComparison.Ordinal) >= 0, "byte 4 bit 6 was not decoded");
            Assert(fault.FaultDescription.IndexOf("板温故障", StringComparison.Ordinal) >= 0, "byte 5 bit 1 was not decoded");
            Assert(fault.FaultDescription.IndexOf("上桥臂欠压", StringComparison.Ordinal) >= 0, "byte 6 bit 7 was not decoded");
            Assert(fault.FaultDescription.IndexOf("电机超速", StringComparison.Ordinal) >= 0, "byte 7 bit 2 was not decoded");
        }

        private static void AssertReadItem(PreCurrentReadItem item, string name, uint addressOffset, int tableIndex, int dataSize, string unit)
        {
            Assert(item.Name == name, "pre-current item name is incorrect");
            Assert(item.AddressOffset == addressOffset, "pre-current address offset is incorrect");
            Assert(item.TableIndex == tableIndex, "pre-current table index is incorrect");
            Assert(item.DataSize == dataSize, "pre-current data size is incorrect");
            Assert(item.Unit == unit, "pre-current unit is incorrect");
        }

        private static void Assert(bool condition, string message = "assertion failed")
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            _assertions++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException("expected " + typeof(TException).Name);
        }
    }
}
