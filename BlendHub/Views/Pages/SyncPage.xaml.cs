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
    public sealed partial class SyncPage : Page
    {
        public SyncViewModel ViewModel { get; }

        public SyncPage()
        {
            this.InitializeComponent();
            ViewModel = new SyncViewModel(GroupedSyncItems);
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
                if (ViewModel.SelectedSource is BlenderVersionInfo sourceInfo)
                {
                    await ViewModel.RefreshItemsAsync(sourceInfo.ConfigPath);
                    ViewModel.RefreshTargetVersions(sourceInfo.Version);
                }
            }
        }

        private async void SourceVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceVersionComboBox.SelectedItem is BlenderVersionInfo sourceInfo)
            {
                await ViewModel.RefreshItemsAsync(sourceInfo.ConfigPath);
                ViewModel.RefreshTargetVersions(sourceInfo.Version);
            }
            ViewModel.ValidateSyncState();
        }

        private async void StartSyncButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartSyncCmdCommand.ExecuteAsync(this.XamlRoot);
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
