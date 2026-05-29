using System;
using Microsoft.UI.Xaml;
using BlendHub.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BlendHub
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static ElementTheme SelectedTheme { get; set; } = ElementTheme.Default;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Create main window but show splash screen first
            MainWindow = new MainWindow();
            MainWindow.ShowSplashScreen();
            MainWindow.Activate();

            try
            {
                // Perform actual initialization tasks
                MainWindow.UpdateSplashStatus("Loading configuration...", 20);
                var settings = AppSettingsService.Instance.Settings;
                await System.Threading.Tasks.Task.Delay(300);

                // Clean up temp images daily on first launch
                try
                {
                    await src.Models.BoardPersistence.CleanUpTempImagesAsync();
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Temp images cleanup failed: {cleanupEx.Message}");
                }

                MainWindow.UpdateSplashStatus("Searching for Blender installations...", 50);
                var blenderService = new BlenderSettingsService();
                try
                {
                    await System.Threading.Tasks.Task.Run(() => blenderService.GetInstalledVersions());
                }
                catch (Exception blenderEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Scan Blender installations failed: {blenderEx.Message}");
                }
                await System.Threading.Tasks.Task.Delay(200);

                MainWindow.UpdateSplashStatus("Loading projects...", 80);
                try
                {
                    var loadedProjects = await System.Threading.Tasks.Task.Run(() => ProjectService.LoadProjects());
                    if (AppSettingsService.Instance.Settings.AutoDetectBlenderVersion)
                    {
                        await ProjectService.DetectProjectVersionsAsync(loadedProjects);
                    }
                }
                catch (Exception projectEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Loading projects failed: {projectEx.Message}");
                }
                await System.Threading.Tasks.Task.Delay(300);

                MainWindow.UpdateSplashStatus("Ready", 100);
                await System.Threading.Tasks.Task.Delay(200);
            }
            catch (Exception initEx)
            {
                System.Diagnostics.Debug.WriteLine($"[App] General startup initialization failed: {initEx.Message}");
                MainWindow.UpdateSplashStatus("Startup error occurred, continuing...", 100);
                await System.Threading.Tasks.Task.Delay(1000);
            }
            finally
            {
                // Transition to main UI
                MainWindow.RestoreMainContent();
            }
        }

        public static MainWindow MainWindow { get; set; } = null!;
    }
}
