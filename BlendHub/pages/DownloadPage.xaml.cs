using BlendHub.Helpers;
using BlendHub.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using BlendHub.Controls;
using BlendHub.Dialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class DownloadPage : Page
    {
        private List<BlenderVersionGroup> _allVersions = new();
        private string _currentSearchText = string.Empty;
        private string _currentSortOption = "Version (Newest First)";
        private bool _isLoading = false;

        public ObservableCollection<VersionGroup> GroupedVersionsCollection { get; } = new();

        public DownloadPage()
        {
            this.InitializeComponent();

            
            GroupedVersions.Source = GroupedVersionsCollection;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_allVersions.Count == 0)
            {
                _ = LoadVersionsAsync();
            }
        }

        private async Task LoadVersionsAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            LoadingPanel.Visibility = Visibility.Visible;
            ErrorInfoBar.IsOpen = false;

            try
            {
                await LoadWebVersionsAsync();
                ShowVersions();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Error loading versions: {ex.Message}\n{ex.StackTrace}");
                ShowError($"Failed to load versions: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadWebVersionsAsync()
        {
            try
            {
                Debug.WriteLine("[DownloadPage] Loading web versions...");

                // Initialize database (copies from app dir to roaming on first run)
                await WebVersionDatabaseHelper.InitializeDatabaseAsync();
                var dbPath = WebVersionDatabaseHelper.GetDatabasePath();
                Debug.WriteLine($"[DownloadPage] Database path: {dbPath}");
                Debug.WriteLine($"[DownloadPage] Database exists: {File.Exists(dbPath)}");

                // Load versions from database
                var rawData = await WebVersionDatabaseHelper.GetAllVersionsAsync();
                Debug.WriteLine($"[DownloadPage] Raw data count: {rawData?.Count ?? 0}");

                if (rawData == null || rawData.Count == 0)
                {
                    Debug.WriteLine("[DownloadPage] No web version data available");
                    _allVersions = new List<BlenderVersionGroup>();
                    return;
                }

                _allVersions = BuildVersionGroups(rawData);
                Debug.WriteLine($"[DownloadPage] Loaded {_allVersions.Count} web versions");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Error loading web versions: {ex.Message}");
                _allVersions = new List<BlenderVersionGroup>();
            }
        }

        private List<BlenderVersionGroup> BuildVersionGroups(Dictionary<string, BlenderVersionJsonInfo> rawData)
        {
            var versionGroupsBuilder = new Dictionary<string, (string fullVersion, string releaseDate, int installerCount)>();

            foreach (var versionEntry in rawData)
            {
                var versionInfo = versionEntry.Value;
                if (versionInfo.WindowsInstallers?.Count > 0)
                {
                    var fullVersion = VersionHelper.GetFullVersionFromFilename(versionInfo.WindowsInstallers[0].Filename);
                    var shortVersion = VersionHelper.GetShortVersion(fullVersion);

                    if (!versionGroupsBuilder.ContainsKey(shortVersion))
                    {
                        versionGroupsBuilder[shortVersion] = (
                            fullVersion,
                            versionInfo.WindowsInstallers[0].ReleaseDate,
                            versionInfo.WindowsInstallers.Count
                        );
                    }
                }
            }

            var groupsList = new List<BlenderVersionGroup>();
            foreach (var kvp in versionGroupsBuilder)
            {
                DateTime.TryParse(kvp.Value.releaseDate, out var date);
                groupsList.Add(new BlenderVersionGroup
                {
                    Version = kvp.Value.fullVersion,
                    ShortVersion = kvp.Key,
                    ReleaseDate = kvp.Value.releaseDate,
                    InstallersCountText = $"{kvp.Value.installerCount} installer{(kvp.Value.installerCount > 1 ? "s" : "")} available",
                    ComparableVersion = VersionHelper.ParseVersion(kvp.Key),
                    ComparableDate = date
                });
            }

            // Sort by version (newest first)
            var sorted = groupsList.OrderByDescending(v => v.ComparableVersion).ToList();

            return sorted;
        }

        private void ShowVersions()
        {
            if (_allVersions.Count == 0)
            {
                ShowError("No version data available. Please refresh.");
                GroupedVersions.Source = null;
                return;
            }

            if (ErrorInfoBar != null)
                ErrorInfoBar.IsOpen = false;

            ApplyFiltersAndSort();
        }

        private void ShowError(string message)
        {
            if (ErrorInfoBar == null) return;
            ErrorInfoBar.Message = message;
            ErrorInfoBar.IsOpen = true;
        }

        private void ApplyFiltersAndSort()
        {
            IEnumerable<BlenderVersionGroup> filtered = _allVersions;

            if (!string.IsNullOrEmpty(_currentSearchText))
            {
                var searchLower = _currentSearchText.ToLower();
                filtered = filtered.Where(v =>
                    v.Version.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    v.ShortVersion.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    v.ReleaseDate.Contains(searchLower, StringComparison.OrdinalIgnoreCase));
            }

            filtered = _currentSortOption switch
            {
                "Version (Newest First)" => filtered.OrderByDescending(v => v.ComparableVersion),
                "Version (Oldest First)" => filtered.OrderBy(v => v.ComparableVersion),
                "Release Date (Newest)" => filtered.OrderByDescending(v => v.ComparableDate),
                "Release Date (Oldest)" => filtered.OrderBy(v => v.ComparableDate),
                _ => filtered
            };

            // Determine if we should sort groups in descending order based on the selected option
            bool isDescending = _currentSortOption.Contains("Newest");

            // Group by major version (e.g., "5.x", "4.x", etc.)
            var grouped = filtered.GroupBy(v => GetMajorVersionCategory(v.ShortVersion));

            IOrderedEnumerable<IGrouping<string, BlenderVersionGroup>> sortedGroups;
            if (isDescending)
            {
                sortedGroups = grouped.OrderByDescending(g => GetMajorVersionOrder(g.Key));
            }
            else
            {
                sortedGroups = grouped.OrderBy(g => GetMajorVersionOrder(g.Key));
            }

            // Create grouped collection and update it incrementally
            var groupedCollection = new List<IGrouping<string, BlenderVersionGroup>>();
            foreach (var group in sortedGroups)
            {
                groupedCollection.Add(group);
            }

            UpdateGroupedVersions(groupedCollection);
        }

        private string GetMajorVersionCategory(string shortVersion)
        {
            // Extract major version only (e.g., "5.0" from "5.3.0", "4.0" from "4.2.1")
            if (string.IsNullOrEmpty(shortVersion)) return "Unknown";

            var parts = shortVersion.Split('.');
            if (parts.Length >= 1)
            {
                return $"{parts[0]}.0";
            }

            return shortVersion;
        }

        private int GetMajorVersionOrder(string category)
        {
            // For sorting categories (5.x comes before 4.x, etc.)
            if (string.IsNullOrEmpty(category)) return 0;

            var parts = category.Split('.');
            if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
            {
                return major;
            }

            return 0;
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                _currentSearchText = sender.Text ?? string.Empty;
                ApplyFiltersAndSort();
            }
        }

        private void SortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _currentSortOption = item.Text;
                SortButtonText.Text = item.Text;
                ApplyFiltersAndSort();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            RefreshButtonIcon.Visibility = Visibility.Collapsed;
            RefreshProgressRing.Visibility = Visibility.Visible;

            try
            {
                await WebVersionDatabaseHelper.RefreshDatabaseAsync();
                _allVersions.Clear();
                await LoadVersionsAsync();
            }
            catch (Exception ex)
            {
                ShowError($"Refresh failed: {ex.Message}");
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                RefreshButtonIcon.Visibility = Visibility.Visible;
                RefreshProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        private async void SettingsCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CommunityToolkit.WinUI.Controls.SettingsCard card && card.DataContext is BlenderVersionGroup group)
            {
                await ShowDownloadDialog(group);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BlenderVersionGroup group)
            {
                await ShowDownloadDialog(group);
            }
        }

        private async Task ShowDownloadDialog(BlenderVersionGroup group)
        {
            try
            {
                // Get installers for this version from web database
                List<WindowsInstaller> installers;
                string baseUrl = "https://download.blender.org/release";

                var webData = await WebVersionDatabaseHelper.GetAllVersionsAsync();
                if (webData.TryGetValue(group.ShortVersion, out var versionInfo))
                {
                    installers = versionInfo.WindowsInstallers ?? new List<WindowsInstaller>();
                }
                else
                {
                    installers = new List<WindowsInstaller>();
                }

                // Show download dialog
                var dialog = new ContentDialog
                {
                    Title = "Download Blender",
                    Content = new DownloadDialog(),
                    CloseButtonText = "Close",
                    DefaultButton = ContentDialogButton.None,
                    XamlRoot = this.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                    CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                };

                // Override the default max width so the dialog content isn't clipped
                dialog.Resources["ContentDialogMaxWidth"] = 850.0;

                // Initialize the content
                if (dialog.Content is DownloadDialog downloadDialog)
                {
                    downloadDialog.Initialize(
                        group.ShortVersion,
                        group.Version,
                        group.ReleaseDate,
                        group.IsLatest == Visibility.Visible,
                        installers,
                        baseUrl,
                        dialog
                    );
                }

                var result = await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Error showing download dialog: {ex.Message}");
                ShowError($"Failed to open download dialog: {ex.Message}");
            }
        }

        private void UpdateGroupedVersions(List<IGrouping<string, BlenderVersionGroup>> targetGroups)
        {
            if (GroupedVersionsCollection == null) return;

            // 1. Remove groups that are no longer in targetGroups
            for (int i = GroupedVersionsCollection.Count - 1; i >= 0; i--)
            {
                var existingGroup = GroupedVersionsCollection[i];
                if (!targetGroups.Any(g => g.Key == existingGroup.Key))
                {
                    GroupedVersionsCollection.RemoveAt(i);
                }
            }

            // 2. Add or sync groups
            for (int i = 0; i < targetGroups.Count; i++)
            {
                var targetGroup = targetGroups[i];
                var existingGroup = GroupedVersionsCollection.FirstOrDefault(g => g.Key == targetGroup.Key);

                if (existingGroup == null)
                {
                    var newGroup = new VersionGroup { Key = targetGroup.Key };
                    foreach (var item in targetGroup)
                    {
                        newGroup.Add(item);
                    }
                    GroupedVersionsCollection.Insert(i, newGroup);
                }
                else
                {
                    SyncGroupItems(existingGroup, targetGroup.ToList());

                    int existingIdx = GroupedVersionsCollection.IndexOf(existingGroup);
                    if (existingIdx != i)
                    {
                        GroupedVersionsCollection.RemoveAt(existingIdx);
                        GroupedVersionsCollection.Insert(i, existingGroup);
                    }
                }
            }
        }

        private void SyncGroupItems(VersionGroup existingGroup, List<BlenderVersionGroup> targetItems)
        {
            // Remove items no longer in targetItems
            for (int i = existingGroup.Count - 1; i >= 0; i--)
            {
                var item = existingGroup[i];
                if (!targetItems.Any(x => x.ShortVersion == item.ShortVersion))
                {
                    existingGroup.RemoveAt(i);
                }
            }

            // Add or move items to match targetItems
            for (int i = 0; i < targetItems.Count; i++)
            {
                var targetItem = targetItems[i];
                int existingIdx = -1;

                for (int j = i; j < existingGroup.Count; j++)
                {
                    if (existingGroup[j].ShortVersion == targetItem.ShortVersion)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx == -1)
                {
                    existingGroup.Insert(i, targetItem);
                }
                else if (existingIdx != i)
                {
                    var itemToMove = existingGroup[existingIdx];
                    existingGroup.RemoveAt(existingIdx);
                    existingGroup.Insert(i, itemToMove);
                }
            }
        }
    }

    public class VersionGroup : ObservableCollection<BlenderVersionGroup>
    {
        public string Key { get; set; } = string.Empty;
    }
}
