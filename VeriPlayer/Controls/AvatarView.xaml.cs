using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Services;

namespace VeriPlayer.Controls
{
    public partial class AvatarView : UserControl
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(string), typeof(AvatarView),
            new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty InitialProperty = DependencyProperty.Register(
            nameof(Initial), typeof(string), typeof(AvatarView),
            new PropertyMetadata("?", OnVisualChanged));

        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
            nameof(Size), typeof(double), typeof(AvatarView),
            new PropertyMetadata(44.0, OnVisualChanged));

        public static readonly DependencyProperty RingProperty = DependencyProperty.Register(
            nameof(Ring), typeof(bool), typeof(AvatarView),
            new PropertyMetadata(false, OnVisualChanged));

        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public string Initial
        {
            get => (string)GetValue(InitialProperty);
            set => SetValue(InitialProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public bool Ring
        {
            get => (bool)GetValue(RingProperty);
            set => SetValue(RingProperty, value);
        }

        public AvatarView()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateVisual();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AvatarView view) view.UpdateVisual();
        }

        private void UpdateVisual()
        {
            double size = Size <= 0 ? 44 : Size;
            RootGrid.Width = size;
            RootGrid.Height = size;

            InitialText.Text = string.IsNullOrWhiteSpace(Initial) ? "?" : Initial.Substring(0, 1).ToUpperInvariant();
            InitialText.FontSize = Math.Max(10, size * 0.42);

            var image = Storage.LoadImage(Source, (int)Math.Max(64, size * 2));
            if (image != null)
            {
                var brush = new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                brush.Freeze();
                Circle.Background = brush;
                InitialText.Visibility = Visibility.Collapsed;
            }
            else
            {
                Circle.Background = ThemeService.Solid(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                InitialText.Visibility = Visibility.Visible;
            }

            if (Ring)
            {
                Circle.BorderBrush = ThemeService.Solid(ThemeService.Alpha(ThemeService.AccentColor, 0xAA));
                Circle.BorderThickness = new Thickness(Math.Max(1, size / 26));
            }
            else
            {
                Circle.BorderBrush = ThemeService.Solid(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                Circle.BorderThickness = new Thickness(1);
            }
        }
    }
}
