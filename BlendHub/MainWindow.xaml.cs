using BlendHub.Pages;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using BlendHub.ReferenceBoard;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinRT.Interop;

namespace BlendHub
{
    public sealed partial class MainWindow : Window
    {
        private UIElement? _mainContent;
        private Pages.SplashScreenPage? _splashPage;
        private readonly Dictionary<string, Type> _navigationMap = new()
        {
            { "home", typeof(HomePage) },
            { "download", typeof(DownloadPage) },
            { "backup", typeof(BackupPage) },
            { "restore", typeof(RestorePage) },
            { "sync", typeof(SyncPage) },
            { "project", typeof(ProjectPage) },
            { "canvas", typeof(BlendHub.ReferenceBoard.ReferenceBoard) },
            { "addons", typeof(AddonsPage) },
            { "settings", typeof(SettingsPage) }
        };

        public NavigationView NavigationView => NavView;
        public Frame ContentFrame => this.ContentFrameInternal;

        public MainWindow()
        {
            InitializeComponent();
            _mainContent = this.Content;
            SetWindowProperties();

            DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
            queue.TryEnqueue(() =>
            {
                // Apply theme if selected during setup
                if (App.SelectedTheme != ElementTheme.Default)
                {
                    RootGrid.RequestedTheme = App.SelectedTheme;
                }

                // If splash screen is active, wait until it finishes to navigate
                if (_splashPage == null)
                {
                    var defaultPage = Services.AppSettingsService.Instance.Settings.DefaultPage;
                    var itemToSelect = NavView.MenuItems.OfType<NavigationViewItem>()
                                        .FirstOrDefault(i => i.Tag?.ToString() == defaultPage) ?? HomeItem;
                    NavView.SelectedItem = itemToSelect;
                }
            });
        }

        private void SetWindowProperties()
        {
            this.Title = "BlendHub";
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // Set the window icon
            AppWindow appWindow = GetAppWindowForCurrentWindow();
            if (appWindow != null)
            {
                appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));
                appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

                void UpdateTitleBarColors()
                {
                    appWindow.TitleBar.ButtonForegroundColor = RootGrid.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                    appWindow.TitleBar.ButtonHoverForegroundColor = RootGrid.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                }
                RootGrid.ActualThemeChanged += (s, e) => UpdateTitleBarColors();
                UpdateTitleBarColors();

                // Maximize the window
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();

                    // Set minimum window size
                    presenter.PreferredMinimumWidth = 840;
                    presenter.PreferredMinimumHeight = 500;
                }
            }

        }



        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr windowHandle = WindowNative.GetWindowHandle(this);
            WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            return AppWindow.GetFromWindowId(windowId);
        }

        // TitleBar events (delegated from TitleBar control)
        private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        // NavigationView events
        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            AppTitleBar.IsPaneToggleButtonVisible = false;
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            // Sync navigation selection
            foreach (var item in _navigationMap)
            {
                if (e.SourcePageType == item.Value)
                {
                    // Find in MenuItems or FooterMenuItems
                    var menuItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == item.Key)
                                ?? NavView.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == item.Key);
                    
                    NavView.SelectedItem = menuItem;
                    break;
                }
            }

            // Pass window handle to pages that need it for file pickers/dialogs
            if (e.Content is BlendHub.ReferenceBoard.ReferenceBoard boardPage)
            {
                boardPage.WindowHandle = WindowNative.GetWindowHandle(this);
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
            {
                var tag = item.Tag.ToString();
                if (_navigationMap.TryGetValue(tag ?? "", out var targetPage))
                {
                    Navigate(targetPage);
                }

                UpdateNavigationViewIcons(item);
            }
        }

        private void UpdateNavigationViewIcons(NavigationViewItem selectedItem)
        {
            foreach (var menuItem in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                SetItemIconState(menuItem, false);
            }
            foreach (var menuItem in NavView.FooterMenuItems.OfType<NavigationViewItem>())
            {
                SetItemIconState(menuItem, false);
            }

            if (selectedItem != null)
            {
                SetItemIconState(selectedItem, true);
            }
        }

        private void SetItemIconState(NavigationViewItem item, bool isSelected)
        {
            if (item.Icon is PathIcon pathIcon)
            {
                string? tag = item.Tag?.ToString();
                string? iconName = tag switch
                {
                    "home" => "home",
                    "project" => "project",
                    "canvas" => "canvas",
                    "download" => "download",
                    "addons" => "addons",
                    "backup" => "backup",
                    "restore" => "restore",
                    "sync" => "sync",
                    "settings" => "settings",
                    _ => null
                };

                if (iconName != null)
                {
                    string state = isSelected ? "filled" : "outline";
                    string resourceKey = $"{iconName}_{state}";

                    if (Application.Current.Resources.TryGetValue(resourceKey, out object geometryString))
                    {
                        pathIcon.Data = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Microsoft.UI.Xaml.Media.Geometry), geometryString);
                    }
                }
            }
        }

        // Splash Screen Helpers
        public void ShowSplashScreen()
        {
            _splashPage = new Pages.SplashScreenPage();
            this.Content = _splashPage;
        }

        public void RestoreMainContent()
        {
            this.Content = _mainContent;
            _splashPage = null;

            // Re-apply title bar since content changed
            this.SetTitleBar(AppTitleBar);

            // Navigate to default page now that startup/version checking tasks are fully complete
            var defaultPage = Services.AppSettingsService.Instance.Settings.DefaultPage;
            var itemToSelect = NavView.MenuItems.OfType<NavigationViewItem>()
                                .FirstOrDefault(i => i.Tag?.ToString() == defaultPage) ?? HomeItem;
            NavView.SelectedItem = itemToSelect;
        }

        public void UpdateSplashStatus(string status, double? progress = null)
        {
            _splashPage?.UpdateStatus(status, progress);
        }

        // Navigation helper for quick access
        public void Navigate(Type pageType, object? parameter = null)
        {
            if (ContentFrameInternal.CurrentSourcePageType != pageType)
            {
                ContentFrameInternal.Navigate(pageType, parameter);
            }
        }

        private void TestButton2Click(object sender, RoutedEventArgs e)
        {
            TestButton2TeachingTip.IsOpen = true;
        }

        // Download logic properties
        private System.Threading.CancellationTokenSource? _downloadCts;
        private bool _isDownloading = false;
        private bool _isPaused = false;
        private string _downloadUrl = string.Empty;
        private string _downloadFilename = string.Empty;
        private Windows.Storage.StorageFile? _destinationFile;
        private long _totalBytesRead = 0;
        private long _contentLength = 0;
        private string _downloadedFilePath = string.Empty;
        private static readonly System.Net.Http.HttpClient _httpClient = new();

        public string ActiveDownloadFilename => _downloadFilename;
        public bool IsCurrentlyDownloading => _isDownloading;

        public async System.Threading.Tasks.Task DownloadFileAsync(string url, string filename)
        {
            _downloadUrl = url;
            _downloadFilename = filename;
            _totalBytesRead = 0;
            _contentLength = 0;
            _isPaused = false;

            try
            {
                _isDownloading = true;
                TriggerUIRefresh();

                // Get Downloads folder
                var downloadsFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");

                // Create destination file with .tmp suffix
                _destinationFile = await downloadsFolder.CreateFileAsync(filename + ".tmp", Windows.Storage.CreationCollisionOption.GenerateUniqueName);

                // Open TeachingTip
                TestButton2TeachingTip.IsOpen = true;

                // Update UI state
                NotificationEmptyState.Visibility = Visibility.Collapsed;
                NotificationActivePanel.Visibility = Visibility.Visible;
                NotificationCompletionPanel.Visibility = Visibility.Collapsed;
                 NotificationPauseResumeIcon.Glyph = "\uE769"; // Pause glyph
                 ToolTipService.SetToolTip(NotificationPauseResumeBtn, "Pause");
                NotificationPauseResumeBtn.IsEnabled = true;
                NotificationDeleteDownloadBtn.IsEnabled = true;
                NotificationProgressBar.Value = 0;
                NotificationFilenameText.Text = filename;
                NotificationProgressText.Text = "Starting download...";

                // Start active download stream
                await StartDownloadStreamAsync();
            }
            catch (Exception ex)
            {
                ShowDownloadError(ex.Message);
            }
        }

        private async System.Threading.Tasks.Task StartDownloadStreamAsync()
        {
            if (string.IsNullOrEmpty(_downloadUrl) || _destinationFile == null) return;

            _downloadCts = new System.Threading.CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, _downloadUrl);

                if (_totalBytesRead > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(_totalBytesRead, null);
                }

                using var response = await _httpClient.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);

                if (_totalBytesRead > 0)
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    {
                        _totalBytesRead = 0;
                    }
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }

                if (_totalBytesRead == 0)
                {
                    _contentLength = response.Content.Headers.ContentLength ?? 0;
                }
                else
                {
                    if (response.Content.Headers.ContentRange?.Length.HasValue == true)
                    {
                        _contentLength = response.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (response.Content.Headers.ContentLength.HasValue)
                    {
                        _contentLength = _totalBytesRead + response.Content.Headers.ContentLength.Value;
                    }
                }

                using var contentStream = await response.Content.ReadAsStreamAsync(token);

                using var fileStream = await _destinationFile.OpenStreamForWriteAsync();
                if (_totalBytesRead > 0)
                {
                    fileStream.Seek(_totalBytesRead, SeekOrigin.Begin);
                }
                else
                {
                    fileStream.SetLength(0);
                }

                var buffer = new byte[8192];
                var lastProgressUpdate = -1;
                var lastProgressUpdateBytes = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                    _totalBytesRead += bytesRead;

                    var progress = _contentLength > 0 ? (double)_totalBytesRead / _contentLength : 0;
                    var progressPercent = (int)(progress * 100);

                    if (_totalBytesRead - lastProgressUpdateBytes >= 512 * 1024 || progressPercent > lastProgressUpdate)
                    {
                        lastProgressUpdate = progressPercent;
                        lastProgressUpdateBytes = _totalBytesRead;

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_isPaused && _isDownloading)
                            {
                                NotificationProgressBar.Value = progressPercent;
                                string sizeText = _contentLength > 0 ? $"of {FormatBytes(_contentLength)}" : "";
                                string percentText = _contentLength > 0 ? $"({progressPercent}%)" : "";
                                NotificationProgressText.Text = $"{FormatBytes(_totalBytesRead)} {sizeText} {percentText}".Trim();
                            }
                        });
                    }
                }

                await fileStream.FlushAsync(token);

                // Download completed successfully
                DispatcherQueue.TryEnqueue(async () =>
                {
                    _isDownloading = false;
                    TriggerUIRefresh();

                    try
                    {
                        var downloadsFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");

                        string finalFilename = _downloadFilename;
                        await _destinationFile.RenameAsync(finalFilename, Windows.Storage.NameCollisionOption.ReplaceExisting);
                        _downloadedFilePath = Path.Combine(downloadsFolder.Path, finalFilename);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to rename completed download: {ex.Message}");
                        _downloadedFilePath = _destinationFile.Path;
                    }

                    NotificationProgressText.Text = "Download complete.";
                    NotificationProgressBar.Value = 100;
                    NotificationPauseResumeBtn.IsEnabled = false;
                    NotificationDeleteDownloadBtn.IsEnabled = false;
                    NotificationCompletionPanel.Visibility = Visibility.Visible;
                });
            }
            catch (OperationCanceledException)
            {
                // Download was paused or cancelled
            }
            catch (Exception ex)
            {
                ShowDownloadError(ex.Message);
            }
        }

        private void ShowDownloadError(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _isDownloading = false;
                TriggerUIRefresh();
                NotificationProgressBar.Value = 0;
                NotificationProgressText.Text = $"Download failed: {message}";
                NotificationPauseResumeBtn.IsEnabled = false;
            });
        }

        private string FormatBytes(long bytes)
        {
            return BlendHub.Helpers.FormatHelper.FormatBytes(bytes);
        }

        private async void NotificationPauseResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused)
            {
                _isPaused = false;
                NotificationPauseResumeIcon.Glyph = "\uE769";
                ToolTipService.SetToolTip(NotificationPauseResumeBtn, "Pause");
                NotificationProgressText.Text = $"Resuming download...";

                TriggerUIRefresh();
                await StartDownloadStreamAsync();
            }
            else
            {
                _isPaused = true;
                NotificationPauseResumeIcon.Glyph = "\uE8E5";
                ToolTipService.SetToolTip(NotificationPauseResumeBtn, "Resume");
                NotificationProgressText.Text = $"Paused: {FormatBytes(_totalBytesRead)} of {FormatBytes(_contentLength)}";

                TriggerUIRefresh();
                _downloadCts?.Cancel();
            }
        }

        private async void NotificationDeleteDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            _isDownloading = false;
            _isPaused = false;
            _downloadCts?.Cancel();
            TriggerUIRefresh();

            NotificationEmptyState.Visibility = Visibility.Visible;
            NotificationActivePanel.Visibility = Visibility.Collapsed;

            try
            {
                if (_destinationFile != null)
                {
                    await _destinationFile.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete);
                }
            }
            catch { }

            _destinationFile = null;
            _totalBytesRead = 0;
            _contentLength = 0;
        }

        private void NotificationOpenInstallerBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _downloadedFilePath, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to open installer: {ex.Message}");
            }
        }

        private void NotificationOpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_downloadedFilePath))
                {
                    var folderPath = Path.GetDirectoryName(_downloadedFilePath);
                    if (folderPath != null && Directory.Exists(folderPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folderPath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to open download folder: {ex.Message}");
            }
        }

        private void TriggerUIRefresh()
        {
        }
    }
}
