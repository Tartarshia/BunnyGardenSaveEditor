@echo off
setlocal
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
if not exist "%~dp0dist" mkdir "%~dp0dist"
set "ZIP=%~dp0dist\BunnyGardenSaveEditor-v1.0.0-windows.zip"
if exist "%ZIP%" del /q "%ZIP%"
tar.exe -a -c -f "%ZIP%" -C "%~dp0" BunnyGardenSaveEditor.exe Launch-BunnyGardenSaveEditor.cmd README.md ACHIEVEMENTS.md LICENSE
if errorlevel 1 exit /b 1
certutil -hashfile "%ZIP%" SHA256
