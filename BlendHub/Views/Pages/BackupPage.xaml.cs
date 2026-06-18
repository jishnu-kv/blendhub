using BlendHub.Models;
using BlendHub.Services;
using BlendHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class BackupPage : Page
    {
        public BackupViewModel ViewModel { get; }

        public BackupPage()
        {
            this.InitializeComponent();
            ViewModel = new BackupViewModel(GroupedBackupItems);
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
            await ViewModel.LoadVersionsAsync();
            ViewModel.RefreshManageBackupsList();

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
                if (VersionComboBox.SelectedItem is BlenderVersionInfo info)
                {
                    await ViewModel.RefreshItemsAsync(info.ConfigPath);
                }
                ViewModel.RefreshManageBackupsList();
            }
        }

        private async void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionComboBox.SelectedItem is BlenderVersionInfo info)
            {
                VersionPathText.Text = info.ConfigPath;
                await ViewModel.RefreshItemsAsync(info.ConfigPath);
            }
        }

        private async void StartBackupButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartBackupCmdCommand.ExecuteAsync(this.XamlRoot);
            UpdateInfoBarPanelMargin();
        }

        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string backupName)
            {
                ViewModel.OpenBackupFolderCmdCommand.Execute(backupName);
            }
        }

        private void InfoItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string backupName)
            {
                var vms = ViewModel.GetBackupItemsForPath(backupName);
                if (vms.Count > 0)
                {
                    ItemsInfoTeachingTip.DataContext = vms;
                    ItemsInfoTeachingTip.Target = (FrameworkElement)sender;
                    ItemsInfoTeachingTip.IsOpen = true;
                }
            }
        }

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string backupName)
            {
                await ViewModel.DeleteBackupCmdCommand.ExecuteAsync(new DeleteBackupParams(this.XamlRoot, backupName));
                UpdateInfoBarPanelMargin();
            }
        }

        public static string GetOpenBackupAutomationId(string backupName) => $"OpenBackup_{backupName.Replace('-', '_').Replace('.', '_')}";
        public static string GetDeleteBackupAutomationId(string backupName) => $"DeleteBackup_{backupName.Replace('-', '_').Replace('.', '_')}";
        public static string GetViewBackupAutomationId(string backupName) => $"ViewBackup_{backupName.Replace('-', '_').Replace('.', '_')}";

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
