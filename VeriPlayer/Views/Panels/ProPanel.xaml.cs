using System;
using System.Windows;
using System.Windows.Controls;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    /// <summary>Статус Pro+ и что в него входит. Открывается и по клику на закрытую функцию.</summary>
    public partial class ProPanel : UserControl, IModalPanel
    {
        public event EventHandler CloseRequested;

        public string Title => "Pro+";

        /// <param name="feature">Название функции, на которую нажали без подписки (или null).</param>
        public ProPanel(string feature = null)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(feature) && !LicenseService.IsProCurrent)
            {
                FeatureHint.Text = "«" + feature + "» доступно в подписке Pro+.";
                FeatureHint.Visibility = Visibility.Visible;
            }

            var nick = AuthService.Current?.Nick ?? "";
            HowTo.Text = "Ключ выдаёт владелец Veri Player. Сообщите ему свой ник — «" + nick +
                         "»: ключ работает только для этого аккаунта.";
            Refresh();
        }

        private void Refresh()
        {
            var status = LicenseService.For(AuthService.Current);
            StatusText.Text = status.Text;

            // команде ключ не нужен — вместо активации она выпускает ключи; подписчику — только для продления
            HowTo.Visibility = !status.ByRole && !status.Active ? Visibility.Visible : Visibility.Collapsed;
            ActivateButton.Content = status.ByRole ? "Выпустить ключи Pro+"
                : status.Active ? "Продлить ключом" : "Ввести ключ активации";
            if (status.Active) FeatureHint.Visibility = Visibility.Collapsed;
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (LicenseService.For(AuthService.Current).ByRole)
            {
                KeyGeneratorWindow.Open(Window.GetWindow(this));
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            new ProActivationWindow { Owner = Window.GetWindow(this) }.ShowDialog();
            Refresh();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
