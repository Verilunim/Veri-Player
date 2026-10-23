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
            if (NewPassword.Password.Length < 4)
            {
                MainWindow.Instance?.ShowToast("Новый пароль слишком короткий (минимум 4 символа)", "warn");
                return;
            }
            if (NewPassword.Password != ConfirmPassword.Password)
            {
                MainWindow.Instance?.ShowToast("Пароли не совпадают", "warn");
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
