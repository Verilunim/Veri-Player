using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VeriPlayer.Models;
using VeriPlayer.Services;
using Path = System.Windows.Shapes.Path;
using Track = VeriPlayer.Models.Track;

namespace VeriPlayer.Views
{
    public partial class PlayerView : UserControl
    {
        private readonly List<TrackRow> _rows = new List<TrackRow>();
        private Storyboard _spin;
        private string _context = "Библиотека";
        private bool _seeking;
        private bool _volumeGuard;
        private bool _sortGuard;

        public event Action RequestAddMusic;

        public PlayerView()
        {
            InitializeComponent();

            DiscRotate.CenterX = 25;
            DiscRotate.CenterY = 25;
            BuildSpin();

            InitSort();

            // подписка живёт, пока плеер на экране: после ухода на другую вкладку и возврата
            // её нужно восстановить, иначе полоса перемотки и время останавливались
            Loaded += (s, e) =>
            {
                AudioService.PositionTick -= OnPosition;
                AudioService.PositionTick += OnPosition;
                OnPosition(AudioService.Position);
            };
            Unloaded += (s, e) => AudioService.PositionTick -= OnPosition;

            Loaded += (s, e) =>
            {
                _spin.Begin(this, true);
                if (!AudioService.IsPlaying) _spin.Pause(this);
                SyncNowPlaying();
                SyncPlayState();
                SyncVolume();
                ShuffleButton.IsChecked = AudioService.Shuffle;
                ApplyRepeatVisual();
            };
        }

        private void BuildSpin()
        {
            _spin = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(16));
            Storyboard.SetTarget(_spin, DiscRotate);
            Storyboard.SetTargetProperty(_spin, new PropertyPath(RotateTransform.AngleProperty));
            _spin.Children.Add(animation);
        }

        private void InitSort()
        {
            _sortGuard = true;
            ComboBoxItem target = null;
            foreach (var item in SortBox.Items)
            {
                if (item is ComboBoxItem ci && (ci.Tag as string) == App.Settings.SortMode) { target = ci; break; }
            }
            SortBox.SelectedItem = target ?? SortBox.Items[0];
            _sortGuard = false;
        }

        private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sortGuard) return;
            if (SortBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                App.Settings.SortMode = tag;
                App.SaveSettings();
                MainWindow.Instance?.RefreshLibrary();
            }
        }

        // ================================================================== данные

        public void LoadRows(IList<TrackRow> rows, string title, string subtitle, string context)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);
            _context = string.IsNullOrWhiteSpace(title) ? "Библиотека" : title;

            TrackList.ItemsSource = null;
            TrackList.ItemsSource = _rows;

            HeaderTitle.Text = title;
            HeaderSubtitle.Text = subtitle;

            UpdateEmptyState(context);
            UpdateHero();
            AnimateRowsIn();
        }

        // ================================================================== анимации появления

        private static readonly IEasingFunction EaseOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

        /// <summary>Строки списка по очереди всплывают снизу — «лесенкой», а не все разом.</summary>
        private void AnimateRowsIn()
        {
            if (PerformanceService.ReduceMotion || _rows.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // видимых строк немного — дальше генератор ещё не создал контейнеры
                for (int i = 0; i < Math.Min(_rows.Count, 24); i++)
                {
                    if (TrackList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement item) break;
                    Rise(item, TimeSpan.FromMilliseconds(28 * i), 0, 18);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Проявляет элемент со сдвигом (dx, dy) → 0 через <paramref name="delay"/>.</summary>
        private static void Rise(FrameworkElement element, TimeSpan delay, double dx, double dy, int ms = 460)
        {
            if (element.RenderTransform is not TranslateTransform shift)
                element.RenderTransform = shift = new TranslateTransform();

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * 0.8))
            {
                BeginTime = delay,
                EasingFunction = EaseOut
            };
            // по окончании возвращаем обычные значения, чтобы снятая анимация не оставила элемент невидимым
            fade.Completed += (s, e) => { element.Opacity = 1; shift.X = 0; shift.Y = 0; };
            element.Opacity = 0;
            element.BeginAnimation(OpacityProperty, fade);
            shift.BeginAnimation(TranslateTransform.XProperty, dx == 0 ? null
                : new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = delay, EasingFunction = EaseOut });
            shift.BeginAnimation(TranslateTransform.YProperty, dy == 0 ? null
                : new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = delay, EasingFunction = EaseOut });
            // до старта задержки держим элемент невидимым и на исходной точке
            if (dx != 0) shift.X = dx;
            if (dy != 0) shift.Y = dy;
        }

        /// <summary>Карточки «далее» выезжают справа одна за другой.</summary>
        private void AnimateUpNext()
        {
            if (PerformanceService.ReduceMotion) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < UpNext.Items.Count; i++)
                    if (UpNext.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement card)
                        Rise(card, TimeSpan.FromMilliseconds(70 * i), 60, 0, 520);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Новый трек в главном блоке: обложка проявляется, текст и номер въезжают.</summary>
        private void AnimateHeroChange()
        {
            if (PerformanceService.ReduceMotion) return;
            HeroCover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = EaseOut });
            Rise(HeroText, TimeSpan.Zero, -28, 0, 520);
            Rise(HeroIndex, TimeSpan.FromMilliseconds(60), 0, 22, 520);
        }

        // ================================================================== главный блок

        /// <summary>Трек в главном блоке: играющий, а если ничего не играет — первый в списке.</summary>
        private Track HeroTrack => AudioService.Current ?? _rows.FirstOrDefault(r => !r.Missing)?.Track ?? _rows.FirstOrDefault()?.Track;

        private void UpdateHero()
        {
            var track = HeroTrack;
            Hero.Visibility = track == null ? Visibility.Collapsed : Visibility.Visible;
            if (track == null) { _heroId = null; _upNextKey = null; return; }

            if (_heroId != null && _heroId != track.Id) AnimateHeroChange();
            _heroId = track.Id;

            bool current = AudioService.Current != null && AudioService.Current.Id == track.Id;
            HeroLabel.Text = (current ? (AudioService.IsPlaying ? "СЕЙЧАС ИГРАЕТ" : "НА ПАУЗЕ") : "НАЧНИТЕ С ЭТОГО ТРЕКА");
            HeroTitle.Text = track.DisplayTitle;
            HeroMeta.Text = track.DisplayArtist +
                            (string.IsNullOrWhiteSpace(track.Album) ? "" : " · " + track.Album) +
                            (track.Duration > 0 ? " · " + track.DurationText : "");

            // номер — позиция в очереди (или в списке), как крупное «03» в примере
            int index = current ? AudioService.Index : _rows.FindIndex(r => r.Track == track);
            HeroIndex.Text = (Math.Max(0, index) + 1).ToString("00");

            var image = Storage.LoadImage(track.CoverPath, PerformanceService.HeroCoverPixels);
            HeroCover.Source = image;
            HeroCoverInitial.Text = image == null && track.DisplayTitle.Length > 0
                ? track.DisplayTitle.Substring(0, 1).ToUpperInvariant() : "";
            HeroCoverFill.Fill = image == null ? ThemeService.CoverBrush(track.Id) : (Brush)new SolidColorBrush(Color.FromRgb(0xA8, 0x9C, 0x93));

            if (HeroPlay.Template?.FindName("HeroPlayGlyph", HeroPlay) is Path glyph)
                glyph.Data = (Geometry)FindResource(current && AudioService.IsPlaying ? "IconPause" : "IconPlay");
            if (HeroPlay.Template?.FindName("HeroPlayText", HeroPlay) is TextBlock label)
                label.Text = current && AudioService.IsPlaying ? "ПАУЗА" : "СЛУШАТЬ";

            // следующие три трека после героя
            var source = current && AudioService.Queue.Count > 0 ? AudioService.Queue : _rows.Select(r => r.Track).ToList();
            int start = Math.Max(0, current ? AudioService.Index : source.IndexOf(track));
            var next = source.Skip(start + 1).Concat(source.Take(start)).Where(t => t.Id != track.Id).Take(3).ToList();
            // перерисовываем и анимируем, только если очередь правда сменилась (а не пауза/обновление)
            var key = string.Join("|", next.Select(t => t.Id));
            UpNext.ItemsSource = next;
            if (key == _upNextKey) return;
            _upNextKey = key;
            AnimateUpNext();
        }

        private string _heroId;
        private string _upNextKey;

        private void HeroPlay_Click(object sender, RoutedEventArgs e)
        {
            var track = HeroTrack;
            if (track == null) return;
            if (AudioService.Current != null && AudioService.Current.Id == track.Id) AudioService.TogglePlay();
            else Play(_rows.FirstOrDefault(r => r.Track == track));
        }

        private void UpNext_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not Track track) return;
            var row = _rows.FirstOrDefault(r => r.Track.Id == track.Id);
            if (row != null) Play(row);
            else AudioService.PlayTrack(track);
        }

        private void UpdateEmptyState(string context)
        {
            if (_rows.Count > 0)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                TrackList.Visibility = Visibility.Visible;
                return;
            }

            TrackList.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyButton.Visibility = Visibility.Visible;

            if (LibraryService.Tracks.Count == 0)
            {
                EmptyIcon.Data = (Geometry)FindResource("IconNote");
                EmptyTitle.Text = "Ваша библиотека пуста";
                EmptyText.Text = "Перетащите MP3-файлы прямо в окно или добавьте папку с музыкой — Veri Player соберёт библиотеку сам.";
                EmptyButton.Content = "Добавить музыку";
                return;
            }

            switch (context)
            {
                case "favorites":
                    EmptyIcon.Data = (Geometry)FindResource("IconHeart");
                    EmptyTitle.Text = "В избранном пусто";
                    EmptyText.Text = "Отмечайте треки сердечком — они появятся здесь.";
                    break;
                case "recent":
                    EmptyIcon.Data = (Geometry)FindResource("IconClock");
                    EmptyTitle.Text = "История пуста";
                    EmptyText.Text = "Здесь появятся треки, которые вы недавно слушали.";
                    break;
                case "playlist":
                    EmptyIcon.Data = (Geometry)FindResource("IconWave");
                    EmptyTitle.Text = "В плейлисте пусто";
                    EmptyText.Text = "Добавьте треки в плейлист через правую кнопку мыши в библиотеке.";
                    break;
                default:
                    EmptyIcon.Data = (Geometry)FindResource("IconSearch");
                    EmptyTitle.Text = "Ничего не найдено";
                    EmptyText.Text = "Попробуйте изменить запрос или выбрать другой раздел.";
                    break;
            }
            EmptyButton.Visibility = Visibility.Collapsed;
        }

        public void SyncRows()
        {
            var currentId = AudioService.Current?.Id;
            foreach (var row in _rows)
            {
                row.IsFavorite = UserDataService.IsFavorite(row.Id);
                row.IsCurrent = row.Id == currentId;
            }
        }

        // ================================================================== сейчас играет

        public void SyncNowPlaying()
        {
            var track = AudioService.Current;

            if (track == null)
            {
                NowTitle.Text = "Ничего не играет";
                NowArtist.Text = "Добавьте музыку, чтобы начать";
                NowCoverImage.Source = null;
                NowCoverInitial.Visibility = Visibility.Visible;
                NowCoverInitial.Text = "♪";
                NowCover.Background = (Brush)FindResource("AccentDeepBrush");
                NowFavorite.IsChecked = false;
                SeekTotal.Text = "0:00";
                SeekCurrent.Text = "0:00";
                SeekSlider.Value = 0;
                TrackActions.Visibility = Visibility.Collapsed;
                SyncRows();
                UpdateHero();
                return;
            }

            NowTitle.Text = track.DisplayTitle;
            NowArtist.Text = track.DisplayArtist +
                             (string.IsNullOrWhiteSpace(track.Album) ? "" : " · " + track.Album);

            var image = Storage.LoadImage(track.CoverPath, 180);
            NowCoverImage.Source = image;
            if (image != null)
            {
                NowCoverInitial.Visibility = Visibility.Collapsed;
                NowCover.Background = Brushes.Transparent;
            }
            else
            {
                NowCoverInitial.Visibility = Visibility.Visible;
                NowCoverInitial.Text = track.DisplayTitle.Length > 0
                    ? track.DisplayTitle.Substring(0, 1).ToUpperInvariant()
                    : "♪";
                NowCover.Background = ThemeService.CoverBrush(track.Id);
            }

            NowFavorite.IsChecked = UserDataService.IsFavorite(track.Id);
            SeekTotal.Text = FormatTime(AudioService.Duration);

            TrackActions.Visibility = Visibility.Visible;
            ClipDot.Visibility = track.HasVideo ? Visibility.Visible : Visibility.Collapsed;
            BtnClip.ToolTip = track.HasVideo
                ? "Клип добавлен — нажмите, чтобы заменить или убрать"
                : "Клип на фон песни (MP4, до 5 секунд)";

            SyncRows();
            UpdateHero();
        }

        public void SyncPlayState()
        {
            bool playing = AudioService.IsPlaying;

            if (PlayButton.Template?.FindName("Glyph", PlayButton) is Path glyph)
                glyph.Data = (Geometry)FindResource(playing ? "IconPause" : "IconPlay");
            UpdateHero();

            if (_spin == null) return;
            if (playing && !PerformanceService.ReduceMotion) _spin.Resume(this);
            else _spin.Pause(this);
        }

        public void SyncVolume()
        {
            _volumeGuard = true;
            VolumeSlider.Value = AudioService.Volume * 100.0;
            _volumeGuard = false;

            bool muted = AudioService.Volume <= 0.001;
            MuteButton.IsChecked = muted;
            VolumeGlyph.Data = (Geometry)FindResource(muted ? "IconMute" : "IconVolume");
        }

        private void OnPosition(double seconds)
        {
            if (_seeking) return;
            var total = AudioService.Duration;
            SeekCurrent.Text = FormatTime(seconds);
            SeekTotal.Text = FormatTime(total);
            SeekSlider.Value = total > 0.4 ? Math.Max(0, Math.Min(100, seconds / total * 100)) : 0;
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
                : string.Format("{0}:{1:00}", span.Minutes, span.Seconds);
        }

        // ================================================================== управление

        private void Play(TrackRow row)
        {
            if (row == null) return;
            if (row.Missing)
            {
                MainWindow.Instance?.ShowToast("Файл не найден: " + row.Title, "error");
                return;
            }
            var queue = _rows.Where(r => !r.Missing).Select(r => r.Track).ToList();
            AudioService.PlayTrack(row.Track, queue, _context);
        }

        private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TrackRow row)
                Play(row);
        }

        private void RowFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TrackRow row)
            {
                bool now = UserDataService.ToggleFavorite(row.Id);
                row.IsFavorite = now;
                if (AudioService.Current != null && AudioService.Current.Id == row.Id)
                    NowFavorite.IsChecked = now;
            }
            e.Handled = true;
        }

        private void Row_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not TrackRow row) return;

            var menu = new ContextMenu { PlacementTarget = element, Placement = PlacementMode.MousePoint };

            menu.Items.Add(Item("Играть", () => Play(row)));
            menu.Items.Add(Item("Играть следующим", () => PlayNext(row.Track)));

            menu.Items.Add(new Separator());
            menu.Items.Add(Item(UserDataService.IsFavorite(row.Id) ? "Убрать из избранного" : "В избранное",
                () =>
                {
                    bool now = UserDataService.ToggleFavorite(row.Id);
                    row.IsFavorite = now;
                    if (AudioService.Current != null && AudioService.Current.Id == row.Id)
                        NowFavorite.IsChecked = now;
                }));

            var addTo = new MenuItem { Header = "Добавить в плейлист" };
            var playlists = UserDataService.Current.Playlists;
            if (playlists.Count == 0)
            {
                addTo.Items.Add(new MenuItem { Header = "Плейлистов пока нет", IsEnabled = false });
            }
            else
            {
                foreach (var playlist in playlists)
                {
                    var target = playlist;
                    addTo.Items.Add(Item(target.Name, () =>
                    {
                        UserDataService.AddToPlaylist(target, new[] { row.Id });
                        MainWindow.Instance?.ShowToast("Добавлено в «" + target.Name + "»", "ok");
                    }));
                }
            }
            addTo.Items.Add(new Separator());
            addTo.Items.Add(Item("Новый плейлист…", () =>
            {
                var panel = new VeriPlayer.Views.Panels.PromptPanel("Новый плейлист", "Название", "", "Создать");
                panel.Submitted += value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    var created = UserDataService.CreatePlaylist(value);
                    UserDataService.AddToPlaylist(created, new[] { row.Id });
                    MainWindow.Instance?.ShowToast("Добавлено в «" + created.Name + "»", "ok");
                };
                MainWindow.Instance?.ShowPanel(panel);
            }));
            menu.Items.Add(addTo);

            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Поделиться треком…", () =>
            {
                MainWindow.Instance?.ShowPanel(new VeriPlayer.Views.Panels.ShareTrackPanel(row.Track));
            }));
            menu.Items.Add(Item(Pro("Изменить информацию…"), () =>
            {
                if (MainWindow.Instance?.RequirePro("Редактирование песен") == true)
                    MainWindow.Instance.ShowPanel(new VeriPlayer.Views.Panels.TrackEditPanel(row.Track));
            }));

            var track = row.Track;
            menu.Items.Add(Item(Pro(track.HasCover ? "Заменить обложку…" : "Добавить обложку…"),
                () => MainWindow.Instance?.PickCover(track)));
            if (track.HasCover)
            {
                menu.Items.Add(Item("Обложка на весь экран", () => MainWindow.Instance?.ShowCoverFullscreen(track)));
                menu.Items.Add(Item("Убрать обложку", () => MainWindow.Instance?.ClearCover(track)));
            }
            menu.Items.Add(Item(Pro(track.HasVideo ? "Заменить клип на фон…" : "Прикрепить клип на фон (MP4, до 5 с)…"),
                () => MainWindow.Instance?.PickClip(track)));
            if (track.HasVideo)
                menu.Items.Add(Item("Убрать клип", () => MainWindow.Instance?.ClearClip(track)));

            if (row.Track.Exists && !row.Track.IsRemote)
                menu.Items.Add(Item("Показать в проводнике", () => RevealInExplorer(row.Track.FilePath)));

            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Убрать из библиотеки", () =>
            {
                MainWindow.Instance?.Confirm("Убрать «" + row.Title + "» из библиотеки?", () =>
                {
                    LibraryService.Remove(row.Track, false);
                    MainWindow.Instance?.RefreshAll();
                    MainWindow.Instance?.ShowToast("Трек убран из библиотеки", "info");
                }, "Убрать");
            }));
            menu.Items.Add(Item("Удалить файл с диска", () =>
            {
                MainWindow.Instance?.Confirm("Удалить файл «" + row.Title + "» навсегда?", () =>
                {
                    LibraryService.Remove(row.Track, true);
                    MainWindow.Instance?.RefreshAll();
                    MainWindow.Instance?.ShowToast("Файл удалён", "info");
                });
            }));

            menu.IsOpen = true;
            e.Handled = true;
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void PlayNext(Track track)
        {
            if (track == null || AudioService.Queue.Count == 0)
            {
                Play(_rows.FirstOrDefault(r => r.Track == track));
                return;
            }
            int index = AudioService.Index;
            if (index < 0) index = 0;
            if (!AudioService.Queue.Contains(track)) AudioService.Queue.Insert(index + 1, track);
            MainWindow.Instance?.ShowToast("Играет следующим: " + track.DisplayTitle, "info");
        }

        /// <summary>Подпись пункта меню: без Pro+ помечаем, что функция платная.</summary>
        private static string Pro(string header) => LicenseService.IsProCurrent ? header : header + "   🔒 Pro+";

        /// <summary>Замки на кнопках обложки и клипа.</summary>
        /// <summary>Спектр (настройка «Показывать спектр» и облегчённый режим) и вращение пластинки.</summary>
        public void ApplyPerformance()
        {
            Spectrum.Visibility = PerformanceService.Visualizer ? Visibility.Visible : Visibility.Hidden;
            if (_spin == null) return;
            if (AudioService.IsPlaying && !PerformanceService.ReduceMotion) _spin.Resume(this);
            else _spin.Pause(this);
        }

        public void RefreshProLocks()
        {
            var visibility = LicenseService.IsProCurrent ? Visibility.Collapsed : Visibility.Visible;
            LockCover.Visibility = visibility;
            LockClip.Visibility = visibility;
        }

        private static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        private void Add_Click(object sender, RoutedEventArgs e) => RequestAddMusic?.Invoke();

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!AudioService.HasTrack && _rows.Count > 0)
            {
                Play(_rows.FirstOrDefault(r => !r.Missing) ?? _rows[0]);
                return;
            }
            AudioService.TogglePlay();
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => AudioService.Previous();
        private void Next_Click(object sender, RoutedEventArgs e) => AudioService.Next();

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Shuffle = ShuffleButton.IsChecked == true;
            App.Settings.Shuffle = AudioService.Shuffle;
            App.SaveSettings();
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            // off → all → one → off
            switch (AudioService.Repeat)
            {
                case "all": AudioService.Repeat = "one"; break;
                case "one": AudioService.Repeat = "off"; break;
                default: AudioService.Repeat = "all"; break;
            }
            App.Settings.Repeat = AudioService.Repeat;
            App.SaveSettings();
            ApplyRepeatVisual();
        }

        private void ApplyRepeatVisual()
        {
            RepeatOne.Visibility = AudioService.Repeat == "one" ? Visibility.Visible : Visibility.Collapsed;
            RepeatToggle.IsChecked = AudioService.Repeat != "off";
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            bool muted = MuteButton.IsChecked == true;
            if (muted) _lastVolume = AudioService.Volume <= 0.001 ? 0.6 : AudioService.Volume;
            AudioService.Volume = muted ? 0 : _lastVolume;
            App.Settings.Volume = AudioService.Volume;
            App.Settings.Muted = muted;
            SyncVolume();
        }

        private double _lastVolume = 0.8;

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_volumeGuard) return;
            AudioService.Volume = e.NewValue / 100.0;
            App.Settings.Volume = AudioService.Volume;
            App.Settings.Muted = AudioService.Volume <= 0.001;
            bool muted = AudioService.Volume <= 0.001;
            MuteButton.IsChecked = muted;
            VolumeGlyph.Data = (Geometry)FindResource(muted ? "IconMute" : "IconVolume");
        }

        private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _seeking = true;

        private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _seeking = false;
            AudioService.SeekPercent(SeekSlider.Value / 100.0);
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_seeking) return;
            SeekCurrent.Text = FormatTime(AudioService.Duration * e.NewValue / 100.0);
        }

        private void NowFavorite_Click(object sender, RoutedEventArgs e)
        {
            var track = AudioService.Current;
            if (track == null) return;
            bool now = UserDataService.ToggleFavorite(track.Id);
            NowFavorite.IsChecked = now;
            SyncRows();
        }

        // ================================================================== обложка и клип

        private void BtnCover_Click(object sender, RoutedEventArgs e)
        {
            var track = AudioService.Current;
            if (track == null) { MainWindow.Instance?.ShowToast("Сначала включите трек.", "info"); return; }
            MainWindow.Instance?.PickCover(track);
        }

        private void BtnExpand_Click(object sender, RoutedEventArgs e)
        {
            var track = AudioService.Current;
            if (track == null) { MainWindow.Instance?.ShowToast("Сначала включите трек.", "info"); return; }
            MainWindow.Instance?.ShowCoverFullscreen(track);
        }

        private void BtnClip_Click(object sender, RoutedEventArgs e)
        {
            var track = AudioService.Current;
            if (track == null) { MainWindow.Instance?.ShowToast("Сначала включите трек.", "info"); return; }

            if (!track.HasVideo)
            {
                MainWindow.Instance?.PickClip(track);
                return;
            }

            var menu = new ContextMenu { PlacementTarget = BtnClip, Placement = PlacementMode.Top };
            menu.Items.Add(Item("Заменить клип…", () => MainWindow.Instance?.PickClip(track)));
            menu.Items.Add(Item("Убрать клип", () => MainWindow.Instance?.ClearClip(track)));
            menu.IsOpen = true;
        }

        /// <summary>Вызывается из главного окна при смене трека/состояния.</summary>
        public void RefreshTransportVisuals()
        {
            SyncPlayState();
            ApplyRepeatVisual();
            ShuffleButton.IsChecked = AudioService.Shuffle;
        }
    }
}
