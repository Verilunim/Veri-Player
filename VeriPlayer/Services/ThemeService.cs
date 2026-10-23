using System;
using System.Windows;
using System.Windows.Media;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>Перекрашивает всё приложение из одного RGB-выбора пользователя.</summary>
    public static class ThemeService
    {
        public static Color AccentColor { get; private set; } = Color.FromRgb(0xA8, 0x55, 0xF7);
        public static Color SecondColor { get; private set; } = Color.FromRgb(0x4F, 0x8B, 0xFF);

        public static void Apply(AppSettings s)
        {
            if (s == null) return;

            double hue = ((s.Hue % 360) + 360) % 360;
            double sat = Math.Max(0.05, Math.Min(1, s.Saturation));
            double val = Math.Max(0.35, Math.Min(1, s.Value));

            var accent = Hsv(hue, sat, val);
            var accent2 = Hsv((hue + s.SecondHueOffset + 360) % 360, Math.Min(1, sat * 0.95), Math.Min(1, val * 1.02));
            var deep = Hsv(hue, Math.Min(1, sat * 0.9), Math.Max(0.18, val * 0.42));
            var pale = Hsv(hue, Math.Max(0, sat * 0.35), 0.97);

            AccentColor = accent;
            SecondColor = accent2;

            Set("AccentColor", accent);
            Set("AccentColor2", accent2);
            Set("AccentBrush", Solid(accent));
            Set("AccentBrush2", Solid(accent2));
            Set("AccentDeepBrush", Solid(deep));
            Set("AccentPaleBrush", Solid(pale));
            Set("AccentSoftBrush", Solid(Alpha(accent, 0x38)));
            Set("AccentSofterBrush", Solid(Alpha(accent, 0x1E)));
            Set("AccentFaintBrush", Solid(Alpha(accent, 0x12)));
            Set("AccentGlowBrush", Solid(Alpha(accent, 0x66)));
            Set("AccentGlowBrush2", Solid(Alpha(accent2, 0x55)));
            Set("AccentBorderBrush", Solid(Alpha(accent, 0x8C)));
            Set("AccentTextBrush", Solid(Hsv(hue, Math.Min(1, sat * 0.75), 1.0)));

            var horizontal = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            horizontal.GradientStops.Add(new GradientStop(accent, 0));
            horizontal.GradientStops.Add(new GradientStop(accent2, 1));
            horizontal.Freeze();
            Set("AccentGradientBrush", horizontal);

            var vertical = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };
            vertical.GradientStops.Add(new GradientStop(Alpha(accent, 0x40), 0));
            vertical.GradientStops.Add(new GradientStop(Alpha(accent, 0x04), 1));
            vertical.Freeze();
            Set("AccentVeilBrush", vertical);

            // мягкое свечение для карточек
            var radius = 34.0;
            var glow = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.75,
                RadiusY = 0.75
            };
            glow.GradientStops.Add(new GradientStop(Alpha(accent, 0x3A), 0));
            glow.GradientStops.Add(new GradientStop(Alpha(accent, 0x00), 1));
            glow.Freeze();
            Set("AccentHaloBrush", glow);
            _ = radius;
        }

        private static void Set(string key, object value)
        {
            var app = Application.Current;
            if (app == null) return;
            app.Resources[key] = value;
        }

        public static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        public static Color Hsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Max(0, Math.Min(1, s));
            v = Math.Max(0, Math.Min(1, v));

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;

            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        public static string ToHex(Color c) => string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);

        public static (double hue, double sat, double val) ToHsv(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            if (delta > 0.0001)
            {
                if (max == r) h = 60 * (((g - b) / delta) % 6);
                else if (max == g) h = 60 * (((b - r) / delta) + 2);
                else h = 60 * (((r - g) / delta) + 4);
            }
            if (h < 0) h += 360;

            double s = max <= 0 ? 0 : delta / max;
            return (h, s, max);
        }

        /// <summary>Детерминированный градиент-заглушка для обложки трека.</summary>
        public static LinearGradientBrush CoverBrush(string seed)
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(seed))
                foreach (var ch in seed) hash = unchecked(hash * 31 + ch);

            double hue = Math.Abs(hash) % 360;
            var c1 = Hsv(hue, 0.62, 0.85);
            var c2 = Hsv((hue + 48) % 360, 0.72, 0.55);

            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            b.GradientStops.Add(new GradientStop(c1, 0));
            b.GradientStops.Add(new GradientStop(c2, 1));
            b.Freeze();
            return b;
        }
    }
}
