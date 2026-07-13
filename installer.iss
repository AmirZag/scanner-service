[Setup]
AppName=Resaa Scanner Service
AppVersion=1.0
AppVerName=Resaa Scanner Service 1.0
AppPublisher=Resaa Softwares
AppPublisherURL=https://github.com/Amirzag/scanner-service
AppSupportURL=https://github.com/Amirzag/scanner-service/issues
AppUpdatesURL=https://github.com/Amirzag/scanner-service/releases
AppId={{A1B2C3D4-E5F6-4A5B-8C7D-9E0F1A2B3C4D}
VersionInfoVersion=1.0.0.0

; Prevent multiple instances of the installer from running
AppMutex=ResaaScannerInstallerMutex

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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64

; Close running instances before installation
CloseApplications=yes
CloseApplicationsFilter=ScannerService.TrayApp.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Name: "persian"; MessagesFile: "compiler:Languages\Persian.isl"  ; Persian.isl not included in standard Inno Setup

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"
Name: "quicklaunchicon"; Description: "Create a &quick launch icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Main application executable
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\ScannerService.TrayApp.exe"; DestDir: "{app}"; Flags: ignoreversion

; Required DLL files (excluding PDB debug symbols)
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\*.dll"; DestDir: "{app}"; Flags: ignoreversion; Excludes: "*.pdb"

; NAPS2 Worker executable (required for TWAIN scanning)
Source: "src\ScannerService.TrayApp\bin\Release\net8.0-windows\publish\win-x64\NAPS2.Worker.exe"; DestDir: "{app}"; Flags: ignoreversion

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

[Code]
function IsUpgrade(): Boolean;
begin
  Result := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-4A5B-8C7D-9E0F1A2B3C4D}_is1');
end;

function InitializeSetup(): Boolean;
var
  PreviousVersion: String;
begin
  Result := True;

  if IsUpgrade() then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-4A5B-8C7D-9E0F1A2B3C4D}_is1', 'DisplayVersion', PreviousVersion) then
    begin
      MsgBox('Resaa Scanner Service version ' + PreviousVersion + ' is already installed.' + #13#10 +
             'You can choose to Repair, Modify, or Uninstall the application.',
             mbInformation, MB_OK);
    end;
  end;
end;
