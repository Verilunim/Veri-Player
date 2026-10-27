using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class ProfilePanel : UserControl, IModalPanel
    {
        public event EventHandler CloseRequested;
        public string Title => "Мой профиль";

        public ProfilePanel()
        {
            InitializeComponent();
            Fill();
        }

        private void Fill()
        {
            var user = AuthService.Current;
            if (user == null) return;

            Avatar.Source = user.AvatarPath;
            Avatar.Initial = user.Initial;
            NickBox.Text = user.Nick;
            BioBox.Text = user.Bio;

            var data = UserDataService.Current;
            var minutes = ListeningService.TotalMinutes(data);
            StatMinutes.Text = ListeningService.FormatMinutes(minutes);
            StatHours.Text = minutes >= 60
                ? "это " + (int)(minutes / 60) + " ч " + ((int)minutes % 60).ToString("00") + " мин музыки"
                : "музыки за всё время";
            var taste = TasteService.Build(data);
            StatArtist.Text = taste.TopArtist ?? "пока не ясно";
            StatGenre.Text = taste.TopGenre ?? "пока не ясно";

            MetaText.Text = user.RoleLabel +
                            "  ·  в Veri Player с " + user.CreatedText +
                            "  ·  входов: " + user.LoginCount +
                            "  ·  в избранном: " + data.Favorites.Count;
        }

        private void ChangeAvatar_Click(object sender, RoutedEventArgs e)
        {
            var user = AuthService.Current;
            if (user == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите фото",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            var saved = Storage.SaveSquareAvatar(dialog.FileName);
            if (saved == null)
            {
                MainWindow.Instance?.ShowToast("Не удалось прочитать изображение", "error");
                return;
            }

            Storage.SafeDelete(user.AvatarPath);
            user.AvatarPath = saved;
            AuthService.LogActivity(user, "profile", "Обновлён аватар");
            AuthService.Save();

            Avatar.Source = saved;
            Avatar.Initial = user.Initial;
            MainWindow.Instance?.RefreshUserCard();
            MainWindow.Instance?.ShowToast("Аватар обновлён", "ok");
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var user = AuthService.Current;
            if (user == null) return;

            var nick = (NickBox.Text ?? "").Trim();
            if (nick.Length < 3 || nick.Length > 24)
            {
                MainWindow.Instance?.ShowToast("Ник должен быть от 3 до 24 символов", "warn");
                return;
            }
            if (nick.Any(c => "\\/:*?\"<>|".Contains(c)))
            {
                MainWindow.Instance?.ShowToast("Ник содержит недопустимые символы", "warn");
                return;
            }

            var existing = AuthService.Find(nick);
            if (existing != null && existing.Id != user.Id)
            {
                MainWindow.Instance?.ShowToast("Такое имя уже занято", "warn");
                return;
            }

            bool nickChanged = nick != user.Nick;
            user.Nick = nick;
            user.Bio = (BioBox.Text ?? "").Trim();
            if (nickChanged) AuthService.LogActivity(user, "profile", "Ник изменён на «" + nick + "»");
            else AuthService.LogActivity(user, "profile", "Обновлён профиль");
            AuthService.Save();

            MainWindow.Instance?.RefreshUserCard();
            MainWindow.Instance?.ShowToast("Профиль сохранён", "ok");
            Fill();
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            var user = AuthService.Current;
            if (user == null) return;

            if (!AuthService.CheckPassword(user, OldPassword.Password))
            {
                MainWindow.Instance?.ShowToast("Текущий пароль неверный", "error");
                return;
            }
            var error = AuthService.ValidatePassword(NewPassword.Password, ConfirmPassword.Password, user.IsStaff);
            if (error != null)
            {
                MainWindow.Instance?.ShowToast(error, "warn");
                return;
            }

            AuthService.ChangePassword(user, NewPassword.Password);
            OldPassword.Clear();
            NewPassword.Clear();
            ConfirmPassword.Clear();
            MainWindow.Instance?.ShowToast("Пароль изменён", "ok");
        }

        private void Logout_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.Logout();

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
