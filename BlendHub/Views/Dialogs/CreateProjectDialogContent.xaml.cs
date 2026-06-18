using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BlendHub.Dialogs
{
    public sealed partial class CreateProjectDialogContent : UserControl
    {
        private BlenderSettingsService _blenderService = new BlenderSettingsService();
        public ObservableCollection<ProjectFolder> Folders { get; } = new ObservableCollection<ProjectFolder>();
        public ObservableCollection<FileLauncher> FileLaunchers { get; } = new ObservableCollection<FileLauncher>();

        public event EventHandler? ValidationChanged;

        public CreateProjectDialogContent()
        {
            this.InitializeComponent();

            // Populate Blender versions
            var versions = _blenderService.GetInstalledVersions();
            BlenderVersionComboBox.ItemsSource = versions;
            if (versions.Count > 0)
            {
                BlenderVersionComboBox.SelectedIndex = 0;
            }

            // Populate Presets
            LoadPresets();

            // Pre-load global launchers from settings
            LoadGlobalLaunchers();

            FolderNamesItemsControl.ItemsSource = Folders;
            FileLaunchersItemsControl.ItemsSource = FileLaunchers;
        }

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

            PopulateFoldersFromSelectedPreset();
            _isPresetChanging = false;
        }

        private void PopulateFoldersFromSelectedPreset()
        {
            Folders.Clear();
            var settings = AppSettingsService.Instance.Settings;
            var presetName = PresetComboBox.SelectedItem as string ?? settings.SelectedPreset;
            if (settings.ProjectPresets.TryGetValue(presetName, out var folders))
            {
                foreach (var folderName in folders)
                {
                    AddFolder(folderName);
                }
            }
            RaiseValidationChanged();
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPresetChanging) return;
            PopulateFoldersFromSelectedPreset();
        }

        private void LoadGlobalLaunchers()
        {
            var globalLaunchers = AppSettingsService.Instance.Settings.DefaultLaunchers;
            foreach (var kvp in globalLaunchers)
            {
                FileLaunchers.Add(new FileLauncher
                {
                    Extension = kvp.Key,
                    ProgramPath = kvp.Value,
                    ProgramName = !string.IsNullOrEmpty(kvp.Value) ? System.IO.Path.GetFileNameWithoutExtension(kvp.Value) : ""
                });
            }
        }

        public string ProjectName => ProjectNameTextBox.Text;
        public string FileName => FileNameTextBox.Text;
        public string ProjectLocation => ProjectLocationTextBox.Text;
        public BlenderVersionInfo? SelectedVersion => BlenderVersionComboBox.SelectedItem as BlenderVersionInfo;
        public bool AutoUpdatePrimaryBlend => AutoUpdatePrimaryBlendToggle.IsOn;

        // Public setters for edit mode
        public void SetProjectName(string name) => ProjectNameTextBox.Text = name;
        public void SetFileName(string name) => FileNameTextBox.Text = name;
        public void SetProjectLocation(string location) => ProjectLocationTextBox.Text = location;
        public void SetSelectedVersion(BlenderVersionInfo? version) => BlenderVersionComboBox.SelectedItem = version;

        // Public getter for versions lookup
        public System.Collections.Generic.IEnumerable<BlenderVersionInfo>? GetBlenderVersions()
            => BlenderVersionComboBox.ItemsSource as System.Collections.Generic.IEnumerable<BlenderVersionInfo>;

        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProjectName)) { return ShowWarning("Project Name is required."); }
                if (string.IsNullOrWhiteSpace(FileName)) { return ShowWarning("File Name is required."); }
                if (SelectedVersion == null) { return ShowWarning("Please select a Blender version."); }
                if (string.IsNullOrWhiteSpace(ProjectLocation)) { return ShowWarning("Project Location is required."); }

                foreach (var folder in Folders)
                {
                    if (folder.IsSelected && string.IsNullOrWhiteSpace(folder.Name))
                    {
                        return ShowWarning("Folder name cannot be empty. Please enter a name or deselect it.");
                    }
                }

                if (WarningInfoBar != null)
                {
                    WarningInfoBar.IsOpen = false;
                    WarningInfoBar.Visibility = Visibility.Collapsed;
                }
                return true;
            }
        }

        private bool ShowWarning(string message)
        {
            if (WarningInfoBar != null)
            {
                WarningInfoBar.Message = message;
                WarningInfoBar.IsOpen = true;
                WarningInfoBar.Visibility = Visibility.Visible;
            }
            return false;
        }

        private void RaiseValidationChanged()
        {
            // Forces evaluation of IsValid
            bool valid = IsValid;
            ValidationChanged?.Invoke(this, EventArgs.Empty);
        }

        // Event handler for TextBox.TextChanged
        private void Validation_Changed(object sender, TextChangedEventArgs e)
        {
            RaiseValidationChanged();
        }

        // Event handler for ComboBox.SelectionChanged
        private void Validation_Changed(object sender, SelectionChangedEventArgs e)
        {
            RaiseValidationChanged();
        }

        private void AddFolder(string name = "")
        {
            var folder = new ProjectFolder($"Folder {Folders.Count + 1}:", name);
            folder.PropertyChanged += Folder_PropertyChanged;
            Folders.Add(folder);
            RaiseValidationChanged();
        }

        private void Folder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectFolder.Name) || e.PropertyName == nameof(ProjectFolder.IsSelected))
            {
                RaiseValidationChanged();
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            AddFolder();
        }

        private void DeleteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ProjectFolder folder)
            {
                folder.PropertyChanged -= Folder_PropertyChanged;
                Folders.Remove(folder);
                UpdateLabels();
                RaiseValidationChanged();
            }
        }

        private void UpdateLabels()
        {
            for (int i = 0; i < Folders.Count; i++)
            {
                Folders[i].Label = $"Folder {i + 1}:";
            }
        }

        // --- File Launchers ---
        private void AddLauncher_Click(object sender, RoutedEventArgs e)
        {
            FileLaunchers.Add(new FileLauncher());
        }

        private void DeleteLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FileLauncher launcher)
            {
                FileLaunchers.Remove(launcher);
            }
        }

        private async void BrowseLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not FileLauncher launcher) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");

            BlendHub.Helpers.WindowHelper.InitializeWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                launcher.ProgramPath = file.Path;
                launcher.ProgramName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
            }
        }

        // --- Folder Picker ---
        private async void BrowseLocation_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            BlendHub.Helpers.WindowHelper.InitializeWithWindow(folderPicker);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                ProjectLocationTextBox.Text = folder.Path;
                RaiseValidationChanged();
            }
        }
    }
}
