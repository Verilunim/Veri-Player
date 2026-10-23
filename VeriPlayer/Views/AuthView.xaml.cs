using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views
{
    public partial class AuthView : UserControl
    {
        private string _avatarSource;
        private bool _loginMode = true;
        private bool _ownerSetup;

        public event Action<UserAccount> Authenticated;

        public AuthView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                ShowLogin(true, false);
                Dispatcher.BeginInvoke(new Action(() => UpdateUnderline(false)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }

        public void Prepare()
        {
            LoginError.Visibility = Visibility.Collapsed;
            RegError.Visibility = Visibility.Collapsed;
            LoginNick.Clear();
            LoginPassword.Clear();
            ApplyOwnerSetup();
            if (_ownerSetup) ShowLogin(false, false);
            else LoginNick.Focus();
        }

        // ------------------------------------------------------------------ первый запуск

        /// <summary>Пока нет владельца с паролем, форма регистрации создаёт владельца.</summary>
        private void ApplyOwnerSetup()
        {
            _ownerSetup = AuthService.NeedsOwnerSetup;
            var pending = _ownerSetup ? AuthService.PendingOwner : null;

            TabHost.Visibility = _ownerSetup ? Visibility.Collapsed : Visibility.Visible;
            RegLoginLink.Visibility = _ownerSetup ? Visibility.Collapsed : Visibility.Visible;
            RegTitle.Text = _ownerSetup ? "Первый запуск" : "Создать аккаунт";
            RegSubtitle.Text = pending != null
                ? "Задайте новый пароль для владельца «" + pending.Nick + "». Стандартный пароль больше не действует."
                : _ownerSetup
                    ? "Создайте аккаунт владельца: придумайте ник и пароль."
                    : "Ник, пароль, аватар и пара слов о себе.";
            BtnRegister.Content = _ownerSetup ? "Продолжить" : "Создать аккаунт";

            RegNick.Text = pending?.Nick ?? "";
            RegNick.IsReadOnly = pending != null;
            RegPassword.Clear();
            RegPassword2.Clear();
        }

        // ------------------------------------------------------------------ вкладки

        private void TabLogin_Click(object sender, RoutedEventArgs e) => ShowLogin(true);
        private void TabRegister_Click(object sender, RoutedEventArgs e) => ShowLogin(false);
        private void LinkToRegister_Click(object sender, RoutedEventArgs e) => ShowLogin(false);
        private void LinkToLogin_Click(object sender, RoutedEventArgs e) => ShowLogin(true);

        private void ShowLogin(bool login, bool animate = true)
        {
            if (_ownerSetup) login = false;
            _loginMode = login;
            LoginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
            RegisterPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;

            UpdateUnderline(animate);

            if (login)
            {
                LoginError.Visibility = Visibility.Collapsed;
                LoginNick.Focus();
            }
            else
            {
                RegError.Visibility = Visibility.Collapsed;
                RegNick.Focus();
            }
        }

        private void TabHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateUnderline(false);

        /// <summary>Ставит подчёркивание ровно под выбранной вкладкой по её фактическому размеру.</summary>
        private void UpdateUnderline(bool animate)
        {
            var tab = _loginMode ? (FrameworkElement)TabLogin : TabRegister;
            if (tab == null || TabUnderline == null) return;
            if (tab.ActualWidth <= 0) return; // разметка ещё не посчитана — придёт SizeChanged

            const double inset = 15;
            double x = tab.TranslatePoint(new Point(0, 0), TabHost).X + inset;
            double width = Math.Max(18, tab.ActualWidth - inset * 2);

            TabUnderline.Width = width;
            if (animate)
                Animate(TabUnderlineShift, System.Windows.Media.TranslateTransform.XProperty, x, 0.28);
            else
                TabUnderlineShift.X = x;
        }

        private static void Animate(System.Windows.Media.TranslateTransform target,
            System.Windows.DependencyProperty property, double to, double seconds)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            target.BeginAnimation(property, animation);
        }

        // ------------------------------------------------------------------ аватар

        private void PickAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите аватар",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            _avatarSource = dialog.FileName;
            RegAvatar.Source = _avatarSource;
            RegAvatar.Initial = RegNick.Text.Length > 0 ? RegNick.Text : "V";
            RegAvatarHint.Text = System.IO.Path.GetFileName(_avatarSource);
        }

        private void ShowError(StackPanel panel, TextBlock text, string message)
        {
            text.Text = message;
            panel.Visibility = Visibility.Visible;
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25));
            panel.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        // ------------------------------------------------------------------ вход

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var (ok, error, user) = AuthService.Login(LoginNick.Text, LoginPassword.Password);
            if (!ok)
            {
                ShowError(LoginError, LoginErrorText, error);
                return;
            }
            LoginError.Visibility = Visibility.Collapsed;
            Authenticated?.Invoke(user);
        }

        // ------------------------------------------------------------------ регистрация

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            RegAvatar.Initial = RegNick.Text.Length > 0 ? RegNick.Text : "V";
            var (ok, error, user) = _ownerSetup
                ? AuthService.SetupOwner(RegNick.Text, RegPassword.Password, RegPassword2.Password, RegBio.Text, _avatarSource)
                : AuthService.Register(RegNick.Text, RegPassword.Password, RegPassword2.Password, RegBio.Text, _avatarSource);

            if (!ok)
            {
                ShowError(RegError, RegErrorText, error);
                return;
            }

            RegError.Visibility = Visibility.Collapsed;
            _avatarSource = null;
            ApplyOwnerSetup();
            Authenticated?.Invoke(user);
        }

        // ------------------------------------------------------------------ прочее

        private void Nick_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnLogin_Click(sender, new RoutedEventArgs());
        }
    }
}
