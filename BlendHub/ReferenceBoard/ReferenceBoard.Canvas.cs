using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using BlendHub.ReferenceBoard;
using src.Core;
using src.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class ReferenceBoard : Page
    {
        private void CanvasContainer_Loaded(object sender, RoutedEventArgs e)
        {
            CenterOnContent();
            UpdateItemsZoomFactor(1.0f);

            CanvasContainer.AddHandler(PointerPressedEvent, new PointerEventHandler(Canvas_PointerPressed), true);
            CanvasContainer.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(CanvasContainer_PointerWheelChanged), true);
        }
        
        private void CanvasContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CanvasContainer.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
            };
        }

        private void UpdateItemsZoomFactor(double zoomFactor)
        {
            foreach (var child in InfiniteCanvasArea.Children)
            {
                if (child is IBoardItem item)
                {
                    item.ZoomFactor = zoomFactor;
                }
            }
        }

        private void UpdateZoomUI(float zoom)
        {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;
            try
            {
                if (ZoomSlider != null) ZoomSlider.Value = zoom;
                if (ZoomComboBox != null) ZoomComboBox.Text = $"{(int)Math.Round(zoom * 100)}%";
            }
            finally { _isUpdatingUI = false; }
        }

        private void SetZoom(float newZoom, Point? focalPoint = null)
        {
            if (newZoom < 0.01f) newZoom = 0.01f;
            if (newZoom > 10.0f) newZoom = 10.0f;

            float oldZoom = (float)CanvasTransform.ScaleX;
            Point focus = focalPoint ?? new Point(CanvasContainer.ActualWidth / 2, CanvasContainer.ActualHeight / 2);

            // Calculate focal point in unscaled canvas coordinates
            double absoluteX = (focus.X - CanvasTransform.TranslateX) / oldZoom;
            double absoluteY = (focus.Y - CanvasTransform.TranslateY) / oldZoom;

            // Update transform
            CanvasTransform.ScaleX = newZoom;
            CanvasTransform.ScaleY = newZoom;

            // Adjust translation so the focal point stays under the pointer
            CanvasTransform.TranslateX = focus.X - (absoluteX * newZoom);
            CanvasTransform.TranslateY = focus.Y - (absoluteY * newZoom);

            UpdateItemsZoomFactor(newZoom);
            UpdateZoomUI(newZoom);
        }

        private void ZoomToPoint(float zoomFactor, Point? focalPoint = null)
        {
            SetZoom((float)CanvasTransform.ScaleX * zoomFactor, focalPoint);
        }

        private void CanvasContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(CanvasContainer);
            var properties = ptr.Properties;

            float currentZoom = (float)CanvasTransform.ScaleX;
            float zoomFactor = properties.MouseWheelDelta > 0 ? 1.15f : (1.0f / 1.15f);
            float newZoom = currentZoom * zoomFactor;

            SetZoom(newZoom, ptr.Position);

            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomToPoint(1.15f);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomToPoint(1.0f / 1.15f);
        private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1.0f);

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            SetZoom((float)e.NewValue);
        }

        private void ZoomComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            if (ZoomComboBox.SelectedItem is string text)
            {
                ApplyZoomText(text);
            }
        }

        private void ZoomComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs e)
        {
            ApplyZoomText(e.Text);
            e.Handled = true;
        }

        private void ApplyZoomText(string text)
        {
            string clean = text.Replace("%", "").Trim();
            if (float.TryParse(clean, out float percent))
            {
                SetZoom(percent / 100f);
            }
        }

        private Rect GetContentBounds()
        {
            if (InfiniteCanvasArea.Children.Count == 0) return Rect.Empty;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool hasContent = false;

            foreach (var child in InfiniteCanvasArea.Children)
            {
                if (child is FrameworkElement fe)
                {
                    double left = Canvas.GetLeft(fe);
                    double top = Canvas.GetTop(fe);
                    double width = fe.Width;
                    double height = fe.Height;

                    // Handle Polyline separately
                    if (fe is Polyline poly && poly.Points.Count > 0)
                    {
                        foreach (var p in poly.Points)
                        {
                            minX = Math.Min(minX, p.X);
                            minY = Math.Min(minY, p.Y);
                            maxX = Math.Max(maxX, p.X);
                            maxY = Math.Max(maxY, p.Y);
                        }
                        hasContent = true;
                        continue;
                    }

                    if (double.IsNaN(width)) width = fe.ActualWidth;
                    if (double.IsNaN(height)) height = fe.ActualHeight;

                    if (width > 0 && height > 0)
                    {
                        minX = Math.Min(minX, left);
                        minY = Math.Min(minY, top);
                        maxX = Math.Max(maxX, left + width);
                        maxY = Math.Max(maxY, top + height);
                        hasContent = true;
                    }
                }
            }

            if (!hasContent) return Rect.Empty;
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            Rect bounds = GetContentBounds();
            if (bounds.IsEmpty) return;

            // Add some padding around the content
            double padding = 100;
            Rect paddedBounds = new Rect(bounds.X - padding, bounds.Y - padding, bounds.Width + padding * 2, bounds.Height + padding * 2);

            // Calculate the required zoom to fit the content
            double zoomX = CanvasContainer.ActualWidth / paddedBounds.Width;
            double zoomY = CanvasContainer.ActualHeight / paddedBounds.Height;
            float targetZoom = (float)Math.Min(zoomX, zoomY);

            // Clamp zoom levels
            targetZoom = Math.Max(0.01f, Math.Min(2.0f, targetZoom));

            // Calculate the offsets to center the bounding box
            double contentCenterX = paddedBounds.X + (paddedBounds.Width / 2);
            double contentCenterY = paddedBounds.Y + (paddedBounds.Height / 2);

            CanvasTransform.ScaleX = targetZoom;
            CanvasTransform.ScaleY = targetZoom;
            CanvasTransform.TranslateX = (CanvasContainer.ActualWidth / 2) - (contentCenterX * targetZoom);
            CanvasTransform.TranslateY = (CanvasContainer.ActualHeight / 2) - (contentCenterY * targetZoom);

            UpdateItemsZoomFactor(targetZoom);
            UpdateZoomUI(targetZoom);
        }

        private void CenterOnContent()
        {
            Rect bounds = GetContentBounds();
            if (bounds.IsEmpty)
            {
                CanvasTransform.TranslateX = CanvasContainer.ActualWidth / 2;
                CanvasTransform.TranslateY = CanvasContainer.ActualHeight / 2;
                CanvasTransform.ScaleX = 1.0f;
                CanvasTransform.ScaleY = 1.0f;
            }
            else
            {
                double contentCenterX = bounds.X + (bounds.Width / 2);
                double contentCenterY = bounds.Y + (bounds.Height / 2);
                
                CanvasTransform.ScaleX = 1.0f;
                CanvasTransform.ScaleY = 1.0f;
                CanvasTransform.TranslateX = (CanvasContainer.ActualWidth / 2) - contentCenterX;
                CanvasTransform.TranslateY = (CanvasContainer.ActualHeight / 2) - contentCenterY;
            }
            UpdateItemsZoomFactor(1.0f);
            UpdateZoomUI(1.0f);
        }

        private void DeselectAll()
        {
            _isUpdatingUI = true;
            try
            {
                foreach (var item in _selectedItems) item.IsSelected = false;
                _selectedItems.Clear();
                if (_selectedItem != null) _selectedItem.IsSelected = false;
                _selectedItem = null;
            }
            finally { _isUpdatingUI = false; }

            if (_currentTool != ToolType.Shape && _currentTool != ToolType.Pen)
            {
                UpdateToolUI();
            }
        }

        private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(InfiniteCanvasArea);

            if (_currentTool == ToolType.Pen && ptr.Properties.IsLeftButtonPressed)
            {
                DeselectAll();

                var strokeColor = _shapeStrokeColor;
                strokeColor.A = (byte)(ShapeOutlineOpacitySlider.Alpha * 255 / 100.0);

                _currentPolyline = new Polyline
                {
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = ShapeThicknessSlider.Value,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _currentPolyline.Points.Add(ptr.Position);
                InfiniteCanvasArea.Children.Add(_currentPolyline);
                try { InfiniteCanvasArea.CapturePointer(e.Pointer); } catch { }
                e.Handled = true;
                return;
            }

            if (_currentTool == ToolType.Shape && _currentShapeType != null && ptr.Properties.IsLeftButtonPressed)
            {
                DeselectAll();
                _drawingStartPoint = ptr.Position;

                Shape shape;
                if (_currentShapeType == "Rectangle") shape = new Rectangle();
                else if (_currentShapeType == "Circle") shape = new Ellipse();
                else if (_currentShapeType == "Line") shape = new Line { X1 = 0, Y1 = 0, X2 = 0, Y2 = 0 };
                else shape = new Microsoft.UI.Xaml.Shapes.Path(); // Arrow

                var fillColor = _shapeFillColor;
                fillColor.A = (byte)(ShapeOpacitySlider.Alpha * 255 / 100.0);

                var strokeColor = _shapeStrokeColor;
                strokeColor.A = (byte)(ShapeOutlineOpacitySlider.Alpha * 255 / 100.0);

                shape.Stroke = _isShapeOutlineNone ? null : new SolidColorBrush(strokeColor);
                shape.StrokeThickness = ShapeThicknessSlider.Value;
                shape.Fill = _isShapeFillNone ? null : new SolidColorBrush(fillColor);
                shape.Opacity = 1.0;

                _drawingShape = shape;
                Canvas.SetLeft(_drawingShape, _drawingStartPoint.X);
                Canvas.SetTop(_drawingShape, _drawingStartPoint.Y);
                InfiniteCanvasArea.Children.Add(_drawingShape);

                try { InfiniteCanvasArea.CapturePointer(e.Pointer); } catch { }
                e.Handled = true;
                return;
            }


            var pointerPoint = e.GetCurrentPoint(CanvasContainer);
            var props = pointerPoint.Properties;

            // Pan mode: Middle button
            if (props.IsMiddleButtonPressed)
            {
                DeselectAll();
                _isDragging = true;
                _lastPointerPosition = pointerPoint.Position;
                try { InfiniteCanvasArea.CapturePointer(e.Pointer); } catch { }
                e.Handled = true;
            }
            // Selection mode: Left button on empty canvas or background container
            else if (props.IsLeftButtonPressed && (ReferenceEquals(e.OriginalSource, InfiniteCanvasArea) || ReferenceEquals(e.OriginalSource, CanvasContainer)))
            {
                this.Focus(FocusState.Programmatic);
                bool isMultiSelect = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control) ||
                                     e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);

                if (!isMultiSelect) DeselectAll();

                _selectedItemsAtStart.Clear();
                _selectedItemsAtStart.AddRange(_selectedItems);

                _isSelecting = true;
                _drawingStartPoint = ptr.Position;

                if (_selectionMarquee == null)
                {
                    _selectionMarquee = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)),
                        Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Visibility = Visibility.Collapsed,
                        IsHitTestVisible = false
                    };
                    InfiniteCanvasArea.Children.Add(_selectionMarquee);
                }

                Canvas.SetLeft(_selectionMarquee, _drawingStartPoint.X);
                Canvas.SetTop(_selectionMarquee, _drawingStartPoint.Y);
                _selectionMarquee.Width = 0;
                _selectionMarquee.Height = 0;
                _selectionMarquee.Visibility = Visibility.Visible;

                try { InfiniteCanvasArea.CapturePointer(e.Pointer); } catch { }
                e.Handled = true;
            }
        }

        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(InfiniteCanvasArea);

            if (_currentPolyline != null)
            {
                _currentPolyline.Points.Add(ptr.Position);
                e.Handled = true;
                return;
            }

            if (_drawingShape != null)
            {
                bool isShiftPressed = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);
                Point currentPos = ptr.Position;

                if (isShiftPressed)
                {
                    if (_drawingShape is Rectangle || _drawingShape is Ellipse)
                    {
                        double w1 = Math.Abs(currentPos.X - _drawingStartPoint.X);
                        double h1 = Math.Abs(currentPos.Y - _drawingStartPoint.Y);
                        double size = Math.Max(w1, h1);

                        double newX = currentPos.X < _drawingStartPoint.X ? _drawingStartPoint.X - size : _drawingStartPoint.X;
                        double newY = currentPos.Y < _drawingStartPoint.Y ? _drawingStartPoint.Y - size : _drawingStartPoint.Y;
                        currentPos = new Point(newX + (currentPos.X < _drawingStartPoint.X ? 0 : size), newY + (currentPos.Y < _drawingStartPoint.Y ? 0 : size));
                    }
                    else if (_drawingShape is Line || _drawingShape is Microsoft.UI.Xaml.Shapes.Path)
                    {
                        double dx = currentPos.X - _drawingStartPoint.X;
                        double dy = currentPos.Y - _drawingStartPoint.Y;
                        double angle = Math.Atan2(dy, dx);
                        double snappedAngle = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
                        double distance = Math.Sqrt(dx * dx + dy * dy);
                        currentPos = new Point(
                            _drawingStartPoint.X + distance * Math.Cos(snappedAngle),
                            _drawingStartPoint.Y + distance * Math.Sin(snappedAngle)
                        );
                    }
                }

                double x = Math.Min(currentPos.X, _drawingStartPoint.X);
                double y = Math.Min(currentPos.Y, _drawingStartPoint.Y);
                double w = Math.Max(1, Math.Abs(currentPos.X - _drawingStartPoint.X));
                double h = Math.Max(1, Math.Abs(currentPos.Y - _drawingStartPoint.Y));

                if (_drawingShape is Line line)
                {
                    double padding = 30;
                    double px = x - padding;
                    double py = y - padding;
                    double pw = w + padding * 2;
                    double ph = h + padding * 2;

                    Canvas.SetLeft(_drawingShape, px);
                    Canvas.SetTop(_drawingShape, py);
                    _drawingShape.Width = pw;
                    _drawingShape.Height = ph;

                    line.X1 = _drawingStartPoint.X - px;
                    line.Y1 = _drawingStartPoint.Y - py;
                    line.X2 = currentPos.X - px;
                    line.Y2 = currentPos.Y - py;
                }
                else if (_drawingShape is Microsoft.UI.Xaml.Shapes.Path path && _currentShapeType == "Arrow")
                {
                    double padding = 30;
                    double px = x - padding;
                    double py = y - padding;
                    double pw = w + padding * 2;
                    double ph = h + padding * 2;

                    Canvas.SetLeft(_drawingShape, px);
                    Canvas.SetTop(_drawingShape, py);
                    _drawingShape.Width = pw;
                    _drawingShape.Height = ph;

                    path.Data = CreateArrowGeometry(_drawingStartPoint, currentPos, new Point(px, py));
                }
                else
                {
                    _drawingShape.Width = w;
                    _drawingShape.Height = h;
                    Canvas.SetLeft(_drawingShape, x);
                    Canvas.SetTop(_drawingShape, y);
                }
                e.Handled = true;
                return;
            }

            if (_isSelecting)
            {
                Point currentPos = ptr.Position;
                double x = Math.Min(currentPos.X, _drawingStartPoint.X);
                double y = Math.Min(currentPos.Y, _drawingStartPoint.Y);
                double w = Math.Abs(currentPos.X - _drawingStartPoint.X);
                double h = Math.Abs(currentPos.Y - _drawingStartPoint.Y);

                if (_selectionMarquee != null)
                {
                    Canvas.SetLeft(_selectionMarquee, x);
                    Canvas.SetTop(_selectionMarquee, y);
                    _selectionMarquee.Width = w;
                    _selectionMarquee.Height = h;
                }

                // Update real-time selection preview
                Rect marqueeRect = new Rect(x, y, w, h);
                foreach (var child in InfiniteCanvasArea.Children)
                {
                    if (child is IBoardItem item && child is FrameworkElement fe && child != _selectionMarquee)
                    {
                        double left = Canvas.GetLeft(fe);
                        double top = Canvas.GetTop(fe);
                        if (double.IsNaN(left)) left = 0;
                        if (double.IsNaN(top)) top = 0;

                        Rect itemRect = new Rect(left, top, fe.ActualWidth, fe.ActualHeight);
                        itemRect.Intersect(marqueeRect);
                        bool isInside = !itemRect.IsEmpty;

                        item.IsSelected = _selectedItemsAtStart.Contains(item) || isInside;
                    }
                }
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                var pointerPoint = e.GetCurrentPoint(CanvasContainer);
                double deltaX = pointerPoint.Position.X - _lastPointerPosition.X;
                double deltaY = pointerPoint.Position.Y - _lastPointerPosition.Y;
                
                CanvasTransform.TranslateX += deltaX;
                CanvasTransform.TranslateY += deltaY;
                
                _lastPointerPosition = pointerPoint.Position;
                e.Handled = true;
            }

            _currentMouseCanvasPosition = ptr.Position;
            if (CursorPosText != null)
            {
                CursorPosText.Text = $"{(int)Math.Round(ptr.Position.X)}, {(int)Math.Round(ptr.Position.Y)} px";
            }
        }

        private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(InfiniteCanvasArea);

            if (_isSelecting)
            {
                _isSelecting = false;
                if (_selectionMarquee != null) _selectionMarquee.Visibility = Visibility.Collapsed;

                // Finalize selection list
                _selectedItems.Clear();
                foreach (var child in InfiniteCanvasArea.Children)
                {
                    if (child is IBoardItem item && item.IsSelected)
                    {
                        if (!_selectedItems.Contains(item)) _selectedItems.Add(item);
                    }
                }

                // If only one item selected, set it as the primary _selectedItem
                _selectedItem = _selectedItems.Count == 1 ? _selectedItems[0] : null;

                InfiniteCanvasArea.ReleasePointerCapture(e.Pointer);
                UpdateToolUI();
                e.Handled = true;
                return;
            }

            if (_currentPolyline != null)
            {
                SmoothPolyline(_currentPolyline);

                // Convert regular polyline to a selectable Path and wrap it in PenItemControl
                var path = ConvertPolylineToPath(_currentPolyline);
                var points = _currentPolyline.Points.ToList();
                InfiniteCanvasArea.Children.Remove(_currentPolyline);

                double w = CalculatePolylineWidth(_currentPolyline);
                double h = CalculatePolylineHeight(_currentPolyline);
                double left = Canvas.GetLeft(path);
                double top = Canvas.GetTop(path);

                var pic = new PenItemControl
                {
                    Shape = path,
                    Points = points,
                    Width = w,
                    Height = h
                };
                Canvas.SetLeft(pic, left);
                Canvas.SetTop(pic, top);
                WireUpEvents(pic);
                _historyManager.ExecuteCommand(new AddItemCommand(InfiniteCanvasArea, pic));

                MarkAsUnsaved();
                _currentPolyline = null;
                InfiniteCanvasArea.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
                return;
            }

            if (_drawingShape != null)
            {
                var shape = _drawingShape;
                _drawingShape = null;
                InfiniteCanvasArea.Children.Remove(shape);

                if ((shape.Width > 5 && shape.Height > 5) || shape is Microsoft.UI.Xaml.Shapes.Path)
                {
                    double w = shape.Width;
                    double h = shape.Height;
                    if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0)
                    {
                        if (shape is Polyline pl)
                        {
                            w = CalculatePolylineWidth(pl);
                            h = CalculatePolylineHeight(pl);
                        }
                        else if (shape is Microsoft.UI.Xaml.Shapes.Path path)
                        {
                            // For paths, ActualWidth/ActualHeight might not be ready yet
                            // Use a default or try to get it from Data if possible
                            w = shape.ActualWidth > 0 ? shape.ActualWidth : 100;
                            h = shape.ActualHeight > 0 ? shape.ActualHeight : 100;
                        }
                        else
                        {
                            w = shape.ActualWidth > 0 ? shape.ActualWidth : 100;
                            h = shape.ActualHeight > 0 ? shape.ActualHeight : 100;
                        }
                    }

                    var sic = new ShapeItemControl
                    {
                        Shape = shape,
                        ShapeType = shape is Rectangle ? "Rectangle" : (shape is Ellipse ? "Circle" : (shape is Line ? "Line" : "Arrow")),
                        Width = Math.Max(20, w),
                        Height = Math.Max(20, h),
                        StartPoint = _drawingStartPoint,
                        EndPoint = ptr.Position
                    };


                    // Calculate points relative to the control's top-left
                    double px = Canvas.GetLeft(shape);
                    double py = Canvas.GetTop(shape);
                    sic.StartPoint = new Point(_drawingStartPoint.X - px, _drawingStartPoint.Y - py);
                    sic.EndPoint = new Point(ptr.Position.X - px, ptr.Position.Y - py);

                    // Clear fixed sizes so they stretch to the control size
                    if (shape is Shape s)
                    {
                        s.Width = double.NaN;
                        s.Height = double.NaN;

                        if (s is Line || s is Microsoft.UI.Xaml.Shapes.Path)
                        {
                            s.Stretch = Stretch.None;
                        }
                        else
                        {
                            s.Stretch = Stretch.Fill;
                        }
                    }

                    Canvas.SetLeft(sic, Canvas.GetLeft(shape));
                    Canvas.SetTop(sic, Canvas.GetTop(shape));

                    // Crucial: ensure hit testing is off if we are still drawing
                    sic.IsHitTestVisible = (_currentTool == ToolType.None);

                    WireUpEvents(sic);
                    _historyManager.ExecuteCommand(new AddItemCommand(InfiniteCanvasArea, sic));
                    MarkAsUnsaved();
                }
                InfiniteCanvasArea.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;
                InfiniteCanvasArea.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        private Geometry CreateArrowGeometry(Point start, Point end, Point? topLeft = null)
        {
            double thickness = (_drawingShape as Shape)?.StrokeThickness ?? 0;
            Point tl = topLeft ?? new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y));
            return GeometryUtils.CreateArrowGeometry(start, end, thickness, tl);
        }

        private double CalculatePolylineWidth(Polyline pl)
        {
            if (pl.Points.Count == 0) return 0;
            double min = pl.Points.Min(p => p.X);
            double max = pl.Points.Max(p => p.X);
            double padding = pl.StrokeThickness / 2 + 5;
            return Math.Max(1, max - min) + padding * 2;
        }

        private double CalculatePolylineHeight(Polyline pl)
        {
            if (pl.Points.Count == 0) return 0;
            double min = pl.Points.Min(p => p.Y);
            double max = pl.Points.Max(p => p.Y);
            double padding = pl.StrokeThickness / 2 + 5;
            return Math.Max(1, max - min) + padding * 2;
        }

        private void SmoothPolyline(Polyline polyline)
        {
            // We now rely on Bezier interpolation in ConvertPolylineToPath
            // for superior smoothing, so we just do a very light simplification here.
            if (polyline.Points.Count < 3) return;

            var points = polyline.Points.ToList();
            var simplified = new List<Point> { points[0] };
            double tolerance = 0.5; // Very low tolerance to keep detail

            for (int i = 1; i < points.Count; i++)
            {
                var p1 = simplified.Last();
                var p2 = points[i];
                double dist = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));

                if (dist > tolerance || i == points.Count - 1)
                {
                    simplified.Add(p2);
                }
            }

            polyline.Points.Clear();
            foreach (var p in simplified) polyline.Points.Add(p);
        }


        private Microsoft.UI.Xaml.Shapes.Path ConvertPolylineToPath(Polyline polyline)
        {
            if (polyline.Points.Count < 2)
                return new Microsoft.UI.Xaml.Shapes.Path();

            // FILTER SMALL MOVEMENTS
            List<Point> filteredPoints = new List<Point>();

            const double MIN_DISTANCE = 3.0;

            filteredPoints.Add(polyline.Points[0]);

            for (int i = 1; i < polyline.Points.Count; i++)
            {
                Point last = filteredPoints[filteredPoints.Count - 1];
                Point current = polyline.Points[i];

                double dx = current.X - last.X;
                double dy = current.Y - last.Y;

                double distance = Math.Sqrt(dx * dx + dy * dy);

                // Only add point if movement is large enough
                if (distance >= MIN_DISTANCE)
                {
                    filteredPoints.Add(current);
                }
            }

            // If filtering removed too many points
            if (filteredPoints.Count < 2)
                return new Microsoft.UI.Xaml.Shapes.Path();

            // -----------------------------------------
            // CALCULATE BOUNDS
            // -----------------------------------------

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (var p in filteredPoints)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            double padding = polyline.StrokeThickness / 2 + 5;

            double px = minX - padding;
            double py = minY - padding;

            // -----------------------------------------
            // CREATE GEOMETRY
            // -----------------------------------------

            var geometry = new PathGeometry();

            var figure = new PathFigure
            {
                StartPoint = new Point(
                    filteredPoints[0].X - px,
                    filteredPoints[0].Y - py
                ),
                IsClosed = false
            };

            for (int i = 1; i < filteredPoints.Count; i++)
            {
                figure.Segments.Add(new LineSegment
                {
                    Point = new Point(
                        filteredPoints[i].X - px,
                        filteredPoints[i].Y - py
                    )
                });
            }

            geometry.Figures.Add(figure);

            // CREATE PATH
            var path = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Stroke = polyline.Stroke,
                StrokeThickness = polyline.StrokeThickness,

                // Make lines visually smoother
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,

                Opacity = polyline.Opacity,
                Stretch = Stretch.None
            };

            Canvas.SetLeft(path, px);
            Canvas.SetTop(path, py);

            return path;
        }
        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Drop to add to board";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;
            }
        }

        private async void Canvas_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var dropPoint = e.GetPosition(InfiniteCanvasArea);

                double offsetX = 0;
                double offsetY = 0;

                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        string ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp")
                        {
                            try
                            {
                                // Copy to assets folder
                                string newPath = await BoardPersistence.CopyImageToAssetsAsync(file.Path, _currentBoardName);

                                var bitmap = new BitmapImage();
                                var assetFile = await StorageFile.GetFileFromPathAsync(newPath);
                                using (var stream = await assetFile.OpenAsync(FileAccessMode.Read))
                                {
                                    await bitmap.SetSourceAsync(stream);
                                }

                                var imgControl = new ImageItemControl
                                {
                                    Source = bitmap,
                                    ImagePath = newPath
                                };

                                // Offset multiple items so they don't overlap perfectly
                                Point pos = new Point(dropPoint.X + offsetX, dropPoint.Y + offsetY);
                                AddBoardItem(imgControl, pos);

                                offsetX += 20;
                                offsetY += 20;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error dropping file: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
    }
}
