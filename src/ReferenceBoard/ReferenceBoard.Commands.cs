using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

namespace BlendHub.ReferenceBoard
{
    public sealed partial class ReferenceBoard : Page
    {
        private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveBoardAsync();

        private async Task SaveBoardAsync()
        {
            string? boardNameBeforeSave = _currentBoardName;
            if (string.IsNullOrEmpty(_currentBoardName))
            {
                var nameInput = new TextBox { PlaceholderText = "Project Name", Text = "" };
                var stack = new StackPanel { Spacing = 12 };
                stack.Children.Add(new TextBlock { Text = "Enter a name for your reference board:" });
                stack.Children.Add(nameInput);

                var dialog = new ContentDialog
                {
                    Title = "Save Board",
                    Content = stack,
                    PrimaryButtonText = "Save",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _currentBoardName = nameInput.Text;

                    // Move assets from Temp to the new board folder
                    if (!string.IsNullOrEmpty(_currentBoardName))
                    {
                        string tempFolder = BoardPersistence.GetAssetsFolder(null);
                        string destFolder = BoardPersistence.GetAssetsFolder(_currentBoardName);

                        if (Directory.Exists(tempFolder))
                        {
                            foreach (var file in Directory.GetFiles(tempFolder))
                            {
                                string fileName = System.IO.Path.GetFileName(file);
                                string destFile = System.IO.Path.Combine(destFolder, fileName);
                                try
                                {
                                    if (File.Exists(destFile)) File.Delete(destFile);
                                    File.Move(file, destFile);

                                    // Update paths for items on canvas
                                    foreach (var child in InfiniteCanvasArea.Children)
                                    {
                                        if (child is ImageItemControl img && img.ImagePath == file)
                                        {
                                            img.ImagePath = destFile;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                else return;
            }

            var board = new BoardData
            {
                Name = _currentBoardName,
                ViewportX = CanvasTransform.TranslateX,
                ViewportY = CanvasTransform.TranslateY,
                ZoomFactor = (float)CanvasTransform.ScaleX
            };
            Func<double, double> S = (v) => double.IsFinite(v) ? v : 0;

            foreach (UIElement child in InfiniteCanvasArea.Children)
            {
                var item = new BoardItemData { Left = S(Canvas.GetLeft(child)), Top = S(Canvas.GetTop(child)), ZIndex = Canvas.GetZIndex(child) };
                if (child is IBoardItem bi) item.Rotation = S(bi.Rotation);

                if (child is ImageItemControl img) { item.Type = BoardItemType.Image; item.Content = img.ImagePath; item.Width = S(img.Width); item.Height = S(img.Height); }
                else if (child is TextItemControl txt) { item.Type = BoardItemType.Text; item.Content = txt.Text; item.FontSize = txt.FontSize; item.TextColorHex = BoardPersistence.ColorToHex(txt.TextColor); item.BgColorHex = BoardPersistence.ColorToHex(txt.BackgroundColor); }
                else if (child is PenItemControl pic) { item.Type = BoardItemType.Drawing; item.Points = pic.Points?.Select(p => new PointData { X = S(p.X), Y = S(p.Y) }).ToList(); item.Width = S(pic.Width); item.Height = S(pic.Height); if (pic.Shape is Shape s) item.TextColorHex = BoardPersistence.ColorToHex((s.Stroke as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Red); }
                else if (child is Polyline poly) { item.Type = BoardItemType.Drawing; item.Points = poly.Points.Select(p => new PointData { X = S(p.X), Y = S(p.Y) }).ToList(); item.TextColorHex = BoardPersistence.ColorToHex((poly.Stroke as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Red); }
                else if (child is ShapeItemControl sic)
                {
                    item.Type = BoardItemType.Shape;
                    item.ShapeType = sic.ShapeType;
                    item.Width = S(sic.Width);
                    item.Height = S(sic.Height);

                    if (sic.ShapeType == "Line" || sic.ShapeType == "Arrow")
                    {
                        item.Points = new List<PointData>
                        {
                            new PointData { X = sic.StartPoint.X, Y = sic.StartPoint.Y },
                            new PointData { X = sic.EndPoint.X, Y = sic.EndPoint.Y }
                        };
                    }

                    if (sic.Shape is Shape s)
                    {
                        item.BgColorHex = BoardPersistence.ColorToHex((s.Fill as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent);
                        item.TextColorHex = BoardPersistence.ColorToHex((s.Stroke as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.White);
                        item.FontSize = s.StrokeThickness;
                    }
                }
                else continue;
                board.Items.Add(item);
            }

            bool isFirstSave = string.IsNullOrEmpty(boardNameBeforeSave);

            try
            {
                await BoardPersistence.SaveBoardAsync(board);
                _hasUnsavedChanges = false;
                TitleChanged?.Invoke($"Reference Board - {_currentBoardName}");
                RefreshSavedProjectsList();

                // Play visual feedback animation
                SaveSuccessAnimation.Begin();

                if (isFirstSave)
                {
                    // Show success dialog ONLY on the first save
                    var successDialog = new ContentDialog
                    {
                        Title = "Success",
                        Content = $"Board '{_currentBoardName}' saved successfully.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    // Save notification could be added to status bar here if desired
                }

                // Refresh list to update selection
                if (ProjectsComboBox != null)
                {
                    ProjectsComboBox.PlaceholderText = "Select Board";
                }
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Save Error",
                    Content = $"Failed to save board: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
                };
                await errorDialog.ShowAsync();
            }
        }

        private async void ClearBoard_Click(object sender, RoutedEventArgs e)
        {
            if (!await CheckUnsavedChangesAsync()) return;
            InfiniteCanvasArea.Children.Clear();
            _historyManager = new HistoryManager();

            // Reset ComboBox state for New Board
            if (ProjectsComboBox != null)
            {
                ProjectsComboBox.SelectedIndex = -1;
                ProjectsComboBox.PlaceholderText = "New Board";
            }
        }

        private void EraseAllMarkups_Click(object sender, RoutedEventArgs e)
        {
            var markups = InfiniteCanvasArea.Children
                .Where(c => c is Polyline || c is ShapeItemControl || c is PenItemControl)
                .ToList();

            if (markups.Count == 0) return;

            var composite = new CompositeCommand();
            foreach (var markup in markups)
            {
                composite.Add(new RemoveItemCommand(InfiniteCanvasArea, markup));
            }

            _historyManager.ExecuteCommand(composite);
            MarkAsUnsaved();
        }

        private async void DeleteBoard_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentBoardName)) return;
            var dialog = new ContentDialog { Title = "Delete Board", Content = "Are you sure you want to permanently delete this board?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = this.XamlRoot, Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) { BoardPersistence.DeleteBoard(_currentBoardName); ClearBoard_Click(sender, e); RefreshSavedProjectsList(); }
        }


        private void RefreshSavedProjectsList()
        {
            if (ProjectsComboBox == null) return;

            var names = BoardPersistence.GetSavedBoardNames();
            _isUpdatingUI = true;
            try
            {
                ProjectsComboBox.ItemsSource = names;
                if (!string.IsNullOrEmpty(_currentBoardName))
                {
                    ProjectsComboBox.SelectedItem = names.FirstOrDefault(n => n == _currentBoardName);
                }
            }
            finally { _isUpdatingUI = false; }

            // If no board is active and there's exactly one saved board, select it
            if (string.IsNullOrEmpty(_currentBoardName) && names.Count == 1)
            {
                ProjectsComboBox.SelectedIndex = 0;
            }
        }

        private void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            if (ProjectsComboBox.SelectedItem is string name)
            {
                LoadBoard(name);
            }
        }

        private async void LoadBoard(string name)
        {
            if (!await CheckUnsavedChangesAsync()) return;
            var board = await BoardPersistence.LoadBoardAsync(name);
            if (board == null) return;
            InfiniteCanvasArea.Children.Clear();
            _historyManager = new HistoryManager(); _currentBoardName = board.Name; _hasUnsavedChanges = false;
            foreach (var itemData in board.Items)
            {
                UIElement? control = null;
                if (itemData.Type == BoardItemType.Image && !string.IsNullOrEmpty(itemData.Content))
                {
                    var img = new ImageItemControl { ImagePath = itemData.Content, Width = itemData.Width, Height = itemData.Height };
                    try
                    {
                        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(itemData.Content);
                        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(stream);
                        img.Source = bitmap;
                    }
                    catch { }
                    WireUpEvents(img);
                    control = img;
                }
                else if (itemData.Type == BoardItemType.Text)
                {
                    var txt = new TextItemControl { Text = itemData.Content ?? "" };
                    if (itemData.FontSize > 0) txt.FontSize = itemData.FontSize;
                    if (itemData.TextColorHex != null) txt.TextColor = BoardPersistence.HexToColor(itemData.TextColorHex);
                    if (itemData.BgColorHex != null) txt.BackgroundColor = BoardPersistence.HexToColor(itemData.BgColorHex);
                    WireUpEvents(txt);
                    control = txt;
                }
                else if (itemData.Type == BoardItemType.Drawing && itemData.Points != null)
                {
                    var poly = new Polyline { Stroke = new SolidColorBrush(BoardPersistence.HexToColor(itemData.TextColorHex ?? "#FFFF0000")), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    foreach (var p in itemData.Points) poly.Points.Add(new Point(p.X, p.Y));

                    var path = ConvertPolylineToPath(poly);
                    var pic = new PenItemControl { Shape = path, Points = poly.Points.ToList(), Width = itemData.Width > 0 ? itemData.Width : path.Width, Height = itemData.Height > 0 ? itemData.Height : path.Height, Rotation = itemData.Rotation };
                    Canvas.SetLeft(pic, itemData.Left);
                    Canvas.SetTop(pic, itemData.Top);
                    WireUpEvents(pic);
                    control = pic;
                }
                else if (itemData.Type == BoardItemType.Shape)
                {
                    FrameworkElement? shape = null;
                    if (itemData.ShapeType == "Rectangle") shape = new Rectangle { Fill = new SolidColorBrush(BoardPersistence.HexToColor(itemData.BgColorHex ?? "#00000000")), Stroke = new SolidColorBrush(BoardPersistence.HexToColor(itemData.TextColorHex ?? "#FFFFFFFF")), StrokeThickness = itemData.FontSize > 0 ? itemData.FontSize : 2 };
                    else if (itemData.ShapeType == "Circle") shape = new Ellipse { Fill = new SolidColorBrush(BoardPersistence.HexToColor(itemData.BgColorHex ?? "#00000000")), Stroke = new SolidColorBrush(BoardPersistence.HexToColor(itemData.TextColorHex ?? "#FFFFFFFF")), StrokeThickness = itemData.FontSize > 0 ? itemData.FontSize : 2 };
                    else if (itemData.ShapeType == "Line" && itemData.Points?.Count >= 2)
                    {
                        var pStart = new Point(itemData.Points[0].X, itemData.Points[0].Y);
                        var pEnd = new Point(itemData.Points[1].X, itemData.Points[1].Y);

                        shape = new Line
                        {
                            X1 = pStart.X - itemData.Left,
                            Y1 = pStart.Y - itemData.Top,
                            X2 = pEnd.X - itemData.Left,
                            Y2 = pEnd.Y - itemData.Top,
                            Stroke = new SolidColorBrush(BoardPersistence.HexToColor(itemData.TextColorHex ?? "#FFFFFFFF")),
                            StrokeThickness = itemData.FontSize > 0 ? itemData.FontSize : 2
                        };
                    }
                    else if (itemData.ShapeType == "Arrow" && itemData.Points?.Count >= 2)
                    {
                        var pStart = new Point(itemData.Points[0].X, itemData.Points[0].Y);
                        var pEnd = new Point(itemData.Points[1].X, itemData.Points[1].Y);
                        shape = new Microsoft.UI.Xaml.Shapes.Path
                        {
                            Fill = new SolidColorBrush(BoardPersistence.HexToColor(itemData.BgColorHex ?? "#00000000")),
                            Stroke = new SolidColorBrush(BoardPersistence.HexToColor(itemData.TextColorHex ?? "#FFFFFFFF")),
                            StrokeThickness = itemData.FontSize > 0 ? itemData.FontSize : 3,
                            Data = GeometryUtils.CreateArrowGeometry(pStart, pEnd, itemData.FontSize > 0 ? itemData.FontSize : 3, new Point(itemData.Left, itemData.Top))
                        };
                    }

                    if (shape != null)
                    {
                        var sic = new ShapeItemControl
                        {
                            Shape = shape,
                            ShapeType = itemData.ShapeType,
                            Width = itemData.Width,
                            Height = itemData.Height,
                            Rotation = itemData.Rotation
                        };
                        if (itemData.Points?.Count >= 2)
                        {
                            sic.StartPoint = new Point(itemData.Points[0].X, itemData.Points[0].Y);
                            sic.EndPoint = new Point(itemData.Points[1].X, itemData.Points[1].Y);
                        }
                        Canvas.SetLeft(sic, itemData.Left);
                        Canvas.SetTop(sic, itemData.Top);
                        WireUpEvents(sic);
                        InfiniteCanvasArea.Children.Add(sic);
                    }
                }
                
                if (control != null)
                {
                    Canvas.SetLeft(control, itemData.Left);
                    Canvas.SetTop(control, itemData.Top);
                    Canvas.SetZIndex(control, itemData.ZIndex);
                    if (control is IBoardItem bi) bi.Rotation = itemData.Rotation;
                    InfiniteCanvasArea.Children.Add(control);
                }
            }

            // Restore viewport state with a slight delay to allow the ScrollViewer to calculate layout
            await Task.Delay(50);
            CanvasTransform.TranslateX = board.ViewportX;
            CanvasTransform.TranslateY = board.ViewportY;
            CanvasTransform.ScaleX = board.ZoomFactor;
            CanvasTransform.ScaleY = board.ZoomFactor;
            UpdateZoomUI((float)board.ZoomFactor);
            UpdateItemsZoomFactor(board.ZoomFactor);


            _hasUnsavedChanges = false;
            TitleChanged?.Invoke($"Reference Board - {name}");
        }

        private async void ShowShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var grid = new Grid { ColumnSpacing = 40, Margin = new Thickness(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keys = new StackPanel { Spacing = 8 };
            keys.Children.Add(new TextBlock { Text = "Ctrl + S", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + Z", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + Shift + Z", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + V", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + T", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Delete", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + +/-", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + 0", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Ctrl + 1", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Mouse Wheel", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            keys.Children.Add(new TextBlock { Text = "Middle Mouse Button + Drag", FontWeight = Microsoft.UI.Text.FontWeights.Bold });

            var descs = new StackPanel { Spacing = 8 };
            descs.Children.Add(new TextBlock { Text = "Save Board" });
            descs.Children.Add(new TextBlock { Text = "Undo" });
            descs.Children.Add(new TextBlock { Text = "Redo" });
            descs.Children.Add(new TextBlock { Text = "Paste Image" });
            descs.Children.Add(new TextBlock { Text = "Add Text" });
            descs.Children.Add(new TextBlock { Text = "Delete selected item" });
            descs.Children.Add(new TextBlock { Text = "Zoom In / Out" });
            descs.Children.Add(new TextBlock { Text = "Zoom 100%" });
            descs.Children.Add(new TextBlock { Text = "Zoom Fit" });
            descs.Children.Add(new TextBlock { Text = "Zoom in / out" });
            descs.Children.Add(new TextBlock { Text = "Pan canvas" });

            Grid.SetColumn(keys, 0); Grid.SetColumn(descs, 1); grid.Children.Add(keys); grid.Children.Add(descs);
            var dialog = new ContentDialog { Title = "Keyboard Shortcuts", Content = grid, CloseButtonText = "Close", XamlRoot = this.XamlRoot, Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style };
            await dialog.ShowAsync();
        }

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Z:
                        if (shift) _historyManager.Redo();
                        else _historyManager.Undo();
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.V:
                        ImportFromClipboard_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.T:
                        AddTextButton_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.S:
                        _ = SaveBoardAsync();
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.Add:
                    case (Windows.System.VirtualKey)187: // Windows.System.VirtualKey.Equals (which is used for +)
                        ZoomToPoint(1.15f);
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.Subtract:
                    case (Windows.System.VirtualKey)189: // Windows.System.VirtualKey.Minus
                        ZoomToPoint(1.0f / 1.15f);
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.Number0:
                        Zoom100_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.Number1:
                        ZoomFit_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Windows.System.VirtualKey.A:
                        _selectedItems.Clear();
                        foreach (UIElement child in InfiniteCanvasArea.Children)
                        {
                            if (child is IBoardItem item)
                            {
                                item.IsSelected = true;
                                _selectedItems.Add(item);
                            }
                        }
                        UpdateToolUI();
                        e.Handled = true;
                        break;
                }
                if (e.Handled) return;
            }

            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                var itemsToDelete = new List<UIElement>();
                foreach (UIElement child in InfiniteCanvasArea.Children)
                {
                    if (child is IBoardItem item && item.IsSelected)
                    {
                        itemsToDelete.Add(child);
                    }
                }
                
                if (itemsToDelete.Count > 0)
                {
                    var composite = new CompositeCommand();
                    foreach (var itemToDelete in itemsToDelete)
                    {
                        composite.Add(new RemoveItemCommand(InfiniteCanvasArea, itemToDelete));
                    }
                    _historyManager.ExecuteCommand(composite);
                    MarkAsUnsaved();
                    e.Handled = true;
                }
            }
        }
    }
}
