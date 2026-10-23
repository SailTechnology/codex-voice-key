using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CodexVoiceKey
{
    [Flags]
    internal enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }
    internal enum BindingKind { Keyboard, Mouse, Copilot }

    internal sealed class HotkeyBinding
    {
        public BindingKind Kind;
        public int Key;
        public KeyModifiers Modifiers;
        public string Label;

        public static HotkeyBinding Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("Hotkey cannot be empty.");
            string clean = text.Replace(" ", "");
            if (clean.Equals("Copilot", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding { Kind = BindingKind.Copilot, Key = 0x86, Label = "Copilot" };
            string[] parts = clean.Split('+');
            KeyModifiers modifiers = KeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                KeyModifiers value;
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": value = KeyModifiers.Ctrl; break;
                    case "alt": value = KeyModifiers.Alt; break;
                    case "shift": value = KeyModifiers.Shift; break;
                    case "win": case "windows": value = KeyModifiers.Win; break;
                    default: throw new ArgumentException("Unknown modifier: " + parts[i]);
                }
                if ((modifiers & value) != 0) throw new ArgumentException("Duplicate hotkey modifier.");
                modifiers |= value;
            }
            string keyName = parts[parts.Length - 1];
            int key;
            BindingKind kind = BindingKind.Keyboard;
            switch (keyName.ToLowerInvariant())
            {
                case "mousemiddle": case "mbutton": key = 3; kind = BindingKind.Mouse; break;
                case "mousex1": case "xbutton1": key = 5; kind = BindingKind.Mouse; break;
                case "mousex2": case "xbutton2": key = 6; kind = BindingKind.Mouse; break;
                default:
                    Keys parsed;
                    if (keyName.Length == 1 && Char.IsDigit(keyName[0])) keyName = "D" + keyName;
                    if (!Enum.TryParse<Keys>(keyName, true, out parsed)) throw new ArgumentException("Unknown key: " + keyName);
                    key = (int)parsed;
                    if (key < 8 || key > 0xFE || (parsed & Keys.Modifiers) != 0)
                        throw new ArgumentException("Choose a keyboard key, MouseMiddle, MouseX1, MouseX2 or Copilot.");
                    if (key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5B || key == 0x5C || (key >= 0xA0 && key <= 0xA5))
                        throw new ArgumentException("A modifier alone cannot be the voice key. Use e.g. Ctrl+B or F8.");
                    break;
            }
            return new HotkeyBinding { Kind = kind, Key = key, Modifiers = modifiers, Label = clean };
        }
    }

    internal sealed class KeyStroke
    {
        public int Key;
        public bool Down;
        public KeyStroke(int key, bool down) { Key = key; Down = down; }
    }

    internal sealed class InputDecision
    {
        public bool Suppress, Start, Stop, MaskMenu;
        public readonly List<KeyStroke> Forward = new List<KeyStroke>();
    }

    // Pure input decisions allow sequence tests without injecting keys into Windows.
    internal sealed class HoldKeyState
    {
        private readonly HotkeyBinding binding;
        public bool Active { get; private set; }
        public HoldKeyState(HotkeyBinding binding) { this.binding = binding; }

        public InputDecision Process(BindingKind kind, int key, bool down, KeyModifiers modifiers, bool targetForeground)
        {
            InputDecision result = new InputDecision();
            if (binding.Kind != kind || key != binding.Key) return result;
            if (Active)
            {
                result.Suppress = true;
                if (!down) { Active = false; result.Stop = true; }
                return result;
            }
            if (down && targetForeground && modifiers == binding.Modifiers)
            {
                Active = true;
                result.Suppress = result.Start = true;
                result.MaskMenu = (modifiers & KeyModifiers.Win) != 0;
            }
            return result;
        }
    }

    internal sealed class CopilotKeyState
    {
        private int pendingWin;
        private readonly HashSet<int> pendingShifts = new HashSet<int>();
        private readonly HashSet<int> consumedModifiers = new HashSet<int>();
        public bool Active { get; private set; }
        public bool Pending { get { return pendingWin != 0 || consumedModifiers.Count != 0; } }
        private static bool IsWin(int key) { return key == 0x5B || key == 0x5C; }
        private static bool IsShift(int key) { return key == 0xA0 || key == 0xA1 || key == 0x10; }

        public InputDecision Process(int key, bool down, bool targetForeground)
        {
            InputDecision result = new InputDecision();
            if (Active)
            {
                if (key == 0x86)
                {
                    result.Suppress = true;
                    if (!down) { Active = false; result.Stop = true; }
                    return result;
                }
                if (IsWin(key) || consumedModifiers.Contains(key))
                {
                    if (down) consumedModifiers.Add(key); else consumedModifiers.Remove(key);
                    result.Suppress = true;
                }
                return result;
            }
            if (consumedModifiers.Contains(key))
            {
                if (!down) consumedModifiers.Remove(key);
                result.Suppress = true;
                return result;
            }
            if (pendingWin != 0)
            {
                if (key == 0x86 && down)
                {
                    Active = true;
                    consumedModifiers.Add(pendingWin);
                    foreach (int shift in pendingShifts) consumedModifiers.Add(shift);
                    pendingWin = 0;
                    pendingShifts.Clear();
                    result.Suppress = true;
                    result.Start = targetForeground;
                    return result;
                }
                if (IsShift(key) && down)
                {
                    pendingShifts.Add(key);
                    result.Suppress = true;
                    return result;
                }
                if (key == pendingWin && down) { result.Suppress = true; return result; }
                // Replay the whole transaction in order, including the current
                // event, so an injected Windows key can never arrive after its up.
                result.Forward.Add(new KeyStroke(pendingWin, true));
                foreach (int shift in pendingShifts) result.Forward.Add(new KeyStroke(shift, true));
                result.Forward.Add(new KeyStroke(key, down));
                pendingWin = 0;
                pendingShifts.Clear();
                result.Suppress = true;
                return result;
            }
            if (IsWin(key) && down)
            {
                pendingWin = key;
                result.Suppress = true;
            }
            else if (key == 0x86)
            {
                result.Suppress = true;
                if (down) { Active = true; result.Start = targetForeground; result.MaskMenu = true; }
            }
            return result;
        }
    }
}
