#pragma warning disable MVVMTK0045
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BlendHub.Models;
using BlendHub.Services;
using BlendHub.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BlendHub.ViewModels
{
    public partial class SyncViewModel : ObservableObject
    {
        private readonly BlenderSettingsService _blenderService = new();
        private readonly ResourceLoader _resourceLoader = new();
        private readonly CollectionViewSource _groupedSyncItems;

        public ObservableCollection<ConfigItemViewModel> SyncItems { get; } = new();
        public ObservableCollection<TargetVersionViewModel> TargetVersions { get; } = new();
        public ObservableCollection<BlenderVersionInfo> SourceVersionsList { get; } = new();

        [ObservableProperty]
        private BlenderVersionInfo? _selectedSource;

        [ObservableProperty]
        private bool _isSyncing;

        [ObservableProperty]
        private double _syncProgress;

        [ObservableProperty]
        private bool _isStartSyncEnabled;

        [ObservableProperty]
        private bool _isWarningOpen;

        [ObservableProperty]
        private string _warningTitle = string.Empty;

        [ObservableProperty]
        private string _warningMessage = string.Empty;

        [ObservableProperty]
        private InfoBarSeverity _warningSeverity = InfoBarSeverity.Warning;

        [ObservableProperty]
        private bool _isWarningClosable = true;

        [ObservableProperty]
        private bool _isSuccessOpen;

        [ObservableProperty]
        private string _successMessage = string.Empty;

        [ObservableProperty]
        private bool _isErrorOpen;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isSourceCardEnabled = true;

        [ObservableProperty]
        private bool _isTargetVersionsExpanderEnabled = true;

        [ObservableProperty]
        private bool _isItemsToSyncExpanderEnabled = true;

        public SyncViewModel(CollectionViewSource groupedSyncItems)
        {
            _groupedSyncItems = groupedSyncItems;
            ValidateSyncState();
        }

        public async Task LoadVersionsAsync()
        {
            var versions = await _blenderService.GetInstalledVersionsAsync();
            SourceVersionsList.Clear();
            foreach (var ver in versions)
            {
                SourceVersionsList.Add(ver);
            }
            if (SourceVersionsList.Count > 0)
                SelectedSource = SourceVersionsList[0];
        }

        partial void OnSelectedSourceChanged(BlenderVersionInfo? value)
        {
            if (value != null)
            {
                _ = RefreshItemsAsync(value.ConfigPath);
                RefreshTargetVersions(value.Version);
            }
            ValidateSyncState();
        }

        public async Task RefreshItemsAsync(string versionPath)
        {
            await BlenderPageHelper.RefreshConfigItemsAsync(SyncItems, versionPath, _blenderService, 
                (vm) => ValidateSyncState(), _groupedSyncItems);
            ValidateSyncState();
        }

        public void RefreshTargetVersions(string sourceVersion)
        {
            TargetVersions.Clear();
            foreach (var v in SourceVersionsList)
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
                    TargetVersions.Add(vm);
                }
            }
            ValidateSyncState();
        }

        public void ValidateSyncState()
        {
            if (IsSyncing) return;

            bool hasSource = SelectedSource != null;
            bool hasItems = SyncItems.Any(i => i.IsEnabled);
            bool hasTarget = TargetVersions.Any(v => v.IsSelected);

            if (!hasSource || !hasItems || !hasTarget || TargetVersions.Count == 0)
            {
                IsStartSyncEnabled = false;

                if (!hasSource)
                {
                    WarningTitle = _resourceLoader.GetString("Warning_MissingSource_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_MissingSource_Message");
                    WarningSeverity = InfoBarSeverity.Warning;
                }
                else if (TargetVersions.Count == 0)
                {
                    WarningTitle = _resourceLoader.GetString("Warning_NoTargetsAvailable_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_NoTargetsAvailable_Message");
                    WarningSeverity = InfoBarSeverity.Error;
                }
                else if (!hasItems)
                {
                    WarningTitle = _resourceLoader.GetString("Warning_NoSyncItemsSelected_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_NoSyncItemsSelected_Message");
                    WarningSeverity = InfoBarSeverity.Warning;
                }
                else if (!hasTarget)
                {
                    WarningTitle = _resourceLoader.GetString("Warning_NoTargetsSelected_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_NoTargetsSelected_Message");
                    WarningSeverity = InfoBarSeverity.Warning;
                }

                IsWarningOpen = true;
                IsSuccessOpen = false;

                EnableAllExpanders(TargetVersions.Count > 0);
            }
            else
            {
                IsStartSyncEnabled = true;
                IsWarningOpen = false;
                EnableAllExpanders(true);
            }
        }

        [RelayCommand]
        public async Task StartSyncCmdAsync(Microsoft.UI.Xaml.XamlRoot xamlRoot)
        {
            IsWarningOpen = false;
            IsSuccessOpen = false;
            IsErrorOpen = false;

            if (SelectedSource == null)
            {
                WarningTitle = _resourceLoader.GetString("Warning_MissingSource_Title");
                WarningMessage = _resourceLoader.GetString("Warning_MissingSource_Message");
                IsWarningOpen = true;
                return;
            }

            var enabledItems = SyncItems.Where(i => i.IsEnabled).ToList();
            if (enabledItems.Count == 0)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoSyncItemsSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoSyncItemsSelected_Message");
                IsWarningOpen = true;
                return;
            }

            var selectedTargets = TargetVersions.Where(v => v.IsSelected).ToList();
            if (selectedTargets.Count == 0)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoTargetsSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoTargetsSelected_Message");
                IsWarningOpen = true;
                return;
            }

            // Version mismatch check (New to Old)
            bool hasCriticalItems = enabledItems.Any(i => i.Name == "Preferences" || i.Name == "Startup File");
            var olderTargets = selectedTargets.Where(t => BlenderPageHelper.IsVersionNewer(SelectedSource.Version, t.Version)).ToList();

            if (hasCriticalItems && olderTargets.Count > 0)
            {
                var targetVersionsStr = string.Join(", ", olderTargets.Select(t => t.Version));
                var result = await BlenderPageHelper.ShowVersionMismatchDialog(
                    new Frame { XamlRoot = xamlRoot }, 
                    SelectedSource.Version, 
                    targetVersionsStr, 
                    "syncing"
                );
                if (result != ContentDialogResult.Primary)
                    return;
            }

            IsStartSyncEnabled = false;
            IsSyncing = true;
            SyncProgress = 0;

            try
            {
                var items = SyncItems.Select(vm => new Services.BackupItem
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
                    await _blenderService.SyncAsync(SelectedSource.ConfigPath, target.Path, items, (msg, progress) =>
                    {
                        SyncProgress = ((double)currentTarget + progress) / totalTargets * 100;
                    });
                    currentTarget++;
                }

                SuccessMessage = string.Format(_resourceLoader.GetString("Success_SyncComplete"), selectedTargets.Count);
                IsSuccessOpen = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                IsErrorOpen = true;
            }
            finally
            {
                IsStartSyncEnabled = true;
                IsSyncing = false;
                ValidateSyncState();
            }
        }

        [RelayCommand]
        public void LaunchBlenderCmd()
        {
            var selectedTargets = TargetVersions.Where(v => v.IsSelected).ToList();
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

        private void EnableAllExpanders(bool enable)
        {
            IsItemsToSyncExpanderEnabled = enable;
            IsTargetVersionsExpanderEnabled = enable;
            IsSourceCardEnabled = enable;
        }
    }
}
#pragma warning restore MVVMTK0045
