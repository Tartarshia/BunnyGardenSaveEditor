@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /target:winexe /out:"%~dp0BunnyGardenSaveEditor.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0BunnyGardenSaveEditor.cs"
if errorlevel 1 exit /b 1
