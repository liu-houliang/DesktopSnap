using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DesktopSnap
{
    public class DisplayInfo
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

        delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        public static List<DisplayInfo> GetDisplays()
        {
            var list = new List<DisplayInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                {
                    list.Add(new DisplayInfo
                    {
                        Left = lprcMonitor.left,
                        Top = lprcMonitor.top,
                        Right = lprcMonitor.right,
                        Bottom = lprcMonitor.bottom
                    });
                    return true;
                }, IntPtr.Zero);
            return list;
        }
    }
}
