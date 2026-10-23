using System;
using System.Windows;
using System.Windows.Controls;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class AddByLinkPanel : UserControl, IModalPanel
    {
        private ShareService.Payload _payload;

        public event EventHandler CloseRequested;
        public string Title => "Добавить по ссылке";

        public AddByLinkPanel()
        {
            InitializeComponent();
            Loaded += (s, e) => LinkBox.Focus();
        }

        private void Link_Changed(object sender, TextChangedEventArgs e)
        {
            var text = LinkBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(text))
            {
                _payload = null;
                PreviewCard.Visibility = Visibility.Collapsed;
                StatusText.Text = "";
                AddButton.IsEnabled = false;
                return;
            }

            if (ShareService.TryParse(text, out var payload, out var error))
            {
                _payload = payload;
                PreviewTitle.Text = payload.Artist + " — " + payload.Title;
                PreviewInfo.Text = "Длительность: " +
                    (payload.Duration > 0 ? TimeSpan.FromSeconds(payload.Duration).ToString(@"m\:ss") : "неизвестно") +
                    (string.IsNullOrEmpty(payload.Data) ? " · только метаданные" : " · файл внутри") +
                    (string.IsNullOrWhiteSpace(payload.From) ? "" : " · от " + payload.From);
                PreviewCard.Visibility = Visibility.Visible;
                StatusText.Text = string.IsNullOrEmpty(payload.Data)
                    ? "В ссылке нет самого файла: трек добавится, но музыку нужно будет положить вручную."
                    : "Всё готово — можно добавить.";
                AddButton.IsEnabled = true;
            }
            else
            {
                _payload = null;
                PreviewCard.Visibility = Visibility.Collapsed;
                StatusText.Text = error;
                AddButton.IsEnabled = false;
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    LinkBox.Text = Clipboard.GetText().Trim();
                    LinkBox.CaretIndex = LinkBox.Text.Length;
                }
                else
                {
                    StatusText.Text = "Буфер обмена пуст.";
                }
            }
            catch
            {
                StatusText.Text = "Не удалось прочитать буфер обмена.";
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => LinkBox.Text = "";

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var text = LinkBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            var (ok, error, track) = ShareService.AddFromLink(text);
            if (!ok)
            {
                StatusText.Text = error;
                MainWindow.Instance?.ShowToast(error, "error");
                return;
            }

            MainWindow.Instance?.RefreshAll();
            MainWindow.Instance?.ShowToast("Трек добавлен: " + track.DisplayTitle, "ok");
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
