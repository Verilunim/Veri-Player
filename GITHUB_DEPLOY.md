# 🚀 Инструкция по загрузке на GitHub

## 1️⃣ Создать репозиторий на GitHub

1. Открой https://github.com/new
2. Название: `veri-player`
3. Описание: `Эстетичный музыкальный плеер для Windows с AI-генерацией треков`
4. Public или Private (на твой выбор)
5. **НЕ** создавай README, .gitignore или LICENSE (они уже есть локально)
6. Нажми **Create repository**

## 2️⃣ Связать локальный репозиторий с GitHub

Скопируй команды, которые GitHub покажет после создания репозитория:

```bash
cd "C:\Users\vanda\OneDrive\Belgeler\Default Project\Veri Player"

# Добавить remote (замени YOUR_USERNAME на свой ник GitHub)
git remote add origin https://github.com/YOUR_USERNAME/veri-player.git

# Переименовать ветку в main (если нужно)
git branch -M main

# Запушить
git push -u origin main
```

## 3️⃣ Если попросит авторизацию

GitHub больше не принимает пароли в командной строке. Используй:

### Вариант A: GitHub CLI (рекомендуется)
```bash
# Установи GitHub CLI: https://cli.github.com/
gh auth login

# Потом пуш будет работать автоматически
git push -u origin main
```

### Вариант B: Personal Access Token
1. Открой https://github.com/settings/tokens
2. **Generate new token (classic)**
3. Выбери scopes: `repo` (полный доступ к репозиториям)
4. Скопируй токен (показывается один раз!)
5. При пуше используй токен как пароль:
   - Username: `YOUR_USERNAME`
   - Password: `YOUR_TOKEN`

## 4️⃣ Добавить темы (topics) на GitHub

После пуша открой репозиторий на GitHub и добавь темы:
- `music-player`
- `wpf`
- `csharp`
- `dotnet`
- `windows`
- `ai-music`
- `suno-ai`
- `spotify`
- `qr-code`

## 5️⃣ Настроить релиз

1. Открой репозиторий → **Releases** → **Create a new release**
2. Tag: `v1.1.0.0`
3. Title: `Veri Player 1.1.0.0`
4. Description:
   ```markdown
   ## ✨ Что нового
   - ✅ Трей следит за позицией трека
   - ✅ API ключ в панели генерации музыки
   
   ## 📦 Скачать
   - [VeriPlayer.exe (64.35 MB)](https://github.com/YOUR_USERNAME/veri-player/releases/download/v1.1.0.0/VeriPlayer.exe)
   ```
5. Загрузи файл: `dist/VeriPlayer.exe`
6. Нажми **Publish release**

## 6️⃣ Обновить README

В README.md замени:
- `YOUR_USERNAME` → твой GitHub username
- Ссылки на issues/releases

Потом:
```bash
git add README.md
git commit -m "Update README with GitHub links"
git push
```

---

## 🎯 Быстрая команда (после создания репозитория на GitHub)

```bash
cd "C:\Users\vanda\OneDrive\Belgeler\Default Project\Veri Player"
git remote add origin https://github.com/YOUR_USERNAME/veri-player.git
git branch -M main
git push -u origin main
```

**Готово!** Репозиторий на GitHub 🎉
