@echo off
setlocal
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
findstr /i /m /c:"WMAC-Gen2" /c:"0000000000000001" /c:"gho_" /c:"C:\Users\" "%~dp0BunnyGardenSaveEditor.cs" >nul
if not errorlevel 1 exit /b 1
echo PASS: native Windows editor source compiled and contains no scanned private paths or tokens.
