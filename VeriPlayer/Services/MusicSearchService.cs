using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Глобальный поиск открытой музыки, которую авторы разрешили слушать и скачивать:
    /// Openverse (Jamendo, ccMixter и др.) и Internet Archive. Ключи не нужны.
    /// </summary>
    public static partial class MusicSearchService
    {
        public sealed class Result
        {
            public string Id = "";
            public string Title = "";
            public string Artist = "";
            public string Album = "";
            public double Duration;
            public string StreamUrl = "";
            public string CoverUrl = "";
            public string Source = "";
            public string License = "";
            public string LicenseUrl = "";
            public string PageUrl = "";

            public string DurationText => Duration <= 0 ? "" : TimeSpan.FromSeconds(Duration).ToString(Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
            /// <summary>Строка для указания авторства — условие лицензий Creative Commons.</summary>
            public string Attribution => "«" + Title + "» — " + (Artist.Length > 0 ? Artist : "неизвестный автор") +
                                         (License.Length > 0 ? ", " + License : "") + " · " + Source;
        }

        internal static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("VeriPlayer/1.5 (+https://github.com/Verilunim/Veri-Player)");
            return http;
        }

        /// <summary>Ищет во всех источниках сразу; упавший источник не мешает остальным.</summary>
        public static async Task<(List<Result> results, List<string> failed)> SearchAsync(string query, CancellationToken ct)
        {
            query = (query ?? "").Trim();
            var failed = new List<string>();
            if (query.Length == 0) return (new List<Result>(), failed);

            var openverse = Guard("Openverse", () => OpenverseAsync(query, ct), failed);
            var archive = Guard("Internet Archive", () => ArchiveAsync(query, ct), failed);
            await Task.WhenAll(openverse, archive).ConfigureAwait(false);

            // чередуем источники, чтобы наверху были результаты из обоих; одинаковые треки убираем
            var a = openverse.Result;
            var b = archive.Result;
            var merged = new List<Result>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                foreach (var r in new[] { i < a.Count ? a[i] : null, i < b.Count ? b[i] : null })
                    if (r != null && r.StreamUrl.Length > 0 && seen.Add(r.Artist + "|" + r.Title)) merged.Add(r);
            }
            return (merged, failed);
        }

        private static async Task<List<Result>> Guard(string name, Func<Task<List<Result>>> run, List<string> failed)
        {
            try { return await run().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Diagnostics.Log("search " + name + ": " + ex.Message);
                lock (failed) failed.Add(name);
                return new List<Result>();
            }
        }

        // ================================================================== Openverse

        private static async Task<List<Result>> OpenverseAsync(string query, CancellationToken ct)
        {
            var url = "https://api.openverse.org/v1/audio/?category=music&page_size=30&q=" + Uri.EscapeDataString(query);
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            var list = new List<Result>();
            if (!doc.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array) return list;

            foreach (var r in items.EnumerateArray())
            {
                var type = Str(r, "filetype").ToLowerInvariant();
                // плеер играет MP3/WAV/FLAC/AAC; Ogg и прочее пропускаем
                if (type.Length > 0 && !(type.StartsWith("mp3") || type.StartsWith("mp32") || type == "wav" || type == "flac" || type == "m4a" || type == "aac"))
                    continue;
                var license = Str(r, "license").ToUpperInvariant();
                var version = Str(r, "license_version");
                list.Add(new Result
                {
                    Id = "ov_" + Str(r, "id"),
                    Title = Str(r, "title"),
                    Artist = Str(r, "creator"),
                    Duration = r.TryGetProperty("duration", out var d) && d.TryGetDouble(out var ms) ? ms / 1000.0 : 0,
                    StreamUrl = Str(r, "url"),
                    CoverUrl = Str(r, "thumbnail"),
                    Source = SourceName(Str(r, "source")),
                    License = license == "CC0" ? "CC0" : license == "PDM" ? "Public Domain" : ("CC " + license + " " + version).Trim(),
                    LicenseUrl = Str(r, "license_url"),
                    PageUrl = Str(r, "foreign_landing_url")
                });
            }
            return list;
        }

        private static string SourceName(string key) => key switch
        {
            "jamendo" => "Jamendo",
            "ccmixter" => "ccMixter",
            "freesound" => "Freesound",
            "wikimedia_audio" => "Wikimedia",
            _ => string.IsNullOrEmpty(key) ? "Openverse" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('_', ' '))
        };

        internal static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonDocument.Parse(text);
        }

        internal static string Str(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            // в Internet Archive поле бывает массивом строк
            if (v.ValueKind == JsonValueKind.Array)
                foreach (var x in v.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String) return x.GetString() ?? "";
            return "";
        }
    }
}
