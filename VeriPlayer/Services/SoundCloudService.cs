using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// SoundCloud: вход по OAuth 2.1 + PKCE, ваши треки, лайки и плейлисты, воспроизведение потоком.
    /// По правилам SoundCloud рядом с треком показываем автора, источник и ссылку на трек.
    /// </summary>
    public static partial class SoundCloudService
    {
        public const string SourceKey = "soundcloud";
        private const string Api = "https://api.soundcloud.com";
        private const string Auth = "https://secure.soundcloud.com";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // ссылки на поток отдаются переадресацией на CDN — её адрес и нужен плееру
        private static readonly HttpClient NoRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim RefreshGate = new SemaphoreSlim(1, 1);

        public static string File => Path.Combine(Storage.Root, "soundcloud.json");
        public static ServiceAccount Account { get; private set; } = new ServiceAccount { Port = 8897 };
        public static bool IsConnected => Account.IsConnected;
        public static event Action Changed;

        public static void Init()
        {
            Account = Storage.LoadProtected(File, new ServiceAccount { Port = 8897 }) ?? new ServiceAccount { Port = 8897 };
            if (Account.Port <= 0) Account.Port = 8897;
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

        // ================================================================== вход

        public static async Task<(bool ok, string error)> ConnectAsync(string clientId, string clientSecret, int port)
        {
            clientId = (clientId ?? "").Trim();
            clientSecret = (clientSecret ?? "").Trim();
            if (clientId.Length == 0 || clientSecret.Length == 0)
                return (false, "Укажите Client ID и Client Secret вашего приложения SoundCloud.");

            var pkce = new OAuthLoopback.Pkce();
            var redirect = OAuthLoopback.RedirectUri(port);
            var url = Auth + "/authorize?client_id=" + Uri.EscapeDataString(clientId) +
                      "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                      "&response_type=code&code_challenge_method=S256" +
                      "&code_challenge=" + Uri.EscapeDataString(pkce.Challenge) +
                      "&state=" + Uri.EscapeDataString(pkce.State);

            var (code, error) = await OAuthLoopback.AuthorizeAsync(url, port, pkce.State, "SoundCloud", TimeSpan.FromMinutes(5))
                .ConfigureAwait(false);
            if (error != null) return (false, error);

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirect,
                ["code_verifier"] = pkce.Verifier,
                ["code"] = code
            };
            var account = new ServiceAccount { ClientId = clientId, ClientSecret = clientSecret, Port = port };
            var tokenError = await RequestTokenAsync(account, form).ConfigureAwait(false);
            if (tokenError != null)
                return (false, tokenError + " Проверьте ключи и что Redirect URI в приложении SoundCloud точно: " + redirect);

            Account = account;
            try
            {
                using var me = await GetJsonAsync("/me").ConfigureAwait(false);
                Account.UserName = Str(me.RootElement, "username");
                Account.UserId = Str(me.RootElement, "urn");
                Account.UserUrl = Str(me.RootElement, "permalink_url");
            }
            catch { }
            Save();
            return (true, null);
        }

        private static async Task<string> RequestTokenAsync(ServiceAccount account, Dictionary<string, string> form)
        {
            try
            {
                var response = await Http.PostAsync(Auth + "/oauth/token", new FormUrlEncodedContent(form)).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return "SoundCloud не выдал токен (" + (int)response.StatusCode + ").";

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                account.AccessToken = Str(root, "access_token");
                var refresh = Str(root, "refresh_token");
                if (refresh.Length > 0) account.RefreshToken = refresh; // одноразовый — сразу сохраняем новый
                double expires = root.TryGetProperty("expires_in", out var e) && e.TryGetDouble(out var s) ? s : 3600;
                account.ExpiresAt = DateTime.UtcNow.AddSeconds(expires - 60);
                return account.AccessToken.Length > 0 ? null : "Пустой ответ SoundCloud.";
            }
            catch (Exception ex)
            {
                return "Ошибка связи с SoundCloud: " + ex.Message;
            }
        }

        private static async Task<string> AccessTokenAsync()
        {
            if (!IsConnected) throw new InvalidOperationException("SoundCloud не подключён.");
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
                    throw new InvalidOperationException("Сессия SoundCloud истекла — подключите аккаунт заново.");
                }
                Save();
                return Account.AccessToken;
            }
            finally
            {
                RefreshGate.Release();
            }
        }

        private static async Task<JsonDocument> GetJsonAsync(string pathOrUrl)
        {
            var token = await AccessTokenAsync().ConfigureAwait(false);
            var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : Api + pathOrUrl;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + token);
            request.Headers.TryAddWithoutValidation("Accept", "application/json; charset=utf-8");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("SoundCloud ответил " + (int)response.StatusCode + ".");
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(text);
        }

        internal static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
    }
}
