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
                var recentProjects = projects.OrderByDescending(p => p.CreatedAt).Take(5).ToList();

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

        private void LoadVersions()
        {
            try
            {
                var versions = _blenderService.GetInstalledVersions();
                VersionsGridView.ItemsSource = versions;
                VersionsGridView.Visibility = versions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                NoVersionsPanel.Visibility = versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        private void CreateProjectQuickAction_Click(object sender, RoutedEventArgs e)
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

        private void ReferenceBoardQuickAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainWindow?.Navigate(typeof(BlendHub.ReferenceBoard.ReferenceBoard));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
            }
        }

        private void ManageAddonsQuickAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainWindow?.Navigate(typeof(AddonsPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
            }
        }

        private void DownloadBlenderQuickAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainWindow?.Navigate(typeof(DownloadPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
            }
        }

        private void CreateBackupQuickAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainWindow?.Navigate(typeof(BackupPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Navigation failed: {ex.Message}");
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
}
