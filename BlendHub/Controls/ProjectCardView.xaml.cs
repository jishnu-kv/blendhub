using BlendHub.Models;
using BlendHub.Pages;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BlendHub.Controls
{
    public sealed partial class ProjectCardView : UserControl
    {
        private readonly BlenderSettingsService _blenderService = new();

        public static readonly DependencyProperty ProjectProperty =
            DependencyProperty.Register("Project", typeof(Project), typeof(ProjectCardView), new PropertyMetadata(null, OnProjectChanged));

        private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProjectCardView card)
            {
                card.UpdateProject(e.OldValue as Project, e.NewValue as Project);
            }
        }

        public Project Project
        {
            get => (Project)GetValue(ProjectProperty);
            set => SetValue(ProjectProperty, value);
        }

        private void UpdateProject(Project? oldProject, Project? newProject)
        {
            if (newProject != null)
            {
                _currentProjectPath = newProject.FullBlendPath;

                // Immediately reset thumbnail when recycled for a different project to prevent flickering/swapping
                if (ThumbnailImage != null && (oldProject == null || newProject.Name != oldProject.Name || newProject.Path != oldProject.Path))
                {
                    ThumbnailImage.Source = null;
                    Debug.WriteLine($"[ProjectCard] Recycled: Reset thumbnail to null for new project: {newProject.Name} (was: {oldProject?.Name ?? "none"})");
                }

                // Try to load instantly from cache or load async
                LoadThumbnailForProject(newProject);
            }
            else
            {
                _currentProjectPath = string.Empty;
                if (ThumbnailImage != null)
                {
                    ThumbnailImage.Source = null;
                }
            }
        }

        private string _currentProjectPath = string.Empty;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> ThumbnailCache = new();

        public ProjectCardView()
        {
            this.InitializeComponent();
            this.Unloaded += ProjectCardView_Unloaded;
        }

        private void ProjectCardView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clear thumbnail when control is unloaded
            if (ThumbnailImage != null)
            {
                ThumbnailImage.Source = null;
            }
        }

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null) return;

            // Navigate to detail page using centralized MainWindow
            App.MainWindow.Navigate(typeof(ProjectDetailPage), Project);
        }

        private void Thumbnail_Loaded(object sender, RoutedEventArgs e)
        {
            // Trigger thumbnail loading when the image control is loaded
            if (Project != null)
            {
                Debug.WriteLine($"[ProjectCard] ThumbnailImage loaded, triggering load for: {Project.Name}");
                LoadThumbnailForProject(Project);
            }
        }

        private void LoadThumbnailForProject(Project project)
        {
            var path = project.FullBlendPath;

            // Check in-memory cache first for instant layout rendering
            if (ThumbnailCache.TryGetValue(path, out var cachedImage))
            {
                ThumbnailImage.Source = cachedImage;
                Debug.WriteLine($"[ProjectCard] Loaded thumbnail from cache instantly for: {project.Name}");
                return;
            }

            // Don't set default thumbnail here - let it remain as is to avoid flickering
            if (ThumbnailImage.Source == null)
            {
                Debug.WriteLine($"[ProjectCard] Thumbnail source is null for: {project.Name} - will load actual thumbnail from disk");
            }

            _ = LoadThumbnailAsync(project);
        }

        private async Task LoadThumbnailAsync(Project project)
        {
            var path = project.FullBlendPath;

            if (!File.Exists(path))
            {
                Debug.WriteLine($"[ProjectCard] Blend file not found: {path}");
                return;
            }

            Windows.Storage.FileProperties.StorageItemThumbnail? thumbnail = null;

            try
            {
                var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                thumbnail = await storageFile.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 48);

                // Check if this card is still bound to the same project path
                if (_currentProjectPath != path || Project?.FullBlendPath != path)
                {
                    Debug.WriteLine($"[ProjectCard] Project changed during thumbnail loading, skipping update for: {project.Name}");
                    return;
                }

                if (thumbnail != null)
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.DecodePixelWidth = 48;
                    bitmapImage.DecodePixelHeight = 48;
                    await bitmapImage.SetSourceAsync(thumbnail.AsStreamForRead().AsRandomAccessStream());

                    // Final check before setting source and caching in-memory
                    if (_currentProjectPath == path && Project?.FullBlendPath == path)
                    {
                        ThumbnailImage.Source = bitmapImage;
                        ThumbnailCache[path] = bitmapImage; // Cache it!
                        Debug.WriteLine($"[ProjectCard] Successfully loaded and cached thumbnail for: {project.Name}");
                    }
                    else
                    {
                        Debug.WriteLine($"[ProjectCard] Project changed during bitmap creation, skipping update for: {project.Name}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[ProjectCard] No thumbnail available for: {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectCard] Error loading thumbnail for {path}: {ex.Message}");
                if (_currentProjectPath == path && Project?.FullBlendPath == path)
                {
                    ThumbnailImage.Source = null;
                }
            }
            finally
            {
                thumbnail?.Dispose();
            }
        }



        public static void ClearThumbnailCache()
        {
            ThumbnailCache.Clear();
            Debug.WriteLine("[ProjectCard] Thumbnail cache cleared.");
        }



        private void RequestRefresh()
        {
            try
            {
                if (App.MainWindow.ContentFrame.Content is Page page)
                {
                    var method = page.GetType().GetMethod("LoadProjects") ?? page.GetType().GetMethod("LoadRecentProjects");
                    if (method != null)
                    {
                        Debug.WriteLine($"[ProjectCard] Invoking refresh method: {method.Name} on page: {page.GetType().Name}");
                        method.Invoke(page, null);
                    }
                    else
                    {
                        Debug.WriteLine($"[ProjectCard] No refresh method found on page: {page.GetType().Name}");
                    }
                }
                else
                {
                    Debug.WriteLine("[ProjectCard] No active page found for refresh");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectCard] Error during refresh: {ex.Message}");
            }
        }
    }
}
