using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VeriPlayer.Services
{
    public static partial class MusicSearchService
    {
        /// <summary>
        /// Bandcamp: поиск тем же адресом, что использует их сайт (официального API нет, может измениться).
        /// Слушать и скачивать — на самом Bandcamp: купить или забрать бесплатный релиз;
        /// скачанный файл подхватит <see cref="DownloadsWatcher"/>.
        /// </summary>
        private static async Task<List<Result>> BandcampAsync(string query, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new { search_text = query, search_filter = "", full_page = false, fan_id = (string)null });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync("https://bandcamp.com/api/bcsearch_public_api/1/autocomplete_elastic", content, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));

            var list = new List<Result>();
            if (!doc.RootElement.TryGetProperty("auto", out var auto) || !auto.TryGetProperty("results", out var results)) return list;

            foreach (var r in results.EnumerateArray())
            {
                var type = Str(r, "type");
                var kind = type == "a" ? "Альбом" : type == "t" ? "Трек" : type == "b" ? "Исполнитель" : "";
                if (kind.Length == 0) continue;

                var url = Str(r, "item_url_path");
                if (url.Length == 0) url = Str(r, "item_url_root");
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;

                list.Add(new Result
                {
                    Id = "bc_" + type + "_" + Str(r, "id"),
                    Title = Str(r, "name"),
                    Artist = type == "b" ? Str(r, "location") : Str(r, "band_name"),
                    Album = Str(r, "album_name"),
                    CoverUrl = Str(r, "img"),
                    Source = "Bandcamp",
                    PageUrl = url,
                    OnSiteOnly = true,
                    Kind = kind
                });
                if (list.Count >= 20) break;
            }
            return list;
        }
    }
}
