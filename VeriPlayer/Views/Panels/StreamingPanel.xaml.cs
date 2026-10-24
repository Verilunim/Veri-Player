using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    /// <summary>Страница внешнего сервиса: подключение аккаунта и его музыка. Один код для SoundCloud и YouTube Music.</summary>
    public partial class StreamingPanel : UserControl
    {
        public enum Kind { SoundCloud, YouTubeMusic }

        public sealed class Row
        {
            public string Title { get; set; } = "";
            public string Subtitle { get; set; } = "";
            public string Right { get; set; } = "";
            public Geometry Icon { get; set; }
            public ImageSource Cover { get; set; }
            public object Payload { get; set; }
        }

        private readonly Kind _kind;
        private bool IsSoundCloud => _kind == Kind.SoundCloud;
        private ServiceAccount Account => IsSoundCloud ? SoundCloudService.Account : YouTubeMusicService.Account;
        private bool Connected => IsSoundCloud ? SoundCloudService.IsConnected : YouTubeMusicService.IsConnected;

        public StreamingPanel(Kind kind)
        {
            InitializeComponent();
            _kind = kind;

            Headline.Text = IsSoundCloud ? "SoundCloud" : "YouTube Music";
            PortalButton.Content = IsSoundCloud ? "Открыть портал SoundCloud" : "Открыть Google Cloud Console";
            Intro.Text = IsSoundCloud
                ? "Подключите свой аккаунт SoundCloud: ваши треки, лайки и плейлисты будут здесь и будут играть прямо в Veri Player. " +
                  "Нужно своё приложение SoundCloud — его ключи хранятся только у вас, в зашифрованном виде."
                : "Подключите YouTube Music, чтобы видеть свои плейлисты и понравившиеся треки. Правила YouTube не разрешают " +
                  "проигрывать звук отдельно от их плеера, поэтому треки открываются в YouTube Music. Доступ только для чтения.";
            Steps.Text = IsSoundCloud
                ? "1. Откройте портал SoundCloud → Your apps → зарегистрируйте приложение.\n" +
                  "2. В поле Redirect URI укажите адрес выше.\n" +
                  "3. Скопируйте Client ID и Client Secret сюда и нажмите «Подключить»."
                : "1. В Google Cloud Console создайте проект и включите YouTube Data API v3.\n" +
                  "2. APIs & Services → Credentials → Create credentials → OAuth client ID → тип «Desktop app».\n" +
                  "3. Скопируйте Client ID и Client Secret сюда и нажмите «Подключить». Пока приложение в режиме Testing, " +
                  "добавьте свой Google-аккаунт в Test users.";

            ClientIdBox.Text = Account.ClientId;
            ClientSecretBox.Password = Account.ClientSecret;
            PortBox.Text = Account.Port.ToString();
            UpdateRedirect();
            Refresh();
        }

        public void Refresh()
        {
            bool connected = Connected;
            Kicker.Text = IsSoundCloud ? "МУЗЫКА ИЗ SOUNDCLOUD" : "МУЗЫКА ИЗ YOUTUBE MUSIC";
            Status.Text = connected ? "Подключено: " + Account.UserName : "Не подключено";
            ConnectView.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            LibraryView.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            Attribution.Visibility = connected && IsSoundCloud ? Visibility.Visible : Visibility.Collapsed;
            Attribution.Text = "Треки воспроизводятся из SoundCloud. Автор указан у каждого трека; страница трека — правой кнопкой мыши → «Открыть на SoundCloud».";

            if (connected) ShowRoot();
        }

        // ================================================================== подключение

        private int Port => int.TryParse(PortBox.Text, out var p) ? p : 0;

        private void Port_Changed(object sender, TextChangedEventArgs e) => UpdateRedirect();

        private void UpdateRedirect()
        {
            if (RedirectBox == null) return;
            RedirectBox.Text = OAuthLoopback.RedirectUri(Port);
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            ConnectError.Visibility = Visibility.Collapsed;
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Ждём подтверждения в браузере…";
            try
            {
                var (ok, error) = IsSoundCloud
                    ? await SoundCloudService.ConnectAsync(ClientIdBox.Text, ClientSecretBox.Password, Port)
                    : await YouTubeMusicService.ConnectAsync(ClientIdBox.Text, ClientSecretBox.Password, Port);
                if (!ok)
                {
                    ConnectError.Text = error;
                    ConnectError.Visibility = Visibility.Visible;
                    return;
                }
                MainWindow.Instance?.ShowToast(Headline.Text + " подключён", "ok");
                Refresh();
            }
            finally
            {
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Подключить";
            }
        }

        private void Portal_Click(object sender, RoutedEventArgs e) =>
            YouTubeMusicService.Open(IsSoundCloud ? "https://soundcloud.com/you/apps" : "https://console.cloud.google.com/apis/credentials");

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Confirm("Отключить " + Headline.Text + "? Ключи приложения останутся, вход нужно будет повторить.", () =>
            {
                if (IsSoundCloud) SoundCloudService.Disconnect(); else YouTubeMusicService.Disconnect();
                Refresh();
            }, "Отключить");
        }
    }
}
