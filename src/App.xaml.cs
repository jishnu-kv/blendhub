using BlendHub.Services;
using Microsoft.UI.Xaml;

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
            // Check if this is the first run
            if (AppSettingsService.Instance.Settings.IsFirstRun)
            {
                // Show setup window on first run
                var setupWindow = new SetupWindow();
                setupWindow.Activate();
                return;
            }

            // Create main window but show splash screen first
            MainWindow = new MainWindow();
            MainWindow.ShowSplashScreen();
            MainWindow.Activate();

            // Perform actual initialization tasks
            MainWindow.UpdateSplashStatus("Loading configuration...", 20);
            var settings = AppSettingsService.Instance.Settings;
            await System.Threading.Tasks.Task.Delay(300);

            MainWindow.UpdateSplashStatus("Searching for Blender installations...", 50);
            var blenderService = new BlenderSettingsService();
            await System.Threading.Tasks.Task.Run(() => blenderService.GetInstalledVersions());
            await System.Threading.Tasks.Task.Delay(200);

            MainWindow.UpdateSplashStatus("Loading projects...", 80);
            var loadedProjects = await System.Threading.Tasks.Task.Run(() => ProjectService.LoadProjects());
            if (AppSettingsService.Instance.Settings.AutoDetectBlenderVersion)
            {
                await ProjectService.DetectProjectVersionsAsync(loadedProjects);
            }
            await System.Threading.Tasks.Task.Delay(300);

            MainWindow.UpdateSplashStatus("Ready", 100);
            await System.Threading.Tasks.Task.Delay(200);

            // Transition to main UI
            MainWindow.RestoreMainContent();
        }

        public static MainWindow MainWindow { get; set; } = null!;
    }
}
