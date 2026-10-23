# 🎵 Veri Player

**Эстетичный музыкальный плеер для Windows** с поддержкой AI-генерации треков, анимированными фонами и облачными интеграциями.

![Version](https://img.shields.io/badge/version-1.1.0.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## ✨ Возможности

### 🎨 Визуальная эстетика
- **Анимированные фоны**: Снег, дождь, листья, сакура, звёзды, пузыри, светлячки, угли
- **Кастомные фоны**: Загрузка изображений (min 1920×1080) и MP4-видео (до 5 сек)
- **RGB-тематизация**: Выбор цвета темы через палитру
- **Плавные переходы**: Crossfade между треками, анимированные переходы между вкладками
- **Fullscreen обложек**: Полноэкранный просмотр с плавным показом/скрытием

### 🎵 Работа с музыкой
- **Импорт**: Файлы MP3, FLAC, WAV, M4A, OGG, WMA
- **Метаданные**: Редактирование title, artist, album, genre, year
- **Обложки**: Загрузка/обрезка квадратных обложек
- **Фоновые клипы**: MP4-видео до 5 секунд для каждого трека
- **Плейлисты**: Создание, редактирование, экспорт
- **Избранное** и **история прослушиваний**

### 🤖 AI-генерация
- **Suno AI интеграция**: Создание треков по текстовому описанию
- **Параметры**: Стиль, текст песни, странность, вовлечение в стиль
- **История генераций**: Сохранение всех созданных треков
- **Прямое добавление в библиотеку**

### 🔗 Облачные интеграции
- **Spotify**: Подключение аккаунта, импорт плейлистов, синхронизация
- **Yandex Music**: Импорт библиотеки (в разработке)

### 📤 Шеринг
- **QR-коды**: Генерация QR для мгновенной передачи
- **Ссылки**: Передача метаданных или полного файла (до 16 MB)
- **Схема URI**: `veriplayer://track/...`

### 👥 Система аккаунтов
- **Локальные профили**: Ник, пароль (PBKDF2-SHA256), аватар, био
- **Роли**: Owner, Co-owner, User
- **Панель администратора**: Мониторинг пользователей (Owner/Co-owner)
- **Первый запуск**: владелец сам задаёт ник и пароль, предустановленных аккаунтов нет

### 🎮 Управление
- **Горячие клавиши**: Space (play/pause), Left/Right (prev/next), Ctrl+M (mute)
- **Системный трей**: Показ прогресса трека в tooltip
- **Drag & Drop**: Перетаскивание файлов/папок для импорта
- **Контекстные меню**: Полный набор действий для каждого трека

### 🛠️ Диагностика
Встроенные CLI-команды для тестирования:
- `--demo` — фиктивная библиотека
- `--login nick:pass` — автовход
- `--play <index>` — запуск трека
- `--modal <name>` — открыть панель
- `--audit <path>` — проверка UI-геометрии
- `--sharetest` — тест шеринга

## 📦 Установка

### Требования
- **OS**: Windows 10/11 (x64)
- **.NET**: 8.0 Runtime (включён в standalone-сборку)
- **Кодеки**: Media Feature Pack (для MP4-видео)

### Скачать
Последний релиз: **[VeriPlayer.exe (64 MB)](dist/VeriPlayer.exe)**

Просто скачайте и запустите — всё уже внутри.

## 🚀 Использование

### Первый запуск
1. Создайте аккаунт владельца: придумайте ник и пароль (остальные пользователи регистрируются сами, роль Co-owner выдаётся в админ-панели)
2. Добавьте музыку: **кнопка "+"** → Файлы/Папки
3. Настройте фон: **Настройки** → **Эффекты фона**
4. Выберите тему: **RGB-палитра** в настройках

### AI-генерация
1. Получите API ключ на [sunoapi.com](https://sunoapi.com) (от $5)
2. **Меню "+"** → **Создать песню с AI**
3. Вставьте ключ, опишите стиль и текст
4. Дождитесь генерации (~1 минута) → **Добавить в библиотеку**

### Шеринг треков
1. **ПКМ по треку** → **Поделиться треком**
2. Выберите режим: **Метаданные** (QR + короткая ссылка) или **С файлом** (длинная ссылка)
3. **Копировать ссылку** или **Сохранить QR**
4. Получатель: **Вкладка "Добавить по ссылке"** → вставить → **Добавить**

## 🏗️ Сборка из исходников

### Требования
- **Visual Studio 2022** или **Rider**
- **.NET 8.0 SDK**
- **Packages**: NAudio, TagLibSharp, QRCoder, DiscordRichPresence

### Команды
```bash
# Клонировать
git clone https://github.com/YOUR_USERNAME/veri-player.git
cd veri-player

# Восстановить пакеты
dotnet restore "Veri Player/VeriPlayer/VeriPlayer.csproj"

# Debug-сборка
dotnet build "Veri Player/VeriPlayer/VeriPlayer.csproj" -c Debug

# Release (standalone exe)
dotnet publish "Veri Player/VeriPlayer/VeriPlayer.csproj" -c Release -p:PublishProfile=VeriPlayer-win-x64

# Запуск
./Veri Player/dist/VeriPlayer.exe
```

## 📁 Структура

```
Veri Player/
├── VeriPlayer/
│   ├── Assets/              # Иконки, логотип
│   ├── Controls/            # ParticleField, custom controls
│   ├── Models/              # Track, User, Playlist, Settings
│   ├── Services/            # Audio, Library, Auth, Spotify, Share
│   ├── Themes/              # Theme.xaml, Icons.xaml
│   ├── Views/               # MainWindow, PlayerView, AdminView, AuthView
│   │   └── Panels/          # Модальные панели
│   ├── App.xaml(.cs)        # Точка входа, диагностика
│   └── VeriPlayer.csproj
├── dist/                    # Опубликованный exe
└── README.md
```

## 🔧 Архитектура

### Сервисы
- **AudioService**: NAudio, crossfade, repeat/shuffle, позиция
- **LibraryService**: Импорт, CRUD треков, query
- **AuthService**: Локальные аккаунты, PBKDF2-хэши
- **ShareService**: Сборка/парсинг ссылок, base64url, QR
- **QrEncoder**: Чистый C# QR-генератор (EC level M, версии 1-40)
- **SpotifyService**: OAuth PKCE, импорт плейлистов
- **MusicGenService**: Suno API, генерация, история
- **TrayService**: NotifyIcon, tooltip с прогрессом
- **BackgroundService**: Управление фонами, клипами
- **Storage**: %AppData%\VeriPlayer (JSON-сериализация)

### UI
- **WPF + XAML**: Custom theme, glass panels
- **ParticleField**: Canvas-based анимации (8 эффектов)
- **MainWindow**: Shell (sidebar + content) + Auth overlay
- **PlayerView**: Треки, транспорт, seek bar
- **Панели**: Track edit, effects, settings, Spotify, generate music, share, add by link

## 🎨 Дизайн

- **Палитра**: Stepped tonal ladder (5-7 уровней, #05070C → #1E2636)
- **Акценты**: Luminous accent (#38BDF8 и т.д.) + muted supporting tint
- **Типографика**: Fluid scale (clamp()), custom properties
- **Layout**: CSS Grid-first (для section rhythm), flex внутри компонентов
- **Компоненты**: Tokenized (spacing, radius, shadow через var())
- **Геометрия**: Fully rounded (999px pills, 50% circles)

## 📝 История версий

### 1.1.0.0 (23 октября 2026)
- ✅ **Трей следит за позицией**: Tooltip показывает прогресс трека
- ✅ **API ключ в панели генерации**: Поле для Suno API ключа с сохранением

### 1.0.0.3 (17 октября 2026)
- ✅ **Шеринг треков**: QR-коды + ссылки (meta/full)
- ✅ **Вкладка "Добавить по ссылке"**: Импорт треков из ссылок
- ✅ **Smooth transitions**: Crossfade между песнями, анимация вкладок
- ✅ **Фикс видео-лупа**: Убран чёрный экран при повторе клипа
- ✅ **QRCoder**: Переход на battle-tested библиотеку

### 1.0.0.2 (14 октября 2026)
- ✅ **Обложки**: Загрузка, обрезка, fullscreen
- ✅ **Фоновые клипы**: MP4 до 5 секунд для каждого трека
- ✅ **Фикс меню**: PART_Popup для подменю

### 1.0.0.1 (10 октября 2026)
- ✅ **Первый релиз**: Базовая библиотека, плейлисты, аккаунты
- ✅ **Анимированные фоны**: 8 эффектов
- ✅ **Spotify интеграция**: OAuth + импорт плейлистов

## 🤝 Вклад

Pull requests приветствуются! Для крупных изменений откройте issue для обсуждения.

### Разработка
1. Fork репозиторий
2. Создайте feature-ветку (`git checkout -b feature/amazing-feature`)
3. Commit изменения (`git commit -m 'Add amazing feature'`)
4. Push в ветку (`git push origin feature/amazing-feature`)
5. Откройте Pull Request

### Code Style
- **C#**: PascalCase для public, camelCase для private
- **XAML**: PascalCase для x:Name, строчные для binding paths
- **Комментарии**: XML-docs для public API, inline для сложной логики
- **Commit messages**: Русский или английский, императив

## 📄 Лицензия

MIT License — свободное использование, модификация и распространение.

## 👨‍💻 Авторы

- **Verilunim** — Owner, главный разработчик
- **Jomapel** — Co-owner, тестирование

## 🙏 Благодарности

- **NAudio** — аудио playback
- **TagLibSharp** — метаданные
- **QRCoder** — генерация QR
- **Suno AI** — генерация музыки
- **Spotify** — интеграция плейлистов

## 📧 Контакты

- **GitHub**: [@YOUR_USERNAME](https://github.com/YOUR_USERNAME)
- **Issues**: [github.com/YOUR_USERNAME/veri-player/issues](https://github.com/YOUR_USERNAME/veri-player/issues)

---

Made with 💙 in Russia
