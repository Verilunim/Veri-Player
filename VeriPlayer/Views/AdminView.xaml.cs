using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views
{
    public partial class AdminView : UserControl
    {
        private readonly List<UserRow> _rows = new List<UserRow>();
        private bool _roleGuard;
        private string _selectedId;

        public event Action RequestOpenPlayer;

        public AdminView()
        {
            InitializeComponent();

            // список всегда актуален, как только панель показывается
            IsVisibleChanged += (s, e) => { if (IsVisible) Refresh(); };
        }

        // ================================================================== обновление

        public void Refresh()
        {
            BuildStats();

            var filter = (UserSearch.Text ?? "").Trim().ToLowerInvariant();
            var rows = new List<UserRow>();

            foreach (var user in AuthService.SortedUsers())
            {
                if (filter.Length > 0 && !user.Nick.ToLowerInvariant().Contains(filter)) continue;

                var data = UserDataService.For(user.Id);
                rows.Add(new UserRow
                {
                    User = user,
                    TrackCount = LibraryService.Tracks.Count(t => t.AddedById == user.Id),
                    PlaylistCount = data.Playlists.Count,
                    FavoriteCount = data.Favorites.Count
                });
            }

            _rows.Clear();
            _rows.AddRange(rows);

            UserList.ItemsSource = null;
            UserList.ItemsSource = _rows;
            NoUsers.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_selectedId != null)
            {
                var again = _rows.FirstOrDefault(r => r.User.Id == _selectedId);
                if (again != null) UserList.SelectedItem = again;
                else ShowDetail(null);
            }
            else
            {
                ShowDetail(null);
            }
        }

        private void BuildStats()
        {
            Stats.Children.Clear();
            Stats.ColumnDefinitions.Clear();

            var users = AuthService.Users;
            var staff = users.Count(u => u.IsStaff);
            var banned = users.Count(u => u.IsBanned);

            var cells = new (string Value, string Label)[]
            {
                (users.Count.ToString(), "пользователей"),
                (staff.ToString(), "команда"),
                (banned.ToString(), "заблокировано"),
                (LibraryService.Tracks.Count.ToString(), "треков в библиотеке"),
                (Storage.Human(LibraryService.TotalSize()), "объём музыки")
            };

            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) Stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = cells[i].Value,
                    FontSize = 24,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AccentTextBrush")
                });
                stack.Children.Add(new TextBlock
                {
                    Text = cells[i].Label,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 2, 0, 0),
                    Foreground = (Brush)FindResource("TextFaintBrush")
                });

                Grid.SetColumn(stack, i * 2);
                Stats.Children.Add(stack);
            }
        }

        private void UserSearch_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        // ================================================================== выбор

        private void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowDetail(UserList.SelectedItem as UserRow);
        }

        private void ShowDetail(UserRow row)
        {
            if (row == null)
            {
                _selectedId = null;
                DetailScroll.Visibility = Visibility.Collapsed;
                NothingSelected.Visibility = Visibility.Visible;
                return;
            }

            _selectedId = row.User.Id;
            NothingSelected.Visibility = Visibility.Collapsed;
            DetailScroll.Visibility = Visibility.Visible;
            DetailScroll.DataContext = row;

            ActivityList.ItemsSource = null;
            ActivityList.ItemsSource = row.User.Activity;

            _roleGuard = true;
            foreach (var item in RoleBox.Items)
            {
                if (item is ComboBoxItem ci && ci.Tag as string == ((int)row.User.Role).ToString())
                {
                    RoleBox.SelectedItem = ci;
                    break;
                }
            }
            _roleGuard = false;

            UpdateActions(row);
        }

        private UserRow Current => UserList.SelectedItem as UserRow;

        private void UpdateActions(UserRow row)
        {
            var me = AuthService.Current;
            if (me == null || row == null) return;

            var target = row.User;
            bool isOwner = me.Role == UserRole.Owner;
            bool self = target.Id == me.Id;
            bool targetStaff = target.IsStaff;

            bool canModerate = isOwner || !targetStaff;
            bool canChangeRole = isOwner && !self;
            bool canBan = !self && canModerate;
            bool canReset = canModerate;
            bool canDelete = !self && canModerate && !(target.Role == UserRole.Owner &&
                                                       AuthService.Users.Count(u => u.Role == UserRole.Owner) <= 1);

            RoleBox.IsEnabled = canChangeRole;
            BanButton.IsEnabled = canBan;
            ResetButton.IsEnabled = canReset;
            DeleteButton.IsEnabled = canDelete;

            BanButton.Content = target.IsBanned ? "Разблокировать" : "Заблокировать";

            string hint;
            if (self) hint = "Это ваш аккаунт — изменить роль, заблокировать или удалить себя нельзя.";
            else if (!isOwner && targetStaff) hint = "Менять роли и модерировать команду может только владелец.";
            else hint = "Все действия записываются в журнал пользователя.";

            PermissionHint.Text = hint;
        }

        // ================================================================== действия

        private void Role_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_roleGuard) return;
            var row = Current;
            if (row == null || RoleBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;

            var role = (UserRole)int.Parse(tag);
            if (role == row.User.Role) return;

            AuthService.SetRole(row.User, role);
            MainWindow.Instance?.ShowToast("Роль обновлена: " + row.User.Nick + " — " + row.User.RoleLabel, "ok");
            Refresh();
        }

        private void Ban_Click(object sender, RoutedEventArgs e)
        {
            var row = Current;
            if (row == null) return;

            bool banned = !row.User.IsBanned;
            var question = banned
                ? "Заблокировать пользователя «" + row.User.Nick + "»?"
                : "Разблокировать пользователя «" + row.User.Nick + "»?";

            MainWindow.Instance?.Confirm(question, () =>
            {
                AuthService.SetBanned(row.User, banned);
                MainWindow.Instance?.ShowToast(banned ? "Пользователь заблокирован" : "Пользователь разблокирован", "info");
                Refresh();
            }, banned ? "Заблокировать" : "Разблокировать");
        }

        private void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            var row = Current;
            if (row == null) return;

            var password = GeneratePassword();
            MainWindow.Instance?.Confirm("Сбросить пароль пользователю «" + row.User.Nick + "»?", () =>
            {
                AuthService.ResetPassword(row.User, password);
                ShowPassword(row.User.Nick, password);
                Refresh();
            }, "Сбросить");
        }

        private void ShowPassword(string nick, string password)
        {
            var stack = new StackPanel { Margin = new Thickness(26, 4, 26, 24), Width = 420 };

            stack.Children.Add(new TextBlock
            {
                Text = "Новый пароль для «" + nick + "»:",
                Style = (Style)FindResource("TextMuted")
            });

            var box = new TextBox
            {
                Text = password,
                IsReadOnly = true,
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(box);

            stack.Children.Add(new TextBlock
            {
                Text = "Сохраните его и передайте пользователю — позже пароль не показывается повторно.",
                Style = (Style)FindResource("TextCaption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var copy = new Button
            {
                Content = "Скопировать",
                Style = (Style)FindResource("BtnGhost")
            };
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(password); MainWindow.Instance?.ShowToast("Пароль скопирован", "ok"); }
                catch { }
            };
            buttons.Children.Add(copy);

            var done = new Button
            {
                Content = "Готово",
                Style = (Style)FindResource("BtnPrimary"),
                Margin = new Thickness(10, 0, 0, 0)
            };
            done.Click += (s, e) => MainWindow.Instance?.CloseModal();
            buttons.Children.Add(done);

            stack.Children.Add(buttons);

            MainWindow.Instance?.ShowModal(stack, "Пароль сброшен");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var row = Current;
            if (row == null) return;

            MainWindow.Instance?.Confirm(
                "Удалить аккаунт «" + row.User.Nick + "»? Музыка останется, но профиль, избранное и плейлисты пропадут.",
                () =>
                {
                    var error = AuthService.Delete(row.User);
                    if (!string.IsNullOrEmpty(error))
                    {
                        MainWindow.Instance?.ShowToast(error, "error");
                        return;
                    }
                    UserDataService.Drop(row.User.Id);
                    MainWindow.Instance?.ShowToast("Аккаунт удалён", "info");
                    _selectedId = null;
                    Refresh();
                });
        }

        private void OpenPlayer_Click(object sender, RoutedEventArgs e) => RequestOpenPlayer?.Invoke();

        private static string GeneratePassword()
        {
            const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            // System.Random предсказуем — для паролей нужен криптографический генератор
            var chars = new char[12];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
            return new string(chars);
        }
    }
}
