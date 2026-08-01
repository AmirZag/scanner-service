# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common Commands

### Build and Run
```bash
# Build the solution
dotnet build

# Run the application (Debug)
dotnet run --project src/ScannerService.TrayApp/ScannerService.TrayApp.csproj

# Run in Release mode
dotnet build -c Release
dotnet run --project src/ScannerService.TrayApp/ScannerService.TrayApp.csproj --configuration Release
```

### Build for Production
```bash
# Publish self-contained executable for Windows x64
dotnet publish src/ScannerService.TrayApp/ScannerService.TrayApp.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=false \
  /p:PublishReadyToRun=true \
  --output "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64"
```

### Create Installer
```bash
# Using PowerShell (requires Inno Setup and Administrator privileges)
.\build-installer.ps1
```

### Development
```bash
# Restore dependencies
dotnet restore

# Run tests (when test projects are added)
dotnet test
```

## Architecture

This project follows **Clean Architecture** with four distinct layers:

### Layer Dependency Flow
```
TrayApp (Presentation/Composition)
    ↓
Infrastructure (Data Access, External Services)
    ↓
Application (DTOs, Interfaces, Validators)
    ↓
Domain (Entities)
```

### Layer Responsibilities

**Domain** (`ScannerService.Domain/`)
- Pure entities with business logic (`Profile`, `ExportSetting`)
- No external dependencies
- Entities: `Profile` (scan configurations), `ExportSetting` (default export settings)

**Application** (`ScannerService.Application/`)
- DTOs for data transfer (`ScannerDto`, `ProfileDto`, `ScanRequestDto`, etc.)
- Interfaces for repositories and services (`IScannerService`, `IProfileRepository`, `IScanJobService`, etc.)
- FluentValidation validators for all input DTOs
- No infrastructure dependencies

**Infrastructure** (`ScannerService.Infrastructure/`)
- EF Core `Context` (SQLite database)
- Repository implementations (`ProfileRepository`, `ExportSettingRepository`)
- Scanner integration services (`ScannerService`, `ScanJobService`, `RecentScansService`)
- Uses NAPS2.Sdk for scanner hardware interaction

**TrayApp** (`ScannerService.TrayApp/`)
- Windows Forms system tray application
- ASP.NET Core Web API host (`WebApiHostService`)
- Minimal API endpoints defined in `ConfigureEndpoints()`
- Composition root for dependency injection
- Configuration management (`appsettings.json`)
- Rate limiting middleware

### Key Design Patterns

1. **Repository Pattern**: `IProfileRepository`, `IExportSettingRepository` with base class `RepositoryBase<T>`
2. **Service Layer**: `IScannerService`, `IScanJobService`, `IRecentScansService`
3. **DTO Pattern**: Separate DTOs in Application layer from Domain entities
4. **Validator Pattern**: FluentValidation validators for all DTOs

## Scanner Integration (NAPS2)

The application uses **NAPS2.Sdk** for cross-platform scanner support:

- **TWAIN**: Requires `NAPS2.Worker.exe` (Win32 worker process) - must be included in published output
- **WIA**: Native Windows scanner driver
- **ESCL**: Network scanner support

**Important**: `ScannerService` initialization includes `SetUpWin32Worker()` for TWAIN support. If this fails (e.g., antivirus blocking), TWAIN will be unavailable but WIA/ESCL will continue to work.

### Admin Privileges

Most modern scanners (WIA/ESCL) work without admin privileges. TWAIN-only scanners may require administrator access. The tray app provides "اجرا با دسترسی ادمین" (Run as Admin) option in the context menu.

## API Configuration

- Default port: `58472` (configurable in `appsettings.json`)
- If port is in use, automatically finds alternative port
- CORS enabled for local network (localhost, 127.x.x.x, private IP ranges)
- Rate limiting: 100 requests/minute
- API documentation: `/scalar` endpoint (Scalar UI)
- OpenAPI spec: `/openapi/openapi.json`

## Database

- SQLite database (`scanner.db`) created in application directory on first run
- Schema managed by EF Core `Context` with `EnsureCreatedAsync()`
- Seed data: Default `ExportSetting` (Id=1, Format=PDF, FileName="scan_{datetime}")

## Code Quality Settings

- All warnings treated as errors (`TreatWarningsAsErrors=true`)
- SonarAnalyzer.CSharp static analysis enabled
- Nullable reference types enabled
- Implicit usings enabled
- Target framework: .NET 8

## Localization

The tray application UI strings are in Persian (Farsi) and defined in `Properties/Resources.resx`. When adding UI elements, add corresponding resource strings.

## Configuration Files

- `appsettings.json`: Main configuration (API port, timeouts, logging)
- `Directory.Build.props`: Global MSBuild settings (code quality, analyzers)
- `Directory.Packages.props`: Central package version management
- `Installer.iss`: Inno Setup installer script

## Adding New Features

Follow the layer dependency order:
1. **Domain**: Add or modify entities
2. **Application**: Add DTOs, interfaces, validators
3. **Infrastructure**: Implement repositories/services
4. **TrayApp**: Add API endpoints, configure DI in `WebApiHostService.StartAsync()`

### Adding an API Endpoint

Add the endpoint in `WebApiHostService.ConfigureEndpoints()`:
```csharp
app.MapGet("/api/endpoint", async (IRepository svc) =>
    Results.Ok(await svc.MethodAsync()))
    .WithName("MethodName")
    .WithTags("Category")
    .Produces(StatusCodes.Status200OK);
```
