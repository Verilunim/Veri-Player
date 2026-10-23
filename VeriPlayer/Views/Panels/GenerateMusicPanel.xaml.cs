using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class GenerateMusicPanel : UserControl, IModalPanel
    {
        public event EventHandler CloseRequested;
        public string Title => "Создать песню";
        
        private List<GeneratedTrack> _currentResults = new List<GeneratedTrack>();
        
        public GenerateMusicPanel()
        {
            InitializeComponent();
            
            // Загружаем сохранённый API ключ
            ApiKeyBox.Text = App.Settings.SunoApiKey ?? "";
            
            MusicGenService.HistoryChanged += OnHistoryChanged;
            Unloaded += (s, e) => MusicGenService.HistoryChanged -= OnHistoryChanged;
        }
        
        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            var key = ApiKeyBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MainWindow.Instance?.ShowToast("Введите API ключ", "warn");
                return;
            }
            
            App.Settings.SunoApiKey = key;
            App.SaveSettings();
            MainWindow.Instance?.ShowToast("API ключ сохранён", "success");
        }
        
        private void OnHistoryChanged()
        {
            // Обновляем статусы треков в UI
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_currentResults.Count > 0)
                    ResultsList.Items.Refresh();
            }));
        }
        
        private void InstrumentalCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (LyricsBox == null) return;
            LyricsBox.IsEnabled = InstrumentalCheck.IsChecked != true;
            
            if (InstrumentalCheck.IsChecked == true)
                LyricsBox.Text = "[Instrumental - текст не требуется]";
        }
        
        private void Weirdness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WeirdnessText != null)
                WeirdnessText.Text = $"{(int)WeirdnessSlider.Value}%";
        }
        
        private void Adherence_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AdherenceText != null)
                AdherenceText.Text = $"{(int)AdherenceSlider.Value}%";
        }
        
        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(App.Settings.SunoApiKey))
            {
                MainWindow.Instance?.ShowToast("Нужен API ключ Suno. Настройте его в Настройках → AI генерация.", "warn");
                return;
            }
            
            var style = StylePromptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(style))
            {
                MainWindow.Instance?.ShowToast("Укажите стиль песни", "warn");
                return;
            }
            
            var lyrics = InstrumentalCheck.IsChecked == true ? "" : LyricsBox.Text.Trim();
            var isInstrumental = InstrumentalCheck.IsChecked == true;
            
            if (!isInstrumental && string.IsNullOrWhiteSpace(lyrics))
            {
                MainWindow.Instance?.ShowToast("Укажите текст песни или выберите 'Инструментальная'", "warn");
                return;
            }
            
            GenerateButton.IsEnabled = false;
            GenerateButton.Content = "Создаём...";
            
            try
            {
                var weirdness = WeirdnessSlider.Value / 100.0;
                var adherence = AdherenceSlider.Value / 100.0;
                var negative = NegativePromptBox.Text.Trim();
                
                var tracks = await MusicGenService.GenerateAsync(style, lyrics, isInstrumental, weirdness, adherence, negative);
                
                if (tracks.Count == 0)
                    throw new Exception("API не вернул треков");
                
                _currentResults = tracks;
                ResultsList.ItemsSource = _currentResults.Select(t => new TrackViewModel(t)).ToList();
                ResultsPanel.Visibility = Visibility.Visible;
                
                MainWindow.Instance?.ShowToast($"Генерация запущена! Ожидайте ~60 секунд.", "info");
                
                // Автообновление UI каждые 5 секунд
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(5000);
                        await Dispatcher.BeginInvoke(new Action(() =>
                        {
                            foreach (var vm in ResultsList.ItemsSource.OfType<TrackViewModel>())
                                vm.Refresh();
                            ResultsList.Items.Refresh();
                            
                            // Загружаем обложки
                            LoadImages();
                        }));
                        
                        // Останавливаем, если все треки готовы или ошиблись
                        if (_currentResults.All(t => t.Status == "complete" || t.Status == "failed"))
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToast($"Ошибка: {ex.Message}", "error");
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = "Создать 2 варианта";
            }
        }
        
        private void LoadImages()
        {
            foreach (var item in ResultsList.Items.OfType<TrackViewModel>())
            {
                if (!string.IsNullOrWhiteSpace(item.Track.LocalImagePath) && System.IO.File.Exists(item.Track.LocalImagePath))
                {
                    var container = ResultsList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    if (container != null)
                    {
                        var image = FindVisualChild<Image>(container, "TrackImage");
                        if (image != null && image.Source == null)
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.UriSource = new Uri(item.Track.LocalImagePath, UriKind.Absolute);
                                bmp.EndInit();
                                bmp.Freeze();
                                image.Source = bmp;
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t && (string.IsNullOrEmpty(name) || t.Name == name))
                    return t;
                
                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
        
        private void Play_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as TrackViewModel;
            if (vm == null) return;
            
            var track = vm.Track;
            
            if (track.Status != "complete")
            {
                MainWindow.Instance?.ShowToast("Трек ещё генерируется...", "info");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(track.LocalPath) || !System.IO.File.Exists(track.LocalPath))
            {
                MainWindow.Instance?.ShowToast("Файл не найден", "error");
                return;
            }
            
            // Создаём временный Track для проигрывания
            var tempTrack = new Track
            {
                FilePath = track.LocalPath,
                Title = track.Title,
                Artist = "AI Generated",
                Duration = track.Duration
            };
            
            AudioService.PlayTrack(tempTrack, new List<Track> { tempTrack });
            MainWindow.Instance?.ShowToast($"▶ {track.Title}", "info");
        }
        
        private void AddToLibrary_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as TrackViewModel;
            if (vm == null) return;
            
            var track = vm.Track;
            
            if (track.Status != "complete")
            {
                MainWindow.Instance?.ShowToast("Дождитесь завершения генерации", "warn");
                return;
            }
            
            if (track.AddedToLibrary)
            {
                MainWindow.Instance?.ShowToast("Трек уже в библиотеке", "info");
                return;
            }
            
            try
            {
                MusicGenService.AddToLibrary(track);
                MainWindow.Instance?.ShowToast($"✓ Добавлено: {track.Title}", "success");
                MainWindow.Instance?.RefreshAll();
                
                vm.Refresh();
                ResultsList.Items.Refresh();
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToast($"Ошибка: {ex.Message}", "error");
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        
        // ViewModel для биндинга
        private class TrackViewModel
        {
            public GeneratedTrack Track { get; }
            
            public TrackViewModel(GeneratedTrack track)
            {
                Track = track;
            }
            
            public string Title => Track.Title;
            
            public string StatusText
            {
                get
                {
                    if (Track.IsInstrumental)
                        return "Инструментал";
                    return Track.Lyrics?.Length > 60 ? Track.Lyrics.Substring(0, 60) + "..." : Track.Lyrics;
                }
            }
            
            public string Status
            {
                get
                {
                    return Track.Status switch
                    {
                        "pending" => "⏳ Ожидание",
                        "generating" => "🎵 Создаётся...",
                        "complete" => Track.AddedToLibrary ? "✓ В библиотеке" : "✓ Готово",
                        "failed" => "❌ Ошибка",
                        _ => Track.Status
                    };
                }
            }
            
            public void Refresh()
            {
                // Триггер для обновления биндингов
            }
        }
    }
}
