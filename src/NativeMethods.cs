using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexVoiceKey
{
    internal static class NativeMethods
    {
        internal static readonly UIntPtr InjectedMarker = unchecked((UIntPtr)0x434F444558564F49UL);
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint key, scan, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public int x, y; public uint mouseData, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public KeyboardInput keyboard; [FieldOffset(0)] public MouseInput mouse; }
        [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
        {
            public uint size, flags;
            public IntPtr active, focus, capture, menuOwner, moveSize, caret;
            public int left, top, right, bottom;
        }

        internal static IntPtr FocusedControl(IntPtr window)
        {
            uint process;
            uint thread = GetWindowThreadProcessId(window, out process);
            GuiThreadInfo info = new GuiThreadInfo();
            info.size = (uint)Marshal.SizeOf(typeof(GuiThreadInfo));
            return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.focus : IntPtr.Zero;
        }

        internal static bool ModifiersDown()
        {
            return GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x10) < 0 ||
                GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
        }

        private static Input MakeKey(int key, bool down)
        {
            Input input = new Input();
            input.type = 1;
            input.data.keyboard.key = (ushort)key;
            input.data.keyboard.flags = down ? 0u : 2u;
            input.data.keyboard.extra = InjectedMarker;
            return input;
        }
        internal static void SendKey(int key, bool down) { Send(new Input[] { MakeKey(key, down) }); }
        internal static void SendPaste() { Send(new Input[] { MakeKey(0xA2, true), MakeKey(0x56, true), MakeKey(0x56, false), MakeKey(0xA2, false) }); }
        private static void Send(Input[] inputs)
        {
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected input injection. Match the target application's privilege level.");
        }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
    }
}
