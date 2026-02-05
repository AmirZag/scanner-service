[Setup]
AppName=Resaa Scanner Service
AppVersion=1.0
DefaultDirName={pf}\ResaaScanner
DefaultGroupName=Resaa Softwares
OutputDir=.
OutputBaseFilename=ResaaScannerSetup
Compression=lzma2
SolidCompression=yes
SetupIconFile=src\ScannerService.TrayApp\Properties\app.ico
UninstallDisplayIcon={app}\ScannerService.TrayApp.exe

[Files]
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; IconFilename: "{app}\ScannerService.TrayApp.exe"; IconIndex: 0
Name: "{commonstartup}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; IconFilename: "{app}\ScannerService.TrayApp.exe"; IconIndex: 0

[Run]
Filename: "{app}\ScannerService.TrayApp.exe"; Description: "Launch Scanner Service"; Flags: nowait postinstall skipifsilent