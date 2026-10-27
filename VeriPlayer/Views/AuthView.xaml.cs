using System;
using System.Linq;
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
        /// <summary>Нет ни одного аккаунта с паролем — вкладка «Вход» не нужна.</summary>
        private bool _setupOnly;

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
            // видео идёт только пока экран входа на виду — после входа файл отпускаем
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) StartVideo();
                else StopVideo();
            };
        }

        // ------------------------------------------------------------------ видео на фоне

        private const string VideoResource = "VeriPlayer.auth-background.mp4";

        /// <summary>
        /// Видео зашито в exe. MediaElement играет только файлы, поэтому при первом показе
        /// (или если файл повреждён/устарел) оно распаковывается в папку данных программы.
        /// </summary>
        private static string FindVideo()
        {
            using var stream = typeof(AuthView).Assembly.GetManifestResourceStream(VideoResource);
            if (stream == null) return null;

            var path = System.IO.Path.Combine(Storage.Dir("cache"), "auth-background.mp4");
            var file = new System.IO.FileInfo(path);
            if (file.Exists && file.Length == stream.Length) return path;

            var temp = path + ".tmp";
            using (var output = System.IO.File.Create(temp)) stream.CopyTo(output);
            System.IO.File.Move(temp, path, true);
            return path;
        }

        private void StartVideo()
        {
            try
            {
                var path = FindVideo();
                if (path == null) return;
                if (AuthVideo.Source == null)
                    AuthVideo.Source = new Uri(path, UriKind.Absolute);
                AuthVideo.Play();
            }
            catch { StopVideo(); }
        }

        private void StopVideo()
        {
            try
            {
                VideoLayer.BeginAnimation(OpacityProperty, null);
                VideoLayer.Opacity = 0;
                AuthVideo.Stop();
                AuthVideo.Close();
                AuthVideo.Source = null;
            }
            catch { }
        }

        private void AuthVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (PerformanceService.ReduceMotion) { VideoLayer.Opacity = 1; return; }
            VideoLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(1.2))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        /// <summary>Закончилось — сразу по новой, и так до входа в аккаунт.</summary>
        private void AuthVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            AuthVideo.Position = TimeSpan.Zero;
            AuthVideo.Play();
        }

        private void AuthVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) => StopVideo();

        /// <summary>Карточка входа выплывает чуть увеличиваясь — в такт общему проявлению экрана.</summary>
        private void AnimateCard()
        {
            AuthCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            AuthCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            if (PerformanceService.ReduceMotion) return;
            var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(620))
            {
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            };
            AuthCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
            AuthCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
        }

        public void Prepare()
        {
            LoginError.Visibility = Visibility.Collapsed;
            RegError.Visibility = Visibility.Collapsed;
            LoginNick.Clear();
            LoginPassword.Clear();
            ApplyOwnerSetup();
            ChangePanel.Visibility = Visibility.Collapsed;
            if (_setupOnly) ShowLogin(false, false);
            else ShowLogin(true, false);
            AnimateCard();
        }

        // ------------------------------------------------------------------ первый запуск

        /// <summary>Пока нет владельца с паролем, форма регистрации создаёт владельца.</summary>
        private void ApplyOwnerSetup()
        {
            _ownerSetup = AuthService.NeedsOwnerSetup;
            var pending = _ownerSetup ? AuthService.PendingOwner : null;
            // в другие аккаунты можно войти всегда, даже пока владелец не настроен
            _setupOnly = _ownerSetup && !AuthService.Users.Any(u => !string.IsNullOrEmpty(u.PasswordHash));

            TabHost.Visibility = _setupOnly ? Visibility.Collapsed : Visibility.Visible;
            RegLoginLink.Visibility = _setupOnly ? Visibility.Collapsed : Visibility.Visible;
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
            if (_setupOnly) login = false;
            bool changed = _loginMode != login;
            _loginMode = login;
            LoginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
            RegisterPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;

            UpdateUnderline(animate);
            // «Вход» прилетает слева, «Регистрация» — справа, вслед за подчёркиванием
            if (animate && changed) FlyIn(login ? LoginPanel : RegisterPanel, login ? -36 : 36);

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

        /// <summary>Панель влетает сбоку и проявляется.</summary>
        private static void FlyIn(FrameworkElement panel, double fromX)
        {
            panel.BeginAnimation(OpacityProperty, null);
            if (PerformanceService.ReduceMotion) { panel.Opacity = 1; return; }

            if (panel.RenderTransform is not System.Windows.Media.TranslateTransform shift)
                panel.RenderTransform = shift = new System.Windows.Media.TranslateTransform();

            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            shift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
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

            if (user.MustChangePassword)
            {
                ShowChange(user);
                return;
            }
            Authenticated?.Invoke(user);
        }

        // ------------------------------------------------------------------ обязательная смена пароля

        private void ShowChange(UserAccount user)
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Collapsed;
            TabHost.Visibility = Visibility.Collapsed;
            ChangePanel.Visibility = Visibility.Visible;
            FlyIn(ChangePanel, 36);
            ChangeError.Visibility = Visibility.Collapsed;
            ChangePassword1.Clear();
            ChangePassword2.Clear();
            ChangeSubtitle.Text = "Сейчас у «" + user.Nick + "» стандартный пароль, равный нику, — его знает кто угодно. " +
                                  "Придумайте новый" + (user.IsStaff ? " (минимум " + AuthService.MinStaffPasswordLength + " символов)" : "") +
                                  ", чтобы продолжить.";
            ChangePassword1.Focus();
        }

        private void BtnChange_Click(object sender, RoutedEventArgs e)
        {
            var user = AuthService.Current;
            var error = AuthService.CompleteRequiredChange(user, ChangePassword1.Password, ChangePassword2.Password);
            if (error != null)
            {
                ShowError(ChangeError, ChangeErrorText, error);
                return;
            }

            ChangePanel.Visibility = Visibility.Collapsed;
            TabHost.Visibility = Visibility.Visible;
            Authenticated?.Invoke(user);
        }

        private void ChangeCancel_Click(object sender, RoutedEventArgs e)
        {
            // без нового пароля в аккаунт не пускаем
            AuthService.Logout();
            ChangePanel.Visibility = Visibility.Collapsed;
            Prepare();
            ShowLogin(true, false);
        }

        private void ChangePassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnChange_Click(sender, new RoutedEventArgs());
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
