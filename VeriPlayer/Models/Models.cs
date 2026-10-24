using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace VeriPlayer.Models
{
    public enum UserRole
    {
        User = 0,
        CoOwner = 1,
        Owner = 2
    }

    public class ActivityEntry
    {
        public DateTime At { get; set; } = DateTime.Now;
        public string Kind { get; set; } = "info";
        public string Text { get; set; } = "";
    }

    public class UserAccount
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Nick { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public int PasswordIterations { get; set; } = 120000;
        /// <summary>Неудачные попытки входа подряд и время, до которого вход заблокирован.</summary>
        /// <summary>После входа нужно сразу задать новый пароль (стандартный «пароль = ник»).</summary>
        public bool MustChangePassword { get; set; }
        /// <summary>Какой пароль из сборки (его хэш) уже применён к встроенному аккаунту команды.</summary>
        public string BuiltInCredential { get; set; } = "";
        public int FailedLogins { get; set; }
        public DateTime LockoutUntil { get; set; }
        /// <summary>Активированный ключ Pro+ (проверяется подписью при каждом запуске).</summary>
        public string ProKey { get; set; } = "";
        /// <summary>Самое позднее время, которое видела программа: перевод часов назад не продлевает Pro+.</summary>
        public DateTime LicenseSeenUtc { get; set; }
        /// <summary>Ключ активирован, приветствие Pro+ ещё не показано (покажется один раз при запуске).</summary>
        public bool ProWelcomePending { get; set; }
        public string Bio { get; set; } = "";
        public string AvatarPath { get; set; } = "";
        public UserRole Role { get; set; } = UserRole.User;
        public bool IsBanned { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastLoginAt { get; set; }
        public int LoginCount { get; set; }
        public List<ActivityEntry> Activity { get; set; } = new List<ActivityEntry>();

        [JsonIgnore]
        public bool IsStaff => Role == UserRole.Owner || Role == UserRole.CoOwner;

        [JsonIgnore]
        public string RoleLabel
        {
            get
            {
                switch (Role)
                {
                    case UserRole.Owner: return "Владелец";
                    case UserRole.CoOwner: return "Со-владелец";
                    default: return "Пользователь";
                }
            }
        }

        [JsonIgnore]
        public string Initial => string.IsNullOrWhiteSpace(Nick) ? "?" : Nick.Trim().Substring(0, 1).ToUpperInvariant();

        [JsonIgnore]
        public string CreatedText => CreatedAt.ToString("dd.MM.yyyy HH:mm");

        [JsonIgnore]
        public string LastLoginText => LastLoginAt.Year < 2000 ? "ещё не входил" : LastLoginAt.ToString("dd.MM.yyyy HH:mm");

        [JsonIgnore]
        public string BioOrEmpty => string.IsNullOrWhiteSpace(Bio) ? "Описание не заполнено" : Bio;
    }

    public class Track
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; set; } = "";
        /// <summary>Откуда файл был импортирован, если его скопировали в папку программы.</summary>
        public string SourcePath { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Genre { get; set; } = "";
        public double Duration { get; set; }
        public int Year { get; set; }
        public long FileSize { get; set; }
        public string CoverPath { get; set; } = "";
        /// <summary>Короткий клип (MP4, до 5 секунд), который играет фоном вместе с треком.</summary>
        public string VideoPath { get; set; } = "";
        public string AddedById { get; set; } = "";
        public string AddedByName { get; set; } = "";
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public int Plays { get; set; }
        public DateTime LastPlayedAt { get; set; }

        /// <summary>Откуда трек: пусто — локальный файл, "soundcloud" — поток из SoundCloud.</summary>
        public string Source { get; set; } = "";
        /// <summary>Идентификатор трека в сервисе (для SoundCloud — urn).</summary>
        public string RemoteId { get; set; } = "";
        /// <summary>Страница трека в сервисе — по правилам SoundCloud на неё нужна ссылка.</summary>
        public string WebUrl { get; set; } = "";
        /// <summary>Адрес обложки в сервисе; локальная копия появляется в CoverPath.</summary>
        public string ArtworkUrl { get; set; } = "";

        [JsonIgnore]
        public bool IsRemote => !string.IsNullOrEmpty(Source);

        [JsonIgnore]
        public bool Exists => IsRemote || (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath));

        [JsonIgnore]
        public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath) && File.Exists(CoverPath);

        [JsonIgnore]
        public bool HasVideo => !string.IsNullOrWhiteSpace(VideoPath) && File.Exists(VideoPath);

        [JsonIgnore]
        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(FilePath) : Title;

        [JsonIgnore]
        public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Неизвестный исполнитель" : Artist;

        [JsonIgnore]
        public string DisplayAlbum => string.IsNullOrWhiteSpace(Album) ? "—" : Album;

        [JsonIgnore]
        public string DurationText
        {
            get
            {
                if (Duration <= 0) return "--:--";
                var t = TimeSpan.FromSeconds(Duration);
                return t.TotalHours >= 1
                    ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                    : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
            }
        }

        [JsonIgnore]
        public string AddedText => AddedAt.ToString("dd.MM.yyyy");

        [JsonIgnore]
        public string SizeText
        {
            get
            {
                double b = FileSize;
                string[] u = { "Б", "КБ", "МБ", "ГБ" };
                int i = 0;
                while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
                return i == 0 ? string.Format("{0} {1}", (long)b, u[i]) : string.Format("{0:0.0} {1}", b, u[i]);
            }
        }

        [JsonIgnore]
        public string SearchBlob => (DisplayTitle + " " + DisplayArtist + " " + Album + " " + Genre).ToLowerInvariant();
    }

    public class Playlist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Новый плейлист";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<string> TrackIds { get; set; } = new List<string>();
    }

    public class UserData
    {
        public List<string> Favorites { get; set; } = new List<string>();
        public List<string> Recent { get; set; } = new List<string>();
        public List<Playlist> Playlists { get; set; } = new List<Playlist>();
    }

    public class BackgroundImage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "Мой фон";
        public string FilePath { get; set; } = "";
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public long FileSize { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public string AddedByName { get; set; } = "";

        [JsonIgnore]
        public string SizeText => PixelWidth + " × " + PixelHeight;

        [JsonIgnore]
        public bool HasFile => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
    }

    public class AppSettings
    {
        // ---- appearance ----
        /// <summary>Версия оформления: при смене дизайна старые цвета один раз сбрасываются на новые.</summary>
        public int DesignVersion { get; set; }
        public double Hue { get; set; } = 78;
        public double Saturation { get; set; } = 0.37;
        public double Value { get; set; } = 0.37;
        public double SecondHueOffset { get; set; } = 12;
        public bool AutoHue { get; set; }
        public double AutoHueSpeed { get; set; } = 18;

        public string Effect { get; set; } = "none";
        public double EffectIntensity { get; set; } = 1.0;
        public bool Visualizer { get; set; } = true;
        public bool ReduceMotion { get; set; }

        public string BackgroundId { get; set; } = "";
        public double BackgroundDim { get; set; } = 0.55;
        public double BackgroundBlur { get; set; } = 10;

        // ---- behaviour ----
        public bool CopyFilesIntoLibrary { get; set; }
        public string SortMode { get; set; } = "added";
        public double Volume { get; set; } = 0.8;
        public bool Muted { get; set; }
        public bool Shuffle { get; set; }
        public string Repeat { get; set; } = "off";
        public string LastTrackId { get; set; } = "";

        // ---- services ----
        public string SpotifyClientId { get; set; } = "";
        public int SpotifyPort { get; set; } = 8899;
        
        // ---- AI music generation ----
        public string SunoApiKey { get; set; } = "";

        // ---- Pro+ (только для команды) ----
        /// <summary>Путь к закрытому ключу подписи, если он лежит не на месте по умолчанию.</summary>
        public string LicensePrivateKeyPath { get; set; } = "";
    }

    public class SpotifyToken
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public string Scope { get; set; } = "";
        public string UserName { get; set; } = "";
        public string UserId { get; set; } = "";
    }

    public class SpotifyPlaylistInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Owner { get; set; } = "";
        public int TrackCount { get; set; }
        public string ImageUrl { get; set; } = "";
        public string Url { get; set; } = "";
        public List<SpotifyTrackInfo> Tracks { get; set; } = new List<SpotifyTrackInfo>();
    }

    public class SpotifyTrackInfo
    {
        public string Name { get; set; } = "";
        public string Artists { get; set; } = "";
        public string Album { get; set; } = "";
        public string Url { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public int DurationMs { get; set; }

        [JsonIgnore]
        public string DurationText => TimeSpan.FromMilliseconds(Math.Max(0, DurationMs)).ToString(@"m\:ss");

        [JsonIgnore]
        public string Line => (Artists + " — " + Name).Trim(' ', '—', ' ');
    }
}
