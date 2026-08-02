# Архитектура Device Widget for Android

Проект разделён по направлению зависимостей: бизнес-модели и контракты ничего не знают о WPF, Windows, ADB-процессах и способе хранения настроек. Платформенный код реализует эти контракты, а UI получает готовые сервисы через конструкторы.

```text
┌─────────────────────────────────────────────────────┐
│ AndroidWidget (WPF presentation + composition root) │
└──────────────────────┬──────────────────────────────┘
                       │ использует
          ┌────────────▼─────────────┐
          │ AndroidWidget.Core       │
          │ domain models + ports    │
          └────────────▲─────────────┘
                       │ реализует
          ┌────────────┴────────────────────┐
          │ AndroidWidget.Infrastructure    │
          │ ADB, scrcpy, Windows, JSON, log │
          └─────────────────────────────────┘
```

`Core` не ссылается ни на один другой проект. `Infrastructure` ссылается только на `Core`. Корневой WPF-проект является внешним слоем и ссылается на оба проекта.

## Модули

### AndroidWidget.Core

- `Devices`, `Files`, `Messaging`, `Settings` — неизменяемые модели предметной области;
- `Operations/OperationResult` — единый результат внешней операции;
- `Abstractions` — порты приложения: устройства, настройки, desktop-интеграции, лог и диагностические проверки.

В этом проекте запрещены ссылки на WPF, WinForms, Registry, `Process`, файловые диалоги и конкретные реализации ADB.

### AndroidWidget.Infrastructure

- `Adb/AdbCommandRunner` — безопасный запуск команд, таймауты, отмена, прогресс и захват stdout/stderr;
- `Adb/AndroidDeviceService` — публичный фасад сценариев Android;
- `Adb/DeviceSnapshotReader` — свойства устройства, заряд, экран и блокировка;
- `Adb/SmsNotificationReader` — определение SMS-приложения, парсинг и дедупликация уведомлений;
- `Scrcpy` — извлечение проверенного embedded-пакета, пресеты, трансляция и запись экрана;
- `Settings/JsonSettingsService` — JSON-настройки и Windows-автозапуск;
- `Windows/WindowsDesktopIntegration` — Explorer, открытие файлов и MTP;
- `Diagnostics` — локальный лог и smoke-проверки.

Инфраструктура не создаёт окон и не обращается к WPF-элементам.

### AndroidWidget (presentation)

- `Composition/AppServices` — единственное место создания конкретных сервисов;
- `App` — жизненный цикл и независимая координация окон по serial устройства;
- `Presentation/Tray` — системный трей и его GDI-иконка;
- `Presentation/Files` — отображаемая модель файлового браузера;
- `Presentation/Transfers` — единая последовательная очередь передач с прогрессом и отменой;
- `Presentation/Media` — пути записей, обнаружение и автоматический импорт новых фотографий;
- окна WPF — события ввода, визуальное состояние и вызов сценариев через интерфейсы;
- `Models/PhoneSkin` и `Services/ThemeService` — WPF-зависимые визуальные правила.

Окна не должны использовать `new` для инфраструктурных сервисов и не должны запускать `Process` напрямую.

## Runtime-поток

1. `AppServices.Create()` один раз собирает граф объектов.
2. `MainWindow` опрашивает `IAndroidDeviceService` и сообщает `App` полный снимок устройств.
3. `App` сопоставляет окна по стабильному ADB serial: одна мини-карточка — один serial.
4. Пользовательское действие проходит через контракт Core к реализации Infrastructure.
5. UI получает `OperationResult` и отвечает только за отображение результата.

Это гарантирует, что отключённая раскрытая карточка исчезает, а другое устройство не занимает её место.

## Правила изменений

- Новое Android-действие: сначала добавить сценарий в `IAndroidDeviceService`, затем реализацию в инфраструктуре, после этого вызвать его из UI.
- Новая ОС: реализовать платформенные `IDesktopIntegration`, `ISettingsService`, shell и поставку scrcpy; Core менять не требуется.
- Новое отображаемое поле: бизнес-данные добавить в Core-модель, визуальное форматирование — в presentation view model.
- UI-значки, цвета, `Brush`, `Thickness` и пути к ресурсам не помещать в Core.
- Не передавать необработанные shell-строки: аргументы локального процесса задавать через `ArgumentList`, ввод Android экранировать отдельно.
- Долгие операции принимают `CancellationToken` и имеют ограниченный timeout.

## Проверка границ

```powershell
dotnet build AndroidWidget.csproj -c Release
dotnet format AndroidWidget.csproj --verify-no-changes
dotnet run --project AndroidWidget.csproj -c Release -- --verify-sms-parser
dotnet run --project AndroidWidget.csproj -c Release -- --verify-scrcpy-bundle
dotnet run --project AndroidWidget.csproj -c Release -- --verify-wireless-qr
```

`Directory.Build.props` включает nullable-контекст, детерминированную сборку и трактует предупреждения как ошибки для всех проектов.

## Кроссплатформенный companion

Companion реализован отдельной вертикалью и не протаскивает Android/сетевые детали в существующий ADB Core:

```text
Android companion
       │ WSS + certificate fingerprint + installation token
       ▼
AndroidWidget.CompanionHost ── AndroidWidget.Protocol
       │
       ▼
AndroidWidget.Desktop (Avalonia: Windows / macOS / Linux)
```

- `AndroidWidget.Protocol` содержит только версионированные сообщения и JSON-настройки.
- `AndroidWidget.CompanionHost` отвечает за сертификат, одноразовое сопряжение, токены и независимое состояние устройств.
- `AndroidWidget.Desktop` отображает устройства и уведомления, а также предоставляет общий UI для ADB, scrcpy, снимков, записи и Wireless debugging; отключённая карточка удаляется, другая не занимает её состояние.
- `companion-android` содержит foreground-соединение, Android Keystore и `NotificationListenerService`.
- `ICompanionService` отделяет read-only проверку пакета от явно вызываемой установки; UI вызывает установку только после отдельного пользовательского подтверждения.
- `tools/CompanionHostSmoke` проходит настоящий WSS handshake, pairing, status и notification через независимый TLS-клиент.

Телефонные звонки, SMS provider, контакты и Phone Link намеренно не входят в эту архитектуру.
