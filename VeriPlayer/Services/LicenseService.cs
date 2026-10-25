using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Подписка Pro+: ключи подписаны закрытым ключом владельца (утилита tools/VeriKeygen),
    /// приложение знает только открытый — подделать ключ или включить Pro+ правкой файлов нельзя.
    /// Владелец и со-владелец получают Pro+ по роли.
    /// </summary>
    public static class LicenseService
    {
        // Открытый ключ из «VeriKeygen pubkey». Закрытый лежит только у владельца.
        private const string PublicKey =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcMsQrOGqOjNM5h1oRDGFT0K5LYquj9d4q+IVq3w7pKvlz59/jceU64+zfKDUub3rPhkWz4cAgrmZYaJwF/eJLQ==";

        private static readonly ECDsa Verifier = CreateVerifier();

        public static event Action Changed;

        public sealed class Status
        {
            public bool Active;
            public bool ByRole;
            /// <summary>Последний день действия (включительно) или null — бессрочно.</summary>
            public DateTime? Expires;
            /// <summary>Ключ был, но истёк — чтобы показать «срок закончился», а не «нет подписки».</summary>
            public bool Expired;

            public string Text =>
                ByRole ? "Pro+ включён для команды Veri Player"
                : Active ? (Expires == null ? "Pro+ активен бессрочно" : "Pro+ активен по " + Expires.Value.ToString("dd.MM.yyyy") + " включительно")
                : Expired ? "Срок Pro+ закончился" + (Expires != null ? " (действовал по " + Expires.Value.ToString("dd.MM.yyyy") + ")" : "")
                : "Pro+ не активирован";
        }

        public static bool IsProCurrent => For(AuthService.Current).Active;

        public static Status For(UserAccount user)
        {
            if (user == null) return new Status();
            if (user.IsStaff) return new Status { Active = true, ByRole = true };
            if (string.IsNullOrWhiteSpace(user.ProKey)) return new Status();

            if (!LicenseFormat.TryVerify(user.ProKey, Verifier, out var payload) ||
                !string.Equals(payload.n.Trim(), user.Nick.Trim(), StringComparison.OrdinalIgnoreCase))
                return new Status();

            var expires = payload.ExpiresUtc;
            if (expires == null) return new Status { Active = true };

            // часы, переведённые назад, не продлевают подписку
            var now = DateTime.UtcNow > user.LicenseSeenUtc ? DateTime.UtcNow : user.LicenseSeenUtc;
            var lastDay = expires.Value.AddDays(-1).Date;
            return now < expires.Value
                ? new Status { Active = true, Expires = lastDay }
                : new Status { Expired = true, Expires = lastDay };
        }

        /// <summary>Запоминает текущее время у пользователя (вызывается при входе).</summary>
        public static void Touch(UserAccount user)
        {
            if (user != null && DateTime.UtcNow > user.LicenseSeenUtc) user.LicenseSeenUtc = DateTime.UtcNow;
        }

        public static (bool ok, string error) Activate(UserAccount user, string key)
        {
            if (user == null) return (false, "Сначала войдите в аккаунт.");
            if (string.IsNullOrWhiteSpace(key)) return (false, "Вставьте ключ активации.");

            if (!LicenseFormat.TryVerify(key, Verifier, out var payload))
                return (false, "Ключ недействителен. Проверьте, что он скопирован целиком.");
            if (!string.Equals(payload.n.Trim(), user.Nick.Trim(), StringComparison.OrdinalIgnoreCase))
                return (false, "Этот ключ уже не активен."); // чей это ключ — не раскрываем
            if (payload.ExpiresUtc is DateTime end && DateTime.UtcNow >= end)
                return (false, "Срок действия этого ключа уже закончился.");

            user.ProKey = string.Concat(key.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            // приветствие покажем один раз — при следующем запуске программы
            user.ProWelcomePending = true;
            Touch(user);
            AuthService.LogActivity(user, "pro", "Активирован Pro+ (ключ " + payload.i + ")");
            AuthService.Save();
            Changed?.Invoke();
            return (true, null);
        }

        /// <summary>Показать приветствие Pro+ сейчас? Возвращает true один раз после активации.</summary>
        public static bool TakeWelcome(UserAccount user)
        {
            if (user == null || !user.ProWelcomePending) return false;
            user.ProWelcomePending = false;
            AuthService.Save();
            return For(user).Active;
        }

        // ================================================================== выпуск ключей (команда)

        /// <summary>Где утилита VeriKeygen хранит закрытый ключ по умолчанию.</summary>
        public static string DefaultPrivateKeyPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veriplayer", "license-private.pem");

        public static string IssuedFile => Path.Combine(Storage.Root, "issued-keys.json");

        public sealed class IssuedKey
        {
            public string Id { get; set; } = "";
            public string Nick { get; set; } = "";
            /// <summary>Последний день (yyyy-MM-dd) или пусто — бессрочно.</summary>
            public string LastDay { get; set; } = "";
            public DateTime IssuedAt { get; set; } = DateTime.Now;
            public string IssuedBy { get; set; } = "";
            public string Key { get; set; } = "";

            public string TermText => LastDay.Length == 0 ? "бессрочно"
                : "по " + DateTime.ParseExact(LastDay, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd.MM.yyyy");

            public string Line => IssuedAt.ToString("dd.MM.yyyy HH:mm") + "  ·  " + Nick + "  ·  " + TermText + "  ·  №" + Id;
        }

        private static bool CanIssue => AuthService.Current?.IsStaff == true;

        /// <summary>
        /// Загружает закрытый ключ и проверяет, что он парный ключу, вшитому в программу:
        /// иначе выданные им ключи не прошли бы проверку.
        /// </summary>
        public static (ECDsa signer, string error) LoadSigner(string path)
        {
            if (!CanIssue) return (null, "Выпуск ключей доступен только владельцу и со-владельцу.");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return (null, "Файл закрытого ключа не найден. Создайте его командой «VeriKeygen init» или выберите файл.");
            try
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(File.ReadAllText(path));
                if (!ecdsa.ExportSubjectPublicKeyInfo().SequenceEqual(Convert.FromBase64String(PublicKey)))
                {
                    ecdsa.Dispose();
                    return (null, "Этот закрытый ключ не подходит к программе: подписанные им ключи не пройдут проверку.");
                }
                return (ecdsa, null);
            }
            catch (Exception ex)
            {
                return (null, "Не удалось прочитать закрытый ключ: " + ex.Message);
            }
        }

        /// <param name="lastDay">Последний день действия (включительно) или null — бессрочно.</param>
        public static (string key, string error) Issue(ECDsa signer, string nick, DateTime? lastDay)
        {
            if (!CanIssue) return (null, "Выпуск ключей доступен только владельцу и со-владельцу.");
            if (signer == null) return (null, "Сначала укажите файл закрытого ключа.");
            nick = (nick ?? "").Trim();
            if (nick.Length == 0) return (null, "Укажите ник пользователя.");
            if (lastDay != null && lastDay.Value.Date < DateTime.UtcNow.Date)
                return (null, "Дата окончания уже прошла.");

            var payload = new LicenseFormat.Payload
            {
                n = nick,
                e = lastDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                i = Guid.NewGuid().ToString("N").Substring(0, 12),
                t = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            var key = LicenseFormat.Sign(payload, signer);

            var entry = new IssuedKey
            {
                Id = payload.i,
                Nick = nick,
                LastDay = payload.e,
                IssuedBy = AuthService.Current?.Nick ?? "",
                Key = key
            };
            var issued = LoadIssued();
            issued.Insert(0, entry);
            Storage.SaveProtected(IssuedFile, issued);
            AuthService.LogActivity("pro", "Выпущен ключ Pro+ для «" + nick + "» (" + entry.TermText + ", №" + payload.i + ")");
            return (key, null);
        }

        public static List<IssuedKey> LoadIssued() =>
            Storage.LoadProtected(IssuedFile, new List<IssuedKey>()) ?? new List<IssuedKey>();

        public sealed class KeyInfo
        {
            public bool Genuine;
            public string Nick = "";
            public string Id = "";
            public DateTime? Issued;
            public DateTime? LastDay;
            public bool Expired;

            public string Text
            {
                get
                {
                    if (!Genuine) return "Ключ поддельный или повреждён: подпись не совпадает.";
                    var term = LastDay == null ? "бессрочно" : "по " + LastDay.Value.ToString("dd.MM.yyyy") + " включительно";
                    var state = Expired ? "срок истёк"
                        : LastDay == null ? "действует"
                        : "действует, осталось дней: " + Math.Max(1, (int)(LastDay.Value.AddDays(1) - DateTime.UtcNow.Date).TotalDays);
                    return "Ник: " + Nick + "\nСрок: " + term + " — " + state +
                           (Issued != null ? "\nВыпущен: " + Issued.Value.ToString("dd.MM.yyyy") : "") +
                           "\nНомер: " + Id;
                }
            }
        }

        /// <summary>Разбирает любой ключ: для кого он, до какого числа и настоящий ли.</summary>
        public static KeyInfo Inspect(string key)
        {
            if (!CanIssue || !LicenseFormat.TryVerify(key, Verifier, out var payload)) return new KeyInfo();
            var end = payload.ExpiresUtc;
            DateTime? issued = DateTime.TryParseExact(payload.t, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t) ? t : (DateTime?)null;
            return new KeyInfo
            {
                Genuine = true,
                Nick = payload.n,
                Id = payload.i,
                Issued = issued,
                LastDay = end?.AddDays(-1).Date,
                Expired = end != null && DateTime.UtcNow >= end.Value
            };
        }

        private static ECDsa CreateVerifier()
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
            return ecdsa;
        }
    }
}
