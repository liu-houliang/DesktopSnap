using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DesktopSnap
{
    public sealed partial class MainWindow : Window
    {
        private readonly string _saveFilePath;

        public MainWindow()
        {
            this.InitializeComponent();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _saveFilePath = Path.Combine(appData, "DesktopSnap", "layout.json");
            
            Directory.CreateDirectory(Path.GetDirectoryName(_saveFilePath));
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var icons = DesktopIconManager.GetIcons();
                string json = JsonSerializer.Serialize(icons, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_saveFilePath, json);
                
                StatusInfo.Severity = InfoBarSeverity.Success;
                StatusInfo.Message = $"成功保存 {icons.Count} 个图标的布局！";
            }
            catch (Exception ex)
            {
                StatusInfo.Severity = InfoBarSeverity.Error;
                StatusInfo.Message = $"保存失败: {ex.Message}";
            }
        }

        private void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(_saveFilePath))
                {
                    StatusInfo.Severity = InfoBarSeverity.Warning;
                    StatusInfo.Message = "没有找到保存的布局文件，请先保存。";
                    return;
                }

                string json = File.ReadAllText(_saveFilePath);
                var icons = JsonSerializer.Deserialize<List<IconInfo>>(json);

                if (icons != null)
                {
                    DesktopIconManager.SetIcons(icons);
                    StatusInfo.Severity = InfoBarSeverity.Success;
                    StatusInfo.Message = $"成功恢复 {icons.Count} 个图标的布局！";
                }
            }
            catch (Exception ex)
            {
                StatusInfo.Severity = InfoBarSeverity.Error;
                StatusInfo.Message = $"恢复失败: {ex.Message}";
            }
        }
    }
}
