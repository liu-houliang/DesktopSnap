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

        public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        public string AppTitle => $"{L("AppTitle")} v{AppVersion}";
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
        public string ResolutionMismatchTitle => L("ResolutionMismatchTitle");
        public string ResolutionMismatchContent => L("ResolutionMismatchContent");
        public string ScalePos => L("ScalePos");
        public string KeepOriginal => L("KeepOriginal");
        public string PreviewMode => L("PreviewMode");
        public string OriginalPosition => L("OriginalPosition");
        public string ScalingAdaptive => L("ScalingAdaptive");
        public string ResolutionMismatchBanner => L("ResolutionMismatchBanner");
        public string AutoArrangeWarning => L("AutoArrangeWarning");
        
        // New strings for tray and settings
        public string AutoStart => L("AutoStart");
        public string Exit => L("Exit");
        public string AppName => L("AppTitle");
        public string WelcomeTitle => L("WelcomeTitle");
        public string WelcomeMessage => L("WelcomeMessage");
        public string Step1Title => L("Step1Title");
        public string Step1Description => L("Step1Description");
        public string Step2Title => L("Step2Title");
        public string Step2Description => L("Step2Description");
        public string Step3Title => L("Step3Title");
        public string Step3Description => L("Step3Description");
        public string Step4Title => L("Step4Title");
        public string Step4Description => L("Step4Description");
        public string TryNewSnapshot => L("TryNewSnapshot");
        public string GetStarted => L("GetStarted");

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
                { "AppTitle", "桌面定格" },
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
                { "All", "全部" },
                { "ResolutionMismatchTitle", "显示器布局或分辨率已改变" },
                { "ResolutionMismatchContent", "检测到当前的显示器分辨率或排列与快照保存时不一致。\n\n建议选择“按比例缩放”以尝试使图标保持在屏幕相对位置。选择“保持原坐标”可能会导致部分图标超出可见区域。" },
                { "ScalePos", "按比例缩放" },
                { "KeepOriginal", "保持原坐标" },
                { "PreviewMode", "预览模式:" },
                { "OriginalPosition", "原始像素坐标" },
                { "ScalingAdaptive", "智能缩放适配 (推荐)" },
                { "ResolutionMismatchBanner", "警告：检测到显示器布局分辨率与保存时不一致，部分图标可能超出屏幕。" },
                { "AutoArrangeWarning", "提醒：检测到桌面已开启“自动排列图标”，这可能会阻止图标恢复到预想位置。请右键桌面 -> 查看 -> 取消勾选“自动排列图标”后再试。" },
                { "AutoStart", "开机自动启动 (后台运行)" },
                { "Exit", "完全退出" },
                { "Snapshot saved via hotkey.", "已通过快捷键保存最新快照。" },
                { "Latest snapshot restored via hotkey.", "已通过快捷键恢复最新快照。" },
                { "Running in background...", "桌面快照器已最小化到系统托盘，将在后台保护您的桌面。" },
                { "Snapshot saved.", "快照已保存。" },
                { "Desktop restored.", "桌面已恢复。" },
                { "No valid snapshot found.", "未找到有效的快照。" },
                { "WelcomeTitle", "欢迎开启桌面定格之旅" },
                { "WelcomeMessage", "我们将为您简单介绍一下这款高效的桌面图标整理工具。" },
                { "Step1Title", "📷 捕捉布局" },
                { "Step1Description", "整理好您的桌面图标后，点击左侧边栏的“新建快照”或使用快捷键 Ctrl+Alt+S 来定格当前位置。" },
                { "Step2Title", "🔄 一键还原" },
                { "Step2Description", "当图标变乱时，只需选中一个快照并点击“还原”按钮，即使连接了新显示器，图标也会各归各位。" },
                { "Step3Title", "🖥️ 实时预览" },
                { "Step3Description", "无需实际还原，即可在应用内预览不同显示器上的图标布局状态。" },
                { "Step4Title", "🚀 开机守护" },
                { "Step4Description", "支持开机自启并最小化到系统托盘，始终在后台默默保护您的桌面成果。" },
                { "TryNewSnapshot", "快点击“新建桌面快照”试试吧！" },
                { "GetStarted", "开始体验" }
            } },
            { "en", new Dictionary<string, string> {
                { "AppTitle", "Desktop Snap" },
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
                { "All", "All" },
                { "ResolutionMismatchTitle", "Display Layout Mismatch" },
                { "ResolutionMismatchContent", "Detected that the current display resolution or arrangement has changed since this snapshot was taken.\n\nRecommended: choose 'Scale' to keep icons in their relative positions. Choosing 'Keep Original' may result in icons being off-screen." },
                { "ScalePos", "Scale Proportional" },
                { "KeepOriginal", "Keep Original" },
                { "PreviewMode", "Preview:" },
                { "OriginalPosition", "Original Pixels" },
                { "ScalingAdaptive", "Smart Scaling (Recommended)" },
                { "ResolutionMismatchBanner", "Warning: Display layout/resolution mismatch. Some icons might be off-screen." },
                { "AutoArrangeWarning", "Note: 'Auto-arrange icons' is enabled. This may prevent manual positioning. Please right-click Desktop -> View -> uncheck 'Auto-arrange icons'." },
                { "AutoStart", "Run at startup (background)" },
                { "Exit", "Exit" },
                { "Snapshot saved via hotkey.", "Latest snapshot saved via hotkey." },
                { "Latest snapshot restored via hotkey.", "Latest snapshot restored via hotkey." },
                { "Running in background...", "DesktopSnap is minimized to the system tray and running in the background." },
                { "Snapshot saved.", "Snapshot saved." },
                { "Desktop restored.", "Desktop restored." },
                { "No valid snapshot found.", "No valid snapshot found." },
                { "WelcomeTitle", "Welcome to DesktopSnap" },
                { "WelcomeMessage", "Let's take a quick look at how to manage your desktop icons with ease." },
                { "Step1Title", "📷 Capture Layout" },
                { "Step1Description", "Organize your icons as you like, then click 'New Snapshot' or use Ctrl+Alt+S to freeze their positions." },
                { "Step2Title", "🔄 Quick Restore" },
                { "Step2Description", "When icons get messy, select a snapshot and click 'Restore'. Even with new displays, they go back to where they belong." },
                { "Step3Title", "🖥️ Desktop Preview" },
                { "Step3Description", "Preview your icon layouts for different displays directly within the app without applying changes." },
                { "Step4Title", "🚀 Auto Protection" },
                { "Step4Description", "Runs at startup and stays in the system tray, protecting your desktop layouts in the background." },
                { "TryNewSnapshot", "Try clicking 'New Snapshot' now!" },
                { "GetStarted", "Get Started" }
            } }
        };

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
