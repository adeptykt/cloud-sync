@echo off
echo Установка CloudSync Agent...
echo.

:: Проверка прав администратора
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Требуются права администратора!
    echo Пожалуйста, запустите этот файл от имени администратора.
    pause
    exit /b 1
)

:: Остановка и удаление старой службы
echo Остановка старой службы...
sc stop CloudSyncAgent 2>nul
sc delete CloudSyncAgent 2>nul

:: Создание папки установки
set INSTALL_DIR=%ProgramFiles%\CloudSyncAgent
echo Создание папки: %INSTALL_DIR%
mkdir "%INSTALL_DIR%" 2>nul

:: Копирование файлов
echo Копирование файлов...
copy /Y "CloudSyncService.exe" "%INSTALL_DIR%\"
copy /Y "CloudSyncTray.exe" "%INSTALL_DIR%\"
copy /Y "CloudSyncShared.dll" "%INSTALL_DIR%\"

:: Копирование конфига если существует
if exist "config.json" copy /Y "config.json" "%INSTALL_DIR%\"

:: Установка службы
echo Установка службы...
sc create CloudSyncAgent binPath= "\"%INSTALL_DIR%\CloudSyncService.exe\"" start= auto
sc description CloudSyncAgent "Облачная синхронизация файлов с поддержкой порядка загрузки"

:: Настройка прав на папку
echo Настройка прав доступа...
icacls "%INSTALL_DIR%" /grant "CREATOR OWNER:(OI)(CI)F" /T
icacls "%INSTALL_DIR%" /grant "SYSTEM:(OI)(CI)F" /T
icacls "%INSTALL_DIR%" /grant "Administrators:(OI)(CI)F" /T

:: Запуск службы
echo Запуск службы...
sc start CloudSyncAgent

:: Добавление в автозагрузку Tray приложения
echo Добавление в автозагрузку...
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "CloudSyncTray" /t REG_SZ /d "\"%INSTALL_DIR%\CloudSyncTray.exe\"" /f

:: Запуск Tray приложения
echo Запуск Tray приложения...
start "" "%INSTALL_DIR%\CloudSyncTray.exe"

echo.
echo ========================================
echo Установка успешно завершена!
echo ========================================
echo.
echo Служба CloudSyncAgent установлена
echo Tray приложение запущено
echo Папка установки: %INSTALL_DIR%
echo.
pause