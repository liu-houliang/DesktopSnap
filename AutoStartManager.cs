using System;
using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;

namespace DesktopSnap
{
    public static class AutoStartManager
    {
        private const string AppName = "DesktopSnap";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppName);
                        return val != null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking AutoStart: {ex.Message}");
            }
            return false;
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            // Use Environment.ProcessPath (reliable in .NET 6+) instead of
                            // Process.GetCurrentProcess().MainModule.FileName which can
                            // return the dotnet host path in some scenarios.
                            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;
                            // Enclose path in quotes to handle spaces; add --silent for tray-only startup
                            key.SetValue(AppName, $"\"{exePath}\" --silent");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error toggling AutoStart: {ex.Message}");
            }
        }
    }
}
