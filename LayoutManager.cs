using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;

namespace DesktopSnap
{
    public class DesktopLayout
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public bool IsPinned { get; set; } = false;
        public DateTime? PinnedAt { get; set; } = null;
        public List<IconInfo> Icons { get; set; } = new List<IconInfo>();
        public List<DisplayInfo> CapturedDisplays { get; set; } = new List<DisplayInfo>();
        public string SavedTime => SavedAt.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public enum ImportStatus
    {
        Success,
        Updated,
        AsBackup,
        Skipped,
        Error
    }

    public static class LayoutManager
    {
        private static readonly object _autoSaveLock = new object();
        private static string _layoutsDirectory;

        static LayoutManager()
        {
            _layoutsDirectory = Path.Combine(AppEnv.GetDataDirectory(), "Layouts");
            
            // Migration handling...
            if (AppEnv.IsPackaged)
            {
                string oldLayoutsDir = Path.Combine(AppContext.BaseDirectory, "Config", "Layouts");
                if (Directory.Exists(oldLayoutsDir))
                {
                    try
                    {
                        if (!Directory.Exists(_layoutsDirectory)) Directory.CreateDirectory(_layoutsDirectory);
                        var files = Directory.GetFiles(oldLayoutsDir, "*.json");
                        foreach (var file in files)
                        {
                            string destFile = Path.Combine(_layoutsDirectory, Path.GetFileName(file));
                            if (!File.Exists(destFile)) File.Copy(file, destFile);
                        }
                    }
                    catch { }
                }
            }
            if (!Directory.Exists(_layoutsDirectory)) Directory.CreateDirectory(_layoutsDirectory);
        }

        public static string GetLayoutsDirectory() => _layoutsDirectory;

        public static List<DesktopLayout> GetAllLayouts()
        {
            var layouts = new List<DesktopLayout>();
            if (!Directory.Exists(_layoutsDirectory)) return layouts;

            var files = Directory.GetFiles(_layoutsDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var layout = JsonSerializer.Deserialize<DesktopLayout>(json);
                    if (layout != null)
                    {
                        foreach (var icon in layout.Icons)
                        {
                            icon.FilePath = PathService.Denormalize(icon.FilePath);
                            icon.ShortcutTarget = PathService.Denormalize(icon.ShortcutTarget);
                            icon.ShortcutIconLocation = PathService.Denormalize(icon.ShortcutIconLocation);
                            icon.ShortcutWorkingDir = PathService.Denormalize(icon.ShortcutWorkingDir);
                        }

                        if (layout.Id.StartsWith("auto_") || layout.Id == "temp_auto_save")
                        {
                            layout.Name = I18n.Instance.AutoTempSave + " (" + layout.SavedAt.ToString("MM-dd HH:mm") + ")";
                        }

                        layouts.Add(layout);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LayoutManager Error: {ex}"); }
            }
            return layouts
                .OrderByDescending(l => l.IsPinned)
                .ThenByDescending(l => l.PinnedAt ?? DateTime.MinValue)
                .ThenByDescending(l => l.SavedAt)
                .ToList();
        }

        public static void SaveLayout(DesktopLayout layout, bool updateTimestamp = true)
        {
            string file = Path.Combine(_layoutsDirectory, $"{layout.Id}.json");
            SaveLayoutInternal(layout, file, updateTimestamp);
        }

        public static void ExportLayout(DesktopLayout layout, string destinationPath)
        {
            SaveLayoutInternal(layout, destinationPath, false); // Don't update time when exporting
        }

        private static void SaveLayoutInternal(DesktopLayout layout, string path, bool updateTimestamp)
        {
            if (updateTimestamp) layout.SavedAt = DateTime.Now;

            var portableLayout = new DesktopLayout
            {
                Id = layout.Id,
                Name = layout.Name,
                SavedAt = layout.SavedAt,
                IsPinned = layout.IsPinned,
                PinnedAt = layout.PinnedAt,
                CapturedDisplays = layout.CapturedDisplays,
                Icons = layout.Icons.Select(i => new IconInfo
                {
                    Name = i.Name,
                    X = i.X,
                    Y = i.Y,
                    FilePath = PathService.Normalize(i.FilePath),
                    ShortcutTarget = PathService.Normalize(i.ShortcutTarget),
                    ShortcutArgs = i.ShortcutArgs,
                    ShortcutIconLocation = PathService.Normalize(i.ShortcutIconLocation),
                    ShortcutWorkingDir = PathService.Normalize(i.ShortcutWorkingDir),
                    IsHidden = i.IsHidden
                }).ToList()
            };

            string json = JsonSerializer.Serialize(portableLayout, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static (ImportStatus status, DesktopLayout layout) ImportLayout(string sourcePath)
        {
            if (!File.Exists(sourcePath)) return (ImportStatus.Error, null);
            
            try
            {
                string json = File.ReadAllText(sourcePath);
                var layout = JsonSerializer.Deserialize<DesktopLayout>(json);
                if (layout == null) return (ImportStatus.Error, null);

                var existingLayouts = GetAllLayouts();
                var existing = existingLayouts.FirstOrDefault(l => l.Id == layout.Id);

                ImportStatus status = ImportStatus.Success;

                if (existing != null)
                {
                    if (layout.SavedAt == existing.SavedAt)
                    {
                        return (ImportStatus.Skipped, existing);
                    }
                    else if (layout.SavedAt > existing.SavedAt)
                    {
                        // Incoming is newer, overwrite the existing one (keep same ID)
                        status = ImportStatus.Updated;
                    }
                    else
                    {
                        // Incoming is older, import as a new backup copy
                        layout.Id = Guid.NewGuid().ToString();
                        layout.Name += I18n.Instance.ImportTagOld;
                        status = ImportStatus.AsBackup;
                    }
                }

                // Save locally (always preserve the original timestamp of the backup if it's an import)
                string destFile = Path.Combine(_layoutsDirectory, $"{layout.Id}.json");
                SaveLayoutInternal(layout, destFile, false); 

                // Denormalize for UI
                foreach (var icon in layout.Icons)
                {
                    icon.FilePath = PathService.Denormalize(icon.FilePath);
                    icon.ShortcutTarget = PathService.Denormalize(icon.ShortcutTarget);
                    icon.ShortcutIconLocation = PathService.Denormalize(icon.ShortcutIconLocation);
                    icon.ShortcutWorkingDir = PathService.Denormalize(icon.ShortcutWorkingDir);
                }

                return (status, layout);
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Import Error: {ex}"); 
                return (ImportStatus.Error, null);
            }
        }

        public static void ExportAllLayouts(string zipPath)
        {
            // Safety: Ensure the zip is not being created inside the layouts directory itself
            // to avoid recursive inclusion or file locking issues.
            string fullZipPath = Path.GetFullPath(zipPath);
            string layoutsDir = Path.GetFullPath(_layoutsDirectory);
            if (fullZipPath.StartsWith(layoutsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullZipPath, layoutsDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot export to a location inside the layouts directory.");
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(_layoutsDirectory, zipPath);
        }

        public static int ImportAllLayouts(string zipPath)
        {
            int importedCount = 0;
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopSnap_Import_" + Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(zipPath, tempDir);
                
                var files = Directory.GetFiles(tempDir, "*.json");
                foreach (var file in files)
                {
                    var (status, _) = ImportLayout(file);
                    if (status == ImportStatus.Success || status == ImportStatus.Updated || status == ImportStatus.AsBackup)
                    {
                        importedCount++;
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            return importedCount;
        }

        public static void DeleteLayout(string id)
        {
            string file = Path.Combine(_layoutsDirectory, $"{id}.json");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public static void DeleteAllLayouts()
        {
            if (Directory.Exists(_layoutsDirectory))
            {
                var files = Directory.GetFiles(_layoutsDirectory, "*.json");
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch (Exception ex) { Debug.WriteLine($"Failed to delete {file}: {ex.Message}"); }
                }
            }
        }

        public static void RenameLayout(string id, string newName)
        {
            var layouts = GetAllLayouts();
            var layout = layouts.FirstOrDefault(l => l.Id == id);
            if (layout != null)
            {
                layout.Name = newName;
                SaveLayout(layout, false);
            }
        }

        public static void AutoSaveTemporary()
        {
            lock (_autoSaveLock)
            {
                var layouts = GetAllLayouts();
                var autoSaves = layouts.Where(l => l.Id.StartsWith("auto_") || l.Id == "temp_auto_save").OrderByDescending(l => l.SavedAt).ToList();
                var currentIcons = DesktopIconManager.GetIcons();

                if (autoSaves.Count > 0)
                {
                    var lastSaves = autoSaves.First();
                    if (lastSaves.Icons.Count == currentIcons.Count)
                    {
                        var lastSet = new HashSet<string>(lastSaves.Icons.Select(i => $"{i.Name}_{i.X}_{i.Y}"));
                        var currSet = new HashSet<string>(currentIcons.Select(i => $"{i.Name}_{i.X}_{i.Y}"));
                        if (lastSet.SetEquals(currSet))
                        {
                            // No layout change since last auto-save, skip creating redundant backup
                            return;
                        }
                    }
                }

                // Keep up to 3 historic auto saves (we create a new one, so keep 2 existing)
                if (autoSaves.Count >= 3)
                {
                    for (int i = 2; i < autoSaves.Count; i++) // Delete 3rd and older
                    {
                        DeleteLayout(autoSaves[i].Id);
                    }
                }

                var newAutoSave = new DesktopLayout
                {
                    Id = "auto_" + Guid.NewGuid().ToString(),
                    Name = I18n.Instance.AutoTempSave + $" ({DateTime.Now:MM-dd HH:mm})",
                    Icons = currentIcons,
                    CapturedDisplays = DisplayManager.GetDisplays()
                };
                SaveLayout(newAutoSave);
            }
        }

        public static void TogglePin(string id)
        {
            var layouts = GetAllLayouts();
            var layout = layouts.FirstOrDefault(l => l.Id == id);
            if (layout != null)
            {
                layout.IsPinned = !layout.IsPinned;
                if (layout.IsPinned)
                {
                    layout.PinnedAt = DateTime.Now;
                }
                else
                {
                    layout.PinnedAt = null;
                }
                SaveLayout(layout, false);
            }
        }

        public static void MovePinnedOrder(string id, bool up)
        {
            var layouts = GetAllLayouts().Where(l => l.IsPinned).OrderByDescending(l => l.PinnedAt ?? DateTime.MinValue).ToList();
            int index = layouts.FindIndex(l => l.Id == id);
            if (index == -1) return;

            if (up && index > 0)
            {
                var current = layouts[index];
                var target = layouts[index - 1];
                
                // Swap PinnedAt to swap positions
                var temp = current.PinnedAt;
                current.PinnedAt = target.PinnedAt;
                target.PinnedAt = temp;

                // Ensure they are not exactly the same if they were somehow
                if (current.PinnedAt == target.PinnedAt)
                {
                    current.PinnedAt = (current.PinnedAt ?? DateTime.Now).AddSeconds(1);
                }

                SaveLayout(current, false);
                SaveLayout(target, false);
            }
            else if (!up && index < layouts.Count - 1)
            {
                var current = layouts[index];
                var target = layouts[index + 1];
                
                var temp = current.PinnedAt;
                current.PinnedAt = target.PinnedAt;
                target.PinnedAt = temp;

                if (current.PinnedAt == target.PinnedAt)
                {
                    current.PinnedAt = (current.PinnedAt ?? DateTime.Now).AddSeconds(-1);
                }

                SaveLayout(current, false);
                SaveLayout(target, false);
            }
        }
    }
}
