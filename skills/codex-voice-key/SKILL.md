---
name: codex-voice-key
description: Install, configure, diagnose, or uninstall local push-to-talk voice input for Codex or other Windows apps, with a custom shortcut, mouse button, or Copilot key, live text previews and an animated capsule. Use for deploying this bridge or changing its application scope, bindings, microphone, startup behavior, or display position.
---

# Codex Voice Key

Deploy a Windows x64 companion that records while a configured key is held, transcribes locally with Handy and Qwen3-ASR, and pastes into the original foreground window on release. It does not send Enter. The white, smooth capsule shows live previews and playful captions; its pixel mascot and level meter respond to microphone volume.

## Install

The helper [scripts/setup.ps1](scripts/setup.ps1) downloads the public release, checks SHA-256, and runs the installer. Run it with Windows PowerShell or PowerShell 7; no administrator privileges, Python, Node.js, Git, or .NET SDK are required on the target machine.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<skill-directory>\scripts\setup.ps1" -Hotkey "Ctrl+Alt+Space"
```

Use the user's requested binding. Otherwise preserve an existing binding, or use the new-install default `Ctrl+Alt+Space`. Examples: `Ctrl+B`, `F8`, `MouseMiddle`, `MouseX1`, `MouseX2`, `Ctrl+MouseX1`, `Copilot`. Do not bind a plain modifier, left click, or right click. `Copilot` selects special handling for the Windows+Shift+F23 hardware chord; ordinary custom bindings leave that chord alone.

When the user requests WeChat, browsers, other apps or all input boxes, pass `-AllApplications` to setup/install, or set `AllApplications: true` in the existing configuration and restart. Otherwise preserve the current scope; new installs default to the `TargetProcesses` list. Global mode reserves the chosen voice shortcut in every app. The user should focus the intended input box before speaking.

First installation downloads Handy 0.9.8 and a roughly 1.5 GB model. `-UseMirror` chooses hf-mirror.com for the model download. Existing compatible Handy/model files are reused. Installation enables login startup and starts the bridge; `-NoStartup` and `-NoStart` disable those steps when requested. Microphone permission may need enabling in Windows settings.

When setup runs through a packaged app with redirected AppData access, the installer uses a temporary normal-user scheduled task to install outside that file view. It reuses the existing backend/model, preserves configuration and backs up the previous private installation after success. The temporary task is removed. Login startup uses a normal-user logon task with explicit executable/config paths; a Startup shortcut is the fallback if task registration is unavailable. No model loads at login.

Use this skill for Windows. The bridge is not a macOS/Linux build and is not a cloud ASR service.

## Configure an existing installation

The default install folder is `%LOCALAPPDATA%\CodexVoiceKey`.

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Set-Hotkey.ps1" -Hotkey "MouseX1"
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Doctor.ps1"
```

`config.json` contains `Hotkey`, `TargetProcesses`, `AllApplications`, `HandyPath`, `ModelId`, `Microphone`, `PlaySounds`, `KeepModelLoaded`, `ModelIdleSeconds`, `LivePreview`, `MuteOutputWhileRecording`, `KeepRecordings`, `MaxRecordingSeconds`, `TranscriptionTimeoutSeconds`, and `TaskbarGap`. Change only the requested settings. For microphone selection, use `default`, a device-name substring, or the device index listed by the doctor. Restart the bridge after manual edits. `TaskbarGap` is in logical pixels; the capsule centers above the taskbar on the recording application's display.

By default the model loads when recording begins, stays available between utterances, and releases its worker and GPU memory after 60 idle seconds. Login starts only the lightweight key bridge. `ModelIdleSeconds` accepts 1–3600; 0 keeps the model until exit. `KeepModelLoaded: false` restores one-shot Handy CLI inference. Warm mode uses Handy 0.9.8's transcribe.cpp 0.2.4 library and automatic language detection; incompatible libraries, crashes and timeouts fall back to Handy for the same recording. Use the one-shot path when Handy-specific language or text-processing preferences must apply.

`LivePreview: true` attempts a capsule-only preview about every 2.2 seconds once the model is ready, using at most the latest 12 seconds of audio. It shows recent tentative text; release performs full-recording recognition and pastes once. Previews reuse the same worker, skip a busy model, and never start a CLI fallback. Disable `LivePreview` to reduce inference work; previews are also off when `KeepModelLoaded` is false. Do not describe this as instantaneous token streaming or claim speech validation from rendering alone.

`MuteOutputWhileRecording: true` silences active playback outputs before opening the microphone and restores each device's previous mute state as soon as recording stops, before transcription. Volume levels and microphone settings are preserved. Media playback continues silently, including autoplay; this does not pause a video's timeline. Set the option to false when requested. A temporary `--audio-guard` process owns restoration and watches its parent; `audio-output-restore.json` records pending restoration locally. Startup recovers saved states, and management scripts wait for the guard during shutdown. Do not force-kill an audio guard during an upgrade. A disconnected device may remain in the journal for a later recovery.

For a custom installation location, pass `-InstallDirectory` to the management scripts. The executable also accepts `--config <path>`.

## Diagnose

Run `Doctor.ps1` first. Check the configured binding, target process name, microphone list, backend path and bridge log. A successful process start or visual preview does not prove microphone recognition. If testing speech is needed, use a user-provided recording or ask them to hold the chosen key and speak in Codex. Keep recordings and transcript content local.

For a shortcut that stops working after reboot, compare `installationContext.physicalConfig`, `physicalExecutable`, `shadowedInstallation`, the startup action and the running process. MSIX child processes can see redirected AppData files even without package identity. A program can appear installed inside Codex while login sees no config/model and an older build silently uses the default shortcut. Upgrade with v1.3.1 or later through `Install.ps1`; do not fix only the visible shortcut or assume a new diagnose process reports the settings used by an existing process. Check the startup log for the actual version, executable, config path and binding. Verify from a normal Windows process as well as Codex; do not reboot the user's computer automatically.

Version 1.1.0 fixes an audio-resampling integer overflow that caused longer captures to fail before recognition. If older logs contain `startIndex` out-of-range errors while short phrases work, upgrade the bridge. Current pipeline errors include `stage=capture`, `recognition`, or `paste` and a stack trace; diagnose the failing stage before changing the model.

For latency or resource complaints, inspect `resident model ready`, `elapsed_ms`, fallback errors, and `resident model released after idle` in the log. Two bridge executables while a model is loaded are expected: the hotkey process and its private recognition worker. The worker must exit after the configured idle time; inspect actual per-process memory/GPU allocation rather than inferring it from the model file size.

An additional audio guard exists only during recording when output muting is enabled. Check `recording output muted`, `recording output restored` and pending-recovery messages when diagnosing playback state; the guard does not load a model. Use the user's actual playback test to verify audible behavior after command-level checks.

`--overlay-preview` shows the main states without recording or keyboard hooks. `--transcribe-file <wav>` tests the installed backend without pasting text. Recognition runs locally after downloads; clipboard content is replaced with the transcript. If the window or a detectable native input control changes, text stays on the clipboard. Application permissions and paste support still determine whether a particular input receives text; validate WeChat or other apps with the user, without sending a message.

## Uninstall

```powershell
& "$env:LOCALAPPDATA\CodexVoiceKey\scripts\Uninstall.ps1"
```

The uninstaller removes owned app files and its startup entry. It preserves configuration, logs, the reusable skill, Handy and shared model files. Do not remove shared dependencies or user recordings unless that cleanup is explicitly requested.

When finishing, report the install path, chosen key and startup state, and distinguish completed checks from any speech test still needed. Source and release documentation: https://github.com/SailTechnology/codex-voice-key
