---
name: codex-voice-key
description: Install, configure, diagnose, or uninstall local push-to-talk voice input for Codex on Windows, with a custom keyboard shortcut, mouse button, or Copilot key and a small animated capsule. Use for deploying this bridge or changing its bindings, microphone, startup behavior, or display position.
---

# Codex Voice Key

Deploy a Windows x64 companion that records while a configured key is held, transcribes locally with Handy and Qwen3-ASR, and pastes into the same Codex window on release. It does not send Enter. The capsule is white and smooth; its pixel mascot has four expressions.

## Install

The helper [scripts/setup.ps1](scripts/setup.ps1) downloads the public release, checks SHA-256, and runs the installer. Run it with Windows PowerShell or PowerShell 7; no administrator privileges, Python, Node.js, Git, or .NET SDK are required on the target machine.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<skill-directory>\scripts\setup.ps1" -Hotkey "Ctrl+Alt+Space"
```

Use the user's requested binding. Otherwise preserve an existing binding, or use the new-install default `Ctrl+Alt+Space`. Examples: `Ctrl+B`, `F8`, `MouseMiddle`, `MouseX1`, `MouseX2`, `Ctrl+MouseX1`, `Copilot`. Do not bind a plain modifier, left click, or right click. `Copilot` selects special handling for the Windows+Shift+F23 hardware chord; ordinary custom bindings leave that chord alone.

First installation downloads Handy 0.9.8 and a roughly 1.5 GB model. `-UseMirror` chooses hf-mirror.com for the model download. Existing compatible Handy/model files are reused. Installation enables login startup and starts the bridge; `-NoStartup` and `-NoStart` disable those steps when requested. Microphone permission may need enabling in Windows settings.

Use this skill for Windows. The bridge is not a macOS/Linux build and is not a cloud ASR service.

## Configure an existing installation

The default install folder is `%LOCALAPPDATA%\CodexVoiceKey`.

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Set-Hotkey.ps1" -Hotkey "MouseX1"
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Doctor.ps1"
```

`config.json` contains `Hotkey`, `TargetProcesses`, `HandyPath`, `ModelId`, `Microphone`, `PlaySounds`, `KeepRecordings`, `MaxRecordingSeconds`, `TranscriptionTimeoutSeconds`, and `TaskbarGap`. Change only the requested settings. For microphone selection, use `default`, a device-name substring, or the device index listed by the doctor. Restart the bridge after manual edits. `TaskbarGap` is in logical pixels; the capsule centers above the taskbar on Codex's display.

For a custom installation location, pass `-InstallDirectory` to the management scripts. The executable also accepts `--config <path>`.

## Diagnose

Run `Doctor.ps1` first. Check the configured binding, target process name, microphone list, backend path and bridge log. A successful process start or visual preview does not prove microphone recognition. If testing speech is needed, use a user-provided recording or ask them to hold the chosen key and speak in Codex. Keep recordings and transcript content local.

`--overlay-preview` shows all four states without recording or keyboard hooks. `--transcribe-file <wav>` tests the installed backend without pasting text. Recognition runs locally after downloads; clipboard content is replaced with the transcript. If focus changes, text stays on the clipboard instead of being pasted into a different window.

## Uninstall

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Uninstall.ps1"
```

The uninstaller removes owned app files and its startup entry. It preserves configuration, logs, the reusable skill, Handy and shared model files. Do not remove shared dependencies or user recordings unless that cleanup is explicitly requested.

When finishing, report the install path, chosen key and startup state, and distinguish completed checks from any speech test still needed. Source and release documentation: https://github.com/SailTechnology/codex-voice-key
