using System;
using System.Collections.Generic;
using System.Linq;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Подборка «Для вас»: библиотека упорядочена по вкусу пользователя.
    /// Вкус считается по времени прослушивания: чем дольше слушали трек, его исполнителя и жанр,
    /// тем выше они в списке. Избранное и недавние добавляют вес, частые пропуски — убавляют.
    /// Непрослушанные треки любимых исполнителей и жанров тоже поднимаются наверх.
    /// </summary>
    public static class TasteService
    {
        // веса составляющих оценки
        private const double TrackWeight = 1.0;     // за log(1 + минут на самом треке)
        private const double ArtistWeight = 3.0;    // за долю времени на исполнителя
        private const double GenreWeight = 2.0;     // за долю времени на жанр
        private const double FavoriteBonus = 1.6;
        private const double RecentBonus = 0.6;     // у самого недавнего, дальше убывает
        private const double SkipPenalty = 0.5;     // за каждый пропуск, не больше трёх
        /// <summary>Избранный трек влияет на вкус, как столько минут прослушивания.</summary>
        private const double FavoriteAsMinutes = 8;

        private static readonly char[] GenreSeparators = { ',', ';', '/', '|', '&' };

        public sealed class Profile
        {
            public readonly Dictionary<string, double> Artists = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, double> Genres = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public double ArtistTotal;
            public double GenreTotal;
            public bool IsEmpty => ArtistTotal <= 0 && GenreTotal <= 0;

            public string TopArtist => Artists.Count == 0 ? null : Artists.OrderByDescending(p => p.Value).First().Key;
            public string TopGenre => Genres.Count == 0 ? null : Genres.OrderByDescending(p => p.Value).First().Key;
        }

        /// <summary>Вкус пользователя: сколько минут пришлось на каждого исполнителя и жанр.</summary>
        public static Profile Build(UserData data)
        {
            var profile = new Profile();
            if (data == null) return profile;

            var minutes = new Dictionary<string, double>();
            foreach (var pair in data.TrackSeconds)
                minutes[pair.Key] = pair.Value / 60.0;
            foreach (var id in data.Favorites)
            {
                minutes.TryGetValue(id, out var m);
                minutes[id] = m + FavoriteAsMinutes;
            }

            foreach (var pair in minutes)
            {
                var track = LibraryService.Find(pair.Key);
                if (track == null || pair.Value <= 0) continue;

                var artist = ArtistKey(track);
                if (artist != null)
                {
                    profile.Artists.TryGetValue(artist, out var a);
                    profile.Artists[artist] = a + pair.Value;
                    profile.ArtistTotal += pair.Value;
                }

                var genres = GenresOf(track);
                foreach (var genre in genres)
                {
                    // трек с несколькими жанрами делит своё время между ними
                    double share = pair.Value / genres.Count;
                    profile.Genres.TryGetValue(genre, out var g);
                    profile.Genres[genre] = g + share;
                    profile.GenreTotal += share;
                }
            }
            return profile;
        }

        /// <summary>Сортирует треки по вкусу. Пока вкуса нет — сначала новые, как раньше.</summary>
        public static IEnumerable<Track> Rank(IEnumerable<Track> tracks, UserData data)
        {
            var profile = Build(data);
            if (profile.IsEmpty && (data == null || data.Recent.Count == 0))
                return tracks.OrderByDescending(t => t.AddedAt);

            var favorites = new HashSet<string>(data.Favorites);
            var recent = new Dictionary<string, int>();
            for (int i = 0; i < data.Recent.Count; i++) recent[data.Recent[i]] = i;
            int recentSpan = Math.Max(1, data.Recent.Count);

            double Score(Track t)
            {
                double score = 0;

                data.TrackSeconds.TryGetValue(t.Id, out var seconds);
                score += TrackWeight * Math.Log(1 + seconds / 60.0);

                var artist = ArtistKey(t);
                if (artist != null && profile.ArtistTotal > 0 && profile.Artists.TryGetValue(artist, out var a))
                    score += ArtistWeight * a / profile.ArtistTotal;

                if (profile.GenreTotal > 0)
                {
                    double best = 0;
                    foreach (var genre in GenresOf(t))
                        if (profile.Genres.TryGetValue(genre, out var g)) best = Math.Max(best, g / profile.GenreTotal);
                    score += GenreWeight * best;
                }

                if (favorites.Contains(t.Id)) score += FavoriteBonus;
                if (recent.TryGetValue(t.Id, out var index)) score += RecentBonus * (1 - (double)index / recentSpan);
                if (data.Skips.TryGetValue(t.Id, out var skips)) score -= SkipPenalty * Math.Min(3, skips);

                return score;
            }

            return tracks.Select(t => (track: t, score: Score(t)))
                .OrderByDescending(x => x.score)
                .ThenByDescending(x => x.track.AddedAt)
                .Select(x => x.track);
        }

        private static string ArtistKey(Track t) =>
            string.IsNullOrWhiteSpace(t.Artist) ? null : t.Artist.Trim();

        private static List<string> GenresOf(Track t) =>
            string.IsNullOrWhiteSpace(t.Genre)
                ? new List<string>()
                : t.Genre.Split(GenreSeparators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(g => g.Trim())
                    .Where(g => g.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }
}
