using BlendHub;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using BlendHub.ReferenceBoard;
using src.Core;
using src.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using WinRT.Interop;

namespace BlendHub.ReferenceBoard
{
    public class ShapeToolInfo
    {
        public string Glyph { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }

    public class ColorItem
    {
        public Brush? Brush { get; set; }
        public bool IsNone { get; set; }
        public string? Tag { get; set; }
    }

    public sealed partial class ReferenceBoard : Page
    {
        private enum ToolType { None, Pen, Shape }
        private ToolType _currentTool = ToolType.None;
        private string? _currentShapeType;

        internal Color _shapeFillColor = Microsoft.UI.Colors.Transparent;
        internal Color _shapeStrokeColor = BoardPersistence.HexToColor("#8CE0FF");

        private FrameworkElement? _drawingShape;
        private Point _drawingStartPoint;
        private Random _random = new Random();
        private bool _isDragging = false;
        private Point _lastPointerPosition;
        private Point _currentMouseCanvasPosition;
        private HistoryManager _historyManager = new HistoryManager();
        private string? _currentBoardName;
        private bool _hasUnsavedChanges = false;
        private Polyline? _currentPolyline;
        private IBoardItem? _selectedItem;
        private bool _isUpdatingUI = false;
        private bool _isShapeFillNone = true;
        private bool _isShapeOutlineNone = false;
        private bool _isSelecting = false;
        private bool _isMovingSelection = false;
        private Rectangle? _selectionMarquee;
        private List<IBoardItem> _selectedItems = new List<IBoardItem>();
        private List<IBoardItem> _selectedItemsAtStart = new List<IBoardItem>();

        private IntPtr _windowHandle;
        public IntPtr WindowHandle
        {
            get => _windowHandle == IntPtr.Zero ? WindowNative.GetWindowHandle(App.MainWindow) : _windowHandle;
            set => _windowHandle = value;
        }
        public Action<string>? TitleChanged { get; set; }
        public FrameworkElement TitleBarElement => AppTitleBar;

        public ReferenceBoard()
        {
            _isUpdatingUI = true;
            this.InitializeComponent();
            InitializeMainTools();
            InitializeColorPalette();
            _currentTool = ToolType.None;
            RefreshSavedProjectsList();
            _isUpdatingUI = false;
            UpdateShapePreviewUI();

            this.Loaded += (s, e) => this.Focus(FocusState.Programmatic);
        }

        private void InitializeMainTools()
        {
            _currentShapeType = "Pen"; // Ensure a default shape type is set
        }

        private void InitializeColorPalette()
        {
            var colors = new List<string> {
                "#000000", "#FFFFFF", "#C4C4C4", "#999999", "#737373", "#4D4D4D",
                "#9E005D", "#FF0000", "#F26522", "#F7941D", "#FFC20E", "#FFF200",
                "#8CC63F", "#39B54A", "#009245", "#00AEEF", "#0054A6", "#3F0099",
                "#662D91", "#491173", "#FAD9BE", "#BA9470", "#8A5B3B", "#5C3D2B",
                "#FF8AE2", "#FFC184", "#FAF18C", "#8DF29D", "#8CE0FF", "#C6BAFF"
            };

            var items = colors.Select(c => new ColorItem { Brush = new SolidColorBrush(BoardPersistence.HexToColor(c)), Tag = c }).ToList();

            var shapeColors = new List<string>(colors);
            shapeColors.Remove("#4D4D4D");
            var shapeItems = new List<ColorItem> { new ColorItem { IsNone = true, Tag = "Transparent" } };
            shapeItems.AddRange(shapeColors.Select(c => new ColorItem { Brush = new SolidColorBrush(BoardPersistence.HexToColor(c)), Tag = c }));

            if (ShapeFillColorPicker != null)
            {
                ShapeFillColorPicker.ItemsSource = shapeItems;
                var currentHex = BoardPersistence.ColorToHex(_shapeFillColor);
                var selected = shapeItems.FirstOrDefault(i => i.Tag == currentHex || (i.Brush as SolidColorBrush)?.Color == _shapeFillColor || (i.IsNone && _shapeFillColor == Microsoft.UI.Colors.Transparent));
                if (selected != null) ShapeFillColorPicker.SelectedItem = selected;
            }

            if (ShapeOutlineColorPicker != null)
            {
                ShapeOutlineColorPicker.ItemsSource = shapeItems;
                var currentHex = BoardPersistence.ColorToHex(_shapeStrokeColor);
                var selected = shapeItems.FirstOrDefault(i => i.Tag == currentHex || (i.Brush as SolidColorBrush)?.Color == _shapeStrokeColor || (i.IsNone && _shapeStrokeColor == Microsoft.UI.Colors.Transparent));
                if (selected != null) ShapeOutlineColorPicker.SelectedItem = selected;
            }
        }

        private void MarkAsUnsaved()
        {
            _hasUnsavedChanges = true;
            string title = _currentBoardName != null ? $"Reference Board - {_currentBoardName}*" : "Reference Board - Unsaved*";
            TitleChanged?.Invoke(title);
        }

        public async Task<bool> CheckUnsavedChangesAsync()
        {
            if (!_hasUnsavedChanges) return true;
            var dialog = new ContentDialog { Title = "Unsaved Changes", Content = "You have unsaved changes. Do you want to save before continuing?", PrimaryButtonText = "Save", SecondaryButtonText = "Don't Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = this.XamlRoot, Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) { await SaveBoardAsync(); return !_hasUnsavedChanges; }
            return result == ContentDialogResult.Secondary;
        }

        private void UpdateToolUI()
        {
            _isUpdatingUI = true;
            try
            {
                bool showRotate = _selectedItems.Count <= 1;
                foreach (var child in InfiniteCanvasArea.Children)
                {
                    if (child is IBoardItem bi) bi.ShowRotateHandle = showRotate;
                }

                UpdateMainToolSelection();
                UpdateToolbarVisibilities();
                UpdateItemSpecificToolbars();
                UpdateSelectedItemVisuals();
                UpdateDrawingModeState();
            }
            finally { _isUpdatingUI = false; }

            // If we just switched to a drawing tool, ensure no items remain selected
            if (_currentTool != ToolType.None) DeselectAll();
        }

        private void UpdateMainToolSelection()
        {
            if (MainToolSegmented != null)
            {
                string targetTag = _currentTool == ToolType.None ? "Select" : "Shape";
                MainToolSegmented.SelectedItem = MainToolSegmented.Items
                    .Cast<CommunityToolkit.WinUI.Controls.SegmentedItem>()
                    .FirstOrDefault(i => i.Tag.ToString() == targetTag);
            }

            if (ShapeSegmented != null)
            {
                ShapeSegmented.SelectedItem = ShapeSegmented.Items
                    .Cast<CommunityToolkit.WinUI.Controls.SegmentedItem>()
                    .FirstOrDefault(i => i.Tag.ToString() == _currentShapeType);
            }
        }

        private void UpdateToolbarVisibilities()
        {
            bool isDrawing = _currentTool == ToolType.Shape || _currentTool == ToolType.Pen;
            bool isShapeSelected = _selectedItem is ShapeItemControl || _selectedItem is PenItemControl;
            bool hasSelection = _selectedItem != null || _selectedItems.Count > 0;

            if (ShapeCreationTools != null)
                ShapeCreationTools.Visibility = isDrawing ? Visibility.Visible : Visibility.Collapsed;

            if (ShapeAppearanceControls != null)
                ShapeAppearanceControls.Visibility = (isDrawing || isShapeSelected) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemManagementTools != null)
                ItemManagementTools.Visibility = (hasSelection && !isDrawing) ? Visibility.Visible : Visibility.Collapsed;

            if (CursorInfoPanel != null)
            {
                bool isOtherToolsVisible = isDrawing || (hasSelection && !isDrawing);
                CursorInfoPanel.Visibility = isOtherToolsVisible ? Visibility.Collapsed : Visibility.Visible;
            }

            if (ItemInfoPanel != null)
            {
                if (_selectedItem is ImageItemControl img && !isDrawing)
                {
                    ItemInfoPanel.Visibility = Visibility.Visible;
                    double w = img.Width > 0 ? img.Width : img.ActualWidth;
                    double h = img.Height > 0 ? img.Height : img.ActualHeight;
                    ItemSizeText.Text = $"{(int)Math.Round(w)} x {(int)Math.Round(h)} px";
                }
                else
                {
                    ItemInfoPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateItemSpecificToolbars()
        {
            bool isDrawing = _currentTool == ToolType.Shape || _currentTool == ToolType.Pen;
            if (isDrawing)
            {
                if (TextItemToolbarContent != null) TextItemToolbarContent.Content = null;
                if (ImageItemToolbarContent != null) ImageItemToolbarContent.Content = null;
                return;
            }

            if (TextItemToolbarContent != null)
            {
                if (_selectedItem is TextItemControl txt)
                {
                    txt.DisconnectToolbarContent();
                    TextItemToolbarContent.Content = txt.ItemSpecificToolbarContent;
                }
                else TextItemToolbarContent.Content = null;
            }

            if (ImageItemToolbarContent != null)
            {
                if (_selectedItem is ImageItemControl img)
                {
                    img.DisconnectToolbarContent();
                    ImageItemToolbarContent.Content = img.ItemSpecificToolbarContent;
                }
                else ImageItemToolbarContent.Content = null;
            }
        }

        private void UpdateSelectedItemVisuals()
        {
            if (_selectedItem == null) return;

            if (ItemLockIcon != null) ItemLockIcon.Glyph = _selectedItem.IsLocked ? "\uE785" : "\uE72E";
            if (ItemLockButton != null) ItemLockButton.IsChecked = _selectedItem.IsLocked;

            UpdateZIndexButtonStates();
        }

        private void UpdateDrawingModeState()
        {
            bool isDrawing = _currentTool == ToolType.Shape || _currentTool == ToolType.Pen;

            // 1. Update Fill dropdown availability
            if (FillDropdown != null)
            {
                if (isDrawing)
                {
                    FillDropdown.IsEnabled = !(_currentShapeType == "Line" || _currentShapeType == "Pen" || _currentShapeType == "Arrow");
                }
                else if (_selectedItem is ShapeItemControl sic)
                {
                    FillDropdown.IsEnabled = !(sic.ShapeType == "Line" || sic.ShapeType == "Arrow");
                }
                else if (_selectedItem is PenItemControl)
                {
                    FillDropdown.IsEnabled = false;
                }
                else
                {
                    FillDropdown.IsEnabled = true;
                }
                FillDropdown.Opacity = FillDropdown.IsEnabled ? 1.0 : 0.5;
            }

            // 2. Disable hit testing on items when drawing to allow drawing over them
            foreach (var child in InfiniteCanvasArea.Children)
            {
                if (child is FrameworkElement fe)
                {
                    fe.IsHitTestVisible = !isDrawing;
                }
            }
        }

        private void WireUpEvents(UIElement element)
        {
            element.PointerPressed += (s, e) => e.Handled = true;
            if (element is IBoardItem item)
            {
                item.DeleteRequested += (s, ev) => { _historyManager.ExecuteCommand(new RemoveItemCommand(InfiniteCanvasArea, element)); MarkAsUnsaved(); };
                item.BringForwardRequested += (s, ev) => BringForward(element);
                item.SendBackwardRequested += (s, ev) => SendBackward(element);
                item.Selected += (s, ev) =>
                {
                    bool isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    bool isShiftPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                    if (!isCtrlPressed && !isShiftPressed)
                    {
                        if (_selectedItems.Contains(item)) return;
                        DeselectAll();
                    }

                    if (!item.IsSelected)
                    {
                        item.IsSelected = true;
                        if (!_selectedItems.Contains(item)) _selectedItems.Add(item);
                    }
                    else if (isCtrlPressed || isShiftPressed)
                    {
                        item.IsSelected = false;
                        _selectedItems.Remove(item);
                    }

                    _selectedItem = _selectedItems.Count == 1 ? _selectedItems[0] : null;
                    UpdateToolUI();
                };
                item.Moved += (s, delta) =>
                {
                    if (_isMovingSelection || !_selectedItems.Contains(item)) return;
                    _isMovingSelection = true;
                    try
                    {
                        foreach (var otherItem in _selectedItems)
                        {
                            if (otherItem != item)
                            {
                                otherItem.Translate(delta.X, delta.Y);
                            }
                        }
                    }
                    finally { _isMovingSelection = false; }
                };
                item.TransformEnded += (s, ev) => { if (element is FrameworkElement fe) RecordTransform(fe, ev); };
                item.TransformChanged += (s, ev) => UpdateToolUI();
                item.ZoomFactor = CanvasTransform.ScaleX;

                if (item is ITextItem textItem)
                    textItem.TextChangedEnded += (s, ev) => _historyManager.ExecuteCommand(new TextChangeCommand(textItem, ev.OldText, ev.NewText));

                // Synchronize appearance UI when a shape or pen is selected
                if (element is ShapeItemControl || element is PenItemControl)
                {
                    item.Selected += (s, ev) => SyncUIFromSelectedItem(item);
                }
            }
        }

        /// <summary>
        /// Updates the property sliders and color pickers in the UI to match the selected item's current state.
        /// </summary>
        private void SyncUIFromSelectedItem(IBoardItem item)
        {
            Shape? shape = null;
            if (item is ShapeItemControl sic) shape = sic.Shape as Shape;
            else if (item is PenItemControl pic) shape = pic.Shape as Shape;

            if (shape == null) return;

            _isUpdatingUI = true;
            try
            {
                _shapeFillColor = (shape.Fill as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent;
                _shapeStrokeColor = (shape.Stroke as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.White;

                if (ShapeFillPreview != null) ShapeFillPreview.Foreground = shape.Fill;
                if (ShapeOutlinePreview != null) ShapeOutlinePreview.Foreground = shape.Stroke;
                if (ShapeThicknessSlider != null) ShapeThicknessSlider.Value = shape.StrokeThickness;

                if (ShapeOpacitySlider != null)
                    ShapeOpacitySlider.Alpha = _shapeFillColor.A * 100 / 255.0;

                if (ShapeOutlineOpacitySlider != null)
                    ShapeOutlineOpacitySlider.Alpha = _shapeStrokeColor.A * 100 / 255.0;

                UpdateToolUI();
                UpdateShapePreviewUI();
            }
            finally { _isUpdatingUI = false; }
        }

        private void BringForward(UIElement element) { int maxZ = 0; foreach (UIElement child in InfiniteCanvasArea.Children) maxZ = Math.Max(maxZ, Canvas.GetZIndex(child)); _historyManager.ExecuteCommand(new ZIndexCommand(element, Canvas.GetZIndex(element), maxZ + 1)); }
        private void SendBackward(UIElement element) { int minZ = 0; foreach (UIElement child in InfiniteCanvasArea.Children) if (child != element) minZ = Math.Min(minZ, Canvas.GetZIndex(child)); _historyManager.ExecuteCommand(new ZIndexCommand(element, Canvas.GetZIndex(element), minZ - 1)); }
        private void RecordTransform(FrameworkElement control, TransformChangedEventArgs ev) { _historyManager.ExecuteCommand(new TransformCommand(control, ev.OldLeft, ev.OldTop, ev.OldWidth, ev.OldHeight, ev.NewLeft, ev.NewTop, ev.NewWidth, ev.NewHeight, ev.OldFontSize, ev.NewFontSize, ev.OldMaxWidth, ev.NewMaxWidth, ev.OldRotation, ev.NewRotation)); MarkAsUnsaved(); }

        private void AddBoardItem(UIElement element, Point? canvasPosition = null)
        {
            WireUpEvents(element);

            Point pos;
            if (canvasPosition.HasValue)
            {
                pos = canvasPosition.Value;
            }
            else
            {
                // Calculate center of current viewport in canvas coordinates
                double centerX = (CanvasContainer.ActualWidth / 2 - CanvasTransform.TranslateX) / CanvasTransform.ScaleX;
                double centerY = (CanvasContainer.ActualHeight / 2 - CanvasTransform.TranslateY) / CanvasTransform.ScaleY;

                double w = 200, h = 50;
                if (element is FrameworkElement fe)
                {
                    // Use ActualWidth/Height if already measured, else fallback
                    w = fe.Width > 0 ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 200);
                    h = fe.Height > 0 ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 50);
                }
                pos = new Point(centerX - w / 2, centerY - h / 2);
            }

            Canvas.SetLeft(element, pos.X);
            Canvas.SetTop(element, pos.Y);
            InfiniteCanvasArea.Children.Add(element);
            _historyManager.ExecuteCommand(new AddItemCommand(InfiniteCanvasArea, element));
            MarkAsUnsaved();
            UpdateToolUI();
        }

        private void AddTextButton_Click(object sender, RoutedEventArgs e)
        {
            var txt = new TextItemControl { Text = "Double click to edit" };

            Point? pos = null;
            // If triggered by Ctrl+T shortcut (sender is this), use mouse position
            if (ReferenceEquals(sender, this)) pos = _currentMouseCanvasPosition;

            AddBoardItem(txt, pos);
        }

        private void CommonButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || !(sender is FrameworkElement fe) || !(_selectedItem is UIElement element)) return;

            if (fe.Name.Contains("Forward")) BringForward(element);
            else if (fe.Name.Contains("Backward")) SendBackward(element);
            else if (fe.Name.Contains("Delete")) { _historyManager.ExecuteCommand(new RemoveItemCommand(InfiniteCanvasArea, element)); MarkAsUnsaved(); DeselectAll(); }
            else if (fe.Name.Contains("Lock"))
            {
                _selectedItem.IsLocked = !_selectedItem.IsLocked;
                if (ItemLockIcon != null) ItemLockIcon.Glyph = _selectedItem.IsLocked ? "\uE785" : "\uE72E";
                UpdateVisualStateForItem(_selectedItem);
                UpdateToolUI();
            }
            UpdateZIndexButtonStates();
        }

        private void UpdateVisualStateForItem(IBoardItem item)
        {
            if (item is BoardItemBase bib)
            {
                // Trigger the internal UpdateVisualState of the item
                // Since it's protected, we rely on the fact that IsLocked property change 
                // should trigger it if implemented correctly in BoardItemBase.
            }
        }

        private void UpdateZIndexButtonStates()
        {
            if (ForwardButton == null || BackwardButton == null) return;

            if (_selectedItem == null || !(_selectedItem is UIElement element))
            {
                ForwardButton.IsEnabled = false;
                BackwardButton.IsEnabled = false;
                return;
            }

            int currentZ = Canvas.GetZIndex(element);
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            int count = 0;

            foreach (UIElement child in InfiniteCanvasArea.Children)
            {
                int z = Canvas.GetZIndex(child);
                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
                count++;
            }

            if (count <= 1)
            {
                ForwardButton.IsEnabled = false;
                BackwardButton.IsEnabled = false;
            }
            else
            {
                bool canForward = (currentZ < maxZ) || (minZ == maxZ);
                bool canBackward = (currentZ > minZ) || (minZ == maxZ);

                ForwardButton.IsEnabled = canForward;
                BackwardButton.IsEnabled = canBackward;
            }
        }

        private async void ImportFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (dataPackageView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
                {
                    var bitmapStreamReference = await dataPackageView.GetBitmapAsync();
                    using (var stream = await bitmapStreamReference.OpenReadAsync())
                    {
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(stream);

                        var imgControl = new ImageItemControl { Source = bitmap };

                        // Save to assets folder
                        string filePath = await BoardPersistence.SaveStreamToAssetsAsync(stream.AsStreamForRead(), ".png", _currentBoardName);

                        imgControl.ImagePath = filePath;
                        AddBoardItem(imgControl);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently fail or log for now, as clipboard access can be finicky
                System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}");
            }
        }
    }

    public class BoolToDoubleConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool b = value is bool && (bool)value;
            if (parameter as string == "Inverse") b = !b;
            return b ? 1.0 : 0.0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class ZoomFactorConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d) return $"{(int)Math.Round(d * 100)}%";
            if (value is float f) return $"{(int)Math.Round(f * 100)}%";
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
