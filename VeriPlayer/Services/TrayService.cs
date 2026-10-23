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

                // Загружаем иконку из ресурсов
                try
                {
                    var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.ico");
                    Diagnostics.Log("TrayService: trying icon path: " + iconPath);
                    
                    if (File.Exists(iconPath))
                    {
                        _trayIcon.Icon = new System.Drawing.Icon(iconPath);
                        Diagnostics.Log("TrayService: icon loaded successfully");
                    }
                    else
                    {
                        // Fallback: используем стандартную иконку приложения
                        Diagnostics.Log("TrayService: icon file not found, using default");
                        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                    }
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
            var menu = new WinForms.ContextMenuStrip();

            var playPauseItem = new WinForms.ToolStripMenuItem("▶ Играть / Пауза");
            playPauseItem.Click += (s, e) => AudioService.TogglePlay();
            menu.Items.Add(playPauseItem);

            var nextItem = new WinForms.ToolStripMenuItem("⏭ Следующий трек");
            nextItem.Click += (s, e) => AudioService.Next();
            menu.Items.Add(nextItem);

            var prevItem = new WinForms.ToolStripMenuItem("⏮ Предыдущий трек");
            prevItem.Click += (s, e) => AudioService.Previous();
            menu.Items.Add(prevItem);

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
            }
            else
            {
                var status = playing ? "▶" : "⏸";
                var progress = "";
                
                // Добавляем прогресс если песня играет
                if (duration > 0.4)
                {
                    var posTime = FormatTime(position);
                    var durTime = FormatTime(duration);
                    progress = $" [{posTime}/{durTime}]";
                }
                
                _trayIcon.Text = $"{status} {track.DisplayTitle} — {track.DisplayArtist}{progress}";
                
                // Обрезаем если слишком длинно (макс 63 символа для NotifyIcon)
                if (_trayIcon.Text.Length > 63)
                    _trayIcon.Text = _trayIcon.Text.Substring(0, 60) + "...";
            }
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
