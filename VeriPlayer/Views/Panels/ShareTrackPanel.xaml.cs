using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QRCoder;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class ShareTrackPanel : UserControl, IModalPanel
    {
        private readonly Track _track;
        private bool _ready;

        public event EventHandler CloseRequested;
        public string Title => "Поделиться треком";

        public ShareTrackPanel(Track track)
        {
            InitializeComponent();
            _track = track;

            TrackTitle.Text = track.DisplayTitle;
            TrackArtist.Text = track.DisplayArtist;

            var cover = Storage.LoadImage(track.CoverPath, 160);
            if (cover != null)
            {
                CoverImage.Source = cover;
                CoverInitial.Visibility = Visibility.Collapsed;
            }
            else
            {
                CoverInitial.Text = string.IsNullOrEmpty(track.DisplayTitle)
                    ? "♪" : track.DisplayTitle.Substring(0, 1).ToUpperInvariant();
            }

            _ready = true;
            Rebuild();
        }

        private bool IncludeFile => ModeFull.IsChecked == true;

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (_ready) Rebuild();
        }

        private void Rebuild()
        {
            var nick = AuthService.Current?.Nick ?? "";
            string link;
            try
            {
                link = ShareService.BuildLink(_track, IncludeFile, nick);
            }
            catch (Exception ex)
            {
                LinkBox.Text = "";
                QrImage.Source = null;
                QrHint.Text = "Не удалось собрать ссылку: " + ex.Message;
                return;
            }

            LinkBox.Text = link;

            var lengthText = link.Length.ToString();
            if (IncludeFile && !string.IsNullOrEmpty(LinkBox.Text) && link.Length > 1200)
            {
                QrHint.Text = "Ссылка очень длинная (" + lengthText + " симв.), в QR не влезает.\nСкопируйте её текстом.";
                QrImage.Source = null;
                return;
            }

            try
            {
                QrImage.Source = RenderQr(link);
                QrHint.Text = "Наведите камеру, чтобы получить трек.";
            }
            catch (Exception ex)
            {
                QrImage.Source = null;
                QrHint.Text = "QR не поместился: " + ex.Message + "\nСкопируйте ссылку текстом.";
            }
        }

        /// <summary>Рисует QR-код через библиотеку QRCoder (надёжнее самописного кодера).</summary>
        private static BitmapSource RenderQr(string text)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(12);

            var image = new BitmapImage();
            using (var stream = new MemoryStream(png))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        private void CopyLink_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LinkBox.Text)) return;
            try
            {
                Clipboard.SetText(LinkBox.Text);
                MainWindow.Instance?.ShowToast("Ссылка скопирована", "ok");
            }
            catch
            {
                MainWindow.Instance?.ShowToast("Не удалось скопировать", "warn");
            }
        }

        private void SaveQr_Click(object sender, RoutedEventArgs e)
        {
            if (QrImage.Source is not BitmapSource bmp)
            {
                MainWindow.Instance?.ShowToast("QR-кода нет — ссылка слишком длинная.", "warn");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Сохранить QR-код",
                Filter = "Картинка PNG|*.png",
                FileName = SafeName(_track.DisplayTitle) + " - QR.png"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
                MainWindow.Instance?.ShowToast("QR-код сохранён", "ok");
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToast("Не удалось сохранить: " + ex.Message, "error");
            }
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "track";
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
