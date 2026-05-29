using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace BlendHub.Pages
{


    public sealed partial class SyncPage : Page
    {
        private readonly BlenderSettingsService _blenderService = new();
        private ObservableCollection<ConfigItemViewModel> _syncItems = new();
        private ObservableCollection<TargetVersionViewModel> _targetVersions = new();

        public SyncPage()
        {
            this.InitializeComponent();

            TargetVersionsListView.ItemsSource = _targetVersions;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadVersions();

            if (App.MainWindow != null)
            {
                App.MainWindow.Activated += MainWindow_Activated;
            }
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (App.MainWindow != null)
            {
                App.MainWindow.Activated -= MainWindow_Activated;
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                if (SourceVersionComboBox.SelectedItem is BlenderVersionInfo sourceInfo)
                {
                    RefreshItems(sourceInfo.ConfigPath);
                    RefreshTargetVersions(sourceInfo.Version);
                }
            }
        }

        private void LoadVersions()
        {
            var versions = _blenderService.GetInstalledVersions();
            SourceVersionComboBox.ItemsSource = versions;
            if (versions.Count > 0)
                SourceVersionComboBox.SelectedIndex = 0;
        }

        private void SourceVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceVersionComboBox.SelectedItem is BlenderVersionInfo sourceInfo)
            {
                RefreshItems(sourceInfo.ConfigPath);
                RefreshTargetVersions(sourceInfo.Version);
            }
            ValidateSyncState();
        }

        private void ValidateSyncState()
        {
            if (StartSyncButton == null || WarningInfoBar == null) return;
            if (SyncProgressBar != null && SyncProgressBar.Opacity > 0) return;

            bool hasSource = SourceVersionComboBox.SelectedItem != null;
            bool hasItems = _syncItems.Any(i => i.IsEnabled);
            bool hasTarget = _targetVersions.Any(v => v.IsSelected);

            if (!hasSource || !hasItems || !hasTarget || _targetVersions.Count == 0)
            {
                StartSyncButton.IsEnabled = false;

                if (!hasSource)
                {
                    WarningInfoBar.Title = "Missing Source";
                    WarningInfoBar.Message = "Please select a source Blender version.";
                }
                else if (_targetVersions.Count == 0)
                {
                    WarningInfoBar.Title = "No Targets Available";
                    WarningInfoBar.Message = "There are no other Blender versions installed to sync with.";
                    WarningInfoBar.Severity = InfoBarSeverity.Error;
                }
                else if (!hasItems)
                {
                    WarningInfoBar.Title = "No Items Selected";
                    WarningInfoBar.Message = "Please select at least one item to sync.";
                }
                else if (!hasTarget)
                {
                    WarningInfoBar.Title = "No Targets Selected";
                    WarningInfoBar.Message = "Please select at least one target Blender version.";
                }

                if (_targetVersions.Count > 0)
                {
                    WarningInfoBar.Severity = InfoBarSeverity.Warning;
                }

                WarningInfoBar.IsOpen = true;
                if (SuccessInfoBar != null) SuccessInfoBar.IsOpen = false;

                EnableAllExpanders(_targetVersions.Count > 0);
            }
            else
            {
                StartSyncButton.IsEnabled = true;
                WarningInfoBar.IsOpen = false;
                EnableAllExpanders(true);
            }
        }

        private void RefreshItems(string versionPath)
        {
            BlenderPageHelper.RefreshConfigItems(_syncItems, versionPath, _blenderService, 
                (vm) => ValidateSyncState(), GroupedSyncItems);
            ValidateSyncState();
        }


        private void RefreshTargetVersions(string sourceVersion)
        {
            _targetVersions.Clear();
            var allVersions = _blenderService.GetInstalledVersions();
            foreach (var v in allVersions)
            {
                if (v.Version != sourceVersion)
                {
                    var vm = new TargetVersionViewModel
                    {
                        Version = v.Version,
                        Path = v.ConfigPath,
                        IsSelected = false
                    };
                    vm.PropertyChanged += (s, e) => ValidateSyncState();
                    _targetVersions.Add(vm);
                }
            }
            ValidateSyncState();
        }

        private async void StartSyncButton_Click(object sender, RoutedEventArgs e)
        {
            WarningInfoBar.IsOpen = false;
            SuccessInfoBar.IsOpen = false;

            if (SourceVersionComboBox.SelectedItem is not BlenderVersionInfo sourceInfo)
            {
                WarningInfoBar.Title = "No Source Selected";
                WarningInfoBar.Message = "Please select a source Blender version.";
                WarningInfoBar.IsOpen = true;
                return;
            }

            var enabledItems = _syncItems.Where(i => i.IsEnabled).ToList();
            if (enabledItems.Count == 0)
            {
                WarningInfoBar.Title = "No Items Selected";
                WarningInfoBar.Message = "Please enable at least one item to include in the sync.";
                WarningInfoBar.IsOpen = true;
                return;
            }

            var selectedTargets = _targetVersions.Where(v => v.IsSelected).ToList();
            if (selectedTargets.Count == 0)
            {
                WarningInfoBar.Title = "No Target Versions";
                WarningInfoBar.Message = "Please select at least one target Blender version to sync to.";
                WarningInfoBar.IsOpen = true;
                return;
            }

            // Version mismatch check (New to Old)
            bool hasCriticalItems = enabledItems.Any(i => i.Name == "Preferences" || i.Name == "Startup File");
            var olderTargets = selectedTargets.Where(t => BlenderPageHelper.IsVersionNewer(sourceInfo.Version, t.Version)).ToList();

            if (hasCriticalItems && olderTargets.Count > 0)
            {
                var targetVersions = string.Join(", ", olderTargets.Select(t => t.Version));
                var result = await BlenderPageHelper.ShowVersionMismatchDialog(this, sourceInfo.Version, targetVersions, "syncing");
                if (result != ContentDialogResult.Primary)
                    return;
            }

            StartSyncButton.IsEnabled = false;
            SyncProgressBar.Opacity = 1;
            SyncProgressBar.IsIndeterminate = true;

            try
            {
                var items = _syncItems.Select(vm => new Services.BackupItem
                {
                    Name = vm.Name,
                    IsEnabled = vm.IsEnabled,
                    RelativePath = vm.RelativePath,
                    IsFolder = vm.IsFolder
                }).ToList();

                int totalTargets = selectedTargets.Count;
                int currentTarget = 0;

                foreach (var target in selectedTargets)
                {
                    await _blenderService.SyncAsync(sourceInfo.ConfigPath, target.Path, items, (msg, progress) =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            SyncProgressBar.IsIndeterminate = false;
                            SyncProgressBar.Value = ((double)currentTarget + progress) / totalTargets * 100;
                        });
                    });
                    currentTarget++;
                }

                SuccessInfoBar.Message = $"Settings synced to {selectedTargets.Count} Blender version(s).";
                SuccessInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                WarningInfoBar.Severity = InfoBarSeverity.Error;
                WarningInfoBar.Title = "Sync Failed";
                WarningInfoBar.Message = ex.Message;
                WarningInfoBar.IsOpen = true;
            }
            finally
            {
                StartSyncButton.IsEnabled = true;
                SyncProgressBar.Opacity = 0;
            }
        }
        private void EnableAllExpanders(bool enable)
        {
            if (TargetVersionsExpander != null)
            {
                TargetVersionsExpander.IsEnabled = enable;
                TargetVersionsExpander.IsExpanded = enable;
            }
            if (ItemsToSyncExpander != null)
            {
                ItemsToSyncExpander.IsEnabled = enable;
                ItemsToSyncExpander.IsExpanded = enable;
            }
            if (SourceVersionCard != null) SourceVersionCard.IsEnabled = enable;
        }

        private void LaunchBlenderButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedTargets = _targetVersions.Where(v => v.IsSelected).ToList();
            if (selectedTargets.Count > 0)
            {
                var firstTarget = selectedTargets.First();
                var allVersions = _blenderService.GetInstalledVersions();
                var targetVersion = allVersions.FirstOrDefault(v => v.Version == firstTarget.Version);
                if (targetVersion != null)
                {
                    BlenderPageHelper.LaunchBlender(targetVersion, _blenderService);
                }
            }
        }

    }
}
