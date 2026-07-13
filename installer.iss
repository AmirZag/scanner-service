[Setup]
AppName=Resaa Scanner Service
AppVersion=1.0
AppPublisher=Resaa Softwares
AppPublisherURL=https://github.com/Amirzag/scanner-service
AppSupportURL=https://github.com/Amirzag/scanner-service/issues
AppUpdatesURL=https://github.com/Amirzag/scanner-service/releases

DefaultDirName={localappdata}\ResaaScanner
DefaultGroupName=Resaa Softwares
OutputDir=InstallerOutput
OutputBaseFilename=ResaaScannerSetup
Compression=lzma2
SolidCompression=yes
SetupIconFile=src\ScannerService.TrayApp\Properties\app.ico
UninstallDisplayIcon={app}\ScannerService.TrayApp.exe

; Install to user profile (no admin required)
; PrivilegesRequired=admin
; PrivilegesRequiredOverridesAllowed=commandline

; Specify this is an x64 installer
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Close running instances before installation
CloseApplications=yes
CloseApplicationsFilter=ScannerService.TrayApp.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "persian"; MessagesFile: "compiler:Languages\Persian.isl"

[Messages]
english.WelcomeLabel1=Welcome to the Resaa Scanner Service Setup Wizard
english.WelcomeLabel2=This will install Resaa Scanner Service on your computer.
english.FinishedLabel=Resaa Scanner Service has been installed successfully.
english.FinishedLabel2=The application will start automatically when you log in.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"
Name: "quicklaunchicon"; Description: "Create a &quick launch icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Main application executable
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\ScannerService.TrayApp.exe"; DestDir: "{app}"; Flags: ignoreversion

; Required DLL files (excluding PDB debug symbols)
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\*.dll"; DestDir: "{app}"; Flags: ignoreversion; Excludes: "*.pdb"

; Configuration file
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion

; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Dirs]
; Create directories for user data
Name: "{app}\logs"

[Icons]
Name: "{group}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; IconFilename: "{app}\ScannerService.TrayApp.exe"
Name: "{group}\Uninstall Scanner Service"; Filename: "{uninstallexe}"
Name: "{commonstartup}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; IconFilename: "{app}\ScannerService.TrayApp.exe"
Name: "{autodesktop}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; Tasks: desktopicon; IconFilename: "{app}\ScannerService.TrayApp.exe"
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; Tasks: quicklaunchicon; IconFilename: "{app}\ScannerService.TrayApp.exe"

[Run]
; Run the application after installation
Filename: "{app}\ScannerService.TrayApp.exe"; Description: "Launch Scanner Service"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Remove logs on uninstall (optional - comment out to preserve)
Type: filesandordirs; Name: "{app}\logs"
Type: files; Name: "{app}\scanner.db"
; NOTE: appsettings.json is preserved by default

[Registry]
; Add to Windows Startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ResaaScannerService"; ValueData: """{app}\ScannerService.TrayApp.exe"""; Flags: uninsdeletevalue
