using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeriPlayer.Services;

namespace VeriPlayer.Views
{
    /// <summary>Отдельное окно активации ключа Pro+.</summary>
    public partial class ProActivationWindow : Window
    {
        public ProActivationWindow()
        {
            InitializeComponent();

            var user = AuthService.Current;
            var status = LicenseService.For(user);
            StatusText.Text = status.Active
                ? status.Text + ". Новый ключ продлит подписку."
                : "Вставьте ключ, который вы получили от владельца Veri Player.";
            HowTo.Text = "Ключ работает только для аккаунта «" + (user?.Nick ?? "") + "». " +
                         "Чтобы получить ключ, сообщите владельцу этот ник.";

            Loaded += (s, e) => KeyInput.Focus();
        }

        private void KeyInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            ActivateButton.IsEnabled = KeyInput.Text.Trim().Length > 0;
            ErrorRow.Visibility = Visibility.Collapsed;
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText()) KeyInput.Text = Clipboard.GetText().Trim();
            }
            catch { }
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

            DoneStatus.Text = LicenseService.For(AuthService.Current).Text;
            EntryView.Visibility = Visibility.Collapsed;
            DoneView.Visibility = Visibility.Visible;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter && ActivateButton.IsEnabled && EntryView.Visibility == Visibility.Visible)
                Activate_Click(this, new RoutedEventArgs());
            base.OnKeyDown(e);
        }
    }
}
