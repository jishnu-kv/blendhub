using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class HomePage : Page
    {
        private readonly BlenderSettingsService _blenderService = new();
        public ObservableCollection<Project> RecentProjects { get; } = new ObservableCollection<Project>();
        public ObservableCollection<BlenderVersionInfo> BlenderVersions { get; } = new ObservableCollection<BlenderVersionInfo>();


        public HomePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.Bindings.Update();
            LoadVersions();
            LoadRecentProjects();
        }

        public void LoadRecentProjects()
        {
            try
            {
                var projects = ProjectService.LoadProjects();
                var recentProjects = projects.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.CreatedAt).Take(5).ToList();

                Debug.WriteLine($"[HomePage] Loading {recentProjects.Count} recent projects from {projects.Count} total projects");

                RecentProjects.Clear();
                foreach (var project in recentProjects)
                {
                    RecentProjects.Add(project);
                }
                RecentProjectsList.Visibility = RecentProjects.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                NoRecentProjectsPanel.Visibility = RecentProjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                Debug.WriteLine($"[HomePage] Recent projects UI updated with {RecentProjects.Count} items");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Error loading recent projects: {ex.Message}");
            }
        }

        private async void LoadVersions()
        {
            try
            {
                var versions = await _blenderService.GetInstalledVersionsAsync();
                BlenderVersions.Clear();
                foreach (var version in versions)
                {
                    BlenderVersions.Add(version);
                }
                VersionsGridView.Visibility = BlenderVersions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                NoVersionsPanel.Visibility = BlenderVersions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Error loading Blender versions: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadVersions();
        }

        private void ViewAllProjects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainWindow?.Navigate(typeof(ProjectPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
            }
        }

        public System.Collections.Generic.List<QuickActionItem> QuickActions { get; } = new()
        {
            new QuickActionItem { Title = "Create Project", Description = "Set up a new workspace & blend file", Glyph = "\uE710", AutomationId = "BtnCreateProject", NavigationTag = "create_project" },
            new QuickActionItem { Title = "Manage Addons", Description = "Install and configure blender add-ons", Glyph = "\uE74C", AutomationId = "BtnManageAddons", NavigationTag = "manage_addons" },
            new QuickActionItem { Title = "Download Blender", Description = "Get latest or stable blender versions", Glyph = "\uE896", AutomationId = "BtnDownloadBlender", NavigationTag = "download_blender" },
            new QuickActionItem { Title = "Create Backup", Description = "Save preferences, addons & settings", Glyph = "\uE74E", AutomationId = "BtnCreateBackup", NavigationTag = "create_backup" }
        };

        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is QuickActionItem item)
            {
                try
                {
                    switch (item.NavigationTag)
                    {
                        case "create_project":
                            App.MainWindow?.Navigate(typeof(ProjectPage), "create");
                            break;
                        case "reference_board":
                            App.MainWindow?.Navigate(typeof(BlendHub.ReferenceBoard.ReferenceBoard));
                            break;
                        case "manage_addons":
                            App.MainWindow?.Navigate(typeof(AddonsPage));
                            break;
                        case "download_blender":
                            App.MainWindow?.Navigate(typeof(DownloadPage));
                            break;
                        case "create_backup":
                            App.MainWindow?.Navigate(typeof(BackupPage));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
                }
            }
        }

        private void OpenVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BlenderVersionInfo info)
            {
                try
                {
                    _blenderService.LaunchBlender(info.ExecutablePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HomePage] Error launching Blender: {ex.Message}");
                }
            }
        }

        private BlenderVersionInfo? GetVersionInfoFromSender(object sender)
        {
            if (sender is FrameworkElement fe)
            {
                if (fe.DataContext is BlenderVersionInfo dInfo) return dInfo;
                if (fe.Tag is BlenderVersionInfo tInfo) return tInfo;
            }
            if (sender is MenuFlyoutItem mfi && mfi.Tag is BlenderVersionInfo mInfo)
            {
                return mInfo;
            }
            return null;
        }

        private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
        {
            var info = GetVersionInfoFromSender(sender);
            if (info != null)
            {
                try
                {
                    _blenderService.OpenConfigFolder(info.ConfigPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HomePage] Error opening config folder: {ex.Message}");
                }
            }
        }

        private async void LocateExecutable_Click(object sender, RoutedEventArgs e)
        {
            var info = GetVersionInfoFromSender(sender);
            if (info == null) return;

            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add(".exe");

                BlendHub.Helpers.WindowHelper.InitializeWithWindow(picker);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var selectedExe = file.Path;
                    info.ExecutablePath = selectedExe;

                    // Add to CustomBlenderPaths in settings so it persists
                    var settings = AppSettingsService.Instance.Settings;
                    if (!settings.CustomBlenderPaths.Contains(selectedExe, StringComparer.OrdinalIgnoreCase))
                    {
                        settings.CustomBlenderPaths.Add(selectedExe);
                    }

                    // Remove from HiddenBlenderPaths if it was previously hidden
                    settings.HiddenBlenderPaths.RemoveAll(p =>
                        p.Equals(selectedExe, StringComparison.OrdinalIgnoreCase) ||
                        p.Equals(info.IdentificationKey, StringComparison.OrdinalIgnoreCase) ||
                        p.Equals(info.Version, StringComparison.OrdinalIgnoreCase));

                    AppSettingsService.Instance.Save();

                    // Reload list to update UI bindings
                    LoadVersions();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Error locating executable: {ex.Message}");
            }
        }

        private void RemoveVersion_Click(object sender, RoutedEventArgs e)
        {
            var info = GetVersionInfoFromSender(sender);
            if (info == null) return;

            try
            {
                var settings = AppSettingsService.Instance.Settings;
                var key = info.IdentificationKey;

                if (!string.IsNullOrEmpty(key) && !settings.HiddenBlenderPaths.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    settings.HiddenBlenderPaths.Add(key);
                }

                if (!string.IsNullOrEmpty(info.ExecutablePath) && !settings.HiddenBlenderPaths.Contains(info.ExecutablePath, StringComparer.OrdinalIgnoreCase))
                {
                    settings.HiddenBlenderPaths.Add(info.ExecutablePath);
                }

                if (!string.IsNullOrEmpty(info.ConfigPath) && !settings.HiddenBlenderPaths.Contains(info.ConfigPath, StringComparer.OrdinalIgnoreCase))
                {
                    settings.HiddenBlenderPaths.Add(info.ConfigPath);
                }

                if (!string.IsNullOrEmpty(info.Version) && !settings.HiddenBlenderPaths.Contains(info.Version, StringComparer.OrdinalIgnoreCase))
                {
                    settings.HiddenBlenderPaths.Add(info.Version);
                }

                AppSettingsService.Instance.Save();

                // Remove from current in-memory collection
                BlenderVersions.Remove(info);

                VersionsGridView.Visibility = BlenderVersions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                NoVersionsPanel.Visibility = BlenderVersions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Error removing version: {ex.Message}");
            }
        }

        private void CreateProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainWindow?.Navigate(typeof(ProjectPage), "create");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
            }
        }

        private async void OpenBlendFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder
                };
                picker.FileTypeFilter.Add(".blend");
                BlendHub.Helpers.WindowHelper.InitializeWithWindow(picker);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    // Check if parent directory or file can be launched or imported
                    var parentFolder = await file.GetParentAsync();
                    if (parentFolder != null)
                    {
                        var folderPath = parentFolder.Path;
                        var projects = ProjectService.LoadProjects();
                        var existing = projects.FirstOrDefault(p => p.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase));

                        if (existing == null)
                        {
                            var newProject = new Project
                            {
                                Name = parentFolder.Name,
                                Path = folderPath,
                                BlendFileName = file.Name,
                                CreatedAt = DateTime.Now
                            };
                            projects.Add(newProject);
                            ProjectService.SaveProjects(projects);
                        }

                        // Launch blend file directly
                        Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
                        LoadRecentProjects();
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Error opening blend file: {ex.Message}");
            }
        }

        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                if (DragOverlay != null)
                {
                    DragOverlay.Visibility = Visibility.Visible;
                }
            }
        }

        private void DragOverlay_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Add to BlendHub";
            }
        }

        private void DragOverlay_DragLeave(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void DragOverlay_Drop(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }

            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is Windows.Storage.StorageFolder folder)
                    {
                        await ProcessDroppedFolderAsync(folder);
                    }
                    else if (item is Windows.Storage.StorageFile file && file.FileType.Equals(".blend", StringComparison.OrdinalIgnoreCase))
                    {
                        var parentFolder = await file.GetParentAsync();
                        if (parentFolder != null)
                        {
                            await ProcessDroppedFolderAsync(parentFolder);
                        }
                    }
                }
            }
        }

        private async Task ProcessDroppedFolderAsync(Windows.Storage.StorageFolder folder)
        {
            if (folder == null) return;
            string path = folder.Path;

            try
            {
                var (rootBlendFiles, allBlendFiles) = await Task.Run(() =>
                {
                    var rbf = System.IO.Directory.GetFiles(path, "*.blend");
                    var abf = rbf.Length == 0 ? System.IO.Directory.GetFiles(path, "*.blend", System.IO.SearchOption.AllDirectories) : Array.Empty<string>();
                    return (rbf, abf);
                });

                string chosenBlendFile = string.Empty;
                if (rootBlendFiles.Length > 0)
                {
                    // Pick the most recent .blend file
                    var mostRecent = rootBlendFiles
                        .Select(fp => new System.IO.FileInfo(fp))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();
                    chosenBlendFile = mostRecent?.Name ?? System.IO.Path.GetFileName(rootBlendFiles[0]);
                }
                else if (allBlendFiles.Length > 0)
                {
                    var mostRecent = allBlendFiles
                        .Select(fp => new System.IO.FileInfo(fp))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();
                    chosenBlendFile = mostRecent != null ? System.IO.Path.GetRelativePath(path, mostRecent.FullName) : System.IO.Path.GetRelativePath(path, allBlendFiles[0]);
                }

                var projects = ProjectService.LoadProjects();
                var existing = projects.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(chosenBlendFile))
                    {
                        existing.BlendFileName = chosenBlendFile;
                        ProjectService.SaveProjects(projects);
                    }
                }
                else
                {
                    var newProject = new Project
                    {
                        Name = folder.Name,
                        Path = path,
                        BlendFileName = chosenBlendFile,
                        CreatedAt = DateTime.Now
                    };
                    projects.Insert(0, newProject);
                    ProjectService.SaveProjects(projects);
                }

                LoadRecentProjects();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Error processing dropped folder: {ex.Message}");
            }
        }
    }

    public class QuickActionItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
        public string AutomationId { get; set; } = string.Empty;
        public string NavigationTag { get; set; } = string.Empty;
    }
}
