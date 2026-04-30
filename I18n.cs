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
        public string Continue => L("Continue");
        public string Cancel => L("Cancel");
        public string Close => L("Close");
        public string RestoringWait => L("RestoringWait");
        public string All => L("All");
        public string BackupManagement => L("BackupManagement");
        public string BackupDescription => L("BackupDescription");
        public string ExportPack => L("ExportPack");
        public string ImportPack => L("ImportPack");
        public string OpenFolder => L("OpenFolder");
        public string ExportSuccess => L("ExportSuccess");
        public string ImportSuccess => L("ImportSuccess");
        public string ImportSuccessSingle => L("ImportSuccessSingle");
        public string ImportUpdated => L("ImportUpdated");
        public string ImportAsBackup => L("ImportAsBackup");
        public string ImportTagNew => L("ImportTagNew");
        public string ImportTagOld => L("ImportTagOld");
        public string ImportSkippedIdentical => L("ImportSkippedIdentical");
        public string ImportFailedInvalidFormat => L("ImportFailedInvalidFormat");
        public string Export => L("Export");
        public string Import => L("Import");
        public string ResolutionMismatchTitle => L("ResolutionMismatchTitle");
        public string ResolutionMismatchContent => L("ResolutionMismatchContent");
        public string ScalePos => L("ScalePos");
        public string KeepOriginal => L("KeepOriginal");
        public string PreviewMode => L("PreviewMode");
        public string OriginalPosition => L("OriginalPosition");
        public string ScalingAdaptive => L("ScalingAdaptive");
        public string ResolutionMismatchBanner => L("ResolutionMismatchBanner");
        public string AutoArrangeWarning => L("AutoArrangeWarning");
        public string AutoSaveOnDisplayChange => L("AutoSaveOnDisplayChange");
        public string AutoSaveOnDisplayChangeDescription => L("AutoSaveOnDisplayChangeDescription");
        public string DisplayChangeAutoSaveName => L("DisplayChangeAutoSaveName");
        public string DetectedDisplayChange => L("DetectedDisplayChange");
        
        // New strings for tray and settings
        public string AutoStart => L("AutoStart");
        public string AutoStartDescription => L("AutoStartDescription");
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
        public string OpenStore => L("OpenStore");
        public string CheckForUpdates => L("CheckForUpdates");
        public string UpdateAvailable => L("UpdateAvailable");
        public string UpdateDownloading => L("UpdateDownloading");
        public string UpdateDownloadSuccess => L("UpdateDownloadSuccess");
        public string UpdateFailed => L("UpdateFailed");
        public string UpdateLatest => L("UpdateLatest");
        public string UpdateNow => L("UpdateNow");
        public string UpdateIgnore => L("UpdateIgnore");
        public string UpdatePortableTip => L("UpdatePortableTip");
        public string UpdatePackagedTip => L("UpdatePackagedTip");
        public string UpdateAutoCheck => L("UpdateAutoCheck");
        public string DisableAutoUpdate => L("DisableAutoUpdate");
        public string StoreUpdateNotes => L("StoreUpdateNotes");
        public string DeleteAllLayouts => L("DeleteAllLayouts");
        public string ConfirmDeleteAllTitle => L("ConfirmDeleteAllTitle");
        public string ConfirmDeleteAllContent => L("ConfirmDeleteAllContent");
        public string AllSnapshotsDeleted => L("AllSnapshotsDeleted");

        // About dialog
        public string About => L("About");
        public string AboutTitle => L("AboutTitle");
        public string AboutTagline => L("AboutTagline");
        public string AboutDescription => L("AboutDescription");
        public string AboutHomepage => L("AboutHomepage");
        public string AboutWebsite => L("AboutWebsite");
        public string AboutVersionLabel => L("AboutVersionLabel");
        public string AboutGitHub => L("AboutGitHub");
        public string AboutChangelogTitle => L("AboutChangelogTitle");
        public string AboutChangelog => L("AboutChangelog");

        // Language-aware link URLs
        public string HomepageUrl => _currentLanguage == "zh" ? "https://liuhouliang.com" : "https://liuhouliang.com/en";
        public string WebsiteUrl => _currentLanguage == "zh" ? "https://desktopsnap.liuhouliang.com" : "https://desktopsnap.liuhouliang.com/en";
        public string GitHubUrl => "https://github.com/liu-houliang/DesktopSnap";
        public string StoreUrl => "ms-windows-store://pdp/?productid=9n88rj6d0js3";

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
                { "Continue", "继续" },
                { "Cancel", "取消" },
                { "Close", "关闭" },
                { "RestoringWait", "正在恢复桌面图标布局，请稍候..." },
                { "All", "全部" },
                { "BackupManagement", "数据管理与备份" },
                { "BackupDescription", "导出所有快照为备份包，或从备份包恢复。您也可以直接打开备份文件夹手动管理 JSON 文件。" },
                { "ExportPack", "导出所有快照备份 (.zip)" },
                { "ImportPack", "从备份包恢复快照 (.zip)" },
                { "OpenFolder", "打开本地备份文件夹" },
                { "ExportSuccess", "备份已成功导出。" },
                { "ImportSuccess", "备份已成功导入，共计 {0} 个快照。" },
                { "ImportSuccessSingle", "快照导入成功。" },
                { "ImportUpdated", "检测到较新版本，已更新现有快照。" },
                { "ImportAsBackup", "检测到现有快照较新，已作为备份副本导入。" },
                { "ImportTagNew", " (新)" },
                { "ImportTagOld", " (旧)" },
                { "ImportSkippedIdentical", "检测到内容完全一致，已跳过。" },
                { "ImportFailedInvalidFormat", "导入失败：快照文件内容格式不正确或已损坏。" },
                { "Export", "导出快照" },
                { "Import", "导入快照" },
                { "ResolutionMismatchTitle", "显示器布局或分辨率已改变" },
                { "ResolutionMismatchContent", "检测到当前的显示器分辨率或排列与快照保存时不一致。\n\n建议选择“按比例缩放”以尝试使图标保持在屏幕相对位置。选择“保持原坐标”可能会导致部分图标超出可见区域。" },
                { "ScalePos", "按比例缩放" },
                { "KeepOriginal", "保持原坐标" },
                { "PreviewMode", "预览模式:" },
                { "OriginalPosition", "原始像素坐标" },
                { "ScalingAdaptive", "智能缩放适配 (推荐)" },
                { "ResolutionMismatchBanner", "警告：检测到显示器布局分辨率与保存时不一致，部分图标可能超出屏幕。" },
                { "AutoArrangeWarning", "提醒：检测到桌面已开启“自动排列图标”，这可能会阻止图标恢复到预想位置。请右键桌面 -> 查看 -> 取消勾选“自动排列图标”后再试。" },
                { "AutoStart", "开机自动启动" },
                { "AutoStartDescription", "在系统启动时在后台静默运行" },
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
                { "GetStarted", "开始体验" },
                { "About", "关于" },
                { "AboutTitle", "关于桌面定格" },
                { "AboutTagline", "让你的桌面布局，永远如初" },
                { "AboutDescription", "连接新显示器、调整分辨率或重启后，有时桌面图标会被打乱位置。\n\n使用桌面定格，一键保存当前图标布局，随时完整还原，让桌面始终如你所设。" },
                { "AboutHomepage", "作者主页" },
                { "AboutWebsite", "软件主页" },
                { "AboutVersionLabel", "版本" },
                { "AboutGitHub", "GitHub 源代码" },
                { "AboutChangelogTitle", "本版更新内容" },
                { "AboutChangelog", "• 新增：自动更新功能，支持一键检查并安装新版本\n• 新增：支持隐藏图标的显示和处理，恢复布局时隐藏图标自动归位\n• 新增：一键删除所有本地布局功能\n• 优化：支持识别同名不同扩展名的文件，图标匹配更精准\n• 修复：Win11 下图标刷新导致焦点丢失的问题" },
                { "AutoSaveOnDisplayChange", "环境变动后自动保存" },
                { "AutoSaveOnDisplayChangeDescription", "当分辨率或显示器变动后，自动保存变更后的新布局" },
                { "DisplayChangeAutoSaveName", "自动: 环境变动" },
                { "DetectedDisplayChange", "检测到显示器环境变动，已自动为您备份当前布局。" },
                { "OpenStore", "去商店更新" },
                { "System restricted auto-start. Opening Task Manager...", "系统权限限制，已被手动禁用。正在为您打开任务管理器，请手动允许。" },
                { "CheckForUpdates", "检查更新" },
                { "UpdateAvailable", "发现新版本" },
                { "UpdateDownloading", "正在下载更新..." },
                { "UpdateDownloadSuccess", "下载完成，准备安装..." },
                { "UpdateFailed", "更新失败" },
                { "UpdateLatest", "已是最新版本" },
                { "UpdateNow", "立即更新" },
                { "UpdateIgnore", "以后再说" },
                { "UpdatePortableTip", "检测到新版本 {0}。点击“立即更新”将自动下载并重启应用。" },
                { "UpdatePackagedTip", "检测到新版本 {0}。由于您正在使用微软商店版本，请前往商店进行更新。" },
                { "UpdateAutoCheck", "自动检查更新" },
                { "DisableAutoUpdate", "不再自动检查更新" },
                { "StoreUpdateNotes", "请前往 Microsoft Store 查看详细发行说明。" },
                { "HiddenCount", "，其中 {0} 个已隐藏" },
                { "VisibilityFailed", "权限不足导致显隐失败:" },
                { "DeleteAllLayouts", "删除所有本地布局" },
                { "ConfirmDeleteAllTitle", "确认删除所有布局？" },
                { "ConfirmDeleteAllContent", "此操作将永久删除所有已保存的布局快照（包括自动备份）。此操作无法撤销，您确定要继续吗？" },
                { "CountdownConfirm", "确定 ({0}秒)" },
                { "AllSnapshotsDeleted", "所有快照已成功删除。" }
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
                { "Continue", "Continue" },
                { "Cancel", "Cancel" },
                { "Close", "Close" },
                { "RestoringWait", "Restoring desktop icon layout, please wait..." },
                { "All", "All" },
                { "BackupManagement", "Data & Backup Management" },
                { "BackupDescription", "Export all snapshots as a backup package or restore from one. You can also open the local folder to manage files manually." },
                { "ExportPack", "Export All Snapshot Backup (.zip)" },
                { "ImportPack", "Restore from Backup Package (.zip)" },
                { "OpenFolder", "Open Backup Folder" },
                { "ExportSuccess", "Backup exported successfully." },
                { "ImportSuccess", "Imported {0} snapshots successfully from backup." },
                { "ImportSuccessSingle", "Snapshot imported successfully." },
                { "ImportUpdated", "A newer version was detected and the existing snapshot has been updated." },
                { "ImportAsBackup", "Existing snapshot is newer; imported as a backup copy." },
                { "ImportTagNew", " (New)" },
                { "ImportTagOld", " (Old)" },
                { "ImportSkippedIdentical", "Identical content detected; skipping import." },
                { "ImportFailedInvalidFormat", "Import failed: The snapshot file format is invalid or corrupted." },
                { "Export", "Export Layout" },
                { "Import", "Import Layout" },
                { "ResolutionMismatchTitle", "Display Layout Mismatch" },
                { "ResolutionMismatchContent", "Detected that the current display resolution or arrangement has changed since this snapshot was taken.\n\nRecommended: choose 'Scale' to keep icons in their relative positions. Choosing 'Keep Original' may result in icons being off-screen." },
                { "ScalePos", "Scale Proportional" },
                { "KeepOriginal", "Keep Original" },
                { "PreviewMode", "Preview:" },
                { "OriginalPosition", "Original Pixels" },
                { "ScalingAdaptive", "Smart Scaling (Recommended)" },
                { "ResolutionMismatchBanner", "Warning: Display layout/resolution mismatch. Some icons might be off-screen." },
                { "AutoArrangeWarning", "Note: 'Auto-arrange icons' is enabled. This may prevent manual positioning. Please right-click Desktop -> View -> uncheck 'Auto-arrange icons'." },
                { "AutoStart", "Run at startup" },
                { "AutoStartDescription", "Run silently in the background on system boot" },
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
                { "GetStarted", "Get Started" },
                { "About", "About" },
                { "AboutTitle", "About Desktop Snap" },
                { "AboutTagline", "Your desktop, always exactly where you left it." },
                { "AboutDescription", "Desktop Snap solves an everyday frustration: every time you switch displays, change resolution, or restart, your carefully arranged icons shuffle into chaos.\n\nWith Desktop Snap, save your icon layout with one click and restore it perfectly — anytime." },
                { "AboutHomepage", "Author Homepage" },
                { "AboutWebsite", "Official Website" },
                { "AboutVersionLabel", "Version" },
                { "AboutGitHub", "Source Code on GitHub" },
                { "AboutChangelogTitle", "What's New" },
                { "AboutChangelog", "\u2022 New: Auto-update feature with one-click check and installation\n\u2022 New: Hidden icon support - display and restore hidden icons to their positions\n\u2022 New: One-click delete all local layouts\n\u2022 Improved: Support for files with same name but different extensions, more accurate icon matching\n\u2022 Fixed: Win11 focus loss after icon refresh" },
                { "AutoSaveOnDisplayChange", "Auto-save after display change" },
                { "AutoSaveOnDisplayChangeDescription", "Automatically save the layout after displays change." },
                { "DisplayChangeAutoSaveName", "Auto: Display Change" },
                { "DetectedDisplayChange", "Display environment change detected. Current layout has been auto-saved." },
                { "OpenStore", "Go to Store" },
                { "System restricted auto-start. Opening Task Manager...", "System restricted auto-start. Opening Task Manager for you to enable manually." },
                { "CheckForUpdates", "Check for Updates" },
                { "UpdateAvailable", "Update Available" },
                { "UpdateDownloading", "Downloading update..." },
                { "UpdateDownloadSuccess", "Download complete, preparing to install..." },
                { "UpdateFailed", "Update Failed" },
                { "UpdateLatest", "You are up to date" },
                { "UpdateNow", "Update Now" },
                { "UpdateIgnore", "Remind Me Later" },
                { "UpdatePortableTip", "A new version {0} is available. Click 'Update Now' to automatically download and restart the application." },
                { "UpdatePackagedTip", "A new version {0} is available. Since you are using the Microsoft Store version, please update via the Store." },
                { "UpdateAutoCheck", "Check for updates automatically" },
                { "DisableAutoUpdate", "Don't check automatically" },
                { "StoreUpdateNotes", "Please visit the Microsoft Store for detailed release notes." },
                { "HiddenCount", ", including {0} hidden" },
                { "VisibilityFailed", "Visibility change failed (Permission Denied):" },
                { "DeleteAllLayouts", "Delete All Local Layouts" },
                { "ConfirmDeleteAllTitle", "Confirm Delete All Layouts?" },
                { "ConfirmDeleteAllContent", "This will permanently delete ALL saved snapshots (including auto-backups). This action cannot be undone. Are you sure you want to continue?" },
                { "CountdownConfirm", "Confirm ({0}s)" },
                { "AllSnapshotsDeleted", "All snapshots deleted successfully." }
            } }
        };

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
