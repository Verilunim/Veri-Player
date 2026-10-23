using System;
using System.Windows;
using System.Windows.Controls;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    /// <summary>Статус Pro+ и активация ключа. Открывается и по клику на закрытую функцию.</summary>
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

            // активному пользователю поле ключа нужно только для продления подписки
            bool showActivate = !status.ByRole;
            ActivateBlock.Visibility = showActivate ? Visibility.Visible : Visibility.Collapsed;
            ActivateButton.Visibility = showActivate ? Visibility.Visible : Visibility.Collapsed;
            ActivateButton.Content = status.Active ? "Продлить" : "Активировать";
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            var (ok, error) = LicenseService.Activate(AuthService.Current, KeyInput.Text);
            if (!ok)
            {
                ErrorText.Text = error;
                ErrorRow.Visibility = Visibility.Visible;
                return;
            }

            ErrorRow.Visibility = Visibility.Collapsed;
            KeyInput.Clear();
            FeatureHint.Visibility = Visibility.Collapsed;
            Refresh();
            MainWindow.Instance?.ShowToast(LicenseService.For(AuthService.Current).Text, "ok");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
