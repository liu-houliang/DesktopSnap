using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DesktopSnap
{
    public class DesktopLayout
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public List<IconInfo> Icons { get; set; } = new List<IconInfo>();
        public string SavedTime => SavedAt.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static class LayoutManager
    {
        private static string _layoutsDirectory;

        static LayoutManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _layoutsDirectory = Path.Combine(appData, "DesktopSnap", "Layouts");
            Directory.CreateDirectory(_layoutsDirectory);
        }

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
                        if (layout.Id.StartsWith("auto_") || layout.Id == "temp_auto_save")
                        {
                            layout.Name = I18n.Instance.AutoTempSave + " (" + layout.SavedAt.ToString("MM-dd HH:mm") + ")";
                        }
                        layouts.Add(layout);
                    }
                }
                catch { }
            }
            return layouts.OrderByDescending(l => l.Id.StartsWith("auto_") || l.Id == "temp_auto_save").ThenByDescending(l => l.SavedAt).ToList();
        }

        public static void SaveLayout(DesktopLayout layout)
        {
            layout.SavedAt = DateTime.Now; // Update time
            string file = Path.Combine(_layoutsDirectory, $"{layout.Id}.json");
            string json = JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }

        public static void DeleteLayout(string id)
        {
            string file = Path.Combine(_layoutsDirectory, $"{id}.json");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public static void RenameLayout(string id, string newName)
        {
            var layouts = GetAllLayouts();
            var layout = layouts.FirstOrDefault(l => l.Id == id);
            if (layout != null)
            {
                layout.Name = newName;
                SaveLayout(layout);
            }
        }

        public static void AutoSaveTemporary()
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
                Icons = currentIcons
            };
            SaveLayout(newAutoSave);
        }
    }
}
