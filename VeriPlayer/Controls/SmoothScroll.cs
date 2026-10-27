using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeriPlayer.Services;

namespace VeriPlayer.Controls
{
    /// <summary>
    /// Плавная прокрутка колесом: вместо скачка на несколько строк список доезжает до цели с замедлением.
    /// Ставится на ListBox или ScrollViewer: <c>controls:SmoothScroll.IsEnabled="True"</c>.
    /// Для виртуализированного списка нужен VirtualizingPanel.ScrollUnit="Pixel" — смещение в пикселях.
    /// </summary>
    public static class SmoothScroll
    {
        /// <summary>Пикселей на один «щелчок» колеса (Delta = 120).</summary>
        private const double PixelsPerNotch = 110;

        /// <summary>Чем больше, тем быстрее список догоняет цель.</summary>
        private const double Stiffness = 13;

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothScroll),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached("State", typeof(State), typeof(SmoothScroll));

        private sealed class State
        {
            public ScrollViewer Viewer;
            public double Target;
            public bool Running;
            public TimeSpan Last;
            public EventHandler Tick;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            if ((bool)e.NewValue) element.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PerformanceService.ReduceMotion) return;
            if (sender is not DependencyObject host) return;

            var viewer = host as ScrollViewer ?? FindViewer(host);
            if (viewer == null || viewer.ScrollableHeight <= 0) return;

            var state = (State)host.GetValue(StateProperty);
            if (state == null)
            {
                state = new State { Viewer = viewer };
                state.Tick = (s, a) => Step(state, a);
                host.SetValue(StateProperty, state);
            }
            state.Viewer = viewer;

            // новая прокрутка — от текущего места; во время движения — копим цель, как у браузера
            if (!state.Running) state.Target = viewer.VerticalOffset;
            state.Target = Math.Max(0, Math.Min(viewer.ScrollableHeight,
                state.Target - e.Delta / 120.0 * PixelsPerNotch));
            e.Handled = true;

            if (!state.Running)
            {
                state.Running = true;
                state.Last = TimeSpan.Zero;
                CompositionTarget.Rendering += state.Tick;
            }
        }

        private static void Step(State state, EventArgs args)
        {
            var now = (args as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            double dt = state.Last == TimeSpan.Zero ? 1 / 60.0 : (now - state.Last).TotalSeconds;
            if (dt <= 0) return; // тот же кадр
            state.Last = now;

            var viewer = state.Viewer;
            double current = viewer.VerticalOffset;
            double target = Math.Min(state.Target, viewer.ScrollableHeight);
            double diff = target - current;

            if (Math.Abs(diff) < 0.5 || !viewer.IsVisible)
            {
                viewer.ScrollToVerticalOffset(target);
                state.Running = false;
                CompositionTarget.Rendering -= state.Tick;
                return;
            }

            // экспоненциальное сближение — одинаково плавно при любой частоте кадров
            double k = 1 - Math.Exp(-Stiffness * Math.Min(dt, 0.1));
            viewer.ScrollToVerticalOffset(current + diff * k);
        }

        private static ScrollViewer FindViewer(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer viewer) return viewer;
                var nested = FindViewer(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
