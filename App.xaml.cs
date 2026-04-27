using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DesktopSnap
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Check if launched with --silent (e.g. auto-start on boot)
            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool isSilent = cmdArgs.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

            // Also check for MSIX StartupTask activation (for packaged version)
            var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask)
            {
                isSilent = true;
            }

            // --- Single Instance Redirection ---
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("DesktopSnap_SingleInstance");
            bool isActingMainInstance = mainInstance.IsCurrent;
            if (!isActingMainInstance)
            {
                // When auto-starting from registry, there might be edge cases where
                // AppInstance detects a "ghost" instance that actually crashed.
                // For --silent mode, verify the existing instance is actually responsive.
                if (isSilent)
                {
                    // Check if there's a real process running with our executable name
                    var currentProcess = Process.GetCurrentProcess();
                    var processes = Process.GetProcessesByName(currentProcess.ProcessName);
                    int otherInstanceCount = processes.Count(p => p.Id != currentProcess.Id);
                    
                    // If no other real process found, this is a ghost instance registration.
                    // Continue launching as the main instance.
                    if (otherInstanceCount == 0)
                    {
                        Debug.WriteLine("Auto-start detected ghost instance, continuing as main instance...");
                        isActingMainInstance = true;
                        // Continue with initialization below (skip the exit)
                    }
                    else
                    {
                        // Real duplicate instance, redirect and exit
                        try
                        {
                            activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                            await mainInstance.RedirectActivationToAsync(activatedArgs);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RedirectActivation failed during auto-start: {ex.Message}");
                        }
                        Environment.Exit(0);
                        return;
                    }
                }
                else
                {
                    // Normal launch (not auto-start): redirect to existing instance
                    activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                    await mainInstance.RedirectActivationToAsync(activatedArgs);
                    
                    // Exit this duplicate process gracefully
                    Environment.Exit(0);
                    return;
                }
            }

            // Register for future activation redirections (if we are the main instance)
            if (isActingMainInstance)
            {
                Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().Activated += (sender, e) =>
                {
                    MainWindow.Instance?.ShowAndRestore();
                };
            }

            // Set working directory to the app's base directory to ensure relative paths work.
            // This is important when launched from Registry as Auto-Start.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            try {
                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.NotificationInvoked += (sender, e) =>
                {
                    MainWindow.Instance?.ShowAndRestore();
                };
                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
            } catch { }

            // isSilent is already checked at the beginning of OnLaunched
            m_window = new MainWindow(isSilentStart: isSilent);

            // Always activate so the HWND message-loop and tray icon are fully initialized.
            // When silent, MainWindow has already parked itself at -32000,-32000 (off-screen),
            // so Activate() is invisible. We then immediately hide via AppWindow.
            m_window.Activate();
            if (isSilent)
            {
                m_window.Hide(); // AppWindow.Hide() — removes from taskbar, keeps tray icon alive
            }
        }

        private Window m_window;
    }
}
