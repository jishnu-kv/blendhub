using Microsoft.UI.Xaml.Controls;

namespace BlendHub.Pages
{
    public sealed partial class SplashScreenPage : Page
    {
        public SplashScreenPage()
        {
            this.InitializeComponent();
        }

        public void UpdateStatus(string status, double? progress = null)
        {
            // Status and progress elements removed from UI
        }
    }
}
