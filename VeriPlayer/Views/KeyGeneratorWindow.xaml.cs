using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeriPlayer.Services;

namespace VeriPlayer.Views
{
    /// <summary>
    /// Выпуск ключей Pro+ и определение их срока — только для владельца и со-владельца.
    /// Подписывает закрытым ключом с диска (тем же, что и утилита VeriKeygen), в программу он не встроен.
    /// </summary>
    public partial class KeyGeneratorWindow : Window
    {
        private ECDsa _signer;

        public KeyGeneratorWindow(string nick = null)
        {
            InitializeComponent();

            NickBox.Text = nick ?? "";

            LoadSigner(string.IsNullOrWhiteSpace(App.Settings.LicensePrivateKeyPath)
                ? LicenseService.DefaultPrivateKeyPath
                : App.Settings.LicensePrivateKeyPath);
            UpdateSummary();
            RefreshIssued();

            Closed += (s, e) => _signer?.Dispose();
        }

        /// <summary>Открывает окно, если текущий пользователь — владелец или со-владелец.</summary>
        public static void Open(Window owner, string nick = null)
        {
            if (AuthService.Current?.IsStaff != true)
            {
                MainWindow.Instance?.ShowToast("Выпуск ключей доступен только владельцу и со-владельцу.", "error");
                return;
            }
            new KeyGeneratorWindow(nick) { Owner = owner }.Show();
        }

        // ------------------------------------------------------------------ закрытый ключ

        private void LoadSigner(string path)
        {
            _signer?.Dispose();
            var (signer, error) = LicenseService.LoadSigner(path);
            _signer = signer;
            SignerStatus.Text = signer != null ? "Готов к работе: " + path : error;
            SignerStatus.Foreground = signer != null
                ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
                : (System.Windows.Media.Brush)FindResource("DangerBrush");
            IssueButton.IsEnabled = signer != null;
        }

        private void PickSigner_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Закрытый ключ подписи Pro+",
                Filter = "Закрытый ключ (*.pem)|*.pem|Все файлы|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;

            LoadSigner(dialog.FileName);
            if (_signer != null)
            {
                App.Settings.LicensePrivateKeyPath = dialog.FileName;
                App.SaveSettings();
            }
        }

        // ------------------------------------------------------------------ срок

        /// <summary>Последний день действия (UTC-дата) или null — бессрочно; error — если дата введена неверно.</summary>
        private (DateTime? lastDay, bool lifetime, string error) ReadTerm()
        {
            var tag = (TermBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "30";
            if (tag == "lifetime") return (null, true, null);
            if (tag == "date")
            {
                if (!DateTime.TryParseExact(DateBox.Text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                    return (null, false, "Введите дату в формате дд.мм.гггг, например 31.12.2026.");
                if (date.Date < DateTime.UtcNow.Date) return (null, false, "Эта дата уже прошла.");
                return (date.Date, false, null);
            }
            int days = int.Parse(tag, CultureInfo.InvariantCulture);
            // как в VeriKeygen: «30 дней» — сегодня и ещё 29
            return (DateTime.UtcNow.Date.AddDays(days - 1), false, null);
        }

        private void UpdateSummary()
        {
            if (TermSummary == null) return;
            var (lastDay, lifetime, error) = ReadTerm();
            TermSummary.Text = error != null ? ""
                : lifetime ? "Ключ будет действовать бессрочно"
                : "Ключ будет действовать по " + lastDay.Value.ToString("dd.MM.yyyy") + " включительно";
        }

        private void Term_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (DateRow == null) return;
            bool byDate = ((TermBox.SelectedItem as ComboBoxItem)?.Tag as string) == "date";
            DateRow.Visibility = byDate ? Visibility.Visible : Visibility.Collapsed;
            if (byDate && DateBox.Text.Length == 0)
                DateBox.Text = DateTime.Now.AddMonths(1).ToString("dd.MM.yyyy");
            UpdateSummary();
        }

        private void Date_Changed(object sender, TextChangedEventArgs e) => UpdateSummary();

        // ------------------------------------------------------------------ выпуск

        private void Issue_Click(object sender, RoutedEventArgs e)
        {
            IssueError.Visibility = Visibility.Collapsed;

            var (lastDay, _, termError) = ReadTerm();
            if (termError != null) { ShowIssueError(termError); return; }

            var nick = NickBox.Text.Trim();
            if (nick.Length > 0 && AuthService.Find(nick) == null &&
                MessageBox.Show(this, "Пользователя «" + nick + "» нет на этом компьютере. Всё равно выпустить ключ?",
                    "Ключи Pro+", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var (key, error) = LicenseService.Issue(_signer, nick, lastDay);
            if (error != null) { ShowIssueError(error); return; }

            ResultBox.Text = key;
            ResultRow.Visibility = Visibility.Visible;
            RefreshIssued();
            MainWindow.Instance?.ShowToast("Ключ Pro+ для «" + nick + "» создан", "ok");
        }

        private void ShowIssueError(string text)
        {
            IssueErrorText.Text = text;
            IssueError.Visibility = Visibility.Visible;
        }

        private void CopyResult_Click(object sender, RoutedEventArgs e) => Copy(ResultBox.Text);

        // ------------------------------------------------------------------ проверка срока

        private void Inspect_Click(object sender, RoutedEventArgs e)
        {
            var info = LicenseService.Inspect(InspectBox.Text);
            InspectResult.Text = info.Text;
            InspectResult.Foreground = (System.Windows.Media.Brush)FindResource(
                !info.Genuine ? "DangerBrush" : info.Expired ? "WarnBrush" : "TextBrush");
            InspectResult.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ журнал

        private void RefreshIssued()
        {
            var issued = LicenseService.LoadIssued();
            IssuedList.ItemsSource = issued;
            NoIssued.Visibility = issued.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CopyIssued_Click(object sender, RoutedEventArgs e)
        {
            if (IssuedList.SelectedItem is LicenseService.IssuedKey item) Copy(item.Key);
            else MainWindow.Instance?.ShowToast("Выберите ключ в списке", "info");
        }

        // ------------------------------------------------------------------ прочее

        private void Copy(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                Clipboard.SetText(text);
                MainWindow.Instance?.ShowToast("Ключ скопирован", "ok");
            }
            catch
            {
                MainWindow.Instance?.ShowToast("Не удалось скопировать — выделите ключ вручную", "error");
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            base.OnKeyDown(e);
        }
    }
}
