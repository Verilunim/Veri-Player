using System.ComponentModel;
using System.Windows.Media;
using VeriPlayer.Services;

namespace VeriPlayer.Views
{
    /// <summary>Строка результата поиска: обложка и состояние скачивания меняются на лету.</summary>
    public class SearchRow : INotifyPropertyChanged
    {
        public SearchRow(MusicSearchService.Result result)
        {
            Result = result;
            Downloaded = MusicSearchService.FindDownloaded(result) != null;
        }

        public MusicSearchService.Result Result { get; }
        public string Title => Result.Title;
        public string Meta => (Result.Artist.Length > 0 ? Result.Artist : "Неизвестный автор") + "  ·  " + Result.Source +
                              (Result.License.Length > 0 ? "  ·  " + Result.License : "");
        public string Duration => Result.DurationText;
        public string Initial => Result.Title.Length > 0 ? Result.Title.Substring(0, 1).ToUpperInvariant() : "♪";

        private ImageSource _cover;
        public ImageSource Cover { get => _cover; set { _cover = value; Changed(nameof(Cover)); } }

        private bool _downloading;
        public bool Downloading { get => _downloading; set { _downloading = value; Changed(nameof(Downloading)); Changed(nameof(ActionText)); } }

        private bool _downloaded;
        public bool Downloaded { get => _downloaded; set { _downloaded = value; Changed(nameof(Downloaded)); Changed(nameof(ActionText)); } }

        private double _progress;
        public double Progress { get => _progress; set { _progress = value; Changed(nameof(ActionText)); } }

        private bool _playing;
        public bool Playing { get => _playing; set { _playing = value; Changed(nameof(Playing)); } }

        public string ActionText => Downloaded ? "В библиотеке" : Downloading ? (Progress > 0 ? (int)(Progress * 100) + "%" : "…") : "Скачать";

        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
