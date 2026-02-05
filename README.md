# Resaa Scanner Service

A professional, production-ready scanner service for Windows with a clean REST API and system tray interface. Built with .NET 8 and Clean Architecture principles.

## Features

- 🖨️ **Multi-Driver Support** - TWAIN, WIA, and ESCL (eSCL/AirPrint) scanner drivers
- 🎯 **Multiple Profiles** - Save and manage different scan configurations
- 📄 **Multiple Output Formats** - PDF, JPEG, PNG, TIFF, Multi-page TIFF
- 🌐 **REST API** - Full-featured API with OpenAPI/Swagger documentation
- 🔧 **System Tray App** - Lightweight tray application for easy management
- ✅ **Input Validation** - FluentValidation for robust data validation
- 📊 **Production Logging** - Serilog with configurable retention policies
- 🏗️ **Clean Architecture** - Maintainable, testable, and scalable codebase

## Architecture

```
ScannerService/
├── ScannerService.Domain/          # Entities and core domain logic
├── ScannerService.Application/     # Business logic, DTOs, interfaces, validators
├── ScannerService.Infrastructure/  # Data access, scanner hardware integration
└── ScannerService.TrayApp/        # Windows Forms tray application & API host
```

## Tech Stack

- **.NET 8** - Latest LTS framework
- **Entity Framework Core** - SQLite database
- **NAPS2.Sdk** - Cross-platform scanning library
- **FluentValidation** - Input validation
- **Serilog** - Structured logging
- **NSwag** - OpenAPI documentation
- **Scalar** - Modern API documentation UI

## Prerequisites

- Windows 10/11 (64-bit)
- .NET 8 Runtime (included in installer for self-contained deployment)
- Scanner device with TWAIN, WIA, or ESCL support

## Installation

### End Users

1. Download `ResaaScannerSetup.exe` from [Releases](https://github.com/yourusername/scanner-service/releases)
2. Run the installer
3. Choose startup options (auto-start recommended)
4. The service will launch automatically in the system tray

### Developers

```bash
# Clone the repository
git clone https://github.com/yourusername/scanner-service.git
cd scanner-service

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the application
cd ScannerService.TrayApp
dotnet run
```

## Configuration

Edit `appsettings.json` to customize settings:

```json
{
  "ScannerService": {
    "ApiPort": 58472,
    "StatusCheckInterval": 5000,
    "HttpTimeout": 2000,
    "StartupDelay": 2000
  },
  "Logging": {
    "File": {
      "Path": "logs/scanner-.log",
      "RollingInterval": "Day",
      "RetainedFileCountLimit": 7,
      "FileSizeLimitBytes": 10485760,
      "RollOnFileSizeLimit": true
    }
  }
}
```

## API Usage

Once running, access the API at `http://localhost:58472`

### Quick Start

```bash
# Get all available scanners
curl http://localhost:58472/api/scanners

# Create a scan profile
curl -X POST http://localhost:58472/api/profiles \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Quick Scan",
    "deviceId": "your-scanner-id",
    "resolution": 300,
    "bitDepth": "Color",
    "paperSource": "Glass"
  }'

# Perform a scan
curl -X POST http://localhost:58472/api/scan \
  -H "Content-Type: application/json" \
  -d '{
    "profileId": 1,
    "outputFormat": "PDF"
  }'
```

### API Documentation

Interactive API documentation available at:
- **Scalar UI**: `http://localhost:58472/scalar/v1`
- **OpenAPI JSON**: `http://localhost:58472/openapi/v1.json`

## API Endpoints

### Scanners
- `GET /api/scanners` - List all available scanners
- `GET /api/health` - Health check

### Profiles
- `GET /api/profiles` - Get all profiles
- `GET /api/profiles/{id}` - Get profile by ID
- `POST /api/profiles` - Create new profile
- `PUT /api/profiles/{id}` - Update profile
- `DELETE /api/profiles/{id}` - Delete profile

### Scanning
- `POST /api/scan` - Perform scan with profile

### Export Settings
- `GET /api/export-settings` - Get export configuration
- `PUT /api/export-settings` - Update export configuration

## Profile Configuration

Profiles support the following settings:

| Setting | Options | Default |
|---------|---------|---------|
| **PaperSource** | Glass, Feeder | Glass |
| **BitDepth** | Color, Grayscale, BlackAndWhite | Color |
| **PageSize** | A4, A5, Letter, Legal | A4 |
| **HorizontalAlign** | Left, Center, Right | Center |
| **Resolution** | 50-1200 DPI | 200 |
| **Scale** | 1:1, 1:2, 1:4, 1:8 | 1:1 |
| **Brightness** | -100 to 100 | 0 |
| **Contrast** | -100 to 100 | 0 |
| **ImageQuality** | 1-100 | 85 |

## Output Formats

- **PDF** - Single or multi-page PDF
- **JPEG** - Individual JPEG files
- **PNG** - Individual PNG files  
- **TIFF** - Individual TIFF files
- **MultiPageTIFF** - Single multi-page TIFF

## Building from Source

### Development Build
```bash
dotnet build -c Debug
```

### Production Build
```bash
dotnet publish ScannerService.TrayApp/ScannerService.TrayApp.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishReadyToRun=true
```

### Create Installer

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php)

```bash
iscc installer.iss
```

Output: `ResaaScannerSetup.exe`

## Project Structure

```
ScannerService/
├── src/
│   ├── ScannerService.Domain/
│   │   └── Entities/              # Core domain entities
│   ├── ScannerService.Application/
│   │   ├── DTOs/                  # Data transfer objects
│   │   ├── Interfaces/            # Service interfaces
│   │   └── Validators/            # FluentValidation rules
│   ├── ScannerService.Infrastructure/
│   │   ├── Persistence/           # EF Core DbContext
│   │   ├── Repositories/          # Data access layer
│   │   └── Services/              # Scanner hardware integration
│   └── ScannerService.TrayApp/
│       ├── Configurations/        # API host service
│       ├── WebApiHostService.cs   # Configuration models
│       └── TrayApp.cs             # System tray UI
├── installer.iss                  # Inno Setup script
└── appsettings.json               # Configuration file
```

## Logging

Logs are stored in the `logs/` directory with the following behavior:

- **Rolling**: Daily log files (`scanner-20260203.log`)
- **Retention**: Configurable (default: 7 days)
- **Size Limit**: Configurable per file (default: 10 MB)
- **Format**: Timestamped with log level and source context

Example log entry:
```
2026-02-03 20:24:16.482 +03:30 [INF] Scanner Service API started successfully on port 58472
```

## Troubleshooting

### Port Already in Use
If port 58472 is already in use, change it in `appsettings.json`:
```json
"ScannerService": {
  "ApiPort": 58473
}
```

### Scanner Not Detected
1. Ensure scanner drivers are installed
2. Check Windows Device Manager for scanner visibility
3. Try different drivers (WIA usually most reliable on Windows)
4. Check logs for detailed error messages

### TWAIN Worker Failed
This is a known issue with NAPS2 in some environments. The service will automatically fall back to WIA driver. This is expected behavior and does not affect functionality.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [NAPS2](https://github.com/cyanfish/naps2) - Scanning SDK
- [Serilog](https://serilog.net/) - Logging framework
- [FluentValidation](https://fluentvalidation.net/) - Validation library

## Support

For issues and questions:
- 🐛 [Report a bug](https://github.com/yourusername/scanner-service/issues)
- 💡 [Request a feature](https://github.com/yourusername/scanner-service/issues)
- 📧 Email: arezaqassemi@gmail.com

---

** Made with ❤️ by Resaa **