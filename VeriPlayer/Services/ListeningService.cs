using System;
using System.Diagnostics;
using System.Windows.Threading;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Учёт прослушанного времени: общий итог в минутах и секунды по каждому треку.
    /// Всё хранится в данных пользователя, поэтому выход из аккаунта и новый вход итог не сбрасывают.
    /// </summary>
    public static class ListeningService
    {
        /// <summary>Как часто сбрасывать накопленное на диск.</summary>
        private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30);

        /// <summary>Переключили раньше — считаем, что трек пропущен.</summary>
        private const double SkipSeconds = 25;

        private static DispatcherTimer _timer;
        private static readonly Stopwatch Clock = new Stopwatch();
        private static TimeSpan _last;
        private static TimeSpan _lastSave;
        private static bool _dirty;
        private static int _lastWholeMinute = -1;

        private static string _sessionUserId;
        private static Track _sessionTrack;
        private static double _sessionSeconds;

        /// <summary>Итог изменился на целую минуту — пора обновить подписи.</summary>
        public static event Action MinutesChanged;

        public static void Init()
        {
            if (_timer != null) return;
            Clock.Start();
            _last = Clock.Elapsed;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
            AudioService.TrackChanged += OnTrackChanged;
        }

        /// <summary>Всего прослушано минут у пользователя (текущего, если не указан).</summary>
        public static double TotalMinutes(UserData data = null) => (data ?? UserDataService.Current).ListenedSeconds / 60.0;

        /// <summary>«1 234 мин» или «12 ч 05 мин (725 мин)» — для профиля и боковой панели.</summary>
        public static string FormatMinutes(double minutes, bool withHours = false)
        {
            long whole = (long)Math.Floor(minutes);
            var text = whole.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " мин";
            if (!withHours || whole < 60) return text;
            return (whole / 60) + " ч " + (whole % 60).ToString("00") + " мин  ·  " + text;
        }

        private static void Tick()
        {
            var now = Clock.Elapsed;
            double dt = (now - _last).TotalSeconds;
            _last = now;

            // после сна компьютера или зависания не засчитываем пропущенное время
            if (dt <= 0 || dt > 5) return;

            var user = AuthService.Current;
            var track = AudioService.Current;
            if (user == null || track == null || !AudioService.IsPlaying) { SaveIfDue(now); return; }

            var data = UserDataService.For(user.Id);
            data.ListenedSeconds += dt;
            data.TrackSeconds.TryGetValue(track.Id, out var seconds);
            data.TrackSeconds[track.Id] = seconds + dt;
            _dirty = true;

            if (_sessionTrack?.Id == track.Id && _sessionUserId == user.Id) _sessionSeconds += dt;

            int minute = (int)(data.ListenedSeconds / 60);
            if (minute != _lastWholeMinute)
            {
                _lastWholeMinute = minute;
                MinutesChanged?.Invoke();
            }

            SaveIfDue(now);
        }

        private static void OnTrackChanged(Track track)
        {
            CloseSession();
            _sessionTrack = track;
            _sessionUserId = AuthService.Current?.Id;
            _sessionSeconds = 0;
        }

        /// <summary>Трек начали слушать и почти сразу переключили — запоминаем пропуск.</summary>
        private static void CloseSession()
        {
            var track = _sessionTrack;
            if (track == null || _sessionUserId == null) return;
            if (_sessionSeconds < 1 || _sessionSeconds >= SkipSeconds) return;
            if (track.Duration > 0 && _sessionSeconds >= track.Duration * 0.5) return; // короткий трек дослушан

            var data = UserDataService.For(_sessionUserId);
            data.Skips.TryGetValue(track.Id, out var skips);
            data.Skips[track.Id] = skips + 1;
            _dirty = true;
        }

        private static void SaveIfDue(TimeSpan now)
        {
            if (!_dirty || now - _lastSave < SaveEvery) return;
            Flush();
        }

        /// <summary>Сохраняет накопленное — при выходе из аккаунта и закрытии программы.</summary>
        public static void Flush()
        {
            _lastSave = Clock.Elapsed;
            if (!_dirty) return;
            _dirty = false;
            try
            {
                var id = AuthService.Current?.Id;
                if (!string.IsNullOrEmpty(id)) UserDataService.Save(id, false);
                if (!string.IsNullOrEmpty(_sessionUserId) && _sessionUserId != id) UserDataService.Save(_sessionUserId, false);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("listening stats save failed: " + ex.Message);
            }
        }

        /// <summary>Перед сменой пользователя: дописываем статистику прежнего и начинаем заново.</summary>
        public static void EndUser()
        {
            CloseSession();
            Flush();
            _sessionTrack = null;
            _sessionUserId = null;
            _sessionSeconds = 0;
            _lastWholeMinute = -1;
        }
    }
}
