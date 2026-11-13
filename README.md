<div align="center">
  <img src="desktop/PrimeDesktop/Assets/prime_logo.png" alt="Prime Desktop Logo" width="120" />
  <h1>Prime Desktop (BETA)</h1>
  <p><b>Всегда будь в Прайме теперь и для Windows 10/11!</b></p>
  <p>Версия: <b>v0.3 Desk</b> | Единая гибридная архитектура: C# (.NET 8 WPF) + Rust Native Core</p>
  <p>
    <a href="https://github.com/Vaniahorosh/Prime_messenger_Desktop/releases/latest/download/PrimeSetup.exe"><img src="https://img.shields.io/badge/Скачать%20установщик-PrimeSetup.exe-0078D7?style=flat-square&logo=windows&logoColor=white" height="34" alt="Скачать установщик PrimeSetup.exe" /></a>
    &nbsp;
    <a href="https://github.com/Vaniahorosh/Prime_messenger_Desktop/releases/latest/download/PrimeDesktop.exe"><img src="https://img.shields.io/badge/Скачать%20Portable-PrimeDesktop.exe-2ea44f?style=flat-square&logo=windows&logoColor=white" height="34" alt="Скачать портативную версию PrimeDesktop.exe" /></a>
  </p>
</div>

---

## Как установить и запустить

### Вариант 1. Установка на компьютер (рекомендуется)
1. Скачайте файл [PrimeSetup.exe](https://github.com/Vaniahorosh/Prime_messenger_Desktop/releases/latest/download/PrimeSetup.exe).
2. Запустите скачанный файл и нажмите кнопку "Установить".
3. Запустите мессенджер через ярлык на рабочем столе или в меню "Пуск".

### Вариант 2. Без установки (портативная версия)
1. Скачайте файл [PrimeDesktop.exe](https://github.com/Vaniahorosh/Prime_messenger_Desktop/releases/latest/download/PrimeDesktop.exe).
2. Поместите его в любую папку или на флешку и запустите. Программа работает сразу без установки.

---

## О проекте

Prime Desktop - официальный настольный клиент экосистемы Prime Messenger. Приложение обеспечивает защищенное одноранговое (P2P) общение без участия сторонних серверов, облачных провайдеров и внешнего подключения к интернету.

Связь с мобильными устройствами Android и другими рабочими станциями осуществляется напрямую:
- Bluetooth RFCOMM (стандартный профиль последовательного порта).
- Локальная сеть LAN (автоматическое UDP-обнаружение и прямой TCP-транспорт).

---

## Архитектура: C# WPF + Rust Native Core

В проекте реализована единая гибридная архитектура, объединяющая лучшие стороны двух платформ:

1. **Интерфейс и бизнес-логика: C# (.NET 8 WPF)**
   - Нативный интерфейс Windows с эффектом акрилового матового стекла (Frosted Glassmorphism).
   - Встроенный браузер (WebView2) с несколькими вкладками и закладками.
   - Воспроизведение и запись аудиозаметок с отображением звуковой волны (waveform) и ускорением.
   - Коллажи фото и галереи вложений, цитирование сообщений и форматирование текста.

2. **Нативное ядро криптографии и протокола: Rust (prime_native.dll)**
   - Быстрое и безопасное сквозное шифрование AES-256-GCM.
   - Криптографическое хеширование Blake3 и SHA-256 для проверки целостности пакетов и файлов.
   - 5-байтовое кадрирование протокола PrimePacket на уровне нативной памяти без лишних копирований.
   - Маскирование и анонимизация аппаратных MAC и Bluetooth идентификаторов.
   - Автоматический переход (Fallback): если нативная библиотека отсутствует, клиент автоматически переключается на встроенную реализацию .NET.

---

## Основные возможности

### Одноранговая сеть и протокол
- Прямое P2P-соединение: работа по Bluetooth RFCOMM (UUID: fa87c0d0-afac-11de-8a39-0800200c9a66) и TCP/IP (порт 8989).
- Автоматическое обнаружение пиров в локальной сети по UDP (порт 8988).
- Единый протокол PrimePacket: полная совместимость типов пакетов (от 0x01 до 0x0F) с Android-клиентом.
- Очередь отправки (Outbox): доставка сообщений и файлов при временном разрыве соединения.
- Сквозное шифрование: аппаратное ускорение AES-256-GCM через нативное Rust-ядро.

### Интерфейс и дизайн
- Эффект матового стекла (Frosted Glassmorphism): аккуратное размытие фона.
- Плавное затемнение: виньетки сверху и снизу области сообщений.
- Двухрежимная боковая панель: стандартный список диалогов (320px) и компактный режим аватарок (Rail, 72px).
- Панель информации о собеседнике: вкладки медиа, файлов и аудио.
- Плавные сплиттеры: изменение размеров панелей без мерцания.

### Медиа и связь
- Передача любых файлов, изображений, видеозаметок и архивов с адаптивным коллажем фото.
- Голосовые сообщения с отрисовкой звуковой волны (waveform) и переключением скорости (1.0x / 1.5x / 2.0x).
- Реакции, цитирование ответов и разметка текста (жирный, курсив, спойлеры).
- Встроенный веб-браузер с вкладками, историей и закладками.

---

## Структура репозитория

```text
Prime_messenger_Desktop/
│
├── README.md                          # Документация проекта и ссылки на загрузку
├── PROTOCOL.md                        # Спецификация сетевого протокола PrimePacket
├── SETUP_LAPTOP.md                    # Инструкция по установке рабочего места
│
└── desktop/                           # Единый гибридный клиент (C# WPF + Rust Core)
    ├── PrimeDesktop.sln               # Решение Visual Studio
    ├── build.bat                      # Скрипт полной автоматической сборки
    │
    ├── PrimeNative/                   # Нативное ядро на Rust (cdylib -> prime_native.dll)
    │   ├── Cargo.toml                 # Конфигурация Rust и зависимостей
    │   ├── .cargo/config.toml         # Настройки компоновщика (MinGW / GCC)
    │   └── src/
    │       ├── lib.rs                 # Экспортируемый C-ABI интерфейс для C# P/Invoke
    │       ├── crypto.rs              # Реализация AES-256-GCM, Blake3 и SHA-256
    │       ├── protocol.rs            # Кадрирование пакетов PrimePacket
    │       └── anonymizer.rs          # Маскирование и анонимизация пиров
    │
    ├── PrimeDesktop/                  # WPF-клиент (.NET 8)
    │   ├── Assets/                    # Графические ресурсы, иконки и логотипы
    │   ├── Controls/                  # Пользовательские элементы (коллаж фото, аудиоплеер)
    │   ├── Models/                    # Модели данных (ChatMessage, Contact, PacketType)
    │   ├── Network/                   # Сетевой стек Bluetooth RFCOMM и LAN (UDP/TCP)
    │   ├── Resources/                 # Словари стилей (Styles.xaml)
    │   ├── Services/                  # PrimeNativeCore (P/Invoke к Rust), SQLite, медиа
    │   ├── ViewModels/                # MVVM бизнес-логика
    │   ├── Views/                     # Окна браузера, настроек, просмотра медиа
    │   ├── MainWindow.xaml            # Главный интерфейс мессенджера
    │   └── prime_native.dll           # Нативное Rust-ядро
    │
    ├── PrimeSetup/                    # Графический мастер установки
    └── bin/
        ├── Portable/                  # Автономная портативная сборка
        │   └── PrimeDesktop.exe
        └── Installer/                 # Мастер установки Windows
            └── PrimeSetup.exe
```

---

## Сборка из исходного кода

### Требования
- ОС: Windows 10 (версия 19041+) или Windows 11 (x64).
- .NET: .NET 8.0 SDK (x64).
- Rust (опционально): Rust 1.80+ (если требуется перекомпиляция prime_native.dll). При отсутствии Cargo скрипт использует готовое нативное ядро или встроенный C# fallback.

### Быстрая сборка (одной командой)
Запустите сборочный скрипт:
```cmd
cd desktop
build.bat
```

Скрипт автоматически:
1. Проверяет наличие Rust и при наличии пересобирает PrimeNative в библиотеку prime_native.dll.
2. Копирует prime_native.dll в проект PrimeDesktop.
3. Собирает релизный портативный файл PrimeDesktop.exe в папку desktop/bin/Portable/.
4. Собирает мастер установки PrimeSetup.exe в папку desktop/bin/Installer/.

### Запуск в режиме разработки
```cmd
cd desktop\PrimeDesktop
dotnet run
```
или откройте desktop\PrimeDesktop.sln в Visual Studio 2022 и нажмите F5.
