@echo off
setlocal enabledelayedexpansion
REM ============================================
REM nvGPUMonitor MSI Installer Build Script
REM ============================================

echo.
echo ========================================
echo Building nvGPUMonitor MSI Installer
echo ========================================
echo.

REM Step 1: Publish the application as self-contained
echo [1/4] Publishing application...
cd ..
dotnet publish nvGPUMonitor.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
if %errorlevel% neq 0 (
    echo ERROR: Failed to publish application
    pause
    exit /b 1
)
echo     Success!
echo.

REM Step 1.5: Auto-detect publish directory
echo [1.5/4] Detecting publish directory...
set "PUBLISH_DIR="

REM Search for nvGPUMonitor.Wpf.exe in the Release folder
for /f "delims=" %%i in ('dir /b /s /a-d "bin\Release\*nvGPUMonitor.Wpf.exe" 2^>nul') do (
    set "EXE_PATH=%%i"
    for %%j in ("!EXE_PATH!") do (
        set "PUBLISH_DIR=%%~dpj"
    )
)

if not defined PUBLISH_DIR (
    echo ERROR: Could not find nvGPUMonitor.Wpf.exe in publish directory
    pause
    exit /b 1
)

REM Remove trailing backslash
if "%PUBLISH_DIR:~-1%"=="\" set "PUBLISH_DIR=%PUBLISH_DIR:~0,-1%"

echo     Found: %PUBLISH_DIR%
echo.

REM Step 2: Build the WiX installer
echo [2/4] Building WiX installer...
cd Installer

REM Check if WiX is installed
where candle.exe >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: WiX Toolset not found. Please install from https://wixtoolset.org/releases/
    echo.
    echo After installing WiX, make sure it's in your PATH or run this from WiX command prompt
    pause
    exit /b 1
)

REM Compile WiX source
candle.exe Product.wxs -ext WixUIExtension -dPublishDir="%PUBLISH_DIR%\"
if %errorlevel% neq 0 (
    echo ERROR: Failed to compile WiX source
    pause
    exit /b 1
)
echo     Compilation successful!
echo.

REM Step 3: Link and create MSI
echo [3/4] Creating MSI package...
light.exe Product.wixobj -ext WixUIExtension -out nvGPUMonitor-Setup.msi -sice:ICE61
if %errorlevel% neq 0 (
    echo ERROR: Failed to create MSI
    pause
    exit /b 1
)
echo     MSI created successfully!
echo.

REM Step 4: Clean up intermediate files
echo [4/4] Cleaning up...
del Product.wixobj >nul 2>&1
del nvGPUMonitor-Setup.wixpdb >nul 2>&1
echo     Cleanup complete!
echo.

echo ========================================
echo BUILD COMPLETE!
echo ========================================
echo.
echo MSI file location:
echo %CD%\nvGPUMonitor-Setup.msi
echo.
echo You can now install this MSI on any Windows machine.
echo.
pause
