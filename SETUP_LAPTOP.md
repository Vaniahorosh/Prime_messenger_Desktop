# Инструкция по развёртыванию проекта на новом ноутбуке / ПК 

Пошаговое руководство для того, чтобы быстро склонировать, запускать, редактировать и собирать **Prime Messenger Desktop** на новом ноутбуке под управлением Windows 10 / 11.

---

##  Необходимые компоненты и инструменты

### 1. .NET 8.0 SDK (x64)
* **Для чего нужен:** компиляция платформы, запуск проекта `PrimeDesktop`, восстановление NuGet-зависимостей и компиляция однофайлового установщика `PrimeSetup`.
* **Где скачать:** [Официальный сайт Microsoft .NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) (выберите **.NET SDK x64**).
* **Проверка установки:**
  ```cmd
  dotnet --version
  ```
  *(должна отображаться версия `8.0.xxx`).*

### 2. Git для Windows (Git for Windows)
* **Для чего нужен:** загрузка репозитория с GitHub, получение обновлений (`git pull`), фиксация изменений и отправка (`git push`).
* **Где скачать:** [git-scm.com/download/win](https://git-scm.com/download/win)
* **Совет при установке:** обязательно оставьте включённым **Git Credential Manager**, чтобы авторизация в GitHub проходила в один клик через веб-браузер.

### 3. Среда разработки (IDE)
#### Вариант A (Рекомендуется): Visual Studio 2022 Community
* **Для чего нужна:** полноценная работа с WPF, интерактивный визуальный редактор XAML, отладчик, IntelliSense.
* **Где скачать:** [Visual Studio 2022 Community](https://visualstudio.microsoft.com/vs/community/) (бесплатная редакция).
* **Важно:** при установке в Visual Studio Installer выберите рабочую нагрузку:
  *  **«Разработка классических приложений .NET»** (*Desktop development with .NET*).

#### Вариант B: Visual Studio Code (лёгкий вариант)
* Скачайте [Visual Studio Code](https://code.visualstudio.com/).
* Установите расширения:
  * **C# Dev Kit** (от Microsoft)
  * **XAML Tools** / **XAML Language Support**

### 4. Microsoft Edge WebView2 Runtime
* **Для чего нужен:** движок встроенного веб-браузера Prime (рендеринг веб-страниц, изоляция вкладок, видео).
* **Где скачать:** [WebView2 Evergreen Bootstrapper](https://developer.microsoft.com/microsoft-edge/webview2/).
* *Примечание:* на большинстве современных версий Windows 10/11 он уже предустановлен в системе.

### 5. Rust Toolchain (Опционально)
* **Для чего нужен:** перекомпиляция нативного криптографического ядра `prime_native.dll` (`desktop/PrimeNative`).
* **Где скачать:** [rustup.rs](https://rustup.rs/).
* *Примечание:* если Rust не установлен, проект собирается с уже скомпилированным ядром или автоматически использует встроенный управляемый C# fallback.

### 6. Windows PowerShell (предустановлен в Windows)
* Используется скриптом `build.bat` для сжатия файлов и упаковки установщика `PrimeSetup.exe`. Ничего дополнительно устанавливать не требуется.

### 7. Python 3.10+ (Опционально)
* **Для чего нужен:** автоматизация вспомогательных задач, пакетная обработка ресурсов или скрипты.
* **Где скачать:** [python.org/downloads](https://www.python.org/downloads/).

---

##  Пошаговое развёртывание и первый запуск

### Шаг 1. Клонирование репозитория
Откройте PowerShell или Командную строку (Terminal) и перейдите в папку, куда хотите поместить проект (например, `Документы`):
```cmd
cd "%USERPROFILE%\Documents"
git clone https://github.com/Vaniahorosh/Prime_messenger_Desktop.git
cd Prime_messenger_Desktop
```

### Шаг 2. Открытие в Visual Studio
1. Запустите Visual Studio 2022.
2. Нажмите **«Открыть проект или решение»** (*Open a project or solution*).
3. Выберите файл:
   ```text
   Prime_messenger_Desktop\desktop\PrimeDesktop.sln
   ```
4. Дождитесь автоматического восстановления NuGet-пакетов.
5. Нажмите клавишу **`F5`** для запуска в режиме отладки или **`Ctrl+F5`** для быстрого запуска.

### Шаг 3. Запуск и сборка через командную строку (без открытия VS)

* **Быстрый запуск клиента:**
  ```cmd
  cd desktop\PrimeDesktop
  dotnet run
  ```

* **Полная релизная сборка приложения и инсталлятора:**
  ```cmd
  cd desktop
  build.bat
  ```
  После успешного завершения готовые исполняемые файлы будут доступны в:
  * `desktop\bin\Portable\PrimeDesktop.exe` - портативный автономный клиент.
  * `desktop\bin\Installer\PrimeSetup.exe` - мастер установки.
