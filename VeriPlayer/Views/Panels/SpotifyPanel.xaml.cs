using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Services;
using Path = System.Windows.Shapes.Path;

namespace VeriPlayer.Views.Panels
{
    public partial class SpotifyPanel : UserControl, IModalPanel
    {
        private bool _guard = true;
        private bool _busy;

        public event EventHandler CloseRequested;
        public string Title => "Сервисы";

        public SpotifyPanel()
        {
            InitializeComponent();

            ClientIdBox.Text = App.Settings.SpotifyClientId ?? "";
            _guard = true;
            PortBox.Text = (App.Settings.SpotifyPort <= 0 ? 8899 : App.Settings.SpotifyPort).ToString();
            _guard = false;

            UpdateRedirect();
            UpdateState();
        }

        // ================================================================== состояние

        private int Port => int.TryParse(PortBox.Text, out var value) && value >= 1024 && value <= 65000
            ? value
            : (App.Settings.SpotifyPort <= 0 ? 8899 : App.Settings.SpotifyPort);

        private void UpdateRedirect() => RedirectBox.Text = SpotifyService.RedirectUri(Port);

        private void UpdateState()
        {
            bool connected = SpotifyService.IsConnected;

            ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            PlaylistsButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            ClientIdBox.IsEnabled = !connected;

            if (connected)
            {
                SpotifyStatus.Text = "Подключено" +
                                     (string.IsNullOrWhiteSpace(SpotifyService.UserName) ? "" : " как " + SpotifyService.UserName);
                SpotifyHint.Text = "Готово! Можно посмотреть свои плейлисты.";
            }
            else
            {
                SpotifyStatus.Text = "Не подключено";
            }
        }

        private void Port_Changed(object sender, TextChangedEventArgs e)
        {
            if (_guard) return;
            UpdateRedirect();
        }

        private void CopyRedirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RedirectBox.Text);
                MainWindow.Instance?.ShowToast("Адрес скопирован", "ok");
            }
            catch { }
        }

        // ================================================================== подключение

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            App.Settings.SpotifyClientId = (ClientIdBox.Text ?? "").Trim();
            App.Settings.SpotifyPort = Port;
            App.SaveSettings();

            _busy = true;
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Подключение…";
            SpotifyHint.Text = "Открываю браузер…";

            try
            {
                var progress = new Progress<string>(text => SpotifyHint.Text = text);
                var (ok, error) = await SpotifyService.ConnectAsync(App.Settings.SpotifyClientId, Port, progress);

                if (!ok)
                {
                    SpotifyHint.Text = error;
                    MainWindow.Instance?.ShowToast(error, "error");
                    return;
                }

                SpotifyHint.Text = "Аккаунт подключён.";
                MainWindow.Instance?.ShowToast("Spotify подключён", "ok");
                UpdateState();
                await LoadPlaylists();
            }
            catch (Exception ex)
            {
                SpotifyHint.Text = "Ошибка: " + ex.Message;
                MainWindow.Instance?.ShowToast("Ошибка подключения: " + ex.Message, "error");
            }
            finally
            {
                _busy = false;
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Подключить Spotify";
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            SpotifyService.Disconnect();
            PlaylistsHost.Visibility = Visibility.Collapsed;
            PlaylistsHost.Children.Clear();
            SpotifyHint.Text = "Аккаунт отключён.";
            UpdateState();
            MainWindow.Instance?.ShowToast("Spotify отключён", "info");
        }

        // ================================================================== плейлисты

        private async void Playlists_Click(object sender, RoutedEventArgs e) => await LoadPlaylists();

        private async System.Threading.Tasks.Task LoadPlaylists()
        {
            if (_busy) return;
            _busy = true;

            PlaylistsHost.Visibility = Visibility.Visible;
            PlaylistsHost.Children.Clear();
            PlaylistsHost.Children.Add(SectionHeader("Мои плейлисты"));
            PlaylistsHost.Children.Add(InfoLine("Загружаю…"));

            try
            {
                var playlists = await SpotifyService.GetPlaylistsAsync();
                PlaylistsHost.Children.Clear();
                PlaylistsHost.Children.Add(SectionHeader("Мои плейлисты"));

                if (playlists.Count == 0)
                {
                    PlaylistsHost.Children.Add(InfoLine("Плейлисты не найдены или доступ не разрешён."));
                    return;
                }

                foreach (var playlist in playlists)
                    PlaylistsHost.Children.Add(BuildPlaylistRow(playlist));
            }
            catch (Exception ex)
            {
                PlaylistsHost.Children.Clear();
                PlaylistsHost.Children.Add(InfoLine("Ошибка: " + ex.Message));
            }
            finally
            {
                _busy = false;
            }
        }

        private TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("TextSection"),
            Margin = new Thickness(0, 4, 0, 8)
        };

        private TextBlock InfoLine(string text) => new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("TextCaption"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 0, 6)
        };

        private Border BuildPlaylistRow(Models.SpotifyPlaylistInfo playlist)
        {
            var art = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("InputBrush"),
                ClipToBounds = true
            };
            var image = LoadRemoteImage(playlist.ImageUrl);
            if (image != null)
            {
                art.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
            }
            else
            {
                art.Child = new Path
                {
                    Data = (Geometry)FindResource("IconNote"),
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    Stroke = (Brush)FindResource("TextFaintBrush"),
                    StrokeThickness = 1.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            info.Children.Add(new TextBlock
            {
                Text = playlist.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = playlist.TrackCount + " треков" +
                       (string.IsNullOrWhiteSpace(playlist.Owner) ? "" : " · " + playlist.Owner),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("TextFaintBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });

            var open = new Button
            {
                Content = "Открыть",
                Style = (Style)FindResource("BtnGhost"),
                Padding = new Thickness(13, 6, 13, 6),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            open.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(playlist.Url)) SpotifyService.OpenExternal(playlist.Url);
                else MainWindow.Instance?.ShowToast("Ссылка недоступна", "warn");
            };

            var list = new Button
            {
                Style = (Style)FindResource("BtnIconSmall"),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Показать треки",
                VerticalAlignment = VerticalAlignment.Center
            };
            list.Content = new Path
            {
                Data = (Geometry)FindResource("IconChevronRight"),
                Width = 15,
                Height = 15,
                Stretch = Stretch.None,
                Stroke = (Brush)FindResource("TextFaintBrush"),
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            list.Click += async (s, e) => await ShowTracks(playlist);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(open);
            actions.Children.Add(list);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(art, 0);
            Grid.SetColumn(info, 1);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(art);
            grid.Children.Add(info);
            grid.Children.Add(actions);

            return new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
                BorderBrush = (Brush)FindResource("HairlineSoftBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid
            };
        }

        private async System.Threading.Tasks.Task ShowTracks(Models.SpotifyPlaylistInfo playlist)
        {
            PlaylistsHost.Children.Clear();

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
            var back = new Button
            {
                Content = "Назад",
                Style = (Style)FindResource("BtnGhost"),
                Padding = new Thickness(13, 6, 13, 6),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += async (s, e) => await LoadPlaylists();
            header.Children.Add(back);
            header.Children.Add(new TextBlock
            {
                Text = playlist.Name,
                Style = (Style)FindResource("TextH3"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            PlaylistsHost.Children.Add(header);

            var host = new StackPanel();
            PlaylistsHost.Children.Add(host);
            host.Children.Add(InfoLine("Загружаю треки…"));

            var tracks = await SpotifyService.GetPlaylistTracksAsync(playlist.Id);
            host.Children.Clear();

            if (tracks.Count == 0)
            {
                host.Children.Add(InfoLine("Треки недоступны."));
                return;
            }

            foreach (var track in tracks)
            {
                var item = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 3),
                    Background = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                item.Child = new TextBlock
                {
                    Text = track.Line,
                    FontSize = 12.5,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                item.MouseLeftButtonUp += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(track.Url)) SpotifyService.OpenExternal(track.Url);
                };
                host.Children.Add(item);
            }
        }

        // ================================================================== Яндекс

        private void YandexImport_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.CloseModal();
            MainWindow.Instance?.ImportM3u();
        }

        private static ImageSource LoadRemoteImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 120;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
