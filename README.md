# Codex Voice Key · 按住说话，松开输入

给 Windows 版 Codex 加一个可自定义按键的**本地语音输入**：按住说话，松开后由
**Qwen3-ASR 1.7B** 识别中英文并放进输入框。配有暖白色平滑胶囊和戴博士帽的像素水跃鱼。
也可开启**全局输入**，用于微信、浏览器、文档和其他支持粘贴的输入框。
讲话时胶囊逐步显示识别预览，松开后校正整句并填入；小水跃鱼和音量条随声音起伏。

**不限于 Copilot 键。** 可用 `Ctrl+B`、`F8`、鼠标中键、两个鼠标侧键，或者自己的组合键。
默认是 `Ctrl+Alt+Space`。

![四种语音状态](docs/overlay-states.png)

## 最简单的部署方式

### 直接安装

1. 从 [Releases](https://github.com/SailTechnology/codex-voice-key/releases/latest)
   下载 **codex-voice-key-windows-x64.zip**。
2. 解压，双击 **Install.cmd**。
3. 打开 Codex，按住 `Ctrl+Alt+Space` 讲话，松开等待文字出现。

首次安装自动准备 Handy 和约 **1.5 GB** 的 Qwen 模型；下载完成后识别在本机运行，
不需要 API Key。无需管理员权限、Python、Node.js、Git 或 .NET SDK。
当前提供 **Windows 10/11 x64** 版本，使用系统内置的 .NET Framework。

国内网络可在解压目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1 -UseMirror
```

安装时就选择按键：

```powershell
.\scripts\Install.ps1 -Hotkey 'Ctrl+B'
.\scripts\Install.ps1 -Hotkey 'MouseX1'
.\scripts\Install.ps1 -Hotkey 'Copilot'
.\scripts\Install.ps1 -Hotkey 'Copilot' -AllApplications
```

### 安装为 Codex skill

在 Codex 中直接说：

> 从 https://github.com/SailTechnology/codex-voice-key 安装
> `skills/codex-voice-key` 这个 skill，然后为我部署语音输入，把按键设为鼠标侧键 MouseX1。

也可以把 Release 中 **codex-voice-key-skill.zip** 内的 `codex-voice-key` 文件夹
放到你的 Codex 用户 skills 目录，再调用 **`$codex-voice-key`**。
本项目安装器会同步安装该 skill。Skill 由 `SKILL.md`、UI 元数据和可校验下载的部署脚本组成；
参见 [skill 源码](skills/codex-voice-key/SKILL.md) 和
[OpenAI 官方 skill 文档](https://learn.chatgpt.com/docs/build-skills)。

## 支持哪些按键

| 设置值 | 用法 |
| --- | --- |
| `Ctrl+Alt+Space` | 默认组合键 |
| `Ctrl+B`、`Ctrl+Shift+Space` | 自定义键盘组合 |
| `F8`、`F9`、`F24` | 单个功能键 |
| `MouseMiddle` | 鼠标中键 |
| `MouseX1`、`MouseX2` | 两个鼠标侧键 |
| `Ctrl+MouseX1` | 带修饰键的鼠标按钮 |
| `Copilot` | 识别物理 Copilot 键的 Win+Shift+F23 组合 |

**按住开始，松开结束**。长按重复事件不会反复启动录音。
默认仅在配置的目标应用前台时开始录音。用 `-AllApplications` 安装，或将
`config.json` 中的 `AllApplications` 设为 `true` 并重启，就可以在其他应用中录音和填入文字。
先点击要输入的位置，再按住语音键。全局模式会在所有应用中占用所选语音键。
仅目标应用模式下，普通自定义键在其他应用中保留原本功能；
选择 `Copilot` 时，该硬件组合会在全局保留给语音桥，避免弹出 Copilot。
不要把日常打字键或常用组合设成语音键，除非你确实想在 Codex 中占用它。
鼠标左/右键和单独的 Ctrl、Alt、Shift、Win 不作为语音键。

安装后随时改绑定：

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Set-Hotkey.ps1" -Hotkey 'MouseMiddle'
```

## 使用细节

- 胶囊显示聆听、文字预览、转写、完成和出错状态，位于 **开始录音的应用所在屏幕的任务栏上方居中**。
- 外框、圆角、文字、状态图标平滑绘制；只有角色是像素风，支持显示缩放。
- 聆听时角色轻轻摆动，波形和呼吸环跟随实际麦克风音量；整理文字、完成和重试有不同表情，配有 26 句轮换短句。
- 默认开机登录自启，普通权限运行。安装参数 `-NoStartup` 可关闭创建启动项，
  `-NoStart` 可只安装而不立即运行。
- 使用 Windows 默认麦克风，自动尝试可用采样格式。识别前转换成 16 kHz 单声道。
- 转写完成时，只有原来的窗口仍在前台才会粘贴；切换应用或可检测到的原生输入控件后，文字留在剪贴板。
  程序等待语音组合键的修饰键松开后再粘贴，避免误触其他应用的快捷键。
- **只粘贴，不自动发送**。转写文字会替换剪贴板内容。
- 默认 `MuteOutputWhileRecording: true`：录音前自动静音当前活动的音频输出设备，松开并停止录音后立即恢复，
  无需等待转写结束。保留各设备原本的静音状态，不更改音量数值或麦克风设置。
  视频和音乐的播放进度会继续；自动续播的视频也会保持无声。设为 `false` 可关闭。
  临时守护进程负责恢复，语音桥意外退出也会释放静音；恢复记录保存在本机，启动时检查未完成的恢复。
  升级和卸载会先等待守护进程恢复声音，已断开的设备会保留待恢复记录。
- 默认不保留音频，成功、空结果和失败流程都会清理临时录音；可配置 `KeepRecordings`。
- 默认在按下语音键、开始录音时预加载模型，后续多句语音复用同一个模型，避免每句重复加载。
  **空闲 60 秒自动释放模型和显存**，开机登录时只启动轻量按键程序。下次加载尽量与讲话重叠；
  很短的第一句话仍可能等待加载，首次使用显卡还可能编译计算内核。
- 常驻识别使用 Handy 0.9.8 附带的 transcribe.cpp 0.2.4 和同一份 Qwen 模型，
  在独立进程运行。后台崩溃、超时或库版本不兼容时，同一段录音自动回退到 Handy 命令行。
- 模型保留期间会占用内存和显存；`ModelIdleSeconds` 可设置 1～3600 秒的空闲释放时间，
  设为 0 才会一直保留到退出。可设置 `KeepModelLoaded: false` 恢复逐句加载。
  常驻路径使用模型默认的语言自动检测；如需继承 Handy 的语言或文本处理设置，也可关闭常驻。
  识别率取决于麦克风、噪声与表达，请在自己的设备上试说中文、英文或混合语句。
- `LivePreview: true` 时，模型就绪后约每 2.2 秒尝试刷新一次胶囊里的文字；预览处理最近至多 12 秒的声音，
  显示最近的文字，避免长时间讲话时反复处理全部录音。预览可能随上下文修正，松开后才转写并填入完整录音。
  它与最终识别共用一个模型进程，不写入输入框、不触发 Handy 回退；模型忙时跳过预览。
  首次讲话仍需等待模型加载。可设置 `LivePreview: false` 关闭预览；`KeepModelLoaded: false` 时也不做预览。

## 配置和诊断

默认目录：`%LOCALAPPDATA%\CodexVoiceKey`。主要设置在 `config.json`，修改后重启语音桥。
可参考 [完整配置示例](config.example.json)。

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Doctor.ps1"
```

诊断列出按键、目标进程、麦克风和 Handy 路径，不包含录音或转写内容。
麦克风可设为 `default`、诊断列出的设备编号、或者名称中的一部分。
若 Codex 的进程名不同，在 `TargetProcesses` 中添加对应的可执行文件名（不带 `.exe`）。
`TaskbarGap` 控制胶囊和任务栏的距离，默认 12 个逻辑像素。
`PlaySounds: false` 关闭开始和结束提示音，界面状态继续显示。
日志中的 `resident model ready` 表示后台模型已就绪，`elapsed_ms` 是该次常驻转写耗时。
后台模型就绪时会有两个 `CodexVoiceKey.exe` 进程：按键程序和不接触麦克风/剪贴板的识别进程。
开启录音静音时，讲话期间还会有一个临时音频守护进程，停止录音后退出；它不加载识别模型。
错误提示会区分录音处理、语音识别和文字填入；日志保留出错阶段及调用栈，便于定位。
1.1.0 修复了旧版长录音在采样率转换时出现的整数溢出；若日志出现 `startIndex`
越界且短句正常、长句失败，请升级。音频转换已覆盖 16/44.1/48 kHz 的 20 秒和 120 秒录音测试。

若没有文字：先确认 Windows 允许桌面应用使用麦克风，检查 `voice-bridge.log`，
并确保 Codex 和语音桥使用相同权限级别。可用以下命令单独检查识别后端：

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\CodexVoiceKey.exe" --transcribe-file '你的测试录音.wav'
```

此命令只转写文件，不录音、不粘贴。可附加 `--config '配置路径'`。
`--overlay-preview` 可以只预览界面，不录音、不安装键盘钩子。

## 升级与卸载

新版本重新运行安装器即可，现有按键和配置会保留，并备份上一版配置与程序。

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Uninstall.ps1"
```

卸载只删除安装清单内的程序文件和本程序的启动项。
配置、日志、skill、Handy 和共享模型保留，方便重装且不影响其他使用方。

## 从源码构建与验证

```powershell
.\scripts\build.ps1
.\scripts\Test.ps1
.\scripts\Package.ps1
```

只使用 Windows 自带的 C# 编译器。测试覆盖按键与修饰键事件序列、鼠标按钮、
长按、Win64 输入结构、Unicode JSON、音频转换、后台进程复用/异常/超时清理，以及隔离目录中的安装、配置、
升级、诊断和卸载。测试不会向桌面注入按键、录制麦克风或修改真实启动项。
静音自动检查使用模拟输出设备，覆盖原状态保留、新设备、部分失败和恢复记录，不改变测试机的实际声音。
输出静音使用 [Windows Core Audio 的 SetMute 接口](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute)。
这类检查不能代替用户在自己电脑上的麦克风和硬件 Copilot 按键实测。

代码及 skill 使用 MIT 许可；水跃鱼形象是非官方同人素材，不包含在代码 MIT 授权中。
Handy、模型和角色信息见 [第三方说明](THIRD_PARTY_NOTICES.md)。

---

**English:** A Windows x64 push-to-talk companion for Codex. Hold a configurable
keyboard shortcut or mouse button, release to transcribe locally using Handy and
Qwen3-ASR, then paste without submitting. Download the release ZIP and run
`Install.cmd`, or install `skills/codex-voice-key` as a Codex skill. No API key,
administrator access, Python, Git or .NET SDK is needed on the target machine.
