# Настройка Discord Rich Presence

Чтобы текущий трек отображался в Discord (как Spotify), нужно получить Discord Application ID.

## Шаги:

1. Перейти на [Discord Developer Portal](https://discord.com/developers/applications)
2. Нажать "New Application"
3. Ввести имя "Veri Player" (или любое другое)
4. После создания скопировать **Application ID** из раздела "General Information"
5. Перейти в раздел "Rich Presence" → "Art Assets"
6. Загрузить:
   - `veri_logo` — логотип плеера (большая иконка)
   - `play` — иконка воспроизведения (маленькая)
   - `pause` — иконка паузы (маленькая)
7. Открыть файл `DiscordService.cs` в редакторе
8. Заменить строку `private static readonly string ApplicationId = "1234567890123456789";` на ваш Application ID (пока стоит эта заглушка, Rich Presence отключён)
9. Пересобрать приложение

## Примечание
В текущей версии используется заглушечный ID. Для работы Rich Presence нужен настоящий Application ID от Discord.

После настройки трек будет отображаться в Discord с деталями:
- Название трека
- Исполнитель
- Время до конца трека (при воспроизведении)
- Иконка статуса (играет / на паузе)
