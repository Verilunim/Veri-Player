using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VeriPlayer.Services
{
    public static partial class YouTubeMusicService
    {
        /// <summary>Плейлист или трек YouTube Music; Url открывает его в music.youtube.com.</summary>
        public sealed class Item
        {
            public string Id = "";
            public string Title = "";
            public string Subtitle = "";
            public string Url = "";
        }

        public static async Task<List<Item>> PlaylistsAsync()
        {
            var list = new List<Item>();
            string page = "";
            for (int i = 0; i < 4; i++)
            {
                using var doc = await GetJsonAsync("/playlists?part=snippet,contentDetails&mine=true&maxResults=50" + page).ConfigureAwait(false);
                foreach (var p in Items(doc.RootElement))
                {
                    var id = SoundCloudService.Str(p, "id");
                    var title = p.TryGetProperty("snippet", out var sn) ? SoundCloudService.Str(sn, "title") : "";
                    int count = p.TryGetProperty("contentDetails", out var cd) && cd.TryGetProperty("itemCount", out var c) && c.TryGetInt32(out var n) ? n : 0;
                    list.Add(new Item
                    {
                        Id = id,
                        Title = title,
                        Subtitle = count + " тр.",
                        Url = "https://music.youtube.com/playlist?list=" + Uri.EscapeDataString(id)
                    });
                }
                var next = SoundCloudService.Str(doc.RootElement, "nextPageToken");
                if (next.Length == 0) break;
                page = "&pageToken=" + Uri.EscapeDataString(next);
            }
            return list;
        }

        public static async Task<List<Item>> PlaylistItemsAsync(string playlistId)
        {
            var list = new List<Item>();
            string page = "";
            for (int i = 0; i < 4; i++)
            {
                using var doc = await GetJsonAsync("/playlistItems?part=snippet,contentDetails&maxResults=50&playlistId=" +
                                                   Uri.EscapeDataString(playlistId) + page).ConfigureAwait(false);
                foreach (var it in Items(doc.RootElement))
                {
                    if (!it.TryGetProperty("snippet", out var sn)) continue;
                    var videoId = it.TryGetProperty("contentDetails", out var cd) ? SoundCloudService.Str(cd, "videoId") : "";
                    if (videoId.Length == 0) continue;
                    list.Add(new Item
                    {
                        Id = videoId,
                        Title = SoundCloudService.Str(sn, "title"),
                        // у музыкальных каналов YouTube добавляет « - Topic»
                        Subtitle = SoundCloudService.Str(sn, "videoOwnerChannelTitle").Replace(" - Topic", ""),
                        Url = "https://music.youtube.com/watch?v=" + Uri.EscapeDataString(videoId) +
                              "&list=" + Uri.EscapeDataString(playlistId)
                    });
                }
                var next = SoundCloudService.Str(doc.RootElement, "nextPageToken");
                if (next.Length == 0) break;
                page = "&pageToken=" + Uri.EscapeDataString(next);
            }
            return list;
        }
    }
}
