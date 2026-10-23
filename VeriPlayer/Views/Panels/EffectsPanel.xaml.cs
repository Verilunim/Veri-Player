using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Controls;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class EffectsPanel : UserControl, IModalPanel
    {
        private readonly List<Button> _cards = new List<Button>();
        private bool _guard = true;

        public event EventHandler CloseRequested;
        public string Title => "Эффекты фона";

        public EffectsPanel()
        {
            InitializeComponent();

            foreach (var info in Effects.All) _cards.Add(BuildCard(info));
            EffectList.ItemsSource = _cards;

            _guard = true;
            IntensitySlider.Value = Math.Max(15, Math.Min(120, App.Settings.EffectIntensity * 100));
            VisualizerToggle.IsChecked = App.Settings.Visualizer;
            MotionToggle.IsChecked = App.Settings.ReduceMotion;
            _guard = false;

            UpdateIntensityText();
            Highlight();
        }

        private Button BuildCard(Effects.Info info)
        {
            var glyph = new TextBlock
            {
                Text = info.Glyph,
                FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var title = new TextBlock
            {
                Text = info.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var hint = new TextBlock
            {
                Text = info.Hint,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF))
            };

            var stack = new StackPanel();
            stack.Children.Add(glyph);
            stack.Children.Add(title);
            stack.Children.Add(hint);

            var button = new Button
            {
                Content = stack,
                Width = 134,
                Height = 108,
                Margin = new Thickness(0, 0, 12, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = info.Key,
                Style = (Style)FindResource("CardButton")
            };
            button.Click += (s, e) => Select(info.Key);
            return button;
        }

        private void Select(string key)
        {
            Diagnostics.Log($"EffectsPanel.Select: key={key}");
            App.Settings.Effect = key;
            App.SaveSettings();
            Diagnostics.Log($"EffectsPanel.Select: Saved, calling ApplyAppearance");
            MainWindow.Instance?.ApplyAppearance();
            Highlight();
            Diagnostics.Log($"EffectsPanel.Select: Done");
        }

        private void Highlight()
        {
            foreach (var card in _cards)
            {
                bool active = (card.Tag as string) == App.Settings.Effect;
                card.Background = active
                    ? (Brush)FindResource("AccentSoftBrush")
                    : new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
                card.BorderBrush = active
                    ? (Brush)FindResource("AccentBorderBrush")
                    : (Brush)FindResource("HairlineBrush");
            }
        }

        private void UpdateIntensityText()
        {
            IntensityText.Text = Math.Round(App.Settings.EffectIntensity * 100) + "%";
        }

        private void Intensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_guard) return;
            App.Settings.EffectIntensity = IntensitySlider.Value / 100.0;
            App.SaveSettings();
            var particles = FindParticles();
            if (particles != null) particles.Intensity = App.Settings.EffectIntensity;
            UpdateIntensityText();
        }

        private void Visualizer_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Visualizer = VisualizerToggle.IsChecked == true;
            App.SaveSettings();
        }

        private void Motion_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.ReduceMotion = MotionToggle.IsChecked == true;
            App.SaveSettings();
            MainWindow.Instance?.ApplyAppearance();
        }

        private ParticleField FindParticles()
        {
            var window = MainWindow.Instance;
            if (window == null) return null;
            return window.FindName("Particles") as ParticleField;
        }

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
