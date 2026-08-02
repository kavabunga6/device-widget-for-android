# Android Widget Companion

## Сопряжение из основного Windows-виджета

Основной WPF-виджет сам запускает защищённый Companion Host. Отдельный
`AndroidWidget.Desktop` для Windows-сценария не требуется.

1. Нажмите **Компаньон** в карточке нужного телефона и подтвердите установку.
   После успешного `adb install` приложение автоматически откроется на телефоне.
2. Плитка изменится на **Сопрячь**. Нажмите её: виджет создаст одноразовый код и
   ссылку, покажет их на компьютере и откроет эту же ссылку на выбранном телефоне.
3. При доступном ADB виджет использует `adb reverse` и локальный адрес
   `127.0.0.1`, поэтому вводить IP или открывать порт в брандмауэре не требуется.
   Если туннель недоступен, используется адрес компьютера в локальной сети.
4. На телефоне нажмите **Открыть доступ к уведомлениям** и явно разрешите доступ
   Android Widget Companion. До этого карточка показывает, что SMS и уведомления
   отключены.

Связь каждого сопряжённого companion закрепляется за ADB serial той карточки,
из которой был создан код. Серийный номер не зашит в приложение и не является
ID компьютера: он передаётся только локальному host во время явного сопряжения.
Уведомления разных телефонов не заменяют друг друга.

Companion — кроссплатформенная замена интеграции с Phone Link. Он состоит из Android-приложения и desktop-host на Avalonia и работает напрямую в локальной сети без облачного посредника.

## Что уже работает

- сопряжение телефона с Windows, macOS или Linux;
- одновременное подключение нескольких телефонов;
- независимый жизненный цикл карточек: отключённое устройство исчезает и не заменяется другим;
- производитель, модель, версия Android и заряд;
- состояния «экран включён» и «телефон заблокирован»;
- уведомления, на которые пользователь выдал системный доступ;
- стек до пяти баблов уведомлений в карточке соответствующего телефона с настраиваемым временем показа;
- автоматическое переподключение после первого сопряжения;
- открытие ссылки `awidget://pair` непосредственно Android-компаньоном.

Звонки и разговор через компьютер не реализуются. Приложение также не читает SMS provider: сообщение может появиться только как обычное Android-уведомление, если оно не скрыто настройками телефона.

## Компоненты

| Проект | Ответственность |
|---|---|
| `src/AndroidWidget.Protocol` | Версия протокола и DTO сообщений |
| `src/AndroidWidget.CompanionHost` | WSS, сертификат, pairing, токены и реестр соединений |
| `src/AndroidWidget.Desktop` | Avalonia UI для Windows/macOS/Linux |
| `companion-android` | Android foreground-service, Keystore и relay уведомлений |
| `tools/CompanionHostSmoke` | End-to-end проверка WSS и протокола |

## Сопряжение

1. Установите companion самостоятельно либо нажмите отдельную плитку **Компаньон** в карточке ADB-устройства и подтвердите диалог. Автоматической установки нет.
2. Компьютер и телефон должны находиться в одной локальной сети.
3. Запустите `AndroidWidget.Desktop` и нажмите **Новый код**.
4. Передайте ссылку `awidget://pair?...` на телефон и откройте её либо вставьте в companion вручную.
5. Android проверит формат ссылки и точный SHA-256 fingerprint desktop-сертификата.
6. Desktop принимает шестизначный код только один раз и только в течение пяти минут.
7. После успешного входа host выдаёт случайный 256-битный токен. На Android он шифруется ключом из Android Keystore и используется для переподключений.
8. Для показа сообщений отдельно включите companion в системном экране **Доступ к уведомлениям**.

Основной виджет проверяет установку отдельно для каждого ADB serial через `pm path dev.androidwidget.companion`. Пока пакет отсутствует или статус невозможно определить, зависящие от companion возможности считаются недоступными. Проверка никогда не устанавливает и не запускает приложение.

Ссылка содержит локальный IP, порт, fingerprint и короткоживущий код, поэтому её нельзя публиковать или пересылать посторонним. Для следующего этапа запланирован QR-код, чтобы не переносить длинную ссылку вручную.

## Безопасность и приватность

- трафик идёт по WSS и не отправляется в облако;
- Android принимает только сертификат с fingerprint из pairing-ссылки;
- одноразовый код сравнивается без утечки времени и удаляется после первого использования;
- постоянные токены генерируются случайно для каждой установки и сохраняются в пользовательском каталоге desktop;
- Android-токен защищён AES-GCM ключом из Android Keystore;
- backup данных companion отключён, чтобы токен не мигрировал на другое устройство;
- host ограничивает размер сообщения 64 КиБ и проверяет protocol version и installation ID;
- абсолютных путей разработчика, серийников телефонов, API-ключей и фиксированных ID компьютера в исходниках нет.

Android manifest запрашивает только:

- `INTERNET` — локальное WSS-соединение;
- `POST_NOTIFICATIONS` — видимое состояние foreground-service на Android 13+;
- `FOREGROUND_SERVICE` и `FOREGROUND_SERVICE_REMOTE_MESSAGING` — устойчивое соединение в фоне.

`NotificationListenerService` включается пользователем через отдельный системный экран. Разрешения `READ_SMS`, `READ_CALL_LOG`, `READ_PHONE_STATE`, `READ_CONTACTS` и связанные с телефонией разрешения отсутствуют.

## Локальные данные

Desktop вычисляет каталог через `Environment.SpecialFolder.LocalApplicationData` и хранит в `AndroidWidget/companion-v1`:

- `companion-host.pfx` — локальный self-signed сертификат host;
- `paired-devices.json` — installation ID и случайные токены сопряжённых устройств.

На Android SharedPreferences содержит адрес host, fingerprint и зашифрованный токен. Ключ шифрования не экспортируется из Android Keystore.

## Сборка desktop

Требуются .NET 10 SDK (компилятор для Avalonia 12) и .NET 8 runtime:

```powershell
dotnet restore src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj
dotnet build src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj -c Release
dotnet run --project src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj -c Release
```

Framework-dependent публикация:

```powershell
dotnet publish src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj `
  -c Release --self-contained false -o artifacts/companion-desktop
```

Один и тот же проект используется на Windows, macOS и Linux. Платформенные пакеты Avalonia выбираются во время запуска.

## Сборка Android

Требуются JDK 17 и Android SDK 36. Путь к SDK задаётся стандартными `ANDROID_HOME`/`ANDROID_SDK_ROOT` или локальным `local.properties`; путей разработчика в Gradle-файлах нет.

Windows:

```powershell
cd companion-android
./gradlew.bat lintDebug assembleDebug
```

macOS/Linux:

```bash
cd companion-android
./gradlew lintDebug assembleDebug
```

Debug APK: `companion-android/app/build/outputs/apk/debug/app-debug.apk`.

При последующей сборке Windows-виджета найденный release APK (или debug APK для локальной preview-сборки) встраивается как ресурс. Если Android-проект не был собран, плитка установки остаётся отключённой, а обычные ADB-функции продолжают работать.

## Проверка защищённого протокола

```powershell
dotnet run --project tools/CompanionHostSmoke/CompanionHostSmoke.csproj -c Release
```

Smoke-тест создаёт временный сертификат, проверяет его fingerprint независимым TLS-клиентом, проходит pairing и передаёт тестовые status/notification. Временные ключи и каталог удаляются после теста.

## Текущие границы preview

- передача файлов, APK и трансляция экрана пока остаются в Windows ADB-виджете;
- desktop companion пока является отдельным Avalonia-приложением, а не заменой всего WPF UI;
- QR pairing, команды desktop → phone, снимки экрана и передача файлов входят в следующий протокольный этап;
- на macOS и Linux нужны runtime smoke-тесты и упаковка (`.app`, deb/rpm/AppImage); исходный Avalonia-проект уже не содержит Windows API;
- Android 17 при переходе на target SDK 37 потребует отдельного runtime-разрешения на локальную сеть.
