# Загрузка Veri Player на GitHub

Репозиторий готов к загрузке. Финальный размер: **~6.5 MB**.

## Шаг 1: Создать репозиторий на GitHub

1. Зайти на https://github.com/new
2. Указать:
   - **Repository name**: `veri-player`
   - **Description**: `Aesthetic music player with AI generation, animated backgrounds, and cloud integrations`
   - **Public/Private**: выбрать
   - **НЕ добавлять** README, .gitignore, license (уже есть локально)
3. Нажать **Create repository**

## Шаг 2: Загрузить код

Скопировать команды из GitHub (они будут показаны после создания):

```powershell
cd "C:\Users\vanda\OneDrive\Belgeler\Default Project\Veri Player"

# Добавить remote (замените YOUR_USERNAME)
git remote add origin https://github.com/YOUR_USERNAME/veri-player.git

# Загрузить код
git branch -M main
git push -u origin main
```

## Шаг 3: Создать Release

1. Перейти в **Releases** → **Create a new release**
2. Указать:
   - **Tag**: `v1.1.0.0`
   - **Release title**: `Veri Player 1.1.0.0`
   - **Description**: описать изменения
3. Прикрепить файл **VeriPlayer.exe** (64 MB) из `dist/`
4. Нажать **Publish release**

## Структура репозитория

```
veri-player/
├── .github/README.md       # Краткое описание для GitHub
├── README.md               # Полная документация
├── LICENSE                 # MIT License
├── GITHUB_DEPLOY.md        # Эта инструкция
├── DISCORD_SETUP.md        # Настройка Discord RPC
├── README_SUNO.md          # Настройка Suno API
├── VeriPlayer/             # Исходники приложения
│   ├── VeriPlayer.csproj
│   ├── App.xaml.cs
│   ├── MainWindow.xaml.cs
│   ├── Services/           # Бизнес-логика
│   ├── Views/              # UI компоненты
│   ├── Controls/           # Кастомные контролы
│   ├── Models/             # Модели данных
│   ├── Themes/             # Стили и темы
│   └── Assets/             # Иконки, логотип
└── dist/                   # НЕ в Git (только в Releases)
    └── VeriPlayer.exe      # Готовый exe
```

## Размер

- **Git репозиторий**: 6.5 MB
- **Исходники**: 94 файла, ~7 MB
- **Готовый exe**: 64 MB (в Releases)

## Технологии

- .NET 8.0 + WPF
- C# 12
- NAudio 2.2.1 (аудио)
- TagLibSharp 2.3.0 (метаданные)
- QRCoder 1.8.1 (QR-коды)

## Лицензия

MIT — свободное использование и модификация.

---

**После загрузки:**
- Ссылка на репозиторий: `https://github.com/YOUR_USERNAME/veri-player`
- Ссылка на релиз: `https://github.com/YOUR_USERNAME/veri-player/releases/tag/v1.1.0.0`
