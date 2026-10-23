using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class ThemePanel : UserControl, IModalPanel
    {
        private static readonly (string Name, double Hue, double Sat)[] Palette =
        {
            ("Лаванда", 268, 0.68),
            ("Океан", 212, 0.72),
            ("Изумруд", 158, 0.62),
            ("Закат", 18, 0.78),
            ("Роза", 336, 0.66),
            ("Золото", 44, 0.72),
            ("Небо", 196, 0.60),
            ("Фуксия", 300, 0.70)
        };

        private readonly DispatcherTimer _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private bool _guard = true;

        public event EventHandler CloseRequested;
        public string Title => "RGB-выбор цвета";

        public ThemePanel()
        {
            InitializeComponent();
            BuildPresets();

            _guard = true;
            Wheel.Hue = App.Settings.Hue;
            Wheel.Saturation = App.Settings.Saturation;
            ValueSlider.Value = Math.Max(35, App.Settings.Value * 100);
            SecondHueSlider.Value = Math.Max(0, Math.Min(180, App.Settings.SecondHueOffset));
            SpeedSlider.Value = Math.Max(4, Math.Min(70, App.Settings.AutoHueSpeed));
            AutoHue.IsChecked = App.Settings.AutoHue;
            _guard = false;

            Wheel.ColorChanged += (s, e) => Apply();
            _autoTimer.Tick += (s, e) => AutoTick();

            Loaded += (s, e) => UpdatePreview();
            Unloaded += (s, e) => _autoTimer.Stop();
        }

        private void BuildPresets()
        {
            foreach (var entry in Palette)
            {
                var color = ThemeService.Hsv(entry.Hue, entry.Sat, 1.0);
                var ellipse = new Ellipse
                {
                    Width = 22,
                    Height = 22,
                    Fill = new SolidColorBrush(color)
                };
                var button = new Button
                {
                    Content = ellipse,
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = entry.Name,
                    Template = BuildSwatchTemplate()
                };
                double hue = entry.Hue;
                double sat = entry.Sat;
                button.Click += (s, e) =>
                {
                    _guard = true;
                    Wheel.Hue = hue;
                    Wheel.Saturation = sat;
                    _guard = false;
                    Apply();
                };
                Presets.Items.Add(button);
            }
        }

        private static ControlTemplate BuildSwatchTemplate()
        {
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            return template;
        }

        private void Apply()
        {
            if (_guard) return;

            App.Settings.Hue = Wheel.Hue;
            App.Settings.Saturation = Wheel.Saturation;
            App.Settings.Value = ValueSlider.Value / 100.0;
            App.Settings.SecondHueOffset = SecondHueSlider.Value;
            App.Settings.AutoHue = AutoHue.IsChecked == true;
            App.Settings.AutoHueSpeed = SpeedSlider.Value;

            ThemeService.Apply(App.Settings);
            App.SaveSettings();
            MainWindow.Instance?.RefreshUserCard();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var accent = ThemeService.Hsv(Wheel.Hue, Math.Max(0.05, Wheel.Saturation), Math.Max(0.35, ValueSlider.Value / 100.0));
            var second = ThemeService.Hsv(Wheel.Hue + SecondHueSlider.Value, Math.Max(0.05, Wheel.Saturation), 1.0);

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(accent, 0));
            brush.GradientStops.Add(new GradientStop(second, 1));
            Preview.Background = brush;

            Wheel.PreviewBrush = new SolidColorBrush(accent);
            HexText.Text = ThemeService.ToHex(accent) + "  ·  " + ThemeService.ToHex(second);
        }

        private void AutoTick()
        {
            Wheel.Hue = (Wheel.Hue + SpeedSlider.Value * 0.05) % 360;
            Apply();
        }

        private void AutoHue_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.AutoHue = AutoHue.IsChecked == true;
            App.SaveSettings();
            if (AutoHue.IsChecked == true) _autoTimer.Start();
            else _autoTimer.Stop();
        }

        private void Value_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();
        private void SecondHue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();
        private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_guard) return;
            App.Settings.AutoHueSpeed = SpeedSlider.Value;
            App.SaveSettings();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _guard = true;
            App.Settings.Hue = 268;
            App.Settings.Saturation = 0.68;
            App.Settings.Value = 1.0;
            App.Settings.SecondHueOffset = 46;
            App.Settings.AutoHue = false;

            Wheel.Hue = 268;
            Wheel.Saturation = 0.68;
            ValueSlider.Value = 100;
            SecondHueSlider.Value = 46;
            AutoHue.IsChecked = false;
            _guard = false;

            _autoTimer.Stop();
            Apply();
        }

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
