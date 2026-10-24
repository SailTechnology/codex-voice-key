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
    internal sealed class ResidentTranscriber : IDisposable
    {
        private readonly object sync = new object();
        private readonly Func<ProcessStartInfo> makeStart;
        private readonly Action<string> log;
        private readonly int timeoutMs;
        private Process worker;
        private StreamWriter input;
        private volatile bool disposed;
        private DateTime retryAfter;
        private long lastActivity = Stopwatch.GetTimestamp();

        internal ResidentTranscriber(Func<ProcessStartInfo> makeStart, int timeoutMs, Action<string> log)
        { this.makeStart = makeStart; this.timeoutMs = timeoutMs; this.log = log; }

        internal void WarmUp()
        {
            Touch();
            lock (sync)
            {
                try { EnsureStarted(); }
                catch { Failed(); throw; }
            }
        }

        internal string Transcribe(string path)
        {
            lock (sync)
            {
                try
                {
                    EnsureStarted();
                    Stopwatch elapsed = Stopwatch.StartNew();
                    input.WriteLine(new JavaScriptSerializer().Serialize(new { path = Path.GetFullPath(path) }));
                    Dictionary<string, object> reply = ReadReply();
                    object text;
                    if (!reply.TryGetValue("text", out text) || !(text is string))
                        throw new InvalidDataException("Resident worker returned no transcription.");
                    log("resident transcription completed; elapsed_ms=" + elapsed.ElapsedMilliseconds);
                    Touch();
                    return (string)text;
                }
                catch { Failed(); throw; }
            }
        }

        internal bool TryPreview(string path, out string text)
        {
            text = null;
            if (!Monitor.TryEnter(sync)) return false;
            try
            {
                if (disposed || worker == null || worker.HasExited) return false;
                Stopwatch elapsed = Stopwatch.StartNew();
                input.WriteLine(new JavaScriptSerializer().Serialize(new { path = Path.GetFullPath(path) }));
                Dictionary<string, object> reply = ReadReply(Math.Min(timeoutMs, 8000));
                object value;
                if (!reply.TryGetValue("text", out value) || !(value is string))
                    throw new InvalidDataException("Preview worker returned no text.");
                text = (string)value;
                Touch();
                log("resident preview completed; elapsed_ms=" + elapsed.ElapsedMilliseconds);
                return true;
            }
            catch { Failed(); throw; }
            finally { Monitor.Exit(sync); }
        }

        private void EnsureStarted()
        {
            if (disposed) throw new ObjectDisposedException("ResidentTranscriber");
            if (DateTime.UtcNow < retryAfter) throw new InvalidOperationException("Resident worker is recovering; using Handy CLI.");
            if (worker != null && !worker.HasExited) return;
            StopWorker();
            ProcessStartInfo start = makeStart();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
            start.StandardOutputEncoding = start.StandardErrorEncoding = Encoding.UTF8;
            worker = Process.Start(start);
            // Always drain stderr, but never copy native output or transcripts to
            // the bridge log. A busy diagnostic pipe cannot stall the worker.
            worker.ErrorDataReceived += delegate { };
            worker.BeginErrorReadLine();
            input = new StreamWriter(worker.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
            if (disposed) { StopWorker(); throw new ObjectDisposedException("ResidentTranscriber"); }
            Dictionary<string, object> ready = ReadReply(Math.Max(timeoutMs, 10000));
            object ok, backend, load;
            if (!ready.TryGetValue("ready", out ok) || !Object.Equals(ok, true))
                throw new InvalidDataException("Resident worker did not become ready.");
            ready.TryGetValue("backend", out backend); ready.TryGetValue("load_ms", out load);
            log("resident model ready; backend=" + backend + "; load_ms=" + load);
            Touch();
        }

        internal void Touch() { Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp()); }

        internal bool ReleaseIfIdle(int seconds)
        {
            if (seconds <= 0 || !Monitor.TryEnter(sync)) return false;
            try
            {
                double elapsed = (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastActivity)) / (double)Stopwatch.Frequency;
                if (worker == null || elapsed < seconds) return false;
                StopWorker();
                log("resident model released after idle; seconds=" + seconds);
                return true;
            }
            finally { Monitor.Exit(sync); }
        }

        private Dictionary<string, object> ReadReply(int waitMs = 0)
        {
            Process current = worker;
            Task<string> line = Task.Factory.StartNew(delegate { return current.StandardOutput.ReadLine(); });
            if (!line.Wait(waitMs > 0 ? waitMs : timeoutMs)) throw new TimeoutException("Resident transcription timed out.");
            if (line.Result == null) throw new IOException("Resident worker exited unexpectedly.");
            Dictionary<string, object> reply = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line.Result);
            if (reply == null) throw new InvalidDataException("Empty worker response.");
            object error;
            if (reply.TryGetValue("error", out error)) throw new InvalidOperationException(Convert.ToString(error));
            return reply;
        }

        private void Failed()
        {
            StopWorker();
            // Do not keep reloading a broken GPU/DLL on every press. The same
            // recording immediately uses Handy; a later request retries warm mode.
            retryAfter = DateTime.UtcNow.AddSeconds(30);
        }

        private void StopWorker()
        {
            Process current = worker;
            worker = null;
            if (input != null) { try { input.Dispose(); } catch { } input = null; }
            if (current == null) return;
            try { if (!current.HasExited && !current.WaitForExit(1000)) { current.Kill(); current.WaitForExit(3000); } } catch { }
            current.Dispose();
        }

        public void Dispose()
        {
            disposed = true;
            // Closing stdin releases an idle worker. Kill interrupts a native
            // GPU hang before acquiring the request lock during shutdown.
            Process current = worker;
            try { if (current != null && !current.HasExited) current.Kill(); } catch { }
            lock (sync) StopWorker();
        }

        internal static int RunWorker(AppSettings settings)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
            try
            {
                Stopwatch load = Stopwatch.StartNew();
                string model = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "models", Path.GetFileName(settings.ModelId));
                using (NativeTranscriber engine = new NativeTranscriber(Path.GetDirectoryName(settings.HandyPath), model))
                {
                    engine.WarmUp();
                    Console.WriteLine(json.Serialize(new { ready = true, backend = engine.Backend, load_ms = load.ElapsedMilliseconds }));
                    string line;
                    while ((line = Console.ReadLine()) != null)
                    {
                        try
                        {
                            Dictionary<string, string> request = json.Deserialize<Dictionary<string, string>>(line);
                            Console.WriteLine(json.Serialize(new { text = engine.Transcribe(request["path"]) }));
                        }
                        catch (Exception error) { Console.WriteLine(json.Serialize(new { error = error.Message })); }
                    }
                }
                return 0;
            }
            catch (Exception error) { Console.WriteLine(json.Serialize(new { error = error.Message })); return 1; }
        }
    }
}
