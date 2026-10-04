# Resaa Scanner Service

A .NET 10 Windows scanner service with Clean Architecture, providing a REST API for document scanning operations. This service runs as a system tray application and hosts a Web API for managing scanners, profiles, and scan operations.

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Network Scanner Support (Driverless eSCL)](#network-scanner-support-driverless-escl)
- [Development](#development)
- [API Documentation](#api-documentation)
- [Project Structure](#project-structure)
- [Configuration](#configuration)
- [Building for Production](#building-for-production)
- [Creating Installer](#creating-installer)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)

## Features

- **Multi-Driver Scanner Support**: TWAIN, WIA, and ESCL (eScan) drivers
- **RESTful API**: Clean ASP.NET Core Web API with OpenAPI/Swagger documentation
- **Multiple Output Formats**: PDF, JPEG, PNG, TIFF, MultiPageTIFF, ZIP
- **Profile Management**: Save and reuse scan configurations
- **System Tray Application**: Runs in background with auto-startup capability
- **Clean Architecture**: Domain-Driven Design with separated concerns
- **SQLite Database**: Local data persistence for profiles and settings
- **Rate Limiting**: Built-in API rate limiting (100 requests/minute)
- **Interactive API Documentation**: Scalar UI at `/scalar` endpoint

## Architecture

This project follows **Clean Architecture** principles with clear separation of concerns:

```
ScannerService/
├── src/
│   ├── ScannerService.Domain/          # Core entities (Profile, ExportSetting)
│   ├── ScannerService.Application/     # DTOs, interfaces, validators
│   ├── ScannerService.Infrastructure/  # EF Core, repositories, scanner integration
│   └── ScannerService.TrayApp/        # Windows Forms tray app + REST API host
```

### Layer Responsibilities

| Layer | Responsibility | Dependencies |
|-------|---------------|---------------|
| **Domain** | Pure entities with business logic | None |
| **Application** | DTOs, interfaces, validators | Domain |
| **Infrastructure** | Data access, external services, scanner SDK | Domain, Application |
| **TrayApp** | User interface, API hosting, composition | All layers |

## Tech Stack

### Backend
- **.NET 10** - Latest .NET LTS platform
- **C# 14** - Language features (nullable reference types, implicit usings)
- **ASP.NET Core** - Web API framework
- **Entity Framework Core 10.0.12** - ORM for database access
- **SQLite** - Embedded database

### Scanner Integration
- **NAPS2.Sdk 1.4.0** - Cross-platform scanning SDK
- **NAPS2.Images.Gdi** - Windows image processing
- **NAPS2.Sdk.Worker.Win32** - TWAIN worker process for Windows

### API Documentation
- **NSwag.AspNetCore 14.7.1** - OpenAPI specification generation
- **Scalar.AspNetCore 2.17.13** - Modern API documentation UI

### Validation & Logging
- **FluentValidation 12.1.1** - Input validation
- **Serilog.AspNetCore 10.0.0** - Structured logging with file sink

### Code Quality
- **SonarAnalyzer.CSharp** - Static analysis
- TreatWarningsAsErrors = true
- AnalysisLevel = latest

## Prerequisites

### For Development
- **.NET 10 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Visual Studio 2022** or **JetBrains Rider** (recommended)
- Windows 10/11 x64 operating system

### For Building Installer
- **Inno Setup 6** - [Download here](https://jrsoftware.org/isdl.php)
  - Must be run as Administrator when creating installer

### For Running
- Windows 10/11 x64
- Scanner with TWAIN, WIA, or ESCL driver
- (Optional) Administrator privileges - only needed for some TWAIN-only scanners. Most modern scanners (WIA/ESCL) work without admin. If needed, use "اجرا با دسترسی ادمین" (Run as Admin) in the tray menu.

## Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/Amirzag/scanner-service.git
cd scanner-service
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Build the Solution

```bash
dotnet build
```

### 4. Run the Application

```bash
cd src/ScannerService.TrayApp
dotnet run
```

The application will:
1. Start as a system tray application
2. Automatically restart with Administrator privileges if needed
3. Host the Web API on the configured port (default: 58472)
4. Create `scanner.db` SQLite database on first run

### 5. Access the API

- **API Documentation**: http://localhost:58472/scalar
- **OpenAPI Spec**: http://localhost:58472/openapi/openapi.json
- **Health Check**: http://localhost:58472/api/health

## Network Scanner Support (Driverless eSCL)

Network scanners such as the **HP ScanJet Pro 4500 fn1** work **without any vendor driver** through the
eSCL (AirScan) protocol. The app enumerates devices through the TWAIN, WIA, and eSCL drivers in
parallel; only TWAIN and WIA need the manufacturer's driver package — **eSCL does not**, so a network
scanner appears in `/api/scanners` with `"driver": "Escl"` even on a machine where no HP software is
installed. Create a profile bound to that device's id and scan it like any other device.

### Requirements for eSCL discovery

eSCL devices advertise themselves over mDNS/DNS-SD (`_uscan._tcp` / `_uscans._tcp`, UDP 5353). For
automatic discovery to work:

1. **Windows Firewall must allow inbound UDP 5353 for the app executable.** The device's multicast
   answers count as unsolicited inbound traffic and are dropped by the default policy. The app adds
   this rule automatically when it runs as administrator; when it runs unelevated it logs the exact
   `netsh` command to the log file. Manual command (as administrator):

   ```
   netsh advfirewall firewall add rule name="Resaa Scanner Service - mDNS discovery (UDP 5353)" dir=in action=allow protocol=UDP localport=5353 profile=any program="<install path>\ScannerService.TrayApp.exe"
   ```

2. **The scanner and this machine must be on the same subnet/VLAN.** mDNS is link-local multicast and
   never crosses subnet boundaries (mDNS reflectors/gateways exist, but can break source-address
   detection). This is the most common cause in segmented clinic networks.

3. **The network profile must not be Public.** On the Public profile Windows aggressively blocks
   discovery traffic.

4. **The scanner's eSCL/Web Services features must be enabled** in its Embedded Web Server (browse to
   `https://<scanner-ip>`, then *Networking → Advanced*: enable *WS-Discovery* / *WS-Scan*). If the
   device still is not discovered after reconfiguring, HP's documented field fix for this model is a
   firmware re-flash (e.g. v7.128) plus a factory reset in the EWS, then re-enabling those toggles.

### Manual device configuration (no mDNS needed)

When discovery cannot reach the scanner (blocked UDP 5353, segmented network, Wi-Fi client isolation),
configure the device by address. The preferred path is the settings API
(`PUT /api/settings` with an `esclManualDevices` array — see [API Documentation](#api-documentation));
it validates, persists to `appsettings.local.json`, and restarts the API host automatically so the
device appears in `/api/scanners` without touching any file. Editing `appsettings.json` by hand
remains possible for headless/first-boot setups:

```json
"ScannerService": {
  "EsclManualDevices": [
    { "Name": "HP ScanJet Pro 4500 fn1", "Address": "192.168.1.50" },
    { "Address": "http://192.168.1.51:8080/eSCL" }
  ]
}
```

- A bare host/IP uses HP's default endpoint `http://<host>:8080/eSCL`; a full eSCL root URL is used as-is
  (bare IPv6 is not accepted — use the bracketed URL form, e.g. `http://[fe80::1]:8080/eSCL`).
- `Name` is optional (defaults to the host). The device **id** is the full root URL — bind profiles to it.
- When automatic discovery *and* the manual configuration both reach the same scanner, it appears twice
  (discovered by UUID id, manual by URL id); either entry scans correctly — bind the profile to one.
- Restart the app after changing this section by hand (API-made changes apply themselves);
  `POST /api/scanners/refresh` clears the device list cache (or wait for the 30s TTL).

### Verifying the scanner is reachable

From Windows PowerShell (no installation needed):

```powershell
Test-NetConnection -ComputerName 192.168.1.50 -Port 8080
Invoke-WebRequest http://192.168.1.50:8080/eSCL/ScannerCapabilities
```

If the second command returns XML, the eSCL endpoint is alive and the app can scan it (discovery or
manual configuration are then purely a routing/firewall question).

### What still requires the HP driver package

TWAIN/WIA/ISIS scanning from the app's `Twain`/`Wia` devices, and the scanner's front-panel
scan-to-PC feature, require the HP software. Driverless paths are: eSCL via this app (recommended),
and Windows' inbox WSD scan driver (the device appears as a WIA "Web Services Device" when network
discovery and the *Function Discovery* services are enabled — less reliable, not required here).

## Development

### Running in Debug Mode

```bash
dotnet build -c Debug
dotnet run --project src/ScannerService.TrayApp/ScannerService.TrayApp.csproj
```

### Running in Release Mode

```bash
dotnet build -c Release
dotnet run --project src/ScannerService.TrayApp/ScannerService.TrayApp.csproj --configuration Release
```

### Code Style and Analysis

The project enforces strict code quality:
- All warnings are treated as errors
- SonarAnalyzer.CSharp provides static analysis
- Nullable reference types enabled
- Implicit usings enabled

### Running Tests

The unit/integration/E2E suite lives in `tests/ScannerService.UnitTests`:

```bash
dotnet test
```

With line coverage (gate: 100% of coverable lines, minus the documented exclusions in
`tests/Check-Coverage.ps1`):

```powershell
dotnet test tests/ScannerService.UnitTests --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
powershell -NoProfile -File tests/Check-Coverage.ps1
```

## API Documentation

The service provides a RESTful API with the following endpoints:

### Health
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | API health check |
| GET | `/api/health/detailed` | Dependency health (200 healthy / 503 unhealthy) |

### Scanners
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/scanners` | Get list of available scanners |

### Profiles
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/profiles` | Get all scan profiles |
| GET | `/api/profiles/{id}` | Get profile by ID |
| POST | `/api/profiles` | Create new profile |
| PATCH | `/api/profiles/{id}` | Update provided fields only |
| DELETE | `/api/profiles/{id}` | Delete profile |

### Scan Operations
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/scanners/refresh` | Clear the device cache (next listing re-enumerates) |
| POST | `/api/scan` | Execute a scan job |
| GET | `/api/recent-scans/{count}` | Most recent scan groups (count 1-100) |

### Export Settings
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/export-settings` | Get export configuration |
| PUT | `/api/export-settings` | Update export configuration |

### Settings (ScannerService configuration)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/settings` | The current settings (flat JSON, same shape as the PUT body) |
| PUT | `/api/settings` | Full replacement: send every field; validates, persists to `appsettings.local.json`, auto-restarts the API host |
| DELETE | `/api/settings/overrides` | Reset everything to the `appsettings.json` values and restart (no-op with `restarting:false` when nothing was overridden) |

PUT/DELETE respond immediately after persisting; the API host restarts in-process right after the
response is sent (same port, a couple of seconds of downtime, in-flight scans aborted). `ApiPort`
and `ApiHost` are read-only on purpose: the frontend addresses the agent at a fixed host:port.
Every field of the settings body carries a one-line description + allowed range — visible in the
interactive docs at `/scalar` (they come from the XML docs on `ScannerSettingsDto` and flow into
`/openapi/openapi.json`).

### Interactive Documentation

Visit **`/scalar`** (e.g., `http://localhost:58472/scalar`) for interactive API documentation with:
- Request/response examples
- Schema definitions
- Try-it-out functionality
- Code samples in multiple languages

### Example: Get Scanners

```bash
curl http://localhost:58472/api/scanners
```

Response:
```json
[
  {
    "id": "TWAIN::{Scanner-Name}",
    "name": "HP Scanner Pro",
    "driver": "Twain"
  }
]
```

### Example: Execute Scan

```bash
curl -X POST http://localhost:58472/api/scan \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "TWAIN::{Scanner-Name}",
    "format": "PDF",
    "resolution": 300,
    "bitDepth": "Color",
    "paperSource": "Feeder"
  }' \
  --output scan.pdf
```

## Project Structure

```
ScannerService/
├── src/
│   ├── ScannerService.Domain/
│   │   ├── Entities/
│   │   │   ├── Profile.cs              # Scan profile entity
│   │   │   └── ExportSetting.cs        # Export configuration entity
│   │   └── ScannerService.Domain.csproj
│   │
│   ├── ScannerService.Application/
│   │   ├── DTOs/
│   │   │   ├── ScannerDto.cs          # Scanner representation
│   │   │   ├── ProfileDto.cs          # Profile data transfer objects
│   │   │   ├── ExportSettingDto.cs     # Export settings DTO
│   │   │   ├── UpsertProfileDto.cs     # Create/update profile request
│   │   │   └── ScanRequestDto.cs       # Scan execution request
│   │   ├── Interfaces/
│   │   │   ├── IScannerQueries.cs     # Scanner query operations
│   │   │   ├── IScannerService.cs     # Scanner operations
│   │   │   ├── IProfileRepository.cs  # Profile data access
│   │   │   ├── IExportSettingRepository.cs # Export settings data access
│   │   │   └── IScanJobService.cs     # Scan job orchestration
│   │   ├── Validators/
│   │   │   ├── UpsertProfileValidator.cs
│   │   │   ├── ScanRequestValidator.cs
│   │   │   └── ExportSettingValidator.cs
│   │   └── ScannerService.Application.csproj
│   │
│   ├── ScannerService.Infrastructure/
│   │   ├── Persistence/
│   │   │   └── Context.cs             # EF Core DbContext
│   │   ├── Repositories/
│   │   │   ├── ProfileRepository.cs    # Profile data access implementation
│   │   │   ├── ExportSettingRepository.cs # Export settings implementation
│   │   │   └── RepositoryBase.cs       # Base repository with common operations
│   │   ├── Services/
│   │   │   ├── ScannerService.cs      # Scanner hardware integration (NAPS2)
│   │   │   └── ScanJobService.cs      # Scan job orchestration service
│   │   └── ScannerService.Infrastructure.csproj
│   │
│   └── ScannerService.TrayApp/
│       ├── Configurations/
│       │   ├── ScannerServiceConfiguration.cs
│       │   ├── LoggingConfiguration.cs
│       │   └── ConfigurationValidator.cs
│       ├── Middleware/
│       │   └── RateLimitMiddleware.cs  # Rate limiting implementation
│       ├── WebApiHostService.cs        # Web API hosting and lifecycle
│       ├── TrayApplicationContext.cs   # Application entry point
│       ├── Program.cs                  # Main program with auto-elevation
│       ├── Properties/
│       │   ├── Resources.resx         # Persian (Farsi) localization strings
│       │   └── app.ico                 # Application icon
│       ├── appsettings.json            # Configuration file
│       └── ScannerService.TrayApp.csproj
│
├── Directory.Build.props              # Global MSBuild settings
├── Directory.Packages.props           # Centralized package versions
├── Installer.iss                      # Inno Setup installer script
├── build-installer.bat                # Batch build script
├── build-installer.ps1                # PowerShell build script
└── README.md                          # This file
```

## Configuration

Configuration is managed through `appsettings.json`:

```json
{
  "ScannerService": {
    "ApiPort": 58472,               // Web API port
    "ApiHost": "localhost",         // Bind address: "localhost" (default), wildcard "*", or an IP address
    "StatusCheckInterval": 5000,    // Tray app status check interval (ms)
    "HttpTimeout": 2000,            // HTTP timeout (ms)
    "StartupDelay": 2000            // Startup delay (ms)
  },
  "Logging": {
    "File": {
      "Path": "logs/scanner-.log",              // Log file path pattern
      "RollingInterval": "Day",                 // Roll interval: Minute/Hour/Day/Month/Year
      "RetainedFileCountLimit": 7,             // Number of log files to retain
      "FileSizeLimitBytes": 10485760,          // Max log file size (10MB)
      "RollOnFileSizeLimit": true              // Create new file on size limit
    }
  }
}
```

### API Bind Address (`ApiHost`)

`ApiHost` controls which network address the Web API listens on. Accepted values (case-insensitive):

- `localhost` or `loopback` (default) — loopback only (`127.0.0.1` + `[::1]`); only this machine can connect.
- `*`, `+`, `any`, `0.0.0.0`, or `::` — all network interfaces; other devices on the network can connect.
- Any IP address literal (e.g. `192.168.1.50`, `::1`) — binds that address only. DNS host names are rejected at startup.

> **Security note:** the API has **no authentication**. Binding beyond loopback (`*` or an IP) exposes scanner control and scanned documents to every device that can reach the machine — a loud warning is logged at startup. The app then also ensures an inbound firewall rule (`Resaa Scanner Service - Web API (TCP)`, program-scoped): automatically when running elevated, otherwise via a single UAC prompt (declining it logs the exact `netsh` command to run manually). The tray status and API-docs URLs follow the configured bind. Restart the app after changing the value.

### Configuration Classes

- `ScannerServiceConfiguration` - Maps to `ScannerService` section
- `LoggingConfiguration` - Maps to `Logging` section
- `ConfigurationValidator` - Validates configuration on startup

## Building for Production

### Publish Self-Contained Executable

```bash
dotnet publish src/ScannerService.TrayApp/ScannerService.TrayApp.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=false \
  /p:PublishReadyToRun=true \
  --output "src\ScannerService.TrayApp\bin\Release\net10.0-windows\publish\win-x64"
```

**Output**: All required files in the specified output directory including:
- `ScannerService.TrayApp.exe` - Main executable
- `*.dll` - Required assemblies
- `NAPS2.Worker.exe` - TWAIN worker process (required for scanner support)
- `appsettings.json` - Configuration file

### Publish Settings Explanation

| Setting | Purpose |
|---------|---------|
| `-c Release` | Release build configuration |
| `-r win-x64` | Target Windows 64-bit |
| `--self-contained true` | Include .NET runtime with app |
| `/p:PublishSingleFile=false` | Keep files separate (required for NAPS2.Worker.exe) |
| `/p:PublishReadyToRun=true` | Pre-compile to native code (faster startup) |

## Creating Installer

### Using PowerShell (Recommended)

```powershell
.\build-installer.ps1
```

### Using Batch

```bash
.\build-installer.bat
```

### Manual Installer Creation

```bash
# 1. Build and publish
dotnet build -c Release
dotnet publish src/ScannerService.TrayApp/ScannerService.TrayApp.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishReadyToRun=true

# 2. Run Inno Setup (must be Administrator)
iscc Installer.iss
```

**Output**: `InstallerOutput\ResaaScannerSetup.exe`

### Installer Features

- **Maintenance Mode**: Detects existing installation and offers Repair/Modify/Uninstall
- **User-Only Installation**: Installs to `%LOCALAPPDATA%` (no admin required)
- **Auto-Startup**: Adds application to Windows startup
- **Shortcuts**: Creates Start Menu and Desktop shortcuts
- **Clean Uninstall**: Removes all files and registry entries

## Versioning

Versioning is **automatic** — you never number releases by hand. The base version is the
`<VersionPrefix>` property in [`Directory.Build.props`](Directory.Build.props) (set once,
e.g. `1.1.0`); at build time the **git commit count** is appended as the revision, so every
release gets a unique, always-increasing version:

```
<VersionPrefix>.<commitCount>      e.g.  1.1.0.84  ->  1.1.0.85  ->  ...
```

Building the installer after committing new work is all it takes — the artifact is named
`InstallerOutput/ResaaScannerSetup_1.1.0.85.exe` (the number is unique per commit, so identical
code always produces the identical version). Optionally bump `VersionPrefix` (e.g. to `1.2.0`)
when you want to signal a major/minor release.

The version flows automatically to:

| Where | How it gets the version |
|---|---|
| Every assembly & the exe (Explorer → Properties → Details) | `SetVersionFromGitCommitCount` target in `Directory.Build.props` |
| `GET /api/health` and `/api/health/detailed` | `ApiVersion` in `EndpointConfigurationExtensions` |
| OpenAPI document (`info.version`) | `AddOpenApiDocument` in `WebApiHostService` |
| Installer version (Add/Remove Programs, upgrade detection) | `GetFileVersionString` in `Installer.iss` |
| Installer file name (`InstallerOutput/ResaaScannerSetup_<version>.exe`) | `OutputBaseFilename` in `Installer.iss` |

## Troubleshooting

### Scanner Not Detected

**Problem**: `/api/scanners` returns empty array, or the network scanner is missing

**Possible Causes**:
1. USB scanner: manufacturer drivers not installed (TWAIN/WIA need them)
2. Network scanner: firewall blocks inbound UDP 5353 (mDNS), or scanner is on a different subnet/VLAN,
   or its eSCL/Web Services features are disabled in the device's own web settings
3. TWAIN worker process not available
4. Insufficient permissions

**Solutions**:
1. For network scanners see [Network Scanner Support (Driverless eSCL)](#network-scanner-support-driverless-escl) —
   no vendor driver is needed; check the per-driver lines in the log (`Driver "Escl" enumerated 0 device(s) ...`),
   add the firewall rule, or configure the device under `EsclManualDevices`
2. For USB scanners, install the manufacturer drivers
3. Ensure application runs as Administrator
4. Check logs in `%LOCALAPPDATA%\ResaaScanner\logs\`

### Port Already in Use

**Problem**: Application fails to start with "Port already in use" error

**Solutions**:
1. Change `ApiPort` in `appsettings.json`
2. Close other applications using the port
3. The app will automatically find an alternative port

### TWAIN Scanning Fails

**Problem**: TWAIN driver shows errors in logs

**Cause**: TWAIN requires special handling in 64-bit processes

**Solution**: The application includes `NAPS2.Worker.exe` for TWAIN support. Ensure:
- The installer included this file
- The file is in the application directory
- No antivirus is blocking the worker process

### Database Issues

**Problem**: Errors related to `scanner.db`

**Solutions**:
1. Delete `scanner.db` and let the app recreate it
2. Check write permissions in the installation directory
3. Ensure no other process is locking the database

### API Returns 200 with No Content

**Problem**: API returns success but no data

**Cause**: Scanner hardware integration issue

**Solutions**:
1. Test scanner with manufacturer software first
2. Ensure `NAPS2.Worker.exe` is present
3. Run application as Administrator
4. Check logs for specific scanner driver errors

## Contributing

We welcome contributions! Please follow these guidelines:

### Development Workflow

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes following the code style
4. Build and test: `dotnet build && dotnet test`
5. Commit with descriptive messages
6. Push to your fork
7. Submit a pull request

### Code Style Guidelines

- Follow C# naming conventions (PascalCase for public members)
- Use nullable reference types appropriately
- Write XML documentation comments for public APIs
- Add FluentValidation rules for all input DTOs
- Keep methods small and focused
- Use dependency injection for services

### Adding New Features

1. **Domain**: Add entities to `ScannerService.Domain`
2. **Application**: Add DTOs, interfaces, and validators
3. **Infrastructure**: Implement repositories and services
4. **TrayApp**: Add API endpoints and configure DI

### Testing

- Write unit tests for business logic
- Test API endpoints with integration tests
- Verify scanner operations with real hardware

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/Amirzag/scanner-service/issues)
- **Releases**: [GitHub Releases](https://github.com/Amirzag/scanner-service/releases)

## Acknowledgments

- **NAPS2** - Cross-platform scanning SDK
- **Inno Setup** - Installer creation tool
- **Scalar** - Modern API documentation
