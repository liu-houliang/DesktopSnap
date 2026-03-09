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
        public string Rename => L("Rename");
        public string ConfirmDeleteTitle => L("ConfirmDeleteTitle");
        public string ConfirmDeleteContent => L("ConfirmDeleteContent");
        public string ConfirmOverwriteTitle => L("ConfirmOverwriteTitle");
        public string ConfirmOverwriteContent => L("ConfirmOverwriteContent");
        public string ConfirmRestoreTitle => L("ConfirmRestoreTitle");
        public string ConfirmRestoreContent => L("ConfirmRestoreContent");
        public string Yes => L("Yes");
        public string Cancel => L("Cancel");
        public string RestoringWait => L("RestoringWait");
        public string All => L("All");

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
                { "NewSnapshot", "新建桌面快照" },
                { "JumpToDesktop", "跳至桌面:" },
                { "Zoom", "缩放:" },
                { "Overwrite", "覆盖更新为此桌面" },
                { "Delete", "删除此快照" },
                { "Restore", "恢复此布局" },
                { "NoSelection", "请选择左侧的桌面快照或新建一个" },
                { "AutoTempSave", "自动备份" },
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
                { "No icons in this snapshot.", "此快照中没有记录任何图标。" },
                { "Repositioned:", "已复位:" },
                { "Shortcuts recreated:", "已重建快捷方式:" },
                { "Cannot restore:", "无法恢复:" },
                { "Extra icons on desktop:", "桌面上的额外图标:" },
                { "Rename", "重命名" },
                { "ConfirmDeleteTitle", "确认删除" },
                { "ConfirmDeleteContent", "确定要永久删除此桌面快照吗？此操作无法撤销。" },
                { "ConfirmOverwriteTitle", "确认覆盖" },
                { "ConfirmOverwriteContent", "确定要用当前桌面的最新布局覆盖此快照吗？" },
                { "ConfirmRestoreTitle", "确认恢复" },
                { "ConfirmRestoreContent", "确定要将桌面图标恢复到此快照的状态吗？\n当前桌面上未保存的位置变动将会丢失。" },
                { "Yes", "确定" },
                { "Cancel", "取消" },
                { "RestoringWait", "正在恢复桌面图标布局，请稍候..." },
                { "All", "全部" }
            } },
            { "en", new Dictionary<string, string> {
                { "AppTitle", "Desktop Snap v2.0" },
                { "NewSnapshot", "New Snapshot" },
                { "JumpToDesktop", "Jump to Display:" },
                { "Zoom", "Zoom:" },
                { "Overwrite", "Overwrite with Current" },
                { "Delete", "Delete Snapshot" },
                { "Restore", "Restore Layout" },
                { "NoSelection", "Please select a snapshot on the left or create a new one" },
                { "AutoTempSave", "Auto Backup" },
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
                { "No icons in this snapshot.", "There are no icons saved in this snapshot." },
                { "Repositioned:", "Repositioned:" },
                { "Shortcuts recreated:", "Shortcuts recreated:" },
                { "Cannot restore:", "Cannot restore:" },
                { "Extra icons on desktop:", "Extra icons on desktop:" },
                { "Rename", "Rename" },
                { "ConfirmDeleteTitle", "Confirm Delete" },
                { "ConfirmDeleteContent", "Are you sure you want to permanently delete this snapshot? This cannot be undone." },
                { "ConfirmOverwriteTitle", "Confirm Overwrite" },
                { "ConfirmOverwriteContent", "Are you sure you want to overwrite this snapshot with the current desktop layout?" },
                { "ConfirmRestoreTitle", "Confirm Restore" },
                { "ConfirmRestoreContent", "Are you sure you want to restore your desktop icons to this snapshot?\nUnsaved position changes on your current desktop will be lost." },
                { "Yes", "Yes" },
                { "Cancel", "Cancel" },
                { "RestoringWait", "Restoring desktop icon layout, please wait..." },
                { "All", "All" }
            } }
        };

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
