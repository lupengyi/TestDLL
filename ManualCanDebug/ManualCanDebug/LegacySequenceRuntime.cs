using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using CSP;
using Newtonsoft.Json.Linq;

namespace ManualCanDebug
{
    internal sealed class LegacySequenceRuntime : IDisposable
    {
        private const int SocketIndex = 0;
        private readonly string _baseDirectory;
        private readonly MainClass _mainClass;
        private readonly SequenceManage _sequenceManage;
        private readonly SeqRunTimeState _runtimeState;
        private readonly object _testDllMain;
        private bool _disposed;
        private bool _instrumentsInitialized;
        private bool _hardwareTouched;
        private bool _debugSessionStarted;
        public LegacyStepExecutionResult LastStepExecution { get; private set; }

        public LegacySequenceRuntime(string baseDirectory, string defaultSequencePath)
        {
            _baseDirectory = EnsureTrailingSeparator(Path.GetFullPath(baseDirectory));
            string testDllPath = Path.Combine(_baseDirectory, "LegacyRuntime", "CSP.TestDLL.dll");
            if (!File.Exists(testDllPath))
                throw new FileNotFoundException("找不到原平台主 DLL。", testDllPath);
            if (!File.Exists(defaultSequencePath))
                throw new FileNotFoundException("找不到默认 SEQ。", defaultSequencePath);

            WriteRuntimeStationConfig(testDllPath, defaultSequencePath);
            _mainClass = new MainClass(_baseDirectory, 0);
            _mainClass.UIMessageReceived += MainClass_UIMessageReceived;
            _mainClass.EventHapped += MainClass_EventHapped;
            _sequenceManage = SequenceManage.GetInstance();
            _runtimeState = SeqRunTimeState.GetInstance(1);
            _testDllMain = GetPrivateField(_mainClass, "CreatedInstance");
            if (_testDllMain == null)
                throw new InvalidOperationException("原平台没有创建 CSP.TestDllMain 实例。");
            if (SupportsFunction("FCT_SetCanDiagnostics")) InvokeVoid("FCT_SetCanDiagnostics", true);
            LogMessage("原平台执行引擎已加载；TestDllMain.cs 未修改，PLC 自动循环未启动。");
        }

        public event Action<string> Log;
        public event Action<int> CurrentStepChanged;

        public bool InstrumentsInitialized { get { return _instrumentsInitialized; } }
        public IReadOnlyCollection<string> InitializedInstrumentNames
        {
            get
            {
                if (!_instrumentsInitialized) return new string[0];
                string value = Convert.ToString(InvokeTestDll("FCT_GetInitializedInstruments", new object[0])) ?? string.Empty;
                return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim().ToUpperInvariant()).Where(name => name.Length > 0).ToArray();
            }
        }
        /// <summary>
        /// Runs the debug host's single socket as the station the operator picked, so channel and path
        /// mappings resolve exactly like they will on that station in production.
        /// </summary>
        public bool SupportsFunction(string functionName)
        {
            return !string.IsNullOrWhiteSpace(functionName) && _testDllMain.GetType().GetMethod(functionName, BindingFlags.Public | BindingFlags.Instance) != null;
        }
        public int ValidateSequenceFile(string path)
        {
            ThrowIfDisposed();
            if (IsRunning || _debugSessionStarted) throw new InvalidOperationException("运行或调试期间不能执行SEQ离线校验。");
            List<StepSetting_UI> steps;
            _sequenceManage.GetSequence(path, out steps);
            if (steps == null || steps.Count == 0) throw new InvalidOperationException("原CSP引擎未能从导出文件加载任何STEP。");
            return steps.Count;
        }
        public bool IsRunning
        {
            get
            {
                bool[] values = _runtimeState.SequenceRunning;
                return values != null && values.Length > 0 && values[SocketIndex];
            }
        }

        public async Task InitializeInstrumentsAsync()
        {
            ThrowIfDisposed();
            if (_instrumentsInitialized)
            {
                LogMessage("原平台全部仪器已经初始化，无需重复连接。");
                return;
            }

            LogMessage("开始执行原 TestDllMain.ProcessSetup：RES、产品CAN、旋变CAN、LVDC、HVDC、MOXA、DMM、继电器、PLC。");
            _hardwareTouched = true;
            try
            {
                double result = await Task.Run(() => InvokeDouble("ProcessSetup"));
                if (Math.Abs(result) > double.Epsilon)
                    throw new InvalidOperationException("ProcessSetup 返回 " + result + "。期望为 0。");
                _instrumentsInitialized = true;
                LogMessage("原平台全部仪器初始化完成。PLC AutomationLoop 未启动。");
            }
            catch
            {
                LogMessage("仪器初始化中断，正在尝试对已经连接的设备执行安全清理。");
                try { await Task.Run(() => PerformSafeShutdown(false)); } catch (Exception cleanupEx) { LogMessage("初始化失败后的安全清理异常：" + cleanupEx.Message); }
                throw;
            }
        }

        public async Task InitializeInstrumentsAsync(string instrumentsJson)
        {
            ThrowIfDisposed();
            if (_instrumentsInitialized) throw new InvalidOperationException("仪器已经初始化。请先安全下电，再更改初始化选择。");
            if (string.IsNullOrWhiteSpace(instrumentsJson) || instrumentsJson == "[]") throw new InvalidOperationException("请先在仪器中心勾选至少一个需要初始化的仪器。");
            LogMessage("写入仪器中心选择，并执行平台原MainTest.ProcessSetup：" + instrumentsJson);
            _hardwareTouched = true;
            try
            {
                await Task.Run(() => InvokeVoid("FCT_SetInstrumentSelection", instrumentsJson));
                double result = await Task.Run(() => InvokeDouble("ProcessSetup"));
                if (Math.Abs(result) > double.Epsilon) throw new InvalidOperationException("ProcessSetup 返回 " + result + "。期望为 0。");
                _instrumentsInitialized = true;
                LogMessage("MainTest已按仪器中心选择完成初始化：" + Convert.ToString(InvokeTestDll("FCT_GetInitializedInstruments", new object[0])));
            }
            catch
            {
                LogMessage("选择性初始化失败，正在由MainTest清理已连接资源。");
                try { await Task.Run(() => InvokeDouble("ProcessCleanup")); } catch (Exception cleanupEx) { LogMessage("选择性初始化清理异常：" + cleanupEx.Message); }
                _instrumentsInitialized = false; _hardwareTouched = false;
                throw;
            }
        }

        public async Task<string> RunSingleStepAsync(string sequencePath, int stepIndex)
        {
            ThrowIfNotReady();
            if (IsRunning) throw new InvalidOperationException("当前已有流程正在运行。");
            LastStepExecution = null;
            _mainClass.OpenSequence(SocketIndex, sequencePath);
            int resultCountBefore = CurrentResultCount();
            object socket = GetSocketUut();
            MethodInfo method = socket.GetType().GetMethod("RunSingleStep", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(socket.GetType().FullName, "RunSingleStep");
            LogMessage("原平台单步执行：STEP " + (stepIndex + 1));
            object value = await Task.Run(() => Invoke(method, socket, new object[] { stepIndex }));
            string rawReturn = Convert.ToString(value) ?? string.Empty;
            LastStepExecution = CaptureStepExecution(rawReturn, resultCountBefore);
            return rawReturn;
        }

        public async Task PrepareDebugSessionAsync(string sequencePath)
        {
            ThrowIfNotReady();
            if (IsRunning) throw new InvalidOperationException("当前已有流程正在运行。");
            if (_debugSessionStarted) await EndDebugSessionAsync();
            _mainClass.OpenSequence(SocketIndex, sequencePath);
            LogMessage("开始功能块调试会话：执行平台原MainTest.PreUUT。");
            await Task.Run(() => InvokeDoubleWithSocket("PreUUT", SocketIndex));
            _debugSessionStarted = true;
        }

        public async Task<string> RunLoadedSingleStepAsync(int stepIndex)
        {
            ThrowIfNotReady();
            if (!_debugSessionStarted) throw new InvalidOperationException("调试会话尚未准备。");
            LastStepExecution = null;
            int resultCountBefore = CurrentResultCount();
            object socket = GetSocketUut();
            MethodInfo method = socket.GetType().GetMethod("RunSingleStep", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(socket.GetType().FullName, "RunSingleStep");
            object value = await Task.Run(() => Invoke(method, socket, new object[] { stepIndex }));
            string rawReturn = Convert.ToString(value) ?? string.Empty;
            LastStepExecution = CaptureStepExecution(rawReturn, resultCountBefore);
            return rawReturn;
        }

        public async Task EndDebugSessionAsync()
        {
            if (!_debugSessionStarted) return;
            LogMessage("结束功能块调试会话：执行平台原MainTest.PostUUT安全收尾。");
            try { await Task.Run(() => InvokeDoubleWithSocket("PostUUT", SocketIndex)); }
            finally { _debugSessionStarted = false; }
        }

        public int RuntimeCurrentStepIndex { get { return GetCurrentStepIndex(); } }
        public string GetRuntimeSnapshot()
        {
            ThrowIfDisposed();
            return Convert.ToString(InvokeTestDll("FCT_GetRuntimeSnapshot", new object[] { SocketIndex })) ?? "{}";
        }

        public async Task<LegacyRunResult> RunSequenceAsync(string sequencePath, int originalStartIndex, CancellationToken cancellationToken)
        {
            ThrowIfNotReady();
            if (IsRunning) throw new InvalidOperationException("当前已有流程正在运行。");
            _mainClass.OpenSequence(SocketIndex, sequencePath);
            _sequenceManage.SetSerialNumber(SocketIndex, "DEBUG-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            _sequenceManage.StartTest(SocketIndex);
            LogMessage("原平台流程已启动：" + sequencePath);

            bool observedRunning = false;
            int lastStep = -1;
            DateTime start = DateTime.Now;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    _sequenceManage.StopTest(SocketIndex);

                bool running = IsRunning;
                observedRunning |= running;
                int current = GetCurrentStepIndex();
                if (current >= 0 && current != lastStep)
                {
                    lastStep = current;
                    Action<int> handler = CurrentStepChanged;
                    if (handler != null) handler(originalStartIndex + current);
                }

                if (observedRunning && !running) break;
                if (!observedRunning && (DateTime.Now - start).TotalSeconds > 10)
                    throw new TimeoutException("原平台在10秒内没有进入运行状态。请查看原平台事件和仪器初始化日志。");
                await Task.Delay(50);
            }

            SequenceResult result = GetSequenceResult();
            string status = result == null ? string.Empty : result.TotalStatus;
            return new LegacyRunResult(status, cancellationToken.IsCancellationRequested, lastStep);
        }

        public void Stop()
        {
            if (_disposed) return;
            try
            {
                _sequenceManage.StopTest(SocketIndex);
                LogMessage("已向原平台发送停止流程命令。");
            }
            catch (Exception ex)
            {
                LogMessage("停止原平台流程失败：" + Unwrap(ex).Message);
            }
        }

        public async Task SafeShutdownAsync()
        {
            if (_disposed || !_hardwareTouched) return;
            Stop();
            LogMessage("开始安全下电：执行原 TestDllMain.PostUUT 和 ProcessCleanup。");
            await Task.Run(() => PerformSafeShutdown(true));
            LogMessage("安全下电及仪器断开完成。");
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                if (_hardwareTouched)
                {
                    Stop();
                    PerformSafeShutdown(false);
                }
            }
            catch (Exception ex)
            {
                LogMessage("关闭原平台资源时发生错误：" + Unwrap(ex).Message);
            }
            finally
            {
                try { _mainClass.CloseSequence(); } catch { }
                _mainClass.UIMessageReceived -= MainClass_UIMessageReceived;
                _mainClass.EventHapped -= MainClass_EventHapped;
                _disposed = true;
            }
        }

        private void WriteRuntimeStationConfig(string testDllPath, string defaultSequencePath)
        {
            string configDirectory = Path.Combine(_baseDirectory, "Config");
            Directory.CreateDirectory(configDirectory);
            JObject station = new JObject
            {
                ["SocketNumber"] = 1,
                ["SocketColumns"] = 1,
                ["DefaultON_MES"] = false,
                ["StopIfFailed"] = true,
                ["ActionLoged"] = true,
                ["StationName"] = "DEBUG",
                ["UIDisplayType"] = "All",
                ["StepDisplayType"] = "All",
                ["LogoFilePath"] = Path.Combine(configDirectory, "Logo1.png"),
                ["DLLFilePath"] = testDllPath,
                ["DefaultSequence"] = defaultSequencePath,
                ["DLLClassName"] = "CSP.TestDllMain",
                ["Title"] = "FCT 完整流程调试"
            };
            JObject root = new JObject
            {
                ["DefaultStationIndex"] = 0,
                ["StationConfigList"] = new JArray(station)
            };
            File.WriteAllText(Path.Combine(configDirectory, "StationConfig.json"), root.ToString());
        }

        private object GetSocketUut()
        {
            object value = GetPrivateField(_mainClass, "SocketUUTList");
            IList list = value as IList;
            if (list == null || list.Count == 0)
                throw new InvalidOperationException("原平台没有创建测试插槽。");
            return list[SocketIndex];
        }

        private int GetCurrentStepIndex()
        {
            int[] values = _runtimeState.CurrentStepIndex;
            return values == null || values.Length == 0 ? -1 : values[SocketIndex];
        }

        private SequenceResult GetSequenceResult()
        {
            SequenceResult[] values = _runtimeState.SequenceResults;
            return values == null || values.Length == 0 ? null : values[SocketIndex];
        }

        private int CurrentResultCount()
        {
            SequenceResult result = GetSequenceResult();
            return result == null || result.StepResultList_UI == null ? 0 : result.StepResultList_UI.Count;
        }

        private LegacyStepExecutionResult CaptureStepExecution(string rawReturn, int resultCountBefore)
        {
            SequenceResult result = GetSequenceResult();
            List<LegacyPlatformResultRow> rows = new List<LegacyPlatformResultRow>();
            if (result != null && result.StepResultList_UI != null)
            {
                int start = result.StepResultList_UI.Count >= resultCountBefore ? resultCountBefore : 0;
                foreach (StepResult_UI item in result.StepResultList_UI.Skip(start))
                {
                    rows.Add(new LegacyPlatformResultRow
                    {
                        StartTime = item.StartTime,
                        StepType = item.StepType ?? string.Empty,
                        StepName = item.StepName ?? string.Empty,
                        Status = item.Status ?? string.Empty,
                        StringValue = item.StringValue ?? string.Empty,
                        MeasuredValue = item.MeasuredValue ?? string.Empty,
                        LimitsLow = item.LimitsLow ?? string.Empty,
                        LimitsHigh = item.LimitsHigh ?? string.Empty,
                        LimitExpression = item.LimitExpression ?? string.Empty,
                        Unit = item.Unit ?? string.Empty,
                        Comment = item.Comment ?? string.Empty
                    });
                }
            }
            return new LegacyStepExecutionResult(rawReturn, result == null ? string.Empty : result.TotalStatus, rows, DateTime.Now);
        }

        private double InvokeDouble(string methodName)
        {
            object value = InvokeTestDll(methodName, new object[0]);
            return Convert.ToDouble(value);
        }

        private double InvokeDoubleWithSocket(string methodName, int socketIndex)
        {
            object value = InvokeTestDll(methodName, new object[] { socketIndex });
            return Convert.ToDouble(value);
        }

        private void InvokeVoid(string methodName, params object[] arguments)
        {
            InvokeTestDll(methodName, arguments);
        }

        private object InvokeTestDll(string methodName, object[] arguments)
        {
            MethodInfo method = _testDllMain.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(_testDllMain.GetType().FullName, methodName);
            return Invoke(method, _testDllMain, arguments);
        }

        private void PerformSafeShutdown(bool throwOnFailure)
        {
            Exception firstError = null;
            try { InvokeVoid("PostUUT", SocketIndex); }
            catch (Exception ex) { firstError = Unwrap(ex); LogMessage("PostUUT安全下电异常：" + firstError.Message); }
            try { InvokeDouble("ProcessCleanup"); }
            catch (Exception ex)
            {
                Exception cleanupError = Unwrap(ex);
                LogMessage("ProcessCleanup断开仪器异常：" + cleanupError.Message);
                if (firstError == null) firstError = cleanupError;
            }
            finally
            {
                _instrumentsInitialized = false;
                _hardwareTouched = false;
            }
            if (throwOnFailure && firstError != null) throw firstError;
        }

        private static object Invoke(MethodInfo method, object target, object[] arguments)
        {
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field == null ? null : field.GetValue(target);
        }

        private void MainClass_UIMessageReceived(int socketIndex, string messageType, object value)
        {
            LogMessage(string.Format("原平台 UI [{0}] {1}: {2}", socketIndex, messageType, FormatValue(value)));
        }

        private void MainClass_EventHapped(int socketIndex, int eventIndex, string message)
        {
            LogMessage(string.Format("原平台事件 [{0}/{1}] {2}", socketIndex, eventIndex, message));
        }

        private void ThrowIfNotReady()
        {
            ThrowIfDisposed();
            if (!_instrumentsInitialized)
                throw new InvalidOperationException("请先点击“初始化全部仪器”。");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LegacySequenceRuntime));
        }

        private void LogMessage(string message)
        {
            Action<string> handler = Log;
            if (handler != null) handler(message);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "<null>";
            string text = Convert.ToString(value);
            return string.IsNullOrWhiteSpace(text) ? value.GetType().Name : text;
        }

        private static Exception Unwrap(Exception exception)
        {
            TargetInvocationException target = exception as TargetInvocationException;
            return target != null && target.InnerException != null ? target.InnerException : exception;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }

    internal sealed class LegacyRunResult
    {
        public LegacyRunResult(string status, bool cancelled, int lastStepIndex)
        {
            Status = status ?? string.Empty;
            Cancelled = cancelled;
            LastStepIndex = lastStepIndex;
        }

        public string Status { get; private set; }
        public bool Cancelled { get; private set; }
        public int LastStepIndex { get; private set; }
    }

    internal sealed class LegacyStepExecutionResult
    {
        public LegacyStepExecutionResult(string rawReturn, string totalStatus, IReadOnlyList<LegacyPlatformResultRow> results, DateTime capturedAt) { RawReturn = rawReturn ?? string.Empty; TotalStatus = totalStatus ?? string.Empty; Results = results ?? new LegacyPlatformResultRow[0]; CapturedAt = capturedAt; }
        public string RawReturn { get; private set; }
        public string TotalStatus { get; private set; }
        public IReadOnlyList<LegacyPlatformResultRow> Results { get; private set; }
        public DateTime CapturedAt { get; private set; }
    }

    internal sealed class LegacyPlatformResultRow
    {
        public DateTime StartTime { get; set; }
        public string StepType { get; set; }
        public string StepName { get; set; }
        public string Status { get; set; }
        public string StringValue { get; set; }
        public string MeasuredValue { get; set; }
        public string Value { get { return string.IsNullOrWhiteSpace(MeasuredValue) ? StringValue : MeasuredValue; } }
        public string LimitsLow { get; set; }
        public string LimitsHigh { get; set; }
        public string LimitExpression { get; set; }
        public string Unit { get; set; }
        public string Comment { get; set; }
    }
}
