using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class AddonsPage : Page
    {
        private readonly BlenderSettingsService _blenderService = new();
        private readonly AddonService _addonService = new();

        private List<AddonItem> _allAddons = new();
        public ObservableCollection<AddonGroup> GroupedAddons { get; } = new();
        private List<BlenderVersionInfo> _installedVersions = new();

        private List<AddonItem> _onlineAddons = new();
        private string _selectedVersion = "All";
        private string _selectedType = "All Types";
        private string _selectedRepo = "All";
        private string _selectedCategory = "";
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        private bool _isLoading = true;
        private DateTime? _lastSyncedTime;

        public static readonly DependencyProperty ShowBadgeGlobalProperty =
            DependencyProperty.Register(nameof(ShowBadgeGlobal), typeof(bool), typeof(AddonsPage), new PropertyMetadata(true));

        public bool ShowBadgeGlobal
        {
            get => (bool)GetValue(ShowBadgeGlobalProperty);
            set => SetValue(ShowBadgeGlobalProperty, value);
        }

        public static AddonsPage? Instance { get; private set; }

        public AddonsPage()
        {
            Instance = this;
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _isLoading = true;

            // Hide details pane initially
            DetailsGrid.Visibility = Visibility.Collapsed;
            if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;

            if (InstallAddonButton != null) InstallAddonButton.Visibility = Visibility.Visible;

            await RefreshDataAsync();

            _isLoading = false;
            ApplyFilters();

            // Background load online extensions
            await LoadOnlineExtensionsAsync();
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                // Reset InfoBars
                SuccessInfoBar.IsOpen = false;
                WarningInfoBar.IsOpen = false;
                ErrorInfoBar.IsOpen = false;

                // Load Blender Versions
                _installedVersions = await _blenderService.GetInstalledVersionsAsync();

                // Populate Version Filter SubMenu
                if (VersionSubMenu != null)
                {
                    VersionSubMenu.Items.Clear();

                    var allVerItem = new RadioMenuFlyoutItem { Text = "All Versions", Tag = "All", GroupName = "VersionGroup", IsChecked = _selectedVersion == "All" };
                    allVerItem.Click += VersionMenuItem_Click;
                    VersionSubMenu.Items.Add(allVerItem);

                    foreach (var ver in _installedVersions)
                    {
                        var verItem = new RadioMenuFlyoutItem { Text = ver.DisplayName, Tag = ver.Version, GroupName = "VersionGroup", IsChecked = _selectedVersion == ver.Version };
                        verItem.Click += VersionMenuItem_Click;
                        VersionSubMenu.Items.Add(verItem);
                    }
                }

                // Scan all Addons
                _allAddons.Clear();
                foreach (var ver in _installedVersions)
                {
                    if (!string.IsNullOrEmpty(ver.ConfigPath) && Directory.Exists(ver.ConfigPath))
                    {
                        try
                        {
                            var scanned = await _addonService.ScanAddonsAsync(ver.ConfigPath, ver.Version);
                            _allAddons.AddRange(scanned);
                        }
                        catch (Exception ex)
                        {
                            ShowError($"Scanning failed for {ver.DisplayName}", ex.Message);
                        }
                    }
                }

                // Group identical addons (same name, version, and type)
                _allAddons = _allAddons
                    .GroupBy(a => new { a.Name, a.Version, a.Type })
                    .Select(g =>
                    {
                        var main = g.First();
                        main.BlenderVersions = g.SelectMany(x => x.BlenderVersions).Distinct().ToList();
                        main.InstallationPaths = g.SelectMany(x => x.InstallationPaths).Distinct().ToList();
                        main.BlenderVersion = string.Join(", ", main.BlenderVersions);
                        return main;
                    })
                    .ToList();

                // Populate Repository Filter SubMenu
                if (RepositorySubMenu != null)
                {
                    RepositorySubMenu.Items.Clear();

                    var allRepoItem = new RadioMenuFlyoutItem { Text = "All Repositories", Tag = "All", GroupName = "RepositoryGroup", IsChecked = _selectedRepo == "All" };
                    allRepoItem.Click += RepositoryMenuItem_Click;
                    RepositorySubMenu.Items.Add(allRepoItem);

                    var distinctRepos = _allAddons
                        .Where(a => a.Type == "Extension" && !string.IsNullOrEmpty(a.Repository))
                        .Select(a => a.Repository)
                        .Distinct()
                        .OrderBy(r => r);

                    foreach (var repo in distinctRepos)
                    {
                        var repoItem = new RadioMenuFlyoutItem { Text = repo, Tag = repo, GroupName = "RepositoryGroup", IsChecked = _selectedRepo == repo };
                        repoItem.Click += RepositoryMenuItem_Click;
                        RepositorySubMenu.Items.Add(repoItem);
                    }
                }

                // Populate Category Filter SubMenu
                if (CategorySubMenu != null)
                {
                    CategorySubMenu.Items.Clear();

                    var allCatItem = new RadioMenuFlyoutItem { Text = "All Categories", Tag = "", GroupName = "CategoryGroup", IsChecked = _selectedCategory == "" };
                    allCatItem.Click += CategoryMenuItem_Click;
                    CategorySubMenu.Items.Add(allCatItem);

                    string[] categories = { "Mesh", "Camera", "Game Engine", "Import-Export", "Object", "Pipeline", "3D View", "Animation", "Compositing", "Geometry Nodes", "Lighting", "Modeling", "Paint", "Render", "Add Curve", "Bake", "Development", "Grease Pencil", "Material", "Node", "Physics", "Rigging" };

                    foreach (var cat in categories.OrderBy(c => c))
                    {
                        var catItem = new RadioMenuFlyoutItem { Text = cat, Tag = cat, GroupName = "CategoryGroup", IsChecked = _selectedCategory == cat };
                        catItem.Click += CategoryMenuItem_Click;
                        CategorySubMenu.Items.Add(catItem);
                    }
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                ShowError("Data Refresh Failed", ex.Message);
            }
        }

        private void FilterCriteriaChanged(object sender, object e)
        {
            ApplyFilters();
        }

        private void VersionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _selectedVersion = item.Tag?.ToString() ?? "All";
                ApplyFilters();
            }
        }

        private void TypeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _selectedType = item.Text;
                ApplyFilters();
            }
        }

        private void CategoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _selectedCategory = item.Tag?.ToString() ?? "";
                ApplyFilters();
            }
        }

        private void RepositoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                _selectedRepo = item.Tag?.ToString() ?? "All";
                ApplyFilters();
            }
        }

        private IEnumerable<AddonItem> FilterList(IEnumerable<AddonItem> list, string searchText, string selectedVersion, string selectedType, string selectedRepo, string selectedCategory)
        {
            var query = list;

            // 1. Text Search Filter
            if (!string.IsNullOrEmpty(searchText))
            {
                if (searchText.StartsWith("auther:", StringComparison.OrdinalIgnoreCase))
                {
                    var authorSearch = searchText.Substring("auther:".Length).Trim();
                    query = query.Where(a => !string.IsNullOrEmpty(a.Author) && a.Author.Contains(authorSearch, StringComparison.OrdinalIgnoreCase));
                }
                else if (searchText.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
                {
                    var authorSearch = searchText.Substring("author:".Length).Trim();
                    query = query.Where(a => !string.IsNullOrEmpty(a.Author) && a.Author.Contains(authorSearch, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    query = query.Where(a => a.Name.ToLowerInvariant().Contains(searchText) ||
                                             a.Author.ToLowerInvariant().Contains(searchText) ||
                                             a.Category.ToLowerInvariant().Contains(searchText) ||
                                             a.Description.ToLowerInvariant().Contains(searchText));
                }
            }

            // 2. Version Filter
            if (selectedVersion != "All")
            {
                var targetVer = SafeParseAndNormalizeVersion(selectedVersion);
                if (targetVer != null)
                {
                    query = query.Where(a =>
                        (!a.IsOnline && a.BlenderVersions.Contains(selectedVersion)) ||
                        (a.IsOnline &&
                         (string.IsNullOrEmpty(a.BlenderVersionMin) || SafeParseAndNormalizeVersion(a.BlenderVersionMin) is var minVer && (minVer == null || targetVer >= minVer)) &&
                         (string.IsNullOrEmpty(a.BlenderVersionMax) || SafeParseAndNormalizeVersion(a.BlenderVersionMax) is var maxVer && (maxVer == null || targetVer <= maxVer)))
                    );
                }
                else
                {
                    query = query.Where(a => a.BlenderVersions.Contains(selectedVersion) || a.BlenderVersion == selectedVersion);
                }
            }

            // 3. Type Filter
            if (selectedType != "All Types")
            {
                if (selectedType == "Addon")
                {
                    query = query.Where(a => a.Type == "Addon");
                }
                else if (selectedType == "Extension")
                {
                    query = query.Where(a => a.Type == "Extension" && a.ExtensionType == "add-on");
                }
                else if (selectedType == "Theme")
                {
                    query = query.Where(a => a.Type == "Extension" && a.ExtensionType == "theme");
                }
            }

            // 4. Repository Filter
            if (selectedRepo != "All" && selectedType != "Addon")
            {
                query = query.Where(a => a.Type == "Extension" && a.Repository == selectedRepo);
            }

            // 5. Category Filter
            if (!string.IsNullOrEmpty(selectedCategory) && selectedType != "Addon")
            {
                query = query.Where(a => !string.IsNullOrEmpty(a.Category) && a.Category.Contains(selectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            return query;
        }

        private void UpdateGroupedAddons(List<AddonItem> installed, List<AddonItem> market)
        {
            var installedGroup = GroupedAddons.FirstOrDefault(g => g.Key == "Installed");
            var marketGroup = GroupedAddons.FirstOrDefault(g => g.Key == "Blender Extension Market");

            if (installed.Any())
            {
                if (installedGroup == null)
                {
                    installedGroup = new AddonGroup("Installed", installed);
                    GroupedAddons.Insert(0, installedGroup);
                }
                else
                {
                    UpdateGroupItems(installedGroup, installed);
                }
            }
            else if (installedGroup != null)
            {
                GroupedAddons.Remove(installedGroup);
            }

            if (market.Any())
            {
                if (marketGroup == null)
                {
                    marketGroup = new AddonGroup("Blender Extension Market", market);
                    GroupedAddons.Add(marketGroup);
                }
                else
                {
                    UpdateGroupItems(marketGroup, market);
                }
            }
            else if (marketGroup != null)
            {
                GroupedAddons.Remove(marketGroup);
            }
        }

        private void UpdateGroupItems(AddonGroup group, List<AddonItem> newItems)
        {
            for (int i = group.Count - 1; i >= 0; i--)
            {
                var item = group[i];
                if (!newItems.Any(x => x.Name == item.Name && x.Version == item.Version && x.Type == item.Type))
                {
                    group.RemoveAt(i);
                }
            }

            for (int i = 0; i < newItems.Count; i++)
            {
                var target = newItems[i];
                int idx = -1;
                for (int j = i; j < group.Count; j++)
                {
                    if (group[j].Name == target.Name && group[j].Version == target.Version && group[j].Type == target.Type)
                    {
                        idx = j;
                        break;
                    }
                }

                if (idx == -1)
                {
                    group.Insert(i, target);
                }
                else if (idx != i)
                {
                    var move = group[idx];
                    group.RemoveAt(idx);
                    group.Insert(i, move);
                }
            }
        }

        private GridView ActiveAddonList => AddonsGridView;

        private void SetListViewVisibility(Visibility visibility)
        {
            if (AddonsGridView != null)
            {
                AddonsGridView.Visibility = visibility;
            }
        }

        private void ApplyFilters()
        {
            // Guard against null controls during XAML inflation/initialization
            if (AddonsGridView == null || SearchBox == null ||
                EmptyStatePanel == null || DetailsGrid == null)
            {
                return;
            }

            // Save selected item path to restore it after filtering
            var selectedPath = (ActiveAddonList.SelectedItem as AddonItem)?.Path;

            var searchText = SearchBox.Text?.Trim().ToLowerInvariant();
            var selectedVersion = _selectedVersion;
            var selectedType = _selectedType;
            var selectedRepo = _selectedRepo;
            var selectedCategory = _selectedCategory;

            // Dynamically enable/disable repository and category filters based on type
            if (selectedType == "Addon")
            {
                if (RepositorySubMenu != null) RepositorySubMenu.IsEnabled = false;
                if (CategorySubMenu != null) CategorySubMenu.IsEnabled = false;
            }
            else
            {
                if (RepositorySubMenu != null) RepositorySubMenu.IsEnabled = true;
                if (CategorySubMenu != null) CategorySubMenu.IsEnabled = true;
            }

            var installedListQuery = FilterList(_allAddons, searchText ?? "", selectedVersion, selectedType, selectedRepo, selectedCategory);
            var marketListQuery = FilterList(_onlineAddons, searchText ?? "", selectedVersion, selectedType, selectedRepo, selectedCategory);

            installedListQuery = installedListQuery.OrderBy(a => a.Name);
            marketListQuery = marketListQuery.OrderBy(a => a.Name);

            var installedList = installedListQuery.ToList();
            var installedNames = new HashSet<string>(installedList.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            var marketList = marketListQuery.Where(online => !installedNames.Contains(online.Name)).ToList();

            UpdateGroupedAddons(installedList, marketList);

            int totalCount = installedList.Count + marketList.Count;
            if (ItemCountTextBlock != null)
            {
                ItemCountTextBlock.Text = totalCount == 1 ? "1 item" : $"{totalCount} items";
            }

            bool hasItems = GroupedAddons.Any(g => g.Any());

            // Toggle empty state and search results
            if (_isLoading)
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                if (NoSearchResultsPanel != null) NoSearchResultsPanel.Visibility = Visibility.Collapsed;
                SetListViewVisibility(Visibility.Collapsed);
            }
            else if (!hasItems)
            {
                SetListViewVisibility(Visibility.Collapsed);
                bool isFiltered = !string.IsNullOrEmpty(searchText) || selectedVersion != "All" || selectedType != "All Types" || selectedRepo != "All" || !string.IsNullOrEmpty(selectedCategory);
                if (isFiltered)
                {
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                    if (NoSearchResultsPanel != null) NoSearchResultsPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    if (EmptyStateIcon != null) EmptyStateIcon.Glyph = "\uE7FC";
                    if (EmptyStateTitle != null) EmptyStateTitle.Text = "No addons installed";
                    if (EmptyStateDescription != null) EmptyStateDescription.Text = "You don't have any addons or extensions installed for your Blender versions yet.";

                    EmptyStatePanel.Visibility = Visibility.Visible;
                    if (NoSearchResultsPanel != null) NoSearchResultsPanel.Visibility = Visibility.Collapsed;
                }

                if (DetailsPanelOuterGrid != null) DetailsPanelOuterGrid.Visibility = Visibility.Collapsed;
                if (DetailsSeparator != null) DetailsSeparator.Visibility = Visibility.Collapsed;
                if (LeftBodyGrid != null) Grid.SetColumnSpan(LeftBodyGrid, 3);
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                if (NoSearchResultsPanel != null) NoSearchResultsPanel.Visibility = Visibility.Collapsed;
                SetListViewVisibility(Visibility.Visible);

                if (DetailsPanelOuterGrid != null) DetailsPanelOuterGrid.Visibility = Visibility.Visible;
                if (DetailsSeparator != null) DetailsSeparator.Visibility = Visibility.Visible;
                if (LeftBodyGrid != null) Grid.SetColumnSpan(LeftBodyGrid, 1);
            }

            // Restore selection or pick first item
            if (!string.IsNullOrEmpty(selectedPath))
            {
                var restoreItem = GroupedAddons.SelectMany(g => g).FirstOrDefault(a => a.Path == selectedPath);
                if (restoreItem != null)
                {
                    ActiveAddonList.SelectedItem = restoreItem;
                    return;
                }
            }

            var firstGroup = GroupedAddons.FirstOrDefault(g => g.Any());
            if (firstGroup != null)
            {
                ActiveAddonList.SelectedItem = firstGroup.First();
            }
            else
            {
                ActiveAddonList.SelectedItem = null;
            }
        }

        private void AddonsListView_SizeChanged(object sender, SizeChangedEventArgs e)
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

        private void AddonsListView_SelectionChanged(object sender, SelectionChangedEventArgs? e)
        {
            var activeList = sender as GridView;
            if (activeList == null) return;

            if (activeList.SelectedItem is AddonItem item)
            {
                DetailsGrid.Visibility = Visibility.Visible;

                DetailName.Text = item.Name;
                DetailTagline.Text = !string.IsNullOrEmpty(item.Description) ? item.Description : "No description provided.";
                DetailAuthorText.Text = item.Author;

                // Check if online
                bool isOnline = !string.IsNullOrEmpty(item.Path) && item.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                if (LocalActionsGrid != null) LocalActionsGrid.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                if (OnlineActionsGrid != null) OnlineActionsGrid.Visibility = isOnline ? Visibility.Visible : Visibility.Collapsed;

                // Set Badges
                // Set Repository Card Text
                if (isOnline)
                {
                    DetailRepoCardText.Text = "extensions.blender.org";
                }
                else if (item.Type == "Extension")
                {
                    DetailRepoCardText.Text = item.Repository;
                }
                else
                {
                    DetailRepoCardText.Text = "None (Legacy)";
                }

                if (DetailRepoBadge != null)
                {
                    DetailRepoBadge.Visibility = (isOnline || item.Type == "Extension") ? Visibility.Visible : Visibility.Collapsed;
                }

                // Populate Metadata Rows
                DetailHistoryCurrent.Text = !string.IsNullOrEmpty(item.Version) ? $"v{item.Version}" : "Unknown";

                if (DetailSizeText != null && DetailSizeBadgeBorder != null)
                {
                    if (item.ArchiveSize > 0)
                    {
                        DetailSizeBadgeBorder.Visibility = Visibility.Visible;
                        DetailSizeText.Text = FormatBytes(item.ArchiveSize);
                    }
                    else
                    {
                        DetailSizeBadgeBorder.Visibility = Visibility.Collapsed;
                    }
                }

                // Show/hide compatibility card and badges
                bool showCompat = !string.IsNullOrEmpty(item.BlenderVersionMin) || !string.IsNullOrEmpty(item.BlenderVersionMax);
                if (DetailSizeLabel != null)
                {
                    DetailSizeLabel.Visibility = (showCompat || item.ArchiveSize > 0) ? Visibility.Visible : Visibility.Collapsed;
                }

                if (DetailCompatibilityHeaderBadge != null)
                {
                    DetailCompatibilityHeaderBadge.Visibility = showCompat ? Visibility.Visible : Visibility.Collapsed;
                }

                if (CompatibilityCard != null)
                {
                    if (showCompat)
                    {
                        bool isCompatible = false;
                        var compatibleVersions = new List<string>();

                        if (!string.IsNullOrEmpty(item.BlenderVersionMin) && System.Version.TryParse(item.BlenderVersionMin, out var minVer))
                        {
                            foreach (var ver in _installedVersions)
                            {
                                if (System.Version.TryParse(ver.Version, out var installedVer))
                                {
                                    bool verCompat = true;
                                    if (installedVer < minVer) verCompat = false;

                                    if (!string.IsNullOrEmpty(item.BlenderVersionMax) && System.Version.TryParse(item.BlenderVersionMax, out var maxVer))
                                    {
                                        if (installedVer > maxVer) verCompat = false;
                                    }

                                    if (verCompat)
                                    {
                                        isCompatible = true;
                                        compatibleVersions.Add(ver.Version);
                                    }
                                }
                            }
                        }
                        else
                        {
                            isCompatible = true; // No min/max, assume compatible
                        }

                        if (isCompatible)
                        {
                            if (DetailCompatibilityHeaderBadge != null)
                            {
                                DetailCompatibilityHeaderBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 21, 128, 61)); // Green
                                DetailCompatibilityHeaderBadgeText.Text = "Compatible";
                            }
                            CompatibilityCard.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            if (DetailCompatibilityHeaderBadge != null)
                            {
                                DetailCompatibilityHeaderBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 28, 28)); // Red
                                DetailCompatibilityHeaderBadgeText.Text = "Incompatible";
                            }
                            CompatibilityCard.Visibility = Visibility.Visible;

                            if (DetailCompatibilityBadge != null)
                            {
                                DetailCompatibilityBadge.Visibility = Visibility.Collapsed;
                            }
                            if (DetailCompatibilityIcon != null)
                            {
                                DetailCompatibilityIcon.Glyph = "\uE711"; // Error/X
                                DetailCompatibilityIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 28, 28));
                            }
                        }

                        string leftSide = string.IsNullOrEmpty(item.BlenderVersionMin) ? "Any" : $"v{item.BlenderVersionMin}";
                        if (compatibleVersions.Count > 0)
                        {
                            string rightSide = string.Join(", ", compatibleVersions.Distinct());
                            DetailCompatibilityText.Text = $"Requires: {leftSide}  |  Compatible with: {rightSide}";
                        }
                        else
                        {
                            DetailCompatibilityText.Text = $"Requires: {leftSide}";
                        }
                    }
                    else
                    {
                        CompatibilityCard.Visibility = Visibility.Collapsed;
                    }
                }

                // Permissions Card
                if (PropPermissionsPanel != null)
                {
                    bool isExtension = item.Type == "Extension";
                    bool hasPerms = !string.IsNullOrEmpty(item.Permissions) && item.Permissions != "[]";
                    PropPermissionsPanel.Visibility = hasPerms ? Visibility.Visible : Visibility.Collapsed;

                    if (PropPermissionsPanel.Visibility == Visibility.Visible)
                    {
                        // Reset visibility of badges
                        PermNoPermissionsBadge.Visibility = Visibility.Collapsed;
                        PermNetworkBadge.Visibility = Visibility.Collapsed;
                        PermFilesBadge.Visibility = Visibility.Collapsed;
                        PermClipboardBadge.Visibility = Visibility.Collapsed;

                        if (string.IsNullOrEmpty(item.Permissions) || item.Permissions == "[]")
                        {
                            PermNoPermissionsBadge.Visibility = Visibility.Visible;
                            PropPermissions.Text = "No special permissions are required. This extension runs in a secure sandbox.";
                        }
                        else
                        {
                            string permsLower = item.Permissions.ToLowerInvariant();

                            if (permsLower.Contains("network"))
                            {
                                PermNetworkBadge.Visibility = Visibility.Visible;
                            }
                            if (permsLower.Contains("files") || permsLower.Contains("disk") || permsLower.Contains("storage"))
                            {
                                PermFilesBadge.Visibility = Visibility.Visible;
                            }
                            if (permsLower.Contains("clipboard"))
                            {
                                PermClipboardBadge.Visibility = Visibility.Visible;
                            }

                            PropPermissions.Text = item.Permissions;
                        }
                    }
                }

                // Installation Path
                if (isOnline)
                {
                    if (PropPathPanel != null) PropPathPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (PropPathPanel != null) PropPathPanel.Visibility = Visibility.Visible;
                    if (PropPathsContainer != null)
                    {
                        PropPathsContainer.Children.Clear();

                        for (int i = 0; i < item.BlenderVersions.Count; i++)
                        {
                            var ver = item.BlenderVersions[i];
                            var p = item.InstallationPaths.Count > i ? item.InstallationPaths[i] : item.Path;

                            var pathBorder = new Border
                            {
                                Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                                BorderThickness = new Thickness(1),
                                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(12),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };

                            var textStack = new StackPanel { Spacing = 4 };

                            var versionLabel = new TextBlock
                            {
                                Text = $"Blender {ver}",
                                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Foreground = Application.Current.Resources["TextFillColorPrimaryBrush"] as Microsoft.UI.Xaml.Media.Brush
                            };
                            textStack.Children.Add(versionLabel);

                            var pathText = new TextBlock
                            {
                                Text = p,
                                TextWrapping = TextWrapping.Wrap,
                                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
                                IsTextSelectionEnabled = true,
                                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
                            };
                            textStack.Children.Add(pathText);

                            pathBorder.Child = textStack;
                            PropPathsContainer.Children.Add(pathBorder);
                        }
                    }
                }

                // Website Docs button enabled state
                bool hasWebsite = !string.IsNullOrEmpty(item.WebsiteUrl);

                SharedWebsiteButton.Visibility = Visibility.Visible;
                SharedWebsiteButton.IsEnabled = hasWebsite;

                if (WebsiteButton != null)
                {
                    WebsiteButton.Visibility = Visibility.Visible;
                    WebsiteButton.IsEnabled = hasWebsite;
                }

                // Check if update is available and show update button!
                bool updateAvailable = false;
                if (!isOnline && item.Type == "Extension" && _onlineAddons.Count > 0)
                {
                    var onlineItem = _onlineAddons.FirstOrDefault(a =>
                        a.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(a.FolderName) && a.FolderName.Equals(item.FolderName, StringComparison.OrdinalIgnoreCase)));
                    if (onlineItem != null && System.Version.TryParse(item.Version, out var localVer) && System.Version.TryParse(onlineItem.Version, out var onlineVer))
                    {
                        updateAvailable = onlineVer > localVer;
                    }
                }
                if (UpdateAddonButton != null)
                {
                    UpdateAddonButton.Visibility = updateAvailable ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                DetailsGrid.Visibility = Visibility.Collapsed;
            }
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

        private static System.Version? SafeParseAndNormalizeVersion(string versionStr)
        {
            if (System.Version.TryParse(versionStr, out var ver))
            {
                int major = ver.Major;
                int minor = ver.Minor >= 0 ? ver.Minor : 0;
                int build = ver.Build >= 0 ? ver.Build : 0;
                int revision = ver.Revision >= 0 ? ver.Revision : 0;
                return new System.Version(major, minor, build, revision);
            }
            return null;
        }


        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null)
            {
                SearchBox.Text = string.Empty;
            }
        }
    }

    public class AddonGroup : ObservableCollection<AddonItem>
    {
        public string Key { get; set; } = string.Empty;

        public AddonGroup(string key, IEnumerable<AddonItem> items) : base(items)
        {
            Key = key;
        }
    }

    /// <summary>
    /// Converts addon type ("Addon" vs "Extension") to corresponding badge background brush.
    /// </summary>
    public class AddonTypeToBrushConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? typeStr = value as string;
            if (typeStr == "Extension")
            {
                // High-contrast emerald green brush for extensions
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 21, 128, 61));
            }
            // Sleek Steel Blue brush for addons
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 71, 85, 105));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class AddonGroupHeaderMarginConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string key && AddonsPage.Instance != null && AddonsPage.Instance.GroupedAddons.Count > 0)
            {
                if (AddonsPage.Instance.GroupedAddons[0].Key == key)
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
