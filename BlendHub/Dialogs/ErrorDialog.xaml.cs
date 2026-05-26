using Microsoft.UI.Xaml.Controls;

namespace BlendHub.Dialogs
{
    public sealed partial class ErrorDialog : ContentDialog
    {
        public ErrorDialog(string errorMessage)
        {
            this.InitializeComponent();
            MessageTextBlock.Text = errorMessage;
        }
    }
}
