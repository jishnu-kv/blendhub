using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using src.Core;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class ShapeItemControl : BoardItemBase
    {
        public ShapeItemControl()
        {
            this.InitializeComponent();
        }

        public FrameworkElement? Shape
        {
            get => ShapeContent?.Content as FrameworkElement;
            set { if (ShapeContent != null) ShapeContent.Content = value; }
        }

        private string? _shapeType;
        public string? ShapeType
        {
            get => _shapeType;
            set { _shapeType = value; UpdateVisualState(); }
        }

        protected override void OnResizing(double newWidth, double newHeight)
        {
            base.OnResizing(newWidth, newHeight);

            if (Shape is Line line)
            {
                line.X1 = _startPoint.X;
                line.Y1 = _startPoint.Y;
                line.X2 = _endPoint.X;
                line.Y2 = _endPoint.Y;
            }
            else if (ShapeType == "Arrow" && Shape is Microsoft.UI.Xaml.Shapes.Path path)
            {
                // Arrows are more complex because they have a head.
                // We'll recalculate the geometry if they are not stretched.
                // But since we set Stretch="Fill" for arrows in Canvas.cs, 
                // the geometry will scale automatically with Width/Height.
                // However, handle-based resizing (tag="Start"/"End") should update geometry.

                if (newWidth == 0 && newHeight == 0) // Specialized handle move
                {
                    path.Stretch = Stretch.None; // Disable stretch during manual point move
                    path.Data = GeometryUtils.CreateArrowGeometry(_startPoint, _endPoint, path.StrokeThickness, new Windows.Foundation.Point(0, 0));
                }
            }
        }
    }
}
