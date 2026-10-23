using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexVoiceKey
{
    internal sealed class WaveInRecorder : IDisposable
    {
        private const int WaveMapper = -1;
        private const int WaveFormatPcm = 1;
        private const int CallbackFunction = 0x00030000;
        private const int WimData = 0x3C0;
        private const int WhdrDone = 0x00000001;
        private const int WhdrPrepared = 0x00000002;

        private readonly object sync = new object();
        private readonly MemoryStream rawAudio = new MemoryStream();
        private readonly List<IntPtr> bufferPointers = new List<IntPtr>();
        private readonly List<IntPtr> headerPointers = new List<IntPtr>();
        private readonly WaveInProc callback;

        private IntPtr waveHandle = IntPtr.Zero;
        private WaveFormat format;
        private volatile bool stopping;
        private bool started;
        private bool disposed;

        public string DeviceName { get; private set; }
        public string OutputPath { get; private set; }

        public WaveInRecorder(string outputPath)
        {
            OutputPath = outputPath;
            callback = OnWaveInData;
        }

        public void Start()
        {
            if (started)
            {
                throw new InvalidOperationException("Recorder is already started.");
            }

            int deviceId;
            DeviceName = SelectCaptureDevice(out deviceId);
            int error = 1;
            int[,] candidates = { { 16000, 1 }, { 48000, 1 }, { 44100, 1 }, { 48000, 2 }, { 44100, 2 }, { 48000, 4 } };
            for (int candidate = 0; candidate < candidates.GetLength(0); candidate++)
            {
                int rate = candidates[candidate, 0], channels = candidates[candidate, 1];
                format = new WaveFormat {
                    wFormatTag = WaveFormatPcm, nChannels = (ushort)channels, nSamplesPerSec = (uint)rate,
                    nAvgBytesPerSec = (uint)(rate * channels * 2), nBlockAlign = (ushort)(channels * 2), wBitsPerSample = 16
                };
                error = waveInOpen(out waveHandle, deviceId, ref format, callback, UIntPtr.Zero, CallbackFunction);
                if (error == 0) break;
            }
            if (error != 0) throw new InvalidOperationException("waveInOpen failed: " + error + " for " + DeviceName);

            const int bufferCount = 8;
            int bufferBytes = (int)format.nAvgBytesPerSec / 10;
            for (int i = 0; i < bufferCount; i++)
            {
                IntPtr data = Marshal.AllocHGlobal(bufferBytes);
                IntPtr header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                Marshal.StructureToPtr(new WaveHeader
                {
                    lpData = data,
                    dwBufferLength = (uint)bufferBytes
                }, header, false);

                bufferPointers.Add(data);
                headerPointers.Add(header);

                error = waveInPrepareHeader(waveHandle, header, Marshal.SizeOf(typeof(WaveHeader)));
                if (error != 0)
                {
                    throw new InvalidOperationException("waveInPrepareHeader failed: " + error);
                }
                error = waveInAddBuffer(waveHandle, header, Marshal.SizeOf(typeof(WaveHeader)));
                if (error != 0)
                {
                    throw new InvalidOperationException("waveInAddBuffer failed: " + error);
                }
            }

            error = waveInStart(waveHandle);
            if (error != 0)
            {
                throw new InvalidOperationException("waveInStart failed: " + error);
            }
            started = true;
        }

        public void Stop()
        {
            if (waveHandle == IntPtr.Zero || stopping)
            {
                return;
            }
            stopping = true;

            waveInStop(waveHandle);
            waveInReset(waveHandle);
            Thread.Sleep(180);

            foreach (IntPtr header in headerPointers)
            {
                WaveHeader data = (WaveHeader)Marshal.PtrToStructure(header, typeof(WaveHeader));
                if ((data.dwFlags & WhdrPrepared) != 0)
                {
                    waveInUnprepareHeader(waveHandle, header, Marshal.SizeOf(typeof(WaveHeader)));
                }
            }

            waveInClose(waveHandle);
            waveHandle = IntPtr.Zero;
            started = false;

            foreach (IntPtr header in headerPointers)
            {
                Marshal.FreeHGlobal(header);
            }
            foreach (IntPtr buffer in bufferPointers)
            {
                Marshal.FreeHGlobal(buffer);
            }
            headerPointers.Clear();
            bufferPointers.Clear();

            WriteWaveFile();
        }

        private void OnWaveInData(IntPtr device, int message, UIntPtr instance, IntPtr headerPointer, UIntPtr reserved)
        {
            if (message != WimData || headerPointer == IntPtr.Zero)
            {
                return;
            }

            WaveHeader header = (WaveHeader)Marshal.PtrToStructure(headerPointer, typeof(WaveHeader));
            if (header.dwBytesRecorded > 0 && header.lpData != IntPtr.Zero)
            {
                byte[] chunk = new byte[header.dwBytesRecorded];
                Marshal.Copy(header.lpData, chunk, 0, chunk.Length);
                lock (sync)
                {
                    rawAudio.Write(chunk, 0, chunk.Length);
                }
            }

            if (!stopping && waveHandle != IntPtr.Zero)
            {
                int error = waveInAddBuffer(waveHandle, headerPointer, Marshal.SizeOf(typeof(WaveHeader)));
                if (error != 0)
                {
                    Program.Log("waveInAddBuffer refill failed: " + error);
                }
            }
        }

        private void WriteWaveFile()
        {
            byte[] source;
            lock (sync)
            {
                source = rawAudio.ToArray();
            }

            PcmWave.Write(OutputPath, source, (int)format.nSamplesPerSec, format.nChannels);
        }

        private static string SelectCaptureDevice(out int deviceId)
        {
            int count = waveInGetNumDevs();
            string preferred = Program.Settings.Microphone;
            if (String.IsNullOrWhiteSpace(preferred) || preferred.Equals("default", StringComparison.OrdinalIgnoreCase))
            { deviceId = WaveMapper; return "System default microphone"; }
            for (int i = 0; i < count; i++)
            {
                WaveInCaps caps;
                if (waveInGetDevCaps(i, out caps, Marshal.SizeOf(typeof(WaveInCaps))) != 0)
                {
                    continue;
                }
                if (caps.szPname.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0 || preferred == i.ToString())
                {
                    deviceId = i;
                    return caps.szPname;
                }
            }
            throw new InvalidOperationException("Configured microphone not found: " + preferred);
        }

        public static string[] ListDevices()
        {
            List<string> devices = new List<string>();
            for (int i = 0; i < waveInGetNumDevs(); i++)
            {
                WaveInCaps caps;
                if (waveInGetDevCaps(i, out caps, Marshal.SizeOf(typeof(WaveInCaps))) == 0) devices.Add(i + ": " + caps.szPname);
            }
            return devices.ToArray();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { Stop(); } catch { }
            rawAudio.Dispose();
        }

        private delegate void WaveInProc(IntPtr device, int message, UIntPtr instance, IntPtr param1, UIntPtr param2);

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormat
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WaveInCaps
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int waveInOpen(
            out IntPtr device,
            int deviceId,
            ref WaveFormat format,
            WaveInProc callback,
            UIntPtr instance,
            int flags);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInStop(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int waveInGetDevCaps(int deviceId, out WaveInCaps caps, int size);
    }
}
