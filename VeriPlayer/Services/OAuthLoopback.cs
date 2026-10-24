using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Вход через браузер по OAuth 2.0 + PKCE для настольной программы: открывает страницу входа
    /// сервиса и ловит возврат на http://127.0.0.1:порт/callback. Общий для SoundCloud и YouTube.
    /// </summary>
    public static class OAuthLoopback
    {
        public static string RedirectUri(int port) => "http://127.0.0.1:" + port + "/callback";

        public sealed class Pkce
        {
            public string Verifier { get; } = RandomString(64);
            public string State { get; } = RandomString(24);
            public string Challenge => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
        }

        /// <summary>
        /// Открывает браузер и ждёт возврата. Возвращает код авторизации или текст ошибки.
        /// </summary>
        public static async Task<(string code, string error)> AuthorizeAsync(string authorizeUrl, int port, string expectedState,
            string serviceName, TimeSpan timeout)
        {
            if (port < 1024 || port > 65000) return (null, "Порт должен быть в диапазоне 1024–65000.");

            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception ex)
            {
                return (null, "Не удалось занять порт " + port + ": " + ex.Message + ". Укажите другой порт.");
            }

            try
            {
                try { Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true }); }
                catch { return (null, "Не удалось открыть браузер."); }

                using var cts = new CancellationTokenSource(timeout);
                while (true)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false); }
                    catch { return (null, "Не дождался ответа от " + serviceName + ". Попробуйте снова."); }

                    using (client)
                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 5000;
                        var buffer = new byte[8192];
                        int read;
                        try { read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false); }
                        catch { continue; }

                        var firstLine = Encoding.ASCII.GetString(buffer, 0, read).Split('\n').FirstOrDefault() ?? "";
                        var parts = firstLine.Split(' ');
                        var query = ParseQuery(parts.Length > 1 ? parts[1] : "/");
                        query.TryGetValue("code", out var code);
                        query.TryGetValue("error", out var error);
                        query.TryGetValue("state", out var state);

                        // браузер может запросить favicon и т.п. — такие запросы пропускаем
                        if (code == null && error == null)
                        {
                            await Respond(stream, "", false).ConfigureAwait(false);
                            continue;
                        }

                        bool ok = code != null && error == null && state == expectedState;
                        await Respond(stream, serviceName, ok).ConfigureAwait(false);

                        if (error != null) return (null, serviceName + " вернул ошибку: " + error);
                        if (state != expectedState) return (null, "Несовпадение state — запрос отклонён в целях безопасности.");
                        return (code, null);
                    }
                }
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        private static async Task Respond(NetworkStream stream, string service, bool success)
        {
            var title = success ? "Veri Player — " + service + " подключён" : "Veri Player";
            var text = success
                ? "Аккаунт подключён. Это окно можно закрыть и вернуться в Veri Player."
                : string.IsNullOrEmpty(service) ? "" : "Подключение не удалось. Попробуйте ещё раз из Veri Player.";
            var html = "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(title) +
                       "</title><style>body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;" +
                       "background:#92857D;color:#fff;font-family:'Segoe UI',sans-serif}div{max-width:520px;padding:44px;" +
                       "background:rgba(255,255,255,.12);text-align:center}h1{font-size:22px;margin:0 0 12px}" +
                       "p{margin:0;opacity:.85;line-height:1.6}</style></head><body><div><h1>" + WebUtility.HtmlEncode(title) +
                       "</h1><p>" + WebUtility.HtmlEncode(text) + "</p></div></body></html>";
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " +
                                               body.Length + "\r\nConnection: close\r\n\r\n");
            try
            {
                await stream.WriteAsync(head).ConfigureAwait(false);
                await stream.WriteAsync(body).ConfigureAwait(false);
            }
            catch { }
        }

        public static Dictionary<string, string> ParseQuery(string target)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var q = target.IndexOf('?');
            if (q < 0) return dict;
            foreach (var pair in target.Substring(q + 1).Split('&'))
            {
                if (pair.Length == 0) continue;
                var kv = pair.Split(new[] { '=' }, 2);
                dict[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace("+", " ")) : "";
            }
            return dict;
        }

        private static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
            return sb.ToString();
        }

        private static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
