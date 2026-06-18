using BlendHub.Helpers;
using BlendHub.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class DownloadPage : Page
    {
        private List<BlenderVersionGroup> _allVersions = new();
        private string _currentSearchText = string.Empty;
        private string _currentSortOption = "Version (Newest First)";
        private string _selectedPlatform = "all";
        private string _selectedType = "all";
        private bool _isLoading = false;
        private Dictionary<string, BlenderVersionJsonInfo> _rawWebData = new();
        private readonly BlendHub.Services.BlenderSettingsService _blenderSettingsService = new();

        public static string ActiveDownloadFilename { get; set; } = string.Empty;
        public static bool IsCurrentlyDownloading { get; set; } = false;

        public ObservableCollection<VersionGroup> GroupedVersionsCollection { get; } = new();
        public ObservableCollection<BlenderVersionGroup> InstalledVersionsCollection { get; } = new();
        public ObservableCollection<WindowsInstaller> LatestInstallersCollection { get; } = new();
        public ObservableCollection<WindowsInstaller> OlderInstallersCollection { get; } = new();

        // Download management fields
        private System.Threading.CancellationTokenSource? _cts;
        private bool _isDownloading = false;
        private bool _isPaused = false;
        private string _downloadUrl = string.Empty;
        private string _downloadFilename = string.Empty;
        private Windows.Storage.StorageFile? _destinationFile;
        private long _totalBytesRead = 0;
        private long _contentLength = 0;
        private string _downloadedFilePath = string.Empty;
        private static readonly HttpClient _httpClient = new();
        private DateTime? _lastSyncedTime;
        private List<WindowsInstaller> _currentInstallers = new();
        private string _currentVersionId = string.Empty;

        public static readonly DependencyProperty InstallerCountVisibilityProperty =
            DependencyProperty.Register(
                nameof(InstallerCountVisibility),
                typeof(Visibility),
                typeof(DownloadPage),
                new PropertyMetadata(Visibility.Visible));

        public Visibility InstallerCountVisibility
        {
            get => (Visibility)GetValue(InstallerCountVisibilityProperty);
            set => SetValue(InstallerCountVisibilityProperty, value);
        }

        public static DownloadPage? Instance { get; private set; }

        public DownloadPage()
        {
            Instance = this;
            this.InitializeComponent();

            GroupedVersions.Source = GroupedVersionsCollection;
            LatestInstallersList.ItemsSource = LatestInstallersCollection;
            OlderInstallersList.ItemsSource = OlderInstallersCollection;
            this.SizeChanged += DownloadPage_SizeChanged;
        }

        private void DownloadPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            InstallerCountVisibility = e.NewSize.Width <= 1004 ? Visibility.Collapsed : Visibility.Visible;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshDownloadedFilesCache();
            if (_allVersions.Count == 0)
            {
                _ = LoadVersionsAsync();
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            RefreshButton.IsEnabled = !isLoading;
            RefreshButtonIcon.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
            RefreshProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadVersionsAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            SetLoadingState(true);
            ErrorInfoBar.IsOpen = false;

            try
            {
                await LoadWebVersionsAsync();
                ShowVersions();

                // Set initial synced time from database file
                var dbPath = WebVersionDatabaseHelper.GetDatabasePath();
                if (File.Exists(dbPath))
                {
                    _lastSyncedTime = File.GetLastWriteTime(dbPath);
                    UpdateRefreshButtonToolTip();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Error loading versions: {ex.Message}\n{ex.StackTrace}");
                ShowError($"Failed to load versions: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
                SetLoadingState(false);
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
                _rawWebData = await WebVersionDatabaseHelper.GetAllVersionsAsync() ?? new Dictionary<string, BlenderVersionJsonInfo>();
                Debug.WriteLine($"[DownloadPage] Raw data count: {_rawWebData.Count}");

                if (_rawWebData.Count == 0)
                {
                    Debug.WriteLine("[DownloadPage] No web version data available");
                    _allVersions = new List<BlenderVersionGroup>();
                    return;
                }

                _allVersions = BuildVersionGroups(_rawWebData);
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
                    // Find the latest installer based on version (and date)
                    var sortedInstallers = versionInfo.WindowsInstallers
                        .Select(installer =>
                        {
                            var fullV = VersionHelper.GetFullVersionFromFilename(installer.Filename);
                            var parsedV = VersionHelper.ParseVersion(fullV);
                            DateTime.TryParse(installer.ReleaseDate, out var rDate);
                            return new { Installer = installer, ParsedVersion = parsedV, ReleaseDate = rDate, FullVersion = fullV };
                        })
                        .OrderByDescending(x => x.ParsedVersion)
                        .ThenByDescending(x => x.ReleaseDate)
                        .ToList();

                    if (sortedInstallers.Count > 0)
                    {
                        var latestInstaller = sortedInstallers[0];
                        var fullVersion = latestInstaller.FullVersion;
                        var shortVersion = VersionHelper.GetShortVersion(fullVersion);

                        if (!versionGroupsBuilder.ContainsKey(shortVersion))
                        {
                            versionGroupsBuilder[shortVersion] = (
                                fullVersion,
                                latestInstaller.Installer.ReleaseDate,
                                versionInfo.WindowsInstallers.Count
                            );
                        }
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

            if (sorted.Count > 0)
            {
                sorted[0].IsLatest = Visibility.Visible;
            }

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

        private void UpdateInfoBarPanelVisibility()
        {
            if (InfoBarPanel != null)
            {
                bool anyOpen = (SuccessInfoBar != null && SuccessInfoBar.IsOpen) || 
                               (ErrorInfoBar != null && ErrorInfoBar.IsOpen);
                InfoBarPanel.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void InfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            UpdateInfoBarPanelVisibility();
        }

        private void ShowError(string message)
        {
            if (ErrorInfoBar != null)
            {
                ErrorInfoBar.Message = message;
                ErrorInfoBar.IsOpen = true;
                UpdateInfoBarPanelVisibility();
            }
        }

        private void ShowSuccess(string title, string message)
        {
            if (SuccessInfoBar != null)
            {
                SuccessInfoBar.Title = title;
                SuccessInfoBar.Message = message;
                SuccessInfoBar.IsOpen = true;
                UpdateInfoBarPanelVisibility();
            }
        }

        private void ApplyFiltersAndSort()
        {
            // Remember the currently selected item before filtering
            var activeList = ActiveVersionList;
            var previouslySelected = activeList?.SelectedItem as BlenderVersionGroup;

            // Clear the selection temporarily to prevent WinUI selection state desync
            if (VersionsGridView != null) VersionsGridView.SelectedItem = null;

            // Get selected platform
            string platformTag = _selectedPlatform;

            // Get selected type
            string typeTag = _selectedType;

            // Filter and update counts on version groups based on current platform/type filters
            var updatedVersions = new List<BlenderVersionGroup>();
            foreach (var vGroup in _allVersions)
            {
                if (_rawWebData.TryGetValue(vGroup.ShortVersion, out var rawInfo) && rawInfo.WindowsInstallers != null)
                {
                    var matchingInstallers = rawInfo.WindowsInstallers.AsEnumerable();
                    if (platformTag != "all")
                    {
                        matchingInstallers = matchingInstallers.Where(i => platformTag.Contains(GetPlatformFromFilename(i.Filename)));
                    }
                    if (typeTag != "all")
                    {
                        matchingInstallers = matchingInstallers.Where(i => Path.GetExtension(i.Filename)?.ToLower() == typeTag);
                    }

                    // Check if group metadata matches search
                    bool groupMatchesSearch = false;
                    if (string.IsNullOrEmpty(_currentSearchText))
                    {
                        groupMatchesSearch = true;
                    }
                    else
                    {
                        var searchLower = _currentSearchText.ToLower();
                        if (vGroup.Version.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                            vGroup.ShortVersion.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                            vGroup.ReleaseDate.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
                        {
                            groupMatchesSearch = true;
                        }
                    }

                    // If we should filter installers by search, do so
                    if (!string.IsNullOrEmpty(_currentSearchText))
                    {
                        bool shouldFilter = ShouldFilterInstallersBySearch(vGroup.ShortVersion, _currentSearchText);
                        if (shouldFilter || !groupMatchesSearch)
                        {
                            var searchLower = _currentSearchText.ToLower();
                            matchingInstallers = matchingInstallers.Where(i =>
                            {
                                var installerVersion = VersionHelper.GetFullVersionFromFilename(i.Filename);
                                return installerVersion.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                                       i.Filename.Contains(searchLower, StringComparison.OrdinalIgnoreCase);
                            });
                        }
                    }

                    int count = matchingInstallers.Count();
                    if (count > 0)
                    {
                        vGroup.InstallersCountText = $"{count} installer{(count > 1 ? "s" : "")} available";
                        updatedVersions.Add(vGroup);
                    }
                }
            }

            IEnumerable<BlenderVersionGroup> filtered = updatedVersions;

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

            int totalCount = filtered.Count();
            if (ItemCountTextBlock != null)
            {
                ItemCountTextBlock.Text = totalCount == 1 ? "1 version" : $"{totalCount} versions";
            }

            if (NoSearchResultsPanel != null)
            {
                if (groupedCollection.Count == 0 && !string.IsNullOrEmpty(_currentSearchText))
                {
                    NoSearchResultsPanel.Visibility = Visibility.Visible;
                    if (VersionsGridView != null) VersionsGridView.Visibility = Visibility.Collapsed;

                    if (DetailsPanelOuterGrid != null) DetailsPanelOuterGrid.Visibility = Visibility.Collapsed;
                    if (DetailsSeparator != null) DetailsSeparator.Visibility = Visibility.Collapsed;
                    if (LeftBodyGrid != null) Grid.SetColumnSpan(LeftBodyGrid, 3);
                }
                else
                {
                    NoSearchResultsPanel.Visibility = Visibility.Collapsed;
                    if (VersionsGridView != null) VersionsGridView.Visibility = Visibility.Visible;

                    if (DetailsPanelOuterGrid != null) DetailsPanelOuterGrid.Visibility = Visibility.Visible;
                    if (DetailsSeparator != null) DetailsSeparator.Visibility = Visibility.Visible;
                    if (LeftBodyGrid != null) Grid.SetColumnSpan(LeftBodyGrid, 1);

                    // Restore selection cleanly
                    if (VersionsGridView != null)
                    {
                        var allGroupedItems = GroupedVersionsCollection.SelectMany(g => g).ToList();
                        var itemToSelect = allGroupedItems.FirstOrDefault(i => i.ShortVersion == previouslySelected?.ShortVersion);

                        if (itemToSelect == null)
                        {
                            // Fallback to first item if previous is no longer available in the filtered set
                            itemToSelect = allGroupedItems.FirstOrDefault();
                        }

                        if (itemToSelect != null)
                        {
                            VersionsGridView.SelectedItem = itemToSelect;
                        }
                    }
                }
            }

            ApplyInstallersFilter();
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
                ApplyFiltersAndSort();
            }
        }

        private void PlatformMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _selectedPlatform = item.Tag?.ToString() ?? "all";
                ApplyFiltersAndSort();
                ApplyInstallersFilter();
            }
        }

        private void TypeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _selectedType = item.Tag?.ToString() ?? "all";
                ApplyFiltersAndSort();
                ApplyInstallersFilter();
            }
        }

        private GridView ActiveVersionList => VersionsGridView;

        private void VersionsListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is GridView gridView && gridView.ItemsPanelRoot is ItemsWrapGrid panel)
            {
                // Auto stretching columns: calculate width to stretch nicely across multiple columns (minimum width 280)
                double minWidth = 280;
                double availableWidth = e.NewSize.Width - 8;
                int columns = (int)Math.Max(1, Math.Floor(availableWidth / minWidth));
                panel.ItemWidth = Math.Max(minWidth, availableWidth / columns);
            }
        }

        private void VersionsListView_SelectionChanged(object sender, SelectionChangedEventArgs? e)
        {
            var activeList = sender as GridView;
            if (activeList == null) return;

            if (activeList.SelectedItem is BlenderVersionGroup group)
            {
                ShowVersionDetails(group);
            }
            else if (VersionsGridView == null || VersionsGridView.SelectedItem == null)
            {
                HideVersionDetails();
            }
        }

        private void ShowVersionDetails(BlenderVersionGroup group)
        {
            if (group == null)
            {
                HideVersionDetails();
                return;
            }

            _currentVersionId = group.ShortVersion;

            // Set header info
            DetailVersionText.Text = $"Blender {group.Version}";
            DetailReleaseDateText.Text = string.IsNullOrEmpty(group.ReleaseDate) ? string.Empty : $"Released: {group.ReleaseDate}";

            if (group.IsInstalled && group.IsUpdateAvailable)
            {
                var matchingWebGroup = _allVersions.FirstOrDefault(w => w.ShortVersion == group.ShortVersion);
                string latestVersionStr = matchingWebGroup?.Version ?? group.ShortVersion;
                DetailUpdateInfoBar.Message = $"Version {latestVersionStr} is available. Click update to install.";
                DetailUpdateInfoBar.IsOpen = true;
            }
            else
            {
                DetailUpdateInfoBar.IsOpen = false;
            }

            // Show details panel
            DetailsGrid.Visibility = Visibility.Visible;
            NoSelectionPanel.Visibility = Visibility.Collapsed;

            // Load installers for this version from cached rawWebData
            if (_rawWebData.TryGetValue(group.ShortVersion, out var versionInfo))
            {
                _currentInstallers = versionInfo.WindowsInstallers ?? new List<WindowsInstaller>();
            }
            else
            {
                _currentInstallers = new List<WindowsInstaller>();
            }

            ApplyInstallersFilter();

            // Update bottom panel visibility based on active download / completion
            if (DetailBottomPanel != null && DetailProgressPanel != null && DetailCompletionPanel != null)
            {
                bool hasActiveDownloadForThisVersion = _isDownloading && _currentInstallers.Any(i => i.Filename == _downloadFilename);
                bool hasActiveCompletionForThisVersion = !_isDownloading && !string.IsNullOrEmpty(_downloadedFilePath) && _currentInstallers.Any(i => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", i.Filename) == _downloadedFilePath);

                if (hasActiveDownloadForThisVersion)
                {
                    DetailBottomPanel.Visibility = Visibility.Visible;
                    DetailProgressPanel.Visibility = Visibility.Visible;
                    DetailCompletionPanel.Visibility = Visibility.Collapsed;
                }
                else if (hasActiveCompletionForThisVersion)
                {
                    DetailBottomPanel.Visibility = Visibility.Visible;
                    DetailProgressPanel.Visibility = Visibility.Collapsed;
                    DetailCompletionPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    DetailBottomPanel.Visibility = Visibility.Collapsed;
                    DetailProgressPanel.Visibility = Visibility.Collapsed;
                    DetailCompletionPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void HideVersionDetails()
        {
            _currentVersionId = string.Empty;
            _currentInstallers = new List<WindowsInstaller>();
            if (DetailUpdateInfoBar != null) DetailUpdateInfoBar.IsOpen = false;
            if (DetailsGrid != null) DetailsGrid.Visibility = Visibility.Collapsed;
            if (NoSelectionPanel != null) NoSelectionPanel.Visibility = Visibility.Visible;
        }

        private void ApplyInstallersFilter()
        {
            if (_currentInstallers == null) return;

            var filtered = _currentInstallers.AsEnumerable();

            // Get selected platform
            string platformTag = _selectedPlatform;

            // Get selected type
            string typeTag = _selectedType;

            // Filter by platform
            if (platformTag != "all")
            {
                filtered = filtered.Where(i =>
                {
                    var platform = GetPlatformFromFilename(i.Filename);
                    return platformTag.Contains(platform);
                });
            }

            // Filter by type
            if (typeTag != "all")
            {
                filtered = filtered.Where(i =>
                {
                    var ext = Path.GetExtension(i.Filename)?.ToLower();
                    return ext == typeTag;
                });
            }

            // Filter by search query if it is active
            if (!string.IsNullOrEmpty(_currentSearchText))
            {
                var activeGroup = _allVersions.FirstOrDefault(g => g.ShortVersion == _currentVersionId);
                bool groupMatchesSearch = false;
                if (activeGroup != null)
                {
                    var searchLower = _currentSearchText.ToLower();
                    if (activeGroup.Version.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                        activeGroup.ShortVersion.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                        activeGroup.ReleaseDate.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
                    {
                        groupMatchesSearch = true;
                    }
                }

                bool shouldFilter = activeGroup != null && ShouldFilterInstallersBySearch(activeGroup.ShortVersion, _currentSearchText);

                if (shouldFilter || !groupMatchesSearch)
                {
                    var searchLower = _currentSearchText.ToLower();
                    filtered = filtered.Where(i =>
                    {
                        var installerVersion = VersionHelper.GetFullVersionFromFilename(i.Filename);
                        return installerVersion.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                               i.Filename.Contains(searchLower, StringComparison.OrdinalIgnoreCase);
                    });
                }
            }

            var resultList = filtered.ToList();

            // Sort descending: latest version on top
            var sortedResultList = resultList
                .Select(i => new { Installer = i, ParsedVersion = VersionHelper.ParseVersion(VersionHelper.GetFullVersionFromFilename(i.Filename)) })
                .OrderByDescending(x => x.ParsedVersion)
                .Select(x => x.Installer)
                .ToList();

            // Find the highest version number present
            string highestVersionStr = string.Empty;
            Version highestVersion = new Version(0, 0);
            foreach (var installer in sortedResultList)
            {
                var fullVersionStr = VersionHelper.GetFullVersionFromFilename(installer.Filename);
                var parsed = VersionHelper.ParseVersion(fullVersionStr);
                if (parsed > highestVersion)
                {
                    highestVersion = parsed;
                    highestVersionStr = fullVersionStr;
                }
            }

            // Split into Latest and Older groups
            var latestGroup = sortedResultList.Where(i => {
                var ver = VersionHelper.ParseVersion(VersionHelper.GetFullVersionFromFilename(i.Filename));
                return ver == highestVersion;
            }).ToList();

            var olderGroup = sortedResultList.Where(i => {
                var ver = VersionHelper.ParseVersion(VersionHelper.GetFullVersionFromFilename(i.Filename));
                return ver < highestVersion;
            }).ToList();

            // Update header text
            if (LatestInstallersHeader != null)
            {
                LatestInstallersHeader.Text = string.IsNullOrEmpty(highestVersionStr) ? "Latest Version" : $"Latest Version ({highestVersionStr})";
            }

            // Set visibility of groups
            if (LatestInstallersGroup != null)
                LatestInstallersGroup.Visibility = latestGroup.Any() ? Visibility.Visible : Visibility.Collapsed;

            if (OlderInstallersGroup != null)
                OlderInstallersGroup.Visibility = olderGroup.Any() ? Visibility.Visible : Visibility.Collapsed;

            // Update LatestInstallersCollection
            if (LatestInstallersCollection != null)
            {
                for (int i = LatestInstallersCollection.Count - 1; i >= 0; i--)
                {
                    if (!latestGroup.Any(item => item.Filename == LatestInstallersCollection[i].Filename))
                    {
                        LatestInstallersCollection.RemoveAt(i);
                    }
                }
                for (int i = 0; i < latestGroup.Count; i++)
                {
                    var targetItem = latestGroup[i];
                    int existingIndex = -1;
                    for (int j = 0; j < LatestInstallersCollection.Count; j++)
                    {
                        if (LatestInstallersCollection[j].Filename == targetItem.Filename)
                        {
                            existingIndex = j;
                            break;
                        }
                    }
                    if (existingIndex == -1)
                    {
                        LatestInstallersCollection.Insert(i, targetItem);
                    }
                    else if (existingIndex != i)
                    {
                        LatestInstallersCollection.Move(existingIndex, i);
                    }
                }
            }

            // Update OlderInstallersCollection
            if (OlderInstallersCollection != null)
            {
                for (int i = OlderInstallersCollection.Count - 1; i >= 0; i--)
                {
                    if (!olderGroup.Any(item => item.Filename == OlderInstallersCollection[i].Filename))
                    {
                        OlderInstallersCollection.RemoveAt(i);
                    }
                }
                for (int i = 0; i < olderGroup.Count; i++)
                {
                    var targetItem = olderGroup[i];
                    int existingIndex = -1;
                    for (int j = 0; j < OlderInstallersCollection.Count; j++)
                    {
                        if (OlderInstallersCollection[j].Filename == targetItem.Filename)
                        {
                            existingIndex = j;
                            break;
                        }
                    }
                    if (existingIndex == -1)
                    {
                        OlderInstallersCollection.Insert(i, targetItem);
                    }
                    else if (existingIndex != i)
                    {
                        OlderInstallersCollection.Move(existingIndex, i);
                    }
                }
            }

            if (DetailNoInstallersMessage != null)
                DetailNoInstallersMessage.Visibility = sortedResultList.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void DetailViewDownloadPage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentVersionId)) return;
            var url = $"https://download.blender.org/release/Blender{_currentVersionId}/";
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }

        private async void InstallerDownload_Click(object sender, SplitButtonClickEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsInstaller installer)
            {
                if (!string.IsNullOrEmpty(installer.Url))
                {
                    await DownloadFileAsync(installer.Url, installer.Filename);
                }
            }
        }

        private async void DownloadExternally_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsInstaller installer)
            {
                if (!string.IsNullOrEmpty(installer.Url))
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(installer.Url));
                }
            }
        }

        private async Task DownloadFileAsync(string url, string filename)
        {
            _downloadUrl = url;
            _downloadFilename = filename;
            _totalBytesRead = 0;
            _contentLength = 0;
            _isPaused = false;

            try
            {
                _isDownloading = true;
                IsCurrentlyDownloading = true;
                ActiveDownloadFilename = filename;
                ApplyInstallersFilter();

                // Get Downloads folder
                var downloadsFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");

                // Create destination file with .tmp suffix
                _destinationFile = await downloadsFolder.CreateFileAsync(filename + ".tmp", Windows.Storage.CreationCollisionOption.GenerateUniqueName);

                // Set UI state
                if (DetailBottomPanel != null) DetailBottomPanel.Visibility = Visibility.Visible;
                DetailProgressPanel.Visibility = Visibility.Visible;
                DetailCompletionPanel.Visibility = Visibility.Collapsed;
                PauseResumeIcon.Glyph = "\uE769"; // Pause glyph
                PauseResumeText.Text = "Pause";
                PauseResumeBtn.IsEnabled = true;
                DeleteDownloadBtn.IsEnabled = true;
                DetailProgressBar.Value = 0;
                DetailProgressText.Text = $"Starting download of {filename}...";

                // Start active download stream
                await StartDownloadStreamAsync();
            }
            catch (Exception ex)
            {
                ShowDownloadError(ex.Message);
            }
        }

        private async Task StartDownloadStreamAsync()
        {
            if (string.IsNullOrEmpty(_downloadUrl) || _destinationFile == null) return;

            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, _downloadUrl);

                if (_totalBytesRead > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(_totalBytesRead, null);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                if (_totalBytesRead > 0)
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    {
                        _totalBytesRead = 0;
                    }
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }

                if (_totalBytesRead == 0)
                {
                    _contentLength = response.Content.Headers.ContentLength ?? 0;
                }
                else
                {
                    if (response.Content.Headers.ContentRange?.Length.HasValue == true)
                    {
                        _contentLength = response.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (response.Content.Headers.ContentLength.HasValue)
                    {
                        _contentLength = _totalBytesRead + response.Content.Headers.ContentLength.Value;
                    }
                }

                using var contentStream = await response.Content.ReadAsStreamAsync(token);

                using var fileStream = await _destinationFile.OpenStreamForWriteAsync();
                if (_totalBytesRead > 0)
                {
                    fileStream.Seek(_totalBytesRead, SeekOrigin.Begin);
                }
                else
                {
                    fileStream.SetLength(0);
                }

                var buffer = new byte[8192];
                var lastProgressUpdate = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                    _totalBytesRead += bytesRead;

                    var progress = _contentLength > 0 ? (double)_totalBytesRead / _contentLength : 0;
                    var progressPercent = (int)(progress * 100);

                    if (progressPercent > lastProgressUpdate && (progressPercent - lastProgressUpdate >= 1))
                    {
                        lastProgressUpdate = progressPercent;

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_isPaused && _isDownloading)
                            {
                                DetailProgressBar.Value = progressPercent;
                                DetailProgressText.Text = $"Downloading {_downloadFilename}: {FormatBytes(_totalBytesRead)} of {FormatBytes(_contentLength)} ({progressPercent}%)";
                            }
                        });
                    }
                }

                await fileStream.FlushAsync(token);

                // Download completed successfully
                DispatcherQueue.TryEnqueue(async () =>
                {
                    _isDownloading = false;
                    IsCurrentlyDownloading = false;
                    ActiveDownloadFilename = string.Empty;

                    try
                    {
                        var downloadsFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");

                        string finalFilename = _downloadFilename;
                        await _destinationFile.RenameAsync(finalFilename, Windows.Storage.NameCollisionOption.ReplaceExisting);
                        _downloadedFilePath = Path.Combine(downloadsFolder.Path, finalFilename);
                        RefreshDownloadedFilesCache();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DownloadPage] Failed to rename completed download: {ex.Message}");
                        _downloadedFilePath = _destinationFile.Path;
                    }

                    if (DetailBottomPanel != null) DetailBottomPanel.Visibility = Visibility.Visible;
                    DetailProgressPanel.Visibility = Visibility.Collapsed;
                    DetailCompletionPanel.Visibility = Visibility.Visible;
                    CompletionMessageText.Text = $"Download complete: {_downloadFilename} saved to Downloads folder.";
                    ApplyInstallersFilter();
                });
            }
            catch (OperationCanceledException)
            {
                // Download was paused or cancelled
            }
            catch (Exception ex)
            {
                ShowDownloadError(ex.Message);
            }
        }

        private void ShowDownloadError(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _isDownloading = false;
                IsCurrentlyDownloading = false;
                ActiveDownloadFilename = string.Empty;
                ApplyInstallersFilter();

                DetailProgressBar.Value = 0;
                DetailProgressText.Text = $"Download failed: {message}";
                PauseResumeBtn.IsEnabled = false;
            });
        }

        private async void PauseResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused)
            {
                _isPaused = false;
                PauseResumeIcon.Glyph = "\uE769";
                PauseResumeText.Text = "Pause";
                DetailProgressText.Text = $"Resuming download...";

                await StartDownloadStreamAsync();
            }
            else
            {
                _isPaused = true;
                PauseResumeIcon.Glyph = "\uE8E5";
                PauseResumeText.Text = "Resume";
                DetailProgressText.Text = $"Paused: {FormatBytes(_totalBytesRead)} of {FormatBytes(_contentLength)}";

                _cts?.Cancel();
            }
        }

        private async void DeleteDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            _isDownloading = false;
            IsCurrentlyDownloading = false;
            ActiveDownloadFilename = string.Empty;
            _isPaused = false;
            _cts?.Cancel();

            if (DetailBottomPanel != null) DetailBottomPanel.Visibility = Visibility.Collapsed;
            DetailProgressPanel.Visibility = Visibility.Collapsed;
            DetailCompletionPanel.Visibility = Visibility.Collapsed;

            try
            {
                if (_destinationFile != null)
                {
                    await _destinationFile.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete);
                }
                RefreshDownloadedFilesCache();
            }
            catch { }

            _destinationFile = null;
            _totalBytesRead = 0;
            _contentLength = 0;
            ApplyInstallersFilter();
        }

        private void OpenInstallerBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = _downloadedFilePath, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Failed to open installer: {ex.Message}");
            }
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_downloadedFilePath))
                {
                    var folderPath = Path.GetDirectoryName(_downloadedFilePath);
                    if (folderPath != null && Directory.Exists(folderPath))
                    {
                        Process.Start("explorer.exe", folderPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Failed to open download folder: {ex.Message}");
            }
        }

        private static readonly HashSet<string> _downloadedFilesCache = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastCacheRefresh = DateTime.MinValue;
        private static readonly object _cacheLock = new();

        public static void RefreshDownloadedFilesCache()
        {
            _ = RefreshDownloadedFilesCacheAsync();
        }

        public static Task RefreshDownloadedFilesCacheAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (Directory.Exists(downloadsPath))
                    {
                        var files = Directory.GetFiles(downloadsPath);
                        lock (_cacheLock)
                        {
                            _downloadedFilesCache.Clear();
                            foreach (var file in files)
                            {
                                _downloadedFilesCache.Add(Path.GetFileName(file));
                            }
                            _lastCacheRefresh = DateTime.Now;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DownloadPage] Error refreshing downloaded files cache: {ex.Message}");
                }
            });
        }

        public static bool IsDownloaded(string filename)
        {
            lock (_cacheLock)
            {
                if (_downloadedFilesCache.Count == 0 || DateTime.Now - _lastCacheRefresh > TimeSpan.FromSeconds(15))
                {
                    _lastCacheRefresh = DateTime.Now;
                    _ = RefreshDownloadedFilesCacheAsync();
                }
                return _downloadedFilesCache.Contains(filename);
            }
        }

        public static Visibility GetDownloadButtonVisibility(string filename) =>
            IsDownloaded(filename) ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility GetOpenActionsVisibility(string filename) =>
            IsDownloaded(filename) ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility GetDownloadIconVisibility(string filename)
        {
            return (IsCurrentlyDownloading && ActiveDownloadFilename == filename) ? Visibility.Collapsed : Visibility.Visible;
        }

        public static Visibility GetDownloadSpinnerVisibility(string filename)
        {
            return (IsCurrentlyDownloading && ActiveDownloadFilename == filename) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OpenDownloadedInstaller_Click(object sender, SplitButtonClickEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsInstaller installer)
            {
                try
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", installer.Filename);
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DownloadPage] Failed to open downloaded installer: {ex.Message}");
                }
            }
        }

        private void OpenDownloadedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsInstaller installer)
            {
                try
                {
                    var folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (Directory.Exists(folderPath))
                    {
                        Process.Start("explorer.exe", folderPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DownloadPage] Failed to open downloads folder: {ex.Message}");
                }
            }
        }

        private string FormatBytes(long bytes)
        {
            return BlendHub.Helpers.FormatHelper.FormatBytes(bytes);
        }

        private void UpdateRefreshButtonToolTip()
        {
            if (RefreshButton == null) return;

            string toolTipText = "Refresh";
            if (_lastSyncedTime.HasValue)
            {
                toolTipText += $"\nLast synced: {_lastSyncedTime.Value.ToString("g")}";
            }
            ToolTipService.SetToolTip(RefreshButton, toolTipText);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            SetLoadingState(true);
            if (SuccessInfoBar != null) SuccessInfoBar.IsOpen = false;
            if (ErrorInfoBar != null) ErrorInfoBar.IsOpen = false;
            UpdateInfoBarPanelVisibility();

            try
            {
                // 1. Capture old state
                var oldWebData = await WebVersionDatabaseHelper.GetAllVersionsAsync() ?? new Dictionary<string, BlenderVersionJsonInfo>();

                // 2. Perform refresh
                await WebVersionDatabaseHelper.RefreshDatabaseAsync();
                _allVersions.Clear();
                await LoadVersionsAsync();
                _lastSyncedTime = DateTime.Now;
                UpdateRefreshButtonToolTip();

                // 3. Capture new state
                var newWebData = await WebVersionDatabaseHelper.GetAllVersionsAsync() ?? new Dictionary<string, BlenderVersionJsonInfo>();

                // 4. Compare old and new state to detect changes
                var newVersions = new List<string>();
                var newInstallersCount = 0;

                foreach (var kvp in newWebData)
                {
                    var versionKey = kvp.Key;
                    var newVerInfo = kvp.Value;

                    if (!oldWebData.TryGetValue(versionKey, out var oldVerInfo))
                    {
                        newVersions.Add(newVerInfo.Version);
                        newInstallersCount += newVerInfo.WindowsInstallers?.Count ?? 0;
                    }
                    else
                    {
                        var oldFilenames = oldVerInfo.WindowsInstallers?.Select(i => i.Filename).ToHashSet() ?? new HashSet<string>();
                        var newlyAddedInstallers = newVerInfo.WindowsInstallers?
                            .Where(i => !oldFilenames.Contains(i.Filename))
                            .ToList();

                        if (newlyAddedInstallers != null && newlyAddedInstallers.Count > 0)
                        {
                            newInstallersCount += newlyAddedInstallers.Count;
                        }
                    }
                }

                if (newVersions.Count > 0 || newInstallersCount > 0)
                {
                    var msg = "";
                    if (newVersions.Count > 0)
                    {
                        msg += $"New Blender versions detected: {string.Join(", ", newVersions)}. ";
                    }
                    if (newInstallersCount > 0)
                    {
                        msg += $"{newInstallersCount} new installer(s) detected.";
                    }
                    ShowSuccess("Updates Detected", msg.Trim());
                }
                else
                {
                    ShowSuccess("Database Synced", "No new Blender versions or installers were detected.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to sync: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private string GetPlatformFromFilename(string filename)
        {
            var lower = filename.ToLower();
            if (lower.Contains("arm64") || lower.Contains("aarch64"))
                return "windows-arm64";
            if (lower.Contains("x64") || lower.Contains("64") || lower.Contains("amd64"))
                return "windows-x64";
            if (lower.Contains("x86") || lower.Contains("32") || lower.Contains("i686"))
                return "windows32";
            return "windows-x64"; // Default to x64
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

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null)
            {
                SearchBox.Text = string.Empty;
                _currentSearchText = string.Empty;

                // Clear active selections to prevent double selecting/stale highlights
                if (VersionsGridView != null) VersionsGridView.SelectedItem = null;

                ApplyFiltersAndSort();
            }
        }

        private bool ShouldFilterInstallersBySearch(string shortVersion, string searchText)
        {
            if (string.IsNullOrEmpty(searchText)) return false;

            // If the search text doesn't contain any digits, don't filter the installers (e.g. searching "LTS")
            if (!searchText.Any(char.IsDigit)) return false;

            // If the search text is a prefix of or equal to the short version (e.g. searching "5.1" or "5" for group "5.1"), don't filter
            if (shortVersion.StartsWith(searchText, StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        private void TabSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabSegmented == null || VersionsGridView == null) return;

            var selectedItem = TabSegmented.SelectedItem as CommunityToolkit.WinUI.Controls.SegmentedItem;
            string tag = selectedItem?.Tag?.ToString() ?? "download";

            VersionsGridView.SelectedItem = null;
            HideVersionDetails();

            if (tag == "installed")
            {
                VersionsGridView.ItemsSource = InstalledVersionsCollection;
                _ = LoadInstalledVersionsAsync();
            }
            else
            {
                VersionsGridView.ItemsSource = GroupedVersions.View;
                var allGroupedItems = GroupedVersionsCollection.SelectMany(g => g).ToList();
                if (allGroupedItems.Count > 0)
                {
                    VersionsGridView.SelectedItem = allGroupedItems[0];
                }
            }
        }

        private async Task LoadInstalledVersionsAsync()
        {
            try
            {
                var installed = await _blenderSettingsService.GetInstalledVersionsAsync();
                InstalledVersionsCollection.Clear();

                foreach (var inst in installed)
                {
                    var matchingWebGroup = _allVersions.FirstOrDefault(w => w.ShortVersion == inst.Version);

                    bool isUpdateAvailable = false;
                    string latestVersionStr = inst.Version;
                    if (matchingWebGroup != null)
                    {
                        latestVersionStr = matchingWebGroup.Version;
                        isUpdateAvailable = VersionHelper.IsNewerVersion(matchingWebGroup.Version, inst.FullVersion);
                    }

                    var group = new BlenderVersionGroup
                    {
                        Version = inst.FullVersion,
                        ShortVersion = inst.Version,
                        ReleaseDate = matchingWebGroup?.ReleaseDate ?? string.Empty,
                        IsInstalled = true,
                        InstalledExecutablePath = inst.ExecutablePath,
                        InstalledVersion = inst.FullVersion,
                        IsUpdateAvailable = isUpdateAvailable,
                        LastUpdatedText = $"Path: {inst.ExecutablePath}",
                        InstallersCountText = isUpdateAvailable ? $"Update available to version {latestVersionStr}" : "Up to date",
                        ComparableVersion = VersionHelper.ParseVersion(inst.Version)
                    };

                    InstalledVersionsCollection.Add(group);
                }

                if (VersionsGridView.SelectedItem == null && InstalledVersionsCollection.Count > 0)
                {
                    VersionsGridView.SelectedItem = InstalledVersionsCollection[0];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadPage] Error loading installed versions: {ex.Message}");
            }
        }

        private async void DetailUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentInstallers == null || _currentInstallers.Count == 0) return;

            var matchingWebGroup = _allVersions.FirstOrDefault(w => w.ShortVersion == _currentVersionId);
            if (matchingWebGroup == null) return;

            if (_rawWebData.TryGetValue(_currentVersionId, out var versionInfo) && versionInfo.WindowsInstallers?.Count > 0)
            {
                var installers = versionInfo.WindowsInstallers;
                var bestInstaller = installers.FirstOrDefault(i => i.Filename.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                                 ?? installers.FirstOrDefault(i => i.Filename.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                                 ?? installers.FirstOrDefault(i => i.Filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                 ?? installers[0];

                if (!string.IsNullOrEmpty(bestInstaller.Url))
                {
                    DetailUpdateInfoBar.IsOpen = false;
                    await DownloadFileAsync(bestInstaller.Url, bestInstaller.Filename);
                }
            }
        }
    }

    public class VersionGroup : ObservableCollection<BlenderVersionGroup>
    {
        public string Key { get; set; } = string.Empty;
    }

    public class DownloadGroupHeaderMarginConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string key && DownloadPage.Instance != null && DownloadPage.Instance.GroupedVersionsCollection.Count > 0)
            {
                if (DownloadPage.Instance.GroupedVersionsCollection[0].Key == key)
                {
                    return new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8);
                }
            }
            return new Microsoft.UI.Xaml.Thickness(0, 12, 0, 8);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
