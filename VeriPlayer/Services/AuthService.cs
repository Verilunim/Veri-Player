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
        private const int Iterations = 120000;

        public static List<UserAccount> Users { get; private set; } = new List<UserAccount>();
        public static UserAccount Current { get; private set; }
        public static event Action UsersChanged;

        // ------------------------------------------------------------------ init

        public static void Init()
        {
            Users = Storage.Load(Storage.UsersFile, new List<UserAccount>()) ?? new List<UserAccount>();
            SeedDefaults();
            Save();
        }

        /// <summary>Владелец и со-владелец создаются автоматически при первом запуске.</summary>
        private static void SeedDefaults()
        {
            EnsureAccount("Verilunim", "Verilunim", UserRole.Owner,
                "Основатель Veri Player. Следит за порядком и развитием проекта.");
            EnsureAccount("Jomapel", "Jomapel", UserRole.CoOwner,
                "Со-владелец Veri Player. Помогает владельцу и следит за пользователями.");
        }

        private static void EnsureAccount(string nick, string password, UserRole role, string bio)
        {
            var existing = Users.FirstOrDefault(u => string.Equals(u.Nick, nick, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // на случай, если роль/пароль были изменены вручную — гарантируем наличие роли
                if (existing.Role != role && existing.PasswordHash.Length == 0)
                    existing.Role = role;
                return;
            }

            var user = new UserAccount
            {
                Nick = nick,
                Bio = bio,
                Role = role,
                CreatedAt = DateTime.Now
            };
            SetPassword(user, password);
            user.Activity.Add(new ActivityEntry { Kind = "system", Text = "Аккаунт создан системой" });
            Users.Add(user);
        }

        public static void Save()
        {
            Storage.Save(Storage.UsersFile, Users);
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
                disk = Storage.Load(Storage.UsersFile, new List<UserAccount>()) ?? new List<UserAccount>();
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
            user.PasswordHash = Hash(password, salt, Iterations);
        }

        public static void ChangePassword(UserAccount user, string newPassword)
        {
            SetPassword(user, newPassword);
            LogActivity(user, "security", "Пароль изменён");
            Save();
        }

        /// <summary>Проверка старого пароля (например, при смене в профиле).</summary>
        public static bool CheckPassword(UserAccount user, string password)
        {
            if (user == null) return false;
            return Verify(user, password);
        }

        private static string Hash(string password, byte[] salt, int iterations)
        {
            try
            {
                var bytes = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password ?? ""),
                    salt, iterations, HashAlgorithmName.SHA256, 32);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                using (var sha = SHA256.Create())
                {
                    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((password ?? "") + Convert.ToBase64String(salt)));
                    return Convert.ToBase64String(bytes);
                }
            }
        }

        private static bool Verify(UserAccount user, string password)
        {
            try
            {
                var salt = Convert.FromBase64String(user.PasswordSalt ?? "");
                var iter = user.PasswordIterations <= 0 ? Iterations : user.PasswordIterations;
                var candidate = Hash(password, salt, iter);
                var a = Convert.FromBase64String(candidate);
                var b = Convert.FromBase64String(user.PasswordHash ?? "");
                return CryptographicOperations.FixedTimeEquals(a, b) || candidate == user.PasswordHash;
            }
            catch { return false; }
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
            if (!Verify(user, password)) return (false, "Неверный пароль. Попробуйте снова.", null);

            user.LastLoginAt = DateTime.Now;
            user.LoginCount++;
            LogActivity(user, "login", "Вход в систему");
            Current = user;
            Save();
            return (true, null, user);
        }

        public static void Logout()
        {
            Current = null;
        }

        public static (bool ok, string error, UserAccount user) Register(string nick, string password, string confirm,
            string bio, string avatarSourcePath)
        {
            nick = (nick ?? "").Trim();

            if (nick.Length < 3) return (false, "Ник должен содержать минимум 3 символа.", null);
            if (nick.Length > 24) return (false, "Ник не должен превышать 24 символа.", null);
            if (nick.Any(c => "\\/:*?\"<>|".Contains(c))) return (false, "Ник содержит недопустимые символы.", null);
            if (Find(nick) != null) return (false, "Такое имя уже занято.", null);
            if ((password ?? "").Length < 4) return (false, "Пароль должен содержать минимум 4 символа.", null);
            if (password != confirm) return (false, "Пароли не совпадают.", null);

            var user = new UserAccount
            {
                Nick = nick,
                Bio = (bio ?? "").Trim(),
                Role = UserRole.User,
                CreatedAt = DateTime.Now
            };
            SetPassword(user, password);

            if (!string.IsNullOrWhiteSpace(avatarSourcePath))
            {
                var saved = Storage.SaveSquareAvatar(avatarSourcePath);
                if (saved != null) user.AvatarPath = saved;
            }

            user.Activity.Add(new ActivityEntry { Kind = "system", Text = "Аккаунт зарегистрирован" });
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
            LogActivity(user, "security", "Пароль сброшен администратором");
            Save();
        }

        public static string Delete(UserAccount user)
        {
            if (user == null) return "Пользователь не найден.";
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
