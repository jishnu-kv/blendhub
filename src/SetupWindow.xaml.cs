using BlendHub.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;

namespace BlendHub
{
    public sealed partial class SetupWindow : Window
    {
        private AppSettingsService _settingsService = AppSettingsService.Instance;
        private BlenderSettingsService _blenderService = new BlenderSettingsService();
        private List<BlenderVersionInfo> _detectedBlenders = new List<BlenderVersionInfo>();
        private ElementTheme _selectedTheme = ElementTheme.Default;
        private int _currentStep = 1;
        private const int TotalSteps = 4;

        public string Username
        {
            get { return _settingsService.Settings.UserName; }
            set { _settingsService.Settings.UserName = value; }
        }

        public string BackupLocation
        {
            get { return _settingsService.Settings.BackupDirectory; }
            set { _settingsService.Settings.BackupDirectory = value; }
        }

        public SetupWindow()
        {
            this.InitializeComponent();
            SetWindowProperties();
            LoadDetectedBlenders();
            UpdateStepDisplay();
        }

        private void UpdateStepDisplay()
        {
            // Hide all step panels
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Collapsed;
            Step4Panel.Visibility = Visibility.Collapsed;

            // Show current step panel
            switch (_currentStep)
            {
                case 1:
                    Step1Panel.Visibility = Visibility.Visible;
                    StepTitleText.Text = "Step 1 of 4: Your Name";
                    NextButton.Content = "Next";
                    break;
                case 2:
                    Step2Panel.Visibility = Visibility.Visible;
                    StepTitleText.Text = "Step 2 of 4: Choose Your Theme";
                    NextButton.Content = "Next";
                    break;
                case 3:
                    Step3Panel.Visibility = Visibility.Visible;
                    StepTitleText.Text = "Step 3 of 4: Backup Location";
                    NextButton.Content = "Next";
                    break;
                case 4:
                    Step4Panel.Visibility = Visibility.Visible;
                    StepTitleText.Text = "Step 4 of 4: Blender Installations";
                    NextButton.Content = "Complete Setup";
                    break;
            }

            // Update progress bar
            ProgressIndicator.Value = (_currentStep * 100) / TotalSteps;

            // Update navigation buttons
            PreviousButton.IsEnabled = _currentStep > 1;
        }

        private void SetWindowProperties()
        {
            this.Title = "BlendHub Setup";
            this.ExtendsContentIntoTitleBar = true;

            // Get the AppWindow
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                // Set window size
                appWindow.Resize(new Windows.Graphics.SizeInt32(700, 700));

                // Set window icon
                appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));

                // Configure title bar
                appWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

                // Create and configure OverlappedPresenter
                OverlappedPresenter presenter = OverlappedPresenter.Create();
                presenter.PreferredMinimumWidth = 500;
                presenter.PreferredMinimumHeight = 600;
                presenter.PreferredMaximumWidth = 800;
                presenter.PreferredMaximumHeight = 800;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = true;
                presenter.IsResizable = true;

                appWindow.SetPresenter(presenter);

                // Center the window on screen
                var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    var centerX = (displayArea.WorkArea.Width - 700) / 2;
                    var centerY = (displayArea.WorkArea.Height - 700) / 2;
                    appWindow.Move(new Windows.Graphics.PointInt32((int)centerX, (int)centerY));
                }
            }
        }

        private void LoadDetectedBlenders()
        {
            try
            {
                _detectedBlenders = _blenderService.GetInstalledVersions();

                foreach (var blender in _detectedBlenders)
                {
                    var checkbox = new CheckBox
                    {
                        Content = blender.DisplayName,
                        IsChecked = true,
                        Tag = blender,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    DetectedBlendersPanel.Children.Add(checkbox);
                }

                if (_detectedBlenders.Count == 0)
                {
                    var noBlendersText = new TextBlock
                    {
                        Text = "No Blender installations detected. You can add them later in Settings.",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    DetectedBlendersPanel.Children.Add(noBlendersText);
                }
            }
            catch
            {
                var errorText = new TextBlock
                {
                    Text = "Unable to detect Blender installations. You can add them later in Settings.",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Margin = new Thickness(0, 4, 0, 0)
                };
                DetectedBlendersPanel.Children.Add(errorText);
            }
        }

        private void SystemThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            _selectedTheme = ElementTheme.Default;
        }

        private void LightThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            _selectedTheme = ElementTheme.Light;
        }

        private void DarkThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            _selectedTheme = ElementTheme.Dark;
        }

        private async void BrowseBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            // Get the window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                BackupLocation = folder.Path;
            }
        }

        private async void AddBlenderButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();
            filePicker.FileTypeFilter.Add(".exe");

            // Get the window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            Windows.Storage.StorageFile file = await filePicker.PickSingleFileAsync();
            if (file != null && file.Name.Equals("blender.exe", StringComparison.OrdinalIgnoreCase))
            {
                var blenderPath = file.Path;

                // Add to custom blender paths
                if (!_settingsService.Settings.CustomBlenderPaths.Contains(blenderPath))
                {
                    _settingsService.Settings.CustomBlenderPaths.Add(blenderPath);

                    // Add checkbox for this blender
                    var checkbox = new CheckBox
                    {
                        Content = $"Custom: {Path.GetDirectoryName(blenderPath)}",
                        IsChecked = true,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    DetectedBlendersPanel.Children.Add(checkbox);
                }
            }
            else if (file != null)
            {
                // Show error dialog
                var errorDialog = new ContentDialog
                {
                    Title = "Invalid File",
                    Content = "Please select blender.exe file.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < TotalSteps)
            {
                _currentStep++;
                UpdateStepDisplay();
            }
            else
            {
                // Complete setup
                CompleteSetup();
            }
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepDisplay();
            }
        }

        private void CompleteSetup()
        {
            // Save settings
            _settingsService.Save();

            // Apply theme to main window when it opens
            App.SelectedTheme = _selectedTheme;

            // Mark setup as completed
            _settingsService.Settings.IsFirstRun = false;
            _settingsService.Save();

            // Open main app and close setup window
            OpenMainWindow();
            this.Close();
        }

        private void SkipSetupButton_Click(object sender, RoutedEventArgs e)
        {
            // Mark setup as completed
            _settingsService.Settings.IsFirstRun = false;
            _settingsService.Save();

            // Open main app and close setup window
            OpenMainWindow();
            this.Close();
        }

        private void OpenMainWindow()
        {
            // Set the main window as the app's main window
            App.MainWindow = new MainWindow();
            App.MainWindow.Activate();
        }
    }
}
