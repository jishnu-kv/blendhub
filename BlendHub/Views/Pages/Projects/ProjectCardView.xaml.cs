using BlendHub.Models;
using BlendHub.Pages;
using BlendHub.Services;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Controls
{
    public sealed partial class ProjectCardView : UserControl, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

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

                // Expand expander by default if settings specify it
                if (CardExpander != null && AppSettingsService.Instance.Settings.ExpandFoldersByDefault)
                {
                    CardExpander.IsExpanded = true;
                }

                // If expander is already open, reload details
                if (CardExpander != null && CardExpander.IsExpanded)
                {
                    LoadProjectDetails();
                }
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

        // ----------------------------------------------------
        // Detail page collections and fields ported to CardView
        // ----------------------------------------------------
        private ObservableCollection<ProjectItemViewModel> _items = new();
        private ObservableCollection<ProjectItemViewModel> _taskItems = new();

        private List<ProjectItemViewModel> _allItems = new();
        private List<ProjectItemViewModel> _allTaskItems = new();

        private ObservableCollection<ProjectFileViewModel> _projectFiles = new();
        private List<ProjectFileViewModel> _allProjectFiles = new();
        private ObservableCollection<ProjectFileViewModel> _customLaunchers = new();
        private List<ProjectFileViewModel> _allCustomLaunchers = new();
        private string _searchQuery = "";
        private int _previousTabIndex = -1;
        private ObservableCollection<FolderViewModel> _locationFolders = new();
        private List<FolderViewModel> _allLocationFolders = new();
        private Guid _currentFilesLoadId = Guid.Empty;

        public ProjectCardView()
        {
            this.InitializeComponent();
            this.Loaded += ProjectCardView_Loaded;
            this.Unloaded += ProjectCardView_Unloaded;
            CardExpander.Expanding += (s, args) => LoadProjectDetails();
        }

        private void ProjectCardView_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow != null)
            {
                App.MainWindow.SizeChanged += MainWindow_SizeChanged;
                UpdateTabTextVisibility(App.MainWindow.Bounds.Width);
            }
        }

        private void ProjectCardView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow != null)
            {
                App.MainWindow.SizeChanged -= MainWindow_SizeChanged;
            }
            // Clear thumbnail when control is unloaded
            if (ThumbnailImage != null)
            {
                ThumbnailImage.Source = null;
            }
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            UpdateTabTextVisibility(e.Size.Width);
        }

        private static readonly Dictionary<string, string> _tabContentLabels = new()
        {
            { "Projects", "Projects" },
            { "Directories", "Directories" },
            { "Launchers", "Launchers" },
            { "Notes", "Notes" },
            { "Tasks", "Tasks" }
        };

        private void UpdateTabTextVisibility(double windowWidth)
        {
            if (ContentSelectorBar == null) return;
            var showText = windowWidth > 1000;
            foreach (var item in ContentSelectorBar.Items.OfType<CommunityToolkit.WinUI.Controls.SegmentedItem>())
            {
                var tag = item.Tag as string;
                if (tag != null && _tabContentLabels.TryGetValue(tag, out var label))
                {
                    item.Content = showText ? label : null;
                }
            }
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

        public Visibility GetExistsVisibility(bool exists) =>
            exists ? Visibility.Visible : Visibility.Collapsed;

        private async void LocateProject_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null) return;

            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            BlendHub.Helpers.WindowHelper.InitializeWithWindow(picker);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                string oldPath = Project.Path;
                string newPath = folder.Path;

                var loadedProjects = ProjectService.LoadProjects();
                var projectToUpdate = loadedProjects.FirstOrDefault(p => p.Path == oldPath || p.Name == Project.Name);
                if (projectToUpdate != null)
                {
                    projectToUpdate.Path = newPath;
                    ProjectService.SaveProjects(loadedProjects);

                    Project.Path = newPath;

                    RequestRefresh();
                }
            }
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

        // ----------------------------------------------------
        // Detail page logic ported to CardView
        // ----------------------------------------------------
        private void LoadProjectDetails()
        {
            if (Project == null) return;

            _allProjectFiles.Clear();
            _projectFiles.Clear();

            _allCustomLaunchers.Clear();
            _customLaunchers.Clear();

            _locationFolders.Clear();
            _allLocationFolders.Clear();

            if (ContentSelectorBar != null)
            {
                _previousTabIndex = -1;
                if (ContentSelectorBar.SelectedItem == (object)FilesTab)
                {
                    LoadTabContent(0);
                }
                else
                {
                    ContentSelectorBar.SelectedItem = FilesTab;
                }
            }
        }

        private void ContentSelectorBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is Segmented segmented)
            {
                int selectedIndex = segmented.Items.IndexOf(segmented.SelectedItem);
                LoadTabContent(selectedIndex);
                _previousTabIndex = selectedIndex;
            }
        }

        private void LoadTabContent(int tabIndex, bool animate = true)
        {
            if (Project == null || ContentSelectorBar == null) return;

            AddNoteBtn.Visibility = (tabIndex == 3 || tabIndex == 4) ? Visibility.Visible : Visibility.Collapsed;

            if (tabIndex == 3)
                AddNoteBtn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE70B", FontSize = 12 }, new TextBlock { Text = "Add Note", FontSize = 13 } } };
            else if (tabIndex == 4)
                AddNoteBtn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uF7EC", FontSize = 12 }, new TextBlock { Text = "Add Task", FontSize = 13 } } };

            AddFileBtn.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            RefreshDirectoriesBtn.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

            // Toggle visibility of panels based on selected tab index
            if (FilesPanel != null) FilesPanel.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (LocationsPanel != null) LocationsPanel.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (LaunchersPanel != null) LaunchersPanel.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (NotesPanel != null) NotesPanel.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            if (TasksPanel != null) TasksPanel.Visibility = tabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

            switch (tabIndex)
            {
                case 0: LoadProjectFiles(); break;
                case 1: LoadLocations(); break;
                case 2: LoadProjectFiles(); break;
                case 3: LoadProjectItems(); break;
                case 4: LoadProjectItems(); break;
            }

            UpdateSearchBoxVisibility();
            UpdateEmptyStateViews();
        }

        private void LoadLocations()
        {
            if (Project == null) return;

            if (_allLocationFolders.Count == 0 && Project.Subfolders.Count > 0)
            {
                _allLocationFolders.Clear();
                _locationFolders.Clear();

                foreach (var f in Project.Subfolders)
                {
                    string fullPath = Path.Combine(Project.Path, f);
                    var folderVm = new FolderViewModel(f, fullPath, GetFolderItemCount(fullPath))
                    {
                        IsExpanded = AppSettingsService.Instance.Settings.ExpandFoldersByDefault
                    };

                    _allLocationFolders.Add(folderVm);
                    _locationFolders.Add(folderVm);
                }
            }

            FilterLocations();
        }

        public Visibility GetMissingVisibility(bool exists) => exists ? Visibility.Collapsed : Visibility.Visible;
        public double GetOpacity(bool exists) => exists ? 1.0 : 0.55;

        private void UpdateEmptyStateViews()
        {
            // Files Empty States
            if (FilesListView != null && FilesEmptyText != null && FilesNoResultsText != null)
            {
                bool filesAllEmpty = _allProjectFiles.Count == 0;
                bool filesFilteredEmpty = _projectFiles.Count == 0;
                FilesListView.Visibility = filesFilteredEmpty ? Visibility.Collapsed : Visibility.Visible;
                FilesEmptyText.Visibility = filesAllEmpty ? Visibility.Visible : Visibility.Collapsed;
                FilesNoResultsText.Visibility = (!filesAllEmpty && filesFilteredEmpty) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Launchers Empty States
            if (LaunchersListView != null && LaunchersEmptyText != null && LaunchersNoResultsText != null)
            {
                bool launchersAllEmpty = _allCustomLaunchers.Count == 0;
                bool launchersFilteredEmpty = _customLaunchers.Count == 0;
                LaunchersListView.Visibility = launchersFilteredEmpty ? Visibility.Collapsed : Visibility.Visible;
                LaunchersEmptyText.Visibility = launchersAllEmpty ? Visibility.Visible : Visibility.Collapsed;
                LaunchersNoResultsText.Visibility = (!launchersAllEmpty && launchersFilteredEmpty) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Notes Empty States
            if (NotesListView != null && NotesEmptyText != null && NotesNoResultsText != null)
            {
                bool notesAllEmpty = _allItems.Count == 0;
                bool notesFilteredEmpty = _items.Count == 0;
                NotesListView.Visibility = notesFilteredEmpty ? Visibility.Collapsed : Visibility.Visible;
                NotesEmptyText.Visibility = notesAllEmpty ? Visibility.Visible : Visibility.Collapsed;
                NotesNoResultsText.Visibility = (!notesAllEmpty && notesFilteredEmpty) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Tasks Empty States
            if (TasksListView != null && TasksEmptyText != null && TasksNoResultsText != null)
            {
                bool tasksAllEmpty = _allTaskItems.Count == 0;
                bool tasksFilteredEmpty = _taskItems.Count == 0;
                TasksListView.Visibility = tasksFilteredEmpty ? Visibility.Collapsed : Visibility.Visible;
                TasksEmptyText.Visibility = tasksAllEmpty ? Visibility.Visible : Visibility.Collapsed;
                TasksNoResultsText.Visibility = (!tasksAllEmpty && tasksFilteredEmpty) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Locations Empty States
            bool locationsEmpty = _locationFolders.Count == 0;
            if (LocationsRepeater != null) LocationsRepeater.Visibility = locationsEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (LocationsEmptyText != null) LocationsEmptyText.Visibility = locationsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is ProjectItemViewModel vm && Project != null)
            {
                vm.IsCompleted = cb.IsChecked == true;
                vm.Status = vm.IsCompleted ? TodoStatus.Completed : TodoStatus.InProgress;
                ProjectService.UpdateProject(Project);
            }
        }

        private async void LoadProjectFiles()
        {
            if (Project == null) return;
            var loadId = Guid.NewGuid();
            _currentFilesLoadId = loadId;

            _allProjectFiles.Clear();
            _projectFiles.Clear();

            _allCustomLaunchers.Clear();
            _customLaunchers.Clear();

            var projectPath = Project.Path;
            var projectFullBlendPath = Project.FullBlendPath;
            var projectBlenderVersion = Project.BlenderVersion;
            var projectCustomFiles = Project.CustomFiles.ToList();
            var projectFileLaunchers = Project.FileLaunchers.ToDictionary(k => k.Key, v => v.Value);
            var projectFolderExists = Project.FolderExists;
            var projectObj = Project;

            var filterNested = AppSettingsService.Instance.Settings.FilterNestedBlendFiles;
            var defaultLaunchers = AppSettingsService.Instance.Settings.DefaultLaunchers.ToDictionary(k => k.Key, v => v.Value);

            var (allProjectFilesList, allCustomLaunchersList) = await Task.Run(() =>
            {
                if (loadId != _currentFilesLoadId) return (new List<ProjectFileViewModel>(), new List<ProjectFileViewModel>());

                var versions = _blenderService.GetInstalledVersions();
                var versionToUse = versions.FirstOrDefault(v => v.Version == projectBlenderVersion);
                string blenderExePath = versionToUse?.ExecutablePath ?? "";

                string blenderName = "Blender";
                if (versionToUse != null)
                {
                    blenderName = versionToUse.DisplayName;
                }
                else if (!string.IsNullOrEmpty(projectBlenderVersion) && projectBlenderVersion != "default")
                {
                    blenderName = $"Blender {projectBlenderVersion}";
                }
                else
                {
                    var defaultVersion = versions.FirstOrDefault();
                    if (defaultVersion != null)
                    {
                        blenderName = defaultVersion.DisplayName;
                    }
                    else
                    {
                        blenderName = "Blender";
                    }
                }

                var pFilesList = new List<ProjectFileViewModel>();
                var cLaunchersList = new List<ProjectFileViewModel>();

                if (projectFolderExists)
                {
                    try
                    {
                        var allBlendFiles = new DirectoryInfo(projectPath).GetFiles("*.blend", SearchOption.AllDirectories);
                        foreach (var file in allBlendFiles)
                        {
                            if (loadId != _currentFilesLoadId) break;

                            string relFolder = Path.GetDirectoryName(Path.GetRelativePath(projectPath, file.FullName)) ?? "";

                            if (filterNested)
                            {
                                string[] parts = relFolder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 1)
                                {
                                    continue;
                                }
                            }

                            bool isPrimary = file.FullName.Equals(projectFullBlendPath, StringComparison.OrdinalIgnoreCase);

                            var vm = new ProjectFileViewModel(
                                file.Name + (isPrimary ? " (Primary)" : ""),
                                string.IsNullOrEmpty(relFolder) || relFolder == "." ? "Project Root" : relFolder,
                                file.FullName,
                                blenderExePath,
                                blenderName,
                                false
                            )
                            {
                                Size = FormatBytes(file.Length),
                                Modified = file.LastWriteTime.ToString("g"),
                                Project = projectObj
                            };

                            pFilesList.Add(vm);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProjectCard] Error scanning .blend files: {ex}");
                    }

                    if (loadId != _currentFilesLoadId) return (new List<ProjectFileViewModel>(), new List<ProjectFileViewModel>());

                    var mergedLaunchers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in defaultLaunchers)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            mergedLaunchers[kvp.Key] = kvp.Value;
                        }
                    }

                    foreach (var kvp in projectFileLaunchers)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            mergedLaunchers[kvp.Key] = kvp.Value;
                        }
                    }

                    if (mergedLaunchers.Count > 0)
                    {
                        var launcherExts = new HashSet<string>(mergedLaunchers.Keys, StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            var allFiles = new DirectoryInfo(projectPath).GetFiles("*.*", SearchOption.AllDirectories);
                            foreach (var file in allFiles)
                            {
                                if (loadId != _currentFilesLoadId) break;

                                string ext = file.Extension.ToLowerInvariant();
                                if (launcherExts.Contains(ext) && !ext.Equals(".blend", StringComparison.OrdinalIgnoreCase))
                                {
                                    string programPath = mergedLaunchers[ext];
                                    string programName = Path.GetFileNameWithoutExtension(programPath);
                                    string relFolder = Path.GetDirectoryName(Path.GetRelativePath(projectPath, file.FullName)) ?? "";

                                    var vm = new ProjectFileViewModel(
                                        file.Name,
                                        string.IsNullOrEmpty(relFolder) || relFolder == "." ? "Project Root" : relFolder,
                                        file.FullName,
                                        programPath,
                                        programName,
                                        false
                                    )
                                    {
                                        Size = FormatBytes(file.Length),
                                        Modified = file.LastWriteTime.ToString("g"),
                                        Project = projectObj
                                    };

                                    cLaunchersList.Add(vm);
                                }
                            }
                        }
                        catch { }
                    }
                }

                foreach (var storedPath in projectCustomFiles)
                {
                    if (loadId != _currentFilesLoadId) break;

                    string fullPath = Path.IsPathRooted(storedPath)
                        ? storedPath
                        : Path.Combine(projectPath, storedPath);

                    if (File.Exists(fullPath) && !fullPath.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileInfo = new FileInfo(fullPath);
                        string displayFolder = Path.IsPathRooted(storedPath)
                            ? Path.GetDirectoryName(storedPath) ?? ""
                            : (Path.GetDirectoryName(storedPath) ?? "");
                        var vm = new ProjectFileViewModel(
                            Path.GetFileName(fullPath),
                            string.IsNullOrEmpty(displayFolder) || displayFolder == "." ? "Project Root" : displayFolder,
                            fullPath,
                            "",
                            "External App",
                            true
                        )
                        {
                            Size = FormatBytes(fileInfo.Length),
                            Modified = fileInfo.LastWriteTime.ToString("g"),
                            Project = projectObj
                        };
                        cLaunchersList.Add(vm);
                    }
                }

                return (pFilesList, cLaunchersList);
            });

            if (loadId != _currentFilesLoadId) return;

            this.DispatcherQueue.TryEnqueue(async () =>
            {
                if (loadId != _currentFilesLoadId) return;

                foreach (var item in allProjectFilesList)
                {
                    _allProjectFiles.Add(item);
                }
                foreach (var item in allCustomLaunchersList)
                {
                    _allCustomLaunchers.Add(item);
                    _ = LoadFileIconAsync(item.FullPath, item, loadId);
                }

                FilterProjectFiles();
                FilterCustomLaunchers();
            });
        }

        private void FilterProjectFiles()
        {
            var query = _searchQuery.ToLowerInvariant();

            var filtered = _allProjectFiles.Where(f =>
                string.IsNullOrWhiteSpace(query) ||
                f.Name.ToLowerInvariant().Contains(query)
            ).ToList();

            for (int i = _projectFiles.Count - 1; i >= 0; i--)
            {
                if (!filtered.Contains(_projectFiles[i]))
                {
                    _projectFiles.RemoveAt(i);
                }
            }
            foreach (var item in filtered)
            {
                if (!_projectFiles.Contains(item))
                {
                    _projectFiles.Add(item);
                }
            }

            UpdateSearchBoxVisibility();
            UpdateEmptyStateViews();
        }

        private void FilterCustomLaunchers()
        {
            var query = _searchQuery.ToLowerInvariant();

            var filtered = _allCustomLaunchers.Where(f =>
                string.IsNullOrWhiteSpace(query) ||
                f.Name.ToLowerInvariant().Contains(query)
            ).ToList();

            for (int i = _customLaunchers.Count - 1; i >= 0; i--)
            {
                if (!filtered.Contains(_customLaunchers[i]))
                {
                    _customLaunchers.RemoveAt(i);
                }
            }
            foreach (var item in filtered)
            {
                if (!_customLaunchers.Contains(item))
                {
                    _customLaunchers.Add(item);
                }
            }

            UpdateSearchBoxVisibility();
            UpdateEmptyStateViews();
        }

        private void ProjectFileItem_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ProjectFileViewModel vm)
            {
                try
                {
                    if (string.IsNullOrEmpty(vm.ProgramPath))
                    {
                        Process.Start(new ProcessStartInfo { FileName = vm.FullPath, UseShellExecute = true });
                    }
                    else
                    {
                        Process.Start(vm.ProgramPath, $"\"{vm.FullPath}\"");
                    }
                }
                catch { }
            }
        }

        private void FilesListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (e.Items.Count > 0 && e.Items[0] is ProjectFileViewModel vm)
            {
                e.Data.Properties["draggedFile"] = vm;
                e.Data.Properties["sourceListView"] = sender;
            }
        }

        private void FilesListView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        private void FilesListView_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Properties.TryGetValue("draggedFile", out var data) && data is ProjectFileViewModel draggedFile)
            {
                if (sender is ListView targetListView)
                {
                    var pos = e.GetPosition(targetListView);
                    int index = targetListView.Items.Count;

                    for (int i = 0; i < targetListView.Items.Count; i++)
                    {
                        var container = targetListView.ContainerFromIndex(i) as ListViewItem;
                        if (container != null)
                        {
                            var transform = container.TransformToVisual(targetListView);
                            var itemBounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));

                            if (pos.Y < itemBounds.Y + itemBounds.Height / 2)
                            {
                                index = i;
                                break;
                            }
                            else if (pos.Y < itemBounds.Y + itemBounds.Height)
                            {
                                index = i + 1;
                                break;
                            }
                        }
                    }

                    var list = targetListView.ItemsSource as ObservableCollection<ProjectFileViewModel>;
                    if (list != null)
                    {
                        int oldIndex = list.IndexOf(draggedFile);
                        if (oldIndex != -1)
                        {
                            if (oldIndex < index)
                            {
                                index--;
                            }

                            if (index < 0) index = 0;
                            if (index >= list.Count) index = list.Count - 1;

                            if (oldIndex != index)
                            {
                                list.Move(oldIndex, index);

                                int allOldIndex = _allProjectFiles.IndexOf(draggedFile);
                                if (allOldIndex != -1)
                                {
                                    _allProjectFiles.RemoveAt(allOldIndex);
                                    int allNewIndex = _allProjectFiles.Count;

                                    if (index < list.Count)
                                    {
                                        var targetItem = list[index];
                                        allNewIndex = _allProjectFiles.IndexOf(targetItem);
                                        if (allNewIndex == -1) allNewIndex = _allProjectFiles.Count;
                                    }

                                    if (allNewIndex >= _allProjectFiles.Count)
                                        _allProjectFiles.Add(draggedFile);
                                    else
                                        _allProjectFiles.Insert(allNewIndex, draggedFile);
                                }

                                if (Project != null)
                                {
                                    string relPath = Path.GetRelativePath(Project.Path, draggedFile.FullPath);
                                    int customOldIndex = Project.CustomFiles.FindIndex(f => f.Equals(relPath, StringComparison.OrdinalIgnoreCase));
                                    if (customOldIndex != -1)
                                    {
                                        Project.CustomFiles.RemoveAt(customOldIndex);
                                        int customInsertIndex = Project.CustomFiles.Count;
                                        for (int i = index + 1; i < list.Count; i++)
                                        {
                                            if (list[i].IsCustom)
                                            {
                                                string targetRelPath = Path.GetRelativePath(Project.Path, list[i].FullPath);
                                                int targetCustomIndex = Project.CustomFiles.FindIndex(f => f.Equals(targetRelPath, StringComparison.OrdinalIgnoreCase));
                                                if (targetCustomIndex != -1)
                                                {
                                                    customInsertIndex = targetCustomIndex;
                                                    break;
                                                }
                                            }
                                        }
                                        if (customInsertIndex >= Project.CustomFiles.Count)
                                            Project.CustomFiles.Add(relPath);
                                        else
                                            Project.CustomFiles.Insert(customInsertIndex, relPath);

                                        ProjectService.UpdateProject(Project);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }



        private void FolderHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FolderViewModel vm)
            {
                vm.IsExpanded = !vm.IsExpanded;
            }
        }

        private void FileItem_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileViewModel vm)
            {
                if (File.Exists(vm.FullPath)) Process.Start(new ProcessStartInfo { FileName = vm.FullPath, UseShellExecute = true });
                else if (Directory.Exists(vm.FullPath)) Process.Start("explorer.exe", vm.FullPath);
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string fullPath)
            {
                if (File.Exists(fullPath)) Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            }
        }

        private void OpenContainingFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string fullPath)
            {
                if (File.Exists(fullPath)) Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                else if (Directory.Exists(fullPath)) Process.Start("explorer.exe", fullPath);
            }
        }

        private async void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string fullPath)
            {
                var dialog = new BlendHub.Dialogs.DeleteFileDialog(Path.GetFileName(fullPath)) { XamlRoot = this.XamlRoot };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    try
                    {
                        if (File.Exists(fullPath)) File.Delete(fullPath);
                        else if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);

                        _allLocationFolders.Clear();
                        _locationFolders.Clear();
                        LoadTabContent(0);
                    }
                    catch (Exception ex)
                    {
                        var errorDialog = new BlendHub.Dialogs.ErrorDialog($"Could not delete item: {ex.Message}") { XamlRoot = this.XamlRoot };
                        await errorDialog.ShowAsync();
                    }
                }
            }
        }

        private void RefreshLocations_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null || !Directory.Exists(Project.Path)) return;

            var diskFolders = Directory.GetDirectories(Project.Path)
                .Select(d => Path.GetFileName(d))
                .OrderBy(n => n)
                .ToList();

            Project.Subfolders = diskFolders;
            ProjectService.UpdateProject(Project);

            _allLocationFolders.Clear();
            _locationFolders.Clear();

            int selectedIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
            LoadTabContent(selectedIndex);
        }

        private int GetFolderItemCount(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    return new DirectoryInfo(path).GetFileSystemInfos().Length;
                }
            }
            catch { }
            return 0;
        }



        private void FilterLocations()
        {
            var query = _searchQuery.ToLowerInvariant();

            foreach (var folder in _allLocationFolders)
            {
                folder.Filter(_searchQuery);
            }

            List<FolderViewModel> filteredFolders;
            if (string.IsNullOrWhiteSpace(query))
            {
                filteredFolders = _allLocationFolders;
            }
            else
            {
                filteredFolders = _allLocationFolders.Where(folder =>
                    folder.Name.ToLowerInvariant().Contains(query) ||
                    folder.Subfolders.Count > 0 ||
                    folder.FilesOnly.Count > 0
                ).ToList();
            }

            UpdateEmptyStateViews();
        }

        private async Task LoadFileIconAsync(string filePath, ProjectFileViewModel vm, Guid loadId)
        {
            try
            {
                if (loadId != _currentFilesLoadId) return;
                if (!File.Exists(filePath)) return;

                if (filePath.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
                {
                    vm.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/blender_logo.png"));
                    return;
                }

                var file = await StorageFile.GetFileFromPathAsync(filePath);
                if (loadId != _currentFilesLoadId) return;
                var thumb = await file.GetThumbnailAsync(ThumbnailMode.ListView, 256);
                if (loadId != _currentFilesLoadId) return;
                if (thumb != null)
                {
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(thumb.AsStreamForRead().AsRandomAccessStream());
                    if (loadId != _currentFilesLoadId) return;
                    vm.IconSource = bmp;
                }
            }
            catch { }
        }

        private void RemoveCustomFileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Project != null && sender is Button btn && btn.Tag is ProjectFileViewModel vm)
            {
                Project.CustomFiles.Remove(Path.GetRelativePath(Project.Path, vm.FullPath));
                ProjectService.UpdateProject(Project);
                LoadProjectFiles();
            }
        }

        private async void AddCustomFile_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null) return;
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder }; picker.FileTypeFilter.Add("*");
            BlendHub.Helpers.WindowHelper.InitializeWithWindow(picker);
            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                foreach (var file in files)
                {
                    // Store absolute path for external files — never copy to project folder
                    string pathToStore = file.Path.StartsWith(Project.Path, StringComparison.OrdinalIgnoreCase)
                        ? Path.GetRelativePath(Project.Path, file.Path)
                        : file.Path;

                    if (!Project.CustomFiles.Contains(pathToStore, StringComparer.OrdinalIgnoreCase))
                        Project.CustomFiles.Add(pathToStore);
                }
                ProjectService.UpdateProject(Project);
                LoadProjectFiles();
            }
        }


        private void LoadProjectItems()
        {
            if (Project == null) return;
            _allItems.Clear();
            _allTaskItems.Clear();

            foreach (var item in Project.Items.OrderByDescending(i => i.CreatedAt))
            {
                var vm = new ProjectItemViewModel(item);
                if (item.Type == ProjectItemType.Note)
                    _allItems.Add(vm);
                else
                    _allTaskItems.Add(vm);
            }

            FilterProjectItems();
        }

        private bool FilterProjectItem(ProjectItemViewModel item, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return (item.Heading?.ToLowerInvariant().Contains(query) == true) ||
                   (item.Content?.ToLowerInvariant().Contains(query) == true);
        }

        private void RemoveNonMatchingItems(IEnumerable<ProjectItemViewModel> filteredData, ObservableCollection<ProjectItemViewModel> targetCollection)
        {
            var filteredList = filteredData.ToList();
            for (int i = targetCollection.Count - 1; i >= 0; i--)
            {
                var item = targetCollection[i];
                if (!filteredList.Contains(item))
                {
                    targetCollection.Remove(item);
                }
            }
        }

        private void AddBackItems(IEnumerable<ProjectItemViewModel> filteredData, ObservableCollection<ProjectItemViewModel> targetCollection)
        {
            foreach (var item in filteredData)
            {
                if (!targetCollection.Contains(item))
                {
                    targetCollection.Add(item);
                }
            }
        }

        private void FilterProjectItems()
        {
            var query = _searchQuery.ToLowerInvariant();

            var filteredNotes = _allItems.Where(i => FilterProjectItem(i, query)).ToList();
            RemoveNonMatchingItems(filteredNotes, _items);
            AddBackItems(filteredNotes, _items);

            var filteredTasks = _allTaskItems.Where(i => FilterProjectItem(i, query)).ToList();
            RemoveNonMatchingItems(filteredTasks, _taskItems);
            AddBackItems(filteredTasks, _taskItems);

            UpdateSearchBoxVisibility();
            UpdateEmptyStateViews();
        }

        private void UpdateSearchBoxVisibility()
        {
            if (GlobalSearchBox == null || ContentSelectorBar == null || ContentSelectorBar.SelectedItem == null) return;

            int tabIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
            bool hasContent = false;

            switch (tabIndex)
            {
                case 0: hasContent = _allProjectFiles.Count > 0; break;
                case 1: hasContent = _allLocationFolders.Count > 0; break;
                case 2: hasContent = _allCustomLaunchers.Count > 0; break;
                case 3: hasContent = _allItems.Count > 0; break;
                case 4: hasContent = _allTaskItems.Count > 0; break;
            }

            GlobalSearchBox.IsEnabled = hasContent;
        }

        private void GlobalSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                _searchQuery = sender.Text;

                int tabIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
                if (tabIndex == 3 || tabIndex == 4)
                {
                    FilterProjectItems();
                }
                else if (tabIndex == 0)
                {
                    FilterProjectFiles();
                }
                else if (tabIndex == 2)
                {
                    FilterCustomLaunchers();
                }
                else if (tabIndex == 1)
                {
                    FilterLocations();
                }
            }
        }

        private async void AddProjectItem_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null) return;
            var itemType = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem) == 4 ? ProjectItemType.Todo : ProjectItemType.Note;

            var dialog = new BlendHub.Dialogs.ProjectItemDialog(itemType) { XamlRoot = this.XamlRoot };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && (!string.IsNullOrWhiteSpace(dialog.ContentText) || !string.IsNullOrWhiteSpace(dialog.HeadingText)))
            {
                var newItem = new ProjectItem { Heading = dialog.HeadingText, Content = dialog.ContentText, Type = itemType, CreatedAt = DateTime.Now };
                if (itemType == ProjectItemType.Todo)
                {
                    newItem.Priority = dialog.SelectedPriority;
                    newItem.Status = dialog.SelectedStatus;
                    newItem.DueDate = dialog.SelectedDueDate;
                }
                Project.Items.Add(newItem);
                ProjectService.UpdateProject(Project); LoadProjectItems();

                int selectedIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
                if (selectedIndex == 3 || selectedIndex == 4) LoadTabContent(selectedIndex);
            }
        }

        private async void EditItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem btn && btn.Tag is ProjectItemViewModel vm)
            {
                var dialog = new BlendHub.Dialogs.ProjectItemDialog(vm.Item.Type, vm.Heading, vm.Content, vm.Item.Priority, vm.Status, vm.Item.DueDate, true) { XamlRoot = this.XamlRoot };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    vm.Heading = dialog.HeadingText;
                    vm.Content = dialog.ContentText;
                    if (vm.Item.Type == ProjectItemType.Todo)
                    {
                        vm.Item.Priority = dialog.SelectedPriority;
                        vm.Status = dialog.SelectedStatus;
                        vm.Item.DueDate = dialog.SelectedDueDate;
                        vm.UpdatePriority();
                    }
                    if (Project != null) ProjectService.UpdateProject(Project);

                    LoadProjectItems();
                    int selectedIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
                    if (selectedIndex == 3 || selectedIndex == 4) LoadTabContent(selectedIndex);
                }
            }
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem btn && btn.Tag is ProjectItemViewModel vm && Project != null)
            {
                Project.Items.Remove(vm.Item);
                _items.Remove(vm);
                _taskItems.Remove(vm);
                ProjectService.UpdateProject(Project);
                LoadProjectItems();

            }
        }

        private async void EditProjectMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Project != null)
            {
                await ProjectDialogService.ShowEditDialogAsync(Project, this.XamlRoot, () => RequestRefresh());
            }
        }

        private async void UpdateProgressMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Project != null)
            {
                await ProjectDialogService.ShowUpdateProgressDialogAsync(Project, this.XamlRoot, () =>
                {
                    this.Bindings.Update();
                    RequestRefresh();
                });
            }
        }

        public string GetProgressText(int progress)
        {
            return $"{progress}%";
        }

        public Visibility GetProgressVisibility(bool showProgress)
        {
            return showProgress ? Visibility.Visible : Visibility.Collapsed;
        }

        public Visibility GetPinnedVisibility(bool isPinned)
        {
            return isPinned ? Visibility.Visible : Visibility.Collapsed;
        }

        public string GetPinMenuText(bool isPinned)
        {
            return isPinned ? "Unpin project" : "Pin project";
        }

        public string GetPinMenuIcon(bool isPinned)
        {
            return isPinned ? "\xE77A" : "\xE840";
        }

        private void PinProjectMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Project != null)
            {
                Project.IsPinned = !Project.IsPinned;
                ProjectService.UpdateProject(Project);
                this.Bindings.Update();
                RequestRefresh();
            }
        }

        private async void DeleteProjectMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Project != null)
            {
                await ProjectDialogService.ShowDeleteConfirmAsync(Project, this.XamlRoot, () => RequestRefresh());
            }
        }

        private void OpenProjectFolderMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Project != null && Directory.Exists(Project.Path))
            {
                Process.Start("explorer.exe", Project.Path);
            }
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectFileViewModel vm)
            {
                if (File.Exists(vm.FullPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{vm.FullPath}\"");
                }
            }
        }

        private async void RenameFileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectFileViewModel vm)
            {
                await RenameFileAsync(vm);
            }
        }

        private async Task RenameFileAsync(ProjectFileViewModel vm)
        {
            if (Project == null) return;

            var textBox = new TextBox { Text = Path.GetFileNameWithoutExtension(vm.Name), Header = "New Name", HorizontalAlignment = HorizontalAlignment.Stretch };
            var dialog = new ContentDialog
            {
                Title = "Rename File",
                Content = textBox,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                string newName = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    try
                    {
                        string ext = Path.GetExtension(vm.FullPath);
                        string dir = Path.GetDirectoryName(vm.FullPath) ?? "";
                        string newFullPath = Path.Combine(dir, newName + ext);

                        if (File.Exists(vm.FullPath) && !File.Exists(newFullPath))
                        {
                            File.Move(vm.FullPath, newFullPath);

                            // Update Project models if this was the primary blend file or in custom files
                            if (vm.FullPath.Equals(Project.FullBlendPath, StringComparison.OrdinalIgnoreCase))
                            {
                                Project.BlendFileName = newName + ext;
                                ProjectService.UpdateProject(Project);
                            }
                            else
                            {
                                string oldRel = Path.GetRelativePath(Project.Path, vm.FullPath);
                                string newRel = Path.GetRelativePath(Project.Path, newFullPath);
                                int idx = Project.CustomFiles.FindIndex(f => f.Equals(oldRel, StringComparison.OrdinalIgnoreCase));
                                if (idx != -1)
                                {
                                    Project.CustomFiles[idx] = newRel;
                                    ProjectService.UpdateProject(Project);
                                }
                            }

                            LoadProjectFiles();
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorDialog = new BlendHub.Dialogs.ErrorDialog($"Could not rename file: {ex.Message}") { XamlRoot = this.XamlRoot };
                        await errorDialog.ShowAsync();
                    }
                }
            }
        }

        private async void DeleteFileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null) return;

            if (sender is MenuFlyoutItem item && item.Tag is ProjectFileViewModel vm)
            {
                var dialog = new BlendHub.Dialogs.DeleteFileDialog(vm.Name) { XamlRoot = this.XamlRoot };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    try
                    {
                        if (File.Exists(vm.FullPath))
                        {
                            File.Delete(vm.FullPath);

                            // Update Project if it was primary blend file
                            if (vm.FullPath.Equals(Project.FullBlendPath, StringComparison.OrdinalIgnoreCase))
                            {
                                Project.BlendFileName = "";
                                ProjectService.UpdateProject(Project);
                            }
                            else
                            {
                                string rel = Path.GetRelativePath(Project.Path, vm.FullPath);
                                Project.CustomFiles.Remove(rel);
                                ProjectService.UpdateProject(Project);
                            }

                            LoadProjectFiles();
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorDialog = new BlendHub.Dialogs.ErrorDialog($"Could not delete file: {ex.Message}") { XamlRoot = this.XamlRoot };
                        await errorDialog.ShowAsync();
                    }
                }
            }
        }

        private string FormatBytes(long bytes)
        {
            return BlendHub.Helpers.FormatHelper.FormatBytes(bytes);
        }

        private void FileMenuFlyout_Opening(object sender, object e)
        {
            var flyout = sender as MenuFlyout;
            if (flyout == null) return;

            flyout.Items.Clear();

            ProjectFileViewModel? vm = null;
            bool isLauncherTab = false;

            if (flyout.Target is FrameworkElement element)
            {
                vm = element.DataContext as ProjectFileViewModel;
                // Walk up to see if we are inside the LauncherFileTemplate grid (Tag="Launcher")
                FrameworkElement? walk = element;
                while (walk != null)
                {
                    if (walk.Tag is string t && t == "Launcher") { isLauncherTab = true; break; }
                    walk = walk.Parent as FrameworkElement;
                }
            }
            // Also check the button flyout path — parent chain from flyout placement target
            if (!isLauncherTab && flyout.Target == null)
            {
                // button flyout: check via _launchersListView
                isLauncherTab = false; // button path handled via Tag above
            }

            if (vm == null) return;

            // 1. Open (Default)
            var openItem = new MenuFlyoutItem
            {
                Text = "Open",
                Icon = new FontIcon { Glyph = "\uE8E5" },
                Tag = vm
            };
            openItem.Click += (s, args) =>
            {
                if (s is MenuFlyoutItem mi && mi.Tag is ProjectFileViewModel fvm)
                {
                    LaunchFileDefault(fvm);
                }
            };
            flyout.Items.Add(openItem);

            // 2. Open with other versions (only for .blend files, not in Launchers tab)
            if (!isLauncherTab && vm.FullPath.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
            {
                var openWithSub = new MenuFlyoutSubItem
                {
                    Text = "Open with...",
                    Icon = new FontIcon { Glyph = "\uE7AC" }
                };

                var versions = new BlenderSettingsService().GetInstalledVersions();
                if (versions.Any())
                {
                    foreach (var ver in versions)
                    {
                        var verItem = new MenuFlyoutItem
                        {
                            Text = ver.DisplayName,
                            Tag = (vm, ver)
                        };
                        if (Project != null && ver.Version == Project.BlenderVersion)
                        {
                            verItem.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                            verItem.Text += " (Default)";
                        }
                        verItem.Click += (s, args) =>
                        {
                            if (s is MenuFlyoutItem vItem && vItem.Tag is ValueTuple<ProjectFileViewModel, BlenderVersionInfo> data)
                            {
                                LaunchFileWithVersion(data.Item1, data.Item2);
                            }
                        };
                        openWithSub.Items.Add(verItem);
                    }
                }
                else
                {
                    openWithSub.Items.Add(new MenuFlyoutItem { Text = "No Blender versions found", IsEnabled = false });
                }
                flyout.Items.Add(openWithSub);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());

            // 3. Open File Location
            var locItem = new MenuFlyoutItem
            {
                Text = "Open File Location",
                Icon = new FontIcon { Glyph = "\uED25" },
                Tag = vm
            };
            locItem.Click += OpenFileLocation_Click;
            flyout.Items.Add(locItem);

            if (isLauncherTab)
            {
                // Launchers tab: show Remove (unlink) instead of Rename/Delete
                flyout.Items.Add(new MenuFlyoutSeparator());
                var removeItem = new MenuFlyoutItem
                {
                    Text = "Remove",
                    Icon = new FontIcon { Glyph = "\uE894" },
                    Tag = vm,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                };
                removeItem.Click += (s, args) =>
                {
                    if (s is MenuFlyoutItem mi && mi.Tag is ProjectFileViewModel fvm && Project != null)
                    {
                        // Remove by absolute path or relative path stored in CustomFiles
                        string absPath = fvm.FullPath;
                        string relPath = Path.GetRelativePath(Project.Path, absPath);
                        if (!Project.CustomFiles.Remove(absPath))
                            Project.CustomFiles.Remove(relPath);
                        ProjectService.UpdateProject(Project);
                        LoadProjectFiles();
                    }
                };
                flyout.Items.Add(removeItem);
            }
            else
            {
                // Files tab: Rename and Delete
                var renameItem = new MenuFlyoutItem
                {
                    Text = "Rename",
                    Icon = new FontIcon { Glyph = "\uE8AC" },
                    Tag = vm
                };
                renameItem.Click += RenameFileMenu_Click;
                flyout.Items.Add(renameItem);

                var deleteItem = new MenuFlyoutItem
                {
                    Text = "Delete File",
                    Icon = new FontIcon { Glyph = "\uE74D" },
                    Tag = vm,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                };
                deleteItem.Click += DeleteFileMenu_Click;
                flyout.Items.Add(deleteItem);
            }
        }

        private void LaunchFileDefault(ProjectFileViewModel vm)
        {
            try
            {
                if (File.Exists(vm.FullPath))
                {
                    if (vm.FullPath.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
                    {
                        var blenderService = new BlenderSettingsService();
                        var versions = blenderService.GetInstalledVersions();
                        var versionToUse = Project != null ? versions.FirstOrDefault(v => v.Version == Project.BlenderVersion) : null;

                        if (versionToUse != null && !string.IsNullOrEmpty(versionToUse.ExecutablePath) && File.Exists(versionToUse.ExecutablePath))
                        {
                            Process.Start(new ProcessStartInfo(versionToUse.ExecutablePath, $"\"{vm.FullPath}\"") { UseShellExecute = true });
                            return;
                        }
                    }
                    Process.Start(new ProcessStartInfo(vm.FullPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectCardView] Error launching file: {ex.Message}");
            }
        }

        private void LaunchFileWithVersion(ProjectFileViewModel vm, BlenderVersionInfo version)
        {
            try
            {
                if (File.Exists(vm.FullPath) && !string.IsNullOrEmpty(version.ExecutablePath) && File.Exists(version.ExecutablePath))
                {
                    Process.Start(new ProcessStartInfo(version.ExecutablePath, $"\"{vm.FullPath}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectCardView] Error launching file with specific version: {ex.Message}");
            }
        }
    }
}
