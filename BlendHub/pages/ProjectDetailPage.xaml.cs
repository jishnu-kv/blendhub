using BlendHub.Models;
using BlendHub.Services;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Pages
{
    public sealed partial class ProjectDetailPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        private Project? _project;
        public Project? Project { get => _project; set { _project = value; OnPropertyChanged(nameof(Project)); } }
        private System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> _items = new();
        private System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> _todoItems = new();
        private System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> _inProgressItems = new();
        private System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> _completedItems = new();

        private List<ProjectItemViewModel> _allItems = new();
        private List<ProjectItemViewModel> _allTodoItems = new();
        private List<ProjectItemViewModel> _allInProgressItems = new();
        private List<ProjectItemViewModel> _allCompletedItems = new();

        private System.Collections.ObjectModel.ObservableCollection<FolderViewModel> _locationFolders = new();
        private List<FolderViewModel> _allLocationFolders = new();
        private UIElement? _locationsPanel;

        private System.Collections.ObjectModel.ObservableCollection<ProjectFileViewModel> _projectFiles = new();
        private List<ProjectFileViewModel> _allProjectFiles = new();
        private UIElement? _filesPanel;
        private UIElement? _notesPanel;
        private UIElement? _tasksPanel;
        
        private TextBlock? _todoHeader;
        private TextBlock? _inProgressHeader;
        private TextBlock? _completedHeader;
        
        private string _searchQuery = "";

        public ProjectDetailPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is Project project)
            {
                Project = project;
                LoadProjectDetails();
            }
        }

        private void LoadProjectDetails()
        {
            if (_project == null) return;

            _locationsPanel = null;
            _allLocationFolders.Clear();
            _locationFolders.Clear();

            _filesPanel = null;
            _allProjectFiles.Clear();
            _projectFiles.Clear();

            _notesPanel = null;
            _tasksPanel = null;

            ProjectTitle.Text = _project.Name;
            ProjectPath.Text = _project.Path;
            BlenderVersionText.Text = _project.BlenderVersion;
            BlendFileText.Text = _project.BlendFileName;
            CreatedAtText.Text = _project.CreatedAt.ToString("f");

            if (Directory.Exists(_project.Path))
            {
                var dirInfo = new DirectoryInfo(_project.Path);
                ModifiedAtText.Text = dirInfo.LastWriteTime.ToString("f");
                try
                {
                    long size = CalculateFolderSize(_project.Path);
                    ProjectSizeText.Text = FormatBytes(size);
                }
                catch { ProjectSizeText.Text = "Unknown"; }
            }
            else
            {
                ModifiedAtText.Text = "N/A";
                ProjectSizeText.Text = "N/A";
            }

            bool folderExists = _project.FolderExists;
            bool blendExists = _project.BlendFileExists;

            MissingProjectInfoBar.IsOpen = !folderExists;
            OpenBlendBtn.IsEnabled = blendExists;
            OpenFolderBtn.IsEnabled = folderExists;
            EditBtn.IsEnabled = folderExists;

            if (ContentSelectorBar != null)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _previousTabIndex = -1;
                    ContentSelectorBar.SelectedItem = LocationsTab;
                    LoadTabContent(0);
                });
            }
        }

        private long CalculateFolderSize(string folderPath)
        {
            long size = 0;
            var dirInfo = new DirectoryInfo(folderPath);
            foreach (var file in dirInfo.GetFiles("*.*", SearchOption.AllDirectories)) size += file.Length;
            return size;
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1) { number /= 1024; counter++; }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private int _previousTabIndex = -1;

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
            if (_project == null || ContentFrame == null) return; // Note: using local ContentFrame

            RefreshLocationsBtn.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            AddNoteBtn.Visibility = (tabIndex == 1 || tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            if (tabIndex == 1) AddNoteBtn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE70B", FontSize = 12 }, new TextBlock { Text = "Add Note", FontSize = 13 } } };
            else if (tabIndex == 2) AddNoteBtn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uEADF", FontSize = 12 }, new TextBlock { Text = "Add Task", FontSize = 13 } } };
            AddFileBtn.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

            UIElement? newContent = null;
            switch (tabIndex)
            {
                case 0: newContent = GetLocationsPanel(); break;
                case 1: LoadProjectItems(); newContent = GetNotesPanel(); break;
                case 2: LoadProjectItems(); newContent = GetTasksPanel(); break;
                case 3: LoadProjectFiles(); newContent = GetFilesPanel(); break;
            }

            if (newContent != null)
            {
                if (animate)
                {
                    var transitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
                    var slideEffect = new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition
                    {
                        FromHorizontalOffset = _previousTabIndex == -1 ? 150 : (tabIndex > _previousTabIndex ? 150 : -150),
                        FromVerticalOffset = 0
                    };
                    transitions.Add(slideEffect);
                    ContentFrame.ContentTransitions = transitions;
                }
                else
                {
                    ContentFrame.ContentTransitions = null;
                }
                ContentFrame.Content = newContent;
            }

            UpdateSearchBoxVisibility();
        }

        private int GetFolderItemCount(string folderPath)
        {
            try
            {
                if (Directory.Exists(folderPath))
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    return dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly).Length +
                           dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly).Length;
                }
            }
            catch { }
            return 0;
        }

        private UIElement GetLocationsPanel()
        {
            if (_project == null) return new Grid();

            if (_locationsPanel == null)
            {
                if (_project.Subfolders.Count > 0)
                {
                    _allLocationFolders.Clear();
                    _locationFolders.Clear();

                    foreach (var f in _project.Subfolders)
                    {
                        string fullPath = Path.Combine(_project.Path, f);
                        var folderVm = new FolderViewModel(f, fullPath, GetFolderItemCount(fullPath))
                        {
                            IsExpanded = AppSettingsService.Instance.Settings.ExpandFoldersByDefault
                        };

                        _allLocationFolders.Add(folderVm);
                        _locationFolders.Add(folderVm);
                    }

                    var repeater = new ItemsRepeater
                    {
                        ItemsSource = _locationFolders,
                        ItemTemplate = (DataTemplate)Resources["FolderTemplate"],
                        Layout = new StackLayout { Spacing = 4 },
                        Margin = new Thickness(0, 0, 0, 24)
                    };
                    _locationsPanel = repeater;
                }
                else
                {
                    _locationsPanel = new TextBlock
                    {
                        Text = "No subfolders configured",
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 40, 0, 40)
                    };
                }
            }

            FilterLocations();

            return _locationsPanel;
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

                        // Refresh view
                        _locationsPanel = null;
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
            if (_project == null || !Directory.Exists(_project.Path)) return;

            // Sync subfolders with actual directories on disk
            var diskFolders = Directory.GetDirectories(_project.Path)
                .Select(d => Path.GetFileName(d))
                .OrderBy(n => n)
                .ToList();

            _project.Subfolders = diskFolders;
            ProjectService.UpdateProject(_project);

            _locationsPanel = null;
            _allLocationFolders.Clear();
            _locationFolders.Clear();

            // Refresh the current view
            int selectedIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
            LoadTabContent(selectedIndex);
        }

        private UIElement GetNotesPanel()
        {
            if (_notesPanel == null)
            {
                var panel = new StackPanel { Spacing = 12 };

                var itemsControl = new ItemsControl { ItemTemplateSelector = (ProjectItemTemplateSelector)Resources["ItemSelector"], ItemsSource = _items, ItemsPanel = (ItemsPanelTemplate)Resources["ItemsPanelTemplate"] };
                panel.Children.Add(itemsControl);
                
                var emptyText = new TextBlock { Text = "No notes yet", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 40) };
                
                _items.CollectionChanged += (s, e) =>
                {
                    emptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                };
                emptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                
                panel.Children.Add(emptyText);
                _notesPanel = panel;
            }
            return _notesPanel;
        }

        private UIElement GetTasksPanel()
        {
            if (_tasksPanel == null)
            {
                var panel = new StackPanel { Spacing = 12 };

                var grid = new Grid { ColumnSpacing = 16 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var template = (DataTemplate)Resources["TodoTemplate"];

                var todoList = CreateKanbanColumn("Planned", _todoItems, template, out _todoHeader);
                var inProgressList = CreateKanbanColumn("In Progress", _inProgressItems, template, out _inProgressHeader);
                var completedList = CreateKanbanColumn("Completed", _completedItems, template, out _completedHeader);

                Grid.SetColumn(todoList, 0);
                Grid.SetColumn(inProgressList, 1);
                Grid.SetColumn(completedList, 2);

                grid.Children.Add(todoList);
                grid.Children.Add(inProgressList);
                grid.Children.Add(completedList);
                panel.Children.Add(grid);
                _tasksPanel = panel;
            }

            UpdateKanbanHeaders();
            return _tasksPanel;
        }

        private FrameworkElement CreateKanbanColumn(string title, System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> items, DataTemplate template, out TextBlock headerTextBlock)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            headerTextBlock = header;
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var listView = new ListView
            {
                ItemsSource = items,
                ItemTemplate = template,
                CanDragItems = true,
                AllowDrop = true,
                SelectionMode = ListViewSelectionMode.None,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Tag = title,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            listView.Resources["ListViewItemBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            listView.Resources["ListViewItemBackgroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            listView.Resources["ListViewItemBackgroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            listView.Resources["ListViewItemBackgroundSelected"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            listView.Resources["ListViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            listView.Resources["ListViewItemBackgroundSelectedPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            listView.DragItemsStarting += Task_DragItemsStarting;
            listView.DragOver += Task_DragOver;
            listView.Drop += Task_Drop;

            var itemContainerStyle = new Style(typeof(ListViewItem));
            itemContainerStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemContainerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
            itemContainerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            listView.ItemContainerStyle = itemContainerStyle;

            Grid.SetRow(listView, 1);
            grid.Children.Add(listView);
            return grid;
        }

        private void Task_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (e.Items.Count > 0 && e.Items[0] is ProjectItemViewModel vm)
            {
                e.Data.Properties["draggedTask"] = vm;
                e.Data.Properties["sourceListView"] = sender;
            }
        }

        private void Task_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        private void Task_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Properties.TryGetValue("draggedTask", out var data) && data is ProjectItemViewModel draggedTask)
            {
                if (e.DataView.Properties.TryGetValue("sourceListView", out var source) && source is ListView sourceListView && sender is ListView targetListView)
                {
                    if (sourceListView != targetListView)
                    {
                        var sourceList = sourceListView.ItemsSource as System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel>;
                        var targetList = targetListView.ItemsSource as System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel>;
                        
                        sourceList?.Remove(draggedTask);
                        targetList?.Add(draggedTask);

                        string targetTag = targetListView.Tag as string ?? "";
                        if (targetTag == "Planned") draggedTask.Status = TodoStatus.Todo;
                        else if (targetTag == "In Progress") draggedTask.Status = TodoStatus.InProgress;
                        else if (targetTag == "Completed") draggedTask.Status = TodoStatus.Completed;

                        if (_project != null) ProjectService.UpdateProject(_project);
                        UpdateKanbanHeaders();
                    }
                }
            }
        }

        private void LoadProjectFiles()
        {
            if (_project == null) return;
            _allProjectFiles.Clear();
            _projectFiles.Clear();

            // 1. Gather Launcher Files
            if (_project.FileLaunchers.Count > 0 && _project.FolderExists)
            {
                var launcherExts = new HashSet<string>(_project.FileLaunchers.Keys, StringComparer.OrdinalIgnoreCase);
                try
                {
                    var allFiles = new DirectoryInfo(_project.Path).GetFiles("*.*", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        string ext = file.Extension.ToLowerInvariant();
                        if (launcherExts.Contains(ext))
                        {
                            string programPath = _project.FileLaunchers[ext];
                            string programName = Path.GetFileNameWithoutExtension(programPath);
                            string relFolder = Path.GetDirectoryName(Path.GetRelativePath(_project.Path, file.FullName)) ?? "";

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
                                Modified = file.LastWriteTime.ToString("g")
                            };
                            
                            _allProjectFiles.Add(vm);
                            _ = LoadFileIconAsync(file.FullName, vm);
                        }
                    }
                }
                catch { }
            }

            // 2. Gather Custom Files
            foreach (var relPath in _project.CustomFiles)
            {
                string fullPath = Path.Combine(_project.Path, relPath);
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    string relFolder = Path.GetDirectoryName(relPath) ?? "";
                    var vm = new ProjectFileViewModel(
                        Path.GetFileName(relPath),
                        string.IsNullOrEmpty(relFolder) || relFolder == "." ? "Project Root" : relFolder,
                        fullPath,
                        "",
                        "External App",
                        true
                    )
                    {
                        Size = FormatBytes(fileInfo.Length),
                        Modified = fileInfo.LastWriteTime.ToString("g")
                    };
                    _allProjectFiles.Add(vm);
                    _ = LoadFileIconAsync(fullPath, vm);
                }
            }

            FilterProjectFiles();
        }

        private void FilterProjectFiles()
        {
            var query = _searchQuery.ToLowerInvariant();
            
            var filtered = _allProjectFiles.Where(f => 
                string.IsNullOrWhiteSpace(query) || 
                f.Name.ToLowerInvariant().Contains(query) || 
                f.RelativePath.ToLowerInvariant().Contains(query)
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
                    
                    var list = targetListView.ItemsSource as System.Collections.ObjectModel.ObservableCollection<ProjectFileViewModel>;
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
                                
                                if (_project != null)
                                {
                                    string relPath = Path.GetRelativePath(_project.Path, draggedFile.FullPath);
                                    int customOldIndex = _project.CustomFiles.FindIndex(f => f.Equals(relPath, StringComparison.OrdinalIgnoreCase));
                                    if (customOldIndex != -1)
                                    {
                                        _project.CustomFiles.RemoveAt(customOldIndex);
                                        int customInsertIndex = _project.CustomFiles.Count;
                                        for (int i = index + 1; i < list.Count; i++)
                                        {
                                            if (list[i].IsCustom)
                                            {
                                                string targetRelPath = Path.GetRelativePath(_project.Path, list[i].FullPath);
                                                int targetCustomIndex = _project.CustomFiles.FindIndex(f => f.Equals(targetRelPath, StringComparison.OrdinalIgnoreCase));
                                                if (targetCustomIndex != -1)
                                                {
                                                    customInsertIndex = targetCustomIndex;
                                                    break;
                                                }
                                            }
                                        }
                                        if (customInsertIndex >= _project.CustomFiles.Count)
                                            _project.CustomFiles.Add(relPath);
                                        else
                                            _project.CustomFiles.Insert(customInsertIndex, relPath);
                                            
                                        ProjectService.UpdateProject(_project);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private UIElement GetFilesPanel()
        {
            if (_filesPanel == null)
            {
                var listView = new ListView
                {
                    Name = "Control4",
                    Height = 400,
                    MinWidth = 550,
                    BorderThickness = new Thickness(0),
                    ItemsSource = _projectFiles,
                    ItemTemplate = (DataTemplate)Resources["ProjectFileTemplate"],
                    SelectionMode = ListViewSelectionMode.Single,
                    IsItemClickEnabled = true,
                    CanReorderItems = true,
                    AllowDrop = true,
                    CanDragItems = true,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                listView.DragItemsStarting += FilesListView_DragItemsStarting;
                listView.DragOver += FilesListView_DragOver;
                listView.Drop += FilesListView_Drop;

                _filesPanel = listView;
            }

            FilterProjectFiles();

            return _filesPanel;
        }

        private async Task LoadFileIconAsync(string filePath, ProjectFileViewModel vm)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                var thumb = await file.GetThumbnailAsync(ThumbnailMode.ListView, 256);
                if (thumb != null)
                {
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(thumb.AsStreamForRead().AsRandomAccessStream());
                    vm.IconSource = bmp;
                }
            }
            catch { }
        }

        private void RemoveCustomFileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_project != null && sender is Button btn && btn.Tag is ProjectFileViewModel vm)
            {
                _project.CustomFiles.Remove(Path.GetRelativePath(_project.Path, vm.FullPath));
                ProjectService.UpdateProject(_project);
                LoadProjectFiles();
            }
        }

        private async void AddCustomFile_Click(object sender, RoutedEventArgs e)
        {
            if (_project == null) return;
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder }; picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file.Path.StartsWith(_project.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = Path.GetRelativePath(_project.Path, file.Path);
                        if (!_project.CustomFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)) _project.CustomFiles.Add(rel);
                    }
                    else
                    {
                        string dest = Path.Combine(_project.Path, file.Name);
                        try { if (!File.Exists(dest)) File.Copy(file.Path, dest); if (!_project.CustomFiles.Contains(file.Name)) _project.CustomFiles.Add(file.Name); } catch { }
                    }
                }
                ProjectService.UpdateProject(_project);
                LoadProjectFiles();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }

        private async void EditProject_Click(object sender, RoutedEventArgs e)
        {
            if (_project != null) await ProjectDialogService.ShowEditDialogAsync(_project, this.XamlRoot, LoadProjectDetails);
        }

        private async void DeleteProject_Click(object sender, RoutedEventArgs e)
        {
            if (_project != null) await ProjectDialogService.ShowDeleteConfirmAsync(_project, this.XamlRoot, () => App.MainWindow.Navigate(typeof(ProjectPage)));
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (_project != null && Directory.Exists(_project.Path)) Process.Start("explorer.exe", _project.Path); }


        private void UpdateKanbanHeaders()
        {
            if (_todoHeader != null) _todoHeader.Text = $"Planned ({_todoItems.Count})";
            if (_inProgressHeader != null) _inProgressHeader.Text = $"In Progress ({_inProgressItems.Count})";
            if (_completedHeader != null) _completedHeader.Text = $"Completed ({_completedItems.Count})";
        }

        private void LoadProjectItems()
        {
            if (_project == null) return;
            _allItems.Clear();
            _allTodoItems.Clear();
            _allInProgressItems.Clear();
            _allCompletedItems.Clear();

            foreach (var item in _project.Items.OrderByDescending(i => i.CreatedAt))
            {
                var vm = new ProjectItemViewModel(item);
                if (item.Type == ProjectItemType.Note)
                {
                    _allItems.Add(vm);
                }
                else
                {
                    switch (item.Status)
                    {
                        case TodoStatus.Todo: _allTodoItems.Add(vm); break;
                        case TodoStatus.InProgress: _allInProgressItems.Add(vm); break;
                        case TodoStatus.Completed: _allCompletedItems.Add(vm); break;
                    }
                }
            }
            
            FilterProjectItems();
        }

        private bool FilterProjectItem(ProjectItemViewModel item, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return (item.Heading?.ToLowerInvariant().Contains(query) == true) || 
                   (item.Content?.ToLowerInvariant().Contains(query) == true);
        }

        private void RemoveNonMatchingItems(IEnumerable<ProjectItemViewModel> filteredData, System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> targetCollection)
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

        private void AddBackItems(IEnumerable<ProjectItemViewModel> filteredData, System.Collections.ObjectModel.ObservableCollection<ProjectItemViewModel> targetCollection)
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

            var filteredTodo = _allTodoItems.Where(i => FilterProjectItem(i, query)).ToList();
            RemoveNonMatchingItems(filteredTodo, _todoItems);
            AddBackItems(filteredTodo, _todoItems);

            var filteredInProgress = _allInProgressItems.Where(i => FilterProjectItem(i, query)).ToList();
            RemoveNonMatchingItems(filteredInProgress, _inProgressItems);
            AddBackItems(filteredInProgress, _inProgressItems);

            var filteredCompleted = _allCompletedItems.Where(i => FilterProjectItem(i, query)).ToList();
            RemoveNonMatchingItems(filteredCompleted, _completedItems);
            AddBackItems(filteredCompleted, _completedItems);

            UpdateKanbanHeaders();
            UpdateSearchBoxVisibility();
        }

        private void UpdateSearchBoxVisibility()
        {
            if (GlobalSearchBox == null || ContentSelectorBar == null || ContentSelectorBar.SelectedItem == null) return;
            
            int tabIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
            bool hasContent = false;
            
            switch (tabIndex)
            {
                case 0: hasContent = _project != null && _project.Subfolders.Count > 0; break;
                case 1: hasContent = _allItems.Count > 0; break;
                case 2: hasContent = (_allTodoItems.Count + _allInProgressItems.Count + _allCompletedItems.Count) > 0; break;
                case 3: hasContent = _project != null; break; // We'll just check if project isn't null for now
            }
            
            GlobalSearchBox.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
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

            for (int i = _locationFolders.Count - 1; i >= 0; i--)
            {
                if (!filteredFolders.Contains(_locationFolders[i]))
                {
                    _locationFolders.RemoveAt(i);
                }
            }
            foreach (var folder in filteredFolders)
            {
                if (!_locationFolders.Contains(folder))
                {
                    _locationFolders.Add(folder);
                }
            }
        }

        private void GlobalSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                _searchQuery = sender.Text;
                
                int tabIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
                if (tabIndex == 1 || tabIndex == 2)
                {
                    FilterProjectItems();
                }
                else if (tabIndex == 0)
                {
                    FilterLocations();
                }
                else if (tabIndex == 3)
                {
                    FilterProjectFiles();
                }
            }
        }

        private async void AddProjectItem_Click(object sender, RoutedEventArgs e)
        {
            if (_project == null) return;
            var itemType = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem) == 2 ? ProjectItemType.Todo : ProjectItemType.Note;

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
                _project.Items.Add(newItem);
                ProjectService.UpdateProject(_project); LoadProjectItems();

                int selectedIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
                if (selectedIndex == 1 || selectedIndex == 2) LoadTabContent(selectedIndex);
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
                        // DueDateString doesn't have an OnPropertyChanged manually, so we'll do it by triggering a property change
                    }
                    if (_project != null) ProjectService.UpdateProject(_project);
                    
                    LoadProjectItems();
                    int selectedIndex = ContentSelectorBar.Items.IndexOf(ContentSelectorBar.SelectedItem);
                    if (selectedIndex == 1 || selectedIndex == 2) LoadTabContent(selectedIndex);
                }
            }
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem btn && btn.Tag is ProjectItemViewModel vm && _project != null)
            {
                _project.Items.Remove(vm.Item); _items.Remove(vm); ProjectService.UpdateProject(_project);
            }
        }

    }

    public class ProjectItemViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public ProjectItem Item { get; }
        public ProjectItemViewModel(ProjectItem item) { Item = item; }

        public string Id => Item.Id;
        public string Heading { get => Item.Heading; set { Item.Heading = value; OnPropertyChanged(nameof(Heading)); } }
        public string Content { get => Item.Content; set { Item.Content = value; OnPropertyChanged(nameof(Content)); } }
        public bool IsCompleted
        {
            get => Item.IsCompleted;
            set
            {
                Item.IsCompleted = value;
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(TextDecoration));
                OnPropertyChanged(nameof(TextColorBrush));
                OnPropertyChanged(nameof(Status));
            }
        }
        
        public TodoStatus Status
        {
            get => Item.Status;
            set
            {
                Item.Status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(TextDecoration));
                OnPropertyChanged(nameof(TextColorBrush));
            }
        }
        public string CreatedAtString => Item.CreatedAt.ToString("g");
        
        public string DueDateString => Item.DueDate?.ToString("d (ddd)") ?? "";
        public Microsoft.UI.Xaml.Visibility DueDateVisibility => Item.DueDate.HasValue ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Windows.UI.Text.TextDecorations TextDecoration => IsCompleted ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
        public Microsoft.UI.Xaml.Media.Brush TextColorBrush => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[IsCompleted ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"];

        public string PriorityText => Item.Priority.ToString();
        public Microsoft.UI.Xaml.Media.Brush PriorityBackgroundColor => Item.Priority switch
        {
            TodoPriority.High => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 255, 69, 58)),
            TodoPriority.Medium => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 255, 159, 10)),
            TodoPriority.Low => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 48, 209, 88)),
            _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        public Microsoft.UI.Xaml.Media.Brush PriorityTextColor => Item.Priority switch
        {
            TodoPriority.High => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 69, 58)),
            TodoPriority.Medium => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 159, 10)),
            TodoPriority.Low => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 48, 209, 88)),
            _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        };
        public Microsoft.UI.Xaml.Media.Brush PriorityIconColor => PriorityTextColor;
        public Microsoft.UI.Xaml.Visibility PriorityVisibility => Item.Priority == TodoPriority.None ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        public void UpdatePriority()
        {
            OnPropertyChanged(nameof(PriorityText));
            OnPropertyChanged(nameof(PriorityBackgroundColor));
            OnPropertyChanged(nameof(PriorityTextColor));
            OnPropertyChanged(nameof(PriorityIconColor));
            OnPropertyChanged(nameof(PriorityVisibility));
            OnPropertyChanged(nameof(DueDateString));
            OnPropertyChanged(nameof(DueDateVisibility));
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public class ProjectItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? NoteTemplate { get; set; }
        public DataTemplate? TodoTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item) => SelectTemplate(item) ?? base.SelectTemplateCore(item);

        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplate(item) ?? base.SelectTemplateCore(item, container);

        private new DataTemplate? SelectTemplate(object item)
        {
            if (item is ProjectItemViewModel vm)
            {
                return vm.Item.Type == ProjectItemType.Note ? NoteTemplate : TodoTemplate;
            }
            return null;
        }
    }

    public abstract class FileSystemItemViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class FileViewModel : FileSystemItemViewModel
    {
        public string SizeText { get; set; }
        public string ModifiedDateText { get; set; }
        private Microsoft.UI.Xaml.Media.ImageSource? _iconSource;
        public Microsoft.UI.Xaml.Media.ImageSource? IconSource
        {
            get => _iconSource;
            set 
            { 
                _iconSource = value; 
                OnPropertyChanged(nameof(IconSource)); 
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(FallbackIconVisibility));
            }
        }

        public Microsoft.UI.Xaml.Visibility IconVisibility => _iconSource != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility FallbackIconVisibility => _iconSource == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public FileViewModel(string name, string fullPath, string sizeText, string modifiedDateText, Microsoft.UI.Xaml.Media.ImageSource? iconSource)
        {
            Name = name;
            FullPath = fullPath;
            SizeText = sizeText;
            ModifiedDateText = modifiedDateText;
            _iconSource = iconSource;
        }
    }    public class FolderViewModel : FileSystemItemViewModel
    {
        public string Path { get; set; }
        public int ItemCount { get; set; }
        public string ItemCountText => $"{ItemCount} item{(ItemCount == 1 ? "" : "s")}";
        public Microsoft.UI.Xaml.Media.ImageSource FolderIcon => new BitmapImage(new Uri(ItemCount > 0 ? "ms-appx:///Assets/folder_file.png" : "ms-appx:///Assets/folder_empty.png"));

        private bool _isLoaded;
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ExpandIcon));
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));

                if (_isExpanded && !_isLoaded)
                {
                    _isLoaded = true;
                    LoadFiles();
                }
            }
        }

        public string ExpandIcon => IsExpanded ? "\uE70D" : "\uE76C";
        public Microsoft.UI.Xaml.Visibility ContentVisibility => IsExpanded ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility EmptyVisibility => (IsExpanded && Subfolders.Count == 0 && FilesOnly.Count == 0) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public System.Collections.ObjectModel.ObservableCollection<FolderViewModel> Subfolders { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<FileViewModel> FilesOnly { get; set; } = new();

        private List<FileViewModel> _allFilesOnly = new();
        private List<FolderViewModel> _allSubfolders = new();

        public FolderViewModel(string name, string path, int itemCount)
        {
            Name = name;
            FullPath = path;
            Path = path;
            ItemCount = itemCount;
            Subfolders.CollectionChanged += (s, e) => OnPropertyChanged(nameof(EmptyVisibility));
            FilesOnly.CollectionChanged += (s, e) => OnPropertyChanged(nameof(EmptyVisibility));
        }

        public bool Filter(string? query, bool parentMatched = false)
        {
            if (!_isLoaded)
            {
                _isLoaded = true;
                LoadFiles();
            }

            string queryLower = (query ?? "").ToLowerInvariant();

            bool isFolderNameMatch = parentMatched || (!string.IsNullOrEmpty(queryLower) && Name.ToLowerInvariant().Contains(queryLower));

            // Recursively filter subfolders first
            foreach (var sub in _allSubfolders)
            {
                sub.Filter(query ?? "", isFolderNameMatch);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                ResetFilter();
                return true;
            }

            var filteredSubfolders = _allSubfolders.Where(sub => 
                isFolderNameMatch || 
                sub.Name.ToLowerInvariant().Contains(queryLower) || 
                sub.Subfolders.Count > 0 || 
                sub.FilesOnly.Count > 0
            ).ToList();

            var filteredFiles = _allFilesOnly.Where(file => 
                isFolderNameMatch || 
                file.Name.ToLowerInvariant().Contains(queryLower)
            ).ToList();

            // Apply in-place changes to Subfolders collection
            for (int i = Subfolders.Count - 1; i >= 0; i--)
            {
                if (!filteredSubfolders.Contains(Subfolders[i]))
                {
                    Subfolders.RemoveAt(i);
                }
            }
            foreach (var sub in filteredSubfolders)
            {
                if (!Subfolders.Contains(sub))
                {
                    Subfolders.Add(sub);
                }
            }

            // Apply in-place changes to FilesOnly collection
            for (int i = FilesOnly.Count - 1; i >= 0; i--)
            {
                if (!filteredFiles.Contains(FilesOnly[i]))
                {
                    FilesOnly.RemoveAt(i);
                }
            }
            foreach (var file in filteredFiles)
            {
                if (!FilesOnly.Contains(file))
                {
                    FilesOnly.Add(file);
                }
            }

            OnPropertyChanged(nameof(EmptyVisibility));

            return isFolderNameMatch || Subfolders.Count > 0 || FilesOnly.Count > 0;
        }

        public void ResetFilter()
        {
            // Restore everything
            for (int i = Subfolders.Count - 1; i >= 0; i--)
            {
                if (!_allSubfolders.Contains(Subfolders[i]))
                {
                    Subfolders.RemoveAt(i);
                }
            }
            foreach (var sub in _allSubfolders)
            {
                if (!Subfolders.Contains(sub))
                {
                    Subfolders.Add(sub);
                }
                sub.ResetFilter();
            }

            for (int i = FilesOnly.Count - 1; i >= 0; i--)
            {
                if (!_allFilesOnly.Contains(FilesOnly[i]))
                {
                    FilesOnly.RemoveAt(i);
                }
            }
            foreach (var file in _allFilesOnly)
            {
                if (!FilesOnly.Contains(file))
                {
                    FilesOnly.Add(file);
                }
            }

            OnPropertyChanged(nameof(EmptyVisibility));
        }

        private void LoadFiles()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    var dir = new DirectoryInfo(Path);
                    var entries = dir.GetFileSystemInfos("*", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => f is DirectoryInfo)
                        .ThenBy(f => f.Name);

                    foreach (var f in entries)
                    {
                        if (f is DirectoryInfo di)
                        {
                            int count = 0;
                            try { count = di.GetFileSystemInfos().Length; } catch { }
                            var folderVm = new FolderViewModel(di.Name, di.FullName, count);
                            Subfolders.Add(folderVm);
                            _allSubfolders.Add(folderVm);
                        }
                        else if (f is FileInfo fi)
                        {
                            var vm = new FileViewModel(
                                f.Name,
                                f.FullName,
                                FormatBytes(fi.Length),
                                f.LastWriteTime.ToString("g"),
                                null
                            );
                            FilesOnly.Add(vm);
                            _allFilesOnly.Add(vm);
                            _ = LoadIconAsync(f.FullName, vm);
                        }
                    }
                }
            }
            catch { }
        }

        private async System.Threading.Tasks.Task LoadIconAsync(string path, FileViewModel vm)
        {
            try
            {
                StorageFile? file = null;
                if (File.Exists(path)) file = await StorageFile.GetFileFromPathAsync(path);

                if (file != null)
                {
                    var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 256);
                    if (thumb != null)
                    {
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(thumb.AsStreamForRead().AsRandomAccessStream());
                        vm.IconSource = bmp;
                    }
                }
                else if (Directory.Exists(path))
                {
                    vm.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/folder_file.png"));
                }
            }
            catch { }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1) { number /= 1024; counter++; }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }

    public class ProjectFileViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public string FullPath { get; set; }
        public string ProgramPath { get; set; }
        public string ProgramName { get; set; }
        public bool IsCustom { get; set; }
        public string Size { get; set; } = "";
        public string Modified { get; set; } = "";

        private Microsoft.UI.Xaml.Media.ImageSource? _iconSource;
        public Microsoft.UI.Xaml.Media.ImageSource? IconSource
        {
            get => _iconSource;
            set 
            { 
                _iconSource = value; 
                OnPropertyChanged(nameof(IconSource)); 
                OnPropertyChanged(nameof(IconVisibility)); 
                OnPropertyChanged(nameof(FallbackIconVisibility)); 
            }
        }

        public Microsoft.UI.Xaml.Visibility IconVisibility => _iconSource != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility FallbackIconVisibility => _iconSource == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility CustomFileVisibility => IsCustom ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility RelativePathVisibility => (string.IsNullOrEmpty(RelativePath) || RelativePath == "Project Root") ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility ProgramNameVisibility => (string.IsNullOrEmpty(ProgramName) || ProgramName == "External App") ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility FirstSeparatorVisibility => ProgramNameVisibility;

        public ProjectFileViewModel(string name, string relativePath, string fullPath, string programPath, string programName, bool isCustom)
        {
            Name = name;
            RelativePath = relativePath;
            FullPath = fullPath;
            ProgramPath = programPath;
            ProgramName = programName;
            IsCustom = isCustom;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
