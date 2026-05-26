using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace BlendHub.Dialogs
{
    public sealed partial class FeedbackDialog : ContentDialog
    {
        public FeedbackDialog()
        {
            this.InitializeComponent();
        }

        private async void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(EmailTextBox.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            // Change icon to checkmark for feedback
            CopyIcon.Glyph = "\uE73E"; // Checkmark icon

            // Wait for 1 second then restore original icon
            await Task.Delay(1000);
            CopyIcon.Glyph = "\uE8C8"; // Copy icon
        }
    }
}
