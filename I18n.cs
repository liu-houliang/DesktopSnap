using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopSnap
{
    public class I18n : INotifyPropertyChanged
    {
        public static I18n Instance { get; } = new I18n();

        private string _currentLanguage = "zh";
        public string CurrentLanguage 
        {
            get => _currentLanguage;
            set 
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // Refresh all properties
                }
            }
        }

        public void InitializeFromSystem()
        {
            var systemLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            CurrentLanguage = (systemLang == "zh") ? "zh" : "en";
        }

        public string AppTitle => L("AppTitle");
        public string NewSnapshot => L("NewSnapshot");
        public string JumpToDesktop => L("JumpToDesktop");
        public string Zoom => L("Zoom");
        public string Overwrite => L("Overwrite");
        public string Delete => L("Delete");
        public string Restore => L("Restore");
        public string NoSelection => L("NoSelection");
        public string AutoTempSave => L("AutoTempSave");
        public string StatusReady => L("StatusReady");
        public string Settings => L("Settings");
        public string Language => L("Language");
        public string ViewDesktop => L("ViewDesktop");
        public string ContainsIconsPrefix => L("ContainsIconsPrefix");
        public string ContainsIconsSuffix => L("ContainsIconsSuffix");
        public string FitScreen => L("FitScreen");

        public string L(string key)
        {
            if (Translations.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }
            if (Translations.TryGetValue("en", out dict) && dict.TryGetValue(key, out val))
            {
                return val;
            }
            return key;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
        {
            { "zh", new Dictionary<string, string> {
                { "AppTitle", "桌面布局快照器 v2.0" },
                { "NewSnapshot", "➕ 新建桌面快照" },
                { "JumpToDesktop", "跳至桌面:" },
                { "Zoom", "缩放:" },
                { "Overwrite", "💾 覆盖更新为此桌面" },
                { "Delete", "🗑️ 删除此快照" },
                { "Restore", "🔄 恢复此布局" },
                { "NoSelection", "👈 请选择左侧的桌面快照或新建一个" },
                { "AutoTempSave", "🔄 自动备份" },
                { "StatusReady", "准备就绪" },
                { "Settings", "设置" },
                { "Language", "语言 (Language)" },
                { "ViewDesktop", "桌面" },
                { "FitScreen", "适应屏幕" },
                { "ContainsIconsPrefix", "包含 " },
                { "ContainsIconsSuffix", " 个图标" },
                { "Failed to read icons.", "未能读取到图标。" },
                { "Successfully created snapshot:", "成功创建新快照：" },
                { "Overwrote snapshot:", "已更新快照布局：" },
                { "Snapshot deleted.", "已成功删除快照。" },
                { "Successfully restored", "已成功恢复布局，包含" },
                { "No icons in this snapshot.", "此快照中没有记录任何图标。" }
            } },
            { "en", new Dictionary<string, string> {
                { "AppTitle", "Desktop Snap v2.0" },
                { "NewSnapshot", "➕ New Snapshot" },
                { "JumpToDesktop", "Jump to Display:" },
                { "Zoom", "Zoom:" },
                { "Overwrite", "💾 Overwrite with Current" },
                { "Delete", "🗑️ Delete Snapshot" },
                { "Restore", "🔄 Restore Layout" },
                { "NoSelection", "👈 Please select a snapshot on the left or create a new one" },
                { "AutoTempSave", "🔄 Auto Backup" },
                { "StatusReady", "Ready" },
                { "Settings", "Settings" },
                { "Language", "Language" },
                { "ViewDesktop", "Display" },
                { "FitScreen", "Fit Screen" },
                { "ContainsIconsPrefix", "Contains " },
                { "ContainsIconsSuffix", " icons" },
                { "Failed to read icons.", "Failed to read desktop icons." },
                { "Successfully created snapshot:", "Successfully created snapshot:" },
                { "Overwrote snapshot:", "Successfully updated snapshot:" },
                { "Snapshot deleted.", "Snapshot deleted." },
                { "Successfully restored", "Successfully restored layout, containing" },
                { "No icons in this snapshot.", "There are no icons saved in this snapshot." }
            } }
        };

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
