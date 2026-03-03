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
        public MainWindow()
        {
            this.InitializeComponent();
            LayoutManager.AutoSaveTemporary();
            RefreshLayoutsList();
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
                DetailCountText.Text = $"包含 {layout.Icons.Count} 个图标";

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
                    Text = $"桌面 {displayIdx++}",
                    FontSize = 160,
                    Foreground = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };
                Canvas.SetLeft(screenText, display.Left - minX + 80);
                Canvas.SetTop(screenText, display.Top - minY + 80);

                PreviewCanvas.Children.Add(screenRect);
                PreviewCanvas.Children.Add(screenText);
            }

            foreach (var icon in layout.Icons)
            {
                var fontIcon = new FontIcon
                {
                    Glyph = "\uE7C3",
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
            
            PreviewScrollViewer.ChangeView(null, null, 0.15f);
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
                ShowStatus(InfoBarSeverity.Error, "未能读取到图标。");
                return;
            }

            var newLayout = new DesktopLayout
            {
                Name = $"快照 {DateTime.Now:MM-dd HH:mm}",
                Icons = icons
            };
            LayoutManager.SaveLayout(newLayout);
            RefreshLayoutsList();
            LayoutsListView.SelectedItem = ((System.Collections.Generic.List<DesktopLayout>)LayoutsListView.ItemsSource).First(l => l.Id == newLayout.Id);
            ShowStatus(InfoBarSeverity.Success, $"成功创建新快照：{newLayout.Name}");
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
                ShowStatus(InfoBarSeverity.Success, $"已使用当前桌面图标布局覆盖了：{layout.Name}");
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                LayoutManager.DeleteLayout(layout.Id);
                RefreshLayoutsList();
                ShowStatus(InfoBarSeverity.Informational, "已删除选定的快照。");
            }
        }

        private void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutsListView.SelectedItem is DesktopLayout layout)
            {
                if (layout.Icons.Count > 0)
                {
                    DesktopIconManager.SetIcons(layout.Icons);
                    ShowStatus(InfoBarSeverity.Success, $"已成功恢复布局，包含 {layout.Icons.Count} 个图标。");
                }
                else
                {
                    ShowStatus(InfoBarSeverity.Warning, "此快照中没有图标。");
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
