using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VeriPlayer.Services
{
    public static partial class MusicSearchService
    {
        /// <summary>
        /// Internet Archive: ищем только аудио с MP3 и указанной лицензией, затем раскрываем
        /// первые записи в отдельные треки (у альбома их несколько).
        /// </summary>
        private static async Task<List<Result>> ArchiveAsync(string query, CancellationToken ct)
        {
            var q = "(" + query + ") AND mediatype:audio AND format:\"VBR MP3\" AND licenseurl:[* TO *]";
            var url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(q) +
                      "&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=licenseurl&rows=8&output=json";
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);

            var items = new List<(string id, string title, string creator, string license)>();
            if (doc.RootElement.TryGetProperty("response", out var resp) && resp.TryGetProperty("docs", out var docs))
                foreach (var d in docs.EnumerateArray())
                    items.Add((Str(d, "identifier"), Str(d, "title"), Str(d, "creator"), Str(d, "licenseurl")));

            var tasks = items.Where(i => i.id.Length > 0)
                             .Select(i => ArchiveItemAsync(i.id, i.title, i.creator, i.license, ct))
                             .ToList();
            var lists = await Task.WhenAll(tasks).ConfigureAwait(false);

            // по паре треков с каждой записи по очереди, чтобы один длинный альбом не занял всё
            var result = new List<Result>();
            for (int i = 0; i < 4; i++)
                foreach (var l in lists)
                    if (i < l.Count) result.Add(l[i]);
            return result;
        }

        private static async Task<List<Result>> ArchiveItemAsync(string id, string album, string creator, string licenseUrl, CancellationToken ct)
        {
            var list = new List<Result>();
            try
            {
                using var doc = await GetJsonAsync("https://archive.org/metadata/" + Uri.EscapeDataString(id), ct).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("files", out var files)) return list;

                foreach (var f in files.EnumerateArray())
                {
                    if (Str(f, "format") != "VBR MP3") continue;
                    var name = Str(f, "name");
                    var title = Str(f, "title");
                    if (title.Length == 0) title = System.IO.Path.GetFileNameWithoutExtension(name);
                    var artist = Str(f, "creator");
                    list.Add(new Result
                    {
                        Id = "ia_" + id + "/" + name,
                        Title = title,
                        Artist = artist.Length > 0 ? artist : creator,
                        Album = album,
                        Duration = ParseLength(Str(f, "length")),
                        StreamUrl = "https://archive.org/download/" + Uri.EscapeDataString(id) + "/" + EscapePath(name),
                        CoverUrl = "https://archive.org/services/img/" + Uri.EscapeDataString(id),
                        Source = "Internet Archive",
                        License = LicenseName(licenseUrl),
                        LicenseUrl = licenseUrl,
                        PageUrl = "https://archive.org/details/" + Uri.EscapeDataString(id)
                    });
                    if (list.Count >= 12) break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Diagnostics.Log("search archive item " + id + ": " + ex.Message); }
            return list;
        }

        private static string EscapePath(string path) =>
            string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

        /// <summary>«214.5» или «3:34» / «1:02:10» → секунды.</summary>
        internal static double ParseLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
            double total = 0;
            foreach (var part in value.Split(':'))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return 0;
                total = total * 60 + n;
            }
            return total;
        }

        /// <summary>Ссылка лицензии → короткое имя: «CC BY-NC-SA 4.0», «CC0», «Public Domain».</summary>
        internal static string LicenseName(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var u = url.ToLowerInvariant();
            if (u.Contains("publicdomain/zero")) return "CC0";
            if (u.Contains("publicdomain")) return "Public Domain";
            var marker = "/licenses/";
            int i = u.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return "";
            var parts = u.Substring(i + marker.Length).Trim('/').Split('/');
            return ("CC " + parts[0].ToUpperInvariant() + (parts.Length > 1 ? " " + parts[1] : "")).Trim();
        }
    }
}
