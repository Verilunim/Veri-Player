using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    public static class LibraryService
    {
        public static readonly string[] SupportedExtensions =
        {
            ".mp3", ".wav", ".aiff", ".aif", ".m4a", ".mp4", ".aac", ".wma", ".flac", ".ogg", ".oga", ".opus"
        };

        public static List<Track> Tracks { get; private set; } = new List<Track>();
        public static event Action Changed;

        private static readonly SemaphoreSlim ImportGate = new SemaphoreSlim(1, 1);

        public static void Init()
        {
            Tracks = Storage.Load(Storage.LibraryFile, new List<Track>()) ?? new List<Track>();
        }

        public static void Save()
        {
            Storage.Save(Storage.LibraryFile, Tracks);
        }

        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>Только для снимков экрана: наполняет список в памяти, ничего не сохраняя на диск.</summary>
        public static void SeedForDiagnostics(IEnumerable<Track> tracks)
        {
            Tracks.AddRange(tracks);
            NotifyChanged();
        }

        public static bool IsSupported(string path)
        {
            var ext = Path.GetExtension(path);
            return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        public static int CountFor(string userId) => Tracks.Count(t => t.AddedById == userId);

        public static long TotalSize() => Tracks.Sum(t => t.FileSize);

        public static Track Find(string id) => Tracks.FirstOrDefault(t => t.Id == id);

        // ------------------------------------------------------------------ import

        public static List<string> ExpandPaths(IEnumerable<string> paths)
        {
            var files = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        files.AddRange(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories));
                    }
                    else if (File.Exists(p) && IsSupported(p))
                    {
                        files.Add(p);
                    }
                }
                catch { }
            }
            return files.Where(IsSupported).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public sealed class ImportResult
        {
            public int Added;
            public int Skipped;
            public int Failed;
        }

        public static async Task<ImportResult> ImportAsync(IList<string> files, IProgress<(int done, int total, string current)> progress)
        {
            var result = new ImportResult();
            await ImportGate.WaitAsync();
            try
            {
                // при копировании в библиотеку FilePath указывает на копию — сверяем и с исходным путём,
                // иначе повторный импорт той же папки дублировал все треки
                var known = new HashSet<string>(
                    Tracks.Select(t => t.FilePath).Concat(Tracks.Select(t => t.SourcePath)).Where(p => !string.IsNullOrEmpty(p)),
                    StringComparer.OrdinalIgnoreCase);
                var added = new List<Track>();
                int index = 0;

                await Task.Run(() =>
                {
                    foreach (var file in files)
                    {
                        index++;
                        progress?.Report((index, files.Count, Path.GetFileName(file)));

                        if (known.Contains(file)) { result.Skipped++; continue; }

                        var track = BuildTrack(file);
                        if (track == null) { result.Failed++; continue; }

                        if (App.Settings.CopyFilesIntoLibrary)
                        {
                            try
                            {
                                var copy = Storage.CopyInto(file, Storage.CopiedMusicDir);
                                track.SourcePath = file;
                                track.FilePath = copy;
                                track.FileSize = new FileInfo(copy).Length;
                            }
                            catch { }
                        }

                        known.Add(file);
                        known.Add(track.FilePath);
                        track.AddedById = AuthService.Current?.Id ?? "";
                        track.AddedByName = AuthService.Current?.Nick ?? "system";
                        track.AddedAt = DateTime.Now;
                        added.Add(track);
                        result.Added++;
                    }
                });

                if (added.Count > 0)
                {
                    Tracks.AddRange(added);
                    Save();
                    Changed?.Invoke();
                }
            }
            finally
            {
                ImportGate.Release();
            }
            return result;
        }

        /// <summary>Добавляет один файл в библиотеку немедленно. Используется приёмом трека по ссылке.</summary>
        public static Track ImportSingle(string file, string forceTitle = null)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;

            var track = BuildTrack(file);
            if (track == null) return null;

            if (!string.IsNullOrWhiteSpace(forceTitle)) track.Title = forceTitle;
            if (string.IsNullOrWhiteSpace(track.Title)) track.Title = Path.GetFileNameWithoutExtension(file);

            track.AddedById = AuthService.Current?.Id ?? "";
            track.AddedByName = AuthService.Current?.Nick ?? "system";
            track.AddedAt = DateTime.Now;

            Tracks.Add(track);
            Save();
            Changed?.Invoke();
            return track;
        }

        public static Track BuildTrack(string file)
        {
            Track track;
            try
            {
                var info = new FileInfo(file);
                track = new Track
                {
                    FilePath = file,
                    FileSize = info.Length,
                    AddedAt = DateTime.Now
                };

                bool filled = false;
                try
                {
                    using (var tagFile = TagLib.File.Create(file))
                    {
                        var tag = tagFile.Tag;
                        if (tag != null)
                        {
                            track.Title = (tag.Title ?? "").Trim();
                            track.Artist = string.Join(", ", (tag.Performers ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)));
                            track.Album = (tag.Album ?? "").Trim();
                            track.Genre = (tag.FirstGenre ?? "").Trim();
                            track.Year = (int)tag.Year;
                        }
                        if (tagFile.Properties != null && tagFile.Properties.Duration.TotalSeconds > 0.2)
                            track.Duration = tagFile.Properties.Duration.TotalSeconds;
                        filled = true;

                        // обложка
                        try
                        {
                            var picture = tagFile.Tag?.Pictures?.FirstOrDefault();
                            if (picture != null && picture.Data != null && picture.Data.Count > 0)
                            {
                                var data = picture.Data.Data;
                                var img = Storage.LoadImage(data);
                                if (img != null && img.PixelWidth > 8)
                                {
                                    var cover = Path.Combine(Storage.CoversDir, track.Id + ".jpg");
                                    Storage.SaveJpeg(img, cover);
                                    track.CoverPath = cover;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                if (!filled || string.IsNullOrWhiteSpace(track.Title))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var parsed = ParseFileName(name);
                    if (string.IsNullOrWhiteSpace(track.Title)) track.Title = parsed.title;
                    if (string.IsNullOrWhiteSpace(track.Artist)) track.Artist = parsed.artist;
                }

                if (string.IsNullOrWhiteSpace(track.Title))
                    track.Title = Path.GetFileNameWithoutExtension(file);

                return track;
            }
            catch { return null; }
        }

        /// <summary>Пытается разобрать «Artist - Title» (и убрать ведущий номер трека).</summary>
        private static (string artist, string title) ParseFileName(string name)
        {
            var work = name.Trim();
            int dash = work.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                var artist = work.Substring(0, dash).Trim();
                var title = work.Substring(dash + 3).Trim();
                artist = StripTrackNumber(artist);
                if (artist.Length > 0 && title.Length > 0) return (artist, title);
            }
            work = StripTrackNumber(work);
            return ("", work);
        }

        private static string StripTrackNumber(string value)
        {
            int i = 0;
            while (i < value.Length && char.IsDigit(value[i])) i++;
            if (i > 0 && i < 4 && i < value.Length && (value[i] == '.' || value[i] == '-' || value[i] == ' ' || value[i] == '_'))
                return value.Substring(i + 1).Trim();
            return value.Trim();
        }

        // ------------------------------------------------------------------ edit

        public static void Remove(Track track, bool deleteFile)
        {
            if (track == null) return;
            if (deleteFile)
            {
                // играющий файл заблокирован плеером — без остановки удаление молча не срабатывало
                if (AudioService.Current == track) AudioService.Stop();
                Storage.SafeDelete(track.FilePath);
                Storage.SafeDelete(track.CoverPath);
                Storage.SafeDelete(track.VideoPath);
            }
            Tracks.Remove(track);
            UserDataService.RemoveTrackEverywhere(track.Id);
            Save();
            Changed?.Invoke();
        }

        public static void Update(Track track)
        {
            if (track == null) return;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Добавляет готовую запись трека (например, полученную по ссылке без файла).</summary>
        public static void Add(Track track)
        {
            if (track == null) return;
            if (Tracks.Any(t => t.Id == track.Id)) return;
            Tracks.Add(track);
            Save();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ обложка и клип

        /// <summary>Ставит треку новую обложку (квадрат не обязателен — обрежется при показе).</summary>
        public static bool SetCover(Track track, string imagePath)
        {
            if (track == null) return false;

            var image = Storage.LoadImage(imagePath);
            if (image == null) return false;

            try
            {
                var target = Path.Combine(Storage.CoversDir, track.Id + ".jpg");
                var saved = Storage.SaveJpeg(image, target, 760);
                if (saved == null) return false;

                var old = track.CoverPath;
                track.CoverPath = target;
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                    Storage.SafeDelete(old);

                Save();
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        public static void ClearCover(Track track)
        {
            if (track == null) return;
            Storage.SafeDelete(track.CoverPath);
            track.CoverPath = "";
            Save();
            Changed?.Invoke();
        }

        /// <summary>Копирует проверенный клип (MP4, до 5 с) и вешает его на трек.</summary>
        public static bool SetVideo(Track track, string videoPath)
        {
            if (track == null || string.IsNullOrWhiteSpace(videoPath)) return false;

            try
            {
                var copied = Storage.ImportVideoFile(videoPath);
                var old = track.VideoPath;
                track.VideoPath = copied;
                if (!string.Equals(old, copied, StringComparison.OrdinalIgnoreCase))
                    Storage.SafeDelete(old);

                Save();
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }

        public static void ClearVideo(Track track)
        {
            if (track == null) return;
            Storage.SafeDelete(track.VideoPath);
            track.VideoPath = "";
            Save();
            Changed?.Invoke();
        }

        public static void RegisterPlay(Track track)
        {
            if (track == null) return;
            track.Plays++;
            track.LastPlayedAt = DateTime.Now;
            Save();
        }

        public static List<Track> Query(string search, string sortMode, IList<string> restrictTo = null)
        {
            IEnumerable<Track> q = Tracks;

            if (restrictTo != null)
            {
                var set = new HashSet<string>(restrictTo);
                q = Tracks.Where(t => set.Contains(t.Id));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                q = q.Where(t => t.SearchBlob.Contains(s));
            }

            switch (sortMode)
            {
                case "title": q = q.OrderBy(t => t.DisplayTitle, StringComparer.CurrentCultureIgnoreCase); break;
                case "artist": q = q.OrderBy(t => t.DisplayArtist, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.DisplayTitle, StringComparer.CurrentCultureIgnoreCase); break;
                case "album": q = q.OrderBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.DisplayTitle, StringComparer.CurrentCultureIgnoreCase); break;
                case "duration": q = q.OrderByDescending(t => t.Duration); break;
                case "plays": q = q.OrderByDescending(t => t.Plays); break;
                default: q = q.OrderByDescending(t => t.AddedAt); break;
            }

            return q.ToList();
        }
    }
}
