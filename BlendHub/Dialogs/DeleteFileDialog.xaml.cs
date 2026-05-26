using Microsoft.UI.Xaml.Controls;

namespace BlendHub.Dialogs
{
    public sealed partial class DeleteFileDialog : ContentDialog
    {
        public DeleteFileDialog(string fileName)
        {
            this.InitializeComponent();
            MessageTextBlock.Text = $"Are you sure you want to delete {fileName}? This cannot be undone.";
        }
    }
}
