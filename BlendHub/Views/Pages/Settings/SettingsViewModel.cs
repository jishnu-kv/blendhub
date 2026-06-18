#pragma warning disable MVVMTK0045

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlendHub.Models;
using BlendHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlendHub.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        public ObservableCollection<FileLauncher> Launchers { get; } = new ObservableCollection<FileLauncher>();
        public ObservableCollection<ProjectFolder> DefaultFolders { get; } = new ObservableCollection<ProjectFolder>();
        public ObservableCollection<BlenderInstallationViewModel> BlenderInstallations { get; } = new ObservableCollection<BlenderInstallationViewModel>();
        public ObservableCollection<ScanFolderInfo> CustomScanFolders { get; } = new ObservableCollection<ScanFolderInfo>();
        public ObservableCollection<string> Presets { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _backupDirectory = string.Empty;

        partial void OnBackupDirectoryChanged(string value)
        {
            AppSettingsService.Instance.Settings.BackupDirectory = value;
            AppSettingsService.Instance.Save();
        }

        [ObservableProperty]
        private bool _autoDetectVersion;

        partial void OnAutoDetectVersionChanged(bool value)
        {
            AppSettingsService.Instance.Settings.AutoDetectBlenderVersion = value;
            AppSettingsService.Instance.Save();
        }

        [ObservableProperty]
        private bool _expandFoldersByDefault;

        partial void OnExpandFoldersByDefaultChanged(bool value)
        {
            AppSettingsService.Instance.Settings.ExpandFoldersByDefault = value;
            AppSettingsService.Instance.Save();
        }

        [ObservableProperty]
        private bool _filterNestedBlendFiles;

        partial void OnFilterNestedBlendFilesChanged(bool value)
        {
            AppSettingsService.Instance.Settings.FilterNestedBlendFiles = value;
            AppSettingsService.Instance.Save();
        }

        [ObservableProperty]
        private bool _categorizeProjectsByProgress;

        partial void OnCategorizeProjectsByProgressChanged(bool value)
        {
            AppSettingsService.Instance.Settings.CategorizeProjectsByProgress = value;
            AppSettingsService.Instance.Save();
        }

        [ObservableProperty]
        private string _selectedPreset = "Default";

        partial void OnSelectedPresetChanged(string value)
        {
            AppSettingsService.Instance.Settings.SelectedPreset = value;
            AppSettingsService.Instance.Save();
            LoadDefaultFolders();
        }

        public SettingsViewModel()
        {
            LoadAllSettings();
        }

        public void LoadAllSettings()
        {
            LoadGeneralSettings();
            LoadLaunchers();
            LoadPresets();
            LoadDefaultFolders();
            LoadCustomScanFolders();
        }

        public async Task LoadBlenderInstallationsAsync()
        {
            foreach (var vm in BlenderInstallations)
            {
                vm.PropertyChanged -= BlenderInstallation_PropertyChanged;
            }
            BlenderInstallations.Clear();

            var service = new BlenderSettingsService();
            var allVersions = await service.GetInstalledVersionsAsync(includeHidden: true);
            var settings = AppSettingsService.Instance.Settings;

            foreach (var v in allVersions)
            {
                bool isCustom = settings.CustomBlenderPaths.Contains(v.ExecutablePath);
                bool isVisible = !settings.HiddenBlenderPaths.Contains(v.ExecutablePath);
                string? args = string.Empty;
                if (settings.BlenderLaunchArgs != null)
                {
                    settings.BlenderLaunchArgs.TryGetValue(v.ExecutablePath, out args);
                }

                var vm = new BlenderInstallationViewModel
                {
                    DisplayName = v.DisplayName,
                    ExecutablePath = v.ExecutablePath,
                    Version = v.Version,
                    IsCustom = isCustom,
                    IsVisible = isVisible,
                    LaunchArguments = args ?? string.Empty
                };

                vm.PropertyChanged += BlenderInstallation_PropertyChanged;
                BlenderInstallations.Add(vm);
            }
        }

        private void BlenderInstallation_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not BlenderInstallationViewModel vm) return;

            var settings = AppSettingsService.Instance.Settings;
            if (e.PropertyName == nameof(BlenderInstallationViewModel.IsVisible))
            {
                if (vm.IsVisible)
                {
                    settings.HiddenBlenderPaths.Remove(vm.ExecutablePath);
                }
                else
                {
                    if (!settings.HiddenBlenderPaths.Contains(vm.ExecutablePath))
                    {
                        settings.HiddenBlenderPaths.Add(vm.ExecutablePath);
                    }
                }
                AppSettingsService.Instance.Save();
            }
            else if (e.PropertyName == nameof(BlenderInstallationViewModel.LaunchArguments))
            {
                if (settings.BlenderLaunchArgs == null)
                {
                    settings.BlenderLaunchArgs = new System.Collections.Generic.Dictionary<string, string>();
                }
                settings.BlenderLaunchArgs[vm.ExecutablePath] = vm.LaunchArguments;
                AppSettingsService.Instance.Save();
            }
        }

        private void LoadGeneralSettings()
        {
            var settings = AppSettingsService.Instance.Settings;
            BackupDirectory = settings.BackupDirectory;
            AutoDetectVersion = settings.AutoDetectBlenderVersion;
            ExpandFoldersByDefault = settings.ExpandFoldersByDefault;
            FilterNestedBlendFiles = settings.FilterNestedBlendFiles;
            CategorizeProjectsByProgress = settings.CategorizeProjectsByProgress;
            SelectedPreset = settings.SelectedPreset;
        }

        // --- File Launchers ---
        public void LoadLaunchers()
        {
            Launchers.Clear();
            var defaultLaunchers = AppSettingsService.Instance.Settings.DefaultLaunchers;
            foreach (var kvp in defaultLaunchers)
            {
                Launchers.Add(new FileLauncher
                {
                    Extension = kvp.Key,
                    ProgramPath = kvp.Value,
                    ProgramName = System.IO.Path.GetFileNameWithoutExtension(kvp.Value)
                });
            }

            if (Launchers.Count == 0)
            {
                Launchers.Add(new FileLauncher
                {
                    Extension = ".psd",
                    ProgramPath = "",
                    ProgramName = ""
                });
            }
        }

        public void SaveLaunchers()
        {
            var service = AppSettingsService.Instance;
            service.Settings.DefaultLaunchers.Clear();
            foreach (var launcher in Launchers)
            {
                if (!string.IsNullOrWhiteSpace(launcher.Extension))
                {
                    string ext = launcher.Extension.StartsWith(".") ? launcher.Extension : "." + launcher.Extension;
                    service.Settings.DefaultLaunchers[ext.ToLowerInvariant()] = launcher.ProgramPath ?? "";
                }
            }
            service.Save();
        }

        public void AddLauncher()
        {
            Launchers.Add(new FileLauncher());
        }

        public void RemoveLauncher(FileLauncher launcher)
        {
            Launchers.Remove(launcher);
            SaveLaunchers();
        }

        // --- Presets & Default Folders ---
        public void LoadPresets()
        {
            Presets.Clear();
            var settings = AppSettingsService.Instance.Settings;
            foreach (var preset in settings.ProjectPresets.Keys)
            {
                Presets.Add(preset);
            }
        }

        public void LoadDefaultFolders()
        {
            DefaultFolders.Clear();
            var settings = AppSettingsService.Instance.Settings;
            if (!settings.ProjectPresets.TryGetValue(settings.SelectedPreset, out var folders))
            {
                folders = settings.ProjectPresets["Default"];
                AppSettingsService.Instance.Settings.SelectedPreset = "Default";
                SelectedPreset = "Default";
            }
            for (int i = 0; i < folders.Count; i++)
            {
                var folder = new ProjectFolder($"Folder {i + 1}:", folders[i]);
                folder.PropertyChanged += DefaultFolder_PropertyChanged;
                DefaultFolders.Add(folder);
            }
        }

        public void SaveDefaultFolders()
        {
            var service = AppSettingsService.Instance;
            var folders = DefaultFolders.Select(f => f.Name).ToList();
            service.Settings.ProjectPresets[service.Settings.SelectedPreset] = folders;
            if (service.Settings.SelectedPreset == "Default")
            {
                service.Settings.DefaultFolders = folders;
            }
            service.Save();
        }

        public void AddFolder()
        {
            var folder = new ProjectFolder($"Folder {DefaultFolders.Count + 1}:", "");
            folder.PropertyChanged += DefaultFolder_PropertyChanged;
            DefaultFolders.Add(folder);
            SaveDefaultFolders();
        }

        public void RemoveFolder(ProjectFolder folder)
        {
            folder.PropertyChanged -= DefaultFolder_PropertyChanged;
            DefaultFolders.Remove(folder);
            UpdateFolderLabels();
            SaveDefaultFolders();
        }

        public void UpdateFolderLabels()
        {
            for (int i = 0; i < DefaultFolders.Count; i++)
            {
                DefaultFolders[i].Label = $"Folder {i + 1}:";
            }
        }

        private void DefaultFolder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectFolder.Name))
            {
                SaveDefaultFolders();
            }
        }

        // --- Custom Scan Folders ---
        public void LoadCustomScanFolders()
        {
            foreach (var folder in CustomScanFolders)
            {
                folder.PropertyChanged -= ScanFolder_PropertyChanged;
            }
            CustomScanFolders.Clear();

            var paths = AppSettingsService.Instance.Settings.CustomScanFolders;
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    var info = new ScanFolderInfo(path);
                    info.PropertyChanged += ScanFolder_PropertyChanged;
                    CustomScanFolders.Add(info);
                }
            }
        }

        public async Task SaveCustomScanFoldersAsync()
        {
            AppSettingsService.Instance.Settings.CustomScanFolders = CustomScanFolders
                .Select(f => f.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            AppSettingsService.Instance.Save();

            await LoadBlenderInstallationsAsync();
        }

        private async void ScanFolder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            await SaveCustomScanFoldersAsync();
        }

        public async Task AddCustomScanFolderAsync(string path)
        {
            if (!CustomScanFolders.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                var info = new ScanFolderInfo(path);
                info.PropertyChanged += ScanFolder_PropertyChanged;
                CustomScanFolders.Add(info);
                await SaveCustomScanFoldersAsync();
            }
        }

        public async Task RemoveCustomScanFolderAsync(ScanFolderInfo info)
        {
            info.PropertyChanged -= ScanFolder_PropertyChanged;
            CustomScanFolders.Remove(info);
            await SaveCustomScanFoldersAsync();
        }

        public async Task AddCustomBlenderAsync(string path)
        {
            var settings = AppSettingsService.Instance.Settings;
            if (!settings.CustomBlenderPaths.Contains(path))
            {
                settings.CustomBlenderPaths.Add(path);
                AppSettingsService.Instance.Save();
                await LoadBlenderInstallationsAsync();
            }
        }

        public async Task RemoveBlenderInstallationAsync(BlenderInstallationViewModel vm)
        {
            var settings = AppSettingsService.Instance.Settings;
            settings.CustomBlenderPaths.Remove(vm.ExecutablePath);
            settings.HiddenBlenderPaths.Remove(vm.ExecutablePath);
            if (settings.BlenderLaunchArgs != null)
            {
                settings.BlenderLaunchArgs.Remove(vm.ExecutablePath);
            }
            AppSettingsService.Instance.Save();
            await LoadBlenderInstallationsAsync();
        }

        public void ExportSettings(string jsonPath)
        {
            var settings = AppSettingsService.Instance.Settings;
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, json);
        }

        public async Task ImportSettingsAsync(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            var importedSettings = JsonSerializer.Deserialize<AppSettings>(json);
            if (importedSettings != null)
            {
                var target = AppSettingsService.Instance.Settings;
                target.BackupDirectory = importedSettings.BackupDirectory ?? target.BackupDirectory;
                target.UserName = importedSettings.UserName ?? target.UserName;
                target.DefaultPage = importedSettings.DefaultPage ?? target.DefaultPage;
                target.LastRunVersion = importedSettings.LastRunVersion ?? target.LastRunVersion;
                target.LastBoardCleanupDate = importedSettings.LastBoardCleanupDate ?? target.LastBoardCleanupDate;
                target.IsFirstRun = importedSettings.IsFirstRun;
                target.AutoDetectBlenderVersion = importedSettings.AutoDetectBlenderVersion;
                target.ExpandFoldersByDefault = importedSettings.ExpandFoldersByDefault;
                target.FilterNestedBlendFiles = importedSettings.FilterNestedBlendFiles;
                target.CategorizeProjectsByProgress = importedSettings.CategorizeProjectsByProgress;
                target.CustomBlenderPaths = importedSettings.CustomBlenderPaths ?? new System.Collections.Generic.List<string>();
                target.HiddenBlenderPaths = importedSettings.HiddenBlenderPaths ?? new System.Collections.Generic.List<string>();
                target.CustomScanFolders = importedSettings.CustomScanFolders ?? new System.Collections.Generic.List<string>();
                target.BlenderLaunchArgs = importedSettings.BlenderLaunchArgs ?? new System.Collections.Generic.Dictionary<string, string>();
                target.DefaultFolders = importedSettings.DefaultFolders ?? target.DefaultFolders;
                target.ProjectPresets = importedSettings.ProjectPresets ?? target.ProjectPresets;
                target.SelectedPreset = importedSettings.SelectedPreset ?? target.SelectedPreset;
                target.DefaultLaunchers = importedSettings.DefaultLaunchers ?? new System.Collections.Generic.Dictionary<string, string>();

                AppSettingsService.Instance.Save();

                // Reload properties in VM
                LoadAllSettings();
                await LoadBlenderInstallationsAsync();
            }
            else
            {
                throw new Exception("The selected file is not a valid settings file.");
            }
        }
    }
}
