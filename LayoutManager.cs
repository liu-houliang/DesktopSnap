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
                        layouts.Add(layout);
                    }
                }
                catch { }
            }
            return layouts.OrderByDescending(l => l.SavedAt).ToList();
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

        public static void AutoSaveTemporary()
        {
            var layouts = GetAllLayouts();
            var tempSlot = layouts.FirstOrDefault(l => l.Id == "temp_auto_save");
            if (tempSlot == null)
            {
                tempSlot = new DesktopLayout { Id = "temp_auto_save", Name = "🔄 临时自动备份 (启动时)" };
            }
            tempSlot.Icons = DesktopIconManager.GetIcons();
            SaveLayout(tempSlot);
        }
    }
}
