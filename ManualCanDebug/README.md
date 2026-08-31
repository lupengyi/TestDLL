# FCT 手动 CAN Debug 工具

这是一个独立的 `.NET Framework 4.8`、`x86` WPF 工程，只使用两个 ZLG CAN 通道：

- 产品 CAN：设备类型 `48`，通道 `0`，波特率 `500000`
- 旋变 CAN：设备类型 `48`，通道 `1`，波特率 `500000`

默认 IP 和端口沿用原程序：`192.168.0.127:8000`。

界面中的“按 SEQ 顺序手动执行”按照 `FT1-FCT01-01-A0.json` 中的 CAN 步骤固化，包括：

- 进入 FT 模式
- DUT 通信初始化
- CAN 通信测试
- 旋变 700/3500/7000 RPM
- 旋变位置 225/315
- DUT 电流设置 0/100/200/.../900A

产品 CAN 页还提供原始帧收发、DUT 内存参数读取和产品 DBC 信号发送；旋变 CAN 页提供旋变 DBC 信号发送。

工程不引用 `CSP`，也不加载电源、DMM、MOXA、继电器、PLC、DAQ 等其他仪器。

## 打开和运行

用 Visual Studio 打开 `ManualCanDebug.sln`，平台选择 `x86`，编译后运行：

`ManualCanDebug\bin\Debug\ManualCanDebug.exe`

CAN 运行库和 DBC 文件会由工程自动复制到输出目录。连接设备前先确认 ZLG 网口配置和设备 IP 与现场实际一致；如果现场参数不同，直接修改 `ManualCanDebug.Core\CanChannelConfig.cs` 中的默认值。

## 修改协议

主要代码位置：

- 产品 CAN / 旋变 CAN 通信：`ManualCanDebug.Core\CanDebugService.cs`
- 固定 CAN 帧：`ManualCanDebug.Core\CanProtocol.cs`
- SEQ 顺序和默认值：`ManualCanDebug.Core\CanSequenceCatalog.cs`
- WPF 界面：`ManualCanDebug\MainWindow.xaml`
- 按钮事件：`ManualCanDebug\MainWindow.xaml.cs`
