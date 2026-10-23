using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Клип для фона плеера. Правила: строго MP4 и не длиннее 5 секунд —
    /// иначе сам файл превратится в мини-фильм, а нам нужен короткий «живой» фон.
    /// </summary>
    public static class ClipService
    {
        public const double MaxSeconds = 5.0;
        public const string Extension = ".mp4";

        public sealed class Result
        {
            public bool Ok;
            public string Error;
            public double Duration;
        }

        /// <summary>Читает длительность MP4 и проверяет ограничение в 5 секунд.</summary>
        public static async Task<Result> InspectAsync(string path)
        {
            var result = new Result();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                result.Error = "Файл не найден.";
                return result;
            }

            if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "Нужен файл строго в формате MP4.";
                return result;
            }

            MediaPlayer player = null;
            try
            {
                player = new MediaPlayer { Volume = 0 };
                var opened = new TaskCompletionSource<bool>();

                player.MediaOpened += (s, e) => opened.TrySetResult(true);
                player.MediaFailed += (s, e) => opened.TrySetResult(false);
                player.Open(new Uri(path, UriKind.Absolute));

                var finished = await Task.WhenAny(opened.Task, Task.Delay(6000));
                if (finished != opened.Task)
                {
                    result.Error = "Не удалось прочитать клип, файл слишком медленный или повреждён.";
                    return result;
                }

                if (!opened.Task.Result)
                {
                    result.Error = "Формат клипа не поддерживается системой.";
                    return result;
                }

                double seconds = player.NaturalDuration.HasTimeSpan
                    ? player.NaturalDuration.TimeSpan.TotalSeconds
                    : 0;

                result.Duration = seconds;

                if (seconds <= 0.05)
                {
                    result.Error = "Не удалось определить длительность клипа.";
                    return result;
                }

                if (seconds > MaxSeconds + 0.15)
                {
                    result.Error = string.Format(
                        "Клип длится {0:0.#} с — допустимо не более {1:0} с. Обрежьте ролик и попробуйте снова.",
                        seconds, MaxSeconds);
                    return result;
                }

                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "Ошибка: " + ex.Message;
                return result;
            }
            finally
            {
                try { player?.Close(); } catch { }
            }
        }
    }
}
