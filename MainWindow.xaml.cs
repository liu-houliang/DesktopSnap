using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using H.NotifyIcon;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DesktopSnap
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern void FlushMenuThemes();

        public I18n Lang => I18n.Instance;

        // Icon thumbnail cache: file path -> DesktopIconCacheEntry
        private class DesktopIconCacheEntry : IDisposable
        {
            public BitmapImage Image { get; set; }
            public Windows.Storage.Streams.InMemoryRandomAccessStream Stream { get; set; }

            public void Dispose()
            {
                Stream?.Dispose();
                Stream = null;
                Image = null;
            }
        }

        private static readonly ConcurrentDictionary<string, DesktopIconCacheEntry> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _iconLoadSemaphore = new(20, 20); // Max 20 concurrent loads
        private int _selectedDisplayIndex = -1; // -1 for all displays
        
        private int _saveHotkeyId = -1;
        private DispatcherTimer _statusTimer;

        public MainWindow(bool isSilentStart = false)
        {
            Instance = this;
            try {
                SetPreferredAppMode(2); // 2 = ForceDark
                FlushMenuThemes();
            } catch { }

            SettingsManager.ApplySettings();
            this.InitializeComponent();

            this.Title = I18n.Instance.AppTitle;
            ExtendsContentIntoTitleBar = true;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            // Set up WndProc hook for hotkeys
            _wndProcDelegate = CustomWndProc;
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            // Register close event to minimize to tray instead
            appWindow.Closing += AppWindow_Closing;

            // Use DisplayArea for physical pixels (90% of work area)
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            int physWidth = displayArea.WorkArea.Width;
            int physHeight = displayArea.WorkArea.Height;

            int width = (int)(physWidth * 0.90); 
            int height = (int)(physHeight * 0.90);
            
            // Allow larger sizes for 2K/4K
            width = Math.Max(1200, Math.Min(width, 3400));
            height = Math.Max(850, Math.Min(height, 2000));

            // Precise physical centering
            int x = (physWidth - width) / 2 + displayArea.WorkArea.X;
            int y = (physHeight - height) / 2 + displayArea.WorkArea.Y;

            // Silent-start: position the window far off-screen so Activate() is invisible.
            // MoveAndResize is the LAST positioning call, so this won't be overridden.
            if (isSilentStart)
            {
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(-32000, -32000, width, height));
            }
            else
            {
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            }
            
            try { appWindow.SetIcon("Assets\\app.ico"); } catch { }

            // Pre-cache system icon images on the main STA thread.
            // Shell COM interfaces (IShellFolder) require STA and fail silently on thread pool (MTA) threads,
            // which causes system icons to not appear on first load.
            IconExtractor.InitSystemIcons();

            var settings = SettingsManager.Load();
            var lang = settings.Language;
            foreach (ComboBoxItem item in LangCombo.Items)
            {
                if (item.Tag?.ToString() == lang)
                {
                    LangCombo.SelectedItem = item;
                    break;
                }
            }
            AutoStartToggle.IsOn = settings.AutoStart;
            TrayAutoStartToggle.IsChecked = settings.AutoStart;
            AutoSaveOnDisplayChangeToggle.IsOn = settings.AutoSaveOnDisplayChange;
            AutoUpdateToggle.IsOn = settings.EnableAutoUpdate;
            
            // Sync internal settings with actual system auto-start status
            _ = SyncAutoStartWithSystemAsync();

            // ONLY auto-check updates for portable (non-packaged) version.
            // Store version is handled by Microsoft Store automatically.
            if (!AppEnv.IsPackaged && settings.EnableAutoUpdate && !isSilentStart)
            {
                _ = PerformUpdateCheckAsync(true);
            }

            if (AppEnv.IsPackaged)
            {
                if (AutoUpdateSettingRow != null) AutoUpdateSettingRow.Visibility = Visibility.Collapsed;
                if (AutoUpdateSeparator != null) AutoUpdateSeparator.Visibility = Visibility.Collapsed;
                if (CheckUpdateText != null) CheckUpdateText.Text = Lang.OpenStore;
            }

            LayoutManager.AutoSaveTemporary();
            RefreshLayoutsList();
            
            RegisterHotkeys(settings, hwnd);
            
            if (!isSilentStart)
            {
                CheckFirstRun();
            }
        }

        private async Task SyncAutoStartWithSystemAsync()
        {
            var settings = SettingsManager.Load();
            bool actualSystemState = await AutoStartManager.IsAutoStartEnabledAsync();
            
            if (settings.AutoStart != actualSystemState)
            {
                settings.AutoStart = actualSystemState;
                SettingsManager.Save(settings);
                
                // Update UI visually
                if (AutoStartToggle != null) AutoStartToggle.IsOn = actualSystemState;
                if (TrayAutoStartToggle != null) TrayAutoStartToggle.IsChecked = actualSystemState;
            }
            else
            {
                // If they match, still forcefully sync Registry on boot to match the default or saved setting.
                // This ensures correct executable path and cleans out any stuck true references if it was false.
                _ = AutoStartManager.SetAutoStartAsync(settings.AutoStart);
            }
        }

        private async void CheckFirstRun()
        {
            var settings = SettingsManager.Load();
            if (settings.IsFirstRun)
            {
                // Give a small delay to ensure the window is rendered
                await Task.Delay(500);
                WelcomeDialog.XamlRoot = this.Content.XamlRoot;
                await WelcomeDialog.ShowAsync();
            }
        }

        private void WelcomeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var settings = SettingsManager.Load();
            settings.IsFirstRun = false;
            SettingsManager.Save(settings);
        }

        private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SettingsDialog.XamlRoot = this.Content.XamlRoot;
            await SettingsDialog.ShowAsync();
        }

        private async void AboutBtn_Click(object sender, RoutedEventArgs e)
        {
            // Set language-aware URLs at open time so they reflect current language selection
            HomepageLink.NavigateUri = new Uri(Lang.HomepageUrl);
            WebsiteLink.NavigateUri = new Uri(Lang.WebsiteUrl);
            GitHubLink.NavigateUri = new Uri(Lang.GitHubUrl);

            AboutDialog.XamlRoot = this.Content.XamlRoot;
            await AboutDialog.ShowAsync();
        }

        private void RegisterHotkeys(AppSettings settings, IntPtr hwnd)
        {
            if (_saveHotkeyId != -1) HotkeyManager.Unregister(hwnd, _saveHotkeyId);

            _saveHotkeyId = HotkeyManager.Register(hwnd, settings.SaveHotkey, () => 
            {
                var icons = DesktopIconManager.GetIcons();
                if (icons.Count > 0)
                {
                    var newLayout = new DesktopLayout
                    {
                        Name = $"Hotkey Save {DateTime.Now:MM-dd HH:mm}",
                        Icons = icons,
                        CapturedDisplays = DisplayManager.GetDisplays()
                    };
                    LayoutManager.SaveLayout(newLayout);
                    this.DispatcherQueue.TryEnqueue(() => 
                    {
                        RefreshLayoutsList();
                        ShowToast(I18n.Instance.L("Snapshot saved via hotkey."));
                    });
                }
            });
        }
        
        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            var settings = SettingsManager.Load();
            if (settings.CloseToTray)
            {
                args.Cancel = true;
                this.Hide();
                
                if (!settings.HasShownTrayNotification)
                {
                    ShowToast(I18n.Instance.L("Running in background..."));
                    settings.HasShownTrayNotification = true;
                    SettingsManager.Save(settings);
                }
            }
            else
            {
                // Truly closing
                CleanupResources();
                TrayIcon.Dispose();
            }
        }

        // --- Win32 WndProc Hooking for Hotkeys ---

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate _wndProcDelegate;
        private IntPtr _oldWndProc;

        private const int GWLP_WNDPROC = -4;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private DateTime _lastDisplayChangeAutoSave = DateTime.MinValue;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == HotkeyManager.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                HotkeyManager.HandleMessage(id);
            }
            else if (msg == WM_DISPLAYCHANGE)
            {
                var settings = SettingsManager.Load();
                if (settings.AutoSaveOnDisplayChange)
                {
                    // Debounce: Windows often sends multiple WM_DISPLAYCHANGE messages in a row
                    if ((DateTime.Now - _lastDisplayChangeAutoSave).TotalSeconds > 3)
                    {
                        _lastDisplayChangeAutoSave = DateTime.Now;
                        this.DispatcherQueue.TryEnqueue(() => 
                        {
                            PerformAutoSnapshot(I18n.Instance.DisplayChangeAutoSaveName);
                        });
                    }
                }
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var lang = item.Tag.ToString();
                var settings = SettingsManager.Load();
                if (settings.Language != lang)
                {
                    settings.Language = lang;
                    SettingsManager.Save(settings);
                    SettingsManager.ApplySettings();

                    string selectedId = (LayoutsListView.SelectedItem as DesktopLayout)?.Id;
                    RefreshLayoutsList();

                    if (!string.IsNullOrEmpty(selectedId))
                    {
                        var updatedLayout = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).FirstOrDefault(l => l.Id == selectedId);
                        if (updatedLayout != null)
                        {
                            LayoutsListView.SelectedItem = updatedLayout;
                            DetailNameText.Text = updatedLayout.Name;
                            DetailCountText.Text = $"{Lang.ContainsIconsPrefix}{updatedLayout.Icons.Count}{Lang.ContainsIconsSuffix}";
                            DrawPreview(updatedLayout);
                        }
                    }
                }
            }
        }

        private void RefreshLayoutsList()
        {
            var layouts = LayoutManager.GetAllLayouts();
            LayoutsListView.ItemsSource = layouts;
            
            if (LayoutsListView.SelectedItem == null && layouts.Count > 0)
            {
                // Optionally auto-select first
            }
        }

        private async void ExportPortableBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var savePicker = new FileSavePicker();
                InitializeWithWindow.Initialize(savePicker, hwnd);
                savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
                savePicker.FileTypeChoices.Add("Desktop Layout Snapshot", new List<string>() { ".snap", ".json" });
                savePicker.SuggestedFileName = $"{layout.Name}.snap";

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    try
                    {
                        LayoutManager.ExportLayout(layout, file.Path);
                        ShowStatus(InfoBarSeverity.Success, I18n.Instance.ExportSuccess + ": " + file.Name);
                    }
                    catch (Exception ex)
                    {
                        ShowStatus(InfoBarSeverity.Error, $"Export failed: {ex.Message}");
                    }
                }
            }
        }

        private async void ImportSingle_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var openPicker = new FileOpenPicker();
            InitializeWithWindow.Initialize(openPicker, hwnd);
            openPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            openPicker.FileTypeFilter.Add(".snap");
            openPicker.FileTypeFilter.Add(".json");

            var file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    var (status, imported) = LayoutManager.ImportLayout(file.Path);
                    if (imported != null)
                    {
                        RefreshLayoutsList();
                        var matchedLayout = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).FirstOrDefault(l => l.Id == imported.Id);
                        if (matchedLayout != null) LayoutsListView.SelectedItem = matchedLayout;
                        
                        string msg = I18n.Instance.ImportSuccessSingle;
                        if (status == ImportStatus.Updated) msg = I18n.Instance.ImportUpdated;
                        else if (status == ImportStatus.AsBackup) msg = I18n.Instance.ImportAsBackup;
                        else if (status == ImportStatus.Skipped) msg = I18n.Instance.ImportSkippedIdentical;

                        ShowStatus(InfoBarSeverity.Success, msg + (status != ImportStatus.Skipped ? ": " + imported.Name : ""));
                    }
                    else if (status == ImportStatus.Error)
                    {
                        ShowStatus(InfoBarSeverity.Error, I18n.Instance.L("ImportFailedInvalidFormat"));
                    }
                }
                catch (Exception ex)
                {
                    ShowStatus(InfoBarSeverity.Error, $"Import failed: {ex.Message}");
                }
            }
        }

        private async void ExportAll_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var savePicker = new FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, hwnd);
            savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
            savePicker.FileTypeChoices.Add("Layout Backup Package", new List<string>() { ".zip" });
            savePicker.SuggestedFileName = $"DesktopSnap_Backup_{DateTime.Now:yyyyMMdd}.zip";

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    LayoutManager.ExportAllLayouts(file.Path);
                    ShowStatus(InfoBarSeverity.Success, I18n.Instance.ExportSuccess + ": " + file.Name);
                }
                catch (Exception ex)
                {
                    ShowStatus(InfoBarSeverity.Error, $"Backup failed: {ex.Message}");
                }
            }
        }

        private async void ImportAll_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var openPicker = new FileOpenPicker();
            InitializeWithWindow.Initialize(openPicker, hwnd);
            openPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            openPicker.FileTypeFilter.Add(".zip");

            var file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    int count = LayoutManager.ImportAllLayouts(file.Path);
                    RefreshLayoutsList();
                    ShowStatus(InfoBarSeverity.Success, string.Format(I18n.Instance.ImportSuccess, count));
                }
                catch (Exception ex)
                {
                    ShowStatus(InfoBarSeverity.Error, $"Import failed: {ex.Message}");
                }
            }
        }

        private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = LayoutManager.GetLayoutsDirectory();
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, $"Failed to open folder: {ex.Message}");
            }
        }

        private void LayoutsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                NoSelectionText.Visibility = Visibility.Collapsed;
                DetailPanel.Visibility = Visibility.Visible;
                StatusInfo.IsOpen = false;

                DetailNameText.Text = layout.Name;
                DetailCountText.Text = $"{Lang.ContainsIconsPrefix}{layout.Icons.Count}{Lang.ContainsIconsSuffix}";

                if (layout.Id.StartsWith("auto_") || layout.Id == "temp_auto_save")
                {
                    RenameBtn.Visibility = Visibility.Collapsed;
                    DeleteSnapshotBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    RenameBtn.Visibility = Visibility.Visible;
                    DeleteSnapshotBtn.Visibility = Visibility.Visible;
                }

                CancelRename();
                
                // Resolution mismatch detection
                var currentDisplays = DisplayManager.GetDisplays();
                bool mismatched = false;
                if (layout.CapturedDisplays != null && layout.CapturedDisplays.Count > 0)
                {
                    if (layout.CapturedDisplays.Count != currentDisplays.Count) mismatched = true;
                    else
                    {
                        for (int i = 0; i < currentDisplays.Count; i++)
                        {
                            var cur = currentDisplays[i];
                            
                            // Try to find a display in the backup that matches this one's name
                            var cap = layout.CapturedDisplays.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceName) && d.DeviceName == cur.DeviceName);
                            
                            // Fallback to index if name match fails (e.g. for older snapshots or renamed displays)
                            if (cap == null) cap = layout.CapturedDisplays[i];

                            if (cur.Width != cap.Width || 
                                cur.Height != cap.Height ||
                                (cap.Dpi > 0 && cur.Dpi != cap.Dpi))
                            {
                                mismatched = true;
                                break;
                            }
                        }
                    }
                }
                
                ResolutionWarnBar.IsOpen = mismatched;
                PreviewModePanel.Visibility = mismatched ? Visibility.Visible : Visibility.Collapsed;
                
                DrawPreview(layout);
            }
            else
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                NoSelectionText.Visibility = Visibility.Visible;
            }
        }

        private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoUpdateToggle == null) return;
            var settings = SettingsManager.Load();
            if (settings.EnableAutoUpdate != AutoUpdateToggle.IsOn)
            {
                settings.EnableAutoUpdate = AutoUpdateToggle.IsOn;
                SettingsManager.Save(settings);
            }
        }

        private void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            AboutDialog.Hide();
            if (AppEnv.IsPackaged)
            {
                // Packaged version: Just open the store page directly without checking GitHub
                Process.Start(new ProcessStartInfo(Lang.StoreUrl) { UseShellExecute = true });
            }
            else
            {
                _ = PerformUpdateCheckAsync(false);
            }
        }

        private async Task PerformUpdateCheckAsync(bool isAutoCheck)
        {
            var update = await UpdateManager.CheckForUpdateAsync();
            if (update != null && update.IsNewer)
            {
                await ShowUpdateDialog(update);
            }
            else if (!isAutoCheck)
            {
                ShowStatus(InfoBarSeverity.Success, I18n.Instance.UpdateLatest);
            }
        }

        private async Task ShowUpdateDialog(UpdateInfo update)
        {
            var dialog = new ContentDialog
            {
                Title = I18n.Instance.UpdateAvailable,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children = {
                        new TextBlock { 
                            Text = string.Format(AppEnv.IsPackaged ? I18n.Instance.UpdatePackagedTip : I18n.Instance.UpdatePortableTip, update.Version),
                            TextWrapping = TextWrapping.Wrap 
                        },
                        new ScrollViewer {
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            MaxHeight = 300,
                            Content = new TextBlock { 
                                Text = ParseReleaseNotes(update.ReleaseNotes), 
                                FontSize = 12, 
                                Opacity = 0.7, 
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                },
                PrimaryButtonText = AppEnv.IsPackaged ? I18n.Instance.OpenStore : I18n.Instance.UpdateNow,
                SecondaryButtonText = I18n.Instance.UpdateIgnore,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (AppEnv.IsPackaged)
                {
                    Process.Start(new ProcessStartInfo(Lang.StoreUrl) { UseShellExecute = true });
                }
                else
                {
                    await DownloadAndApplyUpdate(update);
                }
            }
        }

        private string ParseReleaseNotes(string notes)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            
            // Look for standard Markdown horizontal rule separator
            var parts = notes.Split(new[] { "---" }, StringSplitOptions.None);
            if (parts.Length < 2) return notes.Trim();

            // Part 0 is Chinese (top), Part 1 is English (bottom)
            string result = I18n.Instance.CurrentLanguage == "zh" ? parts[0] : parts[1];
            return result.Trim();
        }

        private async Task DownloadAndApplyUpdate(UpdateInfo update)
        {
            if (string.IsNullOrEmpty(update.DownloadUrl))
            {
                ShowStatus(InfoBarSeverity.Error, I18n.Instance.L("No download link found in this release."));
                return;
            }

            var progressDialog = new ContentDialog
            {
                Title = I18n.Instance.UpdateDownloading,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children = {
                        new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true, Width = 300 }
                    }
                },
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            var progressBar = (progressDialog.Content as StackPanel).Children[0] as ProgressBar;
            
            // Show the dialog without waiting (we'll hide it later)
            var dialogTask = progressDialog.ShowAsync();

            var zipPath = await UpdateManager.DownloadUpdateAsync(update.DownloadUrl, p => 
            {
                this.DispatcherQueue.TryEnqueue(() => 
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = p * 100;
                });
            });

            progressDialog.Hide();

            if (!string.IsNullOrEmpty(zipPath))
            {
                ShowStatus(InfoBarSeverity.Success, I18n.Instance.UpdateDownloadSuccess);
                await Task.Delay(1000);
                UpdateManager.ApplyUpdatePortable(zipPath);
            }
            else
            {
                ShowStatus(InfoBarSeverity.Error, I18n.Instance.UpdateFailed);
            }
        }

        private void DrawPreview(DesktopLayout layout)
        {
            _iconLoadVersion++; // Cancel any pending icon loads
            PreviewCanvas.Children.Clear();
            PreviewCanvas.Clip = null; // Clear any focus clip
            DesktopJumpsPanel.Children.Clear();
            
            var displays = DisplayManager.GetDisplays();
            
            // Show jump panel only if there are multiple displays
            DesktopJumpsPanel.Visibility = (displays.Count > 1) ? Visibility.Visible : Visibility.Collapsed;

            var jumpLabel = new TextBlock { 
                Text = I18n.Instance.JumpToDesktop, 
                VerticalAlignment = VerticalAlignment.Center, 
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 
                FontSize = 12,
                Margin = new Thickness(0,0,5,0) 
            };
            DesktopJumpsPanel.Children.Add(jumpLabel);

            // Add 'ALL' button to see everything
            var allBtn = new Button { 
                Content = I18n.Instance.All, 
                Padding = new Thickness(12,4,12,4),
                CornerRadius = new CornerRadius(4),
                FontSize = 12,
                Style = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out object styleAllObj) ? (styleAllObj as Style ?? new Style()) : new Style()
            };
            allBtn.Click += (s, ev) => {
                _selectedDisplayIndex = -1;
                DrawPreview(layout);
            };
            DesktopJumpsPanel.Children.Add(allBtn);

            var iconsToDraw = GetEffectiveIcons(layout);
            if (iconsToDraw.Count == 0) return;

            double uiScale = (this.Content?.XamlRoot?.RasterizationScale) ?? 1.0;

            double minX, minY, maxX, maxY;

            if (_selectedDisplayIndex >= 0 && _selectedDisplayIndex < displays.Count)
            {
                var target = displays[_selectedDisplayIndex];
                minX = target.Left - 30;
                minY = target.Top - 30;
                maxX = target.Right + 30;
                maxY = target.Bottom + 30; // Extra room for labels at bottom
            }
            else if (displays.Count > 0)
            {
                minX = displays.Min(d => d.Left) - 30;
                minY = displays.Min(d => d.Top) - 30;
                maxX = displays.Max(d => d.Right) + 30;
                maxY = displays.Max(d => d.Bottom) + 30;
            }
            else
            {
                minX = (iconsToDraw.Count > 0 ? iconsToDraw.Min(i => i.X) : 0) - 140;
                minY = (iconsToDraw.Count > 0 ? iconsToDraw.Min(i => i.Y) : 0) - 140;
                maxX = (iconsToDraw.Count > 0 ? iconsToDraw.Max(i => i.X) : 0) + 180;
                maxY = (iconsToDraw.Count > 0 ? iconsToDraw.Max(i => i.Y) : 0) + 240;
            }

            PreviewCanvas.Width = (maxX - minX) / uiScale;
            PreviewCanvas.Height = (maxY - minY) / uiScale;

            int displayIdx = 1;
            foreach (var display in displays)
            {
                var screenRect = new Rectangle
                {
                    Width = display.Width / uiScale,
                    Height = display.Height / uiScale,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 20, 20, 30)), // Deeper, more premium navy
                    Stroke = new SolidColorBrush(Color.FromArgb(100, 100, 100, 150)), // Subtle blue-ish border
                    StrokeThickness = 2
                };
                Canvas.SetLeft(screenRect, (display.Left - minX) / uiScale);
                Canvas.SetTop(screenRect, (display.Top - minY) / uiScale);
                Canvas.SetZIndex(screenRect, -2); // Ensure screens are at the very back

                var screenText = new TextBlock
                {
                    Text = $"{I18n.Instance.ViewDesktop} {displayIdx}",
                    FontSize = 100 / uiScale, // Scale font size to look consistent
                    Foreground = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), // Subtle ghost text
                    FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                    IsHitTestVisible = false
                };
                // Position text at bottom-right of each screen with some margin
                Canvas.SetLeft(screenText, (display.Left - minX + display.Width - 450) / uiScale);
                Canvas.SetTop(screenText, (display.Top - minY + display.Height - 180) / uiScale);
                Canvas.SetZIndex(screenText, -1); // Keep below icons but above screen background

                PreviewCanvas.Children.Add(screenRect);
                PreviewCanvas.Children.Add(screenText);

                // Add Jump btn
                var jumpBtn = new Button { 
                    Content = displayIdx.ToString(), 
                    Padding = new Thickness(12,4,12,4),
                    CornerRadius = new CornerRadius(4),
                    FontSize = 12,
                    Style = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out object styleObj) ? (styleObj as Style ?? new Style()) : new Style()
                };
                
                var currentIndex = displayIdx - 1;
                jumpBtn.Click += (s, ev) => 
                {
                    _selectedDisplayIndex = currentIndex;
                    DrawPreview(layout);
                };
                DesktopJumpsPanel.Children.Add(jumpBtn);

                displayIdx++;
            }

            foreach (var icon in iconsToDraw)
            {
                bool isShortcut = !string.IsNullOrEmpty(icon.FilePath) &&
                                  icon.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                bool isFolderLike = IsFolderIcon(icon);

                bool isOriginalMode = PreviewModePanel.Visibility == Visibility.Visible && 
                                     (PreviewModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "original";

                double iconScale = 1.0;
                if (isOriginalMode && layout.CapturedDisplays != null)
                {
                    // In Original mode, we want to show the icon at the size it was captured
                    // Current logical size in WinUI is 1.0. To show 'capDpi' size physically,
                    // we need logical = capDpi / currentDpi.
                    uint capDpi = 96;
                    var capDisp = layout.CapturedDisplays.FirstOrDefault(d => icon.X >= d.Left && icon.X < d.Right && icon.Y >= d.Top && icon.Y < d.Bottom);
                    if (capDisp != null) capDpi = capDisp.Dpi;
                    else if (layout.CapturedDisplays.Count > 0) capDpi = layout.CapturedDisplays[0].Dpi;
                    
                    iconScale = (capDpi / 96.0) / uiScale;
                }
                else
                {
                    // In standard mode, we use 1.0 logical scale, letting WinUI handle the DPI scaling
                    iconScale = 1.0;
                }

                var iconGrid = new Grid { Width = 28, Height = 28 };

                // Simple fallback: yellow folder or blue file
                var fontIcon = new FontIcon
                {
                    Glyph = isFolderLike ? "\uE8B7" : "\uE7C3",
                    FontSize = 24 * iconScale,
                    Foreground = new SolidColorBrush(isFolderLike
                        ? Color.FromArgb(255, 255, 210, 80)
                        : Color.FromArgb(255, 120, 180, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconGrid.Children.Add(fontIcon);

                // Load real icon
                string loadPath = icon.FilePath ?? "";
                string cacheKey = string.IsNullOrEmpty(loadPath) ? icon.Name : loadPath;
                string fallbackPath = null;

                if (isShortcut)
                {
                    // If the .lnk file itself was deleted, promote ShortcutTarget/IconLocation to primary path
                    bool lnkExists = System.IO.File.Exists(loadPath);
                    
                    if (!string.IsNullOrEmpty(icon.ShortcutTarget) && System.IO.File.Exists(icon.ShortcutTarget))
                    {
                        if (!lnkExists)
                        {
                            // .lnk is gone but target exe exists — use target as primary
                            fallbackPath = loadPath;
                            loadPath = icon.ShortcutTarget;
                            cacheKey = loadPath;
                        }
                        else
                        {
                            fallbackPath = icon.ShortcutTarget;
                        }
                    }
                    else if (!string.IsNullOrEmpty(icon.ShortcutIconLocation))
                    {
                        string loc = icon.ShortcutIconLocation;
                        int commaIdx = loc.LastIndexOf(',');
                        if (commaIdx > 0) loc = loc.Substring(0, commaIdx).Trim();
                        
                        if (System.IO.File.Exists(loc))
                        {
                            if (!lnkExists)
                            {
                                fallbackPath = loadPath;
                                loadPath = loc;
                                cacheKey = loadPath;
                            }
                            else
                            {
                                fallbackPath = loc;
                            }
                        }
                    }
                }

                double iconVisualSize = 28 * iconScale;
                double fontSize = 11 * iconScale;

                DesktopIconCacheEntry cacheEntry = null;
                if (_iconCache.TryGetValue(cacheKey, out cacheEntry) ||
                    (fallbackPath != null && _iconCache.TryGetValue(fallbackPath, out cacheEntry)))
                {
                    var img = new Image { Width = iconVisualSize, Height = iconVisualSize, Stretch = Stretch.Uniform, Source = cacheEntry.Image };
                    iconGrid.Children.Add(img);
                    fontIcon.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var img = new Image { Width = iconVisualSize, Height = iconVisualSize, Stretch = Stretch.Uniform };
                    iconGrid.Children.Add(img);
                    _ = LoadIconAsync(img, fontIcon, loadPath, fallbackPath, icon.Name, cacheKey);
                }

                // Use a container for better alignment and spacing (StackPanel handles centering and vertical flow)
                var container = new StackPanel
                {
                    Width = 60 * iconScale,
                    Spacing = 4 * iconScale,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                iconGrid.HorizontalAlignment = HorizontalAlignment.Center;
                container.Children.Add(iconGrid);

                var tb = new TextBlock
                {
                    Text = icon.Name,
                    FontSize = fontSize,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                    Width = 60 * iconScale,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxHeight = fontSize * 3.2 // Safety height to prevent vertical bleeding
                };
                container.Children.Add(tb);

                ToolTipService.SetToolTip(container, icon.Name);
                
                // Calculate position: We want the icon to be centered over (icon.X, icon.Y) area
                double logicalCenterX = (icon.X - minX + 14) / uiScale;
                double logicalTopY = (icon.Y - minY) / uiScale;

                Canvas.SetLeft(container, logicalCenterX - (30 * iconScale));
                Canvas.SetTop(container, logicalTopY);

                PreviewCanvas.Children.Add(container);
            }
            
            PreviewScrollViewer.UpdateLayout();
            if (PreviewScrollViewer.ActualWidth > 0)
            {
                FitZoomToScreen(true);
            }
            else
            {
                DispatcherQueue.TryEnqueue(() => FitZoomToScreen(true));
            }
        }

        private void ZoomOriginalBtn_Click(object sender, RoutedEventArgs e)
        {
            PreviewScrollViewer.ChangeView(null, null, 1.0f);
        }

        private void ZoomFitBtn_Click(object sender, RoutedEventArgs e)
        {
            _selectedDisplayIndex = -1;
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                DrawPreview(layout);
            }
        }

        private void FitZoomToScreen(bool disableAnimation)
        {
            if (PreviewCanvas.Width > 0 && PreviewCanvas.Height > 0)
            {
                double sw = PreviewScrollViewer.ActualWidth;
                double sh = PreviewScrollViewer.ActualHeight;
                if (sw == 0 || sh == 0) return;

                double fitWidth = sw / (PreviewCanvas.Width + 120);
                double fitHeight = sh / (PreviewCanvas.Height + 120);
                float zoom = (float)Math.Min(fitWidth, fitHeight);
                if (zoom <= 0) zoom = 0.15f;
                // Leave a little margin and max limit to 1.0 for very small screen captures
                zoom *= 0.95f; 
                if (zoom > 1.0f) zoom = 1.0f;
                PreviewScrollViewer.ChangeView(null, null, zoom, disableAnimation);
            }
        }

        private List<IconInfo> GetEffectiveIcons(DesktopLayout layout)
        {
            if (PreviewModePanel.Visibility == Visibility.Collapsed || 
                (PreviewModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "scale")
            {
                return layout.Icons;
            }

            var currentDisplays = DisplayManager.GetDisplays();
            if (layout.CapturedDisplays == null || layout.CapturedDisplays.Count == 0 || currentDisplays.Count == 0)
            {
                return layout.Icons;
            }

            return layout.Icons.Select(icon => {
                // 1. Find which monitor this icon was on
                int monIdx = -1;
                for (int j = 0; j < layout.CapturedDisplays.Count; j++)
                {
                    var d = layout.CapturedDisplays[j];
                    if (icon.X >= d.Left && icon.X < d.Right && icon.Y >= d.Top && icon.Y < d.Bottom)
                    {
                        monIdx = j;
                        break;
                    }
                }

                if (monIdx < 0) return icon;

                // 2. Find the corresponding monitor in current environment
                var oldMon = layout.CapturedDisplays[monIdx];
                var newMon = currentDisplays.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceName) && d.DeviceName == oldMon.DeviceName);
                
                // Fallback to index if device name match fails (or for old snapshots)
                if (newMon == null && monIdx < currentDisplays.Count)
                {
                    newMon = currentDisplays[monIdx];
                }

                if (newMon != null)
                {
                    uint oldDpi = oldMon.Dpi > 0 ? oldMon.Dpi : 96;
                    uint newDpi = newMon.Dpi > 0 ? newMon.Dpi : 96;

                    // Calculate relative logical position within the monitor
                    // (icon.X - oldMon.Left) is physical pixels from left edge
                    // Divide by (oldDpi/96.0) to get logical pixels
                    double oldScale = oldDpi / 96.0;
                    double relLogX = (double)(icon.X - oldMon.Left) / oldScale;
                    double relLogY = (double)(icon.Y - oldMon.Top) / oldScale;

                    // If physical resolution is the same, we simply stay at the same logical position
                    // This ensures icons stay in the same "grid cells" if only DPI changed.
                    if (oldMon.Width == newMon.Width && oldMon.Height == newMon.Height)
                    {
                        double newScale = newDpi / 96.0;
                        return new IconInfo {
                            Name = icon.Name, FilePath = icon.FilePath,
                            ShortcutTarget = icon.ShortcutTarget, ShortcutArgs = icon.ShortcutArgs,
                            ShortcutIconLocation = icon.ShortcutIconLocation, ShortcutWorkingDir = icon.ShortcutWorkingDir,
                            X = newMon.Left + (int)(relLogX * newScale),
                            Y = newMon.Top + (int)(relLogY * newScale)
                        };
                    }
                    else
                    {
                        // Physical resolution changed - maintain relative logical percentage
                        double oldLogW = oldMon.Width / oldScale;
                        double oldLogH = oldMon.Height / oldScale;
                        double newScale = newDpi / 96.0;
                        double newLogW = newMon.Width / newScale;
                        double newLogH = newMon.Height / newScale;

                        double ratioX = relLogX / oldLogW;
                        double ratioY = relLogY / oldLogH;

                        return new IconInfo {
                            Name = icon.Name, FilePath = icon.FilePath,
                            ShortcutTarget = icon.ShortcutTarget, ShortcutArgs = icon.ShortcutArgs,
                            ShortcutIconLocation = icon.ShortcutIconLocation, ShortcutWorkingDir = icon.ShortcutWorkingDir,
                            X = newMon.Left + (int)(ratioX * newLogW * newScale),
                            Y = newMon.Top + (int)(ratioY * newLogH * newScale)
                        };
                    }
                }
                return icon;
            }).ToList();
        }

        private void PreviewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                DrawPreview(layout);
            }
        }

        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                NameEditBox.Text = layout.Name;
                RenamePanel.Visibility = Visibility.Visible;
                RenameBtn.Visibility = Visibility.Collapsed;
                DetailNameText.Visibility = Visibility.Collapsed;
                NameEditBox.Focus(FocusState.Programmatic);
            }
        }

        private void RenameConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            ConfirmRename();
        }

        private void RenameCancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelRename();
        }

        private void NameEditBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ConfirmRename();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CancelRename();
                e.Handled = true;
            }
        }

        private void NameEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.Content.XamlRoot);
            if (!object.Equals(focused, RenameConfirmBtn))
            {
                CancelRename();
            }
        }

        private void ConfirmRename()
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                string newName = NameEditBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && layout.Name != newName)
                {
                    LayoutManager.RenameLayout(layout.Id, newName);
                    DetailNameText.Text = newName;
                    
                    RefreshLayoutsList();
                    LayoutsListView.SelectedItem = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).FirstOrDefault(l => l.Id == layout.Id);
                }
            }
            CancelRename();
        }

        private void CancelRename()
        {
            RenamePanel.Visibility = Visibility.Collapsed;
            if (LayoutsListView.SelectedItem is DesktopLayout layout && !layout.Id.StartsWith("auto_") && layout.Id != "temp_auto_save")
            {
                RenameBtn.Visibility = Visibility.Visible;
            }
            DetailNameText.Visibility = Visibility.Visible;
        }

        private bool IsFolderIcon(IconInfo icon)
        {
            // Shortcut (.lnk) whose target is a folder
            if (!string.IsNullOrEmpty(icon.FilePath) &&
                icon.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(icon.ShortcutTarget) &&
                System.IO.Directory.Exists(icon.ShortcutTarget))
                return true;

            // Actual directory sitting on the desktop
            if (!string.IsNullOrEmpty(icon.FilePath) && System.IO.Directory.Exists(icon.FilePath))
                return true;

            // Everything else (system icons, programs, files) is NOT a folder
            return false;
        }

        private long _iconLoadVersion = 0;

        private async Task LoadIconAsync(Image img, FontIcon fallbackIcon, string filePath, string fallbackPath, string iconName, string cacheKey)
        {
            long myVersion = _iconLoadVersion;
            try
            {
                if (_iconCache.TryGetValue(cacheKey, out var cacheEntry))
                {
                    img.Source = cacheEntry.Image;
                    fallbackIcon.Visibility = Visibility.Collapsed;
                    return;
                }

                await _iconLoadSemaphore.WaitAsync();
                try
                {
                    if (myVersion != _iconLoadVersion) return;

                    if (_iconCache.TryGetValue(cacheKey, out cacheEntry))
                    {
                        img.Source = cacheEntry.Image;
                        fallbackIcon.Visibility = Visibility.Collapsed;
                        return;
                    }

                    byte[] iconBytes = null;
                    await Task.Run(() => { iconBytes = IconExtractor.GetIconBytes(filePath, iconName); });
                    
                    if (iconBytes == null || iconBytes.Length == 0)
                    {
                        if (!string.IsNullOrEmpty(fallbackPath) && fallbackPath != filePath)
                        {
                            await Task.Run(() => { iconBytes = IconExtractor.GetIconBytes(fallbackPath, iconName); });
                            if (iconBytes != null && iconBytes.Length > 0) cacheKey = fallbackPath;
                        }
                    }

                    if (myVersion != _iconLoadVersion) return;

                    if (iconBytes != null && iconBytes.Length > 0)
                    {
                        var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                        using (var writer = new Windows.Storage.Streams.DataWriter(ras.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(iconBytes);
                            await writer.StoreAsync();
                            writer.DetachStream();
                        }
                        ras.Seek(0);
                        
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(ras); // Fully decode FIRST

                        // Avoid unbounded memory growth
                        if (_iconCache.Count > 200) 
                        {
                            // Clear half the cache (oldest-ish entries in a ConcurrentDictionary)
                            int toRemove = _iconCache.Count / 2;
                            foreach (var key in _iconCache.Keys.Take(toRemove))
                            {
                                if (_iconCache.TryRemove(key, out var oldEntry))
                                {
                                    oldEntry.Dispose();
                                }
                            }
                        }

                        // Cache the fully-decoded image using atomic AddOrUpdate
                        var newEntry = new DesktopIconCacheEntry { Image = bmp, Stream = ras };
                        var existingEntry = _iconCache.AddOrUpdate(
                            cacheKey,
                            newEntry,
                            (key, oldValue) =>
                            {
                                oldValue.Dispose();
                                return newEntry;
                            });

                        // Only update UI if this load is still relevant to the current preview
                        if (myVersion == _iconLoadVersion)
                        {
                            img.Source = bmp;
                            fallbackIcon.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                finally
                {
                    _iconLoadSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadIconAsync Error: {ex}");
            }
        }

        private void NewLayoutBtn_Click(object sender, RoutedEventArgs e)
        {
            var icons = DesktopIconManager.GetIcons();
            if (icons.Count == 0 && !string.IsNullOrEmpty(DesktopIconManager.LastLog))
            {
                ShowStatus(InfoBarSeverity.Error, I18n.Instance.L("Failed to read icons."));
                return;
            }

            var newLayout = new DesktopLayout
            {
                Name = $"Snapshot {DateTime.Now:MM-dd HH:mm}",
                Icons = icons,
                CapturedDisplays = DisplayManager.GetDisplays()
            };
            LayoutManager.SaveLayout(newLayout);
            RefreshLayoutsList();
            var matchedLayout = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).FirstOrDefault(l => l.Id == newLayout.Id);
            if (matchedLayout != null) LayoutsListView.SelectedItem = matchedLayout;
            ShowStatus(InfoBarSeverity.Success, $"{I18n.Instance.L("Successfully created snapshot:")} {newLayout.Name}");
        }

        private async void OverwriteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                var dialog = new ContentDialog
                {
                    Title = I18n.Instance.ConfirmOverwriteTitle,
                    Content = I18n.Instance.ConfirmOverwriteContent,
                    PrimaryButtonText = I18n.Instance.Yes,
                    CloseButtonText = I18n.Instance.Cancel,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var icons = DesktopIconManager.GetIcons();
                    layout.Icons = icons;
                    layout.CapturedDisplays = DisplayManager.GetDisplays();
                    LayoutManager.SaveLayout(layout);
                    RefreshLayoutsList();
                    LayoutsListView.SelectedItem = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).FirstOrDefault(l => l.Id == layout.Id);
                    ShowStatus(InfoBarSeverity.Success, $"{I18n.Instance.L("Overwrote snapshot:")} {layout.Name}");
                }
            }
        }

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                var dialog = new ContentDialog
                {
                    Title = I18n.Instance.ConfirmDeleteTitle,
                    Content = I18n.Instance.ConfirmDeleteContent,
                    PrimaryButtonText = I18n.Instance.Yes,
                    CloseButtonText = I18n.Instance.Cancel,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    LayoutManager.DeleteLayout(layout.Id);
                    RefreshLayoutsList();
                    ShowStatus(InfoBarSeverity.Informational, I18n.Instance.L("Snapshot deleted."));
                }
            }
        }

        private async void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                if (layout.Icons.Count > 0)
                {
                    var dialog = new ContentDialog
                    {
                        Title = I18n.Instance.ConfirmRestoreTitle,
                        Content = I18n.Instance.ConfirmRestoreContent,
                        PrimaryButtonText = I18n.Instance.Yes,
                        CloseButtonText = I18n.Instance.Cancel,
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.Content.XamlRoot
                    };

                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

                    var iconsToRestore = GetEffectiveIcons(layout);

                    LoadingOverlay.Visibility = Visibility.Visible;
                    var result = await Task.Run(() => DesktopIconManager.SetIcons(iconsToRestore));
                    LoadingOverlay.Visibility = Visibility.Collapsed;

                    var msg = new System.Text.StringBuilder();
                    msg.Append($"{I18n.Instance.L("Repositioned:")} {result.Repositioned}");
                    if (result.Recreated > 0) msg.Append($" | {I18n.Instance.L("Shortcuts recreated:")} {result.Recreated}");
                    if (result.MissingFiles.Count > 0) msg.Append($" | {I18n.Instance.L("Cannot restore:")} {string.Join(", ", result.MissingFiles)}");
                    if (result.ExtraIcons > 0) msg.Append($" | {I18n.Instance.L("Extra icons on desktop:")} {result.ExtraIcons}");

                    if (result.AutoArrangeEnabled)
                    {
                        var fullMsg = I18n.Instance.AutoArrangeWarning + "\n\n" + msg.ToString();
                        ShowStatus(InfoBarSeverity.Warning, fullMsg);
                    }
                    else
                    {
                        ShowStatus(result.MissingFiles.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, msg.ToString());
                    }
                    DrawPreview(layout);
                }
                else
                {
                    ShowStatus(InfoBarSeverity.Warning, I18n.Instance.L("No icons in this snapshot."));
                }
            }
        }

        private void ShowStatus(InfoBarSeverity severity, string message)
        {
            StatusInfo.Severity = severity;
            StatusInfo.Message = message;
            StatusInfo.IsOpen = true;

            if (_statusTimer == null)
            {
                _statusTimer = new DispatcherTimer();
                _statusTimer.Interval = TimeSpan.FromSeconds(5);
                _statusTimer.Tick += (s, e) =>
                {
                    StatusInfo.IsOpen = false;
                    _statusTimer.Stop();
                };
            }

            _statusTimer.Stop(); // Reset timer if already running
            _statusTimer.Start();
        }

        // --- System Tray Actions ---
        
        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void ShowWindow()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            // Re-center the window if it was parked off-screen (e.g. after --silent start)
            if (appWindow.Position.X <= -32000 || appWindow.Position.Y <= -32000)
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                int physWidth = displayArea.WorkArea.Width;
                int physHeight = displayArea.WorkArea.Height;
                int w = appWindow.Size.Width;
                int h = appWindow.Size.Height;
                int x = (physWidth - w) / 2 + displayArea.WorkArea.X;
                int y = (physHeight - h) / 2 + displayArea.WorkArea.Y;
                appWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }

            this.Show();
            appWindow.Show();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void TrayShow()
        {
            ShowWindow();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void TraySave()
        {
            var icons = DesktopIconManager.GetIcons();
            if (icons.Count > 0)
            {
                var newLayout = new DesktopLayout
                {
                    Name = $"Tray Save {DateTime.Now:MM-dd HH:mm}",
                    Icons = icons,
                    CapturedDisplays = DisplayManager.GetDisplays()
                };
                LayoutManager.SaveLayout(newLayout);
                RefreshLayoutsList();
                ShowToast(I18n.Instance.L("Snapshot saved."));
            }
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void TrayRestore()
        {
            var layouts = LayoutManager.GetAllLayouts();
            var latest = layouts.FirstOrDefault(l => !l.Id.StartsWith("auto_") && l.Id != "temp_auto_save");
            if (latest != null && latest.Icons.Count > 0)
            {
                var iconsToRestore = GetEffectiveIcons(latest);
                Task.Run(() => DesktopIconManager.SetIcons(iconsToRestore));
                ShowToast(I18n.Instance.L("Desktop restored."));
            }
            else
            {
                ShowToast(I18n.Instance.L("No valid snapshot found."));
            }
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void TrayExit()
        {
            CleanupResources();
            TrayIcon.Dispose();
            Application.Current.Exit();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public async Task TrayToggleAutoStart()
        {
            var settings = SettingsManager.Load();
            
            // Toggle the boolean value based on config, ignoring the UI state to avoid race conditions
            bool newValue = !settings.AutoStart;
            
            bool success = await AutoStartManager.SetAutoStartAsync(newValue);
            if (!success && newValue == true)
            {
                // Failed to enable (e.g. disabled by user in Task Manager)
                newValue = false;
                ShowToast(I18n.Instance.L("System restricted auto-start. Opening Task Manager..."));
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "taskmgr", Arguments = "/0 /startup", UseShellExecute = true }); } catch { }
            }

            settings.AutoStart = newValue;
            SettingsManager.Save(settings);
            
            // Force UI components to match the new truth
            if (TrayAutoStartToggle != null) TrayAutoStartToggle.IsChecked = newValue;
            if (AutoStartToggle != null) AutoStartToggle.IsOn = newValue;
        }

        private async void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoStartToggle == null) return;
            var settings = SettingsManager.Load();
            bool isOn = AutoStartToggle.IsOn;

            if (settings.AutoStart != isOn)
            {
                bool success = await AutoStartManager.SetAutoStartAsync(isOn);
                if (!success && isOn == true)
                {
                    // Revert the toggle visually and return
                    AutoStartToggle.IsOn = false;
                    ShowToast(I18n.Instance.L("System restricted auto-start. Opening Task Manager..."));
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "taskmgr", Arguments = "/0 /startup", UseShellExecute = true }); } catch { }
                    return;
                }

                settings.AutoStart = isOn;
                SettingsManager.Save(settings);

                // Keep Tray menu UI in sync
                if (TrayAutoStartToggle != null)
                {
                    TrayAutoStartToggle.IsChecked = isOn;
                }
            }
        }

        private void AutoSaveOnDisplayChangeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoSaveOnDisplayChangeToggle == null) return;
            var settings = SettingsManager.Load();
            bool isOn = AutoSaveOnDisplayChangeToggle.IsOn;

            if (settings.AutoSaveOnDisplayChange != isOn)
            {
                settings.AutoSaveOnDisplayChange = isOn;
                SettingsManager.Save(settings);
            }
        }

        private void PerformAutoSnapshot(string reason)
        {
            try
            {
                var icons = DesktopIconManager.GetIcons();
                if (icons.Count > 0)
                {
                    var newLayout = new DesktopLayout
                    {
                        Name = $"{reason} {DateTime.Now:MM-dd HH:mm:ss}",
                        Icons = icons,
                        CapturedDisplays = DisplayManager.GetDisplays()
                    };
                    LayoutManager.SaveLayout(newLayout);
                    RefreshLayoutsList();
                    
                    // Specific toast for display change
                    ShowToast(I18n.Instance.DetectedDisplayChange);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AutoSnapshot error: {ex.Message}");
            }
        }
        
        // Clean up resources when the window is actually being closed.
        // We handle this in AppWindow_Closing rather than a finalizer for safety.
        private void CleanupResources()
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (_saveHotkeyId != -1)
                {
                    HotkeyManager.Unregister(hwnd, _saveHotkeyId);
                    _saveHotkeyId = -1;
                }

                if (_oldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtr(hwnd, GWLP_WNDPROC, _oldWndProc);
                    _oldWndProc = IntPtr.Zero;
                }

                // Clear and dispose icon cache
                foreach (var entry in _iconCache.Values)
                {
                    entry.Dispose();
                }
                _iconCache.Clear();

                // Dispose the semaphore to release any waiting tasks
                _iconLoadSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }
        public void ShowAndRestore()
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ShowWindow();
            });
        }

        private void ShowToast(string message)
        {
            try
            {
                var toast = new AppNotificationBuilder()
                    .AddText("DesktopSnap")
                    .AddText(message)
                    .BuildNotification();
                
                AppNotificationManager.Default.Show(toast);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Toast error: {ex.Message}");
                // Fallback to Tray icon if toast fails (e.g. in some unpackaged environments)
                TrayIcon.ShowNotification("DesktopSnap", message, H.NotifyIcon.Core.NotificationIcon.Info);
            }
        }
    }
}

