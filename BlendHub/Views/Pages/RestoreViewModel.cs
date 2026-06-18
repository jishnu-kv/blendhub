#pragma warning disable MVVMTK0045
using System;
using System.Collections.ObjectModel;
using System.IO;
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
    public partial class RestoreViewModel : ObservableObject
    {
        private readonly BlenderSettingsService _blenderService = new();
        private readonly ResourceLoader _resourceLoader = new();
        private readonly CollectionViewSource _groupedRestoreItems;

        public ObservableCollection<ConfigItemViewModel> RestoreItems { get; } = new();
        public ObservableCollection<string> BackupsList { get; } = new();
        public ObservableCollection<BlenderVersionInfo> TargetVersionsList { get; } = new();

        [ObservableProperty]
        private string? _selectedBackup;

        [ObservableProperty]
        private BlenderVersionInfo? _selectedTarget;

        [ObservableProperty]
        private string _backupVersion = string.Empty;

        [ObservableProperty]
        private bool _isRestoring;

        [ObservableProperty]
        private double _restoreProgress;

        [ObservableProperty]
        private bool _isStartRestoreEnabled;

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
        private bool _isBackupSelectionEnabled = true;

        [ObservableProperty]
        private bool _isTargetSelectionEnabled = true;

        [ObservableProperty]
        private bool _isItemsExpanderEnabled = true;

        public RestoreViewModel(CollectionViewSource groupedRestoreItems)
        {
            _groupedRestoreItems = groupedRestoreItems;
            ValidateRestoreState();
        }

        public async Task LoadTargetVersionsAsync()
        {
            var versions = await _blenderService.GetInstalledVersionsAsync();
            TargetVersionsList.Clear();
            foreach (var ver in versions)
            {
                TargetVersionsList.Add(ver);
            }
            if (TargetVersionsList.Count > 0)
                SelectedTarget = TargetVersionsList[0];
        }

        public void RefreshBackups(string backupPath)
        {
            var backups = _blenderService.GetBackups(backupPath);
            BackupsList.Clear();
            foreach (var backup in backups)
            {
                BackupsList.Add(backup);
            }

            if (backups.Count > 0)
            {
                SelectedBackup = BackupsList[0];
                IsErrorOpen = false;
                EnableAllExpanders(true);
            }
            else
            {
                IsErrorOpen = false;
                RestoreItems.Clear();
                EnableAllExpanders(false);
            }
            ValidateRestoreState();
        }

        partial void OnSelectedBackupChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _ = RefreshItemsAsync(value);
            }
            ValidateRestoreState();
        }

        partial void OnSelectedTargetChanged(BlenderVersionInfo? value)
        {
            ValidateRestoreState();
        }

        public async Task RefreshItemsAsync(string backupName)
        {
            BackupVersion = string.Empty;
            int lastUnderscore = backupName.LastIndexOf('_');
            if (lastUnderscore != -1)
            {
                BackupVersion = backupName.Substring(lastUnderscore + 1);
            }

            var backupRoot = Path.Combine(AppSettingsService.Instance.Settings.BackupDirectory, backupName);
            await BlenderPageHelper.RefreshConfigItemsAsync(RestoreItems, backupRoot, _blenderService, 
                (vm) => ValidateRestoreState(), _groupedRestoreItems);
            ValidateRestoreState();
        }

        public void ValidateRestoreState()
        {
            if (IsRestoring) return;

            bool hasLocation = !string.IsNullOrWhiteSpace(AppSettingsService.Instance.Settings.BackupDirectory);
            bool hasBackup = SelectedBackup != null;
            bool hasItems = RestoreItems.Any(i => i.IsEnabled);
            bool hasTarget = SelectedTarget != null;
            bool hasBackups = BackupsList.Count > 0;

            if (!hasLocation || !hasBackup || !hasItems || !hasTarget)
            {
                IsStartRestoreEnabled = false;

                if (hasBackups)
                {
                    if (!hasLocation)
                    {
                        WarningTitle = _resourceLoader.GetString("Warning_MissingLocation_Title");
                        WarningMessage = _resourceLoader.GetString("Warning_MissingLocation_Message");
                    }
                    else if (!hasBackup)
                    {
                        WarningTitle = _resourceLoader.GetString("Warning_NoBackupSelected_Title");
                        WarningMessage = _resourceLoader.GetString("Warning_NoBackupSelected_Message");
                    }
                    else if (!hasItems)
                    {
                        WarningTitle = _resourceLoader.GetString("Warning_NoRestoreItemsSelected_Title");
                        WarningMessage = _resourceLoader.GetString("Warning_NoRestoreItemsSelected_Message");
                    }
                    else if (!hasTarget)
                    {
                        WarningTitle = _resourceLoader.GetString("Warning_NoTargetSelected_Title");
                        WarningMessage = _resourceLoader.GetString("Warning_NoTargetSelected_Message");
                    }

                    WarningSeverity = InfoBarSeverity.Warning;
                    IsWarningClosable = true;
                    IsWarningOpen = true;
                    IsSuccessOpen = false;
                }
                else
                {
                    WarningTitle = _resourceLoader.GetString("Warning_NoBackupsAvailableRestore_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_NoBackupsAvailableRestore_Message");
                    WarningSeverity = InfoBarSeverity.Warning;
                    IsWarningClosable = false;
                    IsWarningOpen = true;
                    IsSuccessOpen = false;
                }
            }
            else
            {
                IsStartRestoreEnabled = true;
                IsWarningOpen = false;
                IsWarningClosable = true;
            }
        }

        [RelayCommand]
        public async Task StartRestoreCmdAsync(Microsoft.UI.Xaml.XamlRoot xamlRoot)
        {
            IsWarningOpen = false;
            IsErrorOpen = false;
            IsSuccessOpen = false;

            if (string.IsNullOrEmpty(AppSettingsService.Instance.Settings.BackupDirectory))
            {
                WarningTitle = _resourceLoader.GetString("Warning_MissingLocation_Title");
                WarningMessage = _resourceLoader.GetString("Warning_MissingLocation_Message");
                IsWarningOpen = true;
                return;
            }

            if (SelectedTarget == null)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoTargetSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoTargetSelected_Message");
                IsWarningOpen = true;
                return;
            }

            if (SelectedBackup == null)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoBackupSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoBackupSelected_Message");
                IsWarningOpen = true;
                return;
            }

            var enabledItems = RestoreItems.Where(i => i.IsEnabled).ToList();
            bool hasCriticalItems = enabledItems.Any(i => i.Name == "Preferences" || i.Name == "Startup File");

            if (hasCriticalItems && !string.IsNullOrEmpty(BackupVersion) && BlenderPageHelper.IsVersionNewer(BackupVersion, SelectedTarget.Version))
            {
                var result = await BlenderPageHelper.ShowVersionMismatchDialog(
                    new Frame { XamlRoot = xamlRoot }, 
                    BackupVersion, 
                    SelectedTarget.Version, 
                    "restoring"
                );
                if (result != ContentDialogResult.Primary)
                    return;
            }

            if (enabledItems.Count == 0)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoRestoreItemsSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoRestoreItemsSelected_Message");
                IsWarningOpen = true;
                return;
            }

            IsStartRestoreEnabled = false;
            IsRestoring = true;
            RestoreProgress = 0;

            try
            {
                var backupPath = Path.Combine(AppSettingsService.Instance.Settings.BackupDirectory, SelectedBackup);
                var items = RestoreItems.Select(vm => new Services.BackupItem
                {
                    Name = vm.Name,
                    IsEnabled = vm.IsEnabled,
                    RelativePath = vm.RelativePath,
                    IsFolder = vm.IsFolder
                }).ToList();

                await _blenderService.RestoreAsync(backupPath, SelectedTarget.ConfigPath, items, (msg, progress) =>
                {
                    RestoreProgress = progress * 100;
                });

                SuccessMessage = string.Format(_resourceLoader.GetString("Success_RestoreComplete"), SelectedTarget.DisplayName);
                IsSuccessOpen = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                IsErrorOpen = true;
            }
            finally
            {
                IsStartRestoreEnabled = true;
                IsRestoring = false;
                ValidateRestoreState();
            }
        }

        [RelayCommand]
        public void LaunchBlenderCmd()
        {
            if (SelectedTarget != null)
            {
                BlenderPageHelper.LaunchBlender(SelectedTarget, _blenderService);
            }
        }

        private void EnableAllExpanders(bool enable)
        {
            IsItemsExpanderEnabled = enable;
            IsTargetSelectionEnabled = enable;
            IsBackupSelectionEnabled = enable;
        }
    }
}
#pragma warning restore MVVMTK0045
