using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BlendHub.Services
{
    public class AppSettings
    {
        public string BackupDirectory { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string DefaultPage { get; set; } = "home";
        public string LastRunVersion { get; set; } = string.Empty;
        public string LastBoardCleanupDate { get; set; } = string.Empty;
        public bool IsFirstRun { get; set; } = true;
        public bool AutoDetectBlenderVersion { get; set; } = true;
        public bool ExpandFoldersByDefault { get; set; } = false;
        public bool FilterNestedBlendFiles { get; set; } = false;
        public bool CategorizeProjectsByProgress { get; set; } = false;
        public System.Collections.Generic.List<string> CustomBlenderPaths { get; set; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> HiddenBlenderPaths { get; set; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> CustomScanFolders { get; set; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.Dictionary<string, string> BlenderLaunchArgs { get; set; } = new System.Collections.Generic.Dictionary<string, string>();
        public System.Collections.Generic.List<string> DefaultFolders { get; set; } = new System.Collections.Generic.List<string>
        {
            "Scenes", "Assets", "Images", "Source Images", "Hdri", "Clip", "Sound", "Scripts", "Movies"
        };
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> ProjectPresets { get; set; } = new()
        {
            { "Default", new() { "Scenes", "Assets", "Images", "Source Images", "Hdri", "Clip", "Sound", "Scripts", "Movies" } }
        };
        public string SelectedPreset { get; set; } = "Default";
        public System.Collections.Generic.Dictionary<string, string> DefaultLaunchers { get; set; } = new System.Collections.Generic.Dictionary<string, string>();
    }

    public class AppSettingsService
    {
        private static readonly string SettingsFilePath = GetSettingsPath();

        private static string GetSettingsPath()
        {
            try
            {
                return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "appsettings.json");
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            }
        }

        private static AppSettingsService? _instance;
        public static AppSettingsService Instance => _instance ??= new AppSettingsService();

        public AppSettings Settings { get; private set; } = new AppSettings();

        private AppSettingsService()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var data = JsonSerializer.Deserialize<AppSettings>(json);
                    if (data != null)
                    {
                        Settings = data;

                        // Migrate or initialize project presets
                        if (Settings.ProjectPresets == null || Settings.ProjectPresets.Count == 0)
                        {
                            Settings.ProjectPresets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>
                            {
                                { "Default", Settings.DefaultFolders ?? new System.Collections.Generic.List<string> { "Scenes", "Assets", "Images", "Source Images", "Hdri", "Clip", "Sound", "Scripts", "Movies" } }
                            };
                            Settings.SelectedPreset = "Default";
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(Settings.BackupDirectory))
            {
                Settings.BackupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BlendHub");
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }

        public bool IsAppUpdated()
        {
            var currentVersion = GetCurrentVersion();
            var lastVersion = Settings.LastRunVersion;

            if (string.IsNullOrEmpty(lastVersion))
            {
                // First time running the app
                Settings.LastRunVersion = currentVersion;
                Save();
                return true;
            }

            if (currentVersion != lastVersion)
            {
                // App has been updated
                Settings.LastRunVersion = currentVersion;
                Save();
                return true;
            }

            return false;
        }

        private string GetCurrentVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                return assembly.GetName().Version?.ToString() ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }
}
