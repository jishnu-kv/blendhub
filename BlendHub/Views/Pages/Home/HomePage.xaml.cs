using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

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

        private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BlenderVersionInfo info)
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
