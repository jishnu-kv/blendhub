using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class PenItemControl : BoardItemBase
    {
        public PenItemControl()
        {
            this.InitializeComponent();
            this.MaintainAspectRatio = true;
        }

        public FrameworkElement? Shape
        {
            get => ShapeContent?.Content as FrameworkElement;
            set { if (ShapeContent != null) ShapeContent.Content = value; }
        }

        public List<Windows.Foundation.Point>? Points { get; set; }
    }
}
