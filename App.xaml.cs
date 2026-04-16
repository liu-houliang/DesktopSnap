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
            // --- Single Instance Redirection ---
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("DesktopSnap_SingleInstance");
            if (!mainInstance.IsCurrent)
            {
                // Redirect the activation (like a toast click) to the existing instance
                var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                await mainInstance.RedirectActivationToAsync(activatedArgs);
                
                // Exit this duplicate process gracefully
                Environment.Exit(0);
                return;
            }

            // Register for future activation redirections (if we are the main instance)
            mainInstance.Activated += (sender, e) =>
            {
                MainWindow.Instance?.ShowAndRestore();
            };

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

            // Check if launched with --silent (e.g. auto-start on boot)
            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool isSilent = cmdArgs.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

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
