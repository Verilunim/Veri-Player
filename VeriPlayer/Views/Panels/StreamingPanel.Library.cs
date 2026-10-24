using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class StreamingPanel
    {
        private string _tab;
        private List<Track> _queue = new List<Track>();
        private string _queueName = "";
        private int _loadVersion;

        private (string Key, string Title)[] TabList => IsSoundCloud
            ? new[] { ("mine", "Мои треки"), ("likes", "Лайки"), ("playlists", "Плейлисты") }
            : new[] { ("likes", "Понравившиеся"), ("playlists", "Плейлисты") };

        private void ShowRoot()
        {
            BackButton.Visibility = Visibility.Collapsed;
            ListTitle.Visibility = Visibility.Collapsed;
            Tabs.Visibility = Visibility.Visible;
            SelectTab(_tab ?? TabList[0].Key);
        }

        private void SelectTab(string key)
        {
            _tab = key;
            Tabs.Items.Clear();
            foreach (var (k, title) in TabList)
            {
                var button = new Button { Content = title, Style = (Style)FindResource(k == key ? "BtnPrimary" : "BtnGhost"), Margin = new Thickness(0, 0, 8, 0) };
                var captured = k;
                button.Click += (s, e) => { if (_tab != captured) SelectTab(captured); };
                Tabs.Items.Add(button);
            }
            _ = LoadTabAsync(key);
        }

        private async Task LoadTabAsync(string key)
        {
            int version = ++_loadVersion;
            ShowMessage("Загружаем…");
            try
            {
                List<Row> rows;
                if (IsSoundCloud)
                {
                    if (key == "playlists")
                        rows = (await SoundCloudService.PlaylistsAsync()).Select(p => new Row
                        { Title = p.Title, Subtitle = p.TrackCount + " тр.", Icon = Icon("IconWave"), Payload = p }).ToList();
                    else
                    {
                        var tracks = key == "likes" ? await SoundCloudService.LikesAsync() : await SoundCloudService.MyTracksAsync();
                        rows = TrackRows(tracks, key == "likes" ? "Лайки SoundCloud" : "Мои треки SoundCloud");
                    }
                }
                else
                {
                    rows = key == "playlists"
                        ? (await YouTubeMusicService.PlaylistsAsync()).Select(p => new Row
                          { Title = p.Title, Subtitle = p.Subtitle, Icon = Icon("IconWave"), Payload = p }).ToList()
                        : ItemRows(await YouTubeMusicService.PlaylistItemsAsync(YouTubeMusicService.LikesPlaylistId));
                }
                if (version != _loadVersion) return;
                Show(rows, key == "playlists" ? "Плейлистов пока нет." : "Здесь пока пусто.");
            }
            catch (Exception ex)
            {
                if (version != _loadVersion) return;
                ShowMessage("Не удалось загрузить: " + ex.Message);
                Refresh();
            }
        }

        private async Task OpenPlaylistAsync(Row row)
        {
            int version = ++_loadVersion;
            Tabs.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Visible;
            ListTitle.Text = row.Title;
            ListTitle.Visibility = Visibility.Visible;
            ShowMessage("Загружаем…");
            try
            {
                var rows = row.Payload is SoundCloudService.PlaylistInfo sc
                    ? TrackRows(await SoundCloudService.PlaylistTracksAsync(sc.Urn), sc.Title)
                    : ItemRows(await YouTubeMusicService.PlaylistItemsAsync(((YouTubeMusicService.Item)row.Payload).Id));
                if (version != _loadVersion) return;
                Show(rows, "В плейлисте пусто.");
            }
            catch (Exception ex)
            {
                if (version == _loadVersion) ShowMessage("Не удалось загрузить: " + ex.Message);
            }
        }

        private List<Row> TrackRows(List<Track> tracks, string queueName)
        {
            _queue = tracks;
            _queueName = queueName;
            var rows = tracks.Select(t => new Row
            {
                Title = t.DisplayTitle,
                Subtitle = t.DisplayArtist,
                Right = t.DurationText,
                Icon = Icon("IconNote"),
                Cover = Storage.LoadImage(t.CoverPath, 80),
                Payload = t
            }).ToList();

            // обложки догружаем в фоне и подставляем по мере готовности
            _ = Task.Run(async () =>
            {
                foreach (var t in tracks.Where(x => !x.HasCover).Take(60))
                {
                    await SoundCloudService.EnsureCoverAsync(t);
                    var row = rows.FirstOrDefault(r => r.Payload == t);
                    if (row == null || !t.HasCover) continue;
                    var image = Storage.LoadImage(t.CoverPath, 80);
                    Dispatcher.Invoke(() => { row.Cover = image; Rows.Items.Refresh(); });
                }
            });
            return rows;
        }

        private List<Row> ItemRows(List<YouTubeMusicService.Item> items) =>
            items.Select(i => new Row { Title = i.Title, Subtitle = i.Subtitle, Right = "↗", Icon = Icon("IconPlay"), Payload = i }).ToList();

        private void Show(List<Row> rows, string empty)
        {
            Rows.ItemsSource = rows;
            Rows.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ListMessage.Text = empty;
            ListMessage.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowMessage(string text)
        {
            Rows.ItemsSource = null;
            Rows.Visibility = Visibility.Collapsed;
            ListMessage.Text = text;
            ListMessage.Visibility = Visibility.Visible;
        }

        private Geometry Icon(string key) => (Geometry)FindResource(key);

        private void Back_Click(object sender, RoutedEventArgs e) => ShowRoot();

        private async void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Rows.SelectedItem is not Row row) return;
            Rows.SelectedItem = null;

            switch (row.Payload)
            {
                case Track track:
                    await SoundCloudService.EnsureCoverAsync(track);
                    AudioService.PlayTrack(track, _queue, _queueName);
                    break;
                case SoundCloudService.PlaylistInfo:
                    await OpenPlaylistAsync(row);
                    break;
                case YouTubeMusicService.Item item when _tab == "playlists" && ListTitle.Visibility != Visibility.Visible:
                    await OpenPlaylistAsync(row);
                    break;
                case YouTubeMusicService.Item item:
                    YouTubeMusicService.Open(item.Url);
                    break;
            }
        }

        private void Rows_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is not Row row) return;
            var url = row.Payload switch
            {
                Track t => t.WebUrl,
                SoundCloudService.PlaylistInfo p => p.WebUrl,
                YouTubeMusicService.Item i => i.Url,
                _ => ""
            };
            if (string.IsNullOrWhiteSpace(url)) return;

            var item = new MenuItem { Header = IsSoundCloud ? "Открыть на SoundCloud" : "Открыть в YouTube Music" };
            item.Click += (s, a) => YouTubeMusicService.Open(url);
            new ContextMenu { Items = { item }, IsOpen = true };
            e.Handled = true;
        }

        /// <summary>Ссылка на страницу трека — требование SoundCloud к сторонним плеерам.</summary>
        public static void OpenOnService(Track track)
        {
            if (track != null && !string.IsNullOrWhiteSpace(track.WebUrl)) YouTubeMusicService.Open(track.WebUrl);
        }
    }
}
