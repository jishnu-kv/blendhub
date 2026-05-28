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
            { "referenceboard", typeof(BlendHub.ReferenceBoard.ReferenceBoard) },
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
                    presenter.PreferredMinimumHeight = 650;
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
            }
        }

        private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            // Check if the clicked item is the Feedback button using its Tag
            if (args.InvokedItemContainer != null && args.InvokedItemContainer.Tag?.ToString() == "feedback")
            {
                // Show feedback dialog without changing navigation selection
                ShowFeedbackDialog();
            }
        }

        private async void ShowFeedbackDialog()
        {
            var dialog = new BlendHub.Dialogs.FeedbackDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (this.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
            };

            await dialog.ShowAsync();
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
    }
}
