using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexVoiceKey
{
    internal static class CoreTests
    {
        private static int count;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            count++;
        }
        private static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--fake-asr-worker") >= 0) return ResidentTests.FakeWorker();
            try
            {
                HotkeyBinding ctrlB = HotkeyBinding.Parse("Ctrl+B");
                Check(ctrlB.Key == 0x42 && ctrlB.Modifiers == KeyModifiers.Ctrl, "Ctrl+B binding");
                Check(HotkeyBinding.Parse("MouseMiddle").Kind == BindingKind.Mouse, "Middle mouse binding");
                Check(HotkeyBinding.Parse("MouseX1").Key == 5 && HotkeyBinding.Parse("MouseX2").Key == 6, "Mouse side buttons");
                Check(HotkeyBinding.Parse("F8").Key == 0x77 && HotkeyBinding.Parse("Copilot").Kind == BindingKind.Copilot, "Standalone and Copilot bindings");
                foreach (string bad in new string[] { "", "Ctrl", "Shift", "MouseLeft", "Ctrl+Ctrl+B", "nonsense+B" })
                {
                    bool rejected = false;
                    try { HotkeyBinding.Parse(bad); } catch (ArgumentException) { rejected = true; }
                    Check(rejected, "Reject invalid binding: " + bad);
                }
                HoldKeyState hold = new HoldKeyState(ctrlB);
                Check(!hold.Process(BindingKind.Keyboard, 0x42, true, KeyModifiers.Ctrl, false).Suppress, "Pass shortcut outside target");
                InputDecision press = hold.Process(BindingKind.Keyboard, 0x42, true, KeyModifiers.Ctrl, true);
                Check(press.Start && press.Suppress, "Start only inside target");
                Check(!hold.Process(BindingKind.Keyboard, 0x42, true, KeyModifiers.Ctrl, true).Start, "Auto-repeat never restarts capture");
                Check(!hold.Process(BindingKind.Keyboard, 0xA0, true, KeyModifiers.Ctrl, true).Suppress, "Do not swallow unrelated Shift");
                Check(hold.Process(BindingKind.Keyboard, 0x42, false, KeyModifiers.None, false).Stop, "Release ends capture after changing focus");
                Check(!hold.Active, "Capture is released");
                HoldKeyState mouse = new HoldKeyState(HotkeyBinding.Parse("MouseX1"));
                Check(!mouse.Process(BindingKind.Mouse, 6, true, KeyModifiers.None, true).Suppress, "Other mouse button passes through");
                Check(mouse.Process(BindingKind.Mouse, 5, true, KeyModifiers.None, true).Start, "Mouse hold starts capture");
                Check(mouse.Process(BindingKind.Mouse, 5, false, KeyModifiers.None, false).Stop, "Mouse release stops capture outside target");

                CopilotKeyState copilot = new CopilotKeyState();
                copilot.Process(0x5B, true, true);
                InputDecision winTap = copilot.Process(0x5B, false, true);
                Check(winTap.Forward.Count == 2 && winTap.Forward[0].Down && !winTap.Forward[1].Down, "A lone Windows tap preserves ordered down/up");
                copilot.Process(0x5B, true, true);
                copilot.Process(0xA0, true, true);
                InputDecision screenShortcut = copilot.Process(0x53, true, true);
                Check(screenShortcut.Forward.Count == 3 && screenShortcut.Forward[0].Key == 0x5B && screenShortcut.Forward[1].Key == 0xA0 && screenShortcut.Forward[2].Key == 0x53, "Windows+Shift+S preserves ordered modifiers");
                Check(!copilot.Process(0xA0, false, true).Suppress && !copilot.Process(0x5B, false, true).Suppress, "Ordinary shortcut modifier ups pass through");
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    copilot.Process(0x5B, true, true);
                    copilot.Process(0xA0, true, true);
                    Check(copilot.Process(0x86, true, true).Start, "Copilot starts capture");
                    for (int repeat = 0; repeat < 10; repeat++) Check(!copilot.Process(0x86, true, true).Start, "Held Copilot never retriggers");
                    Check(copilot.Process(0x86, false, true).Stop, "Copilot release ends capture");
                    Check(copilot.Process(0xA0, false, true).Suppress && copilot.Process(0x5B, false, true).Suppress, "Consumed Copilot modifiers do not open Start");
                    Check(!copilot.Process(0xA0, true, true).Suppress, "Shift works immediately after Copilot");
                    copilot.Process(0xA0, false, true);
                }
                copilot.Process(0x5B, true, false); copilot.Process(0xA0, true, false);
                InputDecision elsewhere = copilot.Process(0x86, true, false);
                Check(elsewhere.Suppress && !elsewhere.Start, "Configured Copilot is reserved outside target without recording");
                copilot.Process(0x86, false, false); copilot.Process(0xA0, false, false); copilot.Process(0x5B, false, false);
                Check(!copilot.Pending && !copilot.Active, "Copilot state fully drains on key-up");

                Check(Marshal.SizeOf(typeof(NativeMethods.Input)) == 40, "Win64 INPUT must include the full union (40 bytes)");
                Check(Marshal.OffsetOf(typeof(NativeMethods.Input), "data").ToInt32() == 8, "Win64 INPUT union alignment");
                Check(Marshal.SizeOf(typeof(NativeMethods.GuiThreadInfo)) == 72, "Win64 focused-control structure layout");
                AppSettings originalSettings = Program.Settings;
                try
                {
                    Program.Settings = new AppSettings { AllApplications = true };
                    Check(Program.IsTargetWindow(new IntPtr(123)), "Global mode accepts windows outside the Codex process list");
                    Check(!Program.IsTargetWindow(IntPtr.Zero), "Global mode still requires a foreground window");
                    Program.Settings.TargetProcesses = new string[0];
                    Program.Settings.Validate();
                    Check(Program.Settings.AllApplications, "Global mode does not require a process allowlist");
                }
                finally { Program.Settings = originalSettings; }
                Check(Program.CanPaste(new IntPtr(10), new IntPtr(20), new IntPtr(10), new IntPtr(20)), "Paste into the original window and control");
                Check(!Program.CanPaste(new IntPtr(10), new IntPtr(20), new IntPtr(11), new IntPtr(20)), "Changing applications leaves text on the clipboard");
                Check(!Program.CanPaste(new IntPtr(10), new IntPtr(20), new IntPtr(10), new IntPtr(21)), "Changing native input controls does not receive the old dictation");
                Check(!Program.CanPaste(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero), "No target never receives pasted text");
                Check(Program.CanPaste(new IntPtr(10), IntPtr.Zero, new IntPtr(10), new IntPtr(20)), "Apps without native focus details retain window-level paste support");
                Check(VoiceOverlay.PreviewTail("中文\r\nEnglish") == "中文  English", "Preview handles multiline mixed-language results");
                string emojiTail = VoiceOverlay.PreviewTail(new string('x', 40) + "😀" + new string('y', 31));
                Check(emojiTail.StartsWith("…😀") && emojiTail.EndsWith(new string('y', 31)), "Recent preview text never splits an emoji surrogate pair");
                Check(Program.ExtractJsonText("{\"text\":\"中文 check \\u8bed\\u97f3 \\nEnglish\"}") == "中文 check 语音 \nEnglish", "JSON Unicode, English and escaped newlines");
                Check(Program.ExtractJsonText("{\"duration\":2}") == String.Empty, "Missing transcript is not hallucinated");
                byte[] stereo = new byte[48000 * 4];
                for (int i = 0; i < 48000; i++)
                {
                    short sample = (short)(10000 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
                    byte[] left = BitConverter.GetBytes(sample), right = BitConverter.GetBytes((short)-sample);
                    Buffer.BlockCopy(left, 0, stereo, i * 4, 2); Buffer.BlockCopy(right, 0, stereo, i * 4 + 2, 2);
                }
                byte[] mono = PcmWave.Resample(stereo, 48000, 2);
                Check(mono.Length == 32000, "48 kHz stereo becomes 1 second of 16 kHz mono");
                bool silent = true; foreach (byte sample in mono) if (sample != 0) silent = false;
                Check(silent, "Both input channels are mixed with correct frame alignment");
                Check(PcmWave.Resample(new byte[0], 44100, 2).Length == 0, "Empty capture is valid empty PCM");
                using (MemoryStream liveAudio = new MemoryStream())
                {
                    // Four seconds at 3 Hz, stereo, plus one unfinished byte.
                    for (short frame = 0; frame < 12; frame++)
                    {
                        byte[] left = BitConverter.GetBytes(frame), right = BitConverter.GetBytes((short)-frame);
                        liveAudio.Write(left, 0, 2); liveAudio.Write(right, 0, 2);
                    }
                    liveAudio.WriteByte(0xEE);
                    byte[] tail = PcmWave.ReadTail(liveAudio, 3, 2, 2);
                    Check(tail.Length == 24 && BitConverter.ToInt16(tail, 0) == 6 && BitConverter.ToInt16(tail, 22) == -11, "Preview contains only the last two seconds, preserving stereo frames");
                    Check(liveAudio.Position == 49, "Preview preserves the microphone's append cursor");
                    Check(PcmWave.ReadTail(liveAudio, 3, 2, 20).Length == 48, "Short previews exclude incomplete trailing frames");
                    liveAudio.WriteByte(0xDD);
                    Check(liveAudio.Length == 50 && liveAudio.ToArray()[49] == 0xDD, "Recording continues appending after a preview");
                }
                // Integer multiplication overflow used to reject recordings
                // longer than 8.4 s at 16 kHz, or 2.8 s at 48 kHz.
                foreach (int rate in new int[] { 16000, 44100, 48000 })
                {
                    foreach (int seconds in new int[] { 20, 120 })
                    {
                        byte[] longAudio = new byte[rate * seconds * 2];
                        for (int i = 0; i < longAudio.Length; i += 2) { longAudio[i] = 0xD2; longAudio[i + 1] = 0x04; }
                        byte[] converted = PcmWave.Resample(longAudio, rate, 1);
                        Check(converted.Length == seconds * 16000 * 2, seconds + "-second recording keeps its duration at " + rate + " Hz");
                        Check(BitConverter.ToInt16(converted, converted.Length - 2) == 1234, "Long recording retains its last sample without integer overflow at " + rate + " Hz");
                    }
                }
                string temp = Path.Combine(Path.GetTempPath(), "codex-voice-test-" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    PcmWave.Write(temp, stereo, 48000, 2);
                    byte[] wav = File.ReadAllBytes(temp);
                    Check(wav.Length == 32044 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "Valid WAV container and duration");
                    Check(BitConverter.ToInt16(wav, 22) == 1 && BitConverter.ToInt32(wav, 24) == 16000, "WAV declares mono 16 kHz");
                    float[] nativeSamples = NativeTranscriber.ReadWave(temp);
                    Check(nativeSamples.Length == 16000 && nativeSamples[100] == 0, "Native reader preserves PCM duration and channel mix");
                    wav[22] = 2;
                    File.WriteAllBytes(temp, wav);
                    bool invalidAudio = false;
                    try { NativeTranscriber.ReadWave(temp); } catch (InvalidDataException) { invalidAudio = true; }
                    Check(invalidAudio, "Reject stereo WAV instead of transcribing with wrong timing");
                    File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });
                    invalidAudio = false;
                    try { NativeTranscriber.ReadWave(temp); } catch (InvalidDataException) { invalidAudio = true; }
                    Check(invalidAudio, "Reject malformed WAV without native access");
                }
                finally { File.Delete(temp); }
                AudioMuteTests.Run(Check);
                ResidentTests.Run(Check);
                Console.WriteLine("PASS: " + count + " input, modifier, JSON, ABI and audio checks (no system input injected).");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
