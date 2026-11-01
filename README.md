# Pagegram

Лёгкий Telegram-клиент для **Windows 10 Mobile** (Lumia 550/640/650/950 и т.д.),
заточенный под телефоны с **512 МБ – 1 ГБ RAM**.

Работает на [TDLib](https://github.com/tdlib/td) (официальная библиотека Telegram на C++),
UI — нативный UWP/XAML на C#, Release собирается через .NET Native.

## Что умеет

- Вход по номеру: код, облачный пароль (2FA), регистрация, привязка почты
- Список чатов (основной), с подгрузкой при прокрутке и счётчиками непрочитанных
- Переписка: чтение, отправка текста, статусы отправки, правки и удаления в реальном времени
- Фото открываются по тапу на весь экран, с зумом
- Очистка кэша файлов, выход из аккаунта

## Почему не лагает

| Что | Как |
|---|---|
| Сообщения и чаты | Хранятся в базе TDLib на диске (`use_message_database`), в RAM — только то, что на экране |
| Модели UI | Из объектов TDLib берутся только строки; сами объекты сразу отдаются GC |
| Апдейты | Ненужные (стикеры, реакции, сторис, пользователи) отсеиваются на фоновом потоке; нужные идут в UI пачками по 100 за один проход диспетчера |
| Аватарки | Нет. Цветной кружок с буквой, кисти общие на всё приложение |
| Медиа в ленте | Нет картинок — только текстовая метка (📷 Фото, 🎤 Голосовое…). Фото грузится только по тапу, максимум 1280px, и декодируется под ширину экрана |
| История | Максимум 150 сообщений в памяти, старые срезаются сверху. Страница чата не кэшируется — при выходе всё освобождается |
| Нехватка памяти | `MemoryManager` следит за лимитом; при `High`/`OverLimit` и уходе в фон чат ужимается до 40 сообщений + принудительный GC |
| Рендер | Тёмная тема (AMOLED), без анимаций переходов, фиксированная высота строк в списке чатов, минимум элементов в шаблонах, `x:Bind` вместо `Binding` |
| Диск | `use_storage_optimizer` — TDLib сам чистит старые файлы |

## Сборка

Нужна Windows-машина. Рекомендуется **Visual Studio 2019** (ARM32 для UWP там поддерживается без плясок;
VS 2022 тоже может, но поддержка ARM32 там помечена устаревшей).

Компоненты VS: *Universal Windows Platform development*, *C++ (v142) Universal Windows Platform tools*,
*MSVC ARM build tools*, Windows 10 SDK **10.0.16299** (и при желании 10.0.15063).

### 1. Ключи API

Зайди на <https://my.telegram.org> → *API development tools*, создай приложение и впиши
`api_id` и `api_hash` в [`Pagegram/Config.cs`](Pagegram/Config.cs).

### 2. TDLib для UWP (ARM)

TDLib подключается как Extension SDK `Telegram.Td.UWP`. Собирается один раз:

```powershell
git clone https://github.com/microsoft/vcpkg C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat
C:\vcpkg\vcpkg.exe install gperf:x86-windows openssl:arm-uwp zlib:arm-uwp openssl:x86-uwp zlib:x86-uwp

git clone https://github.com/tdlib/td.git
cd td\example\uwp
powershell -ExecutionPolicy ByPass .\build.ps1 -vcpkg_root C:\vcpkg -arch "x86 ARM" -compress none
```

Точные параметры скрипта смотри в `td/example/uwp/README.md` — они иногда меняются.
На выходе будет `tdlib.vsix` / `Telegram.Td.UWP.vsix` — установи его двойным кликом.
`x86` нужен только для эмулятора, на телефон хватает `ARM`.

> Если свежий master TDLib не собирается под UWP — возьми последний тег, который собирается,
> API, использованный в приложении, базовый и есть во всех версиях 1.8.x.

### 3. Сборка и установка

1. Открой `Pagegram.sln`, выбери **Release | ARM**.
2. *Package.appxmanifest → Packaging → Choose certificate → Create test certificate* (один раз).
3. Включи на телефоне *Параметры → Обновление и безопасность → Для разработчиков → Режим разработчика*.
4. Подключи Люмию по USB, цель **Device**, `Ctrl+F5`.
   Или *Project → Publish → Create App Packages* и поставь `.appx` через файловый менеджер / WinAppDeployCmd.

Debug-сборки на телефоне заметно медленнее и прожорливее (JIT вместо .NET Native) —
судить о скорости надо по **Release**.

## Структура

```
Pagegram/
  App.xaml(.cs)          — старт, навигация по состоянию авторизации, кнопка «Назад», suspend
  Config.cs              — ключи API и лимиты памяти
  Services/TdService.cs  — клиент TDLib, фильтр и батчинг апдейтов
  Services/ChatStore.cs  — сортированный список чатов из апдейтов
  Services/Formatter.cs  — контент сообщений → короткий текст
  Services/LowMemory.cs  — реакция на лимит памяти Windows 10 Mobile
  Models/                — ChatItem, MessageItem, общие кисти
  Views/                 — LoginPage, ChatsPage, ChatPage, PhotoPage
```

## Чего пока нет

Пуш-уведомлений (нужен свой push-сервер под WNS), отправки медиа, голосовых, стикеров,
секретных чатов, архива и папок, ответов/пересылок. Каркас под это есть — добавляется
по тому же принципу: только строки в моделях, медиа только по запросу.
