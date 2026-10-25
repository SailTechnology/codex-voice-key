using System;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexVoiceKey
{
    public sealed class AppSettings
    {
        public string Hotkey { get; set; }
        public string[] TargetProcesses { get; set; }
        public bool AllApplications { get; set; }
        public string HandyPath { get; set; }
        public string ModelId { get; set; }
        public string Microphone { get; set; }
        public bool PlaySounds { get; set; }
        public bool KeepModelLoaded { get; set; }
        public int ModelIdleSeconds { get; set; }
        public bool LivePreview { get; set; }
        public bool MuteOutputWhileRecording { get; set; }
        public bool KeepRecordings { get; set; }
        public int MaxRecordingSeconds { get; set; }
        public int TranscriptionTimeoutSeconds { get; set; }
        public int TaskbarGap { get; set; }

        public AppSettings()
        {
            Hotkey = "Ctrl+Alt+Space";
            TargetProcesses = new string[] { "ChatGPT", "Codex" };
            HandyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handy", "handy.exe");
            ModelId = "handy-computer/Qwen3-ASR-1.7B-gguf/Qwen3-ASR-1.7B-Q5_K_M.gguf";
            Microphone = "default";
            PlaySounds = true;
            KeepModelLoaded = true;
            ModelIdleSeconds = 60;
            LivePreview = true;
            MuteOutputWhileRecording = true;
            KeepRecordings = false;
            MaxRecordingSeconds = 120;
            TranscriptionTimeoutSeconds = 120;
            TaskbarGap = 12;
        }

        public static AppSettings Load(string path)
        {
            // A missing login-time config must not silently change the shortcut,
            // application scope or backend to constructor defaults.
            if (!File.Exists(path)) throw new FileNotFoundException("The voice configuration is missing. Run the installer to repair startup.", path);
            AppSettings settings = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(path));
            if (settings == null) throw new InvalidDataException("The configuration must be a JSON object.");
            settings.Validate();
            return settings;
        }

        public void Validate()
        {
            HotkeyBinding.Parse(Hotkey);
            if (AllApplications && TargetProcesses == null) TargetProcesses = new string[0];
            if (TargetProcesses == null || (!AllApplications && TargetProcesses.Length == 0))
                throw new InvalidDataException("TargetProcesses must contain at least one process name.");
            foreach (string name in TargetProcesses)
                if (String.IsNullOrWhiteSpace(name) || name.IndexOfAny(new char[] { '/', '\\', ':' }) >= 0)
                    throw new InvalidDataException("TargetProcesses accepts process names, not paths.");
            if (String.IsNullOrWhiteSpace(HandyPath)) throw new InvalidDataException("HandyPath is required.");
            HandyPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(HandyPath));
            if (String.IsNullOrWhiteSpace(ModelId) || ModelId.IndexOf('"') >= 0)
                throw new InvalidDataException("ModelId is required and cannot contain quotes.");
            if (MaxRecordingSeconds < 1 || MaxRecordingSeconds > 600)
                throw new InvalidDataException("MaxRecordingSeconds must be between 1 and 600.");
            if (TranscriptionTimeoutSeconds < 5 || TranscriptionTimeoutSeconds > 1800)
                throw new InvalidDataException("TranscriptionTimeoutSeconds must be between 5 and 1800.");
            if (ModelIdleSeconds < 0 || ModelIdleSeconds > 3600)
                throw new InvalidDataException("ModelIdleSeconds must be between 0 and 3600 (0 keeps the loaded model until exit).");
            if (TaskbarGap < 0 || TaskbarGap > 200) throw new InvalidDataException("TaskbarGap must be between 0 and 200.");
        }
    }
}
