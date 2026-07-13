# Resaa Scanner Service

A .NET 8 Windows scanner service with Clean Architecture, providing a REST API for document scanning operations. This service runs as a system tray application and hosts a Web API for managing scanners, profiles, and scan operations.

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
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
- **.NET 8** - Latest .NET platform
- **C# 12** - Language features (nullable reference types, implicit usings)
- **ASP.NET Core** - Web API framework
- **Entity Framework Core 8** - ORM for database access
- **SQLite** - Embedded database

### Scanner Integration
- **NAPS2.Sdk 1.2.1** - Cross-platform scanning SDK
- **NAPS2.Images.Gdi** - Windows image processing
- **NAPS2.Sdk.Worker.Win32** - TWAIN worker process for Windows

### API Documentation
- **NSwag.AspNetCore 14.6.3** - OpenAPI specification generation
- **Scalar.AspNetCore 1.2.52** - Modern API documentation UI

### Validation & Logging
- **FluentValidation 12.1.1** - Input validation
- **Serilog 8.0.3** - Structured logging with file sink

### Code Quality
- **SonarAnalyzer.CSharp** - Static analysis
- TreatWarningsAsErrors = true
- AnalysisLevel = latest

## Prerequisites

### For Development
- **.NET 8 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** or **JetBrains Rider** (recommended)
- Windows 10/11 x64 operating system

### For Building Installer
- **Inno Setup 6** - [Download here](https://jrsoftware.org/isdl.php)
  - Must be run as Administrator when creating installer

### For Running
- Windows 10/11 x64
- Scanner with TWAIN, WIA, or ESCL driver
- (Optional) Administrator privileges for full scanner hardware access

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

```bash
dotnet test
```

## API Documentation

The service provides a RESTful API with the following endpoints:

### Health
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | API health check |

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
| PUT | `/api/profiles/{id}` | Update existing profile |
| DELETE | `/api/profiles/{id}` | Delete profile |

### Scan Operations
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/scan` | Execute a scan job |

### Export Settings
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/export-settings` | Get export configuration |
| PUT | `/api/export-settings` | Update export configuration |

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
    "StatusCheckInterval": 5000,    // Tray app status check interval (ms)
    "HttpTimeout": 2000,            // HTTP timeout (ms)
    "StartupDelay": 2000            // Startup delay (ms)
  },
  "DatabasePath": "scanner.db",     // SQLite database filename
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    },
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
  --output "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64"
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

## Troubleshooting

### Scanner Not Detected

**Problem**: `/api/scanners` returns empty array

**Possible Causes**:
1. Scanner drivers not installed
2. TWAIN worker process not available
3. Insufficient permissions

**Solutions**:
1. Install scanner manufacturer drivers
2. Ensure application runs as Administrator
3. Check logs in `%LOCALAPPDATA%\ResaaScanner\logs\`

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
