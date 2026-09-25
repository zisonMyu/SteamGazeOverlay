# Steam Gaze

SteamVR 合并眼动显示与 Desktop+ → Windows 坐标桥接，当前为实验版。它以独立 Overlay 运行，复用已有 Desktop+ 的桌面捕获与显示。

## 能做什么

- 显示原始、平滑或两种视线圆环，调整颜色、透明度、角大小、平滑和默认距离。
- 将视线与 Desktop+ 虚拟桌面求交，换算为 Windows 物理像素，处理旋转、缩放、曲率、裁剪、负坐标和显示器间隙。
- 提供 SteamVR Dashboard、中文设置、托盘、可选自启动、桌面预览和 30 秒验收靶板。
- 通过当前用户专用命名管道向其他程序提供视线及桌面坐标；不移动鼠标或自动点击。

## 使用

需要 Windows x64、.NET Framework 4.8、支持新版 OpenVR 眼动 API 的 SteamVR，以及已配置的眼动提供方。目前只在 PSVR2 + PSVR2Toolkit 的一套环境验证过。Windows 映射另外需要 Desktop+。

1. 将发布包解压到可写目录，启动 SteamVR、眼动提供方和 Desktop+。
2. 佩戴头显，运行 `SteamGazeOverlay.exe`。青色为默认处理后视线，橙色为原始视线。
3. SteamVR Dashboard → Steam Gaze 可调节圆环；桌面设置提供完整选项。
4. Windows 设置的“虚拟桌面映射”页点击“打开 Desktop+ 并验收（30 秒）”，观察白色十字和实际注视环。
5. **验收时留在 Desktop+ 标签页。** 如果桌面设置为仅在此标签页显示，切到 Steam Gaze 后桌面会隐藏，程序也就不会输出坐标。

最小化设置窗口会进入托盘；再次启动 EXE 或双击托盘恢复设置。关闭设置窗口会退出。也可运行 `--tray`、`--verify-mapping`、`--stop`。

默认桌面目标是 `elvissteinjr.DesktopPlus0`，来源为完整桌面纹理。目标编号不等于显示器编号，增删、重排 Desktop+ Overlay 后应检查选中的目标。独立单显示器纹理需要手动指定显示器；任意窗口、网页或同尺寸图片不能冒充桌面来源。

## 验收与限制

`data/mapping-check.json` 保存 30 秒观测次数和最后命中坐标。收到坐标只说明链路有输出，不能自动判定人眼精度。固定看十字，不要追逐圆环；先检查中心和四角，再移动、缩放和旋转虚拟桌面。

当前有 22 项离线测试、40 组实际 SteamVR 测试表面求交检查，以及一套 PSVR2 环境的基本可用反馈。量化精度、广泛游戏性能和其他头显兼容性没有完成验证。

平滑会引入延迟，不能修复校准误差。本程序不校准或安装头显驱动，不获取游戏深度；未命中桌面时使用固定显示距离。时间戳和轮询频率来自主机，不能当作传感器采样时间或端到端延迟。

曾观察到 Desktop+ 在显示器拓扑变化后创建共享纹理失败并退出；其最终根因尚未复现。保持被捕获显示器连接稳定，再从验收入口启动/打开 Desktop+；恢复运行不等于上游缺陷已经修复。

## 数据、构建与扩展

设置、当前状态和诊断日志位于程序目录 `data`。不默认录制完整眼动历史；手动验收会保存摘要。默认启用当前 Windows 用户可访问的本机管道 `\\.\pipe\SteamGazeOverlay.v1`，无网络监听和云端遥测，可在“启动与扩展”关闭。

`build.ps1` 在 Windows 上调用 C# 编译器，输出到 `dist` 并执行离线测试。`package.ps1` 生成仅含公开运行文件的 ZIP，不包含用户配置、日志和符号。扩展协议见 [EXTENSIONS.md](EXTENSIONS.md)，第三方来源见 [THIRD-PARTY.md](THIRD-PARTY.md)。

`bridges/steamgaze_hotscreen_bridge.py` 可将有效的 Desktop+ 桌面命中转换成 Hotscreen mod 使用的 `UDP 127.0.0.1:7779` 两个 little-endian float32 坐标；视线无效、数据过期或未命中桌面时发送 `(-1, -1)`。它只使用 Python 标准库，启动和多屏配置见 [`bridges/README.md`](bridges/README.md)。

眼动 Overlay 及桌面注视交互已有相关项目；本项目的侧重点是将全局注视显示、Desktop+ 桌面坐标和本机数据接口整合起来，不宣称首创。
