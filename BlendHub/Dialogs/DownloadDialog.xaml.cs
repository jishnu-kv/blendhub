using BlendHub.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace BlendHub.Dialogs
{
    public sealed partial class DownloadDialog : UserControl
    {
        private List<WindowsInstaller> _allInstallers = new();
        private string _versionId = string.Empty;
        private string _baseUrl = string.Empty;
        private static readonly HttpClient _httpClient = new();

        private ContentDialog? _parentDialog;
        private System.Threading.CancellationTokenSource? _cts;
        private bool _isDownloading = false;
        private bool _isPaused = false;
        private string _downloadUrl = string.Empty;
        private string _downloadFilename = string.Empty;
        private StorageFile? _destinationFile;
        private long _totalBytesRead = 0;
        private long _contentLength = 0;
        private string _downloadedFilePath = string.Empty;

        public DownloadDialog()
        {
            this.InitializeComponent();
            this.Loaded += OnDialogLoaded;
        }

        private void OnDialogLoaded(object sender, RoutedEventArgs e)
        {
            // Apply initial filters after UI is loaded
            ApplyFilters();
        }

        public void Initialize(string versionId, string fullVersion, string releaseDate, bool isLatest, List<WindowsInstaller> installers, string sourceBaseUrl, ContentDialog parentDialog)
        {
            _versionId = versionId;
            _allInstallers = installers ?? new List<WindowsInstaller>();
            _baseUrl = sourceBaseUrl;
            _parentDialog = parentDialog;

            if (_parentDialog != null)
            {
                _parentDialog.Closing += (sender, args) =>
                {
                    if (_isDownloading)
                    {
                        args.Cancel = true; // Prevent closing the dialog during an active download
                    }
                };
            }

            // Set header info
            DialogVersionText.Text = $"Blender {fullVersion}";

            // Filters will be applied automatically when dialog loads
        }

        private void ApplyFilters()
        {
            var filtered = _allInstallers.AsEnumerable();

            // Get selected platform with null check
            string platformTag = "all";
            if (DialogPlatformComboBox?.SelectedItem is ComboBoxItem selectedPlatform)
            {
                platformTag = selectedPlatform.Tag?.ToString() ?? "all";
            }

            // Get selected type with null check
            string typeTag = "all";
            if (DialogTypeComboBox?.SelectedItem is ComboBoxItem selectedType)
            {
                typeTag = selectedType.Tag?.ToString() ?? "all";
            }

            // Filter by platform
            if (platformTag != "all")
            {
                filtered = filtered.Where(i =>
                {
                    var platform = GetPlatformFromFilename(i.Filename);
                    return platformTag.Contains(platform);
                });
            }

            // Filter by type
            if (typeTag != "all")
            {
                filtered = filtered.Where(i =>
                {
                    var ext = Path.GetExtension(i.Filename)?.ToLower();
                    return ext == typeTag;
                });
            }

            var resultList = filtered.ToList();

            // Update UI with null check
            if (DialogInstallersList != null)
                DialogInstallersList.ItemsSource = resultList;

            if (DialogNoInstallersMessage != null)
                DialogNoInstallersMessage.Visibility = resultList.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private string GetPlatformFromFilename(string filename)
        {
            var lower = filename.ToLower();
            if (lower.Contains("arm64") || lower.Contains("aarch64"))
                return "windows-arm64";
            if (lower.Contains("x64") || lower.Contains("64") || lower.Contains("amd64"))
                return "windows-x64";
            if (lower.Contains("x86") || lower.Contains("32") || lower.Contains("i686"))
                return "windows32";
            return "windows-x64"; // Default to x64
        }

        private void DialogPlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void DialogTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private async void DialogViewDownloadPage_Click(object sender, RoutedEventArgs e)
        {
            var url = $"https://download.blender.org/release/Blender{_versionId}/";
            await Launcher.LaunchUriAsync(new Uri(url));
        }

        private async void DialogInstallerDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WindowsInstaller installer)
            {
                // Download the file instead of opening in browser
                if (!string.IsNullOrEmpty(installer.Url))
                {
                    await DownloadFileAsync(installer.Url, installer.Filename);
                }
            }
        }

        private async Task DownloadFileAsync(string url, string filename)
        {
            _downloadUrl = url;
            _downloadFilename = filename;
            _totalBytesRead = 0;
            _contentLength = 0;
            _isPaused = false;

            try
            {
                // Disable parent close button and register closing handler
                _isDownloading = true;
                if (_parentDialog != null)
                {
                    ToggleCloseButton(false);
                }

                // Get Downloads folder
                var downloadsFolder = await StorageFolder.GetFolderFromPathAsync(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");

                // Create destination file
                _destinationFile = await downloadsFolder.CreateFileAsync(filename, CreationCollisionOption.GenerateUniqueName);

                // Set UI state
                DialogProgressPanel.Visibility = Visibility.Visible;
                DialogCompletionPanel.Visibility = Visibility.Collapsed;
                PauseResumeIcon.Glyph = "\uE769"; // Pause glyph
                PauseResumeText.Text = "Pause";
                PauseResumeBtn.IsEnabled = true;
                DeleteDownloadBtn.IsEnabled = true;
                DialogProgressBar.Value = 0;
                DialogProgressText.Text = $"Starting download of {filename}...";

                // Start active download stream
                await StartDownloadStreamAsync();
            }
            catch (Exception ex)
            {
                ShowDownloadError(ex.Message);
            }
        }

        private async Task StartDownloadStreamAsync()
        {
            if (string.IsNullOrEmpty(_downloadUrl) || _destinationFile == null) return;

            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, _downloadUrl);

                // If we have already read some bytes, make a Range request to resume
                if (_totalBytesRead > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(_totalBytesRead, null);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                // If we sent a Range request, server should return 206 Partial Content. Otherwise 200 OK.
                if (_totalBytesRead > 0)
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    {
                        // Server does not support resume or range was invalid; restart from scratch
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

                using var contentStream = await response.Content.ReadAsStreamAsync(token);

                // Open file stream for writing
                // If we are resuming, open stream in write-append mode or seek to end
                using var fileStream = await _destinationFile.OpenStreamForWriteAsync();
                if (_totalBytesRead > 0)
                {
                    fileStream.Seek(_totalBytesRead, SeekOrigin.Begin);
                }
                else
                {
                    fileStream.SetLength(0); // Truncate if starting fresh
                }

                var buffer = new byte[8192];
                var lastProgressUpdate = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                    _totalBytesRead += bytesRead;

                    var progress = _contentLength > 0 ? (double)_totalBytesRead / _contentLength : 0;
                    var progressPercent = (int)(progress * 100);

                    if (progressPercent > lastProgressUpdate && (progressPercent - lastProgressUpdate >= 1))
                    {
                        lastProgressUpdate = progressPercent;

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_isPaused && _isDownloading)
                            {
                                DialogProgressBar.Value = progressPercent;
                                DialogProgressText.Text = $"Downloading {_downloadFilename}: {FormatBytes(_totalBytesRead)} of {FormatBytes(_contentLength)} ({progressPercent}%)";
                            }
                        });
                    }
                }

                await fileStream.FlushAsync(token);

                // Download completed successfully
                DispatcherQueue.TryEnqueue(() =>
                {
                    _isDownloading = false;
                    ToggleCloseButton(true);

                    DialogProgressPanel.Visibility = Visibility.Collapsed;
                    DialogCompletionPanel.Visibility = Visibility.Visible;
                    CompletionMessageText.Text = $"Download complete: {_downloadFilename} saved to Downloads folder.";
                    _downloadedFilePath = _destinationFile.Path;
                    ApplyFilters();
                });
            }
            catch (OperationCanceledException)
            {
                // Download was paused or cancelled; handle in calling handlers
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
                ToggleCloseButton(true);

                DialogProgressBar.Value = 0;
                DialogProgressText.Text = $"Download failed: {message}";
                PauseResumeBtn.IsEnabled = false;
            });
        }

        private async void PauseResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused)
            {
                // Resume download
                _isPaused = false;
                PauseResumeIcon.Glyph = "\uE769"; // Pause glyph
                PauseResumeText.Text = "Pause";
                DialogProgressText.Text = $"Resuming download...";

                await StartDownloadStreamAsync();
            }
            else
            {
                // Pause download
                _isPaused = true;
                PauseResumeIcon.Glyph = "\uE8E5"; // Play/Resume glyph
                PauseResumeText.Text = "Resume";
                DialogProgressText.Text = $"Paused: {FormatBytes(_totalBytesRead)} of {FormatBytes(_contentLength)}";

                _cts?.Cancel();
            }
        }

        private async void DeleteDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            // Cancel active download
            _isDownloading = false;
            _isPaused = false;
            _cts?.Cancel();

            if (_parentDialog != null)
            {
                ToggleCloseButton(true);
            }

            // Hide panels
            DialogProgressPanel.Visibility = Visibility.Collapsed;
            DialogCompletionPanel.Visibility = Visibility.Collapsed;

            // Delete partially downloaded file if it exists
            try
            {
                if (_destinationFile != null)
                {
                    await _destinationFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
            }
            catch { }

            _destinationFile = null;
            _totalBytesRead = 0;
            _contentLength = 0;
            ApplyFilters();
        }

        private void OpenInstallerBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = _downloadedFilePath, UseShellExecute = true });
                }
            }
            catch { }
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_downloadedFilePath))
                {
                    var folderPath = Path.GetDirectoryName(_downloadedFilePath);
                    if (folderPath != null && Directory.Exists(folderPath))
                    {
                        Process.Start("explorer.exe", folderPath);
                    }
                }
            }
            catch { }
        }

        private void ToggleCloseButton(bool enabled)
        {
            if (_parentDialog == null) return;
            var closeButton = FindVisualChildByName<Button>(_parentDialog, "CloseButton");
            if (closeButton != null)
            {
                closeButton.IsEnabled = enabled;
            }
        }

        private T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : DependencyObject
        {
            int childrenCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && child is FrameworkElement fe && fe.Name == name)
                {
                    return typedChild;
                }

                var childOfChild = FindVisualChildByName<T>(child, name);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        public static bool IsDownloaded(string filename)
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", filename);
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static Visibility GetDownloadButtonVisibility(string filename) =>
            IsDownloaded(filename) ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility GetOpenActionsVisibility(string filename) =>
            IsDownloaded(filename) ? Visibility.Visible : Visibility.Collapsed;

        private void OpenDownloadedInstaller_Click(object sender, SplitButtonClickEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsInstaller installer)
            {
                try
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", installer.Filename);
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    }
                }
                catch { }
            }
        }

        private void OpenDownloadedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsInstaller installer)
            {
                try
                {
                    var folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (Directory.Exists(folderPath))
                    {
                        Process.Start("explorer.exe", folderPath);
                    }
                }
                catch { }
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1) { number /= 1024; counter++; }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private void DownloadDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // Cancel button clicked - just close dialog
        }
    }
}
