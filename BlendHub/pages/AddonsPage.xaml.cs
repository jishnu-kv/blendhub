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
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Pages
{
    public sealed partial class AddonsPage : Page
    {
        private readonly BlenderSettingsService _blenderService = new();
        private readonly AddonService _addonService = new();
        
        private List<AddonItem> _allAddons = new();
        private ObservableCollection<AddonItem> _filteredAddons = new();
        private List<BlenderVersionInfo> _installedVersions = new();

        private List<AddonItem> _onlineAddons = new();
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        private bool _isLoading = true;

        public AddonsPage()
        {
            this.InitializeComponent();

            
            // Assign ListView's ItemSource
            AddonsListView.ItemsSource = _filteredAddons;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            _isLoading = true;

            // Hide details pane initially
            DetailsGrid.Visibility = Visibility.Collapsed;
            NoSelectionPanel.Visibility = Visibility.Visible;
            if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Collapsed;

            // Reset segmented control back to All
            if (TabSegmented != null)
            {
                TabSegmented.SelectionChanged -= TabSegmented_SelectionChanged;
                TabSegmented.SelectedIndex = 0;
                TabSegmented.SelectionChanged += TabSegmented_SelectionChanged;
            }

            if (InstallAddonButton != null) InstallAddonButton.Visibility = Visibility.Visible;

            await RefreshDataAsync();

            _isLoading = false;
            ApplyFilters();

            // Background load online extensions so tabs are populated!
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
                _installedVersions = _blenderService.GetInstalledVersions();

                // Populate Version Filter SubItem options
                if (VersionFilterSubItem != null)
                {
                    VersionFilterSubItem.Items.Clear();
                    var allVerItem = new RadioMenuFlyoutItem { Text = "All Versions", GroupName = "VersionGroup", IsChecked = true };
                    allVerItem.Click += FilterCriteriaChanged;
                    VersionFilterSubItem.Items.Add(allVerItem);

                    foreach (var ver in _installedVersions)
                    {
                        var verItem = new RadioMenuFlyoutItem { Text = ver.DisplayName, Tag = ver.Version, GroupName = "VersionGroup" };
                        verItem.Click += FilterCriteriaChanged;
                        VersionFilterSubItem.Items.Add(verItem);
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

                // Populate Repository Filter SubItem options
                if (RepositoryFilterSubItem != null)
                {
                    RepositoryFilterSubItem.Items.Clear();
                    var allRepoItem = new RadioMenuFlyoutItem { Text = "All Repositories", GroupName = "RepoGroup", IsChecked = true };
                    allRepoItem.Click += FilterCriteriaChanged;
                    RepositoryFilterSubItem.Items.Add(allRepoItem);

                    var distinctRepos = _allAddons
                        .Where(a => a.Type == "Extension" && !string.IsNullOrEmpty(a.Repository))
                        .Select(a => a.Repository)
                        .Distinct()
                        .OrderBy(r => r);

                    foreach (var repo in distinctRepos)
                    {
                        var repoItem = new RadioMenuFlyoutItem { Text = repo, Tag = repo, GroupName = "RepoGroup" };
                        repoItem.Click += FilterCriteriaChanged;
                        RepositoryFilterSubItem.Items.Add(repoItem);
                    }
                }

                // Populate Category Filter SubItem options
                if (CategoryFilterSubItem != null)
                {
                    CategoryFilterSubItem.Items.Clear();
                    string[] categories = { "All Categories", "Mesh", "Camera", "Game Engine", "Import-Export", "Object", "Pipeline", "3D View", "Animation", "Compositing", "Geometry Nodes", "Lighting", "Modeling", "Paint", "Render", "Add Curve", "Bake", "Development", "Grease Pencil", "Material", "Node", "Physics", "Rigging" };
                    
                    foreach (var cat in categories)
                    {
                        var item = new RadioMenuFlyoutItem { Text = cat, GroupName = "CategoryGroup", Tag = cat == "All Categories" ? "" : cat, IsChecked = cat == "All Categories" };
                        item.Click += FilterCriteriaChanged;
                        CategoryFilterSubItem.Items.Add(item);
                    }
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                ShowError("Data Refresh Failed", ex.Message);
            }
        }

        private void SortCriteriaChanged(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void FilterCriteriaChanged(object sender, object e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            // Guard against null controls during XAML inflation/initialization
            if (AddonsListView == null || SearchBox == null || 
                EmptyStatePanel == null || NoSelectionPanel == null || DetailsGrid == null)
            {
                return;
            }

            var selectedItem = TabSegmented?.SelectedItem as CommunityToolkit.WinUI.Controls.SegmentedItem;
            var tag = selectedItem?.Tag?.ToString() ?? "Installed";

            // Save selected item path to restore it after filtering
            var selectedPath = (AddonsListView.SelectedItem as AddonItem)?.Path;

            IEnumerable<AddonItem> query;
            if (tag == "Market")
            {
                query = _onlineAddons;
            }
            else if (tag == "Updates")
            {
                query = _allAddons.Where(localItem =>
                {
                    if (localItem.Type != "Extension") return false;

                    var onlineItem = _onlineAddons.FirstOrDefault(a => 
                        a.Name.Equals(localItem.Name, StringComparison.OrdinalIgnoreCase) || 
                        (!string.IsNullOrEmpty(a.FolderName) && a.FolderName.Equals(localItem.FolderName, StringComparison.OrdinalIgnoreCase)));

                    if (onlineItem != null)
                    {
                        if (System.Version.TryParse(localItem.Version, out var localVer) && System.Version.TryParse(onlineItem.Version, out var onlineVer))
                        {
                            return onlineVer > localVer;
                        }
                    }
                    return false;
                });
            }
            else
            {
                query = _allAddons;
            }

            // 1. Text Search Filter
            var searchText = SearchBox.Text?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(searchText))
            {
                query = query.Where(a => a.Name.ToLowerInvariant().Contains(searchText) ||
                                         a.Author.ToLowerInvariant().Contains(searchText) ||
                                         a.Category.ToLowerInvariant().Contains(searchText) ||
                                         a.Description.ToLowerInvariant().Contains(searchText));
            }

            // 2. Version Filter
            if (VersionFilterSubItem != null)
            {
                var selectedVersionItem = VersionFilterSubItem.Items
                    .OfType<RadioMenuFlyoutItem>()
                    .FirstOrDefault(i => i.IsChecked);
                if (selectedVersionItem != null && selectedVersionItem.Text != "All Versions")
                {
                    var verTag = selectedVersionItem.Tag?.ToString();
                    if (!string.IsNullOrEmpty(verTag))
                    {
                        query = query.Where(a => a.BlenderVersion == verTag);
                    }
                }
            }

            // 3. Type Filter
            if (TypeFilterSubItem != null)
            {
                var selectedTypeItem = TypeFilterSubItem.Items
                    .OfType<RadioMenuFlyoutItem>()
                    .FirstOrDefault(i => i.IsChecked);
                if (selectedTypeItem != null && selectedTypeItem.Text != "All Types")
                {
                    var typeTag = selectedTypeItem.Text == "Extensions" ? "Extension" : "Legacy Addon";
                    query = query.Where(a => a.Type == typeTag);

                    if (RepositoryFilterSubItem != null)
                    {
                        RepositoryFilterSubItem.Visibility = typeTag == "Legacy Addon" ? Visibility.Collapsed : Visibility.Visible;
                    }
                    if (CategoryFilterSubItem != null)
                    {
                        CategoryFilterSubItem.Visibility = typeTag == "Legacy Addon" ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
                else
                {
                    if (RepositoryFilterSubItem != null) RepositoryFilterSubItem.Visibility = Visibility.Visible;
                    if (CategoryFilterSubItem != null) CategoryFilterSubItem.Visibility = Visibility.Visible;
                }
            }

            // 4. Repository Filter
            if (RepositoryFilterSubItem != null && RepositoryFilterSubItem.Visibility == Visibility.Visible)
            {
                var selectedRepoItem = RepositoryFilterSubItem.Items
                    .OfType<RadioMenuFlyoutItem>()
                    .FirstOrDefault(i => i.IsChecked);
                if (selectedRepoItem != null && selectedRepoItem.Text != "All Repositories")
                {
                    var repoTag = selectedRepoItem.Tag?.ToString();
                    if (!string.IsNullOrEmpty(repoTag))
                    {
                        query = query.Where(a => a.Type == "Extension" && a.Repository == repoTag);
                    }
                }
            }

            // 5. Category Filter
            if (CategoryFilterSubItem != null)
            {
                var selectedCatItem = CategoryFilterSubItem.Items
                    .OfType<RadioMenuFlyoutItem>()
                    .FirstOrDefault(i => i.IsChecked);
                if (selectedCatItem != null && selectedCatItem.Text != "All Categories")
                {
                    var catTag = selectedCatItem.Text;
                    query = query.Where(a => !string.IsNullOrEmpty(a.Category) && a.Category.Contains(catTag, StringComparison.OrdinalIgnoreCase));
                }
            }

            // 6. Sorting
            if (SortNameDesc != null && SortNameDesc.IsChecked)
            {
                query = query.OrderByDescending(a => a.Name);
            }
            else
            {
                query = query.OrderBy(a => a.Name);
            }

            // Update collection incrementally to avoid scroll resets, flicker, and selection losses
            var results = query.ToList();
            UpdateFilteredCollection(results);

            // Toggle empty state
            if (_isLoading)
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                AddonsListView.Visibility = Visibility.Collapsed;
            }
            else if (_filteredAddons.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                AddonsListView.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                AddonsListView.Visibility = Visibility.Visible;
            }

            // Restore selection or pick first item
            if (!string.IsNullOrEmpty(selectedPath))
            {
                var restoreItem = _filteredAddons.FirstOrDefault(a => a.Path == selectedPath);
                if (restoreItem != null)
                {
                    int index = _filteredAddons.IndexOf(restoreItem);
                    if (index >= 0)
                    {
                        AddonsListView.Select(index);
                    }
                    return;
                }
            }

            if (_filteredAddons.Count > 0)
            {
                AddonsListView.Select(0);
            }
            else
            {
                AddonsListView.SelectAll();
                AddonsListView.InvertSelection();
            }
        }
 
        private void AddonsListView_SelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs e)
        {
            if (sender.SelectedItem is AddonItem item)
            {
                DetailsGrid.Visibility = Visibility.Visible;
                NoSelectionPanel.Visibility = Visibility.Collapsed;

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

                if (DetailSizeText != null)
                {
                    if (item.ArchiveSize > 0)
                    {
                        DetailSizeLabel.Visibility = Visibility.Visible;
                        DetailSizeText.Visibility = Visibility.Visible;
                        DetailSizeText.Text = FormatBytes(item.ArchiveSize);
                    }
                    else
                    {
                        DetailSizeLabel.Visibility = Visibility.Collapsed;
                        DetailSizeText.Visibility = Visibility.Collapsed;
                    }
                }

                // Show/hide compatibility card if both versions are empty
                bool showCompat = !string.IsNullOrEmpty(item.BlenderVersionMin) || !string.IsNullOrEmpty(item.BlenderVersionMax);
                if (CompatibilityCard != null)
                {
                    CompatibilityCard.Visibility = showCompat ? Visibility.Visible : Visibility.Collapsed;
                    if (showCompat)
                    {
                        bool isCompatible = false;
                        var compatibleVersions = new List<string>();
                        
                        if (!string.IsNullOrEmpty(item.BlenderVersionMin) && System.Version.TryParse(item.BlenderVersionMin, out var minVer))
                        {
                            foreach(var ver in _installedVersions)
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
                            DetailCompatibilityBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 21, 128, 61)); // Green
                            DetailCompatibilityBadgeText.Text = "Compatible";
                            DetailCompatibilityIcon.Glyph = "\uE73E"; // Checkmark
                            DetailCompatibilityIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 21, 128, 61));
                        }
                        else
                        {
                            DetailCompatibilityBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 28, 28)); // Red
                            DetailCompatibilityBadgeText.Text = "Incompatible";
                            DetailCompatibilityIcon.Glyph = "\uE711"; // Error/X
                            DetailCompatibilityIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 28, 28));
                        }

                        string leftSide = string.IsNullOrEmpty(item.BlenderVersionMin) ? "Any" : $"v{item.BlenderVersionMin}";
                        string rightSide = compatibleVersions.Count > 0 ? string.Join(", ", compatibleVersions.Distinct()) : "None";
                        DetailCompatibilityText.Text = $"Requires: {leftSide}  |  Compatible with: {rightSide}";
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
                                Background = Application.Current.Resources["ControlFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                                BorderThickness = new Thickness(1),
                                BorderBrush = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
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
                NoSelectionPanel.Visibility = Visibility.Visible;
            }
        }

        // --- Details Panel Click Handlers ---

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddonsListView.SelectedItem is not AddonItem item) return;

            var folderPath = item.InstallationPaths.FirstOrDefault() ?? item.Path;
            try
            {
                if (Directory.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{folderPath}\"");
                }
                else if (File.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{folderPath}\"");
                }
            }
            catch (Exception ex)
            {
                ShowError("Explorer failed to open", ex.Message);
            }
        }

        private void WebsiteButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddonsListView.SelectedItem is not AddonItem item || string.IsNullOrEmpty(item.WebsiteUrl)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.WebsiteUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError("Failed to open link", ex.Message);
            }
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddonsListView.SelectedItem is not AddonItem item) return;

            string selectedVersion = item.BlenderVersion;
            string selectedPath = item.Path;

            if (item.BlenderVersions.Count > 1)
            {
                var versionCombo = new ComboBox
                {
                    Header = "Select Blender Version to Uninstall From",
                    ItemsSource = item.BlenderVersions,
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var dialogPanel = new StackPanel { Spacing = 4, Width = 340 };
                dialogPanel.Children.Add(versionCombo);

                var choiceDialog = new ContentDialog
                {
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    Title = "Uninstall Options",
                    Content = dialogPanel,
                    PrimaryButtonText = "Select",
                    SecondaryButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                if (await choiceDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var idx = versionCombo.SelectedIndex;
                if (idx >= 0 && idx < item.BlenderVersions.Count)
                {
                    selectedVersion = item.BlenderVersions[idx];
                    selectedPath = item.InstallationPaths[idx];
                }
            }

            var dialog = new ContentDialog
            {
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Uninstall Addon?",
                Content = $"Are you sure you want to completely uninstall \"{item.Name}\" from Blender {selectedVersion}?\n\nThis will permanently delete the addon directory/files:\n{selectedPath}",
                PrimaryButtonText = "Uninstall",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    var uninstallItem = new AddonItem
                    {
                        Name = item.Name,
                        FolderName = item.FolderName,
                        Version = item.Version,
                        Type = item.Type,
                        Repository = item.Repository,
                        BlenderVersion = selectedVersion,
                        Path = selectedPath
                    };

                    await _addonService.UninstallAddonAsync(uninstallItem);
                    ShowSuccess("Uninstalled", $"Successfully uninstalled \"{item.Name}\" from Blender {selectedVersion}.");
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    ShowError("Uninstall failed", ex.Message);
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = TabSegmented?.SelectedItem as CommunityToolkit.WinUI.Controls.SegmentedItem;
            var tag = selectedItem?.Tag?.ToString() ?? "Installed";

            if (tag == "Market")
            {
                _onlineAddons.Clear();
                await LoadOnlineExtensionsAsync();
            }
            else
            {
                await RefreshDataAsync();
            }
        }

        // --- Install Addon Click Handlers ---

        private async void InstallAddonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_installedVersions == null || _installedVersions.Count == 0)
                {
                    ShowWarning("No Blender Versions", "No installed Blender versions were detected. Please set up Blender configurations in Settings first.");
                    return;
                }

                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.Downloads;
                picker.FileTypeFilter.Add(".zip");
                picker.FileTypeFilter.Add(".py");

                var window = App.MainWindow;
                if (window != null)
                {
                    IntPtr hwnd = WindowNative.GetWindowHandle(window);
                    InitializeWithWindow.Initialize(picker, hwnd);
                }

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                // Show Custom Dialog to configure target version and extension repository
                var versionCombo = new ComboBox
                {
                    Header = "Target Blender Version",
                    ItemsSource = _installedVersions,
                    DisplayMemberPath = "DisplayName",
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var repoCombo = new ComboBox
                {
                    Header = "Extension Repository (Only for modern Extensions)",
                    ItemsSource = new List<string> { "user_default", "blender_org" },
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var noteText = new TextBlock
                {
                    Text = "Note: If the selected .zip contains a 'blender_manifest.toml' file, it will be installed as an Extension under the selected repository. Otherwise, it will be installed as a Legacy Addon.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
                };

                var dialogPanel = new StackPanel { Spacing = 4, Width = 340 };
                dialogPanel.Children.Add(versionCombo);
                dialogPanel.Children.Add(repoCombo);
                dialogPanel.Children.Add(noteText);

                var dialog = new ContentDialog
                {
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    Title = "Install Options",
                    Content = dialogPanel,
                    PrimaryButtonText = "Install",
                    SecondaryButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var selectedVersion = versionCombo.SelectedItem as BlenderVersionInfo;
                    if (selectedVersion == null || string.IsNullOrEmpty(selectedVersion.ConfigPath))
                    {
                        ShowError("Install Error", "Selected target Blender version is invalid.");
                        return;
                    }

                    var selectedRepo = repoCombo.SelectedItem as string ?? "user_default";

                    try
                    {
                        await _addonService.InstallAddonAsync(file.Path, selectedVersion.ConfigPath, selectedRepo);
                        ShowSuccess("Installed Successfully", $"\"{file.Name}\" was installed for {selectedVersion.DisplayName}.");
                        await RefreshDataAsync();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Installation Failed", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Install Setup Error", ex.Message);
            }
        }

        // --- InfoBar Helpers ---

        private void ShowSuccess(string title, string message)
        {
            SuccessInfoBar.Title = title;
            SuccessInfoBar.Message = message;
            SuccessInfoBar.IsOpen = true;
            UpdateInfoBarPanelMargin();
        }

        private void ShowWarning(string title, string message)
        {
            WarningInfoBar.Title = title;
            WarningInfoBar.Message = message;
            WarningInfoBar.IsOpen = true;
            UpdateInfoBarPanelMargin();
        }

        private void ShowError(string title, string message)
        {
            ErrorInfoBar.Title = title;
            ErrorInfoBar.Message = message;
            ErrorInfoBar.IsOpen = true;
            UpdateInfoBarPanelMargin();
        }

        private void InfoBar_Closed(Microsoft.UI.Xaml.Controls.InfoBar sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs args)
        {
            UpdateInfoBarPanelMargin();
        }

        private void UpdateInfoBarPanelMargin()
        {
            bool anyOpen = SuccessInfoBar.IsOpen || WarningInfoBar.IsOpen || ErrorInfoBar.IsOpen;
            InfoBarPanel.Margin = new Thickness(0, 0, 0, anyOpen ? 7 : 0);
        }

        private async void TabSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabSegmented == null) return;

            var selectedItem = TabSegmented.SelectedItem as CommunityToolkit.WinUI.Controls.SegmentedItem;
            var tag = selectedItem?.Tag?.ToString() ?? "Installed";

            if (InstallAddonButton != null)
            {
                InstallAddonButton.Visibility = (tag == "Market") ? Visibility.Collapsed : Visibility.Visible;
            }

            if ((tag == "Market" || tag == "Updates") && _onlineAddons.Count == 0)
            {
                await LoadOnlineExtensionsAsync();
            }

            ApplyFilters();
        }

        private async Task LoadOnlineExtensionsAsync()
        {
            if (BackgroundUpdatePanel == null) return;

            // Step 1: Load local cache immediately for instantaneous rendering
            string localJson = string.Empty;
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions.json");
                if (!File.Exists(localPath))
                {
                    localPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "extensions.json");
                }

                if (File.Exists(localPath))
                {
                    localJson = await File.ReadAllTextAsync(localPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddonsPage] Failed to load local extensions cache: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(localJson))
            {
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var response = System.Text.Json.JsonSerializer.Deserialize<OnlineApiResponse>(localJson, options);
                    
                    _onlineAddons.Clear();
                    if (response?.Data != null)
                    {
                        foreach (var ext in response.Data)
                        {
                            _onlineAddons.Add(MapToAddonItem(ext));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AddonsPage] Failed to parse local extensions cache: {ex.Message}");
                }
            }

            // Immediately apply filters so the page renders instantly!
            ApplyFilters();

            // Step 2: Asynchronously sync with the live API in the background
            var selectedItem = TabSegmented?.SelectedItem as CommunityToolkit.WinUI.Controls.SegmentedItem;
            var currentTab = selectedItem?.Tag?.ToString() ?? "Installed";

            if (currentTab == "Market" && _onlineAddons.Count == 0)
            {
                BackgroundUpdatePanel.Visibility = Visibility.Visible;
            }

            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(12);
                string liveJson = await _httpClient.GetStringAsync("https://extensions.blender.org/api/v1/extensions/?format=json");

                if (!string.IsNullOrEmpty(liveJson) && liveJson != localJson)
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var response = System.Text.Json.JsonSerializer.Deserialize<OnlineApiResponse>(liveJson, options);

                    if (response?.Data != null)
                    {
                        _onlineAddons.Clear();
                        foreach (var ext in response.Data)
                        {
                            _onlineAddons.Add(MapToAddonItem(ext));
                        }

                        // Re-apply filters to incrementally merge live updates!
                        ApplyFilters();
                    }
                }
            }
            catch (Exception downloadEx)
            {
                System.Diagnostics.Debug.WriteLine($"[AddonsPage] Live background extensions sync failed: {downloadEx.Message}");
            }
            finally
            {
                BackgroundUpdatePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateFilteredCollection(List<AddonItem> results)
        {
            if (_filteredAddons == null) return;

            // 1. Remove items that are no longer in results
            for (int i = _filteredAddons.Count - 1; i >= 0; i--)
            {
                var item = _filteredAddons[i];
                if (!results.Any(x => x.Name == item.Name && x.Version == item.Version && x.Type == item.Type))
                {
                    _filteredAddons.RemoveAt(i);
                }
            }

            // 2. Add or move items to match results content and order exactly
            for (int i = 0; i < results.Count; i++)
            {
                var targetItem = results[i];
                int existingIdx = -1;

                for (int j = i; j < _filteredAddons.Count; j++)
                {
                    if (_filteredAddons[j].Name == targetItem.Name && _filteredAddons[j].Version == targetItem.Version && _filteredAddons[j].Type == targetItem.Type)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx == -1)
                {
                    _filteredAddons.Insert(i, targetItem);
                }
                else if (existingIdx != i)
                {
                    var itemToMove = _filteredAddons[existingIdx];
                    _filteredAddons.RemoveAt(existingIdx);
                    _filteredAddons.Insert(i, itemToMove);
                }
            }
        }

        private AddonItem MapToAddonItem(OnlineExtData ext)
        {
            var item = new AddonItem
            {
                Name = ext.Name,
                FolderName = ext.Id,
                Version = ext.Version,
                Author = string.IsNullOrEmpty(ext.Maintainer) 
                    ? "Unknown" 
                    : (ext.Maintainer.IndexOf('<') >= 0 
                        ? ext.Maintainer.Substring(0, ext.Maintainer.IndexOf('<')).Trim() 
                        : ext.Maintainer),
                Description = ext.Tagline,
                Category = ext.Tags != null && ext.Tags.Count > 0 ? string.Join(", ", ext.Tags) : "General",
                Type = "Extension",
                Repository = "extensions.blender.org",
                BlenderVersion = ext.BlenderVersionMin, 
                Path = ext.ArchiveUrl, // Use URL as Path to flag as online
                WebsiteUrl = ext.Website,
                BlenderVersionMin = ext.BlenderVersionMin,
                License = ext.License != null ? string.Join(", ", ext.License) : string.Empty,
                ArchiveSize = ext.ArchiveSize
            };

            if (ext.Permissions != null && ext.Permissions.Count > 0)
            {
                var perms = ext.Permissions.Select(kv => $"{char.ToUpper(kv.Key[0]) + kv.Key.Substring(1)}: {kv.Value}");
                item.Permissions = string.Join(", ", perms);
            }

            return item;
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int order = 0;
            while (size >= 1024 && order < suffix.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            return $"{size:0.#} {suffix[order]}";
        }

        private async void UpdateAddonButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddonsListView.SelectedItem is not AddonItem localItem) return;
            
            if (_onlineAddons.Count == 0)
            {
                await LoadOnlineExtensionsAsync();
            }

            var onlineItem = _onlineAddons.FirstOrDefault(a => a.Name.Equals(localItem.Name, StringComparison.OrdinalIgnoreCase) || 
                                                               (!string.IsNullOrEmpty(a.FolderName) && a.FolderName.Equals(localItem.FolderName, StringComparison.OrdinalIgnoreCase)));
            
            if (onlineItem != null)
            {
                string targetVersion = localItem.BlenderVersions.FirstOrDefault() ?? localItem.BlenderVersion;
                
                if (localItem.BlenderVersions.Count > 1)
                {
                    var versionCombo = new ComboBox
                    {
                        Header = "Select Blender Version to Update",
                        ItemsSource = localItem.BlenderVersions,
                        SelectedIndex = 0,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 0, 0, 16)
                    };

                    var dialogPanel = new StackPanel { Spacing = 4, Width = 340 };
                    dialogPanel.Children.Add(versionCombo);

                    var choiceDialog = new ContentDialog
                    {
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        Title = "Update Options",
                        Content = dialogPanel,
                        PrimaryButtonText = "Select",
                        SecondaryButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot
                    };

                    if (await choiceDialog.ShowAsync() != ContentDialogResult.Primary)
                    {
                        return;
                    }

                    var idx = versionCombo.SelectedIndex;
                    if (idx >= 0 && idx < localItem.BlenderVersions.Count)
                    {
                        targetVersion = localItem.BlenderVersions[idx];
                    }
                }

                var dialog = new ContentDialog
                {
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    Title = "Update Available",
                    Content = $"A newer version (v{onlineItem.Version}) is available for {localItem.Name}. Do you want to update from v{localItem.Version} in Blender {targetVersion}?",
                    PrimaryButtonText = "Update",
                    SecondaryButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    ProgressOverlayText.Text = $"Updating {localItem.Name}...";
                    ProgressOverlay.Visibility = Visibility.Visible;
                    try
                    {
                        var tempFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_downloads");
                        if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
                        string tempZip = Path.Combine(tempFolder, $"{onlineItem.FolderName}_{Guid.NewGuid():N}.zip");
                        using (var response = await _httpClient.GetAsync(onlineItem.Path, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                await response.Content.CopyToAsync(fs);
                            }
                        }
                        
                        var installedVer = _installedVersions.FirstOrDefault(v => v.Version == targetVersion);
                        if (installedVer != null && !string.IsNullOrEmpty(installedVer.ConfigPath))
                        {
                            await _addonService.InstallAddonAsync(tempZip, installedVer.ConfigPath, localItem.Repository ?? "user_default");
                            ShowSuccess("Updated Successfully", $"\"{localItem.Name}\" was updated to v{onlineItem.Version} for Blender {targetVersion}.");
                            await RefreshDataAsync();
                        }
                        else
                        {
                            ShowError("Update Failed", "Could not determine the installation path for this Blender version.");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowError("Update Failed", ex.Message);
                    }
                    finally
                    {
                        ProgressOverlay.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private async void DownloadInstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddonsListView.SelectedItem is not AddonItem item) return;

            if (_installedVersions == null || _installedVersions.Count == 0)
            {
                ShowWarning("No Blender Versions", "No installed Blender versions were detected. Please set up Blender configurations in Settings first.");
                return;
            }

            var versionCombo = new ComboBox
            {
                Header = "Target Blender Version",
                ItemsSource = _installedVersions,
                DisplayMemberPath = "DisplayName",
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var repoCombo = new ComboBox
            {
                Header = "Extension Repository (Only for modern Extensions)",
                ItemsSource = new List<string> { "user_default", "blender_org" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var noteText = new TextBlock
            {
                Text = "Note: Modern extensions will be downloaded and installed into the user extensions directory of the selected version.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
            };

            var dialogPanel = new StackPanel { Spacing = 4, Width = 340 };
            dialogPanel.Children.Add(versionCombo);
            dialogPanel.Children.Add(repoCombo);
            dialogPanel.Children.Add(noteText);

            var dialog = new ContentDialog
            {
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Download & Install Options",
                Content = dialogPanel,
                PrimaryButtonText = "Download & Install",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var selectedVersion = versionCombo.SelectedItem as BlenderVersionInfo;
                if (selectedVersion == null || string.IsNullOrEmpty(selectedVersion.ConfigPath))
                {
                    ShowError("Install Error", "Selected target Blender version is invalid.");
                    return;
                }

                var selectedRepo = repoCombo.SelectedItem as string ?? "user_default";

                ProgressOverlayText.Text = $"Downloading {item.Name}...";
                ProgressOverlay.Visibility = Visibility.Visible;

                string? tempZip = null;
                try
                {
                    var tempFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_downloads");
                    if (!Directory.Exists(tempFolder))
                    {
                        Directory.CreateDirectory(tempFolder);
                    }

                    tempZip = Path.Combine(tempFolder, $"{item.FolderName}_{Guid.NewGuid():N}.zip");

                    using (var response = await _httpClient.GetAsync(item.Path, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            await response.Content.CopyToAsync(fs);
                        }
                    }

                    await _addonService.InstallAddonAsync(tempZip, selectedVersion.ConfigPath, selectedRepo);

                    ShowSuccess("Download & Installed Successfully", $"\"{item.Name}\" was successfully downloaded and installed for {selectedVersion.DisplayName}.");
                    
                    ProgressOverlay.Visibility = Visibility.Collapsed;
                    
                    TabSegmented.SelectedIndex = 0;
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    ProgressOverlay.Visibility = Visibility.Collapsed;
                    ShowError("Download & Installation Failed", ex.Message);
                }
                finally
                {
                    if (tempZip != null && File.Exists(tempZip))
                    {
                        try
                        {
                            File.Delete(tempZip);
                        }
                        catch { }
                    }
                }
            }
        }

        private async void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddonsListView.SelectedItem is not AddonItem item) return;

            if (_installedVersions == null || _installedVersions.Count == 0)
            {
                ShowWarning("No Blender Versions", "No installed Blender versions were detected.");
                return;
            }

            var otherVersions = _installedVersions
                .Where(v => !item.BlenderVersions.Contains(v.Version))
                .ToList();

            if (otherVersions.Count == 0)
            {
                ShowWarning("Already Synced", "This addon is already installed on all other detected Blender versions, or no other Blender versions were detected.");
                return;
            }

            var versionCombo = new ComboBox
            {
                Header = "Select Target Blender Version",
                ItemsSource = otherVersions,
                DisplayMemberPath = "DisplayName",
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var noteText = new TextBlock
            {
                Text = $"Note: This will copy the addon \"{item.Name}\" to the selected Blender version.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
            };

            var dialogPanel = new StackPanel { Spacing = 4, Width = 340 };
            dialogPanel.Children.Add(versionCombo);
            dialogPanel.Children.Add(noteText);

            var dialog = new ContentDialog
            {
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Sync Addon to Version",
                Content = dialogPanel,
                PrimaryButtonText = "Sync",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var targetVersion = versionCombo.SelectedItem as BlenderVersionInfo;
                if (targetVersion == null || string.IsNullOrEmpty(targetVersion.ConfigPath))
                {
                    ShowError("Sync Error", "Selected target Blender version is invalid.");
                    return;
                }

                ProgressOverlayText.Text = $"Syncing {item.Name} to {targetVersion.DisplayName}...";
                ProgressOverlay.Visibility = Visibility.Visible;

                try
                {
                    var syncItem = new AddonItem
                    {
                        Name = item.Name,
                        FolderName = item.FolderName,
                        Version = item.Version,
                        Type = item.Type,
                        Repository = item.Repository,
                        Path = item.InstallationPaths.FirstOrDefault() ?? item.Path
                    };

                    await _addonService.SyncAddonAsync(syncItem, targetVersion.ConfigPath);
                    ShowSuccess("Sync Successful", $"Successfully synced \"{item.Name}\" to {targetVersion.DisplayName}.");
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    ShowError("Sync Failed", ex.Message);
                }
                finally
                {
                    ProgressOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    public class OnlineApiResponse
    {
        [JsonPropertyName("data")]
        public List<OnlineExtData> Data { get; set; } = new();
    }

    public class OnlineExtData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("tagline")]
        public string Tagline { get; set; } = string.Empty;

        [JsonPropertyName("archive_url")]
        public string ArchiveUrl { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("blender_version_min")]
        public string BlenderVersionMin { get; set; } = string.Empty;

        [JsonPropertyName("website")]
        public string Website { get; set; } = string.Empty;

        [JsonPropertyName("maintainer")]
        public string Maintainer { get; set; } = string.Empty;

        [JsonPropertyName("license")]
        public List<string> License { get; set; } = new();

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("permissions")]
        public Dictionary<string, string>? Permissions { get; set; }

        [JsonPropertyName("archive_size")]
        public long ArchiveSize { get; set; }
    }

    /// <summary>
    /// Converts addon type ("Legacy Addon" vs "Extension") to corresponding badge background brush.
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
            // Sleek Steel Blue brush for legacy addons
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 71, 85, 105));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


}
