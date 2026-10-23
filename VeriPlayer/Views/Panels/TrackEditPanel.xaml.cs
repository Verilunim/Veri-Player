using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class TrackEditPanel : UserControl, IModalPanel
    {
        private readonly Track _track;

        public event EventHandler CloseRequested;
        public string Title => "Информация о треке";

        public TrackEditPanel(Track track)
        {
            InitializeComponent();
            _track = track;

            TitleBox.Text = track.Title;
            ArtistBox.Text = track.Artist;
            AlbumBox.Text = track.Album;
            GenreBox.Text = track.Genre;
            YearBox.Text = track.Year > 0 ? track.Year.ToString(CultureInfo.InvariantCulture) : "";

            FileNameText.Text = track.FilePath;
            FileMetaText.Text = track.DurationText + "  ·  " + track.SizeText +
                                (string.IsNullOrWhiteSpace(track.AddedByName) ? "" : "  ·  добавил " + track.AddedByName);

            var cover = Storage.LoadImage(track.CoverPath, 200);
            if (cover != null)
            {
                CoverImage.Source = cover;
                CoverInitial.Visibility = Visibility.Collapsed;
                CoverHost.Background = System.Windows.Media.Brushes.Transparent;
            }
            else
            {
                CoverHost.Background = ThemeService.CoverBrush(track.Id);
                CoverInitial.Text = track.DisplayTitle.Length > 0
                    ? track.DisplayTitle.Substring(0, 1).ToUpperInvariant()
                    : "♪";
            }

            UpdateMediaState();
            LibraryService.Changed += OnLibraryChanged;

            Loaded += (s, e) =>
            {
                TitleBox.Focus();
                TitleBox.SelectAll();
            };

            Unloaded += (s, e) => LibraryService.Changed -= OnLibraryChanged;
        }

        // ================================================================== обложка и клип

        private void OnLibraryChanged() => UpdateMediaState();

        private void UpdateCoverPreview()
        {
            var cover = Storage.LoadImage(_track.CoverPath, 200);
            if (cover != null)
            {
                CoverImage.Source = cover;
                CoverInitial.Visibility = Visibility.Collapsed;
                CoverHost.Background = System.Windows.Media.Brushes.Transparent;
            }
            else
            {
                CoverImage.Source = null;
                CoverInitial.Visibility = Visibility.Visible;
                CoverHost.Background = ThemeService.CoverBrush(_track.Id);
                CoverInitial.Text = _track.DisplayTitle.Length > 0
                    ? _track.DisplayTitle.Substring(0, 1).ToUpperInvariant()
                    : "♪";
            }
        }

        private void UpdateMediaState()
        {
            if (!IsLoaded && !IsInitialized) return;
            if (CoverStateText == null) return;

            UpdateCoverPreview();

            var thumb = Storage.LoadImage(_track.CoverPath, 120);
            MediaCoverImage.Source = thumb;
            MediaCoverInitial.Visibility = thumb == null ? Visibility.Visible : Visibility.Collapsed;
            MediaCoverInitial.Text = _track.DisplayTitle.Length > 0
                ? _track.DisplayTitle.Substring(0, 1).ToUpperInvariant()
                : "♪";

            CoverStateText.Text = _track.HasCover ? "есть · " + Path.GetFileName(_track.CoverPath) : "нет";
            CoverClearButton.IsEnabled = _track.HasCover;

            bool hasClip = _track.HasVideo;
            ClipStateText.Text = hasClip ? "есть · " + Path.GetFileName(_track.VideoPath) : "нет · MP4 до 5 с";
            ClipPickButton.Content = hasClip ? "Заменить" : "Прикрепить";
            ClipClearButton.IsEnabled = hasClip;
        }

        private void CoverPick_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.PickCover(_track);
            UpdateMediaState();
        }

        private void CoverClear_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ClearCover(_track);
            UpdateMediaState();
        }

        private void ClipPick_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.PickClip(_track);
        }

        private void ClipClear_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ClearClip(_track);
            UpdateMediaState();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _track.Title = (TitleBox.Text ?? "").Trim();
            _track.Artist = (ArtistBox.Text ?? "").Trim();
            _track.Album = (AlbumBox.Text ?? "").Trim();
            _track.Genre = (GenreBox.Text ?? "").Trim();

            if (int.TryParse((YearBox.Text ?? "").Trim(), out var year) && year > 0 && year <= 2100)
                _track.Year = year;
            else
                _track.Year = 0;

            LibraryService.Update(_track);
            AuthService.LogActivity(AuthService.Current, "library", "Изменён трек «" + _track.DisplayTitle + "»");

            MainWindow.Instance?.RefreshAll();
            MainWindow.Instance?.ShowToast("Трек обновлён", "ok");
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
