using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BlendHub.Dialogs
{
    public sealed partial class DeleteFileDialog : ContentDialog
    {
        public DeleteFileDialog(string fileName)
        {
            this.InitializeComponent();
            this.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            this.RequestedTheme = (App.MainWindow?.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;
            MessageTextBlock.Text = $"Are you sure you want to delete {fileName}? This cannot be undone.";
        }
    }
}
