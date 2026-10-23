using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeriPlayer.Models;
using VeriPlayer.Services;

namespace VeriPlayer.Views.Panels
{
    public partial class BackgroundPanel : UserControl, IModalPanel
    {
        private bool _guard = true;

        public event EventHandler CloseRequested;
        public string Title => "Свой фон";

        public BackgroundPanel()
        {
            InitializeComponent();

            _guard = true;
            DimSlider.Value = Math.Round(App.Settings.BackgroundDim * 100);
            BlurSlider.Value = App.Settings.BackgroundBlur;
            _guard = false;

            UpdateLabels();
            Refresh();
        }

        private void Refresh()
        {
            BgList.Children.Clear();
            BgList.Children.Add(BuildNoneRow());

            foreach (var image in BackgroundService.Items)
                BgList.Children.Add(BuildRow(image));
        }

        private Border BuildNoneRow()
        {
            bool active = string.IsNullOrWhiteSpace(App.Settings.BackgroundId);

            var swatch = new Border
            {
                Width = 62,
                Height = 44,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("InputBrush"),
                BorderBrush = (Brush)FindResource("HairlineBrush"),
                BorderThickness = new Thickness(1)
            };

            var row = BuildShell(swatch, "Без фона", "Однотонный тёмный градиент", active, () =>
            {
                BackgroundService.SetActive("");
                Refresh();
            }, null);

            return row;
        }

        private Border BuildRow(BackgroundImage image)
        {
            bool active = App.Settings.BackgroundId == image.Id;

            var thumb = new Border
            {
                Width = 62,
                Height = 44,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("InputBrush"),
                BorderBrush = (Brush)FindResource("HairlineBrush"),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            var bitmap = Storage.LoadImage(image.FilePath, 160);
            if (bitmap != null)
                thumb.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };

            var subtitle = image.SizeText + "  ·  " + Storage.Human(image.FileSize) +
                           (string.IsNullOrWhiteSpace(image.AddedByName) ? "" : "  ·  " + image.AddedByName);

            return BuildShell(thumb, image.Title, subtitle, active, () =>
            {
                BackgroundService.SetActive(image.Id);
                Refresh();
            }, () =>
            {
                MainWindow.Instance?.Confirm("Удалить фон «" + image.Title + "»?", () =>
                {
                    BackgroundService.Remove(image);
                    Refresh();
                    MainWindow.Instance?.ShowToast("Фон удалён", "info");
                });
            });
        }

        private Border BuildShell(UIElement thumb, string title, string subtitle, bool active,
            Action apply, Action remove)
        {
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            info.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("TextFaintBrush"),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (active)
            {
                var chip = new Border { Style = (Style)FindResource("SoftChip") };
                chip.Child = new TextBlock
                {
                    Text = "Активен",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("AccentTextBrush")
                };
                actions.Children.Add(chip);
            }
            else
            {
                var applyButton = new Button
                {
                    Content = "Применить",
                    Style = (Style)FindResource("BtnGhost"),
                    Padding = new Thickness(13, 6, 13, 6),
                    FontSize = 12
                };
                applyButton.Click += (s, e) => apply();
                actions.Children.Add(applyButton);
            }

            if (remove != null)
            {
                var removeButton = new Button
                {
                    Style = (Style)FindResource("BtnIconSmall"),
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "Удалить"
                };
                removeButton.Content = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconTrash"),
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.None,
                    Stroke = (Brush)FindResource("TextFaintBrush"),
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                removeButton.Click += (s, e) => remove();
                actions.Children.Add(removeButton);
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(thumb, 0);
            Grid.SetColumn(info, 1);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(thumb);
            grid.Children.Add(info);
            grid.Children.Add(actions);

            var border = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                Child = grid
            };

            if (active)
            {
                border.Background = (Brush)FindResource("AccentSofterBrush");
                border.BorderBrush = (Brush)FindResource("AccentBorderBrush");
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = (Brush)FindResource("HairlineBrush");
            }

            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonUp += (s, e) => apply();

            return border;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.tif;*.tiff|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            var outcome = BackgroundService.Import(dialog.FileName);
            if (!outcome.Ok)
            {
                MainWindow.Instance?.ShowToast(outcome.Error, "error");
                return;
            }

            MainWindow.Instance?.ApplyBackground();
            Refresh();
            MainWindow.Instance?.ShowToast("Фон установлен", "ok");
        }

        private void UpdateLabels()
        {
            DimText.Text = Math.Round(App.Settings.BackgroundDim * 100) + "%";
            BlurText.Text = Math.Round(App.Settings.BackgroundBlur) + " px";
        }

        private void Dim_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_guard) return;
            App.Settings.BackgroundDim = DimSlider.Value / 100.0;
            App.SaveSettings();
            MainWindow.Instance?.ApplyBackground();
            UpdateLabels();
        }

        private void Blur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_guard) return;
            App.Settings.BackgroundBlur = BlurSlider.Value;
            App.SaveSettings();
            MainWindow.Instance?.ApplyBackground();
            UpdateLabels();
        }

        private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
