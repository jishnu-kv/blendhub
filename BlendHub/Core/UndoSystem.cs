using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlendHub.ReferenceBoard;
using System;
using System.Collections.Generic;

namespace src.Core
{
    public interface IBoardItem
    {
        event EventHandler? DeleteRequested;
        event EventHandler? Selected;
        event EventHandler? BringForwardRequested;
        event EventHandler? SendBackwardRequested;
        event EventHandler<TransformChangedEventArgs>? TransformEnded;
        event EventHandler? TransformChanged;
        event EventHandler<Windows.Foundation.Point>? Moved;
        double Width { get; set; }
        double Height { get; set; }
        double Rotation { get; set; }
        bool IsSelected { get; set; }
        bool IsLocked { get; set; }
        bool ShowRotateHandle { get; set; }
        double ZoomFactor { get; set; }
        void Translate(double dx, double dy);
    }

    public interface ITextItem : IBoardItem
    {
        string Text { get; set; }
        event EventHandler<TextChangedEndedEventArgs>? TextChangedEnded;
    }

    public class TransformChangedEventArgs : EventArgs
    {
        public double OldLeft { get; set; }
        public double OldTop { get; set; }
        public double OldWidth { get; set; }
        public double OldHeight { get; set; }
        public double NewLeft { get; set; }
        public double NewTop { get; set; }
        public double NewWidth { get; set; }
        public double NewHeight { get; set; }
        public double OldFontSize { get; set; }
        public double NewFontSize { get; set; }
        public double OldMaxWidth { get; set; }
        public double NewMaxWidth { get; set; }
        public double OldRotation { get; set; }
        public double NewRotation { get; set; }
    }

    public class TextChangedEndedEventArgs : EventArgs
    {
        public string OldText { get; set; } = string.Empty;
        public string NewText { get; set; } = string.Empty;
    }

    public interface IUndoCommand
    {
        void Execute();
        void Undo();
    }

    public class HistoryManager
    {
        private Stack<IUndoCommand> _undoStack = new Stack<IUndoCommand>();
        private Stack<IUndoCommand> _redoStack = new Stack<IUndoCommand>();

        public void ExecuteCommand(IUndoCommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var command = _undoStack.Pop();
                command.Undo();
                _redoStack.Push(command);
            }
        }

        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var command = _redoStack.Pop();
                command.Execute();
                _undoStack.Push(command);
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
    }

    public class AddItemCommand : IUndoCommand
    {
        private Canvas _canvas;
        private UIElement _element;

        public AddItemCommand(Canvas canvas, UIElement element)
        {
            _canvas = canvas;
            _element = element;
        }

        public void Execute()
        {
            if (!_canvas.Children.Contains(_element))
                _canvas.Children.Add(_element);
        }

        public void Undo()
        {
            if (_canvas.Children.Contains(_element))
                _canvas.Children.Remove(_element);
        }
    }

    public class RemoveItemCommand : IUndoCommand
    {
        private Canvas _canvas;
        private UIElement _element;

        public RemoveItemCommand(Canvas canvas, UIElement element)
        {
            _canvas = canvas;
            _element = element;
        }

        public void Execute()
        {
            if (_canvas.Children.Contains(_element))
                _canvas.Children.Remove(_element);
        }

        public void Undo()
        {
            if (!_canvas.Children.Contains(_element))
                _canvas.Children.Add(_element);
        }
    }

    public class TransformCommand : IUndoCommand
    {
        private UIElement _element;
        private double _oldLeft, _oldTop, _oldWidth, _oldHeight;
        private double _newLeft, _newTop, _newWidth, _newHeight;
        private double _oldFontSize, _newFontSize, _oldMaxWidth, _newMaxWidth;
        private double _oldRotation, _newRotation;
        private FrameworkElement? _target;

        public TransformCommand(UIElement element,
            double oldLeft, double oldTop, double oldWidth, double oldHeight,
            double newLeft, double newTop, double newWidth, double newHeight,
            double oldFontSize = 0, double newFontSize = 0, double oldMaxWidth = 0, double newMaxWidth = 0,
            double oldRotation = 0, double newRotation = 0)
        {
            _element = element;
            _target = element as FrameworkElement;
            _oldLeft = oldLeft; _oldTop = oldTop; _oldWidth = oldWidth; _oldHeight = oldHeight;
            _newLeft = newLeft; _newTop = newTop; _newWidth = newWidth; _newHeight = newHeight;
            _oldFontSize = oldFontSize; _newFontSize = newFontSize;
            _oldMaxWidth = oldMaxWidth; _newMaxWidth = newMaxWidth;
            _oldRotation = oldRotation; _newRotation = newRotation;
        }

        public void Execute()
        {
            Canvas.SetLeft(_element, _newLeft);
            Canvas.SetTop(_element, _newTop);

            if (_element is IBoardItem item)
            {
                item.Width = _newWidth;
                item.Height = _newHeight;
            }

            if (_element is TextItemControl txt)
            {
                if (_newFontSize > 0.1 && !double.IsNaN(_newFontSize) && !double.IsInfinity(_newFontSize))
                    txt.FontSize = _newFontSize;

                if (_newMaxWidth > 0.1 && !double.IsNaN(_newMaxWidth) && !double.IsInfinity(_newMaxWidth))
                    txt.MaxWidth = _newMaxWidth;
            }

            if (_element is IBoardItem itemWithRot)
            {
                itemWithRot.Rotation = _newRotation;
            }
        }

        public void Undo()
        {
            Canvas.SetLeft(_element, _oldLeft);
            Canvas.SetTop(_element, _oldTop);

            if (_element is IBoardItem item)
            {
                item.Width = _oldWidth;
                item.Height = _oldHeight;
            }

            if (_element is TextItemControl txt)
            {
                if (_oldFontSize > 0.1 && !double.IsNaN(_oldFontSize) && !double.IsInfinity(_oldFontSize))
                    txt.FontSize = _oldFontSize;

                if (_oldMaxWidth > 0.1 && !double.IsNaN(_oldMaxWidth) && !double.IsInfinity(_oldMaxWidth))
                    txt.MaxWidth = _oldMaxWidth;
            }

            if (_element is IBoardItem itemWithRot)
            {
                itemWithRot.Rotation = _oldRotation;
            }
        }
    }

    public class ZIndexCommand : IUndoCommand
    {
        private UIElement _element;
        private int _oldZ, _newZ;

        public ZIndexCommand(UIElement element, int oldZ, int newZ)
        {
            _element = element;
            _oldZ = oldZ;
            _newZ = newZ;
        }

        public void Execute()
        {
            Canvas.SetZIndex(_element, _newZ);
        }

        public void Undo()
        {
            Canvas.SetZIndex(_element, _oldZ);
        }
    }

    public class TextChangeCommand : IUndoCommand
    {
        private ITextItem _control;
        private string _oldText, _newText;

        public TextChangeCommand(ITextItem control, string oldText, string newText)
        {
            _control = control;
            _oldText = oldText;
            _newText = newText;
        }

        public void Execute()
        {
            _control.Text = _newText;
        }

        public void Undo()
        {
            _control.Text = _oldText;
        }
    }

    public class CompositeCommand : IUndoCommand
    {
        private List<IUndoCommand> _commands = new List<IUndoCommand>();
        public void Add(IUndoCommand cmd) => _commands.Add(cmd);
        public void Execute() { foreach (var cmd in _commands) cmd.Execute(); }
        public void Undo() { for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(); }
    }
}
