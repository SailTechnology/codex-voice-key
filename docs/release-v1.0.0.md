Windows x64 本地语音输入与 Codex skill 首个公开版本。

- 按住自定义按键录音，松开后用本地 Qwen3-ASR 转写并粘贴，不自动发送。
- 支持键盘组合、功能键、鼠标中键/侧键和物理 Copilot 键。
- 暖白平滑胶囊、四状态像素水跃鱼，任务栏上方居中，支持显示缩放。
- 一键安装、登录自启、改键、诊断、配置保留升级和卸载。
- 附带可安装的 Codex skill，模型单独下载，不需要 API Key。

下载 `codex-voice-key-windows-x64.zip`，解压后运行 `Install.cmd`。
只安装 skill 时下载 `codex-voice-key-skill.zip`，或从仓库安装 `skills/codex-voice-key`。
`SHA256SUMS.txt` 提供两个 ZIP 的校验值。

验证：Windows PowerShell 5.1 的源码构建与隔离安装/配置/升级/卸载、75 项输入及音频检查、
skill 格式校验，以及本地中英混合录音文件转写。不同电脑的麦克风和物理 Copilot 键仍需实际试用。
首次部署会下载约 1.5 GB 模型；识别完成的速度取决于 CPU 和音频长度。

代码和 skill 使用 MIT 许可。角色素材及外部组件请见 THIRD_PARTY_NOTICES.md。
