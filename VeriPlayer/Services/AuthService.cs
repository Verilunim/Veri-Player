using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    public static class AuthService
    {
        // OWASP (2023) для PBKDF2-SHA256; старые хэши на 120 000 пересчитываются при входе
        private const int Iterations = 600000;
        private const int LegacyIterations = 120000;

        public const int MinPasswordLength = 4;
        public const int MinStaffPasswordLength = 8;

        // после стольких ошибок подряд вход блокируется на 30 с, дальше блокировка удваивается
        private const int FreeAttempts = 5;
        private static readonly TimeSpan FirstLockout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);

        public static List<UserAccount> Users { get; private set; } = new List<UserAccount>();
        public static UserAccount Current { get; private set; }
        public static event Action UsersChanged;

        // ------------------------------------------------------------------ init

        public static void Init()
        {
            Users = Storage.LoadProtected(Storage.UsersFile, new List<UserAccount>()) ?? new List<UserAccount>();
            RequireChangeOfDefaultPasswords();
            EnsureBuiltInStaff();
            Save();
        }

        /// <summary>
        /// Владелец и со-владелец встроены в программу (только хэш пароля, см. BuiltInStaff) и есть
        /// в каждой установке — поэтому стать владельцем чужой копии через «первый запуск» нельзя.
        /// </summary>
        private static void EnsureBuiltInStaff()
        {
            foreach (var entry in BuiltInStaff.All)
            {
                var user = Users.FirstOrDefault(u => string.Equals(u.Nick, entry.Nick, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    user = new UserAccount { Nick = entry.Nick, Bio = entry.Bio ?? "", CreatedAt = DateTime.Now };
                    user.Activity.Add(new ActivityEntry { Kind = "system", Text = "Аккаунт создан системой" });
                    Users.Add(user);
                }

                // Пароль из сборки ставим один раз на каждую его версию: потом владелец аккаунта
                // может сменить его в профиле. Аккаунт с этим ником, созданный кем-то другим
                // до обновления, при этом тоже получает пароль владельца.
                if (user.BuiltInCredential != entry.Hash)
                {
                    user.PasswordSalt = entry.Salt;
                    user.PasswordHash = entry.Hash;
                    user.PasswordIterations = entry.Iterations;
                    user.MustChangePassword = false;
                    user.FailedLogins = 0;
                    user.LockoutUntil = default;
                    user.BuiltInCredential = entry.Hash;
                    LogActivity(user, "security", "Установлен пароль встроенного аккаунта");
                }

                if (user.Role != entry.Role) user.Role = entry.Role;
                user.IsBanned = false;
            }

            if (BuiltInStaff.All.Length == 0) return;

            // кто стал владельцем через «первый запуск» прежних версий — теперь обычный пользователь
            foreach (var user in Users.Where(u => u.Role == UserRole.Owner && !IsBuiltIn(u) &&
                                                  u.Activity.Any(a => a.Text == "Аккаунт владельца создан")))
            {
                user.Role = UserRole.User;
                LogActivity(user, "role", "Роль владельца снята: владелец встроен в программу");
            }
        }

        public static bool IsBuiltIn(UserAccount user) =>
            user != null && BuiltInStaff.All.Any(e => string.Equals(e.Nick, user.Nick, StringComparison.OrdinalIgnoreCase));

        private const string DefaultResetMark = "Стандартный пароль сброшен";

        /// <summary>
        /// Раньше владелец и со-владелец создавались с паролем, равным нику. Такой пароль больше
        /// не сбрасывается (это запирало аккаунт): он пускает один раз, после чего нужно задать новый.
        /// </summary>
        private static void RequireChangeOfDefaultPasswords()
        {
            foreach (var user in Users.Where(u => u.IsStaff))
            {
                if (!HasPassword(user))
                {
                    // версия 1.2.0.0 стирала стандартный пароль, и в аккаунт было не войти —
                    // возвращаем прежний пароль (он и был равен нику) с обязательной сменой
                    if (!user.Activity.Any(a => a.Text == DefaultResetMark)) continue;
                    SetPassword(user, user.Nick);
                    user.MustChangePassword = true;
                    LogActivity(user, "security", "Доступ восстановлен: войдите со старым паролем и задайте новый");
                }
                // стандартные пароли создавались на старом числе итераций — новые хэши не проверяем,
                // чтобы не тратить по полсекунды на каждого сотрудника при каждом запуске
                else if (!user.MustChangePassword && user.PasswordIterations < Iterations && Verify(user, user.Nick))
                {
                    user.MustChangePassword = true;
                }
            }
        }

        private static bool HasPassword(UserAccount user) => !string.IsNullOrEmpty(user?.PasswordHash);

        /// <summary>Нет ни одного владельца, который может войти, — нужен экран первого запуска.</summary>
        public static bool NeedsOwnerSetup => !Users.Any(u => u.Role == UserRole.Owner && HasPassword(u));

        /// <summary>Существующий владелец без пароля (после сброса стандартного), если он есть.</summary>
        public static UserAccount PendingOwner => Users.FirstOrDefault(u => u.Role == UserRole.Owner && !HasPassword(u));

        public static void Save()
        {
            Storage.SaveProtected(Storage.UsersFile, Users);
            UsersChanged?.Invoke();
        }

        /// <summary>
        /// Подхватывает аккаунты, которые появились в файле users.json (например,
        /// зарегистрированы в другом окне приложения), не трогая уже загруженные.
        /// Возвращает true, если список изменился.
        /// </summary>
        public static bool MergeFromDisk()
        {
            List<UserAccount> disk;
            try
            {
                disk = Storage.LoadProtected(Storage.UsersFile, new List<UserAccount>()) ?? new List<UserAccount>();
            }
            catch { return false; }

            bool changed = false;
            foreach (var user in disk)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.Id)) continue;
                if (Users.Any(u => u.Id == user.Id)) continue;
                if (Users.Any(u => string.Equals(u.Nick, user.Nick, StringComparison.OrdinalIgnoreCase))) continue;

                Users.Add(user);
                changed = true;
            }

            if (changed) Save();
            return changed;
        }

        // ------------------------------------------------------------------ passwords

        public static void SetPassword(UserAccount user, string password)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            user.PasswordSalt = Convert.ToBase64String(salt);
            user.PasswordIterations = Iterations;
            user.PasswordHash = Convert.ToBase64String(Hash(password, salt, Iterations));
        }

        public static void ChangePassword(UserAccount user, string newPassword)
        {
            SetPassword(user, newPassword);
            user.FailedLogins = 0;
            user.LockoutUntil = default;
            LogActivity(user, "security", "Пароль изменён");
            Save();
        }

        /// <summary>Проверка старого пароля (например, при смене в профиле).</summary>
        public static bool CheckPassword(UserAccount user, string password)
        {
            if (user == null) return false;
            return Verify(user, password);
        }

        /// <summary>Только PBKDF2: запасной одиночный SHA-256 подбирался бы в сотни тысяч раз быстрее.</summary>
        private static byte[] Hash(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? ""), salt, iterations, HashAlgorithmName.SHA256, 32);

        private static bool Verify(UserAccount user, string password)
        {
            try
            {
                var salt = Convert.FromBase64String(user.PasswordSalt ?? "");
                var iter = user.PasswordIterations <= 0 ? LegacyIterations : user.PasswordIterations;
                var expected = Convert.FromBase64String(user.PasswordHash ?? "");
                if (salt.Length == 0 || expected.Length == 0) return false;
                return CryptographicOperations.FixedTimeEquals(Hash(password, salt, iter), expected);
            }
            catch { return false; }
        }

        /// <summary>Проверяет длину и совпадение; владельцу и со-владельцу нужен пароль длиннее.</summary>
        public static string ValidatePassword(string password, string confirm, bool staff)
        {
            int min = staff ? MinStaffPasswordLength : MinPasswordLength;
            if ((password ?? "").Length < min)
                return "Пароль должен содержать минимум " + min + " символов" + (staff ? " (для владельца и со-владельца)." : ".");
            if (password != confirm) return "Пароли не совпадают.";
            return null;
        }

        // ------------------------------------------------------------------ auth

        public static UserAccount Find(string nick)
        {
            if (string.IsNullOrWhiteSpace(nick)) return null;
            return Users.FirstOrDefault(u => string.Equals(u.Nick, nick.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static UserAccount FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Users.FirstOrDefault(u => u.Id == id);
        }

        public static (bool ok, string error, UserAccount user) Login(string nick, string password)
        {
            var user = Find(nick);
            if (user == null) return (false, "Пользователь с таким логином не найден.", null);
            if (user.IsBanned) return (false, "Аккаунт заблокирован администрацией.", null);
            if (!HasPassword(user)) return (false, "Пароль для аккаунта не задан. Обратитесь к владельцу.", null);

            var wait = user.LockoutUntil - DateTime.Now;
            if (wait > TimeSpan.Zero)
                return (false, "Слишком много неверных попыток. Повторите через " + WaitText(wait) + ".", null);

            if (!Verify(user, password))
            {
                user.FailedLogins++;
                string error = "Неверный пароль. Попробуйте снова.";
                if (user.FailedLogins >= FreeAttempts)
                {
                    var lockout = TimeSpan.FromTicks(Math.Min(MaxLockout.Ticks,
                        FirstLockout.Ticks << Math.Min(20, user.FailedLogins - FreeAttempts)));
                    user.LockoutUntil = DateTime.Now + lockout;
                    LogActivity(user, "security", "Вход заблокирован на " + WaitText(lockout) + " после " + user.FailedLogins + " неверных попыток");
                    error = "Неверный пароль. Вход заблокирован на " + WaitText(lockout) + ".";
                }
                else if (user.FailedLogins >= FreeAttempts - 2)
                {
                    error = "Неверный пароль. Осталось попыток до блокировки: " + (FreeAttempts - user.FailedLogins) + ".";
                }
                Save();
                return (false, error, null);
            }

            if (user.FailedLogins > 0)
                LogActivity(user, "security", "Успешный вход после " + user.FailedLogins + " неверных попыток");
            user.FailedLogins = 0;
            user.LockoutUntil = default;

            // пароль известен только сейчас — заодно пересчитываем хэш на актуальное число итераций
            if (user.PasswordIterations < Iterations) SetPassword(user, password);

            // пароль, равный нику, знает кто угодно — у команды его нужно сменить сразу
            if (user.IsStaff && string.Equals(password, user.Nick, StringComparison.OrdinalIgnoreCase))
                user.MustChangePassword = true;

            user.LastLoginAt = DateTime.Now;
            user.LoginCount++;
            LicenseService.Touch(user);
            LogActivity(user, "login", "Вход в систему");
            Current = user;
            Save();
            return (true, null, user);
        }

        /// <summary>
        /// Обязательная смена пароля сразу после входа (стандартный пароль или восстановленный доступ).
        /// </summary>
        public static string CompleteRequiredChange(UserAccount user, string password, string confirm)
        {
            if (user == null || !ReferenceEquals(user, Current)) return "Сначала войдите в аккаунт.";
            var error = ValidatePassword(password, confirm, user.IsStaff);
            if (error != null) return error;
            if (string.Equals(password, user.Nick, StringComparison.OrdinalIgnoreCase))
                return "Пароль не должен совпадать с ником.";

            SetPassword(user, password);
            user.MustChangePassword = false;
            LogActivity(user, "security", "Стандартный пароль заменён на новый");
            Save();
            return null;
        }

        private static string WaitText(TimeSpan span) =>
            span.TotalSeconds < 90 ? Math.Ceiling(span.TotalSeconds) + " с" : Math.Ceiling(span.TotalMinutes) + " мин";

        public static void Logout()
        {
            Current = null;
        }

        public static (bool ok, string error, UserAccount user) Register(string nick, string password, string confirm,
            string bio, string avatarSourcePath)
        {
            if (NeedsOwnerSetup) return (false, "Сначала нужно создать аккаунт владельца.", null);
            return CreateAccount(nick, password, confirm, bio, avatarSourcePath, UserRole.User, "Аккаунт зарегистрирован");
        }

        /// <summary>
        /// Первый запуск: владелец сам задаёт пароль. Если владелец уже есть, но без пароля
        /// (стандартный был сброшен), пароль ставится ему, ник и данные сохраняются.
        /// </summary>
        public static (bool ok, string error, UserAccount user) SetupOwner(string nick, string password, string confirm,
            string bio, string avatarSourcePath)
        {
            if (!NeedsOwnerSetup) return (false, "Владелец уже создан.", null);

            var pending = PendingOwner;
            if (pending == null)
                return CreateAccount(nick, password, confirm, bio, avatarSourcePath, UserRole.Owner, "Аккаунт владельца создан");

            var error = ValidatePassword(password, confirm, staff: true);
            if (error != null) return (false, error, null);

            SetPassword(pending, password);
            if (!string.IsNullOrWhiteSpace(bio)) pending.Bio = bio.Trim();
            if (!string.IsNullOrWhiteSpace(avatarSourcePath))
            {
                var saved = Storage.SaveSquareAvatar(avatarSourcePath);
                if (saved != null) pending.AvatarPath = saved;
            }
            LogActivity(pending, "security", "Пароль владельца задан при первом запуске");
            pending.LastLoginAt = DateTime.Now;
            pending.LoginCount++;
            Current = pending;
            Save();
            return (true, null, pending);
        }

        private static (bool ok, string error, UserAccount user) CreateAccount(string nick, string password, string confirm,
            string bio, string avatarSourcePath, UserRole role, string activityText)
        {
            nick = (nick ?? "").Trim();

            if (nick.Length < 3) return (false, "Ник должен содержать минимум 3 символа.", null);
            if (nick.Length > 24) return (false, "Ник не должен превышать 24 символа.", null);
            if (nick.Any(c => "\\/:*?\"<>|".Contains(c))) return (false, "Ник содержит недопустимые символы.", null);
            if (Find(nick) != null) return (false, "Такое имя уже занято.", null);
            var passwordError = ValidatePassword(password, confirm, staff: role != UserRole.User);
            if (passwordError != null) return (false, passwordError, null);

            var user = new UserAccount
            {
                Nick = nick,
                Bio = (bio ?? "").Trim(),
                Role = role,
                CreatedAt = DateTime.Now
            };
            SetPassword(user, password);

            if (!string.IsNullOrWhiteSpace(avatarSourcePath))
            {
                var saved = Storage.SaveSquareAvatar(avatarSourcePath);
                if (saved != null) user.AvatarPath = saved;
            }

            user.Activity.Add(new ActivityEntry { Kind = "system", Text = activityText });
            Users.Add(user);
            Current = user;
            user.LastLoginAt = DateTime.Now;
            user.LoginCount = 1;
            Save();
            return (true, null, user);
        }

        // ------------------------------------------------------------------ admin

        public static void LogActivity(UserAccount user, string kind, string text)
        {
            if (user == null) return;
            user.Activity.Insert(0, new ActivityEntry { Kind = kind, Text = text });
            if (user.Activity.Count > 60) user.Activity.RemoveRange(60, user.Activity.Count - 60);
        }

        public static void LogActivity(string kind, string text)
        {
            LogActivity(Current, kind, text);
            Save();
        }

        public static void SetRole(UserAccount user, UserRole role)
        {
            if (user == null) return;
            var before = user.Role;
            user.Role = role;
            LogActivity(user, "role", "Роль изменена: " + RoleName(before) + " → " + RoleName(role));
            Save();
        }

        public static void SetBanned(UserAccount user, bool banned)
        {
            if (user == null) return;
            user.IsBanned = banned;
            LogActivity(user, "security", banned ? "Аккаунт заблокирован" : "Аккаунт разблокирован");
            Save();
        }

        public static void ResetPassword(UserAccount user, string newPassword)
        {
            if (user == null) return;
            SetPassword(user, newPassword);
            user.FailedLogins = 0;
            user.LockoutUntil = default;
            LogActivity(user, "security", "Пароль сброшен администратором");
            Save();
        }

        public static string Delete(UserAccount user)
        {
            if (user == null) return "Пользователь не найден.";
            if (IsBuiltIn(user)) return "Встроенный аккаунт команды удалить нельзя.";
            if (user.Role == UserRole.Owner && Users.Count(u => u.Role == UserRole.Owner) <= 1)
                return "Нельзя удалить единственного владельца.";

            Storage.SafeDelete(user.AvatarPath);
            Storage.SafeDelete(Storage.UserDataFile(user.Id));
            Users.Remove(user);
            Save();
            return null;
        }

        public static string RoleName(UserRole role)
        {
            switch (role)
            {
                case UserRole.Owner: return "Владелец";
                case UserRole.CoOwner: return "Со-владелец";
                default: return "Пользователь";
            }
        }

        public static IEnumerable<UserAccount> SortedUsers(bool staffFirst = true)
        {
            return staffFirst
                ? Users.OrderByDescending(u => u.Role).ThenBy(u => u.Nick, StringComparer.CurrentCultureIgnoreCase)
                : Users.OrderBy(u => u.Nick, StringComparer.CurrentCultureIgnoreCase);
        }
    }
}
