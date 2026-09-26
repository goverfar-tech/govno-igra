@echo off
chcp 65001 > nul
REM Сборка игры в builds\survival.exe
REM Если Godot лежит в другом месте — задай переменную GODOT_EXE
REM или отредактируй строку ниже.
if not defined GODOT_EXE set GODOT_EXE=E:\godot\Godot_v4.7.2-stable_win64.exe

cd /d "%~dp0"
if not exist builds mkdir builds

"%GODOT_EXE%" --headless --path . --export-release "Windows Desktop" builds\survival.exe
if errorlevel 1 (
    echo [ОШИБКА] Сборка не удалась. Проверь путь к Godot и установленные export templates.
    pause
    exit /b 1
)
echo.
echo Готово: builds\survival.exe
pause
