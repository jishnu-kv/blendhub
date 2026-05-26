using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class ImageItemControl : BoardItemBase
    {
        public ImageItemControl()
        {
            this.InitializeComponent();
            this.MaintainAspectRatio = true;
        }

        public ImageSource? Source
        {
            get => MainImage?.Source;
            set { if (MainImage != null) MainImage.Source = value; }
        }

        public string? ImagePath { get; set; }

        private void FlipHorizontal_Click(object sender, RoutedEventArgs e)
        {
            ImageScale.ScaleX *= -1;
        }

        private void FlipVertical_Click(object sender, RoutedEventArgs e)
        {
            ImageScale.ScaleY *= -1;
        }
    }
}
