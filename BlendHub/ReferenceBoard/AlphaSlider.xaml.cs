using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;

namespace BlendHub.ReferenceBoard
{
    public sealed partial class AlphaSlider : UserControl
    {
        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register(nameof(Color), typeof(Color), typeof(AlphaSlider), new PropertyMetadata(Microsoft.UI.Colors.Blue, OnColorChanged));

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(object), typeof(AlphaSlider), new PropertyMetadata(null));

        public static readonly DependencyProperty HeaderTemplateProperty =
            DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(AlphaSlider), new PropertyMetadata(null));

        public static readonly DependencyProperty AlphaProperty =
            DependencyProperty.Register(nameof(Alpha), typeof(double), typeof(AlphaSlider), new PropertyMetadata(100.0, OnAlphaChanged));

        public Color Color
        {
            get => (Color)GetValue(ColorProperty);
            set => SetValue(ColorProperty, value);
        }

        public object Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public DataTemplate HeaderTemplate
        {
            get => (DataTemplate)GetValue(HeaderTemplateProperty);
            set => SetValue(HeaderTemplateProperty, value);
        }

        public double Alpha
        {
            get => (double)GetValue(AlphaProperty);
            set => SetValue(AlphaProperty, value);
        }

        public event RangeBaseValueChangedEventHandler? ValueChanged;

        public AlphaSlider()
        {
            this.InitializeComponent();
            this.SizeChanged += AlphaSlider_SizeChanged;
            this.InternalSlider.ValueChanged += (s, e) => ValueChanged?.Invoke(this, e);
            UpdateGradient();
            UpdateCheckeredBackground();
        }

        private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AlphaSlider slider)
            {
                slider.UpdateGradient();
            }
        }

        private static void OnAlphaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // The Slider control is two-way bound to the Alpha property,
            // so we don't need to do much here unless we want to trigger external events.
        }

        private void AlphaSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCheckeredBackground();
        }

        private void UpdateGradient()
        {
            // Convert to RGB if it was HSV (user asked for HSV conversion)
            // But usually we'll just use the Color property which is RGB.
            // The requirement says: "Color: The current selected color (from HSV: H, S, 100)"

            StartStop.Color = Color.FromArgb(0, Color.R, Color.G, Color.B);
            EndStop.Color = Color.FromArgb(255, Color.R, Color.G, Color.B);
        }

        private async void UpdateCheckeredBackground()
        {
            int width = (int)Math.Max(1, this.ActualWidth - 4);
            int height = 12; // Force 12px height for 3 grids (12/4=3)

            if (width <= 0 || height <= 0) return;

            // Generate checkered pattern
            // Pattern: 4x4 pixel squares alternating between transparent and a theme color
            int checkerSize = 4;
            byte[] pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int blockX = x / checkerSize;
                    int blockY = y / checkerSize;
                    bool isWhite = (blockX + blockY) % 2 == 0;

                    int index = (y * width + x) * 4;
                    if (isWhite)
                    {
                        // #383636
                        pixels[index] = 54;     // B
                        pixels[index + 1] = 54; // G
                        pixels[index + 2] = 56; // R
                        pixels[index + 3] = 255; // A
                    }
                    else
                    {
                        // #585858ff (Light Gray / "Black" grid)
                        pixels[index] = 88;     // B
                        pixels[index + 1] = 88; // G
                        pixels[index + 2] = 88; // R
                        pixels[index + 3] = 255; // A
                    }
                }
            }

            WriteableBitmap bitmap = new WriteableBitmap(width, height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(pixels, 0, pixels.Length);
            }
            CheckerImageBrush.ImageSource = bitmap;
        }

        // Simple HSV to RGB conversion if needed
        public static Color FromHsv(double h, double s, double v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);

            v = v * 255;
            byte vByte = (byte)v;
            byte p = (byte)(v * (1 - s));
            byte q = (byte)(v * (1 - f * s));
            byte t = (byte)(v * (1 - (1 - f) * s));

            if (hi == 0) return Color.FromArgb(255, vByte, t, p);
            else if (hi == 1) return Color.FromArgb(255, q, vByte, p);
            else if (hi == 2) return Color.FromArgb(255, p, vByte, t);
            else if (hi == 3) return Color.FromArgb(255, p, q, vByte);
            else if (hi == 4) return Color.FromArgb(255, t, p, vByte);
            else return Color.FromArgb(255, vByte, p, q);
        }
    }
}
