@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Config.ps1"
if errorlevel 1 (
    echo Configuration could not be updated. Installation stopped; existing settings were preserved.
    pause
    exit /b 1
)
set "destination=%LOCALAPPDATA%\CodexLimits\app"
if not exist "%destination%" mkdir "%destination%"
for %%F in (CodexLimits.exe CodexLimits.dll CodexLimits.deps.json CodexLimits.runtimeconfig.json config.example.json README.md) do copy /Y "%~dp0%%F" "%destination%\%%F" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$shell=New-Object -ComObject WScript.Shell; $link=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Codex Limits.lnk')); $link.TargetPath=Join-Path $env:LOCALAPPDATA 'CodexLimits\app\CodexLimits.exe'; $link.WorkingDirectory=Join-Path $env:LOCALAPPDATA 'CodexLimits\app'; $link.Save()"
start "" "%destination%\CodexLimits.exe"
