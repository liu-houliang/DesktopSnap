using System;
using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace DesktopSnap
{
    public static class AutoStartManager
    {
        private const string AppName = "DesktopSnap";



        public static async Task<bool> IsAutoStartEnabledAsync()
        {
            if (AppEnv.IsPackaged)
            {
                try
                {
                    var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("DesktopSnapAutoStart");
                    return startupTask.State == Windows.ApplicationModel.StartupTaskState.Enabled;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking MSIX AutoStart: {ex.Message}");
                    return false;
                }
            }
            else
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
        }

        public static async Task<bool> SetAutoStartAsync(bool enable)
        {
            if (AppEnv.IsPackaged)
            {
                try
                {
                    var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("DesktopSnapAutoStart");
                    if (enable)
                    {
                        var result = await startupTask.RequestEnableAsync();
                        if (result != Windows.ApplicationModel.StartupTaskState.Enabled && result != Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
                        {
                            Debug.WriteLine($"Failed to enable MSIX AutoStart. State: {result}");
                            return false;
                        }
                    }
                    else
                    {
                        startupTask.Disable();
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error toggling MSIX AutoStart: {ex.Message}");
                    return false;
                }
            }
            else
            {
                try
                {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            // Environment.ProcessPath is the most reliable way to get the current executable in .NET 6+
                            string exePath = Environment.ProcessPath;
                            
                            // Fallback if ProcessPath is null
                            if (string.IsNullOrEmpty(exePath))
                            {
                                exePath = Process.GetCurrentProcess().MainModule?.FileName;
                            }

                            if (string.IsNullOrEmpty(exePath))
                            {
                                exePath = Path.Combine(AppContext.BaseDirectory, "DesktopSnap.exe");
                            }

                            // Enclose path in quotes to handle spaces; add --silent for tray-only startup
                            key.SetValue(AppName, $"\"{exePath}\" --silent");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
                {
                    Debug.WriteLine($"Error toggling AutoStart: {ex.Message}");
                    return false;
                }
            }
        }
    }
}
