using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Формат ключа Pro+: «VPRO-» + base64url(json) + «.» + base64url(подпись ECDSA P-256).
    /// Общий для приложения (проверка открытым ключом) и утилиты VeriKeygen (подпись закрытым).
    /// </summary>
    public static class LicenseFormat
    {
        public const string Prefix = "VPRO-";

        public sealed class Payload
        {
            /// <summary>Ник, для которого выпущен ключ (без учёта регистра).</summary>
            public string n { get; set; } = "";
            /// <summary>Последний день действия (UTC, yyyy-MM-dd) или пусто — бессрочно.</summary>
            public string e { get; set; } = "";
            /// <summary>Уникальный номер ключа.</summary>
            public string i { get; set; } = "";
            /// <summary>Дата выпуска (UTC, yyyy-MM-dd).</summary>
            public string t { get; set; } = "";

            /// <summary>Момент окончания: ключ действует включительно до конца дня e.</summary>
            public DateTime? ExpiresUtc =>
                DateTime.TryParseExact(e, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var d) ? d.AddDays(1) : (DateTime?)null;
        }

        public static string Sign(Payload payload, ECDsa privateKey)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            var signature = privateKey.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return Prefix + B64Url(body) + "." + B64Url(signature);
        }

        /// <summary>Проверяет подпись и разбирает ключ. Срок и ник проверяет вызывающий.</summary>
        public static bool TryVerify(string key, ECDsa publicKey, out Payload payload)
        {
            payload = null;
            try
            {
                key = string.Concat((key ?? "").Where(c => !char.IsWhiteSpace(c)));
                if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
                var parts = key.Substring(Prefix.Length).Split('.');
                if (parts.Length != 2) return false;

                var body = FromB64Url(parts[0]);
                var signature = FromB64Url(parts[1]);
                if (!publicKey.VerifyData(body, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    return false;

                payload = JsonSerializer.Deserialize<Payload>(body);
                return payload != null && !string.IsNullOrWhiteSpace(payload.n);
            }
            catch
            {
                payload = null;
                return false;
            }
        }

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
    }
}
