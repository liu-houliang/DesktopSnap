using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DesktopSnap
{
    public class DisplayInfo
    {
        public string DeviceName { get; set; } // e.g. \\.\DISPLAY1
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public uint Dpi { get; set; } = 96; // Default 100%
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public static List<DisplayInfo> GetDisplays()
        {
            var list = new List<DisplayInfo>();
            
            // Keep the delegate rooted so GC doesn't collect it during native call
            EnumMonitorsDelegate callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                uint dpiX = 96, dpiY = 96;
                try
                {
                    // MDT_EFFECTIVE_DPI = 0
                    GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                }
                catch { /* Fallback to 96 if shcore.dll is missing or on older Win */ }

                string deviceName = "";
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    deviceName = mi.szDevice;
                }

                list.Add(new DisplayInfo
                {
                    DeviceName = deviceName,
                    Left = lprcMonitor.left,
                    Top = lprcMonitor.top,
                    Right = lprcMonitor.right,
                    Bottom = lprcMonitor.bottom,
                    Dpi = dpiX
                });
                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            
            GC.KeepAlive(callback); // Ensure it survives the native call
            return list;
        }
    }
}
