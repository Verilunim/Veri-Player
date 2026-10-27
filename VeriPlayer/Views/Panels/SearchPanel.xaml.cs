using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    /// <summary>Глобальный поиск открытой музыки: слушать сразу или скачать в библиотеку.</summary>
    public partial class SearchPanel : UserControl
    {
        private List<SearchRow> _all = new List<SearchRow>();
        private string _filter = "Все";
        private CancellationTokenSource _searchCts;

        public SearchPanel()
        {
            InitializeComponent();
            Loaded += (s, e) => QueryBox.Focus();
            BuildFilters(new string[0]);
        }

        private void Query_Changed(object sender, TextChangedEventArgs e) =>
            QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void Query_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = SearchAsync();
        }

        private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync();

        private async Task SearchAsync()
        {
            var query = QueryBox.Text.Trim();
            if (query.Length == 0) { QueryBox.Focus(); return; }

            _searchCts?.Cancel();
            var cts = _searchCts = new CancellationTokenSource();
            SearchButton.IsEnabled = false;
            StatusText.Text = "Ищем «" + query + "»…";
            EmptyText.Visibility = Visibility.Collapsed;

            try
            {
                var (results, failed) = await MusicSearchService.SearchAsync(query, cts.Token);
                if (cts.IsCancellationRequested) return;

                _all = results.Select(r => new SearchRow(r)).ToList();
                _filter = "Все";
                BuildFilters(_all.Select(r => r.Result.Source).Distinct().ToArray());
                Apply();

                StatusText.Text = _all.Count == 0 ? "" : "Найдено: " + _all.Count +
                                  (failed.Count > 0 ? "  ·  не ответил: " + string.Join(", ", failed) : "");
                if (_all.Count == 0)
                {
                    EmptyText.Text = failed.Count > 0
                        ? "Источники не ответили. Проверьте интернет и попробуйте ещё раз."
                        : "Ничего не нашлось. Попробуйте по-английски или другим словом: жанр, настроение, инструмент.";
                    EmptyText.Visibility = Visibility.Visible;
                }
                _ = LoadCoversAsync(_all, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText.Text = "";
                EmptyText.Text = "Не удалось выполнить поиск: " + ex.Message;
                EmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                if (ReferenceEquals(cts, _searchCts)) SearchButton.IsEnabled = true;
            }
        }

        // ================================================================== фильтр по источнику

        private void BuildFilters(string[] sources)
        {
            Filters.Items.Clear();
            Filters.Visibility = sources.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var name in new[] { "Все" }.Concat(sources))
            {
                var button = new Button
                {
                    Content = name,
                    Style = (Style)FindResource(name == _filter ? "BtnPrimary" : "BtnGhost"),
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(16, 6, 16, 6)
                };
                var captured = name;
                button.Click += (s, e) =>
                {
                    _filter = captured;
                    BuildFilters(sources);
                    Apply();
                };
                Filters.Items.Add(button);
            }
        }

        private void Apply()
        {
            var rows = _filter == "Все" ? _all : _all.Where(r => r.Result.Source == _filter).ToList();
            Results.ItemsSource = rows;
            Results.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (rows.Count > 0) Results.ScrollIntoView(rows[0]);
        }

        // обложки грузим в фоне маленькими, по мере готовности
        private async Task LoadCoversAsync(List<SearchRow> rows, CancellationToken ct)
        {
            using var gate = new SemaphoreSlim(4);
            var tasks = rows.Where(r => r.Result.CoverUrl.Length > 0).Select(async row =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var bytes = await MusicSearchService.Http.GetByteArrayAsync(row.Result.CoverUrl, ct);
                    var image = await Task.Run(() => Storage.LoadImage(bytes), ct);
                    if (image != null) row.Cover = image;
                }
                catch { }
                finally { gate.Release(); }
            });
            try { await Task.WhenAll(tasks); } catch { }
        }

        // ================================================================== слушать и скачать

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not SearchRow row) return;

            // уже скачанное играем из библиотеки, остальное — прямо по ссылке
            var downloaded = MusicSearchService.FindDownloaded(row.Result);
            // в очередь — только то, что играет в плеере (Bandcamp слушают на сайте)
            var rows = (Results.ItemsSource as IEnumerable<SearchRow>)?.Where(r => r.CanPlay).ToList() ?? new List<SearchRow> { row };
            var queue = rows.Select(r => MusicSearchService.FindDownloaded(r.Result) ?? MusicSearchService.ToStreamTrack(r.Result)).ToList();
            var track = downloaded ?? queue[rows.IndexOf(row)];
            AudioService.PlayTrack(track, queue, "Поиск: " + QueryBox.Text.Trim());
            MainWindow.Instance?.ShowToast("Играет «" + row.Title + "»", "info");
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not SearchRow row || row.Downloading) return;

            if (row.Result.OnSiteOnly)
            {
                // скачивание — на самом Bandcamp; файл из «Загрузок» подхватит DownloadsWatcher
                DownloadsWatcher.Start();
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.Result.PageUrl) { UseShellExecute = true }); }
                catch (Exception ex) { MainWindow.Instance?.ShowToast("Не удалось открыть браузер: " + ex.Message, "error"); return; }
                MainWindow.Instance?.ShowToast("Купите или скачайте бесплатно на Bandcamp — файл из «Загрузок» сам появится в библиотеке", "info");
                return;
            }

            if (row.Downloaded)
            {
                var existing = MusicSearchService.FindDownloaded(row.Result);
                if (existing != null) AudioService.PlayTrack(existing, LibraryService.Tracks, "Библиотека");
                return;
            }

            row.Downloading = true;
            try
            {
                var progress = new Progress<double>(p => row.Progress = p);
                var track = await MusicSearchService.DownloadAsync(row.Result, progress, CancellationToken.None);
                row.Downloaded = true;
                MainWindow.Instance?.RefreshAll();
                AudioService.PlayTrack(track, LibraryService.Tracks, "Библиотека");
                MainWindow.Instance?.ShowToast("«" + track.DisplayTitle + "» в библиотеке и уже играет", "ok");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToast("Не удалось скачать «" + row.Title + "»: " + ex.Message, "error");
            }
            finally
            {
                row.Downloading = false;
            }
        }
    }
}
