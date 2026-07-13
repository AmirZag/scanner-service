# Build script for Scanner Service Installer
# Run this script to create the final installer executable

Write-Host "=== Scanner Service Installer Build ===" -ForegroundColor Cyan

# Step 1: Clean previous builds
Write-Host "`n[1/5] Cleaning previous builds..." -ForegroundColor Yellow
Remove-Item -Path "src\ScannerService.TrayApp\bin\Release" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src\ScannerService.TrayApp\bin\Debug" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "InstallerOutput" -Recurse -Force -ErrorAction SilentlyContinue

# Step 2: Build the solution
Write-Host "`n[2/5] Building solution..." -ForegroundColor Yellow
dotnet build src/ScannerService.Domain/ScannerService.Domain.csproj -c Release
dotnet build src/ScannerService.Application/ScannerService.Application.csproj -c Release
dotnet build src/ScannerService.Infrastructure/ScannerService.Infrastructure.csproj -c Release
dotnet build src/ScannerService.TrayApp/ScannerService.TrayApp.csproj -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Publish the TrayApp
Write-Host "`n[3/5] Publishing TrayApp..." -ForegroundColor Yellow
dotnet publish src/ScannerService.TrayApp/ScannerService.TrayApp.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=true `
    --output "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64"

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n❌ Publish failed!" -ForegroundColor Red
    exit 1
}

# Step 4: Remove PDB files (debug symbols) from publish output
Write-Host "`n[4/5] Removing debug symbols..." -ForegroundColor Yellow
Remove-Item -Path "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\*.pdb" -Force -ErrorAction SilentlyContinue

# Step 5: Create installer output directory
Write-Host "`n[5/5] Creating installer..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path "InstallerOutput" -Force | Out-Null

# Check if Inno Setup is available
$innosetupPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
)

$isccPath = $null
foreach ($path in $innosetupPaths) {
    if (Test-Path $path) {
        $isccPath = $path
        break
    }
}

if ($isccPath) {
    Write-Host "Found Inno Setup at: $isccPath" -ForegroundColor Green

    # Compile the installer
    & $isccPath "Installer.iss"

    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✅ Installer created successfully!" -ForegroundColor Green
        Write-Host "Location: $(Get-Location)\InstallerOutput\ResaaScannerSetup.exe" -ForegroundColor Cyan
    } else {
        Write-Host "`n❌ Installer compilation failed!" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`n❌ Inno Setup not found!" -ForegroundColor Red
    Write-Host "Please install Inno Setup from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "Then run this script again." -ForegroundColor Yellow
    exit 1
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Cyan
