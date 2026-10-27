# subtitle-mask

一款字幕遮挡神器

Windows 桌面字幕遮挡工具：把置顶遮挡条放到视频字幕上，按 **C** 在遮挡与显示之间切换，适合外语听力练习。

## 下载与使用

[下载 SubtitleMask.exe](https://github.com/onpong/subtitle-mask/raw/refs/heads/main/SubtitleMask.exe)，双击运行，无需安装程序。需要 Windows 和 .NET Framework 4.x。

1. 拖动黑色遮挡条，将其放到字幕区域；拖动边缘或角落调整大小。
2. 按 **C** 显示字幕，再按 **C** 遮挡字幕。
3. 勾选“锁定位置”，鼠标可以穿透遮挡条，操作下方播放器。
4. 按 **Ctrl + Alt + C** 暂停或恢复全局 C 热键。打字前请先暂停，否则 C 会被工具占用。
5. 可以将控制面板收起到系统托盘；关闭控制面板或选择托盘菜单中的“退出”即可退出程序。

支持自定义遮挡颜色、输入宽高、重置位置，并自动记住位置、尺寸和锁定状态。完整操作说明见 [使用说明](使用说明.md)。

## 适用范围

工具通过实色置顶窗口遮住字幕所在的画面区域，不会修改视频或字幕轨道。适用于普通窗口和无边框全屏。独占全屏、受保护视频或特殊渲染模式可能无法覆盖，请尝试窗口化或无边框全屏。

## 构建

完整 C# WinForms 源码位于 `SubtitleMask.cs`。`build.ps1` 使用 Windows 的 .NET Framework 编译器，不需要安装 .NET SDK。修改源码后，先退出程序，再在项目目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建结果为项目目录下的 `SubtitleMask.exe`。

## 自检

程序内置 10 项行为检查，覆盖遮挡切换、热键消息处理、暂停恢复、尺寸、屏幕范围恢复和设置持久化，同时单独报告系统 C 热键是否注册成功。测试会短暂创建程序窗口，需要 Windows 桌面环境。

```powershell
$testOutput = Join-Path $PWD 'test-output'
Start-Process -FilePath .\SubtitleMask.exe -ArgumentList @('--self-test', $testOutput) -Wait
Get-Content (Join-Path $testOutput 'test-result.txt')
```

输出包括测试记录和界面预览图；测试前请退出正常运行的实例。这些检查不代表已验证所有播放器或独占全屏模式。
