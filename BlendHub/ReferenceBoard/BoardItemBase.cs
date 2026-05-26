using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using src.Core;
using System;
using Windows.Foundation;

namespace BlendHub.ReferenceBoard
{
    public class BoardItemBase : ContentControl, IBoardItem
    {
        public event EventHandler? DeleteRequested;
        public event EventHandler? Selected;
        public event EventHandler<Windows.Foundation.Point>? Moved;
        public event EventHandler? BringForwardRequested;
        public event EventHandler? SendBackwardRequested;
        public event EventHandler<TransformChangedEventArgs>? TransformEnded;
        public event EventHandler? TransformChanged;
        public event EventHandler? LockedChanged;

        protected bool _isSelected = false;
        protected bool _isLocked = false;
        protected bool _showRotateHandle = true;
        protected bool _isResizing = false;
        protected bool _isRotating = false;
        protected bool _isDragging = false;
        protected Point _startResizePosition;
        protected Point _dragStartPointer;
        protected double _dragStartLeft;
        protected double _dragStartTop;
        protected double _startWidth;
        protected double _startHeight;
        protected double _aspectRatio = 1.0;
        protected double _startLeft;
        protected double _startTop;
        protected double _initialRotation;
        protected double _startPointerAngle;
        protected double _zoomFactor = 1.0;
        protected FrameworkElement? _activeHandle;
        protected Point _startPoint;
        protected Point _endPoint;
        protected FrameworkElement? _startHandle;
        protected FrameworkElement? _endHandle;
        protected FrameworkElement? _startVisual;
        protected FrameworkElement? _endVisual;

        protected RotateTransform? _rotationTransform;
        protected CompositeTransform? _toolbarScaleTransform;
        protected FrameworkElement? _floatingToolbar;
        protected FrameworkElement? _selectionOutline;
        protected FrameworkElement? _resizeHandles;
        protected Button? _lockButton;
        protected FontIcon? _lockIcon;
        protected ContentControl? _itemSpecificToolbarContent;
        protected FrameworkElement? _toolbarSeparator;
        protected Rectangle? _selectionRect;
        private FrameworkElement? _topLeftVisual, _topRightVisual, _bottomLeftVisual, _bottomRightVisual;
        private FrameworkElement? _rotateHandle;

        public static readonly DependencyProperty ItemSpecificToolbarContentProperty =
            DependencyProperty.Register("ItemSpecificToolbarContent", typeof(object), typeof(BoardItemBase), new PropertyMetadata(null, OnItemSpecificToolbarContentChanged));

        public static readonly DependencyProperty UseFloatingToolbarProperty =
            DependencyProperty.Register("UseFloatingToolbar", typeof(bool), typeof(BoardItemBase), new PropertyMetadata(true));

        private static void OnItemSpecificToolbarContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BoardItemBase item && item._itemSpecificToolbarContent != null)
            {
                if (item.UseFloatingToolbar)
                {
                    item._itemSpecificToolbarContent.Content = e.NewValue;
                }

                if (item._toolbarSeparator != null)
                    item._toolbarSeparator.Visibility = e.NewValue != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object ItemSpecificToolbarContent
        {
            get => GetValue(ItemSpecificToolbarContentProperty);
            set => SetValue(ItemSpecificToolbarContentProperty, value);
        }

        public bool UseFloatingToolbar
        {
            get => (bool)GetValue(UseFloatingToolbarProperty);
            set => SetValue(UseFloatingToolbarProperty, value);
        }

        public void DisconnectToolbarContent()
        {
            if (_itemSpecificToolbarContent != null)
            {
                _itemSpecificToolbarContent.Content = null;
            }
        }

        public BoardItemBase()
        {
            this.DefaultStyleKey = typeof(BoardItemBase);
            this.PointerPressed += OnPointerPressed;
            this.PointerEntered += OnPointerEntered;
            this.PointerExited += OnPointerExited;
        }

        double IBoardItem.Rotation
        {
            get => Rotation;
            set => Rotation = value;
        }

        public new double Rotation
        {
            get => _rotationTransform?.Angle ?? 0;
            set { if (_rotationTransform != null) _rotationTransform.Angle = value; }
        }

        public double ZoomFactor
        {
            get => _zoomFactor;
            set
            {
                _zoomFactor = value;
                UpdateToolbarScale();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                UpdateVisualState();
            }
        }

        public bool ShowRotateHandle
        {
            get => _showRotateHandle;
            set
            {
                _showRotateHandle = value;
                UpdateVisualState();
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                _isLocked = value;
                UpdateVisualState();
                LockedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Point StartPoint
        {
            get => _startPoint;
            set { _startPoint = value; UpdateHandles(); }
        }

        public Point EndPoint
        {
            get => _endPoint;
            set { _endPoint = value; UpdateHandles(); }
        }

        public bool MaintainAspectRatio { get; set; } = false;

        protected virtual void OnResizingStarted() { }
        protected virtual void OnResizing(double newWidth, double newHeight) { }

        /// <summary>
        /// Updates the scale and translation of all interactive handles and borders 
        /// to maintain a constant visual size and position regardless of canvas zoom.
        /// </summary>
        private void UpdateToolbarScale()
        {
            if (_zoomFactor <= 0) return;
            double invZ = 1.0 / _zoomFactor;

            // 1. Update the floating toolbar container scale
            if (_toolbarScaleTransform != null)
            {
                _toolbarScaleTransform.ScaleX = invZ;
                _toolbarScaleTransform.ScaleY = invZ;
            }

            // 2. Update corner resize handles
            // We want handles to be tucked exactly 2 screen-pixels inside the corners.
            // Formula: Translation = TargetScreenOffset / ZoomFactor
            double screenOffset = 2.0;
            double localShift = screenOffset * invZ;

            UpdateHandleTransform(_topLeftVisual, invZ, localShift, localShift);
            UpdateHandleTransform(_topRightVisual, invZ, -localShift, localShift);
            UpdateHandleTransform(_bottomLeftVisual, invZ, localShift, -localShift);
            UpdateHandleTransform(_bottomRightVisual, invZ, -localShift, -localShift);

            // 3. Update line endpoint handles (no offset needed as they anchor to line ends)
            UpdateHandleTransform(_startVisual, invZ, 0, 0);
            UpdateHandleTransform(_endVisual, invZ, 0, 0);

            // 4. Update rotate handle
            // The handle is positioned via Margin (Y=-48). To maintain its 30px screen-space 
            // center position, we translate it based on the current zoom.
            if (_rotateHandle != null)
            {
                double rotateHandleCenterOffset = 30.0;
                double translateY = rotateHandleCenterOffset * (1.0 - invZ);
                UpdateHandleTransform(_rotateHandle, invZ, 0, translateY);
            }

            // 5. Update selection border thickness
            if (_selectionRect != null)
            {
                _selectionRect.StrokeThickness = 2.0 * invZ;
            }

            UpdateToolbarPosition();
        }

        /// <summary>
        /// Applies scale and translation to a specific handle element.
        /// Reuses existing CompositeTransform to avoid GC pressure.
        /// </summary>
        private void UpdateHandleTransform(FrameworkElement? element, double scale, double tx, double ty)
        {
            if (element == null) return;

            if (element.RenderTransform is CompositeTransform ct)
            {
                ct.ScaleX = scale;
                ct.ScaleY = scale;
                ct.TranslateX = tx;
                ct.TranslateY = ty;
            }
            else
            {
                element.RenderTransform = new CompositeTransform
                {
                    ScaleX = scale,
                    ScaleY = scale,
                    TranslateX = tx,
                    TranslateY = ty
                };
            }
        }

        private void UpdateToolbarPosition()
        {
            if (_floatingToolbar == null) return;

            double toolbarHeight = _floatingToolbar.ActualHeight;
            Canvas.SetLeft(_floatingToolbar, 0);
            // Maintain a fixed visual gap of 12 pixels.
            // Since RenderTransformOrigin is (0, 1), the pivot is at the bottom of the toolbar.
            // The bottom's position in the canvas is (Canvas.Top + ActualHeight).
            // We want this bottom to be (12 / ZoomFactor) pixels above the item's top (0).
            Canvas.SetTop(_floatingToolbar, -toolbarHeight - (12.0 / _zoomFactor));
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _rotationTransform = GetTemplateChild("RotationTransform") as RotateTransform;
            _toolbarScaleTransform = GetTemplateChild("ToolbarScaleTransform") as CompositeTransform;
            _floatingToolbar = GetTemplateChild("FloatingToolbar") as FrameworkElement;
            _itemSpecificToolbarContent = GetTemplateChild("ItemSpecificToolbarContent") as ContentControl;
            _toolbarSeparator = GetTemplateChild("ToolbarSeparator") as FrameworkElement;

            if (_itemSpecificToolbarContent != null)
            {
                if (UseFloatingToolbar)
                {
                    _itemSpecificToolbarContent.Content = ItemSpecificToolbarContent;
                }

                if (_toolbarSeparator != null) _toolbarSeparator.Visibility = ItemSpecificToolbarContent != null ? Visibility.Visible : Visibility.Collapsed;
            }
            _selectionOutline = GetTemplateChild("SelectionOutline") as FrameworkElement;
            _selectionRect = GetTemplateChild("SelectionRect") as Rectangle;
            _resizeHandles = GetTemplateChild("ResizeHandles") as FrameworkElement;

            _topLeftVisual = GetTemplateChild("TopLeftVisual") as FrameworkElement;
            _topRightVisual = GetTemplateChild("TopRightVisual") as FrameworkElement;
            _bottomLeftVisual = GetTemplateChild("BottomLeftVisual") as FrameworkElement;
            _bottomRightVisual = GetTemplateChild("BottomRightVisual") as FrameworkElement;
            _rotateHandle = GetTemplateChild("RotateHandle") as FrameworkElement;

            _startHandle = GetTemplateChild("StartHandle") as FrameworkElement;
            _endHandle = GetTemplateChild("EndHandle") as FrameworkElement;
            _startVisual = GetTemplateChild("StartVisual") as FrameworkElement;
            _endVisual = GetTemplateChild("EndVisual") as FrameworkElement;

            _lockButton = GetTemplateChild("LockButton") as Button;
            if (_lockButton != null) _lockButton.Click += (s, e) => IsLocked = !IsLocked;
            _lockIcon = GetTemplateChild("LockIcon") as FontIcon;

            var deleteBtn = GetTemplateChild("DeleteButton") as Button;
            if (deleteBtn != null) deleteBtn.Click += (s, e) => DeleteRequested?.Invoke(this, EventArgs.Empty);

            var forwardBtn = GetTemplateChild("ForwardButton") as Button;
            if (forwardBtn != null) forwardBtn.Click += (s, e) => BringForwardRequested?.Invoke(this, EventArgs.Empty);

            var backwardBtn = GetTemplateChild("BackwardButton") as Button;
            if (backwardBtn != null) backwardBtn.Click += (s, e) => SendBackwardRequested?.Invoke(this, EventArgs.Empty);

            var rootGrid = GetTemplateChild("TemplateRootGrid") as Grid;
            if (rootGrid != null) rootGrid.SizeChanged += RootGrid_SizeChanged;

            if (_floatingToolbar != null) _floatingToolbar.SizeChanged += FloatingToolbar_SizeChanged;

            SetupHandle("TopLeft");
            SetupHandle("TopRight");
            SetupHandle("BottomLeft");
            SetupHandle("BottomRight");
            SetupHandle("Rotate");
            SetupHandle("Start");
            SetupHandle("End");

            UpdateVisualState();
            UpdateToolbarScale();
        }

        private void SetupHandle(string name)
        {
            if (GetTemplateChild(name + "Handle") is FrameworkElement handle)
            {
                handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Handle_PointerPressed), true);
                handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Handle_PointerMoved), true);
                handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Handle_PointerReleased), true);

                handle.PointerEntered += (s, e) =>
                {
                    if (IsLocked) return;
                    UpdateResizeCursor((s as FrameworkElement)?.Tag?.ToString() ?? "");
                };
                handle.PointerExited += (s, e) =>
                {
                    if (!_isResizing && !_isRotating) this.ProtectedCursor = null;
                };
            }
        }

        private void UpdateResizeCursor(string tag)
        {
            if (IsLocked) return;
            if (tag == "TopLeft" || tag == "BottomRight" || tag == "Start" || tag == "End")
                this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
            else if (tag == "TopRight" || tag == "BottomLeft")
                this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNortheastSouthwest);
            else if (tag == "Rotate")
                this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }

        protected virtual void UpdateVisualState()
        {
            var visibility = _isSelected ? Visibility.Visible : Visibility.Collapsed;
            bool isLineOrArrow = false;
            if (this is ShapeItemControl sic)
            {
                isLineOrArrow = sic.ShapeType == "Line" || sic.ShapeType == "Arrow";
            }

            if (_selectionOutline != null) _selectionOutline.Visibility = isLineOrArrow ? Visibility.Collapsed : visibility;
            if (_resizeHandles != null)
            {
                _resizeHandles.Visibility = IsLocked ? Visibility.Collapsed : visibility;
                if (GetTemplateChild("RegularHandles") is FrameworkElement regular) regular.Visibility = isLineOrArrow ? Visibility.Collapsed : Visibility.Visible;
                if (GetTemplateChild("LineHandles") is FrameworkElement line) line.Visibility = isLineOrArrow ? Visibility.Visible : Visibility.Collapsed;

                if (_rotateHandle != null)
                {
                    _rotateHandle.Visibility = (isLineOrArrow || !_showRotateHandle) ? Visibility.Collapsed : Visibility.Visible;
                }
            }

            if (_floatingToolbar != null) _floatingToolbar.Visibility = UseFloatingToolbar ? visibility : Visibility.Collapsed;

            if (_lockIcon != null)
            {
                _lockIcon.Glyph = IsLocked ? "\uE785" : "\uE72E";
            }
            UpdateHandles();
        }

        protected void UpdateHandles()
        {
            if (_startHandle != null)
            {
                Canvas.SetLeft(_startHandle, _startPoint.X);
                Canvas.SetTop(_startHandle, _startPoint.Y);
            }
            if (_endHandle != null)
            {
                Canvas.SetLeft(_endHandle, _endPoint.X);
                Canvas.SetTop(_endHandle, _endPoint.Y);
            }
        }

        private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // Only show move cursor during active drag (per user request)
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging && !_isResizing)
            {
                this.ProtectedCursor = null;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (IsLocked && _isSelected) return;

            // Prevent starting a drag if we clicked on the floating toolbar
            if (_floatingToolbar != null && _floatingToolbar.Visibility == Visibility.Visible)
            {
                var toolbarPtr = e.GetCurrentPoint(_floatingToolbar);
                if (toolbarPtr.Position.X >= 0 && toolbarPtr.Position.Y >= 0 &&
                    toolbarPtr.Position.X <= _floatingToolbar.ActualWidth &&
                    toolbarPtr.Position.Y <= _floatingToolbar.ActualHeight)
                {
                    return;
                }
            }

            var parent = this.Parent as UIElement;
            if (parent == null) return;

            var ptr = e.GetCurrentPoint(parent);
            if (ptr.Properties.IsLeftButtonPressed)
            {
                Selected?.Invoke(this, EventArgs.Empty);

                if (!IsLocked)
                {
                    _isDragging = true;
                    _dragStartPointer = ptr.Position;
                    _dragStartLeft = Canvas.GetLeft(this);
                    if (!double.IsFinite(_dragStartLeft)) _dragStartLeft = 0;
                    _dragStartTop = Canvas.GetTop(this);
                    if (!double.IsFinite(_dragStartTop)) _dragStartTop = 0;

                    if (this is TextItemControl txt)
                    {
                        _startFontSize = txt.FontSize;
                        _startMaxWidth = txt.MaxWidth;
                    }

                    try { this.CapturePointer(e.Pointer); } catch { }
                    e.Handled = true;
                }
            }
        }

        protected override void OnPointerMoved(PointerRoutedEventArgs e)
        {
            base.OnPointerMoved(e);
            if (IsLocked || !_isDragging) return;

            var parent = this.Parent as UIElement;
            if (parent == null) return;

            try
            {
                var ptr = e.GetCurrentPoint(parent);
                double deltaX = ptr.Position.X - _dragStartPointer.X;
                double deltaY = ptr.Position.Y - _dragStartPointer.Y;

                double nextLeft = _dragStartLeft + deltaX;
                double nextTop = _dragStartTop + deltaY;

                if (double.IsFinite(nextLeft) && double.IsFinite(nextTop))
                {
                    double currentLeft = Canvas.GetLeft(this);
                    double currentTop = Canvas.GetTop(this);
                    double dX = nextLeft - currentLeft;
                    double dY = nextTop - currentTop;

                    if (dX != 0 || dY != 0)
                    {
                        Canvas.SetLeft(this, nextLeft);
                        Canvas.SetTop(this, nextTop);
                        UpdateHandles();
                        this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
                        Moved?.Invoke(this, new Windows.Foundation.Point(dX, dY));
                        TransformChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
                e.Handled = true;
            }
            catch (ArgumentException)
            {
                // Catch potential WinRT argument exceptions during transient layout/parent states
            }
        }

        protected override void OnPointerReleased(PointerRoutedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isDragging)
            {
                _isDragging = false;
                ReleasePointerCapture(e.Pointer);

                double newLeft = Canvas.GetLeft(this);
                double newTop = Canvas.GetTop(this);

                if (Math.Abs(newLeft - _dragStartLeft) > 0.1 || Math.Abs(newTop - _dragStartTop) > 0.1)
                {
                    double currentFontSize = 0;
                    double currentMaxWidth = 0;
                    if (this is TextItemControl txt)
                    {
                        currentFontSize = txt.FontSize;
                        currentMaxWidth = txt.MaxWidth;
                    }

                    TransformEnded?.Invoke(this, new TransformChangedEventArgs
                    {
                        OldLeft = _dragStartLeft,
                        OldTop = _dragStartTop,
                        OldWidth = ActualWidth,
                        OldHeight = ActualHeight,
                        NewLeft = newLeft,
                        NewTop = newTop,
                        NewWidth = ActualWidth,
                        NewHeight = ActualHeight,
                        OldFontSize = _startFontSize,
                        NewFontSize = currentFontSize,
                        OldMaxWidth = _startMaxWidth,
                        NewMaxWidth = currentMaxWidth
                    });
                }
                this.ProtectedCursor = null;
                e.Handled = true;
            }
        }

        protected double _startFontSize = 0;
        protected double _startMaxWidth = 0;

        private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (IsLocked) return;
            _activeHandle = sender as FrameworkElement;
            if (_activeHandle == null) return;

            var parent = this.Parent as UIElement;
            if (parent == null) return;

            _isResizing = true;
            _startResizePosition = e.GetCurrentPoint(parent).Position;
            _startWidth = this.ActualWidth;
            _startHeight = this.ActualHeight;
            _startLeft = Canvas.GetLeft(this);
            _startTop = Canvas.GetTop(this);
            _aspectRatio = _startWidth / _startHeight;

            if (this is TextItemControl txt)
            {
                _startFontSize = txt.FontSize;
                _startMaxWidth = txt.MaxWidth;
            }

            try { _activeHandle.CapturePointer(e.Pointer); } catch { }

            string tag = _activeHandle.Tag?.ToString() ?? "";
            if (tag == "Rotate")
            {
                _isRotating = true;
                _isResizing = false;
                _initialRotation = this.Rotation;

                Point center = new Point(_startLeft + _startWidth / 2, _startTop + _startHeight / 2);
                Point pointerPos = e.GetCurrentPoint(parent).Position;
                _startPointerAngle = Math.Atan2(pointerPos.Y - center.Y, pointerPos.X - center.X) * 180 / Math.PI;

                e.Handled = true;
                return;
            }

            OnResizingStarted();
            e.Handled = true;
        }

        private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if ((!_isResizing && !_isRotating) || _activeHandle == null) return;

            UpdateResizeCursor(_activeHandle.Tag?.ToString() ?? "");

            var parent = this.Parent as UIElement;
            if (parent == null) return;

            var currentPosition = e.GetCurrentPoint(parent).Position;

            if (_isRotating)
            {
                Point center = new Point(_startLeft + _startWidth / 2, _startTop + _startHeight / 2);
                double currentAngle = Math.Atan2(currentPosition.Y - center.Y, currentPosition.X - center.X) * 180 / Math.PI;
                double deltaAngle = currentAngle - _startPointerAngle;
                while (deltaAngle > 180) deltaAngle -= 360;
                while (deltaAngle < -180) deltaAngle += 360;

                double newRotation = (_initialRotation + deltaAngle) % 360;
                if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift))
                {
                    newRotation = Math.Round(newRotation / 45) * 45;
                }

                this.Rotation = newRotation;
                TransformChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            double deltaX = (currentPosition.X - _startResizePosition.X);
            double deltaY = (currentPosition.Y - _startResizePosition.Y);

            double newWidth = _startWidth;
            double newHeight = _startHeight;
            double newLeft = _startLeft;
            double newTop = _startTop;

            string tag = _activeHandle.Tag?.ToString() ?? "";
            bool shift = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);
            bool keepRatio = MaintainAspectRatio || shift;

            if (tag == "BottomRight")
            {
                newWidth = Math.Max(20, _startWidth + deltaX);
                newHeight = keepRatio ? newWidth / _aspectRatio : Math.Max(20, _startHeight + deltaY);
                if (keepRatio && newHeight < 20) { newHeight = 20; newWidth = newHeight * _aspectRatio; }
            }
            else if (tag == "BottomLeft")
            {
                newWidth = Math.Max(20, _startWidth - deltaX);
                newHeight = keepRatio ? newWidth / _aspectRatio : Math.Max(20, _startHeight + deltaY);
                if (keepRatio && newHeight < 20) { newHeight = 20; newWidth = newHeight * _aspectRatio; }
                newLeft = _startLeft + (_startWidth - newWidth);
            }
            else if (tag == "TopRight")
            {
                newWidth = Math.Max(20, _startWidth + deltaX);
                newHeight = keepRatio ? newWidth / _aspectRatio : Math.Max(20, _startHeight - deltaY);
                if (keepRatio && newHeight < 20) { newHeight = 20; newWidth = newHeight * _aspectRatio; }
                newTop = _startTop + (_startHeight - newHeight);
            }
            else if (tag == "TopLeft")
            {
                newWidth = Math.Max(20, _startWidth - deltaX);
                newHeight = keepRatio ? newWidth / _aspectRatio : Math.Max(20, _startHeight - deltaY);
                if (keepRatio && newHeight < 20) { newHeight = 20; newWidth = newHeight * _aspectRatio; }
                newLeft = _startLeft + (_startWidth - newWidth);
                newTop = _startTop + (_startHeight - newHeight);
            }
            else if (tag == "Start" || tag == "End")
            {
                double padding = 30;
                // Get absolute current positions
                Point startAbs = new Point(Canvas.GetLeft(this) + _startPoint.X, Canvas.GetTop(this) + _startPoint.Y);
                Point endAbs = new Point(Canvas.GetLeft(this) + _endPoint.X, Canvas.GetTop(this) + _endPoint.Y);

                // Update the point being moved
                if (tag == "Start")
                    startAbs = new Point(_startResizePosition.X + deltaX, _startResizePosition.Y + deltaY);
                else
                    endAbs = new Point(_startResizePosition.X + deltaX, _startResizePosition.Y + deltaY);

                // Recalculate bounding box
                double minX = Math.Min(startAbs.X, endAbs.X) - padding;
                double minY = Math.Min(startAbs.Y, endAbs.Y) - padding;
                double maxX = Math.Max(startAbs.X, endAbs.X) + padding;
                double maxY = Math.Max(startAbs.Y, endAbs.Y) + padding;

                // Update control
                Canvas.SetLeft(this, minX);
                Canvas.SetTop(this, minY);
                this.Width = Math.Max(20, maxX - minX);
                this.Height = Math.Max(20, maxY - minY);

                // Update relative points
                _startPoint = new Point(startAbs.X - minX, startAbs.Y - minY);
                _endPoint = new Point(endAbs.X - minX, endAbs.Y - minY);

                UpdateHandles();
                OnResizing(0, 0); // Trigger update in subclasses
            }

            if (tag != "Start" && tag != "End" && newWidth > 20 && newHeight > 20)
            {
                this.Width = newWidth;
                this.Height = newHeight;
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
                OnResizing(newWidth, newHeight);
                TransformChanged?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }

        private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing || _isRotating)
            {
                _isResizing = false;
                _isRotating = false;
                _activeHandle?.ReleasePointerCapture(e.Pointer);

                double currentFontSize = 0;
                double currentMaxWidth = 0;
                if (this is TextItemControl txt)
                {
                    currentFontSize = txt.FontSize;
                    currentMaxWidth = txt.MaxWidth;
                }

                TransformEnded?.Invoke(this, new TransformChangedEventArgs
                {
                    OldLeft = _startLeft,
                    OldTop = _startTop,
                    OldWidth = _startWidth,
                    OldHeight = _startHeight,
                    NewLeft = Canvas.GetLeft(this),
                    NewTop = Canvas.GetTop(this),
                    NewWidth = this.ActualWidth,
                    NewHeight = this.ActualHeight,
                    OldFontSize = _startFontSize,
                    NewFontSize = currentFontSize,
                    OldMaxWidth = _startMaxWidth,
                    NewMaxWidth = currentMaxWidth,
                    OldRotation = _initialRotation,
                    NewRotation = this.Rotation
                });
                _activeHandle = null;
                this.ProtectedCursor = null;
                e.Handled = true;
            }
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_floatingToolbar != null)
            {
                _floatingToolbar.Visibility = (IsSelected && !IsLocked && UseFloatingToolbar) ? Visibility.Visible : Visibility.Collapsed;
                if (_floatingToolbar.Visibility == Visibility.Visible)
                {
                    UpdateToolbarPosition();
                }
            }
            if (_rotationTransform != null)
            {
                _rotationTransform.CenterX = e.NewSize.Width / 2;
                _rotationTransform.CenterY = e.NewSize.Height / 2;
            }
            UpdateToolbarPosition();
        }

        private void FloatingToolbar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateToolbarPosition();


        public void Translate(double dx, double dy)
        {
            double nextLeft = Canvas.GetLeft(this) + dx;
            double nextTop = Canvas.GetTop(this) + dy;
            if (double.IsFinite(nextLeft) && double.IsFinite(nextTop))
            {
                Canvas.SetLeft(this, nextLeft);
                Canvas.SetTop(this, nextTop);
                UpdateHandles();
                TransformChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
