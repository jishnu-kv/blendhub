using System;
using System.Linq;
using System.Threading.Tasks;
using BlendHub.Models;
using BlendHub.Services;
using BlendHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; } = new SettingsViewModel();

        public SettingsPage()
        {
            this.InitializeComponent();

            LoadGeneralSettings();
            LoadCurrentTheme();
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadBlenderInstallationsAsync();
        }

        private void LoadGeneralSettings()
        {
            BackupLocationTextBox.Text = ViewModel.BackupDirectory;
            AutoDetectVersionToggle.IsOn = ViewModel.AutoDetectVersion;
            ExpandFoldersToggle.IsOn = ViewModel.ExpandFoldersByDefault;
            FilterNestedBlendFilesToggle.IsOn = ViewModel.FilterNestedBlendFiles;
            CategorizeProjectsToggle.IsOn = ViewModel.CategorizeProjectsByProgress;

            // Set Default Page selection
            foreach (ComboBoxItem item in DefaultPageComboBox.Items)
            {
                if (item.Tag?.ToString() == AppSettingsService.Instance.Settings.DefaultPage)
                {
                    DefaultPageComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadCurrentTheme()
        {
            var currentTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;
            ThemeComboBox.SelectedIndex = currentTheme switch
            {
                ElementTheme.Default => 0,
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0
            };
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ElementTheme theme = tag switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
 
                if (App.MainWindow.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = theme;
                }
            }
        }

        private void DefaultPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DefaultPageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                AppSettingsService.Instance.Settings.DefaultPage = tag;
                AppSettingsService.Instance.Save();
            }
        }

        private void AutoDetectVersionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ViewModel.AutoDetectVersion = AutoDetectVersionToggle.IsOn;
        }

        private void ExpandFoldersToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ViewModel.ExpandFoldersByDefault = ExpandFoldersToggle.IsOn;
        }

        private void FilterNestedBlendFilesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ViewModel.FilterNestedBlendFiles = FilterNestedBlendFilesToggle.IsOn;
        }

        private void CategorizeProjectsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ViewModel.CategorizeProjectsByProgress = CategorizeProjectsToggle.IsOn;
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is string selectedPreset)
            {
                ViewModel.SelectedPreset = selectedPreset;
            }
        }

        private async void AddPresetBtn_Click(object sender, RoutedEventArgs e)
        {
            var textBox = new TextBox
            {
                PlaceholderText = "Preset name",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var dialog = new ContentDialog
            {
                Title = "New Preset",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children = {
                        new TextBlock { Text = "Enter a name for the new preset:" },
                        textBox
                    }
                },
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                PrimaryButtonStyle = Application.Current.Resources["AccentButtonStyle"] as Style,
                CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var name = textBox.Text?.Trim();
                if (string.IsNullOrEmpty(name)) return;

                if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Invalid Name",
                        Content = "Preset name cannot be 'Default'.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                        CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                if (ViewModel.Presets.Contains(name))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Duplicate Name",
                        Content = $"A preset named '{name}' already exists.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                        CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                // Copy current list of folders as a starting point
                var currentFolders = ViewModel.DefaultFolders.Select(f => f.Name).ToList();
                var settings = AppSettingsService.Instance.Settings;
                settings.ProjectPresets[name] = currentFolders;
                settings.SelectedPreset = name;
                AppSettingsService.Instance.Save();

                ViewModel.LoadPresets();
                ViewModel.SelectedPreset = name;
            }
        }

        private async void DeletePresetBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedPreset = ViewModel.SelectedPreset;
            if (selectedPreset == "Default")
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Cannot Delete",
                    Content = "The 'Default' preset cannot be deleted.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                    CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                };
                await errorDialog.ShowAsync();
                return;
            }

            var confirmDialog = new ContentDialog
            {
                Title = "Delete Preset",
                Content = $"Are you sure you want to delete the preset '{selectedPreset}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                PrimaryButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style,
                CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                AppSettingsService.Instance.Settings.ProjectPresets.Remove(selectedPreset);
                AppSettingsService.Instance.Settings.SelectedPreset = "Default";
                AppSettingsService.Instance.Save();

                ViewModel.LoadPresets();
                ViewModel.SelectedPreset = "Default";
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddFolder();
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ProjectFolder folder)
            {
                // Force focus away from the list before removal
                this.Focus(FocusState.Programmatic);
                ViewModel.RemoveFolder(folder);
            }
        }

        private void FolderNameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                ViewModel.SaveDefaultFolders();
            }
        }

        private void FolderNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveDefaultFolders();
        }

        private async void Grip_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProjectFolder folder)
            {
                args.Data.Properties["Folder"] = folder;
                args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;

                // Find the parent SettingsCard to use as the drag visual preview
                DependencyObject parent = fe;
                while (parent != null && parent is not CommunityToolkit.WinUI.Controls.SettingsCard)
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parent is CommunityToolkit.WinUI.Controls.SettingsCard settingsCard)
                {
                    var deferral = args.GetDeferral();
                    try
                    {
                        var renderTargetBitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                        await renderTargetBitmap.RenderAsync(settingsCard);
                        var pixels = await renderTargetBitmap.GetPixelsAsync();
                        var width = renderTargetBitmap.PixelWidth;
                        var height = renderTargetBitmap.PixelHeight;

                        if (width > 0 && height > 0)
                        {
                            var softwareBitmap = new Windows.Graphics.Imaging.SoftwareBitmap(
                                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                                width,
                                height,
                                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

                            softwareBitmap.CopyFromBuffer(pixels);

                            // Calculate anchor point so that the cursor stays perfectly over the grip icon
                            var transform = fe.TransformToVisual(settingsCard);
                            var anchorPoint = transform.TransformPoint(new Windows.Foundation.Point(fe.ActualWidth / 2, fe.ActualHeight / 2));

                            args.DragUI.SetContentFromSoftwareBitmap(softwareBitmap, anchorPoint);
                        }
                    }
                    catch
                    {
                        // Fallback silently if rendering fails
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                }
            }
        }

        private void SettingsCard_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Properties.ContainsKey("Folder"))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.DragUIOverride.IsCaptionVisible = false;
                e.DragUIOverride.IsGlyphVisible = false;
                e.Handled = true;
            }
        }

        private void SettingsCard_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProjectFolder targetFolder &&
                e.DataView.Properties.TryGetValue("Folder", out var sourceObj) && sourceObj is ProjectFolder sourceFolder)
            {
                int sourceIndex = ViewModel.DefaultFolders.IndexOf(sourceFolder);
                int targetIndex = ViewModel.DefaultFolders.IndexOf(targetFolder);

                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    ViewModel.DefaultFolders.Move(sourceIndex, targetIndex);
                    ViewModel.UpdateFolderLabels();
                    ViewModel.SaveDefaultFolders();
                }
                e.Handled = true;
            }
        }

        private void AddLauncher_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddLauncher();
        }

        private void RemoveLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FileLauncher launcher)
            {
                ViewModel.RemoveLauncher(launcher);
            }
        }

        private void ExtensionTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                ViewModel.SaveLaunchers();
            }
        }

        private void ExtensionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveLaunchers();
        }

        private void ContentRoot_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            this.Focus(FocusState.Programmatic);
        }

        private async void BrowseLauncherProgram_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not FileLauncher launcher) return;

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                launcher.ProgramPath = file.Path;
                launcher.ProgramName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                ViewModel.SaveLaunchers();
            }
        }

        private async void BrowseBackupLocation_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                BackupLocationTextBox.Text = folder.Path;
                ViewModel.BackupDirectory = folder.Path;
            }
        }

        private async void AddScanFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                await ViewModel.AddCustomScanFolderAsync(folder.Path);
            }
        }

        private async void RemoveScanFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ScanFolderInfo info)
            {
                await ViewModel.RemoveCustomScanFolderAsync(info);
            }
        }

        private async void AddCustomBlender_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.AddCustomBlenderAsync(file.Path);
            }
        }

        private async void RemoveBlenderInstallation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is BlenderInstallationViewModel vm)
            {
                await ViewModel.RemoveBlenderInstallationAsync(vm);
            }
        }

        private void LaunchArguments_LostFocus(object sender, RoutedEventArgs e)
        {
            // Binding is TwoWay, loss of focus ensures any edited text gets applied.
        }

        private void LaunchArguments_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private async void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("JSON Files", new System.Collections.Generic.List<string>() { ".json" });
            savePicker.SuggestedFileName = "blendhub_settings";

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(savePicker, hwnd);
            }

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    ViewModel.ExportSettings(file.Path);

                    var successDialog = new ContentDialog
                    {
                        Title = "Export Successful",
                        Content = "Your BlendHub settings have been exported successfully.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                        CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                    };
                    await successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Export Failed",
                        Content = $"An error occurred while exporting settings: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                        CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var openPicker = new FileOpenPicker();
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".json");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(openPicker, hwnd);
            }

            var file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    await ViewModel.ImportSettingsAsync(file.Path);

                    // Refresh UI bindings that are not direct VM bindings
                    LoadGeneralSettings();
                    LoadCurrentTheme();

                    var successDialog = new ContentDialog
                    {
                        Title = "Import Successful",
                        Content = "Your BlendHub settings have been imported successfully.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                        CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                    };
                    await successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Import Failed",
                        Content = $"An error occurred while importing settings: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                        CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }
    }
}
