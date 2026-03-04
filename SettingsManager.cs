using System;
using System.IO;
using System.Text.Json;

namespace DesktopSnap
{
    public class AppSettings
    {
        public string Language { get; set; } = "auto";
    }

    public static class SettingsManager
    {
        private static string _settingsFile;

        static SettingsManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsFile = Path.Combine(appData, "DesktopSnap", "settings.json");
        }

        public static AppSettings Load()
        {
            if (File.Exists(_settingsFile))
            {
                try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile)) ?? new AppSettings(); }
                catch { }
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings));
        }

        public static void ApplySettings()
        {
            var settings = Load();
            if (settings.Language == "auto")
            {
                I18n.Instance.InitializeFromSystem();
            }
            else
            {
                I18n.Instance.CurrentLanguage = settings.Language;
            }
        }
    }
}
