using BlendHub.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BlendHub.Models
{
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
    }

    public class FolderViewModel : FileSystemItemViewModel
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

        public Project? Project { get; set; }

        public Microsoft.UI.Xaml.Visibility BlendFileLaunchVisibility => 
            FullPath.EndsWith(".blend", StringComparison.OrdinalIgnoreCase) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
