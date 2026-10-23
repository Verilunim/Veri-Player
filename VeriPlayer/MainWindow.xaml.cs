using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using VeriPlayer.Controls;
using VeriPlayer.Models;
using VeriPlayer.Services;
using VeriPlayer.Views;
using VeriPlayer.Views.Panels;
using Path = System.Windows.Shapes.Path;
using Track = VeriPlayer.Models.Track;

namespace VeriPlayer
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private readonly List<NavItem> _nav = new List<NavItem>();
        private readonly DispatcherTimer _searchTimer = new DispatcherTimer();
        private PlayerView _player;
        private AdminView _admin;
        private AddByLinkPanel _addLink;
        private GenerateMusicPanel _generateMusic;
        private string _view = "library";
        private Playlist _currentPlaylist;
        private Border _dropHint;
        private bool _maximized;
        private bool _adminOpen => _admin != null && ReferenceEquals(ContentHost.Content, _admin);

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            BuildNav();
            WireServices();

            Auth.Authenticated += user => EnterApp();

            _searchTimer.Interval = TimeSpan.FromMilliseconds(220);
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); RefreshLibrary(); };

            PlaylistList.PreviewMouseRightButtonUp += PlaylistList_PreviewMouseRightButtonUp;

            AllowDrop = true;
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
            PreviewKeyDown += OnKeyDown;

            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            
            TrayService.RestoreRequested += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    TrayService.Hide();
                });
            };
        }

        // ================================================================== запуск

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AnimateBlobs();
            BuildDropHint();
            ApplyAppearance();

            if (AuthService.Current != null) EnterApp();
            else ShowAuthScreen();
        }

        private void ShowAuthScreen()
        {
            Shell.Visibility = Visibility.Collapsed;
            Auth.Visibility = Visibility.Visible;
            Auth.Prepare();
            AnimateView(Auth);
        }

        private void EnterApp()
        {
            Auth.Visibility = Visibility.Collapsed;
            Shell.Visibility = Visibility.Visible;
            AnimateView(Shell);

            if (_player == null)
            {
                _player = new PlayerView();
                _player.RequestAddMusic += () => AddMusicMenu(BtnAddMusicSide);
            }

            _view = "library";
            _currentPlaylist = null;
            ContentHost.Content = _player;
            NavList.SelectedIndex = 0;
            PageTitle.Text = "Библиотека";

            UserName.Text = AuthService.Current.Nick;
            UserRole.Text = AuthService.Current.RoleLabel;
            UserAvatar.Source = AuthService.Current.AvatarPath;
            UserAvatar.Initial = AuthService.Current.Initial;
            BtnAdmin.Visibility = AuthService.Current.IsStaff ? Visibility.Visible : Visibility.Collapsed;

            RefreshAll();
            UpdateBackgroundVideo();
            ShowToast("Добро пожаловать, " + AuthService.Current.Nick + "!", "ok");
        }

        private void WireServices()
        {
            AuthService.UsersChanged += () => { if (_admin != null) _admin.Refresh(); };
            LibraryService.Changed += RefreshAll;
            UserDataService.Changed += () =>
            {
                if (_player != null) _player.SyncRows();
                RefreshCounts();
                RefreshPlaylists();
            };
            BackgroundService.Changed += ApplyBackground;
            AudioService.TrackChanged += track =>
            {
                if (track != null) App.Settings.LastTrackId = track.Id;
                if (_player != null) _player.SyncNowPlaying();
                UpdateBackgroundVideo();
                
                // Discord Rich Presence
                DiscordService.UpdatePresence(track, AudioService.IsPlaying);
                TrayService.UpdateTooltip(track, AudioService.IsPlaying, AudioService.Position, AudioService.Duration);
            };
            AudioService.StateChanged += playing =>
            {
                if (_player != null) _player.SyncPlayState();
                UpdateBackgroundVideo();
                
                // Discord Rich Presence
                DiscordService.UpdatePresence(AudioService.Current, playing);
                TrayService.UpdateTooltip(AudioService.Current, playing, AudioService.Position, AudioService.Duration);
            };
            AudioService.Failed += message => Dispatcher.BeginInvoke(new Action(() =>
            {
                StopVideo();
                ShowToast(message, "error");
            }));
        }

        private bool _isRealClose = false;

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Diagnostics.Log("MainWindow_Closing: _isRealClose=" + _isRealClose);
            
            // Если это не настоящее закрытие, сворачиваем в трей
            if (!_isRealClose)
            {
                e.Cancel = true;
                Diagnostics.Log("MainWindow_Closing: cancelling close, showing tray BEFORE hiding window");
                
                // ВАЖНО: показываем иконку трея ДО скрытия окна
                TrayService.Show();
                
                // Показываем уведомление только в первый раз
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    TrayService.ShowBalloon("Veri Player", 
                        "Приложение свёрнуто в трей. Музыка продолжает играть.\nДвойной клик на иконке — вернуть окно.");
                }
                
                // Теперь скрываем окно
                Hide();
                Diagnostics.Log("MainWindow_Closing: window hidden");
                return;
            }

            // Настоящее закрытие
            Diagnostics.Log("MainWindow_Closing: real close, shutting down");
            try
            {
                App.Settings.Volume = AudioService.Volume;
                App.Settings.Shuffle = AudioService.Shuffle;
                App.Settings.Repeat = AudioService.Repeat;
                App.SaveSettings();
                AudioService.Shutdown();
                DiscordService.Shutdown();
                TrayService.Shutdown();
            }
            catch { }
        }

        private bool _trayHintShown = false;

        public void RealClose()
        {
            _isRealClose = true;
            Close();
        }

        // ================================================================== оформление

        private void AnimateBlobs()
        {
            if (App.Settings.ReduceMotion) return;
            Drift(Blob1, 0, 150, 0, 120, 34);
            Drift(Blob2, 0, -170, 0, 140, 41);
            Drift(Blob3, 0, 130, 0, -150, 47);
        }

        private static void Drift(TranslateTransform transform, double x1, double x2, double y1, double y2, double seconds)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x1, x2, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y1, y2, TimeSpan.FromSeconds(seconds * 1.27))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            });
        }

        public void ApplyAppearance()
        {
            ThemeService.Apply(App.Settings);
            
            Diagnostics.Log($"ApplyAppearance: Effect={App.Settings.Effect}, Intensity={App.Settings.EffectIntensity}");
            
            var kind = Effects.Parse(App.Settings.Effect);
            Diagnostics.Log($"ApplyAppearance: Parsed Kind={kind}");
            
            Particles.Kind = kind;
            Particles.Intensity = App.Settings.EffectIntensity;
            
            Diagnostics.Log($"ApplyAppearance: Set Particles.Kind={Particles.Kind}, Particles.Intensity={Particles.Intensity}");
            
            ApplyBackground();
            AuroraLayer.Opacity = App.Settings.ReduceMotion ? 0.32 : 0.5;
        }

        public void ApplyBackground()
        {
            var background = BackgroundService.Active;

            if (background == null)
            {
                BackgroundImage.Source = null;
                BackgroundImage.Effect = null;
                BackgroundImage.Visibility = Visibility.Collapsed;
            }
            else
            {
                double blur = App.Settings.BackgroundBlur;
                int decode = blur > 2 ? 1100 : 2200;
                var image = Storage.LoadImage(background.FilePath, decode);
                BackgroundImage.Source = image;
                BackgroundImage.Visibility = image == null ? Visibility.Collapsed : Visibility.Visible;
                BackgroundImage.Effect = blur > 1
                    ? new BlurEffect { Radius = Math.Min(16, blur * 0.28), KernelType = KernelType.Gaussian }
                    : null;
            }

            BackgroundVeil.Opacity = App.Settings.BackgroundDim;
        }

        // ================================================================== навигация

        private void BuildNav()
        {
            _nav.Clear();
            _nav.Add(new NavItem { Tag = "library", Title = "Библиотека", Icon = (Geometry)FindResource("IconNote") });
            _nav.Add(new NavItem { Tag = "favorites", Title = "Избранное", Icon = (Geometry)FindResource("IconHeart") });
            _nav.Add(new NavItem { Tag = "recent", Title = "Недавние", Icon = (Geometry)FindResource("IconClock") });
            _nav.Add(new NavItem { Tag = "generate", Title = "Создать песню", Icon = (Geometry)FindResource("IconWand") });
            _nav.Add(new NavItem { Tag = "addlink", Title = "Добавить по ссылке", Icon = (Geometry)FindResource("IconLink") });
            NavList.ItemsSource = _nav;
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is not NavItem item) return;
            _view = item.Tag;
            _currentPlaylist = null;
            PlaylistList.SelectedItem = null;

            if (item.Tag == "addlink")
            {
                ShowAddLink();
                return;
            }

            if (item.Tag == "generate")
            {
                ShowGenerate();
                return;
            }

            ShowPlayer();
            RefreshLibrary();
            AnimateView(_player);
        }

        private void ShowAddLink()
        {
            if (_addLink == null)
            {
                _addLink = new AddByLinkPanel();
                _addLink.CloseRequested += (s, e) => SelectView("library");
            }

            ContentHost.Content = _addLink;
            PageTitle.Text = "Добавить по ссылке";
            RefreshCounts();
            AnimateView(_addLink);
        }

        private void ShowGenerate()
        {
            if (_generateMusic == null)
                _generateMusic = new GenerateMusicPanel();

            ContentHost.Content = _generateMusic;
            PageTitle.Text = "Создать песню с AI";
            RefreshCounts();
            AnimateView(_generateMusic);
        }

        private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistList.SelectedItem is not Playlist playlist) return;
            _currentPlaylist = playlist;
            _view = "playlist";
            NavList.SelectedItem = null;
            ShowPlayer();
            RefreshLibrary();
            AnimateView(_player);
        }

        private void ShowPlayer()
        {
            if (_player != null && !ReferenceEquals(ContentHost.Content, _player))
                ContentHost.Content = _player;
        }

        private void BtnNewPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var panel = new PromptPanel("Новый плейлист", "Название", "", "Создать");
            panel.Submitted += value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                UserDataService.CreatePlaylist(value);
                RefreshPlaylists();
                ShowToast("Плейлист «" + value.Trim() + "» создан", "ok");
            };
            ShowPanel(panel);
        }

        private void RefreshPlaylists()
        {
            PlaylistList.ItemsSource = null;
            PlaylistList.ItemsSource = UserDataService.Current.Playlists;
        }

        private void PlaylistList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var playlist = FindDataContext<Playlist>(e.OriginalSource);
            if (playlist == null) return;

            var menu = new ContextMenu
            {
                PlacementTarget = PlaylistList,
                Placement = PlacementMode.MousePoint
            };
            menu.Items.Add(MakeItem("Играть", () =>
            {
                var tracks = UserDataService.Resolve(playlist.TrackIds);
                if (tracks.Count == 0) { ShowToast("В плейлисте пока нет треков", "warn"); return; }
                NavList.SelectedItem = null;
                _currentPlaylist = playlist;
                _view = "playlist";
                ShowPlayer();
                RefreshLibrary();
                AudioService.PlayTrack(tracks[0], tracks, playlist.Name);
            }));
            menu.Items.Add(MakeItem("Переименовать", () =>
            {
                var panel = new PromptPanel("Переименовать", "Название", playlist.Name, "Сохранить");
                panel.Submitted += value =>
                {
                    UserDataService.RenamePlaylist(playlist, value);
                    RefreshPlaylists();
                };
                ShowPanel(panel);
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Удалить", () =>
            {
                Confirm("Удалить плейлист «" + playlist.Name + "»?", () =>
                {
                    UserDataService.DeletePlaylist(playlist);
                    if (ReferenceEquals(_currentPlaylist, playlist))
                    {
                        _currentPlaylist = null;
                        _view = "library";
                        NavList.SelectedIndex = 0;
                    }
                    RefreshPlaylists();
                    RefreshLibrary();
                    ShowToast("Плейлист удалён", "info");
                });
            }));
            menu.IsOpen = true;
            e.Handled = true;
        }

        // ================================================================== список

        public void RefreshAll()
        {
            RefreshCounts();
            RefreshPlaylists();
            RefreshLibrary();
            if (_admin != null) _admin.Refresh();
        }

        /// <summary>Переключить раздел по тегу (library/favorites/recent). Нужно для снимков экрана.</summary>
        public void SelectView(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            foreach (var item in _nav)
            {
                if (string.Equals(item.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    NavList.SelectedItem = item;
                    return;
                }
            }
        }

        /// <summary>Открыть или закрыть панель владельца. Нужно для снимков экрана.</summary>
        public bool AdminOpen => _admin != null && ReferenceEquals(ContentHost.Content, _admin);

        private void RefreshCounts()
        {
            var data = UserDataService.Current;
            if (_nav.Count >= 3)
            {
                _nav[0].Count = LibraryService.Tracks.Count;
                _nav[1].Count = data.Favorites.Count;
                _nav[2].Count = data.Recent.Count;
            }
        }

        public void RefreshLibrary()
        {
            if (_player == null) return;
            if (_view == "addlink" || _view == "generate") return;

            var search = SearchInput.Text;
            var data = UserDataService.Current;
            var sort = App.Settings.SortMode;
            List<Track> tracks;
            string title;
            string subtitle;

            switch (_view)
            {
                case "favorites":
                    tracks = LibraryService.Query(search, sort, data.Favorites);
                    title = "Избранное";
                    subtitle = Plural(tracks.Count, "трек", "трека", "треков");
                    break;

                case "recent":
                    tracks = UserDataService.Resolve(data.Recent);
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var needle = search.Trim().ToLowerInvariant();
                        tracks = tracks.Where(t => t.SearchBlob.Contains(needle)).ToList();
                    }
                    title = "Недавние";
                    subtitle = "последние прослушивания";
                    break;

                case "playlist":
                    if (_currentPlaylist == null) { _view = "library"; RefreshLibrary(); return; }
                    tracks = UserDataService.Resolve(_currentPlaylist.TrackIds);
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var needle = search.Trim().ToLowerInvariant();
                        tracks = tracks.Where(t => t.SearchBlob.Contains(needle)).ToList();
                    }
                    title = _currentPlaylist.Name;
                    subtitle = Plural(tracks.Count, "трек", "трека", "треков");
                    break;

                default:
                    tracks = LibraryService.Query(search, sort);
                    title = "Библиотека";
                    subtitle = LibraryService.Tracks.Count == 0
                        ? "пока пусто"
                        : tracks.Count + " из " + LibraryService.Tracks.Count + " · " + Storage.Human(LibraryService.TotalSize());
                    break;
            }

            var currentId = AudioService.Current?.Id;
            var favorites = new HashSet<string>(data.Favorites);

            var rows = new List<TrackRow>(tracks.Count);
            for (int i = 0; i < tracks.Count; i++)
            {
                rows.Add(new TrackRow(tracks[i], i)
                {
                    IsCurrent = tracks[i].Id == currentId,
                    IsFavorite = favorites.Contains(tracks[i].Id)
                });
            }

            if (!_adminOpen) PageTitle.Text = title;
            _player.LoadRows(rows, title, subtitle, _view);
        }

        public static string Plural(int count, string one, string few, string many)
        {
            int mod100 = count % 100;
            int mod10 = count % 10;
            if (mod100 >= 11 && mod100 <= 14) return count + " " + many;
            if (mod10 == 1) return count + " " + one;
            if (mod10 >= 2 && mod10 <= 4) return count + " " + few;
            return count + " " + many;
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        // ================================================================== импорт

        private void BtnAddMusic_Click(object sender, RoutedEventArgs e) => AddMusicMenu((FrameworkElement)sender);

        private void AddMusicMenu(FrameworkElement target)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MakeItem("Выбрать файлы…", PickFiles));
            menu.Items.Add(MakeItem("Выбрать папку…", PickFolder));
            menu.Items.Add(MakeItem("Импорт списка .m3u…", ImportM3u));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Добавить по ссылке…", () => ShowPanel(new AddByLinkPanel())));
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void PickFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите музыку",
                Multiselect = true,
                Filter = "Аудио|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg;*.opus;*.aiff;*.aif|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            ImportPaths(dialog.FileNames);
        }

        private void PickFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Выберите папку с музыкой",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            ImportPaths(dialog.FolderNames);
        }

        public void ImportM3u()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Плейлист",
                Filter = "Плейлисты|*.m3u;*.m3u8;*.txt|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            var files = new List<string>();
            string baseDir = System.IO.Path.GetDirectoryName(dialog.FileName);
            try
            {
                foreach (var raw in File.ReadAllLines(dialog.FileName, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var candidate = System.IO.Path.IsPathRooted(line) ? line : System.IO.Path.Combine(baseDir, line);
                    if (File.Exists(candidate) && LibraryService.IsSupported(candidate)) files.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Не удалось прочитать плейлист: " + ex.Message, "error");
                return;
            }

            if (files.Count == 0)
            {
                ShowToast("В списке не найдено локальных файлов. Добавьте музыку файлами или папкой.", "warn");
                return;
            }
            ImportPaths(files);
        }

        public async void ImportPaths(IEnumerable<string> paths)
        {
            var list = LibraryService.ExpandPaths(paths);
            if (list.Count == 0)
            {
                ShowToast("Аудиофайлы не найдены.", "warn");
                return;
            }

            var title = new TextBlock { Text = "Импорт музыки", Style = (Style)FindResource("TextH3") };
            var status = new TextBlock
            {
                Style = (Style)FindResource("TextMuted"),
                Margin = new Thickness(0, 8, 0, 14),
                Text = "Подготовка…"
            };
            var bar = new ProgressBar { Height = 8, Width = 420 };
            var percent = new TextBlock
            {
                Style = (Style)FindResource("TextCaption"),
                Margin = new Thickness(0, 10, 0, 0)
            };

            var body = new StackPanel { Margin = new Thickness(26, 4, 26, 26), Width = 460 };
            body.Children.Add(title);
            body.Children.Add(status);
            body.Children.Add(bar);
            body.Children.Add(percent);

            ShowModal(body, "Импорт", false);

            var progress = new Progress<(int done, int total, string current)>(p =>
            {
                bar.Value = p.total == 0 ? 0 : p.done * 100.0 / p.total;
                status.Text = p.current;
                percent.Text = p.done + " / " + p.total;
            });

            var result = await LibraryService.ImportAsync(list, progress);
            CloseModal();
            RefreshAll();

            var message = "Добавлено: " + result.Added;
            if (result.Skipped > 0) message += " · уже было: " + result.Skipped;
            if (result.Failed > 0) message += " · ошибок: " + result.Failed;
            ShowToast(message, result.Added > 0 ? "ok" : "warn");
        }

        // ================================================================== drag&drop

        private void BuildDropHint()
        {
            _dropHint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD0, 6, 6, 12)),
                BorderBrush = (Brush)FindResource("AccentBorderBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(22),
                Margin = new Thickness(28),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var icon = new Path
            {
                Data = (Geometry)FindResource("IconNote"),
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2,
                Width = 64,
                Height = 64,
                Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            var text = new TextBlock
            {
                Text = "Отпустите, чтобы добавить музыку",
                Foreground = (Brush)FindResource("TextBrush"),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            };
            stack.Children.Add(icon);
            stack.Children.Add(text);
            _dropHint.Child = stack;
            Panel.SetZIndex(_dropHint, 500);
            Root.Children.Add(_dropHint);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) && Auth.Visibility == Visibility.Collapsed;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (_dropHint != null)
                _dropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (_dropHint != null) _dropHint.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (Auth.Visibility != Visibility.Collapsed) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;
            ImportPaths(paths);
            e.Handled = true;
        }

        // ================================================================== окно

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, TitleBar) && !ReferenceEquals(e.OriginalSource, PageTitle)) return;

            if (e.ClickCount == 2) { ToggleMaximize(); return; }
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            _maximized = WindowState == WindowState.Maximized;
            Root.Margin = _maximized ? new Thickness(7) : new Thickness(0);

            if (BtnMaximize.Content is Path glyph)
                glyph.Data = (Geometry)FindResource(_maximized ? "IconRestore" : "IconMaximize");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ================================================================== модальные окна

        public void ShowModal(UIElement content, string title, bool closable = true)
        {
            OverlayTitle.Text = title;
            OverlayBody.Content = content;
            OverlayHost.Visibility = Visibility.Visible;
            OverlayClose.Visibility = closable ? Visibility.Visible : Visibility.Collapsed;
            OverlayDim.IsHitTestVisible = closable;

            OverlayCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.22)));
            var scale = new DoubleAnimation(0.96, 1, TimeSpan.FromSeconds(0.26))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        public void ShowPanel(IModalPanel panel)
        {
            if (panel == null) return;
            panel.CloseRequested += (s, e) => CloseModal();
            ShowModal((UIElement)panel, panel.Title);
        }

        public void CloseModal()
        {
            // плавно гасим панель, и только потом убираем — без резкого исчезновения
            var fade = new DoubleAnimation(OverlayCard.Opacity, 0, TimeSpan.FromSeconds(0.16))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (s, e) =>
            {
                OverlayHost.Visibility = Visibility.Collapsed;
                OverlayBody.Content = null;
                OverlayCard.BeginAnimation(OpacityProperty, null);
                OverlayCard.Opacity = 1;
                OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            };
            OverlayCard.BeginAnimation(OpacityProperty, fade);

            var shrink = new DoubleAnimation(1, 0.965, TimeSpan.FromSeconds(0.16))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }

        /// <summary>Плавное появление контента при переключении вкладок и разделов.</summary>
        private void AnimateView(FrameworkElement view)
        {
            if (view == null) return;
            if (App.Settings.ReduceMotion) { view.Opacity = 1; return; }

            view.Opacity = 0;
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void OverlayDim_MouseDown(object sender, MouseButtonEventArgs e) => CloseModal();

        private void OverlayClose_Click(object sender, RoutedEventArgs e) => CloseModal();

        public void Confirm(string question, Action onConfirm, string confirmText = "Удалить")
        {
            var panel = new ConfirmPanel(question, confirmText);
            panel.Confirmed += () => { CloseModal(); onConfirm(); };
            ShowPanel(panel);
        }

        private static MenuItem MakeItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        private static T FindDataContext<T>(object source) where T : class
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is FrameworkElement element && element.DataContext is T match) return match;
                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        // ================================================================== уведомления

        public void ShowToast(string text, string kind = "info")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowToast(text, kind)));
                return;
            }

            Color accent;
            switch (kind)
            {
                case "ok": accent = Color.FromRgb(0x4A, 0xDE, 0x9B); break;
                case "error": accent = Color.FromRgb(0xFF, 0x5A, 0x6E); break;
                case "warn": accent = Color.FromRgb(0xFF, 0xC4, 0x6B); break;
                default: accent = ThemeService.AccentColor; break;
            }

            if (ToastHost.Children.Count > 3) ToastHost.Children.RemoveAt(0);

            var bar = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(accent)
            };
            var label = new TextBlock
            {
                Text = text,
                Foreground = (Brush)FindResource("TextBrush"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(bar, 0);
            Grid.SetColumn(label, 1);
            grid.Children.Add(bar);
            grid.Children.Add(label);

            var border = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 12, 12, 19)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 20, 12),
                Margin = new Thickness(0, 6, 0, 0),
                Child = grid,
                Opacity = 0,
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.55, Color = Colors.Black }
            };
            var transform = new TranslateTransform(0, 22);
            border.RenderTransform = transform;

            ToastHost.Children.Add(border);

            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.22)));
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == "error" ? 6 : 3.6) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
                fade.Completed += (s2, e2) => ToastHost.Children.Remove(border);
                border.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }

        // ================================================================== обложка и клип

        /// <summary>Обновляет всё, что зависит от обложки/клипа трека.</summary>
        private void RefreshMediaFor(Track track)
        {
            RefreshAll();
            if (_player != null) _player.SyncNowPlaying();
            UpdateBackgroundVideo();
        }

        /// <summary>Выбор обложки для трека, у которого её нет (или замена существующей).</summary>
        public bool PickCover(Track track)
        {
            if (track == null) return false;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Обложка песни",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.tif;*.tiff|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return false;

            if (!LibraryService.SetCover(track, dialog.FileName))
            {
                ShowToast("Не удалось прочитать изображение.", "error");
                return false;
            }

            AuthService.LogActivity("library", "Обложка обновлена: «" + track.DisplayTitle + "»");
            RefreshMediaFor(track);
            ShowToast("Обложка сохранена", "ok");
            return true;
        }

        public void ClearCover(Track track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.CoverPath)) return;
            LibraryService.ClearCover(track);
            RefreshMediaFor(track);
            ShowToast("Обложка убрана", "info");
        }

        /// <summary>Прикрепляет короткий клип (MP4, до 5 с) как фон для песни.</summary>
        public async void PickClip(Track track)
        {
            if (track == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Клип на фон — только MP4, не длиннее 5 секунд",
                Filter = "Видео MP4|*.mp4|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            var result = await ClipService.InspectAsync(dialog.FileName);
            if (!result.Ok)
            {
                ShowToast(result.Error, "error");
                return;
            }

            if (!LibraryService.SetVideo(track, dialog.FileName))
            {
                ShowToast("Не удалось сохранить клип.", "error");
                return;
            }

            _videoBadPath = null;
            AuthService.LogActivity("library", "Клип добавлен: «" + track.DisplayTitle + "»");
            RefreshMediaFor(track);
            ShowToast("Клип прикреплён (" + result.Duration.ToString("0.#") + " с)", "ok");
        }

        public void ClearClip(Track track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.VideoPath)) return;
            LibraryService.ClearVideo(track);
            _videoBadPath = null;
            RefreshMediaFor(track);
            ShowToast("Клип убран", "info");
        }

        // ------------------------------------------------------------------ обложка на весь экран

        public bool CoverFullscreenOpen => CoverOverlay.Visibility == Visibility.Visible;

        public void ShowCoverFullscreen(Track track)
        {
            if (track == null) return;

            if (!track.HasCover)
            {
                ShowToast("У этой песни нет обложки — добавьте её кнопкой с иконкой картинки.", "warn");
                return;
            }

            var image = Storage.LoadImage(track.CoverPath, (int)Math.Max(900, ActualWidth));
            if (image == null)
            {
                ShowToast("Не удалось открыть обложку.", "error");
                return;
            }

            CoverFullImage.Source = image;
            CoverFullTitle.Text = track.DisplayTitle;
            CoverFullArtist.Text = track.DisplayArtist;

            CoverOverlay.Visibility = Visibility.Visible;
            CoverOverlay.Opacity = 0;
            CoverOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
            StartCoverHintTimer();
        }

        public void HideCoverFullscreen()
        {
            if (CoverOverlay.Visibility != Visibility.Visible) return;

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.18));
            fade.Completed += (s, e) =>
            {
                CoverOverlay.Visibility = Visibility.Collapsed;
                CoverFullImage.Source = null;
            };
            CoverOverlay.BeginAnimation(OpacityProperty, fade);
        }

        private DispatcherTimer _coverHintTimer;

        private void StartCoverHintTimer()
        {
            CoverFullHint.Visibility = Visibility.Visible;
            if (_coverHintTimer == null)
            {
                _coverHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _coverHintTimer.Tick += (s, e) =>
                {
                    _coverHintTimer.Stop();
                    CoverFullHint.Visibility = Visibility.Collapsed;
                };
            }
            _coverHintTimer.Stop();
            _coverHintTimer.Start();
        }

        private void CoverOverlay_MouseDown(object sender, MouseButtonEventArgs e) => HideCoverFullscreen();

        private void CoverFullClose_Click(object sender, RoutedEventArgs e) => HideCoverFullscreen();

        // ------------------------------------------------------------------ видео на фоне

        private string _videoPath;
        private string _videoBadPath;
        private DispatcherTimer _videoLoopTimer;

        /// <summary>Синхронизирует фоновый клип с текущим треком и состоянием плеера.</summary>
        public void UpdateBackgroundVideo()
        {
            if (VideoBackground == null) return;

            try
            {
                var track = AudioService.Current;
                var path = track?.VideoPath;
                bool has = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

                if (!has || string.Equals(_videoBadPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    StopVideo();
                    return;
                }

                if (!string.Equals(_videoPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _videoPath = path;
                    VideoBackground.Stop();
                    VideoBackground.Source = new Uri(path, UriKind.Absolute);
                    VideoBackground.Position = TimeSpan.Zero;
                }

                VideoBackground.Visibility = Visibility.Visible;
                if (AudioService.IsPlaying)
                {
                    VideoBackground.Play();
                    StartVideoLoopGuard();
                }
                else
                {
                    VideoBackground.Pause();
                    _videoLoopTimer?.Stop();
                }
            }
            catch { StopVideo(); }
        }

        private void StartVideoLoopGuard()
        {
            if (_videoLoopTimer == null)
            {
                _videoLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                _videoLoopTimer.Tick += (s, e) => GuardVideoLoop();
            }
            _videoLoopTimer.Start();
        }

        /// <summary>Перематывает клип в начало чуть раньше конца — так петля идёт без обрыва и чёрного кадра.</summary>
        private void GuardVideoLoop()
        {
            try
            {
                if (VideoBackground.Visibility != Visibility.Visible) return;
                if (!AudioService.IsPlaying) return;

                var dur = VideoBackground.NaturalDuration;
                if (!dur.HasTimeSpan) return;

                var total = dur.TimeSpan.TotalSeconds;
                if (total <= 0.5) return;

                if (VideoBackground.Position.TotalSeconds >= Math.Max(0.1, total - 0.2))
                    VideoBackground.Position = TimeSpan.Zero;
            }
            catch { }
        }

        private void StopVideo()
        {
            try
            {
                _videoLoopTimer?.Stop();
                if (VideoBackground != null)
                {
                    VideoBackground.Stop();
                    VideoBackground.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
            _videoPath = null;
        }

        private void VideoBackground_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Крутим клип по кругу. НЕ вызываем Stop() — он очищает кадр и даёт чёрную вспышку.
            // Просто возвращаем позицию в начало и продолжаем; стык маскируем лёгким проявлением.
            try
            {
                VideoBackground.Position = TimeSpan.Zero;
                VideoBackground.Play();

                var fade = new System.Windows.Media.Animation.DoubleAnimation(0.55, 0.92, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fade.Completed += (s2, e2) =>
                {
                    try { VideoBackground.BeginAnimation(OpacityProperty, null); }
                    catch { }
                };
                VideoBackground.BeginAnimation(OpacityProperty, fade);
            }
            catch { }
        }

        private void VideoBackground_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _videoBadPath = _videoPath;
            StopVideo();
            ShowToast("Не удалось воспроизвести клип — возможно, кодек не поддерживается системой.", "warn");
        }

        // ================================================================== горячие клавиши и панели

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (Auth.Visibility == Visibility.Visible) return;

            if (CoverOverlay.Visibility == Visibility.Visible)
            {
                HideCoverFullscreen();
                e.Handled = true;
                return;
            }

            bool typing = Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is PasswordBox;
            if (typing && e.Key != Key.Escape) return;

            switch (e.Key)
            {
                case Key.Space:
                    AudioService.TogglePlay();
                    e.Handled = true;
                    break;
                case Key.Right when Keyboard.Modifiers == ModifierKeys.None:
                    AudioService.Seek(AudioService.Position + 5);
                    e.Handled = true;
                    break;
                case Key.Left when Keyboard.Modifiers == ModifierKeys.None:
                    AudioService.Seek(AudioService.Position - 5);
                    e.Handled = true;
                    break;
                case Key.N:
                    AudioService.Next();
                    e.Handled = true;
                    break;
                case Key.P:
                    AudioService.Previous();
                    e.Handled = true;
                    break;
                case Key.Up:
                    SetVolume(AudioService.Volume + 0.05);
                    e.Handled = true;
                    break;
                case Key.Down:
                    SetVolume(AudioService.Volume - 0.05);
                    e.Handled = true;
                    break;
                case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                    SearchInput.Focus();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (OverlayHost.Visibility == Visibility.Visible) CloseModal();
                    break;
            }
        }

        private void SetVolume(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            AudioService.Volume = value;
            App.Settings.Volume = value;
            App.Settings.Muted = value <= 0.001;
            if (_player != null) _player.SyncVolume();
        }

        private void BtnEffects_Click(object sender, RoutedEventArgs e) => ShowPanel(new EffectsPanel());
        private void BtnTheme_Click(object sender, RoutedEventArgs e) => ShowPanel(new ThemePanel());
        private void BtnBackground_Click(object sender, RoutedEventArgs e) => ShowPanel(new BackgroundPanel());
        private void BtnServices_Click(object sender, RoutedEventArgs e) => ShowPanel(new SpotifyPanel());
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => ShowPanel(new SettingsPanel());
        private void UserCard_Click(object sender, MouseButtonEventArgs e) => ShowPanel(new ProfilePanel());

        private void BtnAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.Current == null || !AuthService.Current.IsStaff) return;
            ToggleAdmin();
        }

        public void ToggleAdmin()
        {
            if (_player == null) return;
            if (AuthService.Current == null || !AuthService.Current.IsStaff) return;

            // подхватываем аккаунты, зарегистрированные вне этого окна, — список всегда актуален
            AuthService.MergeFromDisk();

            if (_admin == null)
            {
                _admin = new AdminView();
                _admin.RequestOpenPlayer += () => ToggleAdmin();
            }

            if (_adminOpen)
            {
                ContentHost.Content = _player;
                RefreshLibrary();
                AnimateView(_player);
            }
            else
            {
                _admin.Refresh();
                ContentHost.Content = _admin;
                PageTitle.Text = "Панель владельца";
                NavList.SelectedItem = null;
                PlaylistList.SelectedItem = null;
                AnimateView(_admin);
            }
        }

        public void RefreshUserCard()
        {
            if (AuthService.Current == null) return;
            UserAvatar.Source = AuthService.Current.AvatarPath;
            UserAvatar.Initial = AuthService.Current.Initial;
            UserName.Text = AuthService.Current.Nick;
            UserRole.Text = AuthService.Current.RoleLabel;
        }

        public void Logout()
        {
            AudioService.Stop();
            AuthService.Logout();
            CloseModal();

            CoverOverlay.Visibility = Visibility.Collapsed;
            CoverFullImage.Source = null;
            StopVideo();

            _player = null;
            _admin = null;
            _view = "library";
            _currentPlaylist = null;
            ContentHost.Content = null;
            PlaylistList.ItemsSource = null;
            SearchInput.Clear();

            ShowAuthScreen();
            ShowToast("Вы вышли из аккаунта", "info");
        }
    }
}
