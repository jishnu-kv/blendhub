using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Pages
{
    public sealed partial class AddonsPage : Page
    {
        // --- Details Panel Click Handlers & Actions ---
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as AddonItem ?? ActiveAddonList.SelectedItem as AddonItem;
            if (item == null) return;

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
            var item = (sender as FrameworkElement)?.DataContext as AddonItem ?? ActiveAddonList.SelectedItem as AddonItem;
            if (item == null || string.IsNullOrEmpty(item.WebsiteUrl)) return;

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
            var item = (sender as FrameworkElement)?.DataContext as AddonItem ?? ActiveAddonList.SelectedItem as AddonItem;
            if (item == null) return;

            var selectedVersions = new List<(string Version, string Path)>();

            if (item.BlenderVersions.Count > 1)
            {
                var versionsAndPaths = new List<(string Version, string Path)>();
                for (int i = 0; i < item.BlenderVersions.Count; i++)
                {
                    var ver = item.BlenderVersions[i];
                    var path = item.InstallationPaths.Count > i ? item.InstallationPaths[i] : item.Path;
                    versionsAndPaths.Add((ver, path));
                }

                var dialogPanel = CreateVersionSelectionPanel(
                    "Select Blender versions to uninstall from:",
                    versionsAndPaths,
                    vp => $"Blender {vp.Version}",
                    out var checkBoxes);

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

                foreach (var cb in checkBoxes)
                {
                    if (cb.IsChecked == true && cb.Tag is ValueTuple<string, string> vp)
                    {
                        selectedVersions.Add((vp.Item1, vp.Item2));
                    }
                }

                if (selectedVersions.Count == 0)
                {
                    ShowWarning("No Selection", "Please select at least one Blender version to uninstall from.");
                    return;
                }
            }
            else
            {
                selectedVersions.Add((item.BlenderVersion, item.Path));
            }

            string confirmationContent;
            if (selectedVersions.Count == 1)
            {
                confirmationContent = $"Are you sure you want to completely uninstall \"{item.Name}\" from Blender {selectedVersions[0].Version}?\n\nThis will permanently delete the addon directory/files:\n{selectedVersions[0].Path}";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Are you sure you want to completely uninstall \"{item.Name}\" from the following Blender versions?\n");
                foreach (var sv in selectedVersions)
                {
                    sb.AppendLine($"• Blender {sv.Version} at:\n  {sv.Path}\n");
                }
                sb.AppendLine("This will permanently delete the addon directory/files.");
                confirmationContent = sb.ToString();
            }

            var dialog = new ContentDialog
            {
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Uninstall Addon?",
                Content = confirmationContent,
                PrimaryButtonText = "Uninstall",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                int successCount = 0;
                var failedVersions = new List<(string Version, string Error)>();

                foreach (var sv in selectedVersions)
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
                            BlenderVersion = sv.Version,
                            Path = sv.Path,
                            ExtensionType = item.ExtensionType
                        };

                        await _addonService.UninstallAddonAsync(uninstallItem);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failedVersions.Add((sv.Version, ex.Message));
                    }
                }

                await RefreshDataAsync();

                if (failedVersions.Count == 0)
                {
                    if (selectedVersions.Count == 1)
                    {
                        ShowSuccess("Uninstalled", $"Successfully uninstalled \"{item.Name}\" from Blender {selectedVersions[0].Version}.");
                    }
                    else
                    {
                        ShowSuccess("Uninstalled", $"Successfully uninstalled \"{item.Name}\" from {successCount} Blender versions.");
                    }
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"Failed to uninstall from some versions:\n");
                    foreach (var f in failedVersions)
                    {
                        sb.AppendLine($"• Blender {f.Version}: {f.Error}");
                    }
                    ShowError("Uninstall failed", sb.ToString());
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Capture current installed addons before refresh
            var oldAddonsSet = _allAddons
                .SelectMany(a => a.BlenderVersions.Select(v => (Name: a.Name, Version: a.Version, Type: a.Type, BlenderVersion: v)))
                .ToHashSet();

            await RefreshDataAsync();
            await LoadOnlineExtensionsAsync();

            // Capture installed addons after refresh
            var newAddonsSet = _allAddons
                .SelectMany(a => a.BlenderVersions.Select(v => (Name: a.Name, Version: a.Version, Type: a.Type, BlenderVersion: v)))
                .ToHashSet();

            // Compare sets
            var newlyInstalled = newAddonsSet.Where(x => !oldAddonsSet.Contains(x)).ToList();
            var removed = oldAddonsSet.Where(x => !newAddonsSet.Contains(x)).ToList();

            if (newlyInstalled.Count > 0 || removed.Count > 0)
            {
                var messages = new List<string>();
                if (newlyInstalled.Count > 0)
                {
                    var detail = string.Join(", ", newlyInstalled.Select(x => $"{x.Name} ({x.Version}) on Blender {x.BlenderVersion}"));
                    messages.Add($"Added: {detail}");
                }
                if (removed.Count > 0)
                {
                    var detail = string.Join(", ", removed.Select(x => $"{x.Name} ({x.Version}) on Blender {x.BlenderVersion}"));
                    messages.Add($"Removed: {detail}");
                }

                ShowSuccess("Addons Sync Status", string.Join(" | ", messages));
            }
            else
            {
                ShowSuccess("Addons Synced", "No changes in installed addons detected.");
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
                    Text = "Note: If the selected .zip contains a 'blender_manifest.toml' file, it will be installed as an Extension under the selected repository. Otherwise, it will be installed as an Addon.",
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
                        await RefreshDataAsync();
                        ShowSuccess("Installed Successfully", $"\"{file.Name}\" was installed for {selectedVersion.DisplayName}.");
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

        private async void UpdateAddonButton_Click(object sender, RoutedEventArgs e)
        {
            var localItem = (sender as FrameworkElement)?.DataContext as AddonItem ?? ActiveAddonList.SelectedItem as AddonItem;
            if (localItem == null) return;

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
                            await RefreshDataAsync();
                            ShowSuccess("Updated Successfully", $"\"{localItem.Name}\" was updated to v{onlineItem.Version} for Blender {targetVersion}.");
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
            var item = (sender as FrameworkElement)?.DataContext as AddonItem ?? ActiveAddonList.SelectedItem as AddonItem;
            if (item == null) return;

            if (_installedVersions == null || _installedVersions.Count == 0)
            {
                ShowWarning("No Blender Versions", "No installed Blender versions were detected. Please set up Blender configurations in Settings first.");
                return;
            }

            var dialogPanel = CreateVersionSelectionPanel(
                "Select Target Blender Versions:",
                _installedVersions,
                ver => ver.DisplayName,
                out var checkBoxes);

            var repoCombo = new ComboBox
            {
                Header = "Extension Repository (Only for modern Extensions)",
                ItemsSource = new List<string> { "user_default", "blender_org" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 12, 0, 12)
            };
            dialogPanel.Children.Add(repoCombo);

            var noteText = new TextBlock
            {
                Text = "Note: Modern extensions will be downloaded and installed into the user extensions directory of the selected versions.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
            };
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
                var selectedVersions = checkBoxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag as BlenderVersionInfo)
                    .Where(ver => ver != null && !string.IsNullOrEmpty(ver.ConfigPath))
                    .ToList();

                if (selectedVersions.Count == 0)
                {
                    ShowWarning("No Selection", "Please select at least one target Blender version.");
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

                    int successCount = 0;
                    var failedVersions = new List<(string Version, string Error)>();

                    foreach (var ver in selectedVersions)
                    {
                        try
                        {
                            ProgressOverlayText.Text = $"Installing {item.Name} to {ver!.DisplayName}...";
                            await _addonService.InstallAddonAsync(tempZip, ver.ConfigPath, selectedRepo);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failedVersions.Add((ver!.DisplayName, ex.Message));
                        }
                    }

                    ProgressOverlay.Visibility = Visibility.Collapsed;

                    await RefreshDataAsync();

                    if (failedVersions.Count == 0)
                    {
                        if (selectedVersions.Count == 1)
                        {
                            ShowSuccess("Download & Installed Successfully", $"\"{item.Name}\" was successfully downloaded and installed for {selectedVersions[0]!.DisplayName}.");
                        }
                        else
                        {
                            ShowSuccess("Download & Installed Successfully", $"\"{item.Name}\" was successfully downloaded and installed for {successCount} Blender versions.");
                        }
                    }
                    else
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"Failed to install to some versions:\n");
                        foreach (var f in failedVersions)
                        {
                            sb.AppendLine($"• {f.Version}: {f.Error}");
                        }
                        ShowError("Installation failed", sb.ToString());
                    }
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
            var item = (sender as FrameworkElement)?.DataContext as AddonItem ?? ActiveAddonList.SelectedItem as AddonItem;
            if (item == null) return;

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

            var dialogPanel = CreateVersionSelectionPanel(
                "Select Target Blender Versions:",
                otherVersions,
                ver => ver.DisplayName,
                out var checkBoxes);

            var noteText = new TextBlock
            {
                Text = $"Note: This will copy the addon \"{item.Name}\" to the selected Blender versions.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
            };
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
                var selectedVersions = checkBoxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag as BlenderVersionInfo)
                    .Where(ver => ver != null && !string.IsNullOrEmpty(ver.ConfigPath))
                    .ToList();

                if (selectedVersions.Count == 0)
                {
                    ShowWarning("No Selection", "Please select at least one target Blender version.");
                    return;
                }

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
                        Path = item.InstallationPaths.FirstOrDefault() ?? item.Path,
                        ExtensionType = item.ExtensionType
                    };

                    int successCount = 0;
                    var failedVersions = new List<(string Version, string Error)>();

                    foreach (var ver in selectedVersions)
                    {
                        try
                        {
                            ProgressOverlayText.Text = $"Syncing {item.Name} to {ver!.DisplayName}...";
                            await _addonService.SyncAddonAsync(syncItem, ver.ConfigPath);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failedVersions.Add((ver!.DisplayName, ex.Message));
                        }
                    }

                    await RefreshDataAsync();

                    if (failedVersions.Count == 0)
                    {
                        if (selectedVersions.Count == 1)
                        {
                            ShowSuccess("Sync Successful", $"Successfully synced \"{item.Name}\" to {selectedVersions[0]!.DisplayName}.");
                        }
                        else
                        {
                            ShowSuccess("Sync Successful", $"Successfully synced \"{item.Name}\" to {successCount} Blender versions.");
                        }
                    }
                    else
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"Failed to sync to some versions:\n");
                        foreach (var f in failedVersions)
                        {
                            sb.AppendLine($"• {f.Version}: {f.Error}");
                        }
                        ShowError("Sync failed", sb.ToString());
                    }
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

        // --- Shared Dialog Helpers ---

        private StackPanel CreateVersionSelectionPanel<T>(
            string headerText,
            IEnumerable<T> items,
            Func<T, string> displayNameSelector,
            out List<CheckBox> checkBoxes)
        {
            checkBoxes = new List<CheckBox>();
            var dialogPanel = new StackPanel { Spacing = 8, Width = 340 };

            var headerTextBlock = new TextBlock
            {
                Text = headerText,
                Margin = new Thickness(0, 0, 0, 4),
                Style = Application.Current.Resources["BodyTextBlockStyle"] as Style
            };
            dialogPanel.Children.Add(headerTextBlock);

            foreach (var item in items)
            {
                var cb = new CheckBox
                {
                    Content = displayNameSelector(item),
                    Tag = item,
                    IsChecked = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                checkBoxes.Add(cb);
                dialogPanel.Children.Add(cb);
            }

            return dialogPanel;
        }

        // --- InfoBar Helpers ---

        private void ShowInfoBar(Microsoft.UI.Xaml.Controls.InfoBar infoBar, string title, string message)
        {
            infoBar.Title = title;
            infoBar.Message = message;
            infoBar.IsOpen = true;
            UpdateInfoBarPanelMargin();
        }

        private void ShowSuccess(string title, string message) => ShowInfoBar(SuccessInfoBar, title, message);
        private void ShowWarning(string title, string message) => ShowInfoBar(WarningInfoBar, title, message);
        private void ShowError(string title, string message) => ShowInfoBar(ErrorInfoBar, title, message);

        private void InfoBar_Closed(Microsoft.UI.Xaml.Controls.InfoBar sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs args)
        {
            UpdateInfoBarPanelMargin();
        }

        private void UpdateInfoBarPanelMargin()
        {
            bool anyOpen = SuccessInfoBar.IsOpen || WarningInfoBar.IsOpen || ErrorInfoBar.IsOpen;
            InfoBarPanel.Margin = new Thickness(36, 0, 36, anyOpen ? 8 : 0);
        }

        private void DetailAuthorButton_Click(object sender, RoutedEventArgs e)
        {
            var item = ActiveAddonList.SelectedItem as AddonItem;
            if (item != null && !string.IsNullOrEmpty(item.Author))
            {
                SearchBox.Text = $"auther: {item.Author}";
            }
        }

        private void MenuFlyout_Opening(object sender, object e)
        {
            if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement target && target.DataContext is AddonItem item)
            {
                var downloadItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuDownloadInstall");
                var updateItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuUpdate");
                var openFolderItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuOpenFolder");
                var websiteItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuWebsite");
                var syncItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuSync");
                var separatorItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuSeparator");
                var uninstallItem = flyout.Items.FirstOrDefault(i => i.Name == "MenuUninstall");

                bool isOnline = !string.IsNullOrEmpty(item.Path) && item.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

                if (isOnline)
                {
                    if (downloadItem != null) downloadItem.Visibility = Visibility.Visible;
                    if (websiteItem is MenuFlyoutItem menuWebsiteItem)
                    {
                        menuWebsiteItem.Visibility = Visibility.Visible;
                        menuWebsiteItem.IsEnabled = !string.IsNullOrEmpty(item.WebsiteUrl);
                    }
                    if (updateItem != null) updateItem.Visibility = Visibility.Collapsed;
                    if (openFolderItem != null) openFolderItem.Visibility = Visibility.Collapsed;
                    if (syncItem != null) syncItem.Visibility = Visibility.Collapsed;
                    if (separatorItem != null) separatorItem.Visibility = Visibility.Collapsed;
                    if (uninstallItem != null) uninstallItem.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (downloadItem != null) downloadItem.Visibility = Visibility.Collapsed;

                    bool updateAvailable = false;
                    if (item.Type == "Extension" && _onlineAddons.Count > 0)
                    {
                        var onlineItem = _onlineAddons.FirstOrDefault(a =>
                            a.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(a.FolderName) && a.FolderName.Equals(item.FolderName, StringComparison.OrdinalIgnoreCase)));
                        if (onlineItem != null && System.Version.TryParse(item.Version, out var localVer) && System.Version.TryParse(onlineItem.Version, out var onlineVer))
                        {
                            updateAvailable = onlineVer > localVer;
                        }
                    }
                    if (updateItem != null) updateItem.Visibility = updateAvailable ? Visibility.Visible : Visibility.Collapsed;

                    if (openFolderItem != null) openFolderItem.Visibility = Visibility.Visible;
                    if (websiteItem is MenuFlyoutItem menuWebsiteItem2)
                    {
                        menuWebsiteItem2.Visibility = Visibility.Visible;
                        menuWebsiteItem2.IsEnabled = !string.IsNullOrEmpty(item.WebsiteUrl);
                    }
                    if (syncItem != null) syncItem.Visibility = Visibility.Visible;
                    if (separatorItem != null) separatorItem.Visibility = Visibility.Visible;
                    if (uninstallItem != null) uninstallItem.Visibility = Visibility.Visible;
                }
            }
        }
    }
}
