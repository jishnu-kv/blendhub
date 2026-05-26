using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

namespace src.Core
{
    public static class GeometryUtils
    {
        public static Geometry CreateArrowGeometry(Point start, Point end, double strokeThickness, Point topLeft)
        {
            Point p1 = new Point(start.X - topLeft.X, start.Y - topLeft.Y);
            Point p2 = new Point(end.X - topLeft.X, end.Y - topLeft.Y);

            double angle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
            double headLength = 15 + strokeThickness;
            double headAngle = Math.PI / 6;

            Point tip = p2;
            Point wing1 = new Point(
                tip.X - headLength * Math.Cos(angle - headAngle),
                tip.Y - headLength * Math.Sin(angle - headAngle));
            Point wing2 = new Point(
                tip.X - headLength * Math.Cos(angle + headAngle),
                tip.Y - headLength * Math.Sin(angle + headAngle));

            var geometry = new PathGeometry();

            // Main line
            var lineFigure = new PathFigure { StartPoint = p1, IsClosed = false };
            lineFigure.Segments.Add(new LineSegment { Point = p2 });
            geometry.Figures.Add(lineFigure);

            // Arrow head (clean V shape)
            var headFigure = new PathFigure { StartPoint = wing1, IsClosed = false };
            headFigure.Segments.Add(new LineSegment { Point = p2 });
            headFigure.Segments.Add(new LineSegment { Point = wing2 });
            geometry.Figures.Add(headFigure);

            return geometry;
        }
    }
}
