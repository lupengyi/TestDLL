# 出流前产品状态读取引导 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在执行 `Set DUT Current 0 A` 前显示产品状态读取页面，同时在日志中保留原始 CAN 报文和解析后的具体值，由操作者决定是否继续发送0A。

**Architecture:** 在 `ManualCanDebug.Core` 中增加与 UI 无关的读取项目定义、读取结果和逐项读取器，`CanDebugService` 复用现有 `ReadDutValue` 完成真实 CAN 读取并记录解析值。WPF 项目新增代码式弹窗；`MainWindow` 只在0A步骤上先显示弹窗，确认后再调用现有 `SetDutCurrent`。

**Tech Stack:** C#、WPF、.NET Framework 4.8、x86、现有无测试框架的控制台测试程序。

## Global Constraints

- 不使用 Git，不执行提交、重置或检出操作。
- 不判断读取值是否合格，不显示 PASS/FAIL，不根据数值阻止继续。
- 不读取水流、冷却液入口温度或冷却液出口温度。
- 页面显示解析后的具体值；日志同时保留原始 `Product TX/RX` CAN 报文和解析值。
- 保持 .NET Framework 4.8 和 x86。
- 只有 `Set DUT Current 0 A` 插入读取引导；其他电流步骤保持现有行为。

---

### Task 1: Core 读取项目、结果和逐项容错读取器

**Files:**
- Create: `ManualCanDebug.Core/PreCurrentReadItem.cs`
- Create: `ManualCanDebug.Core/PreCurrentReadResult.cs`
- Create: `ManualCanDebug.Core/PreCurrentStatusReader.cs`
- Modify: `ManualCanDebug.Core/ManualCanDebug.Core.csproj`
- Test: `ManualCanDebug.Tests/Program.cs`

**Interfaces:**
- Produces: `PreCurrentReadItem(string name, uint addressOffset, int tableIndex, int dataSize, string unit)`。
- Produces: `PreCurrentReadResult.Success(PreCurrentReadItem item, double value)` 和 `PreCurrentReadResult.Failure(PreCurrentReadItem item, string error)`。
- Produces: `PreCurrentStatusReader.ReadAll(Func<uint,int,int,double> readValue)`，固定顺序返回六个结果。

- [ ] **Step 1: 写读取目录失败测试**

在 `Program.Main` 注册 `Pre-current catalog uses SEQ addresses`，断言六项顺序及参数为：

```csharp
new PreCurrentReadItem("产品母线高压", 0, 184, 4, "V")
new PreCurrentReadItem("Battery 电压", 0, 128, 4, "V")
new PreCurrentReadItem("PSR 电压", 0, 84, 4, "V")
new PreCurrentReadItem("HVDC_OV_FLT", 24, 1, 1, "")
new PreCurrentReadItem("OV_FLT", 24, 19, 1, "")
new PreCurrentReadItem("产品板温", 0, 68, 4, "℃")
```

- [ ] **Step 2: 写单项失败仍继续读取的失败测试**

使用委托记录调用顺序，在 `TableIndex == 128` 时抛出 `InvalidOperationException("read failed")`；断言返回六项、第二项失败、其余项成功，且委托被调用六次。

- [ ] **Step 3: 运行测试确认 RED**

Run:

```powershell
& 'D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' '.\ManualCanDebug.Tests\ManualCanDebug.Tests.csproj' /t:Rebuild /p:Configuration=Debug /nologo /v:minimal
```

Expected: 编译失败，提示 `PreCurrentReadItem` 或 `PreCurrentStatusReader` 不存在。

- [ ] **Step 4: 实现最小 Core 类型**

`PreCurrentStatusReader.ReadAll` 遍历只读目录，对每项单独 `try/catch`：

```csharp
foreach (PreCurrentReadItem item in Items)
{
    try
    {
        results.Add(PreCurrentReadResult.Success(item,
            readValue(item.AddressOffset, item.TableIndex, item.DataSize)));
    }
    catch (Exception ex)
    {
        results.Add(PreCurrentReadResult.Failure(item, ex.Message));
    }
}
```

将三个新文件加入旧式 `ManualCanDebug.Core.csproj` 的 `<Compile Include>` 列表。

- [ ] **Step 5: 运行测试确认 GREEN**

Run build command, then:

```powershell
& '.\ManualCanDebug.Tests\bin\Debug\ManualCanDebug.Tests.exe'
```

Expected: 新增测试及原有18项断言全部通过。

---

### Task 2: 将真实产品读取接入 CanDebugService 并记录解析值

**Files:**
- Modify: `ManualCanDebug.Core/CanDebugService.cs`
- Test: `ManualCanDebug.Tests/Program.cs`

**Interfaces:**
- Consumes: `PreCurrentStatusReader.ReadAll(Func<uint,int,int,double>)`。
- Produces: `public IReadOnlyList<PreCurrentReadResult> ReadPreCurrentStatus()`。

- [ ] **Step 1: 写解析值格式失败测试**

增加 `PreCurrentReadResult.FormatValue()` 的断言：电压和温度采用不丢失有效小数的十进制格式并附单位，故障位显示整数，失败项显示 `读取失败：<原因>`。

- [ ] **Step 2: 运行测试确认 RED**

Expected: 编译失败，提示 `FormatValue` 不存在。

- [ ] **Step 3: 实现格式化与服务方法**

`ReadPreCurrentStatus` 调用读取器并逐条写解析日志：

```csharp
IReadOnlyList<PreCurrentReadResult> results =
    PreCurrentStatusReader.ReadAll(ReadDutValue);
foreach (PreCurrentReadResult result in results)
{
    WriteLog("读取" + result.Item.Name + "：" + result.FormatValue());
}
return results;
```

底层 `ReadDutValue` 不改动，以继续输出已有 `Product TX` 和 `Product RX` 日志。

- [ ] **Step 4: 运行测试确认 GREEN**

Run full test executable.

Expected: 全部测试通过，格式值不含 CAN 地址或原始字节。

---

### Task 3: WPF 出流前状态页面与0A执行接入

**Files:**
- Create: `ManualCanDebug.Core/CanSequenceRules.cs`
- Create: `ManualCanDebug/PreCurrentStatusWindow.cs`
- Modify: `ManualCanDebug.Core/ManualCanDebug.Core.csproj`
- Modify: `ManualCanDebug/MainWindow.xaml.cs`
- Test: `ManualCanDebug.Tests/Program.cs`

**Interfaces:**
- Consumes: `Func<IReadOnlyList<PreCurrentReadResult>> readStatuses`。
- Produces: `PreCurrentStatusWindow.Confirmed`，仅“确认并发送0A”设为 `true`。
- Produces: `CanSequenceRules.RequiresPreCurrentGuide(CanSequenceStep step)`，只有 `CAN_SetDUTCurrent` 且值为 `0.01` 时返回 `true`。

- [ ] **Step 1: 写仅0A触发引导的失败测试**

断言：

```csharp
Assert(CanSequenceRules.RequiresPreCurrentGuide(zeroCurrentStep));
Assert(!CanSequenceRules.RequiresPreCurrentGuide(oneHundredAmpStep));
Assert(!CanSequenceRules.RequiresPreCurrentGuide(communicationStep));
```

- [ ] **Step 2: 运行测试确认 RED**

Expected: 编译失败，提示 `CanSequenceRules` 不存在。

- [ ] **Step 3: 实现规则并接入 MainWindow**

将 `CanSequenceRules.cs` 加入旧式 `ManualCanDebug.Core.csproj` 的 `<Compile Include>` 列表。

在 `ExecuteSequence_Click` 获取页面参数后：

```csharp
if (CanSequenceRules.RequiresPreCurrentGuide(step))
{
    var dialog = new PreCurrentStatusWindow(_service.ReadPreCurrentStatus)
    {
        Owner = this
    };
    dialog.ShowDialog();
    if (!dialog.Confirmed) return;
}
```

确认后继续调用原有 `RunActionAsync`；取消或关闭立即返回，不发送0A。

- [ ] **Step 4: 实现代码式 WPF 弹窗**

窗口使用 `DataGrid` 显示四列：项目、读取值、单位、读取状态。加载和“重新读取”通过 `Task.Run(readStatuses)` 执行，等待期间禁用三个按钮；每次完成后刷新 `ItemsSource`。

按钮：

- `重新读取`：刷新六项值。
- `确认并发送0A`：`Confirmed=true; DialogResult=true; Close()`。
- `取消`：`Confirmed=false; DialogResult=false; Close()`。

页面不创建上下限列，不着色，不出现 PASS/FAIL。

- [ ] **Step 5: 运行测试与 WPF 编译确认 GREEN**

Run:

```powershell
& 'D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' '.\ManualCanDebug.sln' /t:Rebuild /p:Configuration=Debug /p:Platform=x86 /nologo /v:minimal
& '.\ManualCanDebug.Tests\bin\Debug\ManualCanDebug.Tests.exe'
```

Expected: 解决方案编译成功，全部测试通过。

---

### Task 4: 实机读取、日志和发送门控验证

**Files:**
- Verify: `ManualCanDebug/bin/Debug/ManualCanDebug.exe`

**Interfaces:**
- Consumes: 产品 CAN `TX=0x7EE`、`RX=0x7EF`，设备52，通道2，IP `192.166.6.10`。
- Produces: 页面六项解析值、日志原始 TX/RX 和解析值，以及确认/取消后的正确发送行为。

- [ ] **Step 1: 运行最终完整编译和测试**

执行 Task 3 的完整命令，确认退出码为0且测试无失败。

- [ ] **Step 2: 使用编译产物连接产品并初始化 DUT**

在工具中连接产品 CAN，执行 `DUT Communication Init`，确认收到 `0x7EF: 48 A6 16 80` 并产生非零 first address。

- [ ] **Step 3: 打开0A引导并验证读取展示**

选择 `Set DUT Current 0 A` 并执行，确认页面显示六项具体值；日志对每项同时包含 `Product TX/RX` 和 `读取<项目>：<值>`。

- [ ] **Step 4: 验证取消不发送0A**

点击取消，确认窗口关闭后日志中没有 `DUT current command sent`。

- [ ] **Step 5: 验证确认发送0A**

再次打开并读取，点击“确认并发送0A”，确认窗口关闭且日志出现现有0A发送帧和 `DUT current command sent`。
