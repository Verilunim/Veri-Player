using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    /// <summary>Приветствие Pro+: показывается один раз при первом запуске после активации ключа.</summary>
    public partial class ProWelcomePanel : UserControl, IModalPanel
    {
        public event EventHandler CloseRequested;

        public string Title => "Pro+";

        public ProWelcomePanel()
        {
            InitializeComponent();

            var nick = AuthService.Current?.Nick ?? "";
            Headline.Text = "Добро пожаловать в Pro+, " + nick + "!";
            StatusText.Text = LicenseService.For(AuthService.Current).Text;

            Loaded += (s, e) =>
            {
                // плюс «выпрыгивает» с лёгкой отдачей
                var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromSeconds(0.55))
                {
                    EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut }
                };
                PlusScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
                PlusScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
