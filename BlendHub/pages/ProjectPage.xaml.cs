using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using BlendHub.Controls;
using BlendHub.Dialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Pages
{
    public sealed partial class ProjectPage : Page
    {
        private readonly BlenderSettingsService _blenderService = new();
        public ObservableCollection<Project> Projects { get; } = new ObservableCollection<Project>();
        private List<Project> _allProjects = new List<Project>();
        private Project? _lastCreatedProject;

        public ProjectPage()
        {
            this.InitializeComponent();

        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadProjects();
            if (e.Parameter is string projectName && !string.IsNullOrEmpty(projectName))
            {
                SearchTextBox.Text = projectName;
                ApplyFilterAndSort();
            }
        }

        public void LoadProjects()
        {
            try
            {
                var loadedProjects = ProjectService.LoadProjects();
                Debug.WriteLine($"[ProjectPage] Loaded {loadedProjects.Count} projects from JSON");

                _allProjects = loadedProjects;
                ApplyFilterAndSort();

                Debug.WriteLine($"[ProjectPage] UI updated with {Projects.Count} filtered projects");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectPage] Error loading projects: {ex.Message}");
            }
        }

        private string _currentSortField = "Date";
        private string _currentSortOrder = "Desc";

        private void ApplyFilterAndSort()
        {
            if (SearchTextBox == null) return;

            string filter = SearchTextBox.Text.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(filter)
                ? _allProjects
                : _allProjects.Where(p => p.Name.ToLowerInvariant().Contains(filter) || p.Path.ToLowerInvariant().Contains(filter));

            bool searchBoxHadFocus = false;
            if (this.XamlRoot != null)
            {
                var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
                if (focusedElement == (object)SearchTextBox)
                {
                    searchBoxHadFocus = true;
                }
            }

            IEnumerable<Project> sorted;
            if (_currentSortField == "Date")
            {
                sorted = _currentSortOrder == "Asc"
                    ? filtered.OrderBy(x => x.CreatedAt)
                    : filtered.OrderByDescending(x => x.CreatedAt);
            }
            else if (_currentSortField == "Modified")
            {
                Func<Project, DateTime> getModifiedTime = x =>
                {
                    try
                    {
                        if (System.IO.File.Exists(x.FullBlendPath)) return System.IO.File.GetLastWriteTime(x.FullBlendPath);
                        if (System.IO.Directory.Exists(x.Path)) return System.IO.Directory.GetLastWriteTime(x.Path);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProjectPage] Error getting modified time: {ex.Message}");
                    }
                    return x.CreatedAt;
                };
                sorted = _currentSortOrder == "Asc"
                    ? filtered.OrderBy(getModifiedTime)
                    : filtered.OrderByDescending(getModifiedTime);
            }
            else // Name
            {
                Func<Project, string> getName = x => string.IsNullOrEmpty(x.Name) ? "" : x.Name.ToLower();
                sorted = _currentSortOrder == "Asc"
                    ? filtered.OrderBy(getName)
                    : filtered.OrderByDescending(getName);
            }

            string fieldText = _currentSortField switch
            {
                "Modified" => "Date Modified",
                "Name" => "Name",
                "Date" => "Date Created",
                _ => "Date Created"
            };

            string orderText = _currentSortField switch
            {
                "Name" => _currentSortOrder == "Asc" ? "(A-Z)" : "(Z-A)",
                _ => _currentSortOrder == "Asc" ? "(Oldest)" : "(Newest)"
            };

            if (SortButtonText != null)
            {
                SortButtonText.Text = $"{fieldText} {orderText}";
            }

            UpdateFilteredProjects(sorted.ToList());

            bool hasAnyProjects = _allProjects.Count > 0;
            bool hasFilteredProjects = Projects.Count > 0;
            bool isSearching = !string.IsNullOrEmpty(SearchTextBox?.Text.Trim());

            if (ProjectsList != null)
            {
                ProjectsList.ItemsSource = Projects;
                ProjectsList.Visibility = hasFilteredProjects ? Visibility.Visible : Visibility.Collapsed;
            }

            if (searchBoxHadFocus && SearchTextBox != null)
            {
                SearchTextBox.Focus(FocusState.Programmatic);
            }

            if (DropZone != null)
            {
                DropZone.Visibility = (!hasAnyProjects && !isSearching) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (NoSearchResultsPanel != null)
            {
                NoSearchResultsPanel.Visibility = (hasAnyProjects && !hasFilteredProjects) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateFilteredProjects(List<Project> sorted)
        {
            if (Projects == null) return;

            // 1. Remove items that are no longer in results
            for (int i = Projects.Count - 1; i >= 0; i--)
            {
                var item = Projects[i];
                if (!sorted.Any(x => x.Path == item.Path))
                {
                    Projects.RemoveAt(i);
                }
            }

            // 2. Add or move items to match results content and order exactly
            for (int i = 0; i < sorted.Count; i++)
            {
                var targetItem = sorted[i];
                int existingIdx = -1;

                for (int j = i; j < Projects.Count; j++)
                {
                    if (Projects[j].Path == targetItem.Path)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx == -1)
                {
                    Projects.Insert(i, targetItem);
                }
                else if (existingIdx != i)
                {
                    var itemToMove = Projects[existingIdx];
                    Projects.RemoveAt(existingIdx);
                    Projects.Insert(i, itemToMove);
                }
            }
        }

        private void Filter_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
        {
            ApplyFilterAndSort();
        }

        private async void RefreshProjectsButton_Click(object sender, RoutedEventArgs e)
        {
            if (AppSettingsService.Instance.Settings.AutoDetectBlenderVersion)
            {
                await ProjectService.DetectProjectVersionsAsync(_allProjects);
            }
            LoadProjects();
        }

        private void SortFieldItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _currentSortField = item.Tag?.ToString() ?? "Date";
                ApplyFilterAndSort();
            }
        }

        private void SortOrderItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _currentSortOrder = item.Tag?.ToString() ?? "Desc";
                ApplyFilterAndSort();
            }
        }

        private async void ShowDialog_Click(object sender, RoutedEventArgs e)
        {
            var content = new CreateProjectDialogContent();
            var dialog = new ContentDialog
            {
                Title = "Create New Project",
                Content = content,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
            };

            dialog.IsPrimaryButtonEnabled = content.IsValid;
            content.ValidationChanged += (s, args) => { dialog.IsPrimaryButtonEnabled = content.IsValid; };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string projectName = content.ProjectName;
                string projectLocation = content.ProjectLocation;
                string fileName = content.FileName;
                var selectedVersion = content.SelectedVersion;
                string blenderExePath = selectedVersion?.ExecutablePath ?? string.Empty;
                string blenderVersionStr = selectedVersion?.Version ?? "Unknown";
                var folders = content.Folders.Where(f => f.IsSelected).Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var launchers = content.FileLaunchers
                    .Where(l => !string.IsNullOrWhiteSpace(l.Extension) && !string.IsNullOrWhiteSpace(l.ProgramPath))
                    .GroupBy(l => l.Extension.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First().ProgramPath);

                CreatingProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = $"Creating project '{projectName}'...";

                try
                {
                    Project newProject = new Project
                    {
                        Name = projectName,
                        Path = Path.Combine(projectLocation, projectName),
                        BlendFileName = fileName,
                        BlenderVersion = blenderVersionStr,
                        CreatedAt = DateTime.Now,
                        Subfolders = folders,
                        FileLaunchers = launchers
                    };

                    await Task.Run(() =>
                    {
                        if (!Directory.Exists(newProject.Path)) Directory.CreateDirectory(newProject.Path);
                        foreach (var sub in newProject.Subfolders)
                        {
                            string subPath = Path.Combine(newProject.Path, sub);
                            if (!Directory.Exists(subPath)) Directory.CreateDirectory(subPath);
                        }

                        if (!File.Exists(newProject.FullBlendPath))
                        {
                            bool createdProperly = false;
                            if (!string.IsNullOrEmpty(blenderExePath) && File.Exists(blenderExePath))
                            {
                                try
                                {
                                    var info = new ProcessStartInfo
                                    {
                                        FileName = blenderExePath,
                                        Arguments = $"--background --python-expr \"import bpy; bpy.ops.wm.save_as_mainfile(filepath=r'{newProject.FullBlendPath}')\"",
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    };
                                    using var p = Process.Start(info);
                                    if (p != null) createdProperly = p.WaitForExit(10000) && File.Exists(newProject.FullBlendPath);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[ProjectPage] Error starting Blender process: {ex.Message}");
                                    throw new Exception($"Failed to create the .blend file using the Blender executable. {ex.Message}");
                                }
                            }
                            if (!createdProperly)
                            {
                                throw new Exception("Failed to create the .blend file. The Blender process timed out or the file was not created.");
                            }
                        }
                    });

                    _allProjects.Add(newProject);
                    _lastCreatedProject = newProject;
                    ProjectService.SaveProjects(_allProjects);
                    ApplyFilterAndSort();

                    ProjectSuccessInfoBar.Title = "Project Created";
                    ProjectSuccessInfoBar.Message = $"Project '{newProject.Name}' has been successfully setup at {newProject.Path}.";
                    ProjectSuccessInfoBar.IsOpen = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProjectPage] Error creating project: {ex}");
                    await Task.Delay(100);
                    var errorDialog = new ContentDialog
                    {
                        Title = "Creation Failed",
                        Content = $"Could not create project folders: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
                    };
                    await errorDialog.ShowAsync();
                }
                finally { CreatingProgressPanel.Visibility = Visibility.Collapsed; }
            }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (_lastCreatedProject != null && File.Exists(_lastCreatedProject.FullBlendPath))
                Process.Start(new ProcessStartInfo(_lastCreatedProject.FullBlendPath) { UseShellExecute = true });
        }

        private async void BrowseProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".blend");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file != null) await ProcessDroppedFolderAsync(await file.GetParentAsync());
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
                }
            }
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            RootGrid_DragOver(sender, e);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DragOverlay_Drop(sender, e);
        }

        private async Task ProcessDroppedFolderAsync(Windows.Storage.StorageFolder folder)
        {
            if (folder == null) return;
            string path = folder.Path;
            var blendFiles = Directory.GetFiles(path, "*.blend");
            if (blendFiles.Length == 0) return;

            string mainBlend = blendFiles[0];
            if (_allProjects.Any(p => p.Path == path)) return;

            // Automatically scan for existing subdirectories
            var subfolders = Directory.GetDirectories(path)
                .Select(d => System.IO.Path.GetFileName(d))
                .ToList();

            var newProject = new Project
            {
                Name = folder.Name,
                Path = path,
                BlendFileName = Path.GetFileName(mainBlend),
                CreatedAt = DateTime.Now,
                BlenderVersion = "Unknown",
                Subfolders = subfolders
            };

            if (AppSettingsService.Instance.Settings.AutoDetectBlenderVersion)
            {
                await ProjectService.DetectProjectVersionsAsync(new List<Project> { newProject });
            }

            _allProjects.Add(newProject);
            ProjectService.SaveProjects(_allProjects);
            ApplyFilterAndSort();
        }
    }
}

