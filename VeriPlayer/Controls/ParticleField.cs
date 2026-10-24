using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace VeriPlayer.Controls
{
    public enum EffectKind
    {
        None,
        Snow,
        Rain,
        Leaves,
        Sakura,
        Stars,
        Bubbles,
        Fireflies,
        Embers
    }

    public static class Effects
    {
        public sealed class Info
        {
            public EffectKind Kind;
            public string Key;
            public string Title;
            public string Hint;
            public string Glyph;
        }

        public static readonly List<Info> All = new List<Info>
        {
            new Info { Kind = EffectKind.None,       Key = "none",       Glyph = "○", Title = "Без эффекта", Hint = "Чистый фон" },
            new Info { Kind = EffectKind.Snow,       Key = "snow",       Glyph = "❄", Title = "Снег",        Hint = "Медленные снежинки" },
            new Info { Kind = EffectKind.Rain,       Key = "rain",       Glyph = "☔", Title = "Дождь",       Hint = "Струи и капли" },
            new Info { Kind = EffectKind.Leaves,     Key = "leaves",     Glyph = "🍃", Title = "Листья",      Hint = "Осенний листопад" },
            new Info { Kind = EffectKind.Sakura,     Key = "sakura",     Glyph = "🌸", Title = "Сакура",      Hint = "Лепестки цветов" },
            new Info { Kind = EffectKind.Stars,      Key = "stars",      Glyph = "✦", Title = "Звёзды",      Hint = "Ночное небо" },
            new Info { Kind = EffectKind.Bubbles,    Key = "bubbles",    Glyph = "◌", Title = "Пузыри",      Hint = "Поднимаются вверх" },
            new Info { Kind = EffectKind.Fireflies,  Key = "fireflies",  Glyph = "✧", Title = "Светлячки",   Hint = "Тёплые огоньки" },
            new Info { Kind = EffectKind.Embers,     Key = "embers",     Glyph = "✶", Title = "Искры",       Hint = "Огненные частицы" }
        };

        public static EffectKind Parse(string key)
        {
            foreach (var info in All)
                if (string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase))
                    return info.Kind;
            return EffectKind.Snow;
        }

        public static Info Describe(EffectKind kind)
        {
            foreach (var info in All)
                if (info.Kind == kind) return info;
            return All[0];
        }
    }

    /// <summary>Живой фон: снег, дождь, листья, сакура, звёзды, пузыри, светлячки, искры.</summary>
    public class ParticleField : FrameworkElement
    {
        private sealed class P
        {
            public double X, Y, Size, Speed, Drift, Phase, Rot, Spin, Alpha, Extra;
        }

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(EffectKind), typeof(ParticleField),
            new FrameworkPropertyMetadata(EffectKind.Snow, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

        public static readonly DependencyProperty IntensityProperty = DependencyProperty.Register(
            nameof(Intensity), typeof(double), typeof(ParticleField),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public EffectKind Kind
        {
            get => (EffectKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public double Intensity
        {
            get => (double)GetValue(IntensityProperty);
            set => SetValue(IntensityProperty, value);
        }

        private readonly List<P> _items = new List<P>();
        private readonly Random _random = new Random();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private double _lastFrame;
        private double _time;
        private bool _attached;

        // ---- cached drawing resources -------------------------------------------------
        private static readonly Brush SnowCore = Frozen(new RadialGradientBrush(
            new GradientStopCollection { Stop(Color.FromArgb(0xF2, 255, 255, 255), 0), Stop(Color.FromArgb(0x00, 255, 255, 255), 1) }));

        private static readonly Pen RainThin = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0xBF, 0xD8, 0xFF))), 1.1));
        private static readonly Pen RainMid = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0xC8, 0xDD, 0xFF))), 1.5));
        private static readonly Pen RainFat = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0xDC, 0xE8, 0xFF))), 2.0));
        private static readonly Pen StarSparkPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF))), 0.9));
        private static readonly Brush EmberGlow = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB0, 0x5A)));
        private static readonly Brush EmberCore = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xE2, 0xB0)));

        private static readonly Color[] LeafColors =
        {
            Color.FromRgb(0xD9, 0x7B, 0x2E), Color.FromRgb(0xC0, 0x54, 0x2A),
            Color.FromRgb(0xE0, 0xA7, 0x3C), Color.FromRgb(0x8E, 0x9A, 0x3B),
            Color.FromRgb(0xB4, 0x3D, 0x2E), Color.FromRgb(0xD3, 0x8C, 0x3F)
        };

        private static readonly Color[] SakuraColors =
        {
            Color.FromRgb(0xFF, 0xC2, 0xDD), Color.FromRgb(0xFF, 0xAC, 0xCB),
            Color.FromRgb(0xF7, 0x9E, 0xC0), Color.FromRgb(0xFF, 0xD6, 0xE8)
        };

        private static readonly Color[] FireflyColors =
        {
            Color.FromRgb(0xFF, 0xE8, 0x8A), Color.FromRgb(0xC8, 0xFF, 0x9E),
            Color.FromRgb(0xFF, 0xD1, 0x66), Color.FromRgb(0xA8, 0xFF, 0xD6)
        };

        private static readonly Brush[] LeafBrushes = BuildBrushes(LeafColors);
        private static readonly Brush[] SakuraBrushes = BuildBrushes(SakuraColors);
        private static readonly Brush[] FireflyBrushes = BuildBrushes(FireflyColors);

        private static Brush[] BuildBrushes(Color[] colors)
        {
            var list = new Brush[colors.Length];
            for (int i = 0; i < colors.Length; i++) list[i] = Frozen(new SolidColorBrush(colors[i]));
            return list;
        }

        private static readonly Brush BubbleFill = Frozen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
        private static readonly Pen BubblePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xE6, 0xF2, 0xFF))), 1.2));
        private static readonly Pen BubblePenSmall = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0xE6, 0xF2, 0xFF))), 0.9));

        private static readonly Geometry LeafGeometry = FrozenGeometry("M 0,-11 C 7,-6 8,5 0,11 C -8,5 -7,-6 0,-11 Z");
        private static readonly Geometry PetalGeometry = FrozenGeometry("M 0,-10 C 8,-6 8,4 0,11 C -8,4 -8,-6 0,-10 Z");

        public ParticleField()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += (s, e) => { Rebuild(); };
        }

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ParticleField field)
            {
                field.Rebuild();
                field.UpdateRendering();
            }
        }

        private bool _loaded;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            IsVisibleChanged -= OnVisibleChanged;
            IsVisibleChanged += OnVisibleChanged;
            Rebuild();
            UpdateRendering();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            UpdateRendering();
        }

        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateRendering();

        /// <summary>
        /// Подписка на каждый кадр держит рендер WPF в постоянной работе, поэтому включаем её,
        /// только когда эффект выбран и окно видно (не свёрнуто в трей).
        /// </summary>
        private void UpdateRendering()
        {
            bool want = _loaded && IsVisible && Kind != EffectKind.None;
            if (want == _attached) return;
            _attached = want;
            if (want)
            {
                _lastFrame = _clock.Elapsed.TotalSeconds;
                CompositionTarget.Rendering += OnFrame;
            }
            else
            {
                CompositionTarget.Rendering -= OnFrame;
                InvalidateVisual();
            }
        }

        private double Area => Math.Max(0, ActualWidth * ActualHeight);

        private int DesiredCount()
        {
            double density;
            switch (Kind)
            {
                case EffectKind.None: return 0;
                case EffectKind.Rain: density = 12000; break;
                case EffectKind.Stars: density = 9000; break;
                case EffectKind.Bubbles: density = 60000; break;
                case EffectKind.Fireflies: density = 46000; break;
                case EffectKind.Embers: density = 26000; break;
                default: density = 20000; break;
            }
            double raw = Area / density * Math.Max(0.15, Intensity);
            return (int)Math.Max(6, Math.Min(280, raw));
        }

        private void Rebuild()
        {
            int want = DesiredCount();
            while (_items.Count > want) _items.RemoveAt(_items.Count - 1);
            while (_items.Count < want)
            {
                var p = new P();
                Spawn(p, true);
                _items.Add(p);
            }
        }

        private double Rand(double min, double max) => min + _random.NextDouble() * (max - min);

        private void Spawn(P p, bool anywhere)
        {
            double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
            p.Phase = Rand(0, Math.PI * 2);
            p.Rot = Rand(0, 360);
            p.Extra = _random.NextDouble();

            switch (Kind)
            {
                case EffectKind.Rain:
                    p.X = Rand(-60, w + 60);
                    p.Y = anywhere ? Rand(-h, h) : Rand(-h * 0.35, -10);
                    p.Size = Rand(9, 26);
                    p.Speed = Rand(620, 1150);
                    p.Drift = Rand(0.10, 0.24);
                    p.Alpha = Rand(0.22, 0.62);
                    break;

                case EffectKind.Leaves:
                    p.X = Rand(-40, w + 40);
                    p.Y = anywhere ? Rand(-h, h) : Rand(-h * 0.3, -20);
                    p.Size = Rand(0.5, 1.15);
                    p.Speed = Rand(28, 74);
                    p.Drift = Rand(14, 46);
                    p.Spin = Rand(-55, 55);
                    p.Alpha = Rand(0.55, 0.95);
                    break;

                case EffectKind.Sakura:
                    p.X = Rand(-40, w + 40);
                    p.Y = anywhere ? Rand(-h, h) : Rand(-h * 0.3, -20);
                    p.Size = Rand(0.42, 0.95);
                    p.Speed = Rand(24, 62);
                    p.Drift = Rand(18, 58);
                    p.Spin = Rand(-70, 70);
                    p.Alpha = Rand(0.55, 0.95);
                    break;

                case EffectKind.Stars:
                    p.X = Rand(0, w);
                    p.Y = Rand(0, h);
                    p.Size = Rand(0.7, 2.1);
                    p.Speed = Rand(0.3, 1.1);
                    p.Alpha = Rand(0.25, 0.95);
                    break;

                case EffectKind.Bubbles:
                    p.X = Rand(0, w);
                    p.Y = anywhere ? Rand(0, h) : h + Rand(10, 140);
                    p.Size = Rand(3, 16);
                    p.Speed = Rand(14, 52);
                    p.Drift = Rand(6, 22);
                    p.Alpha = Rand(0.16, 0.5);
                    break;

                case EffectKind.Fireflies:
                    p.X = Rand(0, w);
                    p.Y = Rand(0, h);
                    p.Size = Rand(1.4, 3.2);
                    p.Speed = Rand(6, 22);
                    p.Drift = Rand(8, 26);
                    p.Alpha = Rand(0.35, 1.0);
                    break;

                case EffectKind.Embers:
                    p.X = Rand(0, w);
                    p.Y = anywhere ? Rand(0, h) : h + Rand(6, 60);
                    p.Size = Rand(1.2, 3.4);
                    p.Speed = Rand(28, 88);
                    p.Drift = Rand(3, 16);
                    p.Alpha = Rand(0.35, 0.95);
                    break;

                default: // snow
                    p.X = Rand(-40, w + 40);
                    p.Y = anywhere ? Rand(-h, h) : Rand(-h * 0.3, -16);
                    p.Size = Rand(2.2, 7.0);
                    p.Speed = Rand(16, 58);
                    p.Drift = Rand(10, 34);
                    p.Spin = Rand(-24, 24);
                    p.Alpha = Rand(0.42, 0.95);
                    break;
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double since = now - _lastFrame;
            if (since < 1.0 / 48.0) return;   // мягкое ограничение кадров
            _lastFrame = now;

            if (!IsVisible || ActualWidth < 2 || ActualHeight < 2) return;
            double dt = Math.Min(0.06, since);
            _time += dt;

            if (_items.Count != DesiredCount()) Rebuild();

            double w = ActualWidth, h = ActualHeight;
            foreach (var p in _items) Step(p, dt, w, h);
            InvalidateVisual();
        }

        private void Step(P p, double dt, double w, double h)
        {
            switch (Kind)
            {
                case EffectKind.Snow:
                    p.Y += p.Speed * dt;
                    p.X += Math.Sin(_time * 0.8 + p.Phase) * p.Drift * dt;
                    p.Rot += p.Spin * dt;
                    if (p.Y - 12 > h || p.X < -70 || p.X > w + 70) { Spawn(p, false); p.X = Rand(-30, w + 30); }
                    break;

                case EffectKind.Rain:
                    p.Y += p.Speed * dt;
                    p.X += p.Speed * p.Drift * dt;
                    if (p.Y - p.Size > h) { Spawn(p, false); p.X = Rand(-80, w + 80); }
                    break;

                case EffectKind.Leaves:
                case EffectKind.Sakura:
                    p.Y += p.Speed * dt;
                    p.X += (Math.Sin(_time * 0.9 + p.Phase) * p.Drift + p.Drift * 0.35) * dt;
                    p.Rot += p.Spin * dt;
                    if (p.Y - 30 > h || p.X > w + 70) { Spawn(p, false); p.X = Rand(-40, w * 0.85); }
                    break;

                case EffectKind.Stars:
                    p.Alpha = 0.25 + 0.7 * (0.5 + 0.5 * Math.Sin(_time * p.Speed + p.Phase));
                    break;

                case EffectKind.Bubbles:
                    p.Y -= p.Speed * dt;
                    p.X += Math.Sin(_time * 1.2 + p.Phase) * p.Drift * dt;
                    if (p.Y + p.Size < -20) { Spawn(p, false); p.X = Rand(0, w); }
                    break;

                case EffectKind.Fireflies:
                    p.X += Math.Cos(_time * 0.5 + p.Phase) * p.Speed * dt;
                    p.Y += Math.Sin(_time * 0.42 + p.Phase * 1.7) * p.Speed * dt;
                    p.Alpha = 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(_time * 2.1 + p.Phase));
                    if (p.X < -30) p.X = w + 20;
                    if (p.X > w + 30) p.X = -20;
                    if (p.Y < -30) p.Y = h + 20;
                    if (p.Y > h + 30) p.Y = -20;
                    break;

                case EffectKind.Embers:
                    p.Y -= p.Speed * dt;
                    p.X += Math.Sin(_time * 1.6 + p.Phase) * p.Drift * dt;
                    p.Alpha = 0.15 + 0.85 * (0.5 + 0.5 * Math.Sin(_time * 3.4 + p.Phase));
                    if (p.Y + p.Size < -20) { Spawn(p, false); p.X = Rand(0, w); }
                    break;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Kind == EffectKind.None || _items.Count == 0) return;
            foreach (var p in _items) Draw(dc, p);
        }

        private void Draw(DrawingContext dc, P p)
        {
            switch (Kind)
            {
                case EffectKind.Snow:
                {
                    double r = p.Size * 0.5;
                    dc.PushOpacity(Math.Max(0, Math.Min(1, p.Alpha)));
                    dc.DrawEllipse(SnowCore, null, new Point(p.X, p.Y), r * 2.6, r * 2.6);
                    dc.DrawEllipse(Brushes.White, null, new Point(p.X, p.Y), r * 0.85, r * 0.85);
                    dc.Pop();
                    break;
                }

                case EffectKind.Rain:
                {
                    var pen = p.Size > 20 ? RainFat : (p.Size > 14 ? RainMid : RainThin);
                    dc.PushOpacity(Math.Max(0.05, Math.Min(1, p.Alpha)));
                    dc.DrawLine(pen, new Point(p.X, p.Y), new Point(p.X - p.Size * p.Drift, p.Y + p.Size));
                    dc.Pop();
                    break;
                }

                case EffectKind.Leaves:
                case EffectKind.Sakura:
                {
                    var palette = Kind == EffectKind.Leaves ? LeafBrushes : SakuraBrushes;
                    var brush = palette[(int)(p.Extra * palette.Length) % palette.Length];
                    var geometry = Kind == EffectKind.Leaves ? LeafGeometry : PetalGeometry;

                    dc.PushOpacity(Math.Max(0.05, Math.Min(1, p.Alpha)));
                    dc.PushTransform(new TranslateTransform(p.X, p.Y));
                    dc.PushTransform(new RotateTransform(p.Rot));
                    dc.PushTransform(new ScaleTransform(p.Size, p.Size));
                    dc.DrawGeometry(brush, null, geometry);
                    dc.Pop(); dc.Pop(); dc.Pop();
                    dc.Pop();
                    break;
                }

                case EffectKind.Stars:
                {
                    double a = Math.Max(0, Math.Min(1, p.Alpha));
                    dc.PushOpacity(a * 0.28);
                    dc.DrawEllipse(Brushes.White, null, new Point(p.X, p.Y), p.Size * 3.0, p.Size * 3.0);
                    dc.Pop();
                    dc.PushOpacity(a);
                    dc.DrawEllipse(Brushes.White, null, new Point(p.X, p.Y), p.Size * 0.7, p.Size * 0.7);
                    if (p.Extra > 0.86)
                    {
                        double s = p.Size * 4.6;
                        dc.DrawLine(StarSparkPen, new Point(p.X - s, p.Y), new Point(p.X + s, p.Y));
                        dc.DrawLine(StarSparkPen, new Point(p.X, p.Y - s), new Point(p.X, p.Y + s));
                    }
                    dc.Pop();
                    break;
                }

                case EffectKind.Bubbles:
                {
                    dc.PushOpacity(Math.Max(0.05, Math.Min(1, p.Alpha)));
                    var pen = p.Size > 9 ? BubblePen : BubblePenSmall;
                    dc.DrawEllipse(BubbleFill, pen, new Point(p.X, p.Y), p.Size, p.Size);
                    dc.DrawEllipse(Brushes.White, null,
                        new Point(p.X - p.Size * 0.32, p.Y - p.Size * 0.34), p.Size * 0.16, p.Size * 0.16);
                    dc.Pop();
                    break;
                }

                case EffectKind.Fireflies:
                {
                    var color = FireflyBrushes[(int)(p.Extra * FireflyBrushes.Length) % FireflyBrushes.Length];
                    double a = Math.Max(0, Math.Min(1, p.Alpha));
                    dc.PushOpacity(a * 0.35);
                    dc.DrawEllipse(color, null, new Point(p.X, p.Y), p.Size * 4.5, p.Size * 4.5);
                    dc.Pop();
                    dc.PushOpacity(a);
                    dc.DrawEllipse(Brushes.White, null, new Point(p.X, p.Y), p.Size * 0.75, p.Size * 0.75);
                    dc.Pop();
                    break;
                }

                case EffectKind.Embers:
                {
                    double a = Math.Max(0, Math.Min(1, p.Alpha));
                    dc.PushOpacity(a * 0.4);
                    dc.DrawEllipse(EmberGlow, null, new Point(p.X, p.Y), p.Size * 3.4, p.Size * 3.4);
                    dc.Pop();
                    dc.PushOpacity(a);
                    dc.DrawEllipse(EmberCore, null, new Point(p.X, p.Y), p.Size * 0.8, p.Size * 0.8);
                    dc.Pop();
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static GradientStop Stop(Color color, double offset) => new GradientStop(color, offset);

        private static RadialGradientBrush Frozen(RadialGradientBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Frozen(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static Pen Frozen(Pen pen)
        {
            pen.Freeze();
            return pen;
        }

        private static Geometry FrozenGeometry(string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }
    }
}
