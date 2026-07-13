@echo off
REM Build script for Scanner Service Installer
REM Run this script to create the final installer executable

echo === Scanner Service Installer Build ===

REM Step 1: Clean previous builds
echo.
echo [1/5] Cleaning previous builds...
if exist "src\ScannerService.TrayApp\bin\Release" rmdir /s /q "src\ScannerService.TrayApp\bin\Release"
if exist "src\ScannerService.TrayApp\bin\Debug" rmdir /s /q "src\ScannerService.TrayApp\bin\Debug"
if exist "InstallerOutput" rmdir /s /q "InstallerOutput"

REM Step 2: Build the solution
echo.
echo [2/5] Building solution...
dotnet build src/ScannerService.Domain/ScannerService.Domain.csproj -c Release
dotnet build src/ScannerService.Application/ScannerService.Application.csproj -c Release
dotnet build src/ScannerService.Infrastructure/ScannerService.Infrastructure.csproj -c Release
dotnet build src/ScannerService.TrayApp/ScannerService.TrayApp.csproj -c Release

if errorlevel 1 goto :build_failed

REM Step 3: Publish the TrayApp
echo.
echo [3/5] Publishing TrayApp...
dotnet publish src/ScannerService.TrayApp/ScannerService.TrayApp.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishReadyToRun=true --output "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64"

if errorlevel 1 goto :build_failed

REM Step 4: Remove PDB files
echo.
echo [4/5] Removing debug symbols...
del /q "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\*.pdb" 2>nul

REM Step 5: Create installer
echo.
echo [5/5] Creating installer...
if not exist "InstallerOutput" mkdir "InstallerOutput"

REM Try to find Inno Setup
set "ISCC_PATH="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles(x86)%\Inno Setup 5\ISCC.exe" set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 5\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 5\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 5\ISCC.exe"

if defined ISCC_PATH (
    echo Found Inno Setup at: %ISCC_PATH%
    "%ISCC_PATH%" "Installer.iss"

    if errorlevel 1 goto :installer_failed

    echo.
    echo === Build Complete ===
    echo Installer location: %cd%\InstallerOutput\ResaaScannerSetup.exe
    goto :end
) else (
    echo.
    echo ERROR: Inno Setup not found!
    echo Please install from: https://jrsoftware.org/isdl.php
    goto :end
)

:build_failed
echo.
echo Build failed!
exit /b 1

:installer_failed
echo.
echo Installer compilation failed!
exit /b 1

:end
pause
