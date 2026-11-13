@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem =============================================================================
rem Panex Automated Release & Packaging Script
rem =============================================================================
rem Standardized build and packaging pipeline generating:
rem   - dist\<App>-v<Version>-<Arch>-Portable.zip
rem   - dist\<App>-v<Version>-<Arch>-Setup.exe (via Inno Setup)
rem
rem Agnostic execution: fully anchored to %~dp0
rem =============================================================================

rem -----------------------------------------------------------------------------
rem Path Anchoring (Execution Agnostic - Captured immediately at start)
rem -----------------------------------------------------------------------------
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

for %%i in ("%SCRIPT_DIR%\..") do set "ROOT_DIR=%%~fi"
set "PROJECT_DIR=%ROOT_DIR%\Panex"
set "PROJECT_FILE=%PROJECT_DIR%\Panex.csproj"
set "DIST_DIR=%ROOT_DIR%\dist"
set "STAGING_DIR=%DIST_DIR%\staging"
set "ISS_FILE=%SCRIPT_DIR%\InnoSetup.iss"

rem -----------------------------------------------------------------------------
rem Configuration & Metadata
rem -----------------------------------------------------------------------------
set "APP_NAME=Panex"
set "APP_EXE=Panex.exe"

rem Leave VERSION blank to dynamically read from Panex.csproj or appxmanifest.
rem Alternatively, override by setting explicitly: e.g., set "VERSION=1.0.0"
set "VERSION="

rem Default settings
set "ARCH=win-x64"
set "NO_PAUSE=0"

rem -----------------------------------------------------------------------------
rem Parse Command-Line Arguments
rem -----------------------------------------------------------------------------
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--no-pause" (
    set "NO_PAUSE=1"
    shift
    goto :parse_args
)
if /i "%~1"=="-n" (
    set "NO_PAUSE=1"
    shift
    goto :parse_args
)
if /i "%~1"=="/no-pause" (
    set "NO_PAUSE=1"
    shift
    goto :parse_args
)
rem Positional argument: Target Architecture
set "ARCH=%~1"
shift
goto :parse_args

:args_done
rem Normalize architecture names if needed
if /i "%ARCH%"=="x64" set "ARCH=win-x64"
if /i "%ARCH%"=="arm64" set "ARCH=win-arm64"
if /i "%ARCH%"=="x86" set "ARCH=win-x86"

rem -----------------------------------------------------------------------------
rem Dynamic Version Detection
rem -----------------------------------------------------------------------------
if "%VERSION%"=="" (
    if exist "%PROJECT_FILE%" (
        for /f "tokens=3 delims=<>" %%v in ('findstr /i "<Version>" "%PROJECT_FILE%" 2^>nul') do (
            set "VERSION=%%v"
        )
    )
)
if "%VERSION%"=="" (
    if exist "%PROJECT_DIR%\Package.appxmanifest" (
        for /f "tokens=2 delims==" %%v in ('findstr /i "Version=" "%PROJECT_DIR%\Package.appxmanifest" 2^>nul') do (
            for /f "tokens=1 delims= " %%a in ("%%~v") do (
                for /f "tokens=1,2,3 delims=." %%x in ("%%~a") do set "VERSION=%%x.%%y.%%z"
            )
        )
    )
)
if "%VERSION%"=="" set "VERSION=1.0.0"
set "VERSION=%VERSION: =%"

rem Deliverable filenames (includes architecture tag as per multi-architecture rule)
set "ZIP_FILENAME=%APP_NAME%-v%VERSION%-%ARCH%-Portable.zip"
set "SETUP_BASENAME=%APP_NAME%-v%VERSION%-%ARCH%-Setup"
set "SETUP_FILENAME=%SETUP_BASENAME%.exe"

echo =============================================================================
echo Starting Release Packaging Pipeline
echo =============================================================================
echo Application:       %APP_NAME%
echo Version:           %VERSION%
echo Architecture:      %ARCH%
echo Root Directory:    %ROOT_DIR%
echo Project File:      %PROJECT_FILE%
echo Dist Directory:    %DIST_DIR%
echo Staging Directory: %STAGING_DIR%
echo =============================================================================

rem -----------------------------------------------------------------------------
rem Step 1: Clean Slate
rem -----------------------------------------------------------------------------
echo.
echo [1/5] Cleaning previous distribution and staging directories...
if exist "%STAGING_DIR%" (
    rmdir /s /q "%STAGING_DIR%" 2>nul
)
if exist "%DIST_DIR%" (
    rmdir /s /q "%DIST_DIR%" 2>nul
)
mkdir "%STAGING_DIR%" || (
    echo Error: Failed to create staging directory: %STAGING_DIR%
    exit /b 1
)

rem -----------------------------------------------------------------------------
rem Step 2: Publish Application
rem -----------------------------------------------------------------------------
echo.
echo [2/5] Publishing %APP_NAME% Release build (%ARCH%)...
dotnet publish "%PROJECT_FILE%" ^
    -c Release ^
    -p:PublishProfile=%ARCH% ^
    -p:WindowsAppSDKSelfContained=true ^
    -p:SatelliteResourceLanguages=en-US ^
    -o "%STAGING_DIR%"
if errorlevel 1 (
    echo Error: dotnet publish failed.
    exit /b 1
)

rem -----------------------------------------------------------------------------
rem Step 3: English-Only Enforcement (Purge Satellite Folders)
rem -----------------------------------------------------------------------------
echo.
echo [3/5] Purging unwanted satellite language resource folders...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$staging = '%STAGING_DIR%';" ^
    "Get-ChildItem -Path $staging -Directory | Where-Object { $_.Name -notmatch '^(en-us|en-US)$' -and (Get-ChildItem -Path $_.FullName -Filter '*.mui' -Recurse | Select-Object -First 1) } | ForEach-Object { Write-Host '  Purged satellite folder:' $_.Name; Remove-Item -LiteralPath $_.FullName -Recurse -Force };"

rem -----------------------------------------------------------------------------
rem Step 4: Stage License Notices & Create Portable ZIP
rem -----------------------------------------------------------------------------
echo.
if exist "%ROOT_DIR%\LICENSE" copy /y "%ROOT_DIR%\LICENSE" "%STAGING_DIR%\LICENSE" >nul
if exist "%ROOT_DIR%\NOTICE" copy /y "%ROOT_DIR%\NOTICE" "%STAGING_DIR%\NOTICE" >nul
echo [4/5] Creating portable ZIP package: %ZIP_FILENAME%
if exist "%DIST_DIR%\%ZIP_FILENAME%" del /f /q "%DIST_DIR%\%ZIP_FILENAME%"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Compress-Archive -Path '%STAGING_DIR%\*' -DestinationPath '%DIST_DIR%\%ZIP_FILENAME%' -Force"
if errorlevel 1 (
    echo Error: Failed to create portable ZIP archive.
    exit /b 1
)
echo Portable archive created successfully.

rem -----------------------------------------------------------------------------
rem Step 5: Compile Installer with Inno Setup
rem -----------------------------------------------------------------------------
echo.
echo [5/5] Checking for Inno Setup compiler (ISCC.exe)...
set "ISCC_EXE="
where iscc.exe >nul 2>&1
if not errorlevel 1 (
    for /f "delims=" %%I in ('where iscc.exe') do (
        if not defined ISCC_EXE set "ISCC_EXE=%%I"
    )
)
if defined ISCC_EXE goto :found_iscc

if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    goto :found_iscc
)
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
    set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
    goto :found_iscc
)
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
    set "ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
    goto :found_iscc
)

echo Warning: Inno Setup compiler (ISCC.exe) was not found in PATH or standard installation folders.
echo Standard paths checked:
echo   - %ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
echo   - %ProgramFiles%\Inno Setup 6\ISCC.exe
echo   - %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
echo Skipping setup installer generation.
goto :cleanup

:found_iscc
echo Located Inno Setup compiler: "%ISCC_EXE%"
echo Compiling installer: %SETUP_FILENAME%...
"%ISCC_EXE%" ^
    "/DAppName=%APP_NAME%" ^
    "/DAppVersion=%VERSION%" ^
    "/DAppExeName=%APP_EXE%" ^
    "/DSourcePath=%STAGING_DIR%" ^
    "/DOutputBaseName=%SETUP_BASENAME%" ^
    "/O%DIST_DIR%" ^
    "%ISS_FILE%"
if errorlevel 1 (
    echo Error: Inno Setup compilation failed.
    exit /b 1
)
echo Installer created successfully.

:cleanup
rem -----------------------------------------------------------------------------
rem Cleanup Staging Directory
rem -----------------------------------------------------------------------------
echo.
echo Cleaning up temporary staging directory...
if exist "%STAGING_DIR%" (
    rmdir /s /q "%STAGING_DIR%" 2>nul
)

rem -----------------------------------------------------------------------------
rem Summary & Verification
rem -----------------------------------------------------------------------------
echo.
echo =============================================================================
echo Release Packaging Complete!
echo Final deliverables located in: %DIST_DIR%
echo =============================================================================
dir /b "%DIST_DIR%"
echo =============================================================================

rem Pause only if double-clicked in Windows Explorer (interactive) and CI/NO_PAUSE is not set
if not defined CI if "%NO_PAUSE%"=="0" (
    echo %CMDCMDLINE% | findstr /i /c:"\"%~f0\"" >nul 2>&1
    if not errorlevel 1 pause
)

exit /b 0
