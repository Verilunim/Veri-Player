using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Track = VeriPlayer.Models.Track;
using VeriPlayer.Models;
using VeriPlayer.Services;
using VeriPlayer.Views.Panels;

namespace VeriPlayer
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; private set; } = new AppSettings();

        private const int CurrentDesign = 2;

        public static new App Current => (App)Application.Current;

        public static void SaveSettings()
        {
            Storage.Save(Storage.SettingsFile, Settings);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) => Diagnostics.Log("fatal: " + args.ExceptionObject);

            Settings = Storage.Load(Storage.SettingsFile, new AppSettings()) ?? new AppSettings();
            Settings.Volume = Math.Max(0, Math.Min(1, Settings.Volume));
            Settings.EffectIntensity = Math.Max(0.15, Math.Min(1.2, Settings.EffectIntensity));
            Settings.BackgroundDim = Math.Max(0, Math.Min(0.9, Settings.BackgroundDim));
            Settings.BackgroundBlur = Math.Max(0, Math.Min(60, Settings.BackgroundBlur));

            // новый минималистичный дизайн: прежние неоновые цвета и эффекты сбрасываем один раз
            if (Settings.DesignVersion < CurrentDesign)
            {
                var fresh = new AppSettings();
                Settings.Hue = fresh.Hue;
                Settings.Saturation = fresh.Saturation;
                Settings.Value = fresh.Value;
                Settings.SecondHueOffset = fresh.SecondHueOffset;
                Settings.AutoHue = false;
                Settings.Effect = fresh.Effect;
                Settings.DesignVersion = CurrentDesign;
                SaveSettings();
            }

            ThemeService.Apply(Settings);

            AuthService.Init();
            LibraryService.Init();
            BackgroundService.Init();
            SpotifyService.Init();
            SoundCloudService.Init();
            YouTubeMusicService.Init();
            AudioService.Init();
            MusicGenService.Init();
            AudioService.Volume = Settings.Muted ? 0 : Settings.Volume;
            AudioService.Shuffle = Settings.Shuffle;
            AudioService.Repeat = Settings.Repeat;
            
            DiscordService.Init();
            TrayService.Init();

            var window = new MainWindow();

            try
            {
                if (Diagnostics.TryHandleCommandLine(e.Args, window)) return;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("command line: " + ex);
            }

            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Diagnostics.Log("UI: " + e.Exception);
            try
            {
                var window = VeriPlayer.MainWindow.Instance;
                if (window != null)
                {
                    window.ShowToast("Что-то пошло не так: " + e.Exception.Message, "error");
                    e.Handled = true;
                }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                AudioService.Shutdown();
                SaveSettings();
            }
            catch { }
            base.OnExit(e);
        }
    }

    public static class Diagnostics
    {
        private static readonly object Gate = new object();

        public static string LogFile => Path.Combine(Storage.Root, "veriplayer.log");

        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        public static bool TryHandleCommandLine(string[] args, MainWindow window)
        {
#if !DEBUG
            // отладочные ключи (--login, --register, --admin, --demo, --screenshot…) в релизе не нужны:
            // они позволяли обходить экран входа и писать файлы по произвольным путям
            return false;
#else
            if (args == null || args.Length == 0) return false;

            string screenshot = null, view = null, modal = null, login = null, import = null, music = null;
            string register = null, audit = null;
            bool admin = false, demo = false, users = false, sharetest = false;
            int delay = 1700, play = -1;
            double seek = 0;
            double volume = -1;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--screenshot": screenshot = Next(args, ref i); break;
                    case "--view": view = Next(args, ref i); break;
                    case "--modal": modal = Next(args, ref i); break;
                    case "--login": login = Next(args, ref i); break;
                    case "--import": import = Next(args, ref i); break;
                    case "--music": music = Next(args, ref i); break;
                    case "--register": register = Next(args, ref i); break;
                    case "--audit": audit = Next(args, ref i); break;
                    case "--users": users = true; break;
                    case "--play": int.TryParse(Next(args, ref i), out play); break;
                    case "--seek": double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out seek); break;
                    case "--volume": double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out volume); break;
                    case "--admin": admin = true; break;
                    case "--demo": demo = true; break;
                    case "--sharetest": sharetest = true; break;
                    case "--delay": int.TryParse(Next(args, ref i), out delay); break;
                }
            }

            if (music != null) GenerateTestMusic(music);

            bool onlyMusic = music != null && screenshot == null && view == null && modal == null &&
                             login == null && import == null && register == null && audit == null &&
                             !admin && !demo && !users && play < 0;
            if (onlyMusic)
            {
                Application.Current?.Shutdown();
                return true;
            }

            bool nothing = screenshot == null && view == null && modal == null && login == null &&
                           import == null && register == null && audit == null &&
                           !admin && !demo && !users && play < 0 && music == null && !sharetest;
            if (nothing) return false;

            if (login != null)
            {
                var parts = login.Split(new[] { ':' }, 2);
                var nick = parts[0];
                var password = parts.Length > 1 ? parts[1] : parts[0];
                var (ok, error, _) = AuthService.Login(nick, password);
                if (!ok) Log("login failed: " + error);
            }

            if (register != null)
            {
                var parts = register.Split(new[] { ':' }, 2);
                var nick = parts[0];
                var password = parts.Length > 1 ? parts[1] : parts[0];
                var (ok, error, user) = AuthService.Register(nick, password, password, "тестовый аккаунт", null);
                Log("register: ok=" + ok + " err=" + (error ?? "-") + " user=" + (user?.Nick ?? "-"));
            }

            if (users)
            {
                Log("users: " + AuthService.Users.Count + " -> " +
                    string.Join(", ", AuthService.Users.Select(u => u.Nick + "/" + u.Role)));
            }

            if (demo) SeedDemo();
            if (volume >= 0) AudioService.Volume = volume;

            bool applied = false;
            void ApplyOptions()
            {
                if (applied) return;
                applied = true;

                if (view != null) window.SelectView(view);
                if (admin) window.ToggleAdmin();
                if (modal != null)
                {
                    var panel = BuildPanel(modal);
                    if (panel != null) window.ShowPanel(panel);
                }
            }

            void Capture()
            {
                try
                {
                    var width = (int)Math.Max(1, window.ActualWidth);
                    var height = (int)Math.Max(1, window.ActualHeight);
                    var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    target.Render(window);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(target));

                    var full = Path.GetFullPath(screenshot);
                    var dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using (var stream = File.Create(full)) encoder.Save(stream);
                }
                catch (Exception ex)
                {
                    Log("screenshot: " + ex);
                }

                try { window.Close(); } catch { }
                Application.Current?.Shutdown();
            }

            // Loaded срабатывает после EnterApp() внутри окна, поэтому всё применяем поверх него.
            window.Loaded += async (s, e) =>
            {
                try
                {
                    if (import != null)
                    {
                        var files = LibraryService.ExpandPaths(new[] { import });
                        Log("import: " + files.Count + " files");
                        await LibraryService.ImportAsync(files, null);
                        window.RefreshAll();
                    }

                    ApplyOptions();

                    if (sharetest) RunShareTest();

                    if (play >= 0 && LibraryService.Tracks.Count > 0)
                    {
                        var tracks = LibraryService.Tracks.ToList();
                        var index = Math.Max(0, Math.Min(play, tracks.Count - 1));
                        AudioService.PlayTrack(tracks[index], tracks, "Библиотека");
                        Log("play: " + tracks[index].DisplayTitle + " / exists=" + tracks[index].Exists);
                        if (seek > 0)
                        {
                            await Task.Delay(300);
                            AudioService.Seek(seek);
                        }
                    }

                    if (audit != null)
                    {
                        await Task.Delay(Math.Max(250, delay));
                        DumpLayout(window, audit);
                        if (screenshot == null)
                        {
                            try { window.Close(); } catch { }
                            Application.Current?.Shutdown();
                            return;
                        }
                    }

                    if (screenshot != null)
                    {
                        await Task.Delay(Math.Max(300, delay));
                        Log("state: playing=" + AudioService.IsPlaying +
                            " pos=" + Math.Round(AudioService.Position, 1) +
                            " dur=" + Math.Round(AudioService.Duration, 1) +
                            " hasTrack=" + AudioService.HasTrack +
                            " title=" + (AudioService.Current?.DisplayTitle ?? "-"));
                        Capture();
                    }
                }
                catch (Exception ex)
                {
                    Log("diagnostics: " + ex);
                    if (screenshot != null)
                    {
                        try { window.Close(); } catch { }
                        Application.Current?.Shutdown();
                    }
                }
            };

            window.Show();
            return true;
#endif
        }

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length) return null;
            return args[++index];
        }

        // ------------------------------------------------------------------ аудит разметки

        private static void DumpLayout(Window window, string path)
        {
            try
            {
                var lines = new List<string>();
                lines.Add("window " + Math.Round(window.ActualWidth) + "x" + Math.Round(window.ActualHeight));
                Collect(window, window, lines);

                var full = Path.GetFullPath(path);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(full, lines);
                Log("audit: " + full + " (" + lines.Count + " rows)");
            }
            catch (Exception ex)
            {
                Log("audit: " + ex);
            }
        }

        /// <summary>Собирает геометрию всех кнопок в координатах окна — чтобы ловить «кривые» кнопки.</summary>
        private static void Collect(DependencyObject node, Visual root, List<string> lines)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);

                if (child is ButtonBase bb && !(child is RepeatButton))
                {
                    var fe = bb as FrameworkElement;
                    Rect box = Rect.Empty;
                    Rect parentBox = Rect.Empty;
                    string parentName = "-";
                    string parentType = "-";
                    try
                    {
                        box = fe.TransformToAncestor(root)
                                 .TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                        if (VisualTreeHelper.GetParent(fe) is FrameworkElement pfe)
                        {
                            parentType = pfe.GetType().Name;
                            parentName = string.IsNullOrEmpty(pfe.Name) ? "-" : pfe.Name;
                            if (VisualTreeHelper.GetParent(pfe) is Visual)
                            {
                                parentBox = pfe.TransformToAncestor(root)
                                    .TransformBounds(new Rect(0, 0, pfe.ActualWidth, pfe.ActualHeight));
                            }
                        }
                    }
                    catch { }

                    var name = string.IsNullOrEmpty(fe.Name) ? "-" : fe.Name;
                    var text = ContentText(bb) ?? "";
                    lines.Add(string.Format(
                        "{0,-16} | {1,-10} | x={2,7:0.0} y={3,7:0.0} w={4,6:0.0} h={5,5:0.0} | cx={6,7:0.0} | parent={7}/{8} [{9}] | \"{10}\"",
                        name, fe.GetType().Name,
                        box.X, box.Y, box.Width, box.Height,
                        box.X + box.Width / 2,
                        parentName, parentType,
                        parentBox.IsEmpty ? "-" : string.Format("x{0:0} y{1:0} w{2:0} h{3:0}", parentBox.X, parentBox.Y, parentBox.Width, parentBox.Height),
                        text));
                }

                if (child is ListBox listBox)
                {
                    var lfe = (FrameworkElement)child;
                    Rect box = new Rect();
                    try
                    {
                        box = lfe.TransformToAncestor(root)
                            .TransformBounds(new Rect(0, 0, lfe.ActualWidth, lfe.ActualHeight));
                    }
                    catch { }

                    var items = new List<string>();
                    foreach (var item in listBox.Items)
                        items.Add(item?.ToString() ?? "");
                    var lname = string.IsNullOrEmpty(lfe.Name) ? "-" : lfe.Name;
                    lines.Add(string.Format(
                        "{0,-16} | {1,-10} | x={2,7:0.0} y={3,7:0.0} w={4,6:0.0} h={5,5:0.0} |               | items={6} [{7}]",
                        lname, "ListBox", box.X, box.Y, box.Width, box.Height,
                        items.Count,
                        string.Join("; ", items.Take(12).ToArray())));
                }

                if (child is Image image)
                {
                    var ife = (FrameworkElement)child;
                    Rect box = new Rect();
                    try
                    {
                        box = ife.TransformToAncestor(root)
                            .TransformBounds(new Rect(0, 0, ife.ActualWidth, ife.ActualHeight));
                    }
                    catch { }

                    string src;
                    try { src = image.Source?.ToString() ?? "(null)"; }
                    catch { src = "(err)"; }

                    var iname = string.IsNullOrEmpty(ife.Name) ? "-" : ife.Name;
                    lines.Add(string.Format(
                        "{0,-16} | {1,-10} | x={2,7:0.0} y={3,7:0.0} w={4,6:0.0} h={5,5:0.0} |               | src={6}",
                        iname, "Image", box.X, box.Y, box.Width, box.Height, src));
                }

                Collect(child, root, lines);
            }
        }

        private static string ContentText(ContentControl cc)
        {
            if (cc.Content is string s) return s;
            return FindText(cc);
        }

        private static string FindText(DependencyObject node)
        {
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is TextBlock t) return t.Text;
                var inner = FindText(child);
                if (inner != null) return inner;
            }
            return null;
        }

        private static IModalPanel BuildPanel(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "theme": return new ThemePanel();
                case "effects": return new EffectsPanel();
                case "background":
                case "bg": return new BackgroundPanel();
                case "settings": return new SettingsPanel();
                case "profile": return new ProfilePanel();
                case "spotify": return new SpotifyPanel();
                case "generate":
                case "gen": return new GenerateMusicPanel();
                case "share":
                    {
                        var track = LibraryService.Tracks.FirstOrDefault();
                        return track == null ? null : new ShareTrackPanel(track);
                    }
                case "addlink":
                case "bylink": return new AddByLinkPanel();
                case "trackedit":
                    {
                        var track = LibraryService.Tracks.FirstOrDefault();
                        return track == null ? null : new TrackEditPanel(track);
                    }
                case "prompt": return new PromptPanel("Новый плейлист", "Название", "", "Создать");
                case "confirm": return new ConfirmPanel("Удалить этот трек из библиотеки?", "Удалить");
                default: return null;
            }
        }

        /// <summary>Диагностика: проверяет ссылку-передачу трека — сборка и разбор туда-обратно.</summary>
        private static void RunShareTest()
        {
            var track = LibraryService.Tracks.FirstOrDefault();
            if (track == null) { Log("sharetest: no tracks"); return; }

            try
            {
                var meta = ShareService.BuildLink(track, false, "Verilunim");
                Log("sharetest meta len=" + meta.Length + " head=" + meta.Substring(0, Math.Min(40, meta.Length)));

                if (ShareService.TryParse(meta, out var p1, out var e1))
                    Log("sharetest meta parsed: ok artist=" + p1.Artist + " title=" + p1.Title + " hasFile=" + !string.IsNullOrEmpty(p1.Data));
                else
                    Log("sharetest meta parse FAILED: " + e1);

                var full = ShareService.BuildLink(track, true, "Verilunim");
                Log("sharetest full len=" + full.Length);
                if (ShareService.TryParse(full, out var p2, out var e2))
                    Log("sharetest full parsed: ok bytes=" + (p2.Data?.Length ?? 0) + " ext=" + p2.Ext);
                else
                    Log("sharetest full parse FAILED: " + e2);

                var (ok, err, added) = ShareService.AddFromLink(meta);
                Log("sharetest add: ok=" + ok + " err=" + (err ?? "-") + " title=" + (added?.DisplayTitle ?? "-"));
                Log("sharetest library now=" + LibraryService.Tracks.Count);
            }
            catch (Exception ex)
            {
                Log("sharetest exception: " + ex);
            }
        }

        /// <summary>Диагностика: наполняет библиотеку и профиль фиктивными данными (только в памяти).</summary>
        private static void SeedDemo()
        {
            var me = AuthService.Current;
            var existing = Path.Combine(AppContext.BaseDirectory, "VeriPlayer.exe");

            var data = new (string Title, string Artist, string Album, string Genre, int Year, double Duration)[]
            {
                ("Полёт над городом", "Aurora Field", "Night Signals", "Электроника", 2023, 254),
                ("Тихий дождь", "Мария Волкова", "Полутона", "Инди", 2022, 218),
                ("Neon Harbour", "Kite & Co", "Harbour Lights", "Synthwave", 2024, 301),
                ("Северное сияние", "Лесной Хор", "Тундра", "Эмбиент", 2021, 366),
                ("Coffee At Dawn", "Slow Motion", "Morning Tape", "Lo-Fi", 2020, 187),
                ("Дорога домой", "Полярный", "Горизонт", "Рок", 2019, 244),
                ("Velvet Sky", "The Lonely Pines", "Static Bloom", "Дрим-поп", 2023, 276),
                ("Стеклянный шар", "Витрина", "Отражения", "Поп", 2022, 199),
                ("Midnight Drive", "Cassette Ghost", "Autobahn", "Retrowave", 2024, 329),
                ("Первый снег", "Тёплый Свет", "Декабрь", "Фолк", 2018, 232),
                ("Paper Planes", "Isla Grey", "Airborne", "Инди-поп", 2021, 205),
                ("Огни большого города", "Сатин", "Проспект", "Джаз", 2017, 358)
            };

            var tracks = new List<Track>();
            foreach (var item in data)
            {
                tracks.Add(new Track
                {
                    FilePath = existing,
                    Title = item.Title,
                    Artist = item.Artist,
                    Album = item.Album,
                    Genre = item.Genre,
                    Year = item.Year,
                    Duration = item.Duration,
                    FileSize = 6_200_000 + tracks.Count * 480_000,
                    AddedById = me?.Id ?? "demo",
                    AddedByName = me?.Nick ?? "Verilunim"
                });
            }

            LibraryService.SeedForDiagnostics(tracks);

            if (me == null) return;

            var user = UserDataService.Current;
            user.Favorites.Clear();
            user.Recent.Clear();
            foreach (var track in tracks.Take(4)) user.Favorites.Add(track.Id);
            foreach (var track in tracks.Take(3)) user.Recent.Add(track.Id);

            user.Playlists.Clear();
            user.Playlists.Add(new Playlist
            {
                Name = "Вечерний вайб",
                TrackIds = tracks.Take(6).Select(t => t.Id).ToList()
            });
            user.Playlists.Add(new Playlist
            {
                Name = "Для работы",
                TrackIds = tracks.Skip(4).Take(5).Select(t => t.Id).ToList()
            });

            AuthService.LogActivity(me, "library", "Добавлено 12 треков");
            AuthService.LogActivity(me, "auth", "Вход в аккаунт");
        }

        // ==================================================================== тестовая музыка

        /// <summary>Диагностика: создаёт несколько настоящих WAV с тегами и обложками для проверки импорта и воспроизведения.</summary>
        private static void GenerateTestMusic(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);

                var items = new (string Title, string Artist, string Album, string Genre, int Year, double Seconds, double Hz, Color A, Color B)[]
                {
                    ("Полёт над городом", "Aurora Field", "Night Signals", "Электроника", 2023, 26, 220, Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0x36, 0xC5, 0xFF)),
                    ("Тихий дождь", "Мария Волкова", "Полутона", "Инди", 2022, 21, 196, Color.FromRgb(0xFF, 0x7A, 0xB0), Color.FromRgb(0xFF, 0xC2, 0x6E)),
                    ("Neon Harbour", "Kite & Co", "Harbour Lights", "Synthwave", 2024, 29, 174, Color.FromRgb(0x21, 0xE0, 0xB8), Color.FromRgb(0x4F, 0x8B, 0xFF)),
                    ("Северное сияние", "Лесной Хор", "Тундра", "Эмбиент", 2021, 33, 146, Color.FromRgb(0x6E, 0xFF, 0xC8), Color.FromRgb(0x8A, 0x6C, 0xFF)),
                    ("Velvet Sky", "The Lonely Pines", "Static Bloom", "Дрим-поп", 2023, 23, 262, Color.FromRgb(0xFF, 0x9A, 0x6E), Color.FromRgb(0xB0, 0x5A, 0xFF)),
                    ("Midnight Drive", "Cassette Ghost", "Autobahn", "Retrowave", 2024, 27, 233, Color.FromRgb(0xFF, 0x5A, 0x9E), Color.FromRgb(0x5A, 0x9E, 0xFF))
                };

                int number = 0;
                foreach (var item in items)
                {
                    number++;
                    var path = Path.Combine(folder, number.ToString("00") + " " + item.Artist + " - " + item.Title + ".wav");
                    WriteTone(path, item.Seconds, item.Hz);

                    try
                    {
                        using (var file = TagLib.File.Create(path))
                        {
                            file.Tag.Title = item.Title;
                            file.Tag.Performers = new[] { item.Artist };
                            file.Tag.Album = item.Album;
                            file.Tag.Genres = new[] { item.Genre };
                            file.Tag.Year = (uint)item.Year;
                            file.Tag.Pictures = new TagLib.IPicture[]
                            {
                                new TagLib.Picture
                                {
                                    Data = new TagLib.ByteVector(MakeCoverPng(item.Title, item.A, item.B)),
                                    Type = TagLib.PictureType.FrontCover,
                                    MimeType = "image/png",
                                    Description = "cover"
                                }
                            };
                            file.Save();
                        }
                        Log("generated: " + path);
                    }
                    catch (Exception ex)
                    {
                        Log("tag generate: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("generate music: " + ex);
            }
        }

        private static void WriteTone(string path, double seconds, double baseHz)
        {
            const int rate = 44100;
            using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(rate, 16, 1)))
            {
                int total = (int)(seconds * rate);
                for (int i = 0; i < total; i++)
                {
                    double t = (double)i / rate;
                    double vibrato = 1 + 0.004 * Math.Sin(2 * Math.PI * 0.6 * t);
                    double value =
                        0.33 * Math.Sin(2 * Math.PI * baseHz * vibrato * t) +
                        0.22 * Math.Sin(2 * Math.PI * baseHz * 1.25 * t) +
                        0.16 * Math.Sin(2 * Math.PI * baseHz * 1.5 * t) +
                        0.06 * Math.Sin(2 * Math.PI * baseHz * 2 * t);

                    double beat = Math.Exp(-3.0 * ((t * 2) % 1.0));
                    value *= 0.6 + 0.4 * beat;

                    double envelope = Math.Min(1, t / 0.6) * Math.Min(1, Math.Max(0, (seconds - t) / 1.2));
                    double sample = value * envelope;
                    if (sample > 1) sample = 1;
                    else if (sample < -1) sample = -1;

                    writer.WriteSample((float)sample);
                }
            }
        }

        private static byte[] MakeCoverPng(string title, Color a, Color b)
        {
            const int size = 512;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
                dc.DrawRectangle(brush, null, new Rect(0, 0, size, size));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)), null,
                    new Point(size * 0.74, size * 0.26), 150, 150);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)), null,
                    new Point(size * 0.24, size * 0.82), 120, 120);

                var letter = (title.Length > 0 ? title.Substring(0, 1) : "♪").ToUpperInvariant();
                var text = new FormattedText(letter, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 300, Brushes.White, 1.0);
                dc.DrawText(text, new Point(size * 0.5 - text.Width / 2, size * 0.5 - text.Height / 2));
            }

            var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }
    }
}
