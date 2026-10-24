using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VeriPlayer.Services
{
    /// <summary>
    /// YouTube Music через официальный YouTube Data API: вход Google (PKCE, только чтение),
    /// ваши плейлисты и понравившееся. Звук отдельно от плеера YouTube правила запрещают,
    /// поэтому треки открываются в YouTube Music.
    /// </summary>
    public static partial class YouTubeMusicService
    {
        private const string Api = "https://www.googleapis.com/youtube/v3";
        private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim RefreshGate = new SemaphoreSlim(1, 1);

        public static string File => Path.Combine(Storage.Root, "youtube.json");
        public static ServiceAccount Account { get; private set; } = new ServiceAccount { Port = 8898 };
        public static bool IsConnected => Account.IsConnected;
        /// <summary>Плейлист «Понравившиеся» у YouTube всегда «LL».</summary>
        public const string LikesPlaylistId = "LL";
        public static event Action Changed;

        public static void Init()
        {
            Account = Storage.LoadProtected(File, new ServiceAccount { Port = 8898 }) ?? new ServiceAccount { Port = 8898 };
            if (Account.Port <= 0) Account.Port = 8898;
        }

        private static void Save()
        {
            Storage.SaveProtected(File, Account);
            Changed?.Invoke();
        }

        public static void Disconnect()
        {
            Account = new ServiceAccount { ClientId = Account.ClientId, ClientSecret = Account.ClientSecret, Port = Account.Port };
            Save();
        }

        public static async Task<(bool ok, string error)> ConnectAsync(string clientId, string clientSecret, int port)
        {
            clientId = (clientId ?? "").Trim();
            clientSecret = (clientSecret ?? "").Trim();
            if (clientId.Length == 0 || clientSecret.Length == 0)
                return (false, "Укажите Client ID и Client Secret OAuth-клиента Google (тип «Desktop app»).");

            var pkce = new OAuthLoopback.Pkce();
            var redirect = OAuthLoopback.RedirectUri(port);
            var url = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Uri.EscapeDataString(clientId) +
                      "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                      "&response_type=code&scope=" + Uri.EscapeDataString(Scope) +
                      "&access_type=offline&prompt=consent&code_challenge_method=S256" +
                      "&code_challenge=" + Uri.EscapeDataString(pkce.Challenge) +
                      "&state=" + Uri.EscapeDataString(pkce.State);

            var (code, error) = await OAuthLoopback.AuthorizeAsync(url, port, pkce.State, "YouTube", TimeSpan.FromMinutes(5))
                .ConfigureAwait(false);
            if (error != null) return (false, error);

            var account = new ServiceAccount { ClientId = clientId, ClientSecret = clientSecret, Port = port };
            var tokenError = await RequestTokenAsync(account, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirect,
                ["code_verifier"] = pkce.Verifier,
                ["code"] = code
            }).ConfigureAwait(false);
            if (tokenError != null) return (false, tokenError);

            Account = account;
            try
            {
                using var me = await GetJsonAsync("/channels?part=snippet&mine=true").ConfigureAwait(false);
                foreach (var ch in Items(me.RootElement))
                {
                    Account.UserId = SoundCloudService.Str(ch, "id");
                    if (ch.TryGetProperty("snippet", out var sn)) Account.UserName = SoundCloudService.Str(sn, "title");
                    break;
                }
            }
            catch { }
            if (string.IsNullOrEmpty(Account.UserName)) Account.UserName = "аккаунт Google";
            Save();
            return (true, null);
        }

        private static async Task<string> RequestTokenAsync(ServiceAccount account, Dictionary<string, string> form)
        {
            try
            {
                var response = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form)).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return "Google не выдал токен (" + (int)response.StatusCode + "). Проверьте Client ID, Secret и что включён YouTube Data API v3.";

                using var doc = JsonDocument.Parse(text);
                account.AccessToken = SoundCloudService.Str(doc.RootElement, "access_token");
                var refresh = SoundCloudService.Str(doc.RootElement, "refresh_token");
                if (refresh.Length > 0) account.RefreshToken = refresh;
                double expires = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetDouble(out var s) ? s : 3600;
                account.ExpiresAt = DateTime.UtcNow.AddSeconds(expires - 60);
                return account.AccessToken.Length > 0 ? null : "Пустой ответ Google.";
            }
            catch (Exception ex)
            {
                return "Ошибка связи с Google: " + ex.Message;
            }
        }

        private static async Task<string> AccessTokenAsync()
        {
            if (!IsConnected) throw new InvalidOperationException("YouTube не подключён.");
            if (DateTime.UtcNow < Account.ExpiresAt && Account.AccessToken.Length > 0) return Account.AccessToken;

            await RefreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow < Account.ExpiresAt && Account.AccessToken.Length > 0) return Account.AccessToken;
                var error = await RequestTokenAsync(Account, new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = Account.ClientId,
                    ["client_secret"] = Account.ClientSecret,
                    ["refresh_token"] = Account.RefreshToken
                }).ConfigureAwait(false);
                if (error != null)
                {
                    Account.RefreshToken = "";
                    Save();
                    throw new InvalidOperationException("Сессия YouTube истекла — подключите аккаунт заново.");
                }
                Save();
                return Account.AccessToken;
            }
            finally
            {
                RefreshGate.Release();
            }
        }

        private static async Task<JsonDocument> GetJsonAsync(string path)
        {
            var token = await AccessTokenAsync().ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("YouTube ответил " + (int)response.StatusCode + ".");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private static List<JsonElement> Items(JsonElement root)
        {
            var list = new List<JsonElement>();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var i in items.EnumerateArray()) list.Add(i.Clone());
            return list;
        }

        public static void Open(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
