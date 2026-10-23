using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>Генерация музыки через Suno AI API.</summary>
    public static class MusicGenService
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private static List<GeneratedTrack> _history = new List<GeneratedTrack>();
        
        public static IReadOnlyList<GeneratedTrack> History => _history.AsReadOnly();
        public static event Action HistoryChanged;
        
        private const string BaseUrl = "https://api.sunoapi.com/api/v1";
        
        public static void Init()
        {
            Load();
        }
        
        private static void Load()
        {
            try
            {
                var path = Path.Combine(Storage.Root, "generated.json");
                if (File.Exists(path))
                    _history = Storage.Load(path, new List<GeneratedTrack>()) ?? new List<GeneratedTrack>();
            }
            catch { _history = new List<GeneratedTrack>(); }
        }
        
        private static void Save()
        {
            try
            {
                var path = Path.Combine(Storage.Root, "generated.json");
                Storage.Save(path, _history);
                HistoryChanged?.Invoke();
            }
            catch { }
        }
        
        public static string GeneratedDir
        {
            get
            {
                var dir = Path.Combine(Storage.Root, "generated");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }
        
        /// <summary>
        /// Создаёт 2 варианта песни через Suno API.
        /// </summary>
        /// <returns>Список из 2 GeneratedTrack объектов (или меньше при ошибке).</returns>
        public static async Task<List<GeneratedTrack>> GenerateAsync(
            string stylePrompt,
            string lyrics,
            bool isInstrumental,
            double weirdness,
            double styleAdherence,
            string negativePrompt)
        {
            var apiKey = App.Settings.SunoApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Suno API ключ не настроен. Откройте Настройки → AI генерация.");
            
            var tracks = new List<GeneratedTrack>();
            
            try
            {
                // Формируем полный промпт
                var fullPrompt = BuildFullPrompt(stylePrompt, negativePrompt, weirdness, styleAdherence);
                var finalLyrics = isInstrumental ? "[instrumental]" : lyrics;
                
                // Запрос к Suno API (custom mode)
                var payload = new
                {
                    prompt = fullPrompt,
                    lyrics = finalLyrics,
                    make_instrumental = isInstrumental,
                    wait_audio = false  // асинхронный режим
                };
                
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                _http.DefaultRequestHeaders.Clear();
                _http.DefaultRequestHeaders.Add("api-key", apiKey);
                
                var response = await _http.PostAsync($"{BaseUrl}/generate", content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Ошибка API: {response.StatusCode} - {responseText}");
                
                var result = JsonSerializer.Deserialize<SunoGenerateResponse>(responseText);
                
                if (result?.data == null || result.data.Count == 0)
                    throw new Exception("API вернул пустой ответ");
                
                // Создаём объекты для каждого варианта
                foreach (var item in result.data.Take(2))
                {
                    var track = new GeneratedTrack
                    {
                        TaskId = item.id,
                        Title = item.title ?? "Untitled",
                        StylePrompt = stylePrompt,
                        Lyrics = lyrics,
                        IsInstrumental = isInstrumental,
                        NegativePrompt = negativePrompt,
                        Weirdness = weirdness,
                        StyleAdherence = styleAdherence,
                        Status = "generating",
                        CreatedBy = AuthService.Current?.Id ?? "",
                        AudioUrl = item.audio_url ?? "",
                        ImageUrl = item.image_url ?? "",
                        Duration = item.duration
                    };
                    
                    tracks.Add(track);
                    _history.Insert(0, track);
                }
                
                Save();
                
                // Запускаем polling в фоне для каждого трека
                foreach (var track in tracks)
                    _ = PollStatusAsync(track);
                
                return tracks;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"MusicGen error: {ex.Message}");
                throw new Exception($"Не удалось создать песню: {ex.Message}");
            }
        }
        
        private static string BuildFullPrompt(string style, string negative, double weirdness, double adherence)
        {
            var parts = new List<string> { style };
            
            // Weirdness влияет на экспериментальность
            if (weirdness < 0.3)
                parts.Add("traditional arrangement");
            else if (weirdness > 0.7)
                parts.Add("experimental, unconventional structure");
            
            // StyleAdherence влияет на строгость следования стилю
            if (adherence > 0.8)
                parts.Add("strict genre adherence");
            else if (adherence < 0.4)
                parts.Add("genre-blending, fusion elements");
            
            var prompt = string.Join(", ", parts);
            
            if (!string.IsNullOrWhiteSpace(negative))
                prompt += $" [avoid: {negative}]";
            
            return prompt;
        }
        
        /// <summary>Опрашивает статус генерации до готовности.</summary>
        private static async Task PollStatusAsync(GeneratedTrack track)
        {
            var apiKey = App.Settings.SunoApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return;
            
            try
            {
                for (int i = 0; i < 60; i++)  // макс 5 минут (60 * 5сек)
                {
                    await Task.Delay(5000);  // проверяем каждые 5 секунд
                    
                    _http.DefaultRequestHeaders.Clear();
                    _http.DefaultRequestHeaders.Add("api-key", apiKey);
                    
                    var response = await _http.GetAsync($"{BaseUrl}/query?ids={track.TaskId}");
                    if (!response.IsSuccessStatusCode) continue;
                    
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<SunoQueryResponse>(json);
                    var item = result?.data?.FirstOrDefault();
                    
                    if (item == null) continue;
                    
                    track.Status = item.status;
                    track.AudioUrl = item.audio_url ?? track.AudioUrl;
                    track.ImageUrl = item.image_url ?? track.ImageUrl;
                    track.Duration = item.duration;
                    
                    if (item.status == "complete")
                    {
                        track.GeneratedAt = DateTime.Now;
                        await DownloadFilesAsync(track);
                        Save();
                        return;
                    }
                    
                    if (item.status == "error" || item.status == "failed")
                    {
                        track.ErrorMessage = item.error_message ?? "Unknown error";
                        Save();
                        return;
                    }
                }
                
                track.Status = "failed";
                track.ErrorMessage = "Превышено время ожидания (5 минут)";
                Save();
            }
            catch (Exception ex)
            {
                track.Status = "failed";
                track.ErrorMessage = ex.Message;
                Save();
            }
        }
        
        /// <summary>Скачивает аудио и обложку.</summary>
        private static async Task DownloadFilesAsync(GeneratedTrack track)
        {
            try
            {
                // Скачиваем аудио
                if (!string.IsNullOrWhiteSpace(track.AudioUrl))
                {
                    var audioBytes = await _http.GetByteArrayAsync(track.AudioUrl);
                    var audioPath = Path.Combine(GeneratedDir, $"{track.Id}.mp3");
                    File.WriteAllBytes(audioPath, audioBytes);
                    track.LocalPath = audioPath;
                }
                
                // Скачиваем обложку
                if (!string.IsNullOrWhiteSpace(track.ImageUrl))
                {
                    var imageBytes = await _http.GetByteArrayAsync(track.ImageUrl);
                    var imagePath = Path.Combine(GeneratedDir, $"{track.Id}.jpg");
                    File.WriteAllBytes(imagePath, imageBytes);
                    track.LocalImagePath = imagePath;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Download error for {track.Id}: {ex.Message}");
            }
        }
        
        /// <summary>Добавляет сгенерированный трек в основную библиотеку.</summary>
        public static void AddToLibrary(GeneratedTrack genTrack)
        {
            if (string.IsNullOrWhiteSpace(genTrack.LocalPath) || !File.Exists(genTrack.LocalPath))
                throw new Exception("Аудио файл не найден");
            
            var track = LibraryService.ImportSingle(genTrack.LocalPath, forceTitle: genTrack.Title);
            if (track == null)
                throw new Exception("Не удалось добавить в библиотеку");
            
            track.Artist = "AI Generated";
            track.Album = "Suno AI";
            track.Genre = "AI Music";
            
            // Ставим обложку
            if (!string.IsNullOrWhiteSpace(genTrack.LocalImagePath) && File.Exists(genTrack.LocalImagePath))
                LibraryService.SetCover(track, genTrack.LocalImagePath);
            
            LibraryService.Update(track);
            
            genTrack.AddedToLibrary = true;
            Save();
            
            AuthService.LogActivity("generate", $"Добавлена AI-песня «{track.DisplayTitle}»");
        }
        
        public static void Remove(GeneratedTrack track)
        {
            _history.Remove(track);
            
            // Удаляем файлы
            try { if (File.Exists(track.LocalPath)) File.Delete(track.LocalPath); } catch { }
            try { if (File.Exists(track.LocalImagePath)) File.Delete(track.LocalImagePath); } catch { }
            
            Save();
        }
        
        // DTO классы для Suno API
        
        private class SunoGenerateResponse
        {
            public List<SunoTrackData> data { get; set; }
        }
        
        private class SunoQueryResponse
        {
            public List<SunoTrackData> data { get; set; }
        }
        
        private class SunoTrackData
        {
            public string id { get; set; }
            public string title { get; set; }
            public string status { get; set; }
            public string audio_url { get; set; }
            public string image_url { get; set; }
            public double duration { get; set; }
            public string error_message { get; set; }
        }
    }
}
