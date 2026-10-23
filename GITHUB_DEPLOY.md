# Публикация Veri Player на GitHub

Репозиторий: https://github.com/Verilunim/Veri-Player

Готовый exe в Git не хранится (папка `dist/` в `.gitignore`) — он выкладывается в **Releases**.

## Шаг 1: Собрать exe

```powershell
cd "C:\Users\vanda\OneDrive\Belgeler\Default Project\Veri Player"
dotnet publish VeriPlayer/VeriPlayer.csproj -p:PublishProfile=VeriPlayer-win-x64
```

Результат: `dist/VeriPlayer.exe` (~64 MB, .NET уже внутри). Перед сборкой закройте запущенный VeriPlayer — иначе файл занят.

## Шаг 2: Отправить код

Работа ведётся в отдельной ветке, затем она вливается в `main` через Pull Request:

```powershell
git push -u origin <ветка>
```

На GitHub: **Compare & pull request** → проверить изменения → **Merge**.

> В адресе `origin` не должно быть токена. Проверить: `git remote -v`. Правильный вид — `https://github.com/Verilunim/Veri-Player.git`, вход выполняется через Git Credential Manager.

## Шаг 3: Создать Release

1. **Releases** → **Draft a new release**
2. **Choose a tag**: `v1.2.0.0` → **Create new tag** (target: `main`)
3. **Release title**: `Veri Player 1.2.0.0`
4. **Description**: текст из `release-notes/v1.2.0.0.md`
5. Прикрепить **dist/VeriPlayer.exe**
6. **Publish release**

## Ключи Pro+ и безопасность

- **Никогда не коммитьте** закрытый ключ подписи (`%USERPROFILE%\.veriplayer\license-private.pem`). В `.gitignore` уже есть `*.pem`.
- В репозитории лежит только открытый ключ (`LicenseService.PublicKey`) и утилита `tools/VeriKeygen` — без закрытого ключа она ключей не выпустит.
- Если репозиторий публичный, программу можно собрать из исходников без проверки Pro+. Ключи защищают готовый exe из Releases.

## Структура репозитория

```
Veri-Player/
├── .github/README.md       # Краткое описание для GitHub
├── README.md               # Полная документация
├── LICENSE                 # MIT License
├── GITHUB_DEPLOY.md        # Эта инструкция
├── DISCORD_SETUP.md        # Настройка Discord RPC
├── README_SUNO.md          # Настройка Suno API
├── release-notes/          # Тексты релизов
├── VeriPlayer/             # Исходники приложения
│   ├── Services/           # Бизнес-логика (Audio, Auth, License, Library…)
│   ├── Views/              # UI: окна и панели
│   ├── Controls/           # Кастомные контролы
│   ├── Models/             # Модели данных
│   ├── Themes/             # Стили и темы
│   └── Assets/             # Иконки, логотип
├── tools/VeriKeygen/       # Консольный выпуск ключей Pro+
└── dist/                   # НЕ в Git (только в Releases)
    └── VeriPlayer.exe
```

## Технологии

- .NET 8.0 + WPF, C# 12
- NAudio 2.2.1, TagLibSharp 2.3.0, QRCoder 1.8.1, DiscordRichPresence
- ECDSA P-256 (ключи Pro+), PBKDF2-SHA256 и DPAPI (аккаунты)
