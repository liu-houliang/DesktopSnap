using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Linq;
using Windows.UI;

namespace DesktopSnap
{
    public sealed partial class MainWindow : Window
    {
        public I18n Lang => I18n.Instance;

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
                string glyph = "\uE7C3"; // Default Page
                string lowered = icon.Name.ToLowerInvariant();
                if (!lowered.Contains(".")) glyph = "\uE8B7"; // Folder shape
                else if (lowered.EndsWith(".jpg") || lowered.EndsWith(".png") || lowered.EndsWith(".gif")) glyph = "\uEB9F"; // Picture
                else if (lowered.EndsWith(".mp4") || lowered.EndsWith(".mkv")) glyph = "\uE8B2"; // Video
                else if (lowered.EndsWith(".mp3") || lowered.EndsWith(".wav")) glyph = "\uE8D6"; // Audio
                else if (lowered.EndsWith(".txt") || lowered.EndsWith(".doc") || lowered.EndsWith(".docx") || lowered.EndsWith(".pdf")) glyph = "\uE8A5"; // Custom Doc
                else if (lowered.EndsWith(".zip") || lowered.EndsWith(".rar") || lowered.EndsWith(".7z")) glyph = "\uE7B8"; // Archive

                var fontIcon = new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 32,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 180, 255))
                };
                
                Canvas.SetLeft(fontIcon, icon.X - minX);
                Canvas.SetTop(fontIcon, icon.Y - minY);

                var tb = new TextBlock
                {
                    Text = TruncateStr(icon.Name, 12),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                    Width = 64,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                Canvas.SetLeft(tb, icon.X - minX - 16);
                Canvas.SetTop(tb, icon.Y - minY + 36);

                PreviewCanvas.Children.Add(fontIcon);
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

        private string TruncateStr(string str, int length)
        {
            if (str == null) return "";
            return str.Length > length ? str.Substring(0, length) + ".." : str;
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
                    DesktopIconManager.SetIcons(layout.Icons);
                    ShowStatus(InfoBarSeverity.Success, $"{I18n.Instance.L("Successfully restored")} {layout.Icons.Count} {I18n.Instance.ContainsIconsSuffix}");
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

