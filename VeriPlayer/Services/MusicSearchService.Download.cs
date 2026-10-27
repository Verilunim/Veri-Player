using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    public static partial class MusicSearchService
    {
        public const string StreamSource = "stream";

        /// <summary>Трек уже скачан раньше? Ищем по странице источника.</summary>
        public static Track FindDownloaded(Result r) =>
            LibraryService.Tracks.FirstOrDefault(t => !t.IsRemote && t.Exists &&
                                                      string.Equals(t.WebUrl, r.PageUrl, StringComparison.OrdinalIgnoreCase) &&
                                                      string.Equals(t.Title, r.Title, StringComparison.OrdinalIgnoreCase));

        /// <summary>Трек для прослушивания без скачивания: плеер читает его прямо по ссылке.</summary>
        public static Track ToStreamTrack(Result r) => new Track
        {
            Id = "web_" + Math.Abs(r.Id.GetHashCode()).ToString("x"),
            Source = StreamSource,
            FilePath = r.StreamUrl,
            Title = r.Title,
            Artist = r.Artist,
            Album = r.Album.Length > 0 ? r.Album : r.Source,
            Duration = r.Duration,
            WebUrl = r.PageUrl,
            ArtworkUrl = r.CoverUrl,
            License = r.Attribution,
            AddedByName = r.Source
        };

        /// <summary>
        /// Скачивает трек в папку программы, добавляет в библиотеку с обложкой и автором.
        /// Повторное скачивание того же трека не создаёт копию.
        /// </summary>
        public static async Task<Track> DownloadAsync(Result r, IProgress<double> progress, CancellationToken ct)
        {
            var existing = FindDownloaded(r);
            if (existing != null) return existing;

            var ext = Path.GetExtension(new Uri(r.StreamUrl).AbsolutePath).ToLowerInvariant();
            if (!LibraryService.SupportedExtensions.Contains(ext)) ext = ".mp3"; // у Jamendo ссылка без расширения
            var safe = string.Join("_", ((r.Artist.Length > 0 ? r.Artist + " - " : "") + r.Title)
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (safe.Length > 120) safe = safe.Substring(0, 120);
            var target = Path.Combine(Storage.CopiedMusicDir, (safe.Length > 0 ? safe : "track") + ext);
            for (int n = 1; File.Exists(target); n++)
                target = Path.Combine(Storage.CopiedMusicDir, safe + "_" + n + ext);

            var temp = target + ".part";
            try
            {
                using (var response = await Http.GetAsync(r.StreamUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    long? total = response.Content.Headers.ContentLength;
                    using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var output = File.Create(temp);
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        done += read;
                        if (total > 0) progress?.Report((double)done / total.Value);
                    }
                }
                File.Move(temp, target);
            }
            catch
            {
                Storage.SafeDelete(temp);
                throw;
            }

            string cover = null;
            if (r.CoverUrl.Length > 0)
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(r.CoverUrl, ct).ConfigureAwait(false);
                    cover = Path.Combine(Path.GetTempPath(), "vp_cover_" + Guid.NewGuid().ToString("N") + ".jpg");
                    await File.WriteAllBytesAsync(cover, bytes, ct).ConfigureAwait(false);
                }
                catch { cover = null; }
            }

            // в библиотеку добавляем в UI-потоке: на неё подписан интерфейс
            var app = System.Windows.Application.Current;
            Track track = null;
            void Add()
            {
                track = LibraryService.ImportSingle(target, r.Title);
                if (track == null) return;
                if (r.Artist.Length > 0) track.Artist = r.Artist;
                if (r.Album.Length > 0) track.Album = r.Album;
                if (track.Duration <= 0) track.Duration = r.Duration;
                track.WebUrl = r.PageUrl;
                track.License = r.Attribution;
                if (cover != null && !track.HasCover) LibraryService.SetCover(track, cover);
                LibraryService.Update(track);
            }
            if (app != null) app.Dispatcher.Invoke(Add); else Add();
            if (cover != null) Storage.SafeDelete(cover);

            if (track == null) throw new InvalidOperationException("Файл скачан, но не читается как музыка.");
            return track;
        }
    }
}
