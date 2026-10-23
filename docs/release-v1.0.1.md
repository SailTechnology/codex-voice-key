Windows 本地语音输入与 Codex skill。

- 支持自定义键盘快捷键、功能键、鼠标中键/侧键和 Copilot 键。
- 本地 Qwen3-ASR 识别中英混合语音，按住录音、松开转写，仅粘贴而不发送。
- 白色平滑胶囊和四状态像素角色，任务栏上方居中。
- 安装、登录自启、改键、诊断、保留配置升级及卸载。

此补丁修复了全新 Windows 用户没有旧启动项时，卸载器读取缺失注册表值而报错的问题。

下载 **codex-voice-key-windows-x64.zip**，解压运行 **Install.cmd**。
独立 skill 包是 **codex-voice-key-skill.zip**；也可从仓库安装 `skills/codex-voice-key`。
两个 ZIP 的校验值见 **SHA256SUMS.txt**。

无需 API Key 或管理员权限，首次部署另行下载约 1.5 GB 模型。
已验证：Windows PowerShell 5.1 的构建和隔离部署、75 项输入及音频检查、
skill 格式校验以及本地中英混合录音转写。物理按键和麦克风仍需在各自电脑上试用。

代码及 skill 使用 MIT 许可；角色和外部组件的许可见 THIRD_PARTY_NOTICES.md。
