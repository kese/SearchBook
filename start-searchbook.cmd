@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "PROJECT_ROOT=%~dp0"
set "ENV_FILE=%PROJECT_ROOT%.env"
set "SEARCHBOOK_EXE=%PROJECT_ROOT%dist\SearchBook-self-contained-win-x64\SearchBook.exe"

if not exist "%ENV_FILE%" (
    echo SearchBook update configuration was not found: "%ENV_FILE%"
    exit /b 2
)

for /f "usebackq tokens=1,* delims==" %%A in ("%ENV_FILE%") do (
    if /i "%%A"=="SEARCHBOOK_GITHUB_TOKEN" set "SEARCHBOOK_GITHUB_TOKEN=%%B"
)

if not defined SEARCHBOOK_GITHUB_TOKEN (
    echo SEARCHBOOK_GITHUB_TOKEN is empty in "%ENV_FILE%"
    exit /b 3
)

if "%SEARCHBOOK_DRY_RUN%"=="1" exit /b 0

if not exist "%SEARCHBOOK_EXE%" (
    echo SearchBook executable was not found: "%SEARCHBOOK_EXE%"
    exit /b 4
)

start "" "%SEARCHBOOK_EXE%"
exit /b 0
