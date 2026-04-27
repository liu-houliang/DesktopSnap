using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;

namespace DesktopSnap
{
    public static class HotkeyManager
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        private static int _currentId = 1;
        private static Dictionary<int, Action> _hotkeyActions = new Dictionary<int, Action>();

        public static int Register(IntPtr hWnd, string hotKeyString, Action action)
        {
            if (string.IsNullOrWhiteSpace(hotKeyString)) return -1;

            uint modifiers = 0;
            uint key = 0;

            var parts = hotKeyString.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string p = part.Trim().ToUpperInvariant();
                if (p == "CTRL" || p == "CONTROL") modifiers |= MOD_CONTROL;
                else if (p == "ALT") modifiers |= MOD_ALT;
                else if (p == "SHIFT") modifiers |= MOD_SHIFT;
                else if (p == "WIN" || p == "WINDOWS") modifiers |= MOD_WIN;
                else
                {
                    if (Enum.TryParse<ConsoleKey>(p, true, out var consoleKey))
                    {
                        key = (uint)consoleKey;
                    }
                    else if (p.Length == 1)
                    {
                        key = (uint)p[0];
                    }
                }
            }

            if (key == 0) return -1;

            int id = _currentId++;
            if (RegisterHotKey(hWnd, id, modifiers, key))
            {
                _hotkeyActions[id] = action;
                return id;
            }
            return -1;
        }

        public static void Unregister(IntPtr hWnd, int id)
        {
            if (_hotkeyActions.ContainsKey(id))
            {
                UnregisterHotKey(hWnd, id);
                _hotkeyActions.Remove(id);
            }
        }

        public static void HandleMessage(int id)
        {
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action?.Invoke();
            }
        }
    }
}
