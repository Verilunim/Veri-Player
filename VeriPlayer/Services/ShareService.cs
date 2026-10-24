using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Передача треков по ссылке / QR-коду. Работает офлайн: ссылка кодирует метаданные трека,
    /// а при желании — и сам файл (base64), чтобы второй пользователь реально получил звук.
    /// </summary>
    public static class ShareService
    {
        public const string Scheme = "veriplayer://track/";
        public const long MaxEmbedBytes = 16 * 1024 * 1024; // не тащим в ссылку больше ~16 МБ

        public sealed class Payload
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public string Album { get; set; } = "";
            public string Genre { get; set; } = "";
            public int Year { get; set; }
            public double Duration { get; set; }
            public long Size { get; set; }
            public string Ext { get; set; } = ".mp3";
            public string From { get; set; } = "";
            public string Data { get; set; } = ""; // base64 аудио (опционально)
        }

        // ------------------------------------------------------------------ base64url

        private static string B64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromB64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        // ------------------------------------------------------------------ создание ссылки

        /// <summary>Собирает ссылку-передачу. includeFile=true кладёт сам звук в ссылку.</summary>
        public static string BuildLink(Track track, bool includeFile, string fromNick = "")
        {
            if (track == null) return "";

            var payload = new Payload
            {
                Id = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                Genre = track.Genre,
                Year = track.Year,
                Duration = track.Duration,
                Size = track.FileSize,
                Ext = Path.GetExtension(track.FilePath ?? ""),
                From = fromNick ?? ""
            };

            try
            {
                if (includeFile && track.Exists && !track.IsRemote && new FileInfo(track.FilePath).Length <= MaxEmbedBytes)
                    payload.Data = Convert.ToBase64String(File.ReadAllBytes(track.FilePath));
            }
            catch (Exception ex)
            {
                Diagnostics.Log("ShareService.BuildLink: error reading file: " + ex.Message);
            }

            var json = JsonSerializer.Serialize(payload, Storage.Json);
            return Scheme + B64Url(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>Короткая ссылка только с метаданными — удобно для QR-кода.</summary>
        public static string BuildMetaLink(Track track, string fromNick = "") =>
            BuildLink(track, false, fromNick);

        // ------------------------------------------------------------------ разбор ссылки

        public static bool TryParse(string link, out Payload payload, out string error)
        {
            payload = null;
            error = "";

            if (string.IsNullOrWhiteSpace(link))
            {
                error = "Ссылка пустая.";
                return false;
            }

            link = link.Trim();

            if (!link.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            {
                error = "Это не ссылка Veri Player. Ожидается адрес, начинающийся с " + Scheme;
                return false;
            }

            var token = link.Substring(Scheme.Length).Trim();
            try
            {
                var json = Encoding.UTF8.GetString(FromB64Url(token));
                payload = JsonSerializer.Deserialize<Payload>(json, Storage.Json);
            }
            catch (Exception ex)
            {
                error = "Не удалось разобрать ссылку: " + ex.Message;
                return false;
            }

            if (payload == null)
            {
                error = "Ссылка повреждена.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload.Title) && string.IsNullOrWhiteSpace(payload.Artist))
            {
                error = "В ссылке нет информации о треке.";
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ приём трека

        /// <summary>
        /// Добавляет трек из ссылки в библиотеку. Если в ссылке был файл — распаковывает его,
        /// иначе создаёт запись-заглушку (музыку тогда нужно докинуть вручную).
        /// </summary>
        public static (bool ok, string error, Track track) AddFromLink(string link)
        {
            if (!TryParse(link, out var payload, out var parseError))
                return (false, parseError, null);

            Track track;

            if (!string.IsNullOrEmpty(payload.Data))
            {
                // В ссылке лежит сам звук — распаковываем и импортируем настоящий файл
                // расширение приходит из чужой ссылки: пускаем только аудио-форматы, иначе
                // «..\..\» или «.exe» позволили бы записать файл куда угодно
                var ext = string.IsNullOrWhiteSpace(payload.Ext) ? ".mp3" : payload.Ext.Trim();
                if (!ext.StartsWith(".")) ext = "." + ext;
                if (!LibraryService.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    return (false, "Ссылка содержит файл неподдерживаемого формата.", null);

                string filePath;
                try
                {
                    var bytes = FromB64Url(payload.Data);
                    if (bytes.Length > MaxEmbedBytes)
                        return (false, "Файл в ссылке слишком большой.", null);
                    filePath = Path.Combine(Storage.CopiedMusicDir, Storage.UniqueFileName(ext));
                    File.WriteAllBytes(filePath, bytes);
                }
                catch (Exception ex)
                {
                    return (false, "Не удалось сохранить файл: " + ex.Message, null);
                }

                track = LibraryService.ImportSingle(filePath, payload.Title);
                if (track == null)
                    return (false, "Не удалось добавить трек в библиотеку.", null);
            }
            else
            {
                // Только метаданные: создаём запись без файла — музыку пользователь добавит вручную
                track = new Track
                {
                    Title = string.IsNullOrWhiteSpace(payload.Title) ? "Без названия" : payload.Title,
                    Artist = payload.Artist ?? "",
                    Album = payload.Album ?? "",
                    Genre = payload.Genre ?? "",
                    Year = payload.Year,
                    Duration = payload.Duration,
                    FilePath = "",
                    AddedById = AuthService.Current?.Id ?? "",
                    AddedByName = AuthService.Current?.Nick ?? "system",
                    AddedAt = DateTime.Now
                };
                LibraryService.Add(track);
            }

            // перекрываем метаданные тем, что пришли в ссылке (они точнее для заглушки)
            if (!string.IsNullOrWhiteSpace(payload.Title)) track.Title = payload.Title;
            if (!string.IsNullOrWhiteSpace(payload.Artist)) track.Artist = payload.Artist;
            if (!string.IsNullOrWhiteSpace(payload.Album)) track.Album = payload.Album;
            if (!string.IsNullOrWhiteSpace(payload.Genre)) track.Genre = payload.Genre;
            if (payload.Year > 0) track.Year = payload.Year;
            if (payload.Duration > 0) track.Duration = payload.Duration;

            LibraryService.Update(track);

            var nick = AuthService.Current?.Nick ?? "";
            if (!string.IsNullOrWhiteSpace(payload.From) && !string.Equals(payload.From, nick, StringComparison.OrdinalIgnoreCase))
                AuthService.LogActivity("share", "Получен трек «" + track.DisplayTitle + "» от " + payload.From);

            return (true, "", track);
        }

        // ------------------------------------------------------------------ описание

        public static string Describe(Payload payload)
        {
            if (payload == null) return "";
            var artist = string.IsNullOrWhiteSpace(payload.Artist) ? "Неизвестный исполнитель" : payload.Artist;
            var title = string.IsNullOrWhiteSpace(payload.Title) ? "Без названия" : payload.Title;
            var dur = payload.Duration > 0 ? TimeSpan.FromSeconds(payload.Duration).ToString(@"m\:ss") : "?";
            var hasFile = !string.IsNullOrEmpty(payload.Data);
            var who = string.IsNullOrWhiteSpace(payload.From) ? "" : " · передал " + payload.From;
            return artist + " — " + title + "\n" + dur + (hasFile ? " · с файлом" : " · только метаданные") + who;
        }
    }
}
