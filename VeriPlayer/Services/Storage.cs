using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Everything that touches the disk lives here: folders, json files, images.
    /// %AppData%\VeriPlayer
    /// </summary>
    public static class Storage
    {
        private static readonly object Gate = new object();

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true
        };

        public static string Root { get; private set; }

        static Storage()
        {
            Root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VeriPlayer");
            try { Directory.CreateDirectory(Root); } catch { }
        }

        public static string Dir(string name)
        {
            var p = Path.Combine(Root, name);
            try { Directory.CreateDirectory(p); } catch { }
            return p;
        }

        public static string AvatarsDir => Dir("avatars");
        public static string BackgroundsDir => Dir("backgrounds");
        public static string CoversDir => Dir("covers");
        public static string VideosDir => Dir("videos");
        public static string CopiedMusicDir => Dir("music");
        public static string UserDataDir => Dir("userdata");

        public static string UsersFile => Path.Combine(Root, "users.json");
        public static string SettingsFile => Path.Combine(Root, "settings.json");
        public static string LibraryFile => Path.Combine(Root, "library.json");
        public static string BackgroundsFile => Path.Combine(Root, "backgrounds.json");
        public static string SpotifyFile => Path.Combine(Root, "spotify.json");

        public static string UserDataFile(string userId) => Path.Combine(UserDataDir, userId + ".json");

        // ------------------------------------------------------------------ json

        public static T Load<T>(string file, T fallback) where T : class
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(file)) return fallback;
                    var text = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(text)) return fallback;
                    var value = JsonSerializer.Deserialize<T>(text, Json);
                    return value ?? fallback;
                }
                catch
                {
                    try { File.Copy(file, file + ".broken", true); } catch { }
                    return fallback;
                }
            }
        }

        public static void Save<T>(string file, T value) =>
            WriteAtomic(file, () => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)));

        private static void WriteAtomic(string file, Func<byte[]> content)
        {
            lock (Gate)
            {
                try
                {
                    var dir = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var tmp = file + ".tmp";
                    File.WriteAllBytes(tmp, content());
                    // одна операция вместо Delete+Move: при сбое между ними файл пропадал целиком
                    File.Move(tmp, file, true);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("save " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }

        // ------------------------------------------------------------------ json под DPAPI

        // Привязывает шифр к этой программе: другое приложение того же пользователя
        // без этих байтов файл через DPAPI не расшифрует.
        private static readonly byte[] ProtectEntropy = Encoding.UTF8.GetBytes("VeriPlayer/users/v1");

        /// <summary>
        /// Файл зашифрован ключом учётной записи Windows (DPAPI): на другом компьютере или
        /// под другим пользователем Windows его не прочитать и не подменить незаметно.
        /// Старый открытый JSON читается как есть и шифруется при следующем сохранении.
        /// </summary>
        public static T LoadProtected<T>(string file, T fallback) where T : class
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(file)) return fallback;
                    var bytes = File.ReadAllBytes(file);
                    if (bytes.Length == 0) return fallback;

                    var plain = LooksLikeJson(bytes)
                        ? bytes
                        : ProtectedData.Unprotect(bytes, ProtectEntropy, DataProtectionScope.CurrentUser);
                    return JsonSerializer.Deserialize<T>(plain, Json) ?? fallback;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("load " + Path.GetFileName(file) + ": " + ex.Message);
                    try { File.Copy(file, file + ".broken", true); } catch { }
                    return fallback;
                }
            }
        }

        public static void SaveProtected<T>(string file, T value) =>
            WriteAtomic(file, () => ProtectedData.Protect(
                JsonSerializer.SerializeToUtf8Bytes(value, Json), ProtectEntropy, DataProtectionScope.CurrentUser));

        private static bool LooksLikeJson(byte[] bytes)
        {
            int i = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            while (i < bytes.Length && (bytes[i] == ' ' || bytes[i] == '\t' || bytes[i] == '\r' || bytes[i] == '\n')) i++;
            return i < bytes.Length && (bytes[i] == '[' || bytes[i] == '{');
        }

        // ------------------------------------------------------------------ files

        public static string UniqueFileName(string ext)
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext;
        }

        public static void SafeDelete(string path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        public static string Human(long bytes)
        {
            double b = bytes;
            string[] u = { "Б", "КБ", "МБ", "ГБ" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return i == 0 ? ((long)b) + " " + u[i] : string.Format("{0:0.0} {1}", b, u[i]);
        }

        public static string CopyInto(string sourceFile, string targetDir, string baseName = null)
        {
            Directory.CreateDirectory(targetDir);
            var ext = Path.GetExtension(sourceFile);
            var name = string.IsNullOrWhiteSpace(baseName) ? Path.GetFileNameWithoutExtension(sourceFile) : baseName;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var target = Path.Combine(targetDir, name + ext);
            int n = 1;
            while (File.Exists(target))
                target = Path.Combine(targetDir, name + "_" + (n++) + ext);
            File.Copy(sourceFile, target, true);
            return target;
        }

        // ------------------------------------------------------------------ images

        /// <summary>Loads a bitmap without keeping the file locked.</summary>
        public static BitmapImage LoadImage(string path, int decodeWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public static BitmapImage LoadImage(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { return null; }
        }

        public static (int width, int height) ReadPixelSize(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    return (frame.PixelWidth, frame.PixelHeight);
                }
            }
            catch { return (0, 0); }
        }

        /// <summary>Center-crops an image to a square and saves it as png (avatars).</summary>
        public static string SaveSquareAvatar(string sourcePath, int size = 256)
        {
            var src = LoadImage(sourcePath);
            if (src == null) return null;

            int side = Math.Min(src.PixelWidth, src.PixelHeight);
            int x = (src.PixelWidth - side) / 2;
            int y = (src.PixelHeight - side) / 2;
            var cropped = new CroppedBitmap(src, new System.Windows.Int32Rect(x, y, side, side));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                dc.DrawImage(cropped, new System.Windows.Rect(0, 0, size, size));
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            var target = Path.Combine(AvatarsDir, UniqueFileName(".png"));
            using (var fs = File.Create(target)) encoder.Save(fs);
            return target;
        }

        public static string SaveJpeg(BitmapSource source, string targetPath, int maxSide = 600)
        {
            try
            {
                BitmapSource bmp = source;
                if (maxSide > 0 && (source.PixelWidth > maxSide || source.PixelHeight > maxSide))
                {
                    double scale = Math.Min((double)maxSide / source.PixelWidth, (double)maxSide / source.PixelHeight);
                    var tb = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                    tb.Freeze();
                    bmp = tb;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(targetPath)) encoder.Save(fs);
                return targetPath;
            }
            catch { return null; }
        }

        /// <summary>Copies a background picture (validates nothing, validation is elsewhere).</summary>
        public static string ImportBackgroundFile(string sourcePath)
        {
            var target = Path.Combine(BackgroundsDir, UniqueFileName(Path.GetExtension(sourcePath)));
            File.Copy(sourcePath, target, true);
            return target;
        }

        /// <summary>Copies a short clip into the app folder so it survives moving the original.</summary>
        public static string ImportVideoFile(string sourcePath)
        {
            var target = Path.Combine(VideosDir, UniqueFileName(".mp4"));
            File.Copy(sourcePath, target, true);
            return target;
        }
    }
}
