# FCT 平台与调试工具统一工程

本目录是唯一维护根目录。

## 工程位置

- 平台 MainTest：`E:\FST\TestDLL\TestDLL`
- WPF 调试与 SEQ 编辑工具：`E:\FST\TestDLL\ManualCanDebug`
- MainTest 调试运行工程：`E:\FST\TestDLL\CSP.TestDLL.Extension`
- 公共运行 DLL：`E:\FST\TestDLL\DLLs`
- 仪器和产品配置：`E:\FST\TestDLL\Config`
- 平台 SEQ：`E:\FST\TestDLL\TestDLL\bin\Sequence`
- AN23600E 电子负载驱动：`E:\FST\TestDLL\Instruments.Load.AN23600E`

## 当前仪器对象

- `LVDC`：KL30 低压电源（保留原名称以兼容旧 SEQ）
- `LVDC_KL15`：KL15 独立低压电源
- `RELAY_FCT`：FCT 功能继电器板，底层为 `SHT_48SEDO_A`
- `RELAY_HVMUX`：高压切换继电器板，底层为 `SHT_48SEDO_A`
- `DCDC_LOAD`：AN23600E DCDC 电子负载
- `DAQ`：支持 `NI9227` 原生电流输入和旧 `PCI6229` 兼容模式

## 待现场填写的配置

`Config\InstrumentConfig.json` 中以下 Resource 故意留空并默认不初始化，避免误动作：

- KL15 电源 VISA Resource
- 两块 SHT 继电器卡的 IP；Parameter 格式为 `端口,从站地址`
- AN23600E 的 LAN IP；Parameter 为端口，默认 `2101`
- NI-9227 在 NI MAX 中的实际机箱/槽位名称，以及每个互感器通道的倍率

NI-9227 STEP 中：`PhysicalChannel` 是 NI MAX 物理通道，`Ratio` 是一次侧/二次侧倍率。例如 5000:1 填 `5000`，2000:1 填 `2000`。

## 安全规则

- 初始化电子负载后默认执行 `LoadOff`。
- 清理和安全下电会先关闭电子负载。
- 两块 SHT 板清理时会尝试将 48 路输出全部写为断开。
- 未填写 Resource 的仪器不要勾选初始化。
