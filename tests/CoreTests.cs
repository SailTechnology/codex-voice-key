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
                ResidentTests.Run(Check);
                Console.WriteLine("PASS: " + count + " input, modifier, JSON, ABI and audio checks (no system input injected).");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
