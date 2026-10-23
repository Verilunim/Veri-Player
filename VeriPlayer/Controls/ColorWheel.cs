using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VeriPlayer.Services;

namespace VeriPlayer.Controls
{
    /// <summary>Круговой выбор цвета (Hue + Saturation). Яркость задаётся отдельным ползунком.</summary>
    public class ColorWheel : FrameworkElement
    {
        public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
            nameof(Hue), typeof(double), typeof(ColorWheel),
            new FrameworkPropertyMetadata(268.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

        public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(
            nameof(Saturation), typeof(double), typeof(ColorWheel),
            new FrameworkPropertyMetadata(0.7, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

        public static readonly DependencyProperty PreviewBrushProperty = DependencyProperty.Register(
            nameof(PreviewBrush), typeof(Brush), typeof(ColorWheel),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Hue
        {
            get => (double)GetValue(HueProperty);
            set => SetValue(HueProperty, value);
        }

        public double Saturation
        {
            get => (double)GetValue(SaturationProperty);
            set => SetValue(SaturationProperty, value);
        }

        public Brush PreviewBrush
        {
            get => (Brush)GetValue(PreviewBrushProperty);
            set => SetValue(PreviewBrushProperty, value);
        }

        public event EventHandler ColorChanged;

        private ImageSource _wheel;
        private int _wheelSize;
        private bool _dragging;

        public ColorWheel()
        {
            Cursor = Cursors.Hand;
            Focusable = false;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorWheel wheel && e.Property == PreviewBrushProperty) return;
            if (d is ColorWheel w) w.ColorChanged?.Invoke(w, EventArgs.Empty);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double diameter = Math.Max(10, Math.Min(ActualWidth, ActualHeight));
            double radius = diameter / 2;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);

            int pixel = (int)Math.Round(radius * 2);
            if (_wheel == null || _wheelSize != pixel)
            {
                _wheel = BuildWheel(pixel);
                _wheelSize = pixel;
            }

            dc.DrawEllipse(ThemeService.Solid(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), null, center, radius, radius);
            dc.DrawImage(_wheel, new Rect(center.X - radius, center.Y - radius, diameter, diameter));

            double angle = Hue * Math.PI / 180.0;
            double dist = Math.Max(0, Math.Min(1, Saturation)) * radius;
            var knob = new Point(center.X + Math.Cos(angle) * dist, center.Y + Math.Sin(angle) * dist);

            dc.DrawEllipse(null, new Pen(Brushes.White, 2.4), knob, 9, 9);
            dc.DrawEllipse(null, new Pen(ThemeService.Solid(Color.FromArgb(0x66, 0, 0, 0)), 1), knob, 9, 9);
            dc.DrawEllipse(PreviewBrush ?? Brushes.White, null, knob, 6.2, 6.2);
        }

        private static ImageSource BuildWheel(int size)
        {
            size = Math.Max(8, Math.Min(512, size));
            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
            int stride = size * 4;
            var pixels = new byte[size * size * 4];
            double half = size / 2.0;

            for (int y = 0; y < size; y++)
            {
                double dy = y + 0.5 - half;
                for (int x = 0; x < size; x++)
                {
                    double dx = x + 0.5 - half;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    int index = y * stride + x * 4;

                    if (dist > half + 0.8) { pixels[index + 3] = 0; continue; }

                    double hue = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    if (hue < 0) hue += 360;
                    double sat = Math.Min(1, dist / half);
                    var color = ThemeService.Hsv(hue, sat, 1.0);

                    double edge = half - dist;
                    double alpha = edge < 1.2 ? Math.Max(0, edge / 1.2) : 1.0;
                    if (dist < 1.5) alpha = Math.Min(1.0, 0.35 + dist);

                    byte a = (byte)Math.Round(alpha * 255);
                    // Pbgra32 — premultiplied
                    pixels[index + 0] = (byte)(color.B * a / 255);
                    pixels[index + 1] = (byte)(color.G * a / 255);
                    pixels[index + 2] = (byte)(color.R * a / 255);
                    pixels[index + 3] = a;
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, stride, 0);
            bitmap.Freeze();
            return bitmap;
        }

        // ------------------------------------------------------------------ input

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _dragging = true;
            CaptureMouse();
            Apply(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) Apply(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        private void Apply(Point point)
        {
            double radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;

            Hue = angle;
            Saturation = Math.Max(0, Math.Min(1, dist / radius));
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
