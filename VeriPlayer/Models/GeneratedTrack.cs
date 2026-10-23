using System;

namespace VeriPlayer.Models
{
    /// <summary>Трек, созданный через AI генератор музыки (Suno).</summary>
    public class GeneratedTrack
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "AI Generated";
        
        /// <summary>Промпт стиля песни (например "energetic pop with 80s synth").</summary>
        public string StylePrompt { get; set; } = "";
        
        /// <summary>Текст песни или пустая строка для инструментала.</summary>
        public string Lyrics { get; set; } = "";
        
        /// <summary>Инструментальная песня (без текста).</summary>
        public bool IsInstrumental { get; set; }
        
        /// <summary>Негативный промпт — стили/инструменты для исключения.</summary>
        public string NegativePrompt { get; set; } = "";
        
        /// <summary>Странность/экспериментальность (0.0-1.0, дефолт 0.5).</summary>
        public double Weirdness { get; set; } = 0.5;
        
        /// <summary>Вовлечение в стиль (0.0-1.0, дефолт 0.7).</summary>
        public double StyleAdherence { get; set; } = 0.7;
        
        /// <summary>Статус: pending, generating, ready, failed.</summary>
        public string Status { get; set; } = "pending";
        
        /// <summary>URL аудио файла (после генерации).</summary>
        public string AudioUrl { get; set; } = "";
        
        /// <summary>Локальный путь к скачанному MP3.</summary>
        public string LocalPath { get; set; } = "";
        
        /// <summary>Длительность в секундах.</summary>
        public double Duration { get; set; }
        
        /// <summary>Обложка (URL от Suno).</summary>
        public string ImageUrl { get; set; } = "";
        
        /// <summary>Локальный путь к обложке.</summary>
        public string LocalImagePath { get; set; } = "";
        
        /// <summary>Когда создан запрос.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>Когда генерация завершена.</summary>
        public DateTime? GeneratedAt { get; set; }
        
        /// <summary>ID задачи в Suno API.</summary>
        public string TaskId { get; set; } = "";
        
        /// <summary>Сообщение об ошибке (если failed).</summary>
        public string ErrorMessage { get; set; } = "";
        
        /// <summary>Кем создан (userId).</summary>
        public string CreatedBy { get; set; } = "";
        
        /// <summary>Добавлен ли в основную библиотеку.</summary>
        public bool AddedToLibrary { get; set; }
    }
}
