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

    case "staff":
    {
        // пароли встроенных аккаунтов команды: вводятся скрыто, в код попадают только соль и хэш
        var target = Option("--out") ?? FindStaffFile();
        if (target == null)
        {
            Console.Error.WriteLine("Не нашёл VeriPlayer/Services/BuiltInStaff.cs. Запустите из папки проекта или укажите --out <путь>.");
            return 1;
        }

        var staff = new (string Nick, string Role, string Bio)[]
        {
            ("Verilunim", "Owner", "Основатель Veri Player. Следит за порядком и развитием проекта."),
            ("Jomapel", "CoOwner", "Со-владелец Veri Player. Помогает владельцу и следит за пользователями.")
        };

        var entries = new List<string>();
        foreach (var member in staff)
        {
            var password = AskPassword(member.Nick);
            var salt = RandomNumberGenerator.GetBytes(16);
            const int iterations = 600000; // как в AuthService
            var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
            entries.Add(
                "            new Entry\n            {\n" +
                "                Nick = \"" + member.Nick + "\",\n" +
                "                Role = Models.UserRole." + member.Role + ",\n" +
                "                Salt = \"" + Convert.ToBase64String(salt) + "\",\n" +
                "                Hash = \"" + Convert.ToBase64String(hash) + "\",\n" +
                "                Iterations = " + iterations + ",\n" +
                "                Bio = \"" + member.Bio + "\"\n" +
                "            }");
        }

        var code =
            "// Этот файл создаёт «VeriKeygen staff». Здесь только соль и PBKDF2-хэш паролей, самих паролей нет.\n" +
            "namespace VeriPlayer.Services\n{\n    public static class BuiltInStaff\n    {\n" +
            "        public sealed class Entry\n        {\n" +
            "            public string Nick;\n            public Models.UserRole Role;\n            public string Salt;\n" +
            "            public string Hash;\n            public int Iterations;\n            public string Bio;\n        }\n\n" +
            "        public static readonly Entry[] All =\n        {\n" + string.Join(",\n", entries) + "\n        };\n    }\n}\n";
        File.WriteAllText(target, code.Replace("\n", Environment.NewLine), new System.Text.UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine("Готово: " + target);
        Console.WriteLine("Пересоберите программу — пароли применятся в каждой установке при следующем запуске.");
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
        Console.WriteLine("  staff                    задать пароли встроенных Verilunim и Jomapel");
        Console.WriteLine();
        Console.WriteLine("  --key <путь>             другой файл закрытого ключа (по умолчанию " + defaultKeyPath + ")");
        return 0;
}

static string FindStaffFile()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "VeriPlayer", "Services", "BuiltInStaff.cs");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

/// <summary>Скрытый ввод пароля с подтверждением; хэш в открытом exe, поэтому пароль нужен длинный.</summary>
static string AskPassword(string nick)
{
    while (true)
    {
        var first = ReadHidden("Пароль для " + nick + " (минимум 12 символов): ");
        if (first.Length < 12) { Console.WriteLine("  Слишком коротко — нужно минимум 12 символов."); continue; }
        if (string.Equals(first, nick, StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("  Пароль не должен совпадать с ником."); continue; }
        var second = ReadHidden("Повторите пароль для " + nick + ": ");
        if (first != second) { Console.WriteLine("  Пароли не совпадают, попробуйте ещё раз."); continue; }
        return first;
    }
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    var text = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (text.Length > 0) { text.Length--; Console.Write("\b \b"); }
            continue;
        }
        if (!char.IsControl(key.KeyChar)) { text.Append(key.KeyChar); Console.Write('*'); }
    }
    Console.WriteLine();
    return text.ToString();
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
