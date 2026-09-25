@echo off
setlocal
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
if not exist "%~dp0dist" mkdir "%~dp0dist"
set "ZIP=%~dp0dist\BunnyGardenSaveEditor-v1.0.0-windows.zip"
if exist "%ZIP%" del /q "%ZIP%"
pushd "%~dp0"
tar.exe -a -c -f "%ZIP%" BunnyGardenSaveEditor.exe Launch-BunnyGardenSaveEditor.cmd README.md ACHIEVEMENTS.md LICENSE
popd
if errorlevel 1 exit /b 1
certutil -hashfile "%ZIP%" SHA256
