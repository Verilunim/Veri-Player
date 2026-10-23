using System.Security.Cryptography;
using VeriPlayer.Services;

// Утилита владельца: создаёт пару ключей подписи и выпускает ключи Pro+.
// Закрытый ключ хранится только у вас и НИКОГДА не должен попасть в репозиторий или в сборку.

var defaultKeyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".veriplayer", "license-private.pem");

string Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var keyPath = Option("--key") ?? defaultKeyPath;
var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

switch (command)
{
    case "init":
    {
        if (File.Exists(keyPath))
        {
            Console.Error.WriteLine("Закрытый ключ уже существует: " + keyPath);
            Console.Error.WriteLine("Новый не создаю — иначе все выданные ключи перестанут работать.");
            return 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
        Console.WriteLine("Закрытый ключ сохранён: " + keyPath);
        Console.WriteLine("Сделайте его резервную копию в надёжном месте. Потеряете — не сможете выпускать ключи.");
        Console.WriteLine();
        Console.WriteLine("Открытый ключ (вставьте в LicenseService.PublicKey):");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    case "pubkey":
    {
        using var ecdsa = LoadPrivate(keyPath);
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    case "issue":
    {
        if (args.Length < 2 || args[1].StartsWith("--"))
        {
            Console.Error.WriteLine("Укажите ник: VeriKeygen issue <ник> [--days 30 | --until 2026-12-31 | --lifetime]");
            return 1;
        }

        var nick = args[1].Trim();
        string expires;
        if (args.Contains("--lifetime")) expires = "";
        else if (Option("--until") is string until)
        {
            if (!DateTime.TryParseExact(until, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
            {
                Console.Error.WriteLine("Дата --until в формате yyyy-MM-dd, например 2026-12-31");
                return 1;
            }
            expires = until;
        }
        else
        {
            var days = int.TryParse(Option("--days"), out var d) && d > 0 ? d : 30;
            expires = DateTime.UtcNow.Date.AddDays(days - 1).ToString("yyyy-MM-dd");
        }

        using var ecdsa = LoadPrivate(keyPath);
        var payload = new LicenseFormat.Payload
        {
            n = nick,
            e = expires,
            i = Guid.NewGuid().ToString("N").Substring(0, 12),
            t = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };
        var key = LicenseFormat.Sign(payload, ecdsa);

        Console.WriteLine("Ник:      " + nick);
        Console.WriteLine("Действует: " + (expires.Length == 0 ? "бессрочно" : "по " + expires + " включительно"));
        Console.WriteLine("Номер:    " + payload.i);
        Console.WriteLine();
        Console.WriteLine(key);
        return 0;
    }

    default:
        Console.WriteLine("VeriKeygen — выпуск ключей Veri Player Pro+");
        Console.WriteLine();
        Console.WriteLine("  init                     создать пару ключей (один раз)");
        Console.WriteLine("  pubkey                   показать открытый ключ для приложения");
        Console.WriteLine("  issue <ник> [--days N]   ключ на N дней (по умолчанию 30)");
        Console.WriteLine("  issue <ник> --until yyyy-MM-dd");
        Console.WriteLine("  issue <ник> --lifetime   бессрочный ключ");
        Console.WriteLine();
        Console.WriteLine("  --key <путь>             другой файл закрытого ключа (по умолчанию " + defaultKeyPath + ")");
        return 0;
}

static ECDsa LoadPrivate(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine("Нет закрытого ключа: " + path + ". Сначала выполните: VeriKeygen init");
        Environment.Exit(1);
    }
    var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(path));
    return ecdsa;
}
