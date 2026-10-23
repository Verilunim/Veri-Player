using System;
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
                return (false, "Этот ключ выпущен для другого аккаунта («" + payload.n + "»).");
            if (payload.ExpiresUtc is DateTime end && DateTime.UtcNow >= end)
                return (false, "Срок действия этого ключа уже закончился.");

            user.ProKey = string.Concat(key.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            Touch(user);
            AuthService.LogActivity(user, "pro", "Активирован Pro+ (ключ " + payload.i + ")");
            AuthService.Save();
            Changed?.Invoke();
            return (true, null);
        }

        private static ECDsa CreateVerifier()
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
            return ecdsa;
        }
    }
}
