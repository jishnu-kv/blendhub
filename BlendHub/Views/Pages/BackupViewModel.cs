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
#pragma warning disable MVVMTK0045
    public partial class BackupViewModel : ObservableObject
    {
        private readonly BlenderSettingsService _blenderService = new();
        private readonly ResourceLoader _resourceLoader = new();

        public ObservableCollection<ConfigItemViewModel> BackupItems { get; } = new();
        public ObservableCollection<string> Backups { get; } = new();
        public ObservableCollection<BlenderVersionInfo> InstalledVersionsList { get; } = new();

        [ObservableProperty]
        private BlenderVersionInfo? _selectedVersion;

        [ObservableProperty]
        private string _versionPath = string.Empty;

        [ObservableProperty]
        private bool _isBackingUp;

        [ObservableProperty]
        private double _backupProgress;

        [ObservableProperty]
        private bool _isStartBackupEnabled;

        [ObservableProperty]
        private bool _isWarningOpen;

        [ObservableProperty]
        private string _warningTitle = string.Empty;

        [ObservableProperty]
        private string _warningMessage = string.Empty;

        [ObservableProperty]
        private bool _isWarningClosable = true;

        [ObservableProperty]
        private InfoBarSeverity _warningSeverity = InfoBarSeverity.Warning;

        [ObservableProperty]
        private bool _isSuccessOpen;

        [ObservableProperty]
        private string _successMessage = string.Empty;

        [ObservableProperty]
        private bool _isErrorOpen;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isManageBackupsExpanded;

        [ObservableProperty]
        private bool _isManageBackupsEnabled;

        private readonly CollectionViewSource _groupedBackupItems;

        public BackupViewModel(CollectionViewSource groupedBackupItems)
        {
            _groupedBackupItems = groupedBackupItems;
            
            // Ensure backup directory exists
            var backupDir = AppSettingsService.Instance.Settings.BackupDirectory;
            if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                Directory.CreateDirectory(backupDir);

            ValidateBackupState();
        }

        public async Task LoadVersionsAsync()
        {
            var versions = await _blenderService.GetInstalledVersionsAsync();
            InstalledVersionsList.Clear();
            foreach (var ver in versions)
            {
                InstalledVersionsList.Add(ver);
            }
            if (InstalledVersionsList.Count > 0)
                SelectedVersion = InstalledVersionsList[0];
        }

        partial void OnSelectedVersionChanged(BlenderVersionInfo? value)
        {
            if (value != null)
            {
                VersionPath = value.ConfigPath;
                _ = RefreshItemsAsync(value.ConfigPath);
            }
        }

        public async Task RefreshItemsAsync(string versionPath)
        {
            await BlenderPageHelper.RefreshConfigItemsAsync(BackupItems, versionPath, _blenderService,
                (vm) => ValidateBackupState(), _groupedBackupItems);
            ValidateBackupState();
        }

        public void RefreshManageBackupsList()
        {
            var backupDir = AppSettingsService.Instance.Settings.BackupDirectory;
            if (string.IsNullOrEmpty(backupDir))
            {
                IsManageBackupsEnabled = false;
                IsManageBackupsExpanded = false;
                Backups.Clear();
                ValidateBackupState();
                return;
            }

            var backupsList = _blenderService.GetBackups(backupDir);
            Backups.Clear();
            foreach (var backup in backupsList)
            {
                Backups.Add(backup);
            }

            bool hasBackups = Backups.Count > 0;
            IsManageBackupsEnabled = hasBackups;
            if (!hasBackups) IsManageBackupsExpanded = false;

            ValidateBackupState();
        }

        public void ValidateBackupState()
        {
            if (IsBackingUp) return;

            bool hasLocation = !string.IsNullOrWhiteSpace(AppSettingsService.Instance.Settings.BackupDirectory);
            bool hasItems = BackupItems.Any(i => i.IsEnabled);
            bool hasBackups = Backups.Count > 0;

            if (!hasLocation || !hasItems || !hasBackups)
            {
                if (!hasLocation)
                {
                    IsStartBackupEnabled = false;
                    WarningTitle = _resourceLoader.GetString("Warning_MissingLocation_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_MissingLocation_Message");
                    IsWarningClosable = true;
                    WarningSeverity = InfoBarSeverity.Warning;
                }
                else if (!hasItems)
                {
                    IsStartBackupEnabled = false;
                    WarningTitle = _resourceLoader.GetString("Warning_NoItemsSelected_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_NoItemsSelected_Message");
                    IsWarningClosable = true;
                    WarningSeverity = InfoBarSeverity.Warning;
                }
                else
                {
                    // Has location and items, but no backups created yet
                    IsStartBackupEnabled = true;
                    WarningTitle = _resourceLoader.GetString("Warning_NoBackupsAvailable_Title");
                    WarningMessage = _resourceLoader.GetString("Warning_NoBackupsAvailable_Message");
                    IsWarningClosable = false;
                    WarningSeverity = InfoBarSeverity.Informational;
                }

                IsWarningOpen = true;
                IsSuccessOpen = false;
            }
            else
            {
                IsStartBackupEnabled = true;
                IsWarningOpen = false;
                IsWarningClosable = true;
            }
        }

        public async Task ExecuteBackupAsync(BlenderVersionInfo info, string backupName)
        {
            IsStartBackupEnabled = false;
            IsBackingUp = true;
            BackupProgress = 0;

            try
            {
                var items = BackupItems.Select(vm => new Services.BackupItem
                {
                    Name = vm.Name,
                    IsEnabled = vm.IsEnabled,
                    RelativePath = vm.RelativePath,
                    IsFolder = vm.IsFolder
                }).ToList();

                await _blenderService.BackupAsync(info.ConfigPath, AppSettingsService.Instance.Settings.BackupDirectory, backupName, items, (msg, progress) =>
                {
                    BackupProgress = progress * 100;
                });

                SuccessMessage = string.Format(_resourceLoader.GetString("Success_BackupComplete"), Path.Combine(AppSettingsService.Instance.Settings.BackupDirectory, backupName));
                IsSuccessOpen = true;
            }
            catch (Exception ex)
            {
                IsErrorOpen = true;
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsStartBackupEnabled = true;
                IsBackingUp = false;
                RefreshManageBackupsList();
            }
        }

        public void OpenBackupFolder(string path)
        {
            _blenderService.OpenBackupFolder(path);
        }

        public void DeleteBackup(string backupName)
        {
            try
            {
                _blenderService.DeleteBackup(AppSettingsService.Instance.Settings.BackupDirectory, backupName);
                RefreshManageBackupsList();
                SuccessMessage = string.Format(_resourceLoader.GetString("Success_DeleteBackup"), backupName);
                IsSuccessOpen = true;
            }
            catch (Exception ex)
            {
                IsErrorOpen = true;
                ErrorMessage = ex.Message;
            }
        }

        [RelayCommand]
        public void OpenBackupFolderCmd(string backupName)
        {
            string backupPath = System.IO.Path.Combine(AppSettingsService.Instance.Settings.BackupDirectory, backupName);
            OpenBackupFolder(backupPath);
        }

        [RelayCommand]
        public async Task DeleteBackupCmdAsync(DeleteBackupParams parameters)
        {
            var deleteDialog = new ContentDialog
            {
                Title = _resourceLoader.GetString("Dialog_DeleteBackup_Title"),
                Content = string.Format(_resourceLoader.GetString("Dialog_DeleteBackup_Content"), parameters.BackupName),
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.None,
                XamlRoot = parameters.XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Microsoft.UI.Xaml.Style,
                RequestedTheme = (App.MainWindow.Content as Microsoft.UI.Xaml.FrameworkElement)?.RequestedTheme ?? Microsoft.UI.Xaml.ElementTheme.Default,
                CloseButtonStyle = Microsoft.UI.Xaml.Application.Current.Resources["DefaultButtonStyle"] as Microsoft.UI.Xaml.Style
            };

            var result = await deleteDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                DeleteBackup(parameters.BackupName);
            }
        }

        [RelayCommand]
        public async Task StartBackupCmdAsync(Microsoft.UI.Xaml.XamlRoot xamlRoot)
        {
            IsWarningOpen = false;
            IsSuccessOpen = false;
            IsErrorOpen = false;

            if (SelectedVersion == null)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoVersionSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoVersionSelected_Message");
                IsWarningOpen = true;
                return;
            }

            if (string.IsNullOrEmpty(AppSettingsService.Instance.Settings.BackupDirectory))
            {
                WarningTitle = _resourceLoader.GetString("Warning_MissingLocation_Title");
                WarningMessage = _resourceLoader.GetString("Warning_MissingLocation_Message");
                IsWarningOpen = true;
                return;
            }

            var enabledItems = BackupItems.Where(i => i.IsEnabled).ToList();
            if (enabledItems.Count == 0)
            {
                WarningTitle = _resourceLoader.GetString("Warning_NoItemsSelected_Title");
                WarningMessage = _resourceLoader.GetString("Warning_NoItemsSelected_Message");
                IsWarningOpen = true;
                return;
            }

            // Create and show backup name dialog
            string defaultName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            string versionName = SelectedVersion.Version;

            var nameTextBox = new TextBox
            {
                PlaceholderText = "YYYY-MM-DD_HH-mm",
                Text = defaultName
            };

            var previewText = new TextBlock
            {
                Foreground = Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                FontSize = 12,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Text = string.Format(_resourceLoader.GetString("Dialog_NameBackup_FolderPrefix"), defaultName, versionName)
            };

            var dialogContent = new StackPanel { Spacing = 12, Margin = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0) };
            dialogContent.Children.Add(new TextBlock { Text = _resourceLoader.GetString("Dialog_NameBackup_Description"), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            dialogContent.Children.Add(nameTextBox);
            dialogContent.Children.Add(previewText);

            var dialog = new ContentDialog
            {
                Title = _resourceLoader.GetString("Dialog_NameBackup_Title"),
                Content = dialogContent,
                PrimaryButtonText = "Start Backup",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Microsoft.UI.Xaml.Style,
                RequestedTheme = (App.MainWindow.Content as Microsoft.UI.Xaml.FrameworkElement)?.RequestedTheme ?? Microsoft.UI.Xaml.ElementTheme.Default,
                CloseButtonStyle = Microsoft.UI.Xaml.Application.Current.Resources["DefaultButtonStyle"] as Microsoft.UI.Xaml.Style
            };

            nameTextBox.TextChanged += (s, e2) =>
            {
                var name = nameTextBox.Text.Trim();
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name);
                previewText.Text = string.IsNullOrWhiteSpace(name)
                    ? string.Format(_resourceLoader.GetString("Dialog_NameBackup_FolderPrefix"), defaultName, versionName)
                    : string.Format(_resourceLoader.GetString("Dialog_NameBackup_FolderPrefix"), name, versionName);
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            string backupName = nameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(backupName))
                backupName = defaultName;

            await ExecuteBackupAsync(SelectedVersion, backupName);
        }

        public System.Collections.Generic.List<ConfigItemViewModel> GetBackupItemsForPath(string backupName)
        {
            string backupDir = AppSettingsService.Instance.Settings.BackupDirectory;
            string backupPath = System.IO.Path.Combine(backupDir, backupName);

            if (System.IO.Directory.Exists(backupPath))
            {
                var trackedItems = _blenderService.GetDefaultBackupItems(backupPath);
                return trackedItems.Select(item => new ConfigItemViewModel
                {
                    Name = item.Name,
                    IsEnabled = item.IsEnabled,
                    IsExists = item.Exists,
                    TooltipText = item.Category,
                    Category = item.Category,
                    RelativePath = item.RelativePath,
                    IsFolder = item.IsFolder
                }).ToList();
            }
            return new System.Collections.Generic.List<ConfigItemViewModel>();
        }
    }

    public record DeleteBackupParams(Microsoft.UI.Xaml.XamlRoot XamlRoot, string BackupName);
#pragma warning restore MVVMTK0045
}
