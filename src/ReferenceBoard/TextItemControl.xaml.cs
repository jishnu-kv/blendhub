using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using src.Core;
using System;
using Windows.UI;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class TextItemControl : BoardItemBase, ITextItem
    {
        public event EventHandler<TextChangedEndedEventArgs>? TextChangedEnded;
        private string _oldText = "";

        private string[] _fontFamilies = new string[]
        {
            "Arial", "Calibri", "Cambria", "Candara", "Comic Sans MS", "Consolas",
            "Constantia", "Corbel", "Courier New", "Franklin Gothic Medium",
            "Gabriola", "Georgia", "Impact", "Lucida Console", "Lucida Sans Unicode",
            "Microsoft Sans Serif", "Palatino Linotype", "Segoe UI", "Tahoma",
            "Times New Roman", "Trebuchet MS", "Verdana", "Webdings", "Wingdings"
        };



        public TextItemControl()
        {
            this.InitializeComponent();
            PopulateComboboxes();
            this.MaintainAspectRatio = true;

            MainTextBox.GotFocus += MainTextBox_GotFocus;
            MainTextBox.LostFocus += MainTextBox_LostFocus;
            MainTextBox.TextChanged += MainTextBox_TextChanged;

            this.DoubleTapped += (s, e) => EnterEditMode();
        }

        private void MainTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Clear fixed dimensions to allow auto-sizing to content
            this.Width = double.NaN;
            this.Height = double.NaN;
        }

        private void EnterEditMode()
        {
            if (IsLocked) return;
            MainTextBox.IsReadOnly = false;
            MainTextBox.IsHitTestVisible = true;
            MainTextBox.Focus(FocusState.Programmatic);
            MainTextBox.SelectAll();
        }

        private void ExitEditMode()
        {
            MainTextBox.IsReadOnly = true;
            MainTextBox.IsHitTestVisible = false;
            // Clear text selection
            MainTextBox.Select(0, 0);
        }

        protected override void UpdateVisualState()
        {
            base.UpdateVisualState();
            if (!IsSelected) ExitEditMode();
        }

        protected override void OnResizingStarted() { }
        protected override void OnResizing(double newWidth, double newHeight) { }

        private void PopulateComboboxes()
        {
            foreach (var font in _fontFamilies)
            {
                FontComboBox.Items.Add(new ComboBoxItem { Content = font, FontFamily = new FontFamily(font) });
            }
            FontComboBox.SelectedIndex = Math.Max(0, Array.IndexOf(_fontFamilies, "Segoe UI"));

            var fontStyles = new string[] { "Regular", "Italic", "Bold", "Bold Italic" };
            foreach (var style in fontStyles) FontStyleComboBox.Items.Add(new ComboBoxItem { Content = style });
            FontStyleComboBox.SelectedIndex = 0;


        }

        public string Text
        {
            get => MainTextBox?.Text ?? "";
            set
            {
                if (MainTextBox != null) MainTextBox.Text = value;
            }
        }

        public new double FontSize
        {
            get => MainTextBox.FontSize;
            set => MainTextBox.FontSize = value;
        }

        public Color TextColor
        {
            get => (MainTextBox.Foreground as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.White;
            set { MainTextBox.Foreground = new SolidColorBrush(value); FontColorPreview.Background = new SolidColorBrush(value); }
        }

        public Color BackgroundColor
        {
            get => (TextBorder.Background as SolidColorBrush)?.Color ?? Color.FromArgb(255, 33, 33, 33);
            set { TextBorder.Background = new SolidColorBrush(value); BgColorPreview.Background = new SolidColorBrush(value); }
        }

        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontComboBox.SelectedItem is ComboBoxItem item && MainTextBox != null) MainTextBox.FontFamily = item.FontFamily;
        }

        private void FontStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontStyleComboBox.SelectedItem is ComboBoxItem item && MainTextBox != null)
            {
                string style = item.Content?.ToString() ?? "";
                MainTextBox.FontWeight = style.Contains("Bold") ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
                MainTextBox.FontStyle = style.Contains("Italic") ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            }
        }


        private void FontColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (MainTextBox != null && FontColorPreview != null)
            {
                MainTextBox.Foreground = new SolidColorBrush(args.NewColor);
                FontColorPreview.Background = new SolidColorBrush(args.NewColor);
            }
        }

        private void BgColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (TextBorder != null && BgColorPreview != null)
            {
                TextBorder.Background = new SolidColorBrush(args.NewColor);
                BgColorPreview.Background = new SolidColorBrush(args.NewColor);
            }
        }

        private void MainTextBox_GotFocus(object sender, RoutedEventArgs e) => _oldText = MainTextBox.Text;

        private void MainTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
            if (MainTextBox.Text != _oldText)
            {
                TextChangedEnded?.Invoke(this, new TextChangedEndedEventArgs { OldText = _oldText, NewText = MainTextBox.Text });
            }
        }

        private void TextAlignSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TextAlignSegmented != null && MainTextBox != null)
            {
                var item = TextAlignSegmented.SelectedItem as CommunityToolkit.WinUI.Controls.SegmentedItem;
                string tag = item?.Tag?.ToString() ?? "";

                if (tag == "Left") MainTextBox.TextAlignment = TextAlignment.Left;
                else if (tag == "Center") MainTextBox.TextAlignment = TextAlignment.Center;
                else if (tag == "Right") MainTextBox.TextAlignment = TextAlignment.Right;
            }
        }

        public void FocusTextBox()
        {
            EnterEditMode();
        }
    }
}
