using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexVoiceKey
{
    // Only the private worker loads native code. The keyboard/recording process
    // survives a driver failure and can fall back to the original Handy CLI.
    internal sealed class NativeTranscriber : IDisposable
    {
        private IntPtr session;
        public string Version { get; private set; }
        public string Backend { get; private set; }

        public NativeTranscriber(string directory, string model)
        {
            directory = Path.GetFullPath(directory);
            if (!File.Exists(Path.Combine(directory, "transcribe.dll")))
                throw new FileNotFoundException("Handy's native transcription library is missing.");
            if (!File.Exists(model)) throw new FileNotFoundException("The speech model is missing.", model);
            if (!SetDllDirectory(directory)) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Resolve dependent ggml/VC runtime DLLs from the same installed
            // package, never from the current working directory.
            if (LoadLibraryEx(Path.Combine(directory, "transcribe.dll"), IntPtr.Zero, 8) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot load Handy's native library.");
            Version = ReadUtf8(transcribe_version());
            if (Version != "0.2.4")
                throw new NotSupportedException("Resident mode requires transcribe.cpp 0.2.4; found " + Version + ". Use Handy CLI fallback.");
            transcribe_log_set(IntPtr.Zero, IntPtr.Zero);
            Check(transcribe_init_backends(Utf8(directory)), "Initialize compute backends");
            // The 0.2.4 C API explicitly accepts NULL for default params.
            // Avoid mirroring native parameter structs across ABI versions.
            Check(transcribe_open(Utf8(model), IntPtr.Zero, IntPtr.Zero, out session), "Load speech model");
            Backend = ReadUtf8(transcribe_model_backend(transcribe_get_model(session)));
        }

        public string Transcribe(string path)
        {
            if (session == IntPtr.Zero) throw new ObjectDisposedException("NativeTranscriber");
            float[] samples = ReadWave(path);
            Check(transcribe_run(session, samples, samples.Length, IntPtr.Zero), "Transcribe audio");
            return ReadUtf8(transcribe_full_text(session)).Trim();
        }

        public void WarmUp()
        {
            // Compile lazy GPU pipelines before the first real dictation. This
            // contains synthetic silence only; it never opens the microphone.
            Check(transcribe_run(session, new float[16000], 16000, IntPtr.Zero), "Warm up speech model");
        }

        internal static float[] ReadWave(string path)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                if (FourCC(reader) != "RIFF") throw new InvalidDataException("Expected a RIFF WAV file.");
                uint riffBytes = reader.ReadUInt32();
                if (FourCC(reader) != "WAVE" || riffBytes < 4 || (long)riffBytes + 8 > reader.BaseStream.Length)
                    throw new InvalidDataException("Invalid WAV container.");
                long end = (long)riffBytes + 8;
                bool formatValid = false;
                byte[] pcm = null;
                while (reader.BaseStream.Position + 8 <= end)
                {
                    string kind = FourCC(reader);
                    uint length = reader.ReadUInt32();
                    long chunkEnd = reader.BaseStream.Position + length;
                    if (chunkEnd > end) throw new InvalidDataException("Truncated WAV chunk.");
                    if (kind == "fmt ")
                    {
                        if (length < 16) throw new InvalidDataException("Invalid WAV format.");
                        ushort encoding = reader.ReadUInt16(), channels = reader.ReadUInt16();
                        uint rate = reader.ReadUInt32(), bytesPerSecond = reader.ReadUInt32();
                        ushort alignment = reader.ReadUInt16(), bits = reader.ReadUInt16();
                        formatValid = encoding == 1 && channels == 1 && rate == 16000 && bits == 16 && alignment == 2 && bytesPerSecond == 32000;
                        if (!formatValid) throw new InvalidDataException("Expected 16 kHz mono 16-bit PCM.");
                    }
                    else if (kind == "data")
                    {
                        if (length == 0 || (length & 1) != 0 || length > 32000 * 600)
                            throw new InvalidDataException("Invalid audio length.");
                        pcm = reader.ReadBytes((int)length);
                    }
                    reader.BaseStream.Position = chunkEnd + (length & 1);
                }
                if (!formatValid || pcm == null) throw new InvalidDataException("WAV format or samples are missing.");
                float[] samples = new float[pcm.Length / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
                return samples;
            }
        }

        private static string FourCC(BinaryReader reader) { return Encoding.ASCII.GetString(reader.ReadBytes(4)); }
        private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }
        private static string ReadUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return String.Empty;
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        private static void Check(int status, string operation)
        {
            if (status != 0) throw new InvalidOperationException(operation + ": " + ReadUtf8(transcribe_status_string(status)));
        }
        public void Dispose()
        {
            if (session != IntPtr.Zero) { transcribe_close(session); session = IntPtr.Zero; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr transcribe_version();
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void transcribe_log_set(IntPtr callback, IntPtr user);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int transcribe_init_backends(byte[] directory);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int transcribe_open(byte[] path, IntPtr load, IntPtr options, out IntPtr session);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void transcribe_close(IntPtr session);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr transcribe_get_model(IntPtr session);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr transcribe_model_backend(IntPtr model);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int transcribe_run(IntPtr session, float[] pcm, int samples, IntPtr options);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr transcribe_full_text(IntPtr session);
        [DllImport("transcribe.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr transcribe_status_string(int status);
    }
}
