using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>Свои фоны пользователя. Минимум 1920×1080 — меньше не принимаем.</summary>
    public static class BackgroundService
    {
        public const int MinWidth = 1920;
        public const int MinHeight = 1080;

        public static List<BackgroundImage> Items { get; private set; } = new List<BackgroundImage>();
        public static event Action Changed;

        public static void Init()
        {
            Items = Storage.Load(Storage.BackgroundsFile, new List<BackgroundImage>()) ?? new List<BackgroundImage>();
            Items.RemoveAll(i => !i.HasFile);

            var active = Active;
            if (active == null && App.Settings.BackgroundId.Length > 0)
                App.Settings.BackgroundId = "";
        }

        public static void Save()
        {
            Storage.Save(Storage.BackgroundsFile, Items);
            Changed?.Invoke();
        }

        public static BackgroundImage Active =>
            Items.FirstOrDefault(i => i.Id == App.Settings.BackgroundId && i.HasFile);

        // ------------------------------------------------------------------ import

        public sealed class ImportOutcome
        {
            public bool Ok;
            public string Error;
            public BackgroundImage Image;
        }

        public static ImportOutcome Import(string sourcePath)
        {
            var result = new ImportOutcome();
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    result.Error = "Файл не найден.";
                    return result;
                }

                var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".tif", ".tiff" };
                if (!allowed.Contains(ext))
                {
                    result.Error = "Поддерживаются изображения: JPG, PNG, BMP, WEBP, GIF, TIFF.";
                    return result;
                }

                var size = Storage.ReadPixelSize(sourcePath);
                if (size.width <= 0 || size.height <= 0)
                {
                    result.Error = "Не удалось прочитать изображение.";
                    return result;
                }

                if (size.width < MinWidth || size.height < MinHeight)
                {
                    result.Error = string.Format(
                        "Слишком маленькое изображение: {0} × {1}. Нужно минимум {2} × {3}.",
                        size.width, size.height, MinWidth, MinHeight);
                    return result;
                }

                var copied = Storage.ImportBackgroundFile(sourcePath);
                var info = new FileInfo(copied);

                var image = new BackgroundImage
                {
                    Title = Path.GetFileNameWithoutExtension(sourcePath),
                    FilePath = copied,
                    PixelWidth = size.width,
                    PixelHeight = size.height,
                    FileSize = info.Length,
                    AddedAt = DateTime.Now,
                    AddedByName = AuthService.Current?.Nick ?? "system"
                };

                Items.Insert(0, image);
                App.Settings.BackgroundId = image.Id;
                App.SaveSettings();
                Save();

                result.Ok = true;
                result.Image = image;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "Ошибка: " + ex.Message;
                return result;
            }
        }

        public static void Remove(BackgroundImage image)
        {
            if (image == null) return;
            Storage.SafeDelete(image.FilePath);
            Items.Remove(image);
            if (App.Settings.BackgroundId == image.Id)
                App.Settings.BackgroundId = Items.FirstOrDefault()?.Id ?? "";
            App.SaveSettings();
            Save();
        }

        public static void SetActive(string id)
        {
            App.Settings.BackgroundId = id ?? "";
            App.SaveSettings();
            Changed?.Invoke();
        }
    }
}
