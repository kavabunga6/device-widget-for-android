# Device Widget for Android™

Небольшой desktop-виджет для управления подключёнными Android-устройствами.
Каждое устройство получает независимую карточку, которую можно свернуть до
мини-виджета в форме телефона.

Проект работает локально, не требует аккаунта и не содержит телеметрию.

> Android is a trademark of Google LLC. Device Widget for Android is an
> independent project and is not affiliated with or endorsed by Google LLC.

## Возможности

- обнаружение всех устройств из `adb devices -l` и отдельная карточка для каждого;
- состояния `online`, `offline`, `unauthorized`, блокировка и спящий экран;
- drag-and-drop файлов и папок в `/sdcard/Download`;
- установка APK только после явного действия пользователя;
- встроенный scrcpy 4.0 в Windows-сборках: экран, управление и запись;
- ADB-браузер файлов, скачивание на компьютер и MTP-переход;
- скриншоты в выбранную в настройках папку;
- Wireless debugging по QR-коду или шестизначному коду;
- очередь передач с прогрессом и отменой;
- обнаружение новых фотографий и опциональный автоимпорт;
- светлая/тёмная тема, автозапуск, трей и изменяемый размер карточек;
- до пяти последовательных уведомлений-баблов с настраиваемым временем показа;
- дополнительный companion для локальной защищённой передачи разрешённых
  пользователем уведомлений.

Компаньон никогда не устанавливается автоматически. Пока пользователь не
нажал кнопку установки, не подтвердил ADB-диалог, не выполнил сопряжение и не
выдал системный доступ к уведомлениям, зависимые функции отключены. Разрешения
на SMS, звонки, контакты и телефонные идентификаторы не запрашиваются.

## Интерфейс

Скриншоты ниже обезличены и не содержат идентификаторы реальных устройств.

![Мини-виджет подключённого устройства](docs/images/mini-widget.png)

Остальные снимки прежнего прототипа временно исключены: в них использовалось
старое имя продукта. Новые снимки будут сделаны из подписанной release-сборки.

## Платформы

| Платформа | Архитектуры | UI | ADB/scrcpy |
|---|---|---|---|
| Windows 10/11 | x64, ARM64 | полный WPF-виджет | встроены |
| macOS 10.15+ | Intel, Apple Silicon | Avalonia desktop host | из `PATH` |
| Linux | x64, ARM64 | Avalonia desktop host | из `PATH` |
| Android 8.0+ | APK companion | сопряжение и уведомления | не требуется |

macOS и Linux сборки пока предоставляют общий desktop host, а не полностью
идентичную Windows-карточку. Для них установите `adb` и `scrcpy` системным
пакетным менеджером.

## Быстрый запуск из исходников

Требования: .NET 8 SDK, Windows 10/11 и Android Platform Tools либо встроенный
архив scrcpy.

```powershell
git clone <repository-url>
cd device-widget-for-android
dotnet restore AndroidWidget.csproj
dotnet run --project AndroidWidget.csproj
```

Для Avalonia-host:

```powershell
dotnet run --project src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj
```

Android companion собирается Gradle Wrapper:

```powershell
cd companion-android
./gradlew.bat lint test assembleDebug
```

## Настройка телефона

1. Включите режим разработчика и USB debugging.
2. Подключите телефон и подтвердите RSA-отпечаток компьютера на его экране.
3. Для беспроводного режима используйте плитку **Wi-Fi ADB** и системный экран
   Wireless debugging на телефоне.
4. Для уведомлений по желанию установите companion из карточки, создайте новый
   код сопряжения и отдельно разрешите ему доступ к уведомлениям.

Статус `unauthorized` означает, что подтверждение RSA ещё не принято на
телефоне. Один телефон не заменяет другой: состояние окна и мини-карточки
привязано к конкретному ADB serial только во время выполнения и не зашито в код.

## Локальные данные

Приложение вычисляет все пути через системные API. Абсолютных путей конкретного
разработчика в исходниках и конфигурации нет.

| Данные | Расположение |
|---|---|
| Настройки | `%LOCALAPPDATA%\AndroidWidget\settings.json` |
| Диагностический лог | `%LOCALAPPDATA%\AndroidWidget\widget.log` |
| Распакованный scrcpy | `%LOCALAPPDATA%\AndroidWidget\scrcpy\v4.0` |
| Скриншоты | выбранная папка; по умолчанию `Изображения\Device Widget` |
| Записи | выбранная папка; по умолчанию `Видео\Device Widget` |
| Импорт фото | выбранная папка; по умолчанию `Изображения\Device Widget Imports` |

Каталог `AndroidWidget` сохранён как внутренний совместимый идентификатор
данных; это не публичное имя продукта и не привязка к устройству или аккаунту.
Диагностический лог может содержать локальные пути из сообщений ОС — проверьте
его перед отправкой третьим лицам.

Подробнее: [политика приватности](PRIVACY.md) и
[модель companion](docs/COMPANION.md).

## Проверка и сборка

```powershell
dotnet build AndroidWidget.csproj -c Release -warnaserror
dotnet format AndroidWidget.csproj --verify-no-changes
dotnet build src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj -c Release -warnaserror
dotnet run --project AndroidWidget.csproj -c Release -- --verify-scrcpy-bundle
dotnet run --project AndroidWidget.csproj -c Release -- --verify-sms-parser
dotnet run --project AndroidWidget.csproj -c Release -- --verify-companion-bundle
dotnet run --project AndroidWidget.csproj -c Release -- --verify-wireless-qr
```

Полная release-сборка создаёт Windows x64/ARM64, macOS x64/ARM64, Linux
x64/ARM64, подписанный companion APK, контрольные суммы и LGPL-исходники:

```powershell
$env:DEVICE_WIDGET_ANDROID_KEYSTORE = 'path-to-release.jks'
$env:DEVICE_WIDGET_ANDROID_STORE_PASSWORD = '<from-local-secret-store>'
$env:DEVICE_WIDGET_ANDROID_KEY_ALIAS = 'release-alias'
$env:DEVICE_WIDGET_ANDROID_KEY_PASSWORD = '<from-local-secret-store>'
./tools/build_release.ps1 -Version 0.1.1
```

Ключи и пароли не должны находиться в репозитории. Windows и macOS пакеты без
доверенного коммерческого/Apple-сертификата остаются неподписанными; это явно
отмечается в release notes.

## Лицензии и corresponding source

Собственный код распространяется по [Apache License 2.0](LICENSE).
Полный перечень зависимостей, версий, авторских уведомлений и лицензий находится
в [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Условия и контрольные суммы
точных исходников FFmpeg 8.1.1 и libusb 1.0.29 описаны в
[SOURCE_OFFER.md](SOURCE_OFFER.md).

`tools/build_release.ps1` проверяет corresponding-source архивы до сборки и
помещает их рядом с бинарными release assets. `LICENSE`,
`THIRD_PARTY_NOTICES.md`, `SOURCE_OFFER.md` и каталог `licenses/` входят в каждый
desktop-пакет; те же основные тексты встраиваются в APK и доступны кнопкой
**Лицензии и сторонние компоненты**.

Встроенный scrcpy используется без изменений. Его официальный Windows-архив
проверяется по закреплённому SHA-256. FFmpeg собран без GPL-only опций и связан
динамически; точные configure flags находятся в официальном исходном архиве
scrcpy 4.0.

## Структура проекта

```text
AndroidWidget.csproj                  Windows WPF composition root
src/AndroidWidget.Core/               модели и независимые контракты
src/AndroidWidget.Infrastructure/     ADB, scrcpy, настройки и интеграции
src/AndroidWidget.Protocol/           versioned companion protocol
src/AndroidWidget.CompanionHost/      WSS host, pairing and tokens
src/AndroidWidget.Desktop/            Avalonia host for macOS/Linux/Windows
companion-android/                     optional Android companion
licenses/                              shipped license texts and notices
third_party/sources/                   corresponding-source manifest
tools/build_release.ps1               reproducible release packaging
```

Архитектура подробнее описана в [ARCHITECTURE.md](ARCHITECTURE.md).

## Security

Уязвимости следует сообщать через GitHub Private Vulnerability Reporting, а не
публичный issue. Подробности: [SECURITY.md](SECURITY.md).
