using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexVoiceKey
{
    internal static class InputHooks
    {
        private static NativeMethods.HookProc keyboardProc, mouseProc;
        private static IntPtr keyboardHook, mouseHook;
        private static HoldKeyState hold;
        private static CopilotKeyState copilot;
        private static Timer refreshTimer;
        private static HotkeyBinding binding;

        public static void Start(HotkeyBinding requested)
        {
            binding = requested;
            hold = new HoldKeyState(binding);
            copilot = new CopilotKeyState();
            keyboardProc = KeyboardCallback;
            mouseProc = MouseCallback;
            Install();
            refreshTimer = new Timer();
            refreshTimer.Interval = 10000;
            refreshTimer.Tick += delegate
            {
                if (!hold.Active && !copilot.Active && !copilot.Pending)
                {
                    try { Install(); }
                    catch (Exception error) { Program.Log("hook refresh failed: " + error.Message); }
                }
            };
            refreshTimer.Start();
            Application.ApplicationExit += delegate { Stop(); };
        }

        private static void Install()
        {
            IntPtr nextKeyboard = IntPtr.Zero, nextMouse = IntPtr.Zero;
            if (binding.Kind == BindingKind.Mouse)
                nextMouse = NativeMethods.SetWindowsHookEx(14, mouseProc, NativeMethods.GetModuleHandle(null), 0);
            else
                nextKeyboard = NativeMethods.SetWindowsHookEx(13, keyboardProc, NativeMethods.GetModuleHandle(null), 0);
            if (nextKeyboard == IntPtr.Zero && nextMouse == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr oldKeyboard = keyboardHook, oldMouse = mouseHook;
            keyboardHook = nextKeyboard; mouseHook = nextMouse;
            if (oldKeyboard != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(oldKeyboard);
            if (oldMouse != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(oldMouse);
        }

        public static void Stop()
        {
            if (refreshTimer != null) { refreshTimer.Stop(); refreshTimer.Dispose(); refreshTimer = null; }
            if (keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(mouseHook);
            keyboardHook = mouseHook = IntPtr.Zero;
        }

        private static IntPtr KeyboardCallback(int code, IntPtr message, IntPtr pointer)
        {
            if (code < 0) return NativeMethods.CallNextHookEx(keyboardHook, code, message, pointer);
            NativeMethods.KeyboardData data = (NativeMethods.KeyboardData)Marshal.PtrToStructure(pointer, typeof(NativeMethods.KeyboardData));
            if (data.extra == NativeMethods.InjectedMarker)
                return NativeMethods.CallNextHookEx(keyboardHook, code, message, pointer);
            int kind = message.ToInt32();
            bool down = kind == 0x100 || kind == 0x104;
            if (!down && kind != 0x101 && kind != 0x105) return NativeMethods.CallNextHookEx(keyboardHook, code, message, pointer);
            try
            {
                InputDecision decision = binding.Kind == BindingKind.Copilot
                    ? copilot.Process((int)data.key, down, Program.IsCodexForeground())
                    : hold.Process(BindingKind.Keyboard, (int)data.key, down, ReadModifiers(), Program.IsCodexForeground());
                if (Apply(decision)) return new IntPtr(1);
            }
            catch (Exception error) { Program.Log("keyboard hook failed: " + error.Message); }
            return NativeMethods.CallNextHookEx(keyboardHook, code, message, pointer);
        }

        private static IntPtr MouseCallback(int code, IntPtr message, IntPtr pointer)
        {
            if (code < 0) return NativeMethods.CallNextHookEx(mouseHook, code, message, pointer);
            NativeMethods.MouseData data = (NativeMethods.MouseData)Marshal.PtrToStructure(pointer, typeof(NativeMethods.MouseData));
            if (data.extra == NativeMethods.InjectedMarker) return NativeMethods.CallNextHookEx(mouseHook, code, message, pointer);
            int kind = message.ToInt32(), key;
            bool down;
            if (kind == 0x207 || kind == 0x208) { key = 3; down = kind == 0x207; }
            else if (kind == 0x20B || kind == 0x20C) { key = ((data.mouseData >> 16) & 0xffff) == 1 ? 5 : 6; down = kind == 0x20B; }
            else return NativeMethods.CallNextHookEx(mouseHook, code, message, pointer);
            try
            {
                if (Apply(hold.Process(BindingKind.Mouse, key, down, ReadModifiers(), Program.IsCodexForeground()))) return new IntPtr(1);
            }
            catch (Exception error) { Program.Log("mouse hook failed: " + error.Message); }
            return NativeMethods.CallNextHookEx(mouseHook, code, message, pointer);
        }

        private static bool Apply(InputDecision decision)
        {
            foreach (KeyStroke key in decision.Forward) NativeMethods.SendKey(key.Key, key.Down);
            if (decision.MaskMenu) { NativeMethods.SendKey(0xE8, true); NativeMethods.SendKey(0xE8, false); }
            if (decision.Start) Program.BeginVoiceCapture(binding.Label);
            if (decision.Stop) Program.EndVoiceCapture("key release");
            return decision.Suppress;
        }

        private static KeyModifiers ReadModifiers()
        {
            KeyModifiers modifiers = KeyModifiers.None;
            if (NativeMethods.GetAsyncKeyState(0x11) < 0) modifiers |= KeyModifiers.Ctrl;
            if (NativeMethods.GetAsyncKeyState(0x12) < 0) modifiers |= KeyModifiers.Alt;
            if (NativeMethods.GetAsyncKeyState(0x10) < 0) modifiers |= KeyModifiers.Shift;
            if (NativeMethods.GetAsyncKeyState(0x5B) < 0 || NativeMethods.GetAsyncKeyState(0x5C) < 0) modifiers |= KeyModifiers.Win;
            return modifiers;
        }
    }
}
