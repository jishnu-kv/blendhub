using BlendHub.Models;
using BlendHub.Services;
using BlendHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class RestorePage : Page
    {
        public RestoreViewModel ViewModel { get; }

        public RestorePage()
        {
            this.InitializeComponent();
            ViewModel = new RestoreViewModel(GroupedRestoreItems);
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.IsWarningOpen) ||
                    e.PropertyName == nameof(ViewModel.IsSuccessOpen) ||
                    e.PropertyName == nameof(ViewModel.IsErrorOpen))
                {
                    UpdateInfoBarPanelMargin();
                }
            };
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadTargetVersionsAsync();

            var backupDir = AppSettingsService.Instance.Settings.BackupDirectory;
            ViewModel.RefreshBackups(backupDir);

            if (App.MainWindow != null)
            {
                App.MainWindow.Activated += MainWindow_Activated;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (App.MainWindow != null)
            {
                App.MainWindow.Activated -= MainWindow_Activated;
            }
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                var backupDir = AppSettingsService.Instance.Settings.BackupDirectory;
                
                // Save current selection
                var currentSelection = ViewModel.SelectedBackup;
                ViewModel.RefreshBackups(backupDir);
                
                // Try to restore selection
                if (!string.IsNullOrEmpty(currentSelection) && ViewModel.BackupsList.Contains(currentSelection))
                {
                    ViewModel.SelectedBackup = currentSelection;
                }
                
                if (ViewModel.SelectedBackup is string backupName)
                {
                    await ViewModel.RefreshItemsAsync(backupName);
                }
            }
        }

        private async void BackupNameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackupNameComboBox.SelectedItem is string backupName)
            {
                await ViewModel.RefreshItemsAsync(backupName);
            }
            ViewModel.ValidateRestoreState();
        }

        private void TargetVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel.ValidateRestoreState();
        }

        private async void StartRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartRestoreCmdCommand.ExecuteAsync(this.XamlRoot);
            UpdateInfoBarPanelMargin();
        }

        private void LaunchBlenderButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LaunchBlenderCmdCommand.Execute(null);
        }

        private void InfoBar_Closed(Microsoft.UI.Xaml.Controls.InfoBar sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs args)
        {
            UpdateInfoBarPanelMargin();
        }

        private void UpdateInfoBarPanelMargin()
        {
            if (InfoBarPanel == null || ViewModel == null) return;
            bool anyOpen = ViewModel.IsSuccessOpen || ViewModel.IsWarningOpen || ViewModel.IsErrorOpen;
            InfoBarPanel.Margin = new Thickness(0, 0, 0, 0);
            InfoBarPanel.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
