using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace DesktopSnap
{
    public sealed partial class MainWindow : Window
    {
        public I18n Lang => I18n.Instance;

        // Icon thumbnail cache: file path -> BitmapImage
        private static readonly ConcurrentDictionary<string, BitmapImage> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _iconLoadSemaphore = new(10, 10); // Max 10 concurrent loads

        public MainWindow()
        {
            SettingsManager.ApplySettings();
            this.InitializeComponent();

            var lang = SettingsManager.Load().Language;
            foreach (ComboBoxItem item in LangCombo.Items)
            {
                if (item.Tag?.ToString() == lang)
                {
                    LangCombo.SelectedItem = item;
                    break;
                }
            }

            LayoutManager.AutoSaveTemporary();
            RefreshLayoutsList();
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
                DrawPreview(layout);
            }
            else
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                NoSelectionText.Visibility = Visibility.Visible;
            }
        }

        private void DrawPreview(DesktopLayout layout)
        {
            _iconLoadVersion++; // Cancel any pending icon loads from previous preview
            PreviewCanvas.Children.Clear();
            DesktopJumpsPanel.Children.Clear();
            
            var jumpLabel = new TextBlock { Text = I18n.Instance.JumpToDesktop, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray), Margin = new Thickness(0,0,5,0) };
            DesktopJumpsPanel.Children.Add(jumpLabel);

            if (layout.Icons.Count == 0) return;

            var displays = DisplayManager.GetDisplays();

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            if (displays.Count > 0)
            {
                minX = displays.Min(d => d.Left);
                minY = displays.Min(d => d.Top);
                maxX = displays.Max(d => d.Right);
                maxY = displays.Max(d => d.Bottom);
            }
            else
            {
                minX = layout.Icons.Min(i => i.X);
                minY = layout.Icons.Min(i => i.Y);
                maxX = layout.Icons.Max(i => i.X) + 64;
                maxY = layout.Icons.Max(i => i.Y) + 64;
            }

            // Ensure bounding box logic is safe
            minX -= 40; minY -= 40;
            maxX += 80; maxY += 100;

            PreviewCanvas.Width = maxX - minX;
            PreviewCanvas.Height = maxY - minY;

            int displayIdx = 1;
            foreach (var display in displays)
            {
                var screenRect = new Rectangle
                {
                    Width = display.Width,
                    Height = display.Height,
                    Fill = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(screenRect, display.Left - minX);
                Canvas.SetTop(screenRect, display.Top - minY);

                var screenText = new TextBlock
                {
                    Text = $"{I18n.Instance.ViewDesktop} {displayIdx}",
                    FontSize = 160,
                    Foreground = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };
                Canvas.SetLeft(screenText, display.Left - minX + 80);
                Canvas.SetTop(screenText, display.Top - minY + 80);

                PreviewCanvas.Children.Add(screenRect);
                PreviewCanvas.Children.Add(screenText);

                // Add Jump btn
                var jumpBtn = new Button { Content = displayIdx.ToString(), Padding = new Thickness(8,4,8,4) };
                int jumpX = display.Left - minX;
                int jumpY = display.Top - minY;
                jumpBtn.Click += (s, ev) => 
                {
                    PreviewScrollViewer.ChangeView(jumpX, jumpY, null);
                };
                DesktopJumpsPanel.Children.Add(jumpBtn);

                displayIdx++;
            }

            foreach (var icon in layout.Icons)
            {
                bool isShortcut = !string.IsNullOrEmpty(icon.FilePath) &&
                                  icon.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                bool isFolderLike = IsFolderIcon(icon);

                var iconGrid = new Grid { Width = 32, Height = 32 };

                // Simple fallback: yellow folder or blue file
                var fontIcon = new FontIcon
                {
                    Glyph = isFolderLike ? "\uE8B7" : "\uE7C3",
                    FontSize = 28,
                    Foreground = new SolidColorBrush(isFolderLike
                        ? Color.FromArgb(255, 255, 210, 80)
                        : Color.FromArgb(255, 120, 180, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconGrid.Children.Add(fontIcon);

                // Load real icon - skip for plain folders
                if (!isFolderLike && !string.IsNullOrEmpty(icon.FilePath))
                {
                    string loadPath = icon.FilePath;
                    string fallbackPath = null;
                    if (isShortcut)
                    {
                        if (!string.IsNullOrEmpty(icon.ShortcutTarget) && System.IO.File.Exists(icon.ShortcutTarget))
                        {
                            fallbackPath = icon.ShortcutTarget;
                        }
                        else if (!string.IsNullOrEmpty(icon.ShortcutIconLocation))
                        {
                            string loc = icon.ShortcutIconLocation;
                            int commaIdx = loc.LastIndexOf(',');
                            if (commaIdx > 0) loc = loc.Substring(0, commaIdx).Trim();
                            
                            if (System.IO.File.Exists(loc))
                            {
                                fallbackPath = loc;
                            }
                        }
                    }

                    // Synchronous cache check — instant display, no flash
                    BitmapImage cachedBmp = null;
                    if (_iconCache.TryGetValue(loadPath, out cachedBmp) ||
                        (fallbackPath != null && _iconCache.TryGetValue(fallbackPath, out cachedBmp)))
                    {
                        var img = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform, Source = cachedBmp };
                        iconGrid.Children.Add(img);
                        fontIcon.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // Not cached — show fallback, load async
                        var img = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform };
                        iconGrid.Children.Add(img);
                        _ = LoadIconAsync(img, fontIcon, loadPath, fallbackPath);
                    }
                }

                // Shortcut arrow badge (bottom-left corner)
                if (isShortcut)
                {
                    var badge = new FontIcon
                    {
                        Glyph = "\uE71B", // Link arrow
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(-2, 0, 0, -2)
                    };
                    iconGrid.Children.Add(badge);
                }

                ToolTipService.SetToolTip(iconGrid, icon.Name);
                Canvas.SetLeft(iconGrid, icon.X - minX);
                Canvas.SetTop(iconGrid, icon.Y - minY);

                var tb = new TextBlock
                {
                    Text = icon.Name,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                    Width = 72,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                ToolTipService.SetToolTip(tb, icon.Name);
                Canvas.SetLeft(tb, icon.X - minX - 20);
                Canvas.SetTop(tb, icon.Y - minY + 36);

                PreviewCanvas.Children.Add(iconGrid);
                PreviewCanvas.Children.Add(tb);
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
            FitZoomToScreen(false);
        }

        private void FitZoomToScreen(bool disableAnimation)
        {
            if (PreviewCanvas.Width > 0 && PreviewCanvas.Height > 0)
            {
                double sw = PreviewScrollViewer.ActualWidth;
                double sh = PreviewScrollViewer.ActualHeight;
                if (sw == 0 || sh == 0) return;

                double fitWidth = sw / (PreviewCanvas.Width + 40);
                double fitHeight = sh / (PreviewCanvas.Height + 40);
                float zoom = (float)Math.Min(fitWidth, fitHeight);
                if (zoom <= 0) zoom = 0.15f;
                // Leave a little margin and max limit to 1.0 for very small screen captures
                zoom *= 0.95f; 
                if (zoom > 1.0f) zoom = 1.0f;
                PreviewScrollViewer.ChangeView(null, null, zoom, disableAnimation);
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

        private int _iconLoadVersion = 0;

        private async Task LoadIconAsync(Image img, FontIcon fallbackIcon, string filePath, string fallbackPath)
        {
            int myVersion = _iconLoadVersion;
            try
            {
                // Cache hit = instant
                if (_iconCache.TryGetValue(filePath, out var cached))
                {
                    img.Source = cached;
                    fallbackIcon.Visibility = Visibility.Collapsed;
                    return;
                }

                if (!System.IO.File.Exists(filePath))
                {
                    if (!string.IsNullOrEmpty(fallbackPath) && System.IO.File.Exists(fallbackPath))
                        filePath = fallbackPath;
                    else
                        return;
                }

                // Throttle concurrent loads
                await _iconLoadSemaphore.WaitAsync();
                try
                {
                    if (myVersion != _iconLoadVersion) return;

                    // Double-check cache
                    if (_iconCache.TryGetValue(filePath, out cached))
                    {
                        img.Source = cached;
                        fallbackIcon.Visibility = Visibility.Collapsed;
                        return;
                    }

                    // Direct load - NO timeout. Let it finish naturally, then display immediately.
                    var bmp = await LoadThumbnail(filePath);

                    // Try fallback path if primary failed
                    if (bmp == null && !string.IsNullOrEmpty(fallbackPath) && fallbackPath != filePath)
                    {
                        bmp = await LoadThumbnail(fallbackPath);
                        if (bmp != null) filePath = fallbackPath;
                    }

                    if (myVersion != _iconLoadVersion) return;

                    if (bmp != null)
                    {
                        _iconCache[filePath] = bmp;
                        img.Source = bmp;
                        fallbackIcon.Visibility = Visibility.Collapsed;
                    }
                }
                finally
                {
                    _iconLoadSemaphore.Release();
                }
            }
            catch { }
        }

        private async Task<BitmapImage> LoadThumbnail(string filePath)
        {
            try
            {
                var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
                var thumbnail = await storageFile.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 48);

                if (thumbnail != null && thumbnail.Size > 0)
                {
                    var bmp = new BitmapImage();
                    // Use SetSourceAsync to ensure full decode before returning.
                    // SetSource is synchronous but decode is deferred -> image isn't ready
                    // when we assign to img.Source, causing the "shows after switching" bug.
                    await bmp.SetSourceAsync(thumbnail);
                    return bmp;
                }
            }
            catch { }
            return null;
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
                Icons = icons
            };
            LayoutManager.SaveLayout(newLayout);
            RefreshLayoutsList();
            LayoutsListView.SelectedItem = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).First(l => l.Id == newLayout.Id);
            ShowStatus(InfoBarSeverity.Success, $"{I18n.Instance.L("Successfully created snapshot:")} {newLayout.Name}");
        }

        private void OverwriteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                var icons = DesktopIconManager.GetIcons();
                layout.Icons = icons;
                LayoutManager.SaveLayout(layout);
                RefreshLayoutsList();
                LayoutsListView.SelectedItem = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).FirstOrDefault(l => l.Id == layout.Id);
                ShowStatus(InfoBarSeverity.Success, $"{I18n.Instance.L("Overwrote snapshot:")} {layout.Name}");
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                LayoutManager.DeleteLayout(layout.Id);
                RefreshLayoutsList();
                ShowStatus(InfoBarSeverity.Informational, I18n.Instance.L("Snapshot deleted."));
            }
        }

        private void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                if (layout.Icons.Count > 0)
                {
                    var result = DesktopIconManager.SetIcons(layout.Icons);

                    var msg = new System.Text.StringBuilder();
                    msg.Append($"{I18n.Instance.L("Repositioned:")} {result.Repositioned}");

                    if (result.Recreated > 0)
                        msg.Append($" | {I18n.Instance.L("Shortcuts recreated:")} {result.Recreated}");

                    if (result.MissingFiles.Count > 0)
                        msg.Append($" | {I18n.Instance.L("Cannot restore:")} {string.Join(", ", result.MissingFiles)}");

                    if (result.ExtraIcons > 0)
                        msg.Append($" | {I18n.Instance.L("Extra icons on desktop:")} {result.ExtraIcons}");

                    var severity = result.MissingFiles.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
                    ShowStatus(severity, msg.ToString());
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
        }
    }
}

