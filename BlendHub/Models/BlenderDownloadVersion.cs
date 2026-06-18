using Microsoft.UI.Xaml;
using System;

namespace BlendHub.Models
{
    public class BlenderVersionGroup
    {
        public string Version { get; set; } = string.Empty;
        public string ShortVersion { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        
        private string? _lastUpdatedText;
        public string LastUpdatedText
        {
            get => _lastUpdatedText ?? $"Last updated: {ReleaseDate}";
            set => _lastUpdatedText = value;
        }

        public Visibility IsLatest { get; set; } = Visibility.Collapsed;
        public string InstallersCountText { get; set; } = string.Empty;

        public bool IsInstalled { get; set; }
        public string InstalledExecutablePath { get; set; } = string.Empty;
        public string InstalledVersion { get; set; } = string.Empty;
        public bool IsUpdateAvailable { get; set; }
        public Visibility UpdateAvailableVisibility => IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        public string DisplayVersion => IsInstalled ? Version : ShortVersion;

        public bool IsLts => ShortVersion == "4.5" || ShortVersion == "3.6" || ShortVersion == "3.3" || ShortVersion == "2.93" || ShortVersion == "2.83" || ShortVersion == "5.2";

        public Visibility LtsVisibility => IsLts ? Visibility.Visible : Visibility.Collapsed;

        public string LtsSupportPeriod => ShortVersion switch
        {
            "5.2" => "Supported: July 2026 - July 2028",
            "4.5" => "Supported: July 2025 - July 2027",
            "3.6" => "Supported: June 2023 - June 2025",
            "3.3" => "Supported: September 2022 - September 2024",
            "2.93" => "Supported: June 2021 - June 2023",
            "2.83" => "Supported: June 2020 - June 2022",
            _ => string.Empty
        };

        // Added for performance optimization (pre-calculated sorting)
        public Version ComparableVersion { get; set; } = new Version(0, 0);
        public DateTime ComparableDate { get; set; } = DateTime.MinValue;
    }
}
