# ✅ Чек-лист релиза Veri Player 1.2.0.0

## Перед публикацией

- [ ] Токен GitHub отозван и убран из адреса `origin` (`git remote -v` без `ghp_…`)
- [ ] Резервная копия закрытого ключа Pro+ (`%USERPROFILE%\.veriplayer\license-private.pem`) сделана и хранится не в репозитории
- [ ] `git status` чистый, закрытого ключа среди файлов нет
- [ ] `dist/VeriPlayer.exe` собран из актуального кода (версия в свойствах файла — 1.2.0.0)

## Публикация

1. Отправить ветку и влить её в `main` через Pull Request
2. **Releases** → **Draft a new release**
   - Tag: `v1.2.0.0` (target: `main`)
   - Title: `Veri Player 1.2.0.0`
   - Description: `release-notes/v1.2.0.0.md`
   - Файл: `dist/VeriPlayer.exe` (64 MB)
3. **Publish release**

## После публикации

- [ ] Ссылка «Скачать» в README ведёт на релиз: https://github.com/Verilunim/Veri-Player/releases/latest
- [ ] Скачанный exe запускается на чистом компьютере, экран первого запуска просит создать владельца

Подробности — в [GITHUB_DEPLOY.md](GITHUB_DEPLOY.md).
