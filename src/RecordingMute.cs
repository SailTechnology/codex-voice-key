using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexVoiceKey
{
    internal sealed class AudioMuteLease
    {
        private readonly IAudioOutputs outputs;
        private readonly Dictionary<string, bool> originals;
        private readonly Action<Dictionary<string, bool>> save;
        private readonly Action<string> log;
        internal int PendingCount { get { return originals.Count; } }

        internal AudioMuteLease(IAudioOutputs outputs, Dictionary<string, bool> originals,
            Action<Dictionary<string, bool>> save, Action<string> log)
        { this.outputs = outputs; this.originals = new Dictionary<string, bool>(originals); this.save = save; this.log = log; }

        internal void MuteActive()
        {
            Dictionary<string, bool> active = outputs.SnapshotActive();
            bool added = false;
            foreach (KeyValuePair<string, bool> device in active)
                if (!originals.ContainsKey(device.Key)) { originals.Add(device.Key, device.Value); added = true; }
            // Persist before changing a device, including devices connected
            // during recording. Repeated snapshots never replace its original state.
            if (added) save(new Dictionary<string, bool>(originals));
            foreach (KeyValuePair<string, bool> device in active)
                if (!device.Value) outputs.SetMute(device.Key, true);
        }

        internal void Restore()
        {
            foreach (KeyValuePair<string, bool> device in new Dictionary<string, bool>(originals))
            {
                try { outputs.SetMute(device.Key, device.Value); originals.Remove(device.Key); }
                catch (Exception error) { log("output restoration pending; type=" + error.GetType().Name); }
            }
            // Unplugged or temporarily unavailable outputs remain recoverable.
            save(new Dictionary<string, bool>(originals));
        }
    }

    // This lightweight child owns mute/restore, independently of the ASR model.
    // EOF or a dead parent restores outputs even after a forced bridge shutdown.
    internal sealed class RecordingMute : IDisposable
    {
        private readonly Process process;
        private readonly string executable, config;
        private readonly Action<string> log;
        private int disposed;
        internal bool IsActive { get { try { return disposed == 0 && !process.HasExited; } catch { return false; } } }

        private RecordingMute(string executable, string config, Action<string> log)
        {
            this.executable = executable; this.config = config; this.log = log;
            using (Process parent = Process.GetCurrentProcess())
                process = StartProcess(executable, "--audio-guard --audio-parent " + parent.Id +
                    " --audio-parent-start " + parent.StartTime.ToUniversalTime().Ticks + " --config " + Program.Quote(config));
        }

        internal static RecordingMute Begin(string executable, string config, Action<string> log)
        {
            Stopwatch setup = Stopwatch.StartNew();
            RecordingMute mute = new RecordingMute(executable, config, log);
            try { ReadReady(mute.process); log("recording output muted; setup_ms=" + setup.ElapsedMilliseconds); return mute; }
            catch { mute.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            // Closing this pipe is the release signal. Do not kill the guard
            // before it has restored the devices it owns.
            try { process.StandardInput.Close(); } catch { }
            bool exited = false;
            try { exited = process.WaitForExit(3000); } catch { }
            if (exited)
            {
                log(process.ExitCode == 0 ? "recording output restored" : "recording output guard ended; checking recovery");
                process.Dispose();
                try { RecoverIfNeeded(executable, config, log); }
                catch (Exception error) { log("output recovery pending: " + error.GetType().Name); }
            }
            else { log("output guard is still restoring audio"); process.Dispose(); }
        }

        internal static void RecoverIfNeeded(string executable, string config, Action<string> log)
        {
            if (!File.Exists(JournalPath(config))) return;
            using (Process recovery = StartProcess(executable, "--restore-audio --config " + Program.Quote(config)))
            {
                recovery.StandardInput.Close();
                ReadReady(recovery);
                if (!recovery.WaitForExit(3000)) throw new TimeoutException("Audio recovery is still running.");
                log("saved output state recovery checked");
            }
        }

        private static Process StartProcess(string executable, string arguments)
        {
            Process process = Process.Start(new ProcessStartInfo {
                FileName = executable, Arguments = arguments, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            process.ErrorDataReceived += delegate { };
            process.BeginErrorReadLine();
            return process;
        }

        private static void ReadReady(Process process)
        {
            Task<string> reply = Task.Factory.StartNew(delegate { return process.StandardOutput.ReadLine(); });
            if (!reply.Wait(5000)) throw new TimeoutException("Output mute guard startup timed out.");
            Dictionary<string, object> ready = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reply.Result ?? "{}");
            object value;
            if (ready == null || !ready.TryGetValue("ready", out value) || !Object.Equals(value, true))
                throw new InvalidOperationException("Output mute guard could not initialize.");
        }

        private static string JournalPath(string config)
        { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config)), "audio-output-restore.json"); }

        internal static int RunGuard(string config, int parentId, long parentStart, int maxSeconds, bool restoreOnly)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            string path = JournalPath(config);
            bool ownsMutex = false;
            using (Mutex mutex = new Mutex(false, "Local\\CodexVoiceKeyOutputMute"))
            {
                AudioMuteLease lease = null;
                try
                {
                    try { ownsMutex = mutex.WaitOne(3000); } catch (AbandonedMutexException) { ownsMutex = true; }
                    if (!ownsMutex) throw new InvalidOperationException("Another output mute guard is still active.");
                    Dictionary<string, bool> saved = File.Exists(path)
                        ? json.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path)) : new Dictionary<string, bool>();
                    if (saved == null) throw new InvalidDataException("Invalid saved audio output states.");
                    lease = new AudioMuteLease(new WindowsAudioOutputs(), saved, delegate(Dictionary<string, bool> states) {
                        if (states.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        string temporary = path + ".tmp";
                        File.WriteAllText(temporary, json.Serialize(states), new UTF8Encoding(false));
                        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                    }, Program.Log);
                    lease.Restore();
                    if (restoreOnly) { Console.WriteLine("{\"ready\":true}"); return 0; }
                    using (Process parent = Process.GetProcessById(parentId))
                    {
                        if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != parentStart) return 0;
                        lease.MuteActive();
                        Console.WriteLine("{\"ready\":true}");
                        Task<string> release = Task.Factory.StartNew(delegate { return Console.ReadLine(); });
                        Stopwatch lifetime = Stopwatch.StartNew();
                        while (!release.IsCompleted && !parent.HasExited && lifetime.Elapsed.TotalSeconds < maxSeconds + 15)
                        {
                            Thread.Sleep(200);
                            if (release.IsCompleted || parent.HasExited) break;
                            lease.MuteActive();
                        }
                    }
                    lease.Restore();
                    return lease.PendingCount == 0 ? 0 : 2;
                }
                catch (Exception error)
                {
                    try { Console.WriteLine(json.Serialize(new { ready = false, error = error.GetType().Name })); } catch { }
                    Program.Log("output mute guard failed: " + error.GetType().Name);
                    return 1;
                }
                finally
                {
                    if (lease != null) try { lease.Restore(); } catch (Exception error) { Program.Log("output restoration journal pending: " + error.GetType().Name); }
                    if (ownsMutex) mutex.ReleaseMutex();
                }
            }
        }
    }
}
