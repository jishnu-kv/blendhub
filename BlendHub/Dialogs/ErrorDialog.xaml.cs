using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BlendHub.Dialogs
{
    public sealed partial class ErrorDialog : ContentDialog
    {
        public ErrorDialog(string errorMessage)
        {
            this.InitializeComponent();
            this.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            this.RequestedTheme = (App.MainWindow?.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;
            MessageTextBlock.Text = errorMessage;
        }
    }
}
