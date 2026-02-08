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
- 🔐 **Auto-Elevation** - Automatically requests administrator privileges when needed

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
- **NSwag** - OpenAPI document generation
- **Scalar** - Modern API documentation UI
- **Windows Forms** - System tray interface

## Prerequisites

- Windows 10/11 (64-bit)
- .NET 8 Runtime (included in installer for self-contained deployment)
- Scanner device with TWAIN, WIA, or ESCL support
- **Administrator privileges** (required for scanner access)

## Installation

### End Users

1. Download `ResaaScannerSetup.exe` from [Releases](https://github.com/yourusername/scanner-service/releases)
2. **Run the installer as Administrator** (right-click → "Run as administrator")
3. The installer will:
   - Install the application to `Program Files\ResaaScanner`
   - Create a start menu shortcut
   - Add to Windows startup (optional)
4. The service will launch automatically in the system tray
5. **Note:** The application will automatically request administrator privileges on subsequent launches

### Developers

```bash
# Clone the repository
git clone https://github.com/yourusername/scanner-service.git
cd scanner-service

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the application (requires administrator privileges)
cd src/ScannerService.TrayApp
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
  "DatabasePath": "scanner.db",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    },
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
    "format": "PDF"
  }' --output scan.pdf
```

### API Documentation

Interactive API documentation available at:
- **Scalar UI**: `http://localhost:58472/scalar/openapi`
- **OpenAPI JSON**: `http://localhost:58472/openapi/openapi.json`

You can also access the Scalar UI directly from the system tray menu.

## API Endpoints

### Health
- `GET /api/health` - Health check

### Scanners
- `GET /api/scanners` - List all available scanners

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
dotnet publish src/ScannerService.TrayApp/ScannerService.TrayApp.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishReadyToRun=true
```

### Create Installer

**Requirements:**
- [Inno Setup 6](https://jrsoftware.org/isdl.php)
- **Run Inno Setup Compiler as Administrator**

```bash
# Right-click on Inno Setup Compiler → Run as administrator
# Then compile the script
iscc Installer.iss
```

**Important:** Inno Setup must be run as administrator to properly set up the application with required privileges.

Output: `ResaaScannerSetup.exe`

## Project Structure

```
ScannerService/
├── src/
│   ├── ScannerService.Domain/
│   │   └── Entities/              # Core domain entities (Profile, ExportSetting)
│   ├── ScannerService.Application/
│   │   ├── DTOs/                  # Data transfer objects
│   │   ├── Interfaces/            # Service interfaces
│   │   └── Validators/            # FluentValidation rules
│   ├── ScannerService.Infrastructure/
│   │   ├── Persistence/           # EF Core DbContext
│   │   ├── Repositories/          # Data access layer
│   │   └── Services/              # Scanner hardware integration
│   └── ScannerService.TrayApp/
│       ├── Configurations/        # Configuration models
│       ├── WebApiHostService.cs   # API host service
│       ├── TrayApp.cs             # System tray UI
│       ├── Program.cs             # Entry point with auto-elevation
│       └── Properties/
│           ├── app.ico            # Application icon
│           └── Resources.resx     # Localized resources
├── Directory.Build.props          # Global project properties
├── Directory.Packages.props       # Centralized package management
├── Installer.iss                  # Inno Setup script
└── appsettings.json              # Configuration file
```

## Centralized Package Management

This project uses **Central Package Management** (CPM) for consistent versioning across all projects:

- **Directory.Packages.props** - Defines all package versions centrally
- **Directory.Build.props** - Global project settings and analyzers

Benefits:
- Single source of truth for package versions
- Easier updates and maintenance
- Consistent analyzer and code quality settings

## Code Quality

The project enforces strict code quality standards:

- **TreatWarningsAsErrors** - All warnings treated as errors
- **SonarAnalyzer.CSharp** - Advanced code analysis
- **EnforceCodeStyleInBuild** - Enforces coding standards during build
- **Analysis Level** - Latest C# analysis features

## Logging

Logs are stored in the `logs/` directory with the following behavior:

- **Rolling**: Daily log files (`scanner-20260208.log`)
- **Retention**: Configurable (default: 7 days)
- **Size Limit**: Configurable per file (default: 10 MB)
- **Format**: Timestamped with log level and source context

Example log entry:
```
2026-02-08 14:30:42.156 +00:00 [INF] ScannerService.TrayApp.Services.WebApiHostService Scanner Service API started successfully on port 58472
```

## Troubleshooting

### Administrator Privileges Required

**Symptom:** Application doesn't detect scanners or can't access hardware

**Solution:** 
- The application automatically requests administrator privileges on launch
- If manually running from Visual Studio or command line, ensure you run as administrator
- For the installer: Right-click `Installer.iss` in Inno Setup Compiler and select "Run as administrator"

### Port Already in Use

**Symptom:** Error message about port 58472 already in use

**Solution:** Change the port in `appsettings.json`:
```json
"ScannerService": {
  "ApiPort": 58473
}
```

### Scanner Not Detected

1. Ensure scanner drivers are installed
2. Check Windows Device Manager for scanner visibility
3. Verify the application is running with administrator privileges
4. Try different drivers (WIA usually most reliable on Windows)
5. Check logs in `logs/` directory for detailed error messages

### TWAIN Worker Failed

**Symptom:** Log message: "TWAIN worker setup failed"

**This is normal behavior:**
- NAPS2 may fail to initialize TWAIN in some environments
- The service automatically falls back to WIA driver
- WIA provides excellent compatibility on Windows
- This does not affect functionality

### Database Issues

**Location:** `scanner.db` in application directory

**Reset:** Delete `scanner.db` to recreate with default settings

### Build Errors

Ensure you have:
- .NET 8 SDK installed
- All NuGet packages restored (`dotnet restore`)
- Administrator privileges when building for deployment

## Localization

The application includes Persian (Farsi) localization:
- UI elements localized via `Resources.resx`
- Status messages in Persian for better UX in target market
- Date/time formatting using invariant culture for consistency

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Ensure code passes all analyzers and builds without warnings
4. Commit your changes (`git commit -m 'Add amazing feature'`)
5. Push to the branch (`git push origin feature/amazing-feature`)
6. Open a Pull Request

**Code Standards:**
- Follow existing code style and patterns
- All warnings must be resolved (treated as errors)
- Add appropriate logging
- Update documentation as needed

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [NAPS2](https://github.com/cyanfish/naps2) - Scanning SDK
- [Serilog](https://serilog.net/) - Logging framework
- [FluentValidation](https://fluentvalidation.net/) - Validation library
- [Scalar](https://github.com/scalar/scalar) - Modern API documentation
- [Inno Setup](https://jrsoftware.org/isinfo.php) - Windows installer

## Support

For issues and questions:
- 🐛 [Report a bug](https://github.com/Amirzag/scanner-service/issues)
- 💡 [Request a feature](https://github.com/Amirzag/scanner-service/issues)
- 📧 Email: reza.noei@chmail.ir
- 📧 Email: arezaqassemi@gmail.com

---

**Made with ❤️ by Reza Noei & Amirreza Ghasemi**