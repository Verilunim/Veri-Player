using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    public static partial class SoundCloudService
    {
        public sealed class PlaylistInfo
        {
            public string Urn = "";
            public string Title = "";
            public int TrackCount;
            public string WebUrl = "";
            public string Line => Title + "  ·  " + TrackCount + " тр.";
        }

        public static Task<List<Track>> MyTracksAsync() => TracksAsync("/me/tracks?limit=50&linked_partitioning=true");
        public static Task<List<Track>> LikesAsync() => TracksAsync("/me/likes/tracks?limit=50&linked_partitioning=true");
        public static Task<List<Track>> PlaylistTracksAsync(string urn) =>
            TracksAsync("/playlists/" + Uri.EscapeDataString(urn) + "/tracks?limit=100&linked_partitioning=true");

        public static async Task<List<PlaylistInfo>> PlaylistsAsync()
        {
            var list = new List<PlaylistInfo>();
            using var doc = await GetJsonAsync("/me/playlists?limit=50&show_tracks=false&linked_partitioning=true").ConfigureAwait(false);
            foreach (var item in Items(doc.RootElement))
            {
                list.Add(new PlaylistInfo
                {
                    Urn = Str(item, "urn"),
                    Title = Str(item, "title"),
                    TrackCount = item.TryGetProperty("track_count", out var c) && c.TryGetInt32(out var n) ? n : 0,
                    WebUrl = Str(item, "permalink_url")
                });
            }
            return list;
        }

        /// <summary>Собирает до ~200 треков, проходя по страницам next_href.</summary>
        private static async Task<List<Track>> TracksAsync(string path)
        {
            var tracks = new List<Track>();
            string next = path;
            for (int page = 0; page < 4 && !string.IsNullOrEmpty(next); page++)
            {
                using var doc = await GetJsonAsync(next).ConfigureAwait(false);
                foreach (var item in Items(doc.RootElement))
                {
                    var t = ToTrack(item);
                    if (t != null) tracks.Add(t);
                }
                next = Str(doc.RootElement, "next_href");
            }
            return tracks;
        }

        private static IEnumerable<JsonElement> Items(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToList();
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("collection", out var c) && c.ValueKind == JsonValueKind.Array)
                return c.EnumerateArray().ToList();
            return Enumerable.Empty<JsonElement>();
        }

        private static Track ToTrack(JsonElement item)
        {
            // лайки иногда приходят обёрткой { "track": {...} }
            if (item.TryGetProperty("track", out var inner) && inner.ValueKind == JsonValueKind.Object) item = inner;
            var urn = Str(item, "urn");
            if (urn.Length == 0) return null;

            var artist = item.TryGetProperty("user", out var user) ? Str(user, "username") : "";
            double seconds = item.TryGetProperty("duration", out var d) && d.TryGetDouble(out var ms) ? ms / 1000.0 : 0;
            var artwork = Str(item, "artwork_url");
            if (artwork.Length == 0 && item.TryGetProperty("user", out var u2)) artwork = Str(u2, "avatar_url");

            return new Track
            {
                // постоянный Id: избранное и «недавние» узнают тот же трек в следующий раз
                Id = "sc_" + urn.Replace(':', '_'),
                Source = SourceKey,
                RemoteId = urn,
                Title = Str(item, "title"),
                Artist = artist,
                Album = "SoundCloud",
                Genre = Str(item, "genre"),
                Duration = seconds,
                WebUrl = Str(item, "permalink_url"),
                ArtworkUrl = artwork.Replace("-large.", "-t500x500."),
                AddedByName = "SoundCloud",
                CoverPath = CachedCover(urn)
            };
        }

        // ================================================================== поток и обложки

        /// <summary>
        /// Прямая ссылка на MP3-поток для плеера. Если SoundCloud отдаёт только HLS
        /// (его NAudio не играет) — понятная ошибка, трек можно открыть на сайте.
        /// </summary>
        public static async Task<string> ResolveStreamAsync(Track track)
        {
            using var doc = await GetJsonAsync("/tracks/" + Uri.EscapeDataString(track.RemoteId) + "/streams").ConfigureAwait(false);
            var mp3 = Str(doc.RootElement, "http_mp3_128_url");
            if (mp3.Length == 0)
                throw new InvalidOperationException("Этот трек SoundCloud отдаёт только в формате, который плеер не поддерживает. Откройте его на сайте SoundCloud.");

            var token = await AccessTokenAsync().ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, mp3);
            request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + token);
            using var response = await NoRedirect.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            if (response.Headers.Location != null) return response.Headers.Location.ToString();
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
            {
                using var j = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                var u = Str(j.RootElement, "url");
                if (u.Length > 0) return u;
            }
            if (response.IsSuccessStatusCode) return mp3;
            throw new InvalidOperationException("SoundCloud не дал поток (" + (int)response.StatusCode + ").");
        }

        /// <summary>Для плеера: синхронно, с ограничением по времени.</summary>
        public static string ResolveStream(Track track)
        {
            var task = Task.Run(() => ResolveStreamAsync(track));
            if (!task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("SoundCloud не ответил вовремя.");
            return task.Result;
        }

        private static string CachedCover(string urn)
        {
            var path = Path.Combine(Storage.CoversDir, "sc_" + urn.Replace(':', '_') + ".jpg");
            return System.IO.File.Exists(path) ? path : "";
        }

        /// <summary>Скачивает обложку один раз и кладёт рядом с остальными.</summary>
        public static async Task EnsureCoverAsync(Track track)
        {
            if (track == null || track.HasCover || string.IsNullOrWhiteSpace(track.ArtworkUrl)) return;
            var path = Path.Combine(Storage.CoversDir, "sc_" + track.RemoteId.Replace(':', '_') + ".jpg");
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    var bytes = await Http.GetByteArrayAsync(track.ArtworkUrl).ConfigureAwait(false);
                    await System.IO.File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                }
                track.CoverPath = path;
            }
            catch { }
        }
    }
}
