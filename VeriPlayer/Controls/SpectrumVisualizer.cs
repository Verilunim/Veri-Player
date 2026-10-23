using System;
using System.Windows;
using System.Windows.Media;
using VeriPlayer.Services;

namespace VeriPlayer.Controls
{
    /// <summary>Спектр, который реагирует на реальный звук (FFT из NAudio).</summary>
    public class SpectrumVisualizer : FrameworkElement
    {
        private const int Bins = 48;

        private readonly float[] _level = new float[Bins];
        private readonly float[] _target = new float[Bins];
        private readonly float[] _peak = new float[Bins];
        private readonly Random _random = new Random();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private double _lastFrame;
        private double _time;
        private bool _attached;
        private bool _live;

        public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
            nameof(Active), typeof(bool), typeof(SpectrumVisualizer),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnActiveChanged));

        public bool Active
        {
            get => (bool)GetValue(ActiveProperty);
            set => SetValue(ActiveProperty, value);
        }

        public SpectrumVisualizer()
        {
            IsHitTestVisible = false;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpectrumVisualizer viz) viz._live = (bool)e.NewValue;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_attached) return;
            _attached = true;
            AudioService.Spectrum += OnSpectrum;
            _lastFrame = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_attached) return;
            _attached = false;
            AudioService.Spectrum -= OnSpectrum;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void OnSpectrum(float[] bins)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                _live = true;
                for (int i = 0; i < Bins; i++)
                    _target[i] = i < bins.Length ? bins[i] : 0f;
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double since = now - _lastFrame;
            if (since < 1.0 / 50.0) return;
            _lastFrame = now;
            if (!IsVisible || ActualWidth < 4) return;

            double dt = Math.Min(0.1, since);
            _time += dt;
            bool playing = AudioService.IsPlaying;

            for (int i = 0; i < Bins; i++)
            {
                float target;
                if (playing && _live)
                {
                    target = _target[i];
                }
                else
                {
                    // «живой покой» — лёгкая волна, чтобы панель не выглядела мёртвой
                    double idle = 0.05 + 0.045 * Math.Sin(_time * 1.4 - i * 0.35);
                    target = (float)Math.Max(0, idle + 0.02 * _random.NextDouble());
                }

                float speed = target > _level[i] ? 0.42f : 0.13f;
                _level[i] += (target - _level[i]) * speed;

                if (_level[i] > _peak[i]) _peak[i] = _level[i];
                else _peak[i] = Math.Max(_level[i], _peak[i] - (float)(dt * 0.55));
            }

            if (!playing) _live = false;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 8 || height < 8) return;

            var accent = ThemeService.AccentColor;
            var second = ThemeService.SecondColor;

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0)
            };
            gradient.GradientStops.Add(new GradientStop(ThemeService.Alpha(accent, 0x33), 0));
            gradient.GradientStops.Add(new GradientStop(ThemeService.Alpha(accent, 0xCC), 0.55));
            gradient.GradientStops.Add(new GradientStop(ThemeService.Alpha(second, 0xFF), 1));
            gradient.Freeze();

            var peakBrush = ThemeService.Solid(ThemeService.Alpha(Colors.White, 0x99));

            double gap = 3;
            double barWidth = Math.Max(2.0, (width - gap * (Bins - 1)) / Bins);
            double radius = Math.Min(barWidth / 2.0, 4);

            for (int i = 0; i < Bins; i++)
            {
                double x = i * (barWidth + gap);
                double value = Math.Max(0.012, _level[i]);
                double barHeight = value * height;
                double y = height - barHeight;

                dc.DrawRoundedRectangle(gradient, null, new Rect(x, y, barWidth, barHeight), radius, radius);

                double peakY = height - Math.Max(barHeight + 3, _peak[i] * height);
                if (peakY < height - 6)
                    dc.DrawRoundedRectangle(peakBrush, null, new Rect(x, peakY, barWidth, 2.2), 1.1, 1.1);
            }
        }
    }
}
