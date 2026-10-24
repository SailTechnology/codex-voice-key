using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CodexVoiceKey
{
    internal interface IAudioOutputs
    {
        Dictionary<string, bool> SnapshotActive();
        void SetMute(string id, bool muted);
    }

    // Core Audio endpoint controls: output devices only, never microphone gain
    // or mute. SetMute preserves both master and per-channel volume levels.
    internal sealed class WindowsAudioOutputs : IAudioOutputs
    {
        private static readonly Guid VolumeInterface = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static Guid EventContext = new Guid("D0BE9A1A-F21D-46B1-B71B-8CA70BB32F42");

        public Dictionary<string, bool> SnapshotActive()
        {
            Dictionary<string, bool> result = new Dictionary<string, bool>();
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            IMMDeviceCollection devices = null;
            try
            {
                Check(enumerator.EnumAudioEndpoints(0, 1, out devices)); // eRender, DEVICE_STATE_ACTIVE
                uint count; Check(devices.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    object volume = null;
                    try
                    {
                        Check(devices.Item(i, out device));
                        string id; Check(device.GetId(out id));
                        Guid iid = VolumeInterface;
                        Check(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
                        bool muted; Check(((IAudioEndpointVolume)volume).GetMute(out muted));
                        result[id] = muted;
                    }
                    finally { Release(volume); Release(device); }
                }
                return result;
            }
            finally { Release(devices); Release(enumerator); }
        }

        public void SetMute(string id, bool muted)
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            IMMDevice device = null;
            object volume = null;
            try
            {
                Check(enumerator.GetDevice(id, out device));
                Guid iid = VolumeInterface;
                Check(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
                Check(((IAudioEndpointVolume)volume).SetMute(muted, ref EventContext));
            }
            finally { Release(volume); Release(device); Release(enumerator); }
        }

        private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
            [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
    }
}
