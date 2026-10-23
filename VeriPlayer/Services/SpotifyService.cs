using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Подключение Spotify-аккаунта через OAuth 2.0 PKCE (без секрета приложения,
    /// как и положено для настольных программ). Playback Spotify в нативной
    /// программе невозможен — треки открываются в самом Spotify.
    /// </summary>
    public static class SpotifyService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private const string Scopes = "user-read-private user-read-email playlist-read-private playlist-read-collaborative user-library-read";

        public static SpotifyToken Token { get; private set; } = new SpotifyToken();
        public static bool IsConnected => Token != null && !string.IsNullOrWhiteSpace(Token.RefreshToken);
        public static string UserName => Token?.UserName ?? "";
        public static event Action Changed;

        public static string RedirectUri(int port) => "http://127.0.0.1:" + port + "/callback";

        public static void Init()
        {
            Token = Storage.Load(Storage.SpotifyFile, new SpotifyToken()) ?? new SpotifyToken();
        }

        public static void Disconnect()
        {
            Token = new SpotifyToken();
            Storage.Save(Storage.SpotifyFile, Token);
            Changed?.Invoke();
        }

        public static void Save()
        {
            Storage.Save(Storage.SpotifyFile, Token);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ connection

        public static async Task<(bool ok, string error)> ConnectAsync(string clientId, int port, IProgress<string> status)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return (false, "Укажите Client ID приложения Spotify (см. инструкцию ниже).");
            if (port < 1024 || port > 65000)
                return (false, "Порт должен быть в диапазоне 1024–65000.");

            Result = null; // сбрасываем результат прошлой попытки
            var verifier = RandomString(64);
            var challenge = Base64Url(Sha256(Encoding.ASCII.GetBytes(verifier)));
            var state = RandomString(16);
            var redirect = RedirectUri(port);

            var url = "https://accounts.spotify.com/authorize?" +
                      "response_type=code" +
                      "&client_id=" + Uri.EscapeDataString(clientId.Trim()) +
                      "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                      "&scope=" + Uri.EscapeDataString(Scopes) +
                      "&code_challenge_method=S256" +
                      "&code_challenge=" + Uri.EscapeDataString(challenge) +
                      "&state=" + Uri.EscapeDataString(state) +
                      "&show_dialog=true";

            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception ex)
            {
                return (false, "Не удалось занять порт " + port + ": " + ex.Message + ". Укажите другой порт.");
            }

            try
            {
                status?.Report("Открываю браузер для входа в Spotify…");
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { return (false, "Не удалось открыть браузер. Ссылка: " + url); }

                status?.Report("Ожидаю подтверждение в браузере… (окно можно закрыть после входа)");

                var (code, error, gotState) = await WaitForCallback(listener, TimeSpan.FromMinutes(5)).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(error))
                    return (false, "Spotify вернул ошибку: " + error);
                if (string.IsNullOrEmpty(code))
                    return (false, "Не дождался ответа от Spotify. Попробуйте снова.");
                if (gotState != state)
                    return (false, "Несовпадение state — запрос отклонён в целях безопасности.");

                status?.Report("Получаю токен доступа…");
                var token = await ExchangeCodeAsync(clientId.Trim(), code, verifier, redirect).ConfigureAwait(false);
                if (token == null)
                    return (false, "Не удалось получить токен. Проверьте, что Redirect URI в приложении Spotify точно: " + redirect);

                Token = token;
                var profile = await GetProfileAsync().ConfigureAwait(false);
                if (profile != null)
                {
                    Token.UserName = profile.display_name ?? profile.id;
                    Token.UserId = profile.id;
                }
                Save();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, "Ошибка подключения: " + ex.Message);
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        private static async Task<(string code, string error, string state)> WaitForCallback(TcpListener listener, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return (null, null, null); }
                catch { return (null, null, null); }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        using (var stream = client.GetStream())
                        {
                            var buffer = new byte[8192];
                            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                            var request = Encoding.ASCII.GetString(buffer, 0, read);
                            var firstLine = request.Split('\n').FirstOrDefault() ?? "";
                            var parts = firstLine.Split(' ');
                            var target = parts.Length > 1 ? parts[1] : "/";

                            var query = ParseQuery(target);
                            query.TryGetValue("code", out var code);
                            query.TryGetValue("error", out var error);
                            query.TryGetValue("state", out var state);

                            if (query.Count > 0 && (code != null || error != null))
                            {
                                Result = (code, error, state);
                            }

                            var html = BuildResponsePage(code != null && error == null);
                            var body = Encoding.UTF8.GetBytes(html);
                            var head = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                                       "Content-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                            await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
                            await stream.WriteAsync(body).ConfigureAwait(false);
                            await stream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    catch { }
                });

                // ждём заполнения результата
                var spin = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                while (Result == null && DateTime.UtcNow < spin)
                    await Task.Delay(60).ConfigureAwait(false);

                if (Result != null) return Result.Value;
            }

            return (null, null, null);
        }

        private static (string code, string error, string state)? Result;

        private static Dictionary<string, string> ParseQuery(string target)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var qIndex = target.IndexOf('?');
            if (qIndex < 0) return dict;
            var query = target.Substring(qIndex + 1);
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                var kv = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(kv[0]);
                var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace("+", " ")) : "";
                dict[key] = value;
            }
            return dict;
        }

        private static string BuildResponsePage(bool success)
        {
            var title = success ? "Veri Player — Spotify подключён" : "Veri Player — не удалось";
            var text = success
                ? "Аккаунт Spotify успешно подключён. Можно закрыть это окно и вернуться в Veri Player."
                : "Подключение не удалось. Попробуйте ещё раз из Veri Player.";
            return "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>" + title + "</title>" +
                   "<style>body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;" +
                   "background:radial-gradient(circle at 30% 20%,#2a1b4d,#0b0b12 60%);color:#f3f0ff;" +
                   "font-family:'Segoe UI',sans-serif}div{max-width:520px;padding:44px;border-radius:24px;" +
                   "background:rgba(255,255,255,.06);border:1px solid rgba(255,255,255,.12);text-align:center}" +
                   "h1{font-size:22px;margin:0 0 12px}p{margin:0;opacity:.8;line-height:1.6}</style></head>" +
                   "<body><div><h1>" + title + "</h1><p>" + text + "</p></div></body></html>";
        }

        private static async Task<SpotifyToken> ExchangeCodeAsync(string clientId, string code, string verifier, string redirect)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirect,
                ["code_verifier"] = verifier
            });

            var response = await Http.PostAsync("https://accounts.spotify.com/api/token", form).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return ParseToken(text, null);
        }

        private static SpotifyToken ParseToken(string json, string previousRefresh)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var token = new SpotifyToken
            {
                RefreshToken = previousRefresh ?? ""
            };
            if (root.TryGetProperty("access_token", out var at)) token.AccessToken = at.GetString() ?? "";
            if (root.TryGetProperty("refresh_token", out var rt)) token.RefreshToken = rt.GetString() ?? token.RefreshToken;
            if (root.TryGetProperty("scope", out var sc)) token.Scope = sc.GetString() ?? "";
            double expires = 3600;
            if (root.TryGetProperty("expires_in", out var ex) && ex.TryGetDouble(out var seconds)) expires = seconds;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(expires - 30);
            token.UserName = Token?.UserName ?? "";
            token.UserId = Token?.UserId ?? "";
            return token;
        }

        private static async Task<bool> RefreshAsync(string clientId)
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(clientId)) return false;
            try
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = Token.RefreshToken
                });
                var response = await Http.PostAsync("https://accounts.spotify.com/api/token", form).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var refreshed = ParseToken(text, Token.RefreshToken);
                refreshed.UserName = Token.UserName;
                refreshed.UserId = Token.UserId;
                Token = refreshed;
                Storage.Save(Storage.SpotifyFile, Token);
                return true;
            }
            catch { return false; }
        }

        private static async Task<string> AccessTokenAsync()
        {
            if (!IsConnected) return null;
            if (DateTime.UtcNow >= Token.ExpiresAt)
            {
                var ok = await RefreshAsync(App.Settings.SpotifyClientId).ConfigureAwait(false);
                if (!ok) return null;
            }
            return Token.AccessToken;
        }

        // ------------------------------------------------------------------ api

        public sealed class Profile
        {
            public string id { get; set; }
            public string display_name { get; set; }
            public string email { get; set; }
            public string product { get; set; }
            public int followers { get; set; }
            public string image { get; set; }
            public string country { get; set; }
        }

        public static async Task<Profile> GetProfileAsync()
        {
            try
            {
                var token = await AccessTokenAsync().ConfigureAwait(false);
                if (token == null) return null;

                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
                request.Headers.Add("Authorization", "Bearer " + token);
                var response = await Http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var profile = new Profile
                {
                    id = GetString(root, "id"),
                    display_name = GetString(root, "display_name"),
                    email = GetString(root, "email"),
                    product = GetString(root, "product"),
                    country = GetString(root, "country"),
                    followers = root.TryGetProperty("followers", out var f) && f.TryGetProperty("total", out var t) ? t.GetInt32() : 0,
                    image = FirstImage(root)
                };
                return profile;
            }
            catch { return null; }
        }

        public static async Task<List<SpotifyPlaylistInfo>> GetPlaylistsAsync(int limit = 40)
        {
            var list = new List<SpotifyPlaylistInfo>();
            try
            {
                var token = await AccessTokenAsync().ConfigureAwait(false);
                if (token == null) return list;

                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/playlists?limit=" + limit);
                request.Headers.Add("Authorization", "Bearer " + token);
                var response = await Http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items)) return list;

                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new SpotifyPlaylistInfo
                    {
                        Id = GetString(item, "id"),
                        Name = GetString(item, "name"),
                        Owner = item.TryGetProperty("owner", out var ow) ? GetString(ow, "display_name") : "",
                        TrackCount = item.TryGetProperty("tracks", out var tr) && tr.TryGetProperty("total", out var tt) ? tt.GetInt32() : 0,
                        ImageUrl = FirstImage(item),
                        Url = item.TryGetProperty("external_urls", out var eu) ? GetString(eu, "spotify") : ""
                    });
                }
            }
            catch { }
            return list;
        }

        public static async Task<List<SpotifyTrackInfo>> GetPlaylistTracksAsync(string playlistId, int limit = 100)
        {
            var list = new List<SpotifyTrackInfo>();
            try
            {
                var token = await AccessTokenAsync().ConfigureAwait(false);
                if (token == null) return list;

                var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://api.spotify.com/v1/playlists/" + Uri.EscapeDataString(playlistId ?? "") + "/tracks?limit=" + limit);
                request.Headers.Add("Authorization", "Bearer " + token);
                var response = await Http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items)) return list;

                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("track", out var track) || track.ValueKind != JsonValueKind.Object) continue;
                    list.Add(ParseTrack(track));
                }
            }
            catch { }
            return list;
        }

        public static async Task<List<SpotifyTrackInfo>> GetLikedAsync(int limit = 50)
        {
            var list = new List<SpotifyTrackInfo>();
            try
            {
                var token = await AccessTokenAsync().ConfigureAwait(false);
                if (token == null) return list;

                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/tracks?limit=" + limit);
                request.Headers.Add("Authorization", "Bearer " + token);
                var response = await Http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items)) return list;

                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("track", out var track) || track.ValueKind != JsonValueKind.Object) continue;
                    list.Add(ParseTrack(track));
                }
            }
            catch { }
            return list;
        }

        private static SpotifyTrackInfo ParseTrack(JsonElement track)
        {
            var info = new SpotifyTrackInfo
            {
                Name = GetString(track, "name"),
                Album = track.TryGetProperty("album", out var al) ? GetString(al, "name") : "",
                Url = track.TryGetProperty("external_urls", out var eu) ? GetString(eu, "spotify") : "",
                ImageUrl = track.TryGetProperty("album", out var al2) ? FirstImage(al2) : "",
                DurationMs = track.TryGetProperty("duration_ms", out var d) && d.TryGetInt32(out var ms) ? ms : 0
            };

            if (track.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
            {
                info.Artists = string.Join(", ", artists.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.Object)
                    .Select(a => GetString(a, "name"))
                    .Where(n => !string.IsNullOrWhiteSpace(n)));
            }
            return info;
        }

        private static string GetString(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object) return "";
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static string FirstImage(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return "";
            if (!element.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return "";
            foreach (var img in images.EnumerateArray())
            {
                var url = GetString(img, "url");
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
            return "";
        }

        public static void OpenExternal(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // ------------------------------------------------------------------ helpers

        private static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(length);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        private static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
