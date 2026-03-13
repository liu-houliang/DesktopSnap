using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using System;

namespace DesktopSnap
{
    public static class WindowExtensions
    {
        public static void Hide(this Window window)
        {
            var appWindow = GetAppWindow(window);
            appWindow?.Hide();
        }

        public static void Show(this Window window)
        {
            var appWindow = GetAppWindow(window);
            appWindow?.Show();
        }

        private static AppWindow GetAppWindow(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero) return null;
            
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }
    }
}
