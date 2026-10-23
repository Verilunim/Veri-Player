using System;
using System.ComponentModel;
using System.Windows.Media;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views
{
    public class NavItem : INotifyPropertyChanged
    {
        public string Tag { get; set; }
        public string Title { get; set; }
        public Geometry Icon { get; set; }

        private int _count;
        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
            }
        }

        public string CountText => _count > 0 ? _count.ToString() : "";

        private bool _locked;
        /// <summary>Пункт требует Pro+ — рядом показывается замок.</summary>
        public bool Locked
        {
            get => _locked;
            set
            {
                _locked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Locked)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LockVisibility)));
            }
        }

        public System.Windows.Visibility LockVisibility =>
            _locked ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class TrackRow : INotifyPropertyChanged
    {
        public Track Track { get; }
        public int Index { get; set; }
        public string PlaylistId { get; set; }

        public TrackRow(Track track, int index)
        {
            Track = track;
            Index = index;
            CoverBrush = ThemeService.CoverBrush(track.Id);
        }

        public string Id => Track.Id;
        public string Title => Track.DisplayTitle;
        public string Artist => Track.DisplayArtist;
        public string Album => Track.DisplayAlbum;
        public string DurationText => Track.DurationText;
        public string AddedByName => Track.AddedByName;
        public string IndexText => (Index + 1).ToString("00");
        public bool Missing => !Track.Exists;
        public Brush CoverBrush { get; }
        public string Initial => Title.Length > 0 ? Title.Substring(0, 1).ToUpperInvariant() : "♪";

        private ImageSource _cover;
        private bool _coverLoaded;
        public ImageSource Cover
        {
            get
            {
                if (!_coverLoaded)
                {
                    _coverLoaded = true;
                    if (!string.IsNullOrWhiteSpace(Track.CoverPath))
                        _cover = Storage.LoadImage(Track.CoverPath, 120);
                }
                return _cover;
            }
        }

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent == value) return; _isCurrent = value; Raise(nameof(IsCurrent)); }
        }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set { if (_isFavorite == value) return; _isFavorite = value; Raise(nameof(IsFavorite)); }
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class UserRow
    {
        public UserAccount User { get; set; }
        public int TrackCount { get; set; }
        public int PlaylistCount { get; set; }
        public int FavoriteCount { get; set; }
        public string StatsText => TrackCount + " треков · " + PlaylistCount + " плейлистов · " + FavoriteCount + " в избранном";
        public string RoleLabel => User.RoleLabel;
        public string Nick => User.Nick;
        public string Initial => User.Initial;
        public string AvatarPath => User.AvatarPath;
        public bool IsBanned => User.IsBanned;
        public string Bio => User.BioOrEmpty;
        public string CreatedText => User.CreatedText;
        public string LastLoginText => User.LastLoginText;
        public string LoginCountText => User.LoginCount + " входов";
        public bool IsPro => LicenseService.For(User).Active;
        public string ProText => LicenseService.For(User).Text;
    }
}
