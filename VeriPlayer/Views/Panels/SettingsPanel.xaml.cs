using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class SettingsPanel : UserControl, IModalPanel
    {
        private bool _guard = true;

        public event EventHandler CloseRequested;
        public string Title => "Настройки";

        public SettingsPanel()
        {
            InitializeComponent();

            _guard = true;
            ComboBoxItem target = null;
            foreach (var item in SortBox.Items)
                if (item is ComboBoxItem ci && (ci.Tag as string) == App.Settings.SortMode) target = ci;
            SortBox.SelectedItem = target ?? SortBox.Items[0];

            CopyToggle.IsChecked = App.Settings.CopyFilesIntoLibrary;
            MotionToggle.IsChecked = App.Settings.ReduceMotion;
            VisualizerToggle.IsChecked = App.Settings.Visualizer;
            _guard = false;

            var version = typeof(SettingsPanel).Assembly.GetName().Version;
            AboutText.Text = "Veri Player " + version + " — эстетичный плеер для вашей собственной музыки. " +
                             "Все данные хранятся локально на этом компьютере.";
            LibraryStats.Text = "В библиотеке: " + LibraryService.Tracks.Count + " треков · " +
                                Storage.Human(LibraryService.TotalSize());
            BuildHotkeys();
        }

        private void BuildHotkeys()
        {
            var rows = new (string Keys, string Action)[]
            {
                ("Пробел", "играть / пауза"),
                ("→ / ←", "перемотка ±5 сек"),
                ("N / P", "следующий / предыдущий"),
                ("↑ / ↓", "громкость"),
                ("Ctrl + F", "поиск"),
                ("Esc", "закрыть окно")
            };

            for (int i = 0; i < rows.Length; i++)
            {
                Hotkeys.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var key = new Border
                {
                    Padding = new Thickness(9, 4, 9, 4),
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = (Brush)FindResource("HairlineBrush"),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0),
                    Child = new TextBlock
                    {
                        Text = rows[i].Keys,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextBrush")
                    }
                };
                Grid.SetRow(key, i);
                Grid.SetColumn(key, 0);

                var action = new TextBlock
                {
                    Text = rows[i].Action,
                    Style = (Style)FindResource("TextMuted"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(14, i == 0 ? 0 : 8, 0, 0)
                };
                Grid.SetRow(action, i);
                Grid.SetColumn(action, 1);

                Hotkeys.ColumnDefinitions.Clear();
                Hotkeys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Hotkeys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Hotkeys.Children.Add(key);
                Hotkeys.Children.Add(action);
            }
        }

        private void Sort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_guard) return;
            if (SortBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                App.Settings.SortMode = tag;
                App.SaveSettings();
                MainWindow.Instance?.RefreshLibrary();
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.CopyFilesIntoLibrary = CopyToggle.IsChecked == true;
            App.SaveSettings();
        }

        private void Motion_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.ReduceMotion = MotionToggle.IsChecked == true;
            App.SaveSettings();
            MainWindow.Instance?.ApplyAppearance();
        }

        private void Visualizer_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Visualizer = VisualizerToggle.IsChecked == true;
            App.SaveSettings();
        }

        private void OpenData_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", Storage.Root) { UseShellExecute = true }); }
            catch (Exception ex) { MainWindow.Instance?.ShowToast(ex.Message, "error"); }
        }

        private void Logout_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.Logout();

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
