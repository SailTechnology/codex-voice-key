using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexVoiceKey
{
    internal static class ResidentTests
    {
        internal static int FakeWorker()
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
            JavaScriptSerializer json = new JavaScriptSerializer();
            Console.WriteLine("{\"ready\":true,\"backend\":\"test\",\"load_ms\":0}");
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                string path = json.Deserialize<Dictionary<string, string>>(line)["path"];
                switch (Path.GetFileName(path))
                {
                    case "crash.wav": return 23;
                    case "hang.wav": Thread.Sleep(30000); break;
                    case "slow.wav": Thread.Sleep(2000); break;
                    case "error.wav": Console.WriteLine("{\"error\":\"simulated GPU failure\"}"); continue;
                    case "stderr.wav": Console.Error.Write(new string('x', 256000)); Console.Error.WriteLine(); break;
                }
                Console.WriteLine(json.Serialize(new { text = "中文 English " + Path.GetFileName(path) + " pid=" + Process.GetCurrentProcess().Id }));
            }
            return 0;
        }

        internal static void Run(Action<bool, string> check)
        {
            int starts = 0;
            Func<ProcessStartInfo> start = delegate {
                starts++;
                return new ProcessStartInfo { FileName = Process.GetCurrentProcess().MainModule.FileName, Arguments = "--fake-asr-worker" };
            };
            int workerPid;
            using (ResidentTranscriber client = new ResidentTranscriber(start, 5000, delegate { }))
            {
                string preview;
                check(!client.TryPreview("not-loaded.wav", out preview) && starts == 0, "Optional previews never load an extra model");
                client.WarmUp();
                string first = client.Transcribe(Path.Combine(Path.GetTempPath(), "语音 with spaces.wav"));
                workerPid = Int32.Parse(first.Substring(first.LastIndexOf('=') + 1));
                string second = client.Transcribe(Path.Combine(Path.GetTempPath(), "second.wav"));
                check(first.Contains("中文 English 语音 with spaces.wav"), "Resident pipe preserves UTF-8 and spaces");
                check(second.EndsWith("pid=" + workerPid), "Consecutive utterances reuse the same worker");
                check(client.TryPreview("partial.wav", out preview) && preview.EndsWith("pid=" + workerPid), "Preview and final recognition use the same worker");
                check(client.Transcribe("stderr.wav").Contains("stderr.wav"), "Large stderr cannot deadlock transcription");
                check(!client.ReleaseIfIdle(1), "Model is kept for the next immediate utterance");
                Thread.Sleep(1100);
                check(!client.ReleaseIfIdle(0), "Idle zero explicitly keeps the loaded model");
                check(client.ReleaseIfIdle(1), "An idle model releases its process and GPU allocation");
                check(!IsRunning(workerPid), "Idle release really exits the worker");
                string next = client.Transcribe("after-idle.wav");
                int nextPid = Int32.Parse(next.Substring(next.LastIndexOf('=') + 1));
                check(nextPid != workerPid, "Dictation reloads after idle release");
                workerPid = nextPid;
                System.Threading.Tasks.Task<string> inFlight = System.Threading.Tasks.Task.Factory.StartNew(delegate { return client.Transcribe("slow.wav"); });
                Thread.Sleep(1100);
                Stopwatch idleCall = Stopwatch.StartNew();
                check(!client.ReleaseIfIdle(1) && idleCall.ElapsedMilliseconds < 200, "Idle cleanup never interrupts or blocks an active transcription");
                idleCall.Restart();
                check(!client.TryPreview("skip.wav", out preview) && idleCall.ElapsedMilliseconds < 200, "Previews skip a busy model without queueing behind final recognition");
                check(inFlight.Result.Contains("slow.wav"), "Active transcription finishes after skipped idle cleanup");
            }
            bool exited;
            try { using (Process process = Process.GetProcessById(workerPid)) exited = process.HasExited; }
            catch (ArgumentException) { exited = true; }
            check(exited, "Shutdown releases the resident worker");

            foreach (string failure in new string[] { "crash.wav", "error.wav", "hang.wav" })
            {
                using (ResidentTranscriber client = new ResidentTranscriber(start, 700, delegate { }))
                {
                    client.WarmUp();
                    bool failed = false;
                    Stopwatch timer = Stopwatch.StartNew();
                    try { client.Transcribe(failure); } catch (Exception) { failed = true; }
                    check(failed && timer.ElapsedMilliseconds < 6000, "Worker " + failure + " returns control for CLI fallback");
                    timer.Restart(); failed = false;
                    try { client.Transcribe("next.wav"); } catch (InvalidOperationException) { failed = true; }
                    check(failed && timer.ElapsedMilliseconds < 500, "Broken worker is not reloaded on every press");
                }
            }
        }

        private static bool IsRunning(int id)
        {
            try { using (Process process = Process.GetProcessById(id)) return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }
}
