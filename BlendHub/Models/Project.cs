using System;
using System.Collections.Generic;

namespace BlendHub.Models
{
    public enum ProjectItemType { Note, Todo }

    public enum TodoPriority { None, Low, Medium, High }
    public enum TodoStatus { InProgress, Completed }

    public class ProjectItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ProjectItemType Type { get; set; } = ProjectItemType.Note;

        private string _heading = string.Empty;
        public string Heading
        {
            get => _heading;
            set { _heading = value; OnPropertyChanged(nameof(Heading)); }
        }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(nameof(Content)); }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        private DateTime? _dueDate = null;
        public DateTime? DueDate
        {
            get => _dueDate;
            set { _dueDate = value; OnPropertyChanged(nameof(DueDate)); }
        }

        // Todo-specific properties
        private TodoPriority _priority = TodoPriority.Medium;
        public TodoPriority Priority
        {
            get => _priority;
            set { _priority = value; OnPropertyChanged(nameof(Priority)); }
        }

        private TodoStatus _status = TodoStatus.InProgress;
        public TodoStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status)); 
                OnPropertyChanged(nameof(IsCompleted)); 
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsCompleted
        {
            get => _status == TodoStatus.Completed;
            set => Status = value ? TodoStatus.Completed : TodoStatus.InProgress;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public class Project : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        private string _path = string.Empty;
        public string Path
        {
            get => _path;
            set 
            { 
                _path = value; 
                OnPropertyChanged(nameof(Path)); 
                OnPropertyChanged(nameof(FolderExists)); 
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(FullBlendPath));
                OnPropertyChanged(nameof(BlendFileExists));
            }
        }

        private string _blendFileName = string.Empty;
        public string BlendFileName
        {
            get => _blendFileName;
            set 
            { 
                _blendFileName = value; 
                OnPropertyChanged(nameof(BlendFileName)); 
                OnPropertyChanged(nameof(FullBlendPath)); 
                OnPropertyChanged(nameof(BlendFileExists)); 
            }
        }

        public bool AutoUpdatePrimaryBlend { get; set; } = false;
        public List<string> Subfolders { get; set; } = new List<string>();

        private string _blenderVersion = string.Empty;
        public string BlenderVersion
        {
            get => _blenderVersion;
            set { _blenderVersion = value; OnPropertyChanged(nameof(BlenderVersion)); }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Notes { get; set; } = string.Empty; // Legacy
        public List<ProjectItem> Items { get; set; } = new List<ProjectItem>();

        private int _completionProgress = 0;
        public int CompletionProgress
        {
            get => _completionProgress;
            set { _completionProgress = value; OnPropertyChanged(nameof(CompletionProgress)); }
        }

        private bool _showProgress = true;
        public bool ShowProgress
        {
            get => _showProgress;
            set { _showProgress = value; OnPropertyChanged(nameof(ShowProgress)); }
        }

        private bool _isPinned = false;
        public bool IsPinned
        {
            get => _isPinned;
            set { _isPinned = value; OnPropertyChanged(nameof(IsPinned)); }
        }

        /// <summary>
        /// Custom file launchers: maps file extension (e.g. ".psd") to executable path.
        /// </summary>
        public Dictionary<string, string> FileLaunchers { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Manually-added project files (relative paths) that aren't auto-detected by launchers.
        /// Opened with system defaults.
        /// </summary>
        public List<string> CustomFiles { get; set; } = new List<string>();

        public string FullBlendPath => System.IO.Path.Combine(Path, BlendFileName.EndsWith(".blend") ? BlendFileName : BlendFileName + ".blend");

        [System.Text.Json.Serialization.JsonIgnore]
        public bool FolderExists => System.IO.Directory.Exists(Path);

        [System.Text.Json.Serialization.JsonIgnore]
        public bool BlendFileExists => System.IO.File.Exists(FullBlendPath);

        [System.Text.Json.Serialization.JsonIgnore]
        public string StatusText => FolderExists ? "" : "⚠ Project folder not found";

    }
}
