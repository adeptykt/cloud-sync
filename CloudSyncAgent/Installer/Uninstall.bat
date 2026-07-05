@echo off
echo Удаление CloudSync Agent...
echo.

:: Проверка прав администратора
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Требуются права администратора!
    echo Пожалуйста, запустите этот файл от имени администратора.
    pause
    exit /b 1
)

:: Остановка службы
echo Остановка службы...
sc stop CloudSyncAgent 2>nul

:: Удаление службы
echo Удаление службы...
sc delete CloudSyncAgent 2>nul

:: Удаление из автозагрузки
echo Удаление из автозагрузки...
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "CloudSyncTray" /f 2>nul

:: Удаление файлов
echo Удаление файлов...
set INSTALL_DIR=%ProgramFiles%\CloudSyncAgent
if exist "%INSTALL_DIR%" (
    rmdir /S /Q "%INSTALL_DIR%"
)

:: Удаление данных пользователя
echo Удаление данных пользователя...
set USER_DATA=%ProgramData%\CloudSyncAgent
if exist "%USER_DATA%" (
    echo.
    echo Обнаружены пользовательские данные в %USER_DATA%
    choice /C YN /M "Удалить пользовательские данные? "
    if errorlevel 2 (
        echo Данные сохранены
    ) else (
        rmdir /S /Q "%USER_DATA%"
        echo Данные удалены
    )
)

echo.
echo ========================================
echo Удаление завершено!
echo ========================================
pause