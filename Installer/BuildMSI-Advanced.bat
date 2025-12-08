@echo off
setlocal enabledelayedexpansion
REM ============================================
REM Advanced nvGPUMonitor MSI Build with Heat
REM ============================================

echo.
echo ========================================
echo Building nvGPUMonitor MSI Installer (Advanced)
echo ========================================
echo.

REM Add WiX to PATH
set "PATH=C:\Program Files (x86)\WiX Toolset v3.14\bin;%PATH%"
set "PATH=C:\Program Files\WiX Toolset v3.14\bin;%PATH%"

REM Step 1: Publish the application
echo [1/5] Publishing application...
cd ..
dotnet publish nvGPUMonitor.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
if %errorlevel% neq 0 (
    echo ERROR: Failed to publish application
    pause
    exit /b 1
)
echo     Success!
echo.

REM Step 1.5: Detect the actual publish directory
echo [1.5/5] Detecting publish directory...
set "PUBLISH_DIR="

REM Search for nvGPUMonitor.exe in the Release folder
for /f "delims=" %%i in ('dir /b /s /a-d "bin\Release\nvGPUMonitor.exe" 2^>nul ^| findstr /i "publish"') do (
    set "EXE_PATH=%%i"
    for %%j in ("!EXE_PATH!") do (
        set "PUBLISH_DIR=%%~dpj"
    )
)

if not defined PUBLISH_DIR (
    echo ERROR: Could not find nvGPUMonitor.exe in publish directory
    pause
    exit /b 1
)

REM Remove trailing backslash if present
if "%PUBLISH_DIR:~-1%"=="\" set "PUBLISH_DIR=%PUBLISH_DIR:~0,-1%"

echo     Found: %PUBLISH_DIR%
echo.

REM Step 2: Use Heat to harvest all files
echo [2/5] Harvesting published files...
cd Installer

heat.exe dir "%PUBLISH_DIR%" -cg PublishedFiles -gg -scom -sreg -sfrag -srd -dr INSTALLFOLDER -var var.PublishDir -platform x64 -out HarvestedFiles.wxs
if %errorlevel% neq 0 (
    echo ERROR: Failed to harvest files
    pause
    exit /b 1
)
echo     Files harvested successfully!
echo.

REM Step 3: Compile all WiX sources (NO TRAILING BACKSLASH)
echo [3/5] Compiling WiX sources...
candle.exe Product-Simple.wxs HarvestedFiles.wxs -ext WixUIExtension -dPublishDir="%PUBLISH_DIR%"
if %errorlevel% neq 0 (
    echo ERROR: Failed to compile WiX sources
    pause
    exit /b 1
)
echo     Compilation successful!
echo.

REM Step 4: Link and create MSI
echo [4/5] Creating MSI package...
light.exe Product-Simple.wixobj HarvestedFiles.wixobj -ext WixUIExtension -out nvGPUMonitor-Setup.msi -sice:ICE61 -sice:ICE80
if %errorlevel% neq 0 (
    echo ERROR: Failed to create MSI
    pause
    exit /b 1
)
echo     MSI created successfully!
echo.

REM Step 5: Clean up
echo [5/5] Cleaning up...
del *.wixobj >nul 2>&1
del *.wixpdb >nul 2>&1
del HarvestedFiles.wxs >nul 2>&1
echo     Cleanup complete!
echo.

echo ========================================
echo BUILD COMPLETE!
echo ========================================
echo.
echo MSI file: %CD%\nvGPUMonitor-Setup.msi
echo.
pause
