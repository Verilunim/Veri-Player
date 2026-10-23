using System;
using System.Collections.Generic;
using System.Linq;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>Избранное, недавние и плейлисты — у каждого пользователя свои.</summary>
    public static class UserDataService
    {
        private static readonly Dictionary<string, UserData> Cache = new Dictionary<string, UserData>();
        public static event Action Changed;

        public static UserData For(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return new UserData();
            if (Cache.TryGetValue(userId, out var cached) && cached != null) return cached;

            var data = Storage.Load(Storage.UserDataFile(userId), new UserData()) ?? new UserData();
            data.Favorites ??= new List<string>();
            data.Recent ??= new List<string>();
            data.Playlists ??= new List<Playlist>();
            Cache[userId] = data;
            return data;
        }

        public static UserData Current => For(AuthService.Current?.Id);

        public static void Save(string userId, bool notify = true)
        {
            var data = For(userId);
            Storage.Save(Storage.UserDataFile(userId), data);
            if (notify) Changed?.Invoke();
        }

        public static void SaveCurrent(bool notify = true)
        {
            var id = AuthService.Current?.Id;
            if (string.IsNullOrWhiteSpace(id)) return;
            Storage.Save(Storage.UserDataFile(id), For(id));
            if (notify) Changed?.Invoke();
        }

        public static void Drop(string userId) => Cache.Remove(userId);

        // ------------------------------------------------------------------ favorites

        public static bool IsFavorite(string trackId)
        {
            var d = Current;
            return d != null && d.Favorites.Contains(trackId);
        }

        public static bool ToggleFavorite(string trackId)
        {
            var d = Current;
            if (d == null) return false;
            bool now;
            if (d.Favorites.Contains(trackId)) { d.Favorites.Remove(trackId); now = false; }
            else { d.Favorites.Insert(0, trackId); now = true; }
            SaveCurrent();
            return now;
        }

        // ------------------------------------------------------------------ recent

        public static void PushRecent(string trackId)
        {
            var d = Current;
            if (d == null || string.IsNullOrWhiteSpace(trackId)) return;
            d.Recent.Remove(trackId);
            d.Recent.Insert(0, trackId);
            if (d.Recent.Count > 60) d.Recent.RemoveRange(60, d.Recent.Count - 60);
            SaveCurrent(false);
        }

        // ------------------------------------------------------------------ playlists

        public static Playlist CreatePlaylist(string name)
        {
            var d = Current;
            if (d == null) return null;
            var pl = new Playlist { Name = string.IsNullOrWhiteSpace(name) ? "Новый плейлист" : name.Trim() };
            d.Playlists.Add(pl);
            SaveCurrent();
            return pl;
        }

        public static void RenamePlaylist(Playlist pl, string name)
        {
            if (pl == null) return;
            pl.Name = string.IsNullOrWhiteSpace(name) ? pl.Name : name.Trim();
            SaveCurrent();
        }

        public static void DeletePlaylist(Playlist pl)
        {
            var d = Current;
            if (d == null || pl == null) return;
            d.Playlists.Remove(pl);
            SaveCurrent();
        }

        public static void AddToPlaylist(Playlist pl, IEnumerable<string> trackIds)
        {
            if (pl == null) return;
            foreach (var id in trackIds)
                if (!pl.TrackIds.Contains(id)) pl.TrackIds.Add(id);
            SaveCurrent();
        }

        public static void RemoveFromPlaylist(Playlist pl, string trackId)
        {
            if (pl == null) return;
            pl.TrackIds.Remove(trackId);
            SaveCurrent();
        }

        public static void RemoveTrackEverywhere(string trackId)
        {
            foreach (var user in AuthService.Users)
            {
                var data = For(user.Id);
                data.Favorites.Remove(trackId);
                data.Recent.Remove(trackId);
                foreach (var pl in data.Playlists) pl.TrackIds.Remove(trackId);
                Storage.Save(Storage.UserDataFile(user.Id), data);
            }
            Changed?.Invoke();
        }

        public static List<Track> Resolve(IEnumerable<string> ids)
        {
            var list = new List<Track>();
            foreach (var id in ids)
            {
                var t = LibraryService.Find(id);
                if (t != null && !list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }
}
