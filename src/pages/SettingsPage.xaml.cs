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
        public ObservableCollection<CustomBlenderInfo> CustomBlenders { get; } = new ObservableCollection<CustomBlenderInfo>();

        public SettingsPage()
        {
            this.InitializeComponent();

            LoadGeneralSettings();
            LoadCurrentTheme();
            LoadLaunchers();
            LoadDefaultFolders();
            LoadCustomBlenders();
        }

        private void LoadGeneralSettings()
        {
            var settings = AppSettingsService.Instance.Settings;
            BackupLocationTextBox.Text = settings.BackupDirectory;
            UserNameTextBox.Text = settings.UserName;
            AutoDetectVersionToggle.IsOn = settings.AutoDetectBlenderVersion;
            ExpandFoldersToggle.IsOn = settings.ExpandFoldersByDefault;

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

        private void UserNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveUserName();
        }

        private void UserNameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                SaveUserName();
            }
        }

        private void SaveUserName()
        {
            AppSettingsService.Instance.Settings.UserName = UserNameTextBox.Text;
            AppSettingsService.Instance.Save();
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

        // --- Default Folders ---
        private void LoadDefaultFolders()
        {
            DefaultFolders.Clear();
            var folders = AppSettingsService.Instance.Settings.DefaultFolders;
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
            service.Settings.DefaultFolders = DefaultFolders
                .Select(f => f.Name)
                .ToList();
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

        // --- Custom Blender Installations ---
        private void LoadCustomBlenders()
        {
            CustomBlenders.Clear();
            var paths = AppSettingsService.Instance.Settings.CustomBlenderPaths;
            foreach (var path in paths)
            {
                var info = new CustomBlenderInfo(path);
                info.PropertyChanged += CustomBlender_PropertyChanged;
                CustomBlenders.Add(info);
            }
        }

        private void SaveCustomBlenders()
        {
            AppSettingsService.Instance.Settings.CustomBlenderPaths = CustomBlenders
                .Select(b => b.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            AppSettingsService.Instance.Save();
        }

        private void CustomBlender_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SaveCustomBlenders();
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
                var info = new CustomBlenderInfo(file.Path);
                info.PropertyChanged += CustomBlender_PropertyChanged;
                CustomBlenders.Add(info);
                SaveCustomBlenders();
            }
        }

        private async void BrowseCustomBlender_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not CustomBlenderInfo info) return;

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
                info.Path = file.Path;
                SaveCustomBlenders();
            }
        }

        private void RemoveCustomBlender_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CustomBlenderInfo info)
            {
                info.PropertyChanged -= CustomBlender_PropertyChanged;
                CustomBlenders.Remove(info);
                SaveCustomBlenders();
            }
        }

        private async void PrivacyPolicy_Click(object sender, RoutedEventArgs e)
        {
            await BlendHub.Dialogs.LegalDialogs.ShowPrivacyPolicyAsync(this.XamlRoot);
        }

        private async void TermsOfService_Click(object sender, RoutedEventArgs e)
        {
            await BlendHub.Dialogs.LegalDialogs.ShowTermsOfServiceAsync(this.XamlRoot);
        }
    }
}
