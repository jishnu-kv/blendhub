using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using Windows.UI;

namespace BlendHub.Models
{
    public class ConfigItemViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private bool _isExists = true;

        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string TooltipText { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsFolder { get; set; }

        public bool IsExists
        {
            get => _isExists;
            set
            {
                if (_isExists != value)
                {
                    _isExists = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExists)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusGlyph)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusPillBackground)));
                }
            }
        }

        public string StatusText => IsExists ? "Backed Up" : "Missing";
        public string StatusGlyph => IsExists ? "\uE8FB" : "\uE711";

        public Brush StatusBrush => IsExists
            ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorSuccessBrush"]
            : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"];

        public Brush StatusPillBackground
        {
            get
            {
                var resourceName = IsExists ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush";
                if (Microsoft.UI.Xaml.Application.Current.Resources[resourceName] is SolidColorBrush brush)
                {
                    return new SolidColorBrush(brush.Color) { Opacity = 0.1 };
                }
                return IsExists
                    ? new SolidColorBrush(Color.FromArgb(26, 16, 124, 65))
                    : new SolidColorBrush(Color.FromArgb(26, 216, 59, 1));
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class TargetVersionViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Version { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string DisplayName => $"Blender {Version}";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public class CategoryGroup
    {
        public string Key { get; set; } = string.Empty;
        public System.Collections.Generic.IEnumerable<ConfigItemViewModel> Items { get; set; } = new System.Collections.Generic.List<ConfigItemViewModel>();
    }
}
