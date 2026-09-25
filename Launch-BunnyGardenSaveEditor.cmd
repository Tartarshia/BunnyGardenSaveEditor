@echo off
setlocal
if not exist "%~dp0BunnyGardenSaveEditor.exe" call "%~dp0build.cmd"
if exist "%~dp0BunnyGardenSaveEditor.exe" start "" "%~dp0BunnyGardenSaveEditor.exe"
if errorlevel 1 pause
