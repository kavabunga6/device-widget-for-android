# Архитектура Device Widget for Android

Проект разделён по направлению зависимостей: бизнес-модели и протокол ничего не
знают об Avalonia/WPF, ADB-процессах и способе хранения настроек. Release-интерфейс
Windows, macOS и Linux находится в `AndroidWidget.Desktop`; прежний WPF-host
сохранён для Windows-диагностики и проверок совместимости.

```text
┌─────────────────────────────────────────────────────┐
│ AndroidWidget.Desktop (Avalonia release UI/runtime)  │
└──────────────────────┬──────────────────────────────┘
                       │ использует
          ┌────────────▼─────────────┐
          │ AndroidWidget.Core       │
          │ domain models + ports    │
          └────────────▲─────────────┘
                       │ реализует
          ┌────────────┴────────────────────┐
          │ Desktop adapters / Infrastructure │
          │ ADB, scrcpy, OS integration       │
          └─────────────────────────────────┘
```

`Core` не ссылается ни на один другой проект. `CompanionHost` зависит от
`Protocol`. Общий Avalonia-host зависит от `Core`, `Protocol` и `CompanionHost`,
но не от WPF или Windows-only инфраструктуры.

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

## Runtime-поток release-приложения

1. `App` создаёт один `DesktopRuntime` на весь процесс.
2. `DesktopRuntime` владеет единственным ADB polling loop, companion-host,
   монитором фотографий и общей очередью передач.
3. `App` сопоставляет окна по стабильному ADB serial: одна карточка и один
   мини-виджет — один serial.
4. Успешный пустой ADB-снимок закрывает окна отключённых serial; временная ошибка
   опроса не подменяет список устройств и не переставляет оставшиеся карточки.
5. Уведомления companion и события операций фильтруются по serial/client tag,
   поэтому результат отображается только возле соответствующей карточки.

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
- `AndroidWidget.Desktop` является общим release UI для Windows, macOS и Linux: телефонная карточка, мини-режим, системный tray/menu bar, единая панель ADB/scrcpy, снимков, записи, файлов, APK, clipboard, power, Wireless debugging и companion. При отсутствии устройств окно скрывается, но фоновый монитор остаётся активным.
- `companion-android` содержит foreground-соединение, Android Keystore и `NotificationListenerService`.
- `ICompanionService` отделяет read-only проверку пакета от явно вызываемой установки; UI вызывает установку только после отдельного пользовательского подтверждения.
- `tools/CompanionHostSmoke` проходит настоящий WSS handshake, pairing, status и notification через независимый TLS-клиент.

Телефонные звонки, SMS provider, контакты и Phone Link намеренно не входят в эту архитектуру.
