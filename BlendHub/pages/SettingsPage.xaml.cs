using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public ObservableCollection<FileLauncher> Launchers { get; } = new ObservableCollection<FileLauncher>();
        public ObservableCollection<ProjectFolder> DefaultFolders { get; } = new ObservableCollection<ProjectFolder>();
        public ObservableCollection<BlenderInstallationViewModel> BlenderInstallations { get; } = new ObservableCollection<BlenderInstallationViewModel>();
        public ObservableCollection<ScanFolderInfo> CustomScanFolders { get; } = new ObservableCollection<ScanFolderInfo>();

        public SettingsPage()
        {
            this.InitializeComponent();

            LoadGeneralSettings();
            LoadCurrentTheme();
            LoadLaunchers();
            LoadPresets();
            LoadDefaultFolders();
            LoadBlenderInstallations();
            LoadCustomScanFolders();
        }

        private void LoadGeneralSettings()
        {
            var settings = AppSettingsService.Instance.Settings;
            BackupLocationTextBox.Text = settings.BackupDirectory;
            AutoDetectVersionToggle.IsOn = settings.AutoDetectBlenderVersion;
            ExpandFoldersToggle.IsOn = settings.ExpandFoldersByDefault;
            FilterNestedBlendFilesToggle.IsOn = settings.FilterNestedBlendFiles;

            // Set Default Page selection
            foreach (ComboBoxItem item in DefaultPageComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.DefaultPage)
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
            AppSettingsService.Instance.Settings.AutoDetectBlenderVersion = AutoDetectVersionToggle.IsOn;
            AppSettingsService.Instance.Save();
        }

        private void ExpandFoldersToggle_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Settings.ExpandFoldersByDefault = ExpandFoldersToggle.IsOn;
            AppSettingsService.Instance.Save();
        }

        private void FilterNestedBlendFilesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Settings.FilterNestedBlendFiles = FilterNestedBlendFilesToggle.IsOn;
            AppSettingsService.Instance.Save();
        }

        // --- Default Folders ---
        private bool _isPresetChanging = false;

        private void LoadPresets()
        {
            _isPresetChanging = true;
            PresetComboBox.Items.Clear();
            var settings = AppSettingsService.Instance.Settings;
            foreach (var preset in settings.ProjectPresets.Keys)
            {
                PresetComboBox.Items.Add(preset);
            }
            PresetComboBox.SelectedItem = settings.SelectedPreset;
            _isPresetChanging = false;
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPresetChanging) return;
            if (PresetComboBox.SelectedItem is string selectedPreset)
            {
                AppSettingsService.Instance.Settings.SelectedPreset = selectedPreset;
                AppSettingsService.Instance.Save();
                LoadDefaultFolders();
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

                var settings = AppSettingsService.Instance.Settings;
                if (settings.ProjectPresets.ContainsKey(name))
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
                var currentFolders = DefaultFolders.Select(f => f.Name).ToList();
                settings.ProjectPresets[name] = currentFolders;
                settings.SelectedPreset = name;
                AppSettingsService.Instance.Save();

                LoadPresets();
                LoadDefaultFolders();
            }
        }

        private async void DeletePresetBtn_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsService.Instance.Settings;
            if (settings.SelectedPreset == "Default")
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
                Content = $"Are you sure you want to delete the preset '{settings.SelectedPreset}'?",
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
                settings.ProjectPresets.Remove(settings.SelectedPreset);
                settings.SelectedPreset = "Default";
                AppSettingsService.Instance.Save();

                LoadPresets();
                LoadDefaultFolders();
            }
        }

        private void LoadDefaultFolders()
        {
            DefaultFolders.Clear();
            var settings = AppSettingsService.Instance.Settings;
            if (!settings.ProjectPresets.TryGetValue(settings.SelectedPreset, out var folders))
            {
                folders = settings.ProjectPresets["Default"];
                settings.SelectedPreset = "Default";
            }
            for (int i = 0; i < folders.Count; i++)
            {
                var folder = new ProjectFolder($"Folder {i + 1}:", folders[i]);
                folder.PropertyChanged += DefaultFolder_PropertyChanged;
                DefaultFolders.Add(folder);
            }
        }

        private void SaveDefaultFolders()
        {
            var service = AppSettingsService.Instance;
            var folders = DefaultFolders.Select(f => f.Name).ToList();
            service.Settings.ProjectPresets[service.Settings.SelectedPreset] = folders;
            if (service.Settings.SelectedPreset == "Default")
            {
                service.Settings.DefaultFolders = folders;
            }
            service.Save();
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = new ProjectFolder($"Folder {DefaultFolders.Count + 1}:", "");
            folder.PropertyChanged += DefaultFolder_PropertyChanged;
            DefaultFolders.Add(folder);
            SaveDefaultFolders();
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ProjectFolder folder)
            {
                // Force focus away from the list before removal
                this.Focus(FocusState.Programmatic);

                folder.PropertyChanged -= DefaultFolder_PropertyChanged;
                DefaultFolders.Remove(folder);
                UpdateFolderLabels();
                SaveDefaultFolders();
            }
        }

        private void UpdateFolderLabels()
        {
            for (int i = 0; i < DefaultFolders.Count; i++)
            {
                DefaultFolders[i].Label = $"Folder {i + 1}:";
            }
        }

        private void DefaultFolder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectFolder.Name))
            {
                SaveDefaultFolders();
            }
        }

        private void FolderNameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                SaveDefaultFolders();
            }
        }

        private void FolderNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveDefaultFolders();
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
                int sourceIndex = DefaultFolders.IndexOf(sourceFolder);
                int targetIndex = DefaultFolders.IndexOf(targetFolder);

                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    DefaultFolders.Move(sourceIndex, targetIndex);
                    UpdateFolderLabels();
                    SaveDefaultFolders();
                }
                e.Handled = true;
            }
        }

        // --- File Launchers ---
        private void LoadLaunchers()
        {
            Launchers.Clear();
            var defaultLaunchers = AppSettingsService.Instance.Settings.DefaultLaunchers;
            foreach (var kvp in defaultLaunchers)
            {
                Launchers.Add(new FileLauncher
                {
                    Extension = kvp.Key,
                    ProgramPath = kvp.Value,
                    ProgramName = System.IO.Path.GetFileNameWithoutExtension(kvp.Value)
                });
            }

            // Add default .psd entry if no launchers exist
            if (Launchers.Count == 0)
            {
                Launchers.Add(new FileLauncher
                {
                    Extension = ".psd",
                    ProgramPath = "",
                    ProgramName = ""
                });
            }
        }

        private void SaveLaunchers()
        {
            var service = AppSettingsService.Instance;
            service.Settings.DefaultLaunchers.Clear();
            foreach (var launcher in Launchers)
            {
                if (!string.IsNullOrWhiteSpace(launcher.Extension))
                {
                    string ext = launcher.Extension.StartsWith(".") ? launcher.Extension : "." + launcher.Extension;
                    service.Settings.DefaultLaunchers[ext.ToLowerInvariant()] = launcher.ProgramPath ?? "";
                }
            }
            service.Save();
        }

        private void AddLauncher_Click(object sender, RoutedEventArgs e)
        {
            Launchers.Add(new FileLauncher());
        }

        private void RemoveLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FileLauncher launcher)
            {
                Launchers.Remove(launcher);
                SaveLaunchers();
            }
        }

        private void ExtensionTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                SaveLaunchers();
            }
        }

        private void ExtensionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveLaunchers();
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
                SaveLaunchers();
            }
        }

        private async void BrowseBackupLocation_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                BackupLocationTextBox.Text = folder.Path;
                AppSettingsService.Instance.Settings.BackupDirectory = folder.Path;
                AppSettingsService.Instance.Save();
            }
        }

        // --- Custom Blender & Scanning Installations ---
        private void LoadBlenderInstallations()
        {
            foreach (var vm in BlenderInstallations)
            {
                vm.PropertyChanged -= BlenderInstallation_PropertyChanged;
            }
            BlenderInstallations.Clear();

            var service = new BlenderSettingsService();
            var allVersions = service.GetInstalledVersions(includeHidden: true);
            var settings = AppSettingsService.Instance.Settings;

            foreach (var v in allVersions)
            {
                bool isCustom = settings.CustomBlenderPaths.Contains(v.ExecutablePath);
                bool isVisible = !settings.HiddenBlenderPaths.Contains(v.ExecutablePath);
                string? args = string.Empty;
                if (settings.BlenderLaunchArgs != null)
                {
                    settings.BlenderLaunchArgs.TryGetValue(v.ExecutablePath, out args);
                }

                var vm = new BlenderInstallationViewModel
                {
                    DisplayName = v.DisplayName,
                    ExecutablePath = v.ExecutablePath,
                    Version = v.Version,
                    IsCustom = isCustom,
                    IsVisible = isVisible,
                    LaunchArguments = args ?? string.Empty
                };

                vm.PropertyChanged += BlenderInstallation_PropertyChanged;
                BlenderInstallations.Add(vm);
            }
        }

        private void BlenderInstallation_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not BlenderInstallationViewModel vm) return;

            var settings = AppSettingsService.Instance.Settings;
            if (e.PropertyName == nameof(BlenderInstallationViewModel.IsVisible))
            {
                if (vm.IsVisible)
                {
                    settings.HiddenBlenderPaths.Remove(vm.ExecutablePath);
                }
                else
                {
                    if (!settings.HiddenBlenderPaths.Contains(vm.ExecutablePath))
                    {
                        settings.HiddenBlenderPaths.Add(vm.ExecutablePath);
                    }
                }
                AppSettingsService.Instance.Save();
            }
            else if (e.PropertyName == nameof(BlenderInstallationViewModel.LaunchArguments))
            {
                if (settings.BlenderLaunchArgs == null)
                {
                    settings.BlenderLaunchArgs = new System.Collections.Generic.Dictionary<string, string>();
                }
                settings.BlenderLaunchArgs[vm.ExecutablePath] = vm.LaunchArguments;
                AppSettingsService.Instance.Save();
            }
        }

        private void LoadCustomScanFolders()
        {
            foreach (var folder in CustomScanFolders)
            {
                folder.PropertyChanged -= ScanFolder_PropertyChanged;
            }
            CustomScanFolders.Clear();

            var paths = AppSettingsService.Instance.Settings.CustomScanFolders;
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    var info = new ScanFolderInfo(path);
                    info.PropertyChanged += ScanFolder_PropertyChanged;
                    CustomScanFolders.Add(info);
                }
            }
        }

        private void SaveCustomScanFolders()
        {
            AppSettingsService.Instance.Settings.CustomScanFolders = CustomScanFolders
                .Select(f => f.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            AppSettingsService.Instance.Save();

            LoadBlenderInstallations();
        }

        private void ScanFolder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SaveCustomScanFolders();
        }

        private async void AddScanFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindow;
            if (window != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                if (!CustomScanFolders.Any(f => f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    var info = new ScanFolderInfo(folder.Path);
                    info.PropertyChanged += ScanFolder_PropertyChanged;
                    CustomScanFolders.Add(info);
                    SaveCustomScanFolders();
                }
            }
        }

        private void RemoveScanFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ScanFolderInfo info)
            {
                info.PropertyChanged -= ScanFolder_PropertyChanged;
                CustomScanFolders.Remove(info);
                SaveCustomScanFolders();
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
                var settings = AppSettingsService.Instance.Settings;
                if (!settings.CustomBlenderPaths.Contains(file.Path))
                {
                    settings.CustomBlenderPaths.Add(file.Path);
                    AppSettingsService.Instance.Save();
                    LoadBlenderInstallations();
                }
            }
        }

        private void RemoveBlenderInstallation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is BlenderInstallationViewModel vm)
            {
                var settings = AppSettingsService.Instance.Settings;
                settings.CustomBlenderPaths.Remove(vm.ExecutablePath);
                settings.HiddenBlenderPaths.Remove(vm.ExecutablePath);
                if (settings.BlenderLaunchArgs != null)
                {
                    settings.BlenderLaunchArgs.Remove(vm.ExecutablePath);
                }
                AppSettingsService.Instance.Save();
                LoadBlenderInstallations();
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
    }
}
