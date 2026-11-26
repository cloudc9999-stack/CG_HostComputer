# 储供系统控制器

Windows CAN 上位机，用于储供系统连接、帧收发、传感器数据显示、气压绘图、自锁阀、PTC 和电磁阀控制。

## 当前版本

V5.2

主要功能：

- 创芯科技 USB-CAN 连接，支持 ControlCAN.dll。
- 储供连接、SN 核验和 2 秒心跳。
- 主气压、四路支路气压、四路 PT1000 温度显示。
- 五路独立气压绘图及坐标框选放大。
- 自锁阀、四路 PTC、四路电磁阀控制。
- 手动/自动模式、占空比、气压目标值下发。
- 状态上报和可筛选应答日志。
- 深色/浅色主题、停靠布局和独立浮动窗口。

## 目录

- `work/CanHostApp`：WinForms 主程序。
- `work/CanHost.VciBridge`：32 位 ControlCAN 桥接程序。
- `work/CanHostTests`：协议与界面回归测试。

## 构建

需要 Windows x64 和 .NET 8 SDK：

```powershell
dotnet build .\work\CanHostTests\CanHostTests.csproj -c Release
dotnet run --project .\work\CanHostTests\CanHostTests.csproj -c Release -- --render
```

发布：

```powershell
dotnet publish .\work\CanHostApp\CanHost.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=false -o .\release
```

运行时需要将 `ControlCAN.dll` 和 `CanHost.VciBridge.exe` 放在程序目录中。

