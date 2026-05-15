using System;
using System.IO;
using System.Text.Json;

namespace DesktopSnap
{
    public class AppSettings
    {
        public string Language { get; set; } = "auto";
        public bool AutoStart { get; set; } = false;
        public bool CloseToTray { get; set; } = true;
        public string SaveHotkey { get; set; } = "Ctrl+Alt+S";
        public string RestoreHotkey { get; set; } = "Ctrl+Alt+R";
        // Empty string means "auto" (most recent user snapshot).
        // A non-empty GUID means the user has designated a specific layout as the restore target.
        public string RestoreTargetId { get; set; } = "";
        public bool IsFirstRun { get; set; } = true;
        public bool HasShownTrayNotification { get; set; } = false;
        public bool AutoSaveOnDisplayChange { get; set; } = false;
        public bool EnableAutoUpdate { get; set; } = true;
    }

    public static class SettingsManager
    {
        private static string _settingsFile;
        private static AppSettings _cachedSettings;
        private static readonly object _lock = new object();

        static SettingsManager()
        {
            _settingsFile = Path.Combine(AppEnv.GetDataDirectory(), "settings.json");
            
            // If running in Packaged mode, check if we need to migrate from a local portable 'Config' folder
            if (AppEnv.IsPackaged)
            {
                string portableDir = Path.Combine(AppContext.BaseDirectory, "Config", "settings.json");
                if (File.Exists(portableDir) && !File.Exists(_settingsFile))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(_settingsFile);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.Copy(portableDir, _settingsFile);
                    }
                    catch { }
                }
            }
        }

        public static AppSettings Load()
        {
            lock (_lock)
            {
                if (_cachedSettings != null)
                {
                    return CloneSettings(_cachedSettings);
                }

                if (File.Exists(_settingsFile))
                {
                    try 
                    { 
                        _cachedSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile)) ?? new AppSettings(); 
                        return CloneSettings(_cachedSettings);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SettingsManager Error: {ex}"); }
                }
                _cachedSettings = new AppSettings();
                return CloneSettings(_cachedSettings);
            }
        }

        public static void Save(AppSettings settings)
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_settingsFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                // Update cache
                _cachedSettings = CloneSettings(settings);
                
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            // Simple clone via JSON to prevent references leaking out and being modified without Save()
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<AppSettings>(json);
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
