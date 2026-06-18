using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using BlendHub.ReferenceBoard;
using src.Core;
using src.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class ReferenceBoard : Page
    {
        public bool ShowInternalTitleBar
        {
            get => AppTitleBar?.Visibility == Visibility.Visible;
            set => AppTitleBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShapeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;

            if (ShapeSegmented.SelectedItem is CommunityToolkit.WinUI.Controls.SegmentedItem item)
            {
                _currentShapeType = item.Tag?.ToString() ?? "Pen";
                if (MainToolSegmented.SelectedItem is CommunityToolkit.WinUI.Controls.SegmentedItem mainTool && mainTool.Tag?.ToString() == "Shape")
                {
                    _currentTool = _currentShapeType == "Pen" ? ToolType.Pen : ToolType.Shape;
                }
                UpdateToolUI();
            }
        }

        private void ShapeFillColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            if (ShapeFillColorPicker.SelectedItem is ColorItem item)
            {
                _isShapeFillNone = item.IsNone;
                _shapeFillColor = item.IsNone ? Microsoft.UI.Colors.Transparent : (item.Brush as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent;
                UpdateSelectedShapeProperties();
                UpdateShapePreviewUI();
            }
        }

        private void ShapeOutlineColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            if (ShapeOutlineColorPicker.SelectedItem is ColorItem item)
            {
                _isShapeOutlineNone = item.IsNone;
                _shapeStrokeColor = item.IsNone ? Microsoft.UI.Colors.Transparent : (item.Brush as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Black;
                UpdateSelectedShapeProperties();
                UpdateShapePreviewUI();
            }
        }

        private void UpdateShapePreviewUI()
        {
            if (ShapeFillPreview == null || ShapeOutlinePreview == null) return;

            // 1. Update Preview Swatches
            UpdatePreviewSwatch(ShapeFillPreview, ShapeFillNoneIcon, _shapeFillColor);
            UpdatePreviewSwatch(ShapeOutlinePreview, ShapeOutlineNoneIcon, _shapeStrokeColor);

            // 2. Update Thickness Preview
            if (ShapeThicknessPreview != null)
            {
                var previewColor = _shapeStrokeColor == Microsoft.UI.Colors.Transparent ? Microsoft.UI.Colors.Gray : _shapeStrokeColor;
                previewColor.A = (byte)(ShapeOutlineOpacitySlider.Alpha * 255 / 100.0);
                ShapeThicknessPreview.Stroke = new SolidColorBrush(previewColor);
            }

            // 3. Sync Color Pickers
            SyncColorPickerSelection(ShapeFillColorPicker, _shapeFillColor);
            SyncColorPickerSelection(ShapeOutlineColorPicker, _shapeStrokeColor);

            // 4. Update Alpha Sliders
            if (ShapeOpacitySlider != null) ShapeOpacitySlider.Color = _shapeFillColor;
            if (ShapeOutlineOpacitySlider != null) ShapeOutlineOpacitySlider.Color = _shapeStrokeColor;
        }

        private void UpdatePreviewSwatch(IconElement preview, UIElement noneIcon, Color color)
        {
            if (color == Microsoft.UI.Colors.Transparent)
            {
                preview.Visibility = Visibility.Collapsed;
                noneIcon.Visibility = Visibility.Visible;
            }
            else
            {
                preview.Visibility = Visibility.Visible;
                noneIcon.Visibility = Visibility.Collapsed;
                preview.Foreground = new SolidColorBrush(color);
            }
        }

        private void SyncColorPickerSelection(ItemsControl? picker, Color color)
        {
            if (picker == null || !(picker.ItemsSource is IEnumerable<ColorItem> items)) return;

            var currentHex = BoardPersistence.ColorToHex(color);
            var selected = items.FirstOrDefault(i =>
                i.Tag == currentHex ||
                (i.Brush as SolidColorBrush)?.Color == color ||
                (i.IsNone && color == Microsoft.UI.Colors.Transparent));

            if (selected != null)
            {
                if (picker is Selector selector) selector.SelectedItem = selected;
            }
        }

        private void ShapeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            UpdateSelectedShapeProperties();
        }

        private void UpdateSelectedShapeProperties()
        {
            if (InfiniteCanvasArea == null || ShapeThicknessSlider == null || ShapeOpacitySlider == null || ShapeOutlineOpacitySlider == null) return;

            if (ShapeThicknessPreview != null)
                ShapeThicknessPreview.StrokeThickness = ShapeThicknessSlider.Value;

            Color fillColor = _shapeFillColor;
            fillColor.A = (byte)(ShapeOpacitySlider.Alpha * 255 / 100.0);

            Color strokeColor = _shapeStrokeColor;
            strokeColor.A = (byte)(ShapeOutlineOpacitySlider.Alpha * 255 / 100.0);

            foreach (var child in InfiniteCanvasArea.Children)
            {
                if (child is IBoardItem item && item.IsSelected)
                {
                    ApplyPropertiesToItem(child, fillColor, strokeColor, ShapeThicknessSlider.Value);
                }
            }
            UpdateShapePreviewUI();
        }

        private void ApplyPropertiesToItem(UIElement element, Color fillColor, Color strokeColor, double thickness)
        {
            Shape? shape = null;
            if (element is ShapeItemControl sic) shape = sic.Shape as Shape;
            else if (element is PenItemControl pic) shape = pic.Shape as Shape;

            if (shape != null)
            {
                shape.Stroke = _isShapeOutlineNone ? null : new SolidColorBrush(strokeColor);
                shape.StrokeThickness = thickness;

                // Only shapes (not pens) have fill
                if (element is ShapeItemControl)
                {
                    shape.Fill = _isShapeFillNone ? null : new SolidColorBrush(fillColor);
                }

                element.Opacity = 1.0;
                MarkAsUnsaved();
            }
        }

        private void AddImageSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args) => AddImageButton_Click(sender, new RoutedEventArgs());

        private async void AddImageButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg"); picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".webp");
            InitializeWithWindow.Initialize(picker, WindowHandle);
            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                var loadedImages = new List<(ImageItemControl control, double width, double height)>();

                foreach (var file in files)
                {
                    string newPath = await BoardPersistence.CopyImageToAssetsAsync(file.Path, _currentBoardName);
                    var bitmap = new BitmapImage();
                    var assetFile = await StorageFile.GetFileFromPathAsync(newPath);
                    using (var stream = await assetFile.OpenAsync(FileAccessMode.Read)) 
                    { 
                        await bitmap.SetSourceAsync(stream); 
                    }
                    var imgControl = new ImageItemControl { Source = bitmap, ImagePath = newPath };
                    loadedImages.Add((imgControl, bitmap.PixelWidth, bitmap.PixelHeight));
                }

                if (loadedImages.Count > 0)
                {
                    double avgHeight = loadedImages.Average(img => img.height);
                    if (avgHeight <= 0) avgHeight = 200;

                    int rows = Math.Max(1, (int)Math.Floor(Math.Sqrt(files.Count)));
                    int cols = (int)Math.Ceiling((double)files.Count / rows);
                    double spacing = 20;

                    double centerX = (CanvasContainer.ActualWidth / 2 - CanvasTransform.TranslateX) / CanvasTransform.ScaleX;
                    double centerY = (CanvasContainer.ActualHeight / 2 - CanvasTransform.TranslateY) / CanvasTransform.ScaleY;

                    var scaledImages = new List<(ImageItemControl control, double scaledWidth, double scaledHeight)>();
                    foreach (var item in loadedImages)
                    {
                        double scale = avgHeight / (item.height > 0 ? item.height : 1);
                        double scaledW = item.width * scale;
                        scaledImages.Add((item.control, scaledW, avgHeight));
                    }

                    List<Windows.Foundation.Point> positions = new List<Windows.Foundation.Point>();
                    double currentX = 0;
                    double currentY = 0;
                    double maxRowWidth = 0;

                    for (int i = 0; i < scaledImages.Count; i++)
                    {
                        int col = i % cols;

                        if (col == 0 && i > 0)
                        {
                            currentX = 0;
                            currentY += avgHeight + spacing;
                        }

                        positions.Add(new Windows.Foundation.Point(currentX, currentY));
                        
                        currentX += scaledImages[i].scaledWidth + spacing;
                        if (currentX > maxRowWidth) maxRowWidth = currentX;
                    }

                    double totalHeight = currentY + avgHeight;
                    maxRowWidth -= spacing;

                    double startX = centerX - maxRowWidth / 2;
                    double startY = centerY - totalHeight / 2;

                    for (int i = 0; i < scaledImages.Count; i++)
                    {
                        var img = scaledImages[i];
                        img.control.Width = img.scaledWidth;
                        img.control.Height = img.scaledHeight;
                        
                        Windows.Foundation.Point pos = new Windows.Foundation.Point(startX + positions[i].X, startY + positions[i].Y);
                        AddBoardItem(img.control, pos);
                    }
                }
            }
        }
        private void MainToolSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;

            if (MainToolSegmented.SelectedItem is CommunityToolkit.WinUI.Controls.SegmentedItem item)
            {
                string tag = item.Tag?.ToString() ?? "";
                if (tag == "Select") _currentTool = ToolType.None;
                else if (tag == "Shape") _currentTool = _currentShapeType == "Pen" ? ToolType.Pen : ToolType.Shape;

                UpdateToolUI();
            }
        }

    }
}
