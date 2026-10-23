using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexVoiceKey
{
    internal static class Program
    {
        internal static AppSettings Settings = new AppSettings();
        internal static string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexVoiceKey");
        private static readonly object Sync = new object();
        private static Session current;
        private static bool processing;
        private static System.Windows.Forms.Timer safetyTimer;

        private sealed class Session
        {
            public WaveInRecorder Recorder;
            public readonly ManualResetEvent Ready = new ManualResetEvent(false);
            public string Path;
            public Exception StartError;
            public IntPtr Target;
            public bool Released;
            public readonly Stopwatch Duration = Stopwatch.StartNew();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string config = Argument(args, "--config") ?? Path.Combine(DataDirectory, "config.json");
                config = Path.GetFullPath(config);
                DataDirectory = Path.GetDirectoryName(config);
                Settings = AppSettings.Load(config);
                // A WinExe has no console code page. Set stream encodings directly
                // so redirected diagnostics still contain correct Chinese text.
                try
                {
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                    Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                }
                catch (IOException) { }
                string validate = Argument(args, "--validate-hotkey");
                if (validate != null) { Console.WriteLine(HotkeyBinding.Parse(validate).Label); return 0; }
                string diagnose = Argument(args, "--diagnose");
                if (diagnose != null)
                {
                    object report = new {
                        hotkey = Settings.Hotkey, handyFound = File.Exists(Settings.HandyPath),
                        handyPath = Settings.HandyPath, model = Settings.ModelId,
                        modelFound = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "models", Path.GetFileName(Settings.ModelId))),
                        microphones = WaveInRecorder.ListDevices(), configPath = config,
                        targetProcesses = Settings.TargetProcesses, capture = "waveIn", architecture = IntPtr.Size * 8
                    };
                    File.WriteAllText(diagnose, new JavaScriptSerializer().Serialize(report), new UTF8Encoding(false));
                    return 0;
                }
                string audio = Argument(args, "--transcribe-file");
                if (audio != null) { Console.WriteLine(Transcribe(Path.GetFullPath(audio))); return 0; }
                if (Array.IndexOf(args, "--overlay-preview") >= 0)
                {
                    VoiceOverlay.Initialize();
                    VoiceOverlay.ShowRecording();
                    int step = 0;
                    System.Windows.Forms.Timer preview = new System.Windows.Forms.Timer();
                    preview.Interval = 2000;
                    preview.Tick += delegate {
                        step++;
                        if (step == 1) VoiceOverlay.ShowTranscribing();
                        else if (step == 2) VoiceOverlay.ShowSuccess("已经放进输入框");
                        else if (step == 3) VoiceOverlay.ShowError("这次没听清");
                        else { preview.Stop(); Application.ExitThread(); }
                    };
                    preview.Start();
                    Application.Run();
                    preview.Dispose();
                    return 0;
                }
                bool created;
                using (Mutex mutex = new Mutex(true, "Local\\CodexVoiceKeyBridge", out created))
                {
                    if (!created) return 0;
                    Directory.CreateDirectory(Path.Combine(DataDirectory, "recordings"));
                    VoiceOverlay.Initialize();
                    InputHooks.Start(HotkeyBinding.Parse(Settings.Hotkey));
                    safetyTimer = new System.Windows.Forms.Timer();
                    safetyTimer.Interval = 500;
                    safetyTimer.Tick += delegate {
                        Session session;
                        lock (Sync) session = current;
                        if (session != null && !session.Released && session.Duration.Elapsed.TotalSeconds >= Settings.MaxRecordingSeconds)
                            EndVoiceCapture("recording time limit");
                    };
                    safetyTimer.Start();
                    Log("bridge started; capture=waveIn; hotkey=" + Settings.Hotkey + "; model=" + Settings.ModelId);
                    Application.Run(new ApplicationContext());
                    safetyTimer.Dispose();
                }
                return 0;
            }
            catch (Exception error)
            {
                Log("startup/command failed: " + error.Message);
                try { Console.Error.WriteLine(error.Message); } catch (IOException) { }
                return 1;
            }
        }

        private static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }

        internal static bool IsCodexForeground() { return IsTargetWindow(NativeMethods.GetForegroundWindow()); }
        internal static bool IsTargetWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            try
            {
                uint id; NativeMethods.GetWindowThreadProcessId(window, out id);
                using (Process process = Process.GetProcessById((int)id))
                    foreach (string target in Settings.TargetProcesses)
                        if (String.Equals(Path.GetFileNameWithoutExtension(target), process.ProcessName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return false;
        }

        internal static void BeginVoiceCapture(string source)
        {
            Session session;
            lock (Sync)
            {
                if (!IsCodexForeground()) return;
                if (processing) { VoiceOverlay.ShowInfo("上一句还在转写", 1200); return; }
                session = new Session();
                session.Target = NativeMethods.GetForegroundWindow();
                session.Path = Path.Combine(DataDirectory, "recordings", Guid.NewGuid().ToString("N") + ".wav");
                session.Recorder = new WaveInRecorder(session.Path);
                processing = true;
                current = session;
            }
            VoiceOverlay.ShowRecording();
            ThreadPool.QueueUserWorkItem(delegate {
                bool failedBeforeRelease = false;
                try
                {
                    session.Recorder.Start();
                    Log("capture started; device=" + session.Recorder.DeviceName);
                    if (Settings.PlaySounds) try { SystemSounds.Asterisk.Play(); } catch { }
                }
                catch (Exception error)
                {
                    session.StartError = error;
                    VoiceOverlay.ShowError("麦克风打开失败");
                    Log("capture failed: " + error.Message);
                    lock (Sync)
                    {
                        if (current == session && !session.Released)
                        {
                            current = null; processing = false;
                            session.Recorder.Dispose();
                            failedBeforeRelease = true;
                            if (!Settings.KeepRecordings) try { if (File.Exists(session.Path)) File.Delete(session.Path); } catch { }
                        }
                    }
                }
                finally { session.Ready.Set(); if (failedBeforeRelease) session.Ready.Dispose(); }
            });
        }

        internal static void EndVoiceCapture(string reason)
        {
            Session session;
            lock (Sync)
            {
                session = current;
                if (session == null || session.Released) return;
                session.Released = true;
            }
            VoiceOverlay.ShowTranscribing();
            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    if (!session.Ready.WaitOne(10000)) throw new TimeoutException("Microphone startup timed out.");
                    if (session.StartError != null) throw session.StartError;
                    session.Recorder.Stop();
                    session.Recorder.Dispose();
                    Log("capture stopped; reason=" + reason);
                    if (Settings.PlaySounds) try { SystemSounds.Beep.Play(); } catch { }
                    if (!File.Exists(session.Path) || new FileInfo(session.Path).Length < 8000)
                    {
                        VoiceOverlay.ShowError("录音太短");
                        return;
                    }
                    string text = Transcribe(session.Path);
                    if (String.IsNullOrWhiteSpace(text)) { VoiceOverlay.ShowError("没有识别到语音"); return; }
                    bool pasted = PasteIntoTarget(text, session.Target);
                    Log("transcription completed; characters=" + text.Length + "; pasted=" + pasted);
                    if (pasted) VoiceOverlay.ShowSuccess("已经放进输入框");
                    else VoiceOverlay.ShowCopied("已经识别好了");
                }
                catch (Exception error) { Log("voice pipeline failed: " + error.Message); VoiceOverlay.ShowError("转写失败，请运行诊断"); }
                finally
                {
                    session.Recorder.Dispose();
                    if (!Settings.KeepRecordings) try { if (File.Exists(session.Path)) File.Delete(session.Path); } catch { }
                    lock (Sync) { if (current == session) { current = null; processing = false; } }
                    session.Ready.Dispose();
                }
            });
        }

        internal static string Transcribe(string path)
        {
            if (!File.Exists(Settings.HandyPath)) throw new FileNotFoundException("Handy is missing. Run scripts\\install.ps1.", Settings.HandyPath);
            ProcessStartInfo start = new ProcessStartInfo {
                FileName = Settings.HandyPath,
                Arguments = "--transcribe-file " + Quote(path) + " --model " + Quote(Settings.ModelId) + " --json",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process process = Process.Start(start))
            {
                // Drain both pipes while the child runs: large diagnostics must
                // never deadlock WaitForExit with a full stdout/stderr buffer.
                Task<string> stdout = Task.Factory.StartNew(delegate { return process.StandardOutput.ReadToEnd(); });
                Task<string> stderr = Task.Factory.StartNew(delegate { return process.StandardError.ReadToEnd(); });
                if (!process.WaitForExit(Settings.TranscriptionTimeoutSeconds * 1000))
                {
                    try { process.Kill(); } catch { }
                    process.WaitForExit(5000);
                    throw new TimeoutException("Local transcription timed out.");
                }
                string output = stdout.Result, errors = stderr.Result;
                if (process.ExitCode != 0) throw new InvalidOperationException("Handy exited " + process.ExitCode + ": " + errors.Trim());
                return ExtractJsonText(output);
            }
        }

        internal static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0) throw new ArgumentException("Quotes are not allowed in an argument value.");
            return "\"" + value + "\"";
        }

        internal static string ExtractJsonText(string json)
        {
            JavaScriptSerializer parser = new JavaScriptSerializer();
            Dictionary<string, object> value = parser.Deserialize<Dictionary<string, object>>(json.Trim());
            object text;
            if (value == null || !value.TryGetValue("text", out text)) return String.Empty;
            return text as string ?? String.Empty;
        }

        private static bool PasteIntoTarget(string text, IntPtr target)
        {
            bool pasted = false;
            Thread thread = new Thread(delegate() {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        if (NativeMethods.GetForegroundWindow() == target && IsTargetWindow(target))
                        { NativeMethods.SendPaste(); pasted = true; }
                        return;
                    }
                    catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(50); }
                    catch (Exception error) { Log("paste failed: " + error.Message); return; }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join(5000);
            return pasted;
        }

        internal static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                string path = Path.Combine(DataDirectory, "voice-bridge.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                { if (File.Exists(path + ".old")) File.Delete(path + ".old"); File.Move(path, path + ".old"); }
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
