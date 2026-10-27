using System;
using System.IO;
using WinForms = System.Windows.Forms;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Системный трей: при закрытии окна приложение сворачивается в трей и продолжает играть.
    /// </summary>
    public static class TrayService
    {
        private static WinForms.NotifyIcon _trayIcon;
        private static bool _initialized;

        public static event Action RestoreRequested;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                _trayIcon = new WinForms.NotifyIcon
                {
                    Text = "Veri Player",
                    Visible = false
                };

                // логотип берём из самого exe: в однофайловой сборке папки Assets рядом нет
                try
                {
                    var exe = Environment.ProcessPath;
                    _trayIcon.Icon = (!string.IsNullOrEmpty(exe) ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null)
                                     ?? System.Drawing.SystemIcons.Application;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("TrayService: icon load error: " + ex.Message);
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }

                _trayIcon.DoubleClick += (s, e) =>
                {
                    Diagnostics.Log("TrayService: double click detected");
                    RestoreRequested?.Invoke();
                };

                BuildContextMenu();
                
                // Подписываемся на позицию песни для обновления прогресса в tooltip
                AudioService.PositionTick += OnPositionTick;
                AudioService.TrackChanged += _ => OnTrackOrStateChanged();
                AudioService.StateChanged += _ => OnTrackOrStateChanged();
                
                Diagnostics.Log("TrayService: initialized successfully");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("TrayService: init failed: " + ex.Message);
            }
        }

        private static void OnPositionTick(double seconds)
        {
            // Обновляем tooltip с прогрессом только если трей виден
            if (_trayIcon == null || !_trayIcon.Visible) return;
            UpdateTooltip(AudioService.Current, AudioService.IsPlaying, seconds, AudioService.Duration);
        }

        private static void OnTrackOrStateChanged()
        {
            if (_trayIcon == null || !_trayIcon.Visible) return;
            UpdateTooltip(AudioService.Current, AudioService.IsPlaying, AudioService.Position, AudioService.Duration);
        }

        private static void BuildContextMenu()
        {
            var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };

            // сверху — что играет сейчас; символы ▶⏭ в тексте пунктов съезжали, поэтому без них
            var nowItem = new WinForms.ToolStripMenuItem { Enabled = false };
            menu.Items.Add(nowItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var prevItem = new WinForms.ToolStripMenuItem("Предыдущий трек");
            prevItem.Click += (s, e) => AudioService.Previous();
            menu.Items.Add(prevItem);

            var playPauseItem = new WinForms.ToolStripMenuItem("Играть");
            playPauseItem.Click += (s, e) => AudioService.TogglePlay();
            menu.Items.Add(playPauseItem);

            var nextItem = new WinForms.ToolStripMenuItem("Следующий трек");
            nextItem.Click += (s, e) => AudioService.Next();
            menu.Items.Add(nextItem);

            // подписи обновляем в момент открытия меню, чтобы они совпадали с текущим треком
            menu.Opening += (s, e) =>
            {
                var track = AudioService.Current;
                nowItem.Text = track == null ? "Ничего не играет" : Shorten(track.DisplayArtist + " — " + track.DisplayTitle, 48);
                playPauseItem.Text = AudioService.IsPlaying ? "Пауза" : "Играть";
                bool any = AudioService.Queue.Count > 0;
                prevItem.Enabled = playPauseItem.Enabled = nextItem.Enabled = any;
            };

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var showItem = new WinForms.ToolStripMenuItem("Открыть плеер");
            showItem.Click += (s, e) => RestoreRequested?.Invoke();
            menu.Items.Add(showItem);

            var exitItem = new WinForms.ToolStripMenuItem("Выход");
            exitItem.Click += (s, e) =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                mainWindow?.RealClose();
            };
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }

        public static void Show(string tooltip = null)
        {
            if (_trayIcon == null)
            {
                Diagnostics.Log("TrayService.Show: _trayIcon is null");
                return;
            }
            
            try
            {
                _trayIcon.Text = tooltip ?? "Veri Player";
                _trayIcon.Visible = true;
                
                // Принудительно обновляем статус иконки через небольшую задержку
                System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                {
                    try
                    {
                        // Двойное переключение гарантирует видимость
                        if (_trayIcon != null)
                        {
                            _trayIcon.Visible = false;
                            _trayIcon.Visible = true;
                        }
                    }
                    catch { }
                });
                
                Diagnostics.Log("TrayService.Show: icon set to visible, text=" + _trayIcon.Text);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("TrayService.Show: error: " + ex.Message);
            }
        }

        public static void Hide()
        {
            if (_trayIcon == null) return;
            _trayIcon.Visible = false;
        }

        public static void UpdateTooltip(Track track, bool playing, double position = 0, double duration = 0)
        {
            if (_trayIcon == null) return;

            if (track == null)
            {
                _trayIcon.Text = "Veri Player";
                return;
            }

            // прогресс всегда в конце строки: раньше длинное название обрезалось вместе с ним,
            // и казалось, что подсказка не следит за треком
            var status = playing ? "Играет" : "Пауза";
            var progress = duration > 0.4 ? FormatTime(position) + " / " + FormatTime(duration) : "";
            const int limit = 63; // предел подсказки значка в трее
            var head = status + (progress.Length > 0 ? " · " + progress : "") + "\n";
            _trayIcon.Text = head + Shorten(track.DisplayArtist + " — " + track.DisplayTitle, limit - head.Length);
        }

        private static string Shorten(string text, int max)
        {
            if (max < 4) return "";
            return text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
                : string.Format("{0}:{1:00}", span.Minutes, span.Seconds);
        }

        public static void ShowBalloon(string title, string text, int timeoutMs = 3000)
        {
            if (_trayIcon == null) return;
            try
            {
                _trayIcon.BalloonTipTitle = title;
                _trayIcon.BalloonTipText = text;
                _trayIcon.ShowBalloonTip(timeoutMs);
            }
            catch { }
        }

        public static void Shutdown()
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch { }
            _trayIcon = null;
            _initialized = false;
        }
    }
}
