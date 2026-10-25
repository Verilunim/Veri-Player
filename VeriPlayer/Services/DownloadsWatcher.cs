using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// После «Открыть на Bandcamp» полчаса следит за папкой «Загрузки»: купленный или бесплатный
    /// релиз (ZIP или аудиофайл) сам распаковывается в библиотеку и включается.
    /// Вне этого окна папка не читается.
    /// </summary>
    public static class DownloadsWatcher
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
        private static FileSystemWatcher _watcher;
        private static DispatcherTimer _stopTimer;
        private static readonly HashSet<string> Handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Сообщает в UI-потоке: сколько треков добавлено и какой включить.</summary>
        public static event Action<int, Track> Imported;

        public static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public static bool IsActive => _watcher != null;

        public static void Start()
        {
            var folder = DownloadsFolder;
            if (!Directory.Exists(folder)) return;

            if (_watcher == null)
            {
                _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
                // браузер сначала пишет временный файл и в конце переименовывает — ловим оба события
                _watcher.Created += (s, e) => Consider(e.FullPath);
                _watcher.Renamed += (s, e) => Consider(e.FullPath);
                _watcher.EnableRaisingEvents = true;
            }

            _stopTimer ??= new DispatcherTimer { Interval = Window };
            _stopTimer.Tick -= OnStop;
            _stopTimer.Tick += OnStop;
            _stopTimer.Stop();
            _stopTimer.Start();
        }

        private static void OnStop(object sender, EventArgs e) => Stop();

        public static void Stop()
        {
            _stopTimer?.Stop();
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private static bool IsAudio(string path) => LibraryService.IsSupported(path);
        private static bool IsZip(string path) => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

        private static void Consider(string path)
        {
            if (!IsAudio(path) && !IsZip(path)) return;
            lock (Handled) if (!Handled.Add(path)) return;
            _ = Task.Run(() => ImportAsync(path));
        }

        private static async Task ImportAsync(string path)
        {
            try
            {
                // ждём, пока браузер допишет файл и отпустит его
                for (int i = 0; i < 120 && !IsReady(path); i++) await Task.Delay(1000);
                if (!IsReady(path)) return;

                var files = IsZip(path) ? ExtractAudio(path) : new List<string> { path };
                if (files.Count == 0) return;

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                await dispatcher.InvokeAsync(async () =>
                {
                    int before = LibraryService.Tracks.Count;
                    var result = await LibraryService.ImportAsync(files, null);
                    var first = LibraryService.Tracks.Skip(before).FirstOrDefault()
                                ?? LibraryService.Tracks.FirstOrDefault(t => files.Contains(t.FilePath, StringComparer.OrdinalIgnoreCase));
                    Imported?.Invoke(result.Added, first);
                }).Task.Unwrap();
            }
            catch (Exception ex)
            {
                Diagnostics.Log("downloads import " + Path.GetFileName(path) + ": " + ex.Message);
            }
        }

        private static bool IsReady(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return stream.Length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Распаковывает из ZIP только аудиофайлы, в отдельную папку альбома. Пути внутри архива
        /// не используются (только имя файла), поэтому записать что-то вне папки нельзя.
        /// </summary>
        private static List<string> ExtractAudio(string zipPath)
        {
            var album = string.Join("_", Path.GetFileNameWithoutExtension(zipPath).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var target = Path.Combine(Storage.CopiedMusicDir, album.Length > 0 ? album : "Bandcamp");
            Directory.CreateDirectory(target);

            var files = new List<string>();
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                var name = Path.GetFileName(entry.FullName);
                if (name.Length == 0 || !IsAudio(name)) continue;
                var destination = Path.Combine(target, name);
                if (!File.Exists(destination)) entry.ExtractToFile(destination);
                files.Add(destination);
            }
            return files;
        }
    }
}
