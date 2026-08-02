[Setup]
AppName=Resaa Scanner Service
AppVersion=1.0
AppVerName=Resaa Scanner Service 1.0
AppPublisher=Resaa Softwares
AppPublisherURL=https://github.com/Amirzag/scanner-service
AppSupportURL=https://github.com/Amirzag/scanner-service/issues
AppUpdatesURL=https://github.com/Amirzag/scanner-service/releases
AppId={{8F3A2E1B-6C9D-4A2E-8B7D-1C3A5E7F9D2B}
VersionInfoVersion=1.0.0.0

; Prevent multiple instances of the installer from running
AppMutex=Global\ResaaScannerInstallerMutex

; Upgrade settings - use same directory as previous installation
UsePreviousAppDir=yes
UsePreviousGroup=yes
AlwaysRestart=no

; Disable certain pages during repair/modify
DisableDirPage=no
DisableProgramGroupPage=no
DisableFinishedPage=no
DisableWelcomePage=no

; Show installation mode (repair/modify/remove) when upgrading
Uninstallable=yes
CreateUninstallRegKey=yes
UpdateUninstallLogAppName=yes

DefaultDirName={localappdata}\ResaaScanner
DefaultGroupName=Resaa Softwares
OutputDir=InstallerOutput
OutputBaseFilename=ResaaScannerSetup
Compression=lzma2
SolidCompression=yes
SetupIconFile=src\ScannerService.TrayApp\Properties\app.ico
UninstallDisplayIcon={app}\ScannerService.TrayApp.exe

; Install to user profile (no admin required)
PrivilegesRequired=lowest

; Allow uninstall to run with elevated privileges if needed (fixes uninstaller issues)
PrivilegesRequiredOverridesAllowed=commandline

; Ensure app is closed before install/uninstall
CloseApplications=yes
CloseApplicationsFilter=ScannerService.TrayApp.exe

; Specify this is an x64 installer
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Name: "persian"; MessagesFile: "compiler:Languages\Persian.isl"  ; Persian.isl not included in standard Inno Setup

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"
; Quick Launch task removed - Quick Launch is obsolete since Windows 7
; Name: "quicklaunchicon"; Description: "Create a &quick launch icon"; GroupDescription: "Additional icons:"; Flags: unchecked

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
Name: "{userstartup}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; IconFilename: "{app}\ScannerService.TrayApp.exe"
Name: "{autodesktop}\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; Tasks: desktopicon; IconFilename: "{app}\ScannerService.TrayApp.exe"
; Quick Launch is obsolete since Windows 7
; Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\Scanner Service"; Filename: "{app}\ScannerService.TrayApp.exe"; Tasks: quicklaunchicon; IconFilename: "{app}\ScannerService.TrayApp.exe"

[Run]
; Run the application after installation
Filename: "{app}\ScannerService.TrayApp.exe"; Description: "Launch Scanner Service"; Flags: nowait postinstall skipifsilent shellexec

; Note: User data deletion is handled in CurUninstallStepChanged() with user prompt
; No [UninstallDelete] section - users choose during uninstall whether to keep or delete data

[Code]
const
  AppName = 'ScannerService.TrayApp';
  // This AppId must match the one in [Setup] section
  AppId = '{{8F3A2E1B-6C9D-4A2E-8B7D-1C3A5E7F9D2B}';

// Helper function to remove file extension from a path
function RemoveFileExt(const FileName: String): String;
var
  i, LastDot: Integer;
begin
  Result := FileName;
  LastDot := 0;
  // Find the last dot in the filename
  for i := Length(FileName) downto 1 do
  begin
    if FileName[i] = '.' then
    begin
      LastDot := i;
      Break;
    end;
  end;
  if LastDot > 0 then
    Result := Copy(FileName, 1, LastDot - 1);
end;

// Helper function to check if the app is running
// Uses FindWindow to detect if the tray app window exists
function IsAppRunning(): Boolean;
begin
  Result := False;
  // Try to find the window by class name (Windows Forms)
  // The class name might vary, so we try multiple approaches
  if FindWindowByClassName('WindowsForms10.Window.8.app.0.33c0d9d_r6_ad1') > 0 then
    Result := True
  else if FindWindowByWindowName('Resaa Scanner Service') > 0 then
    Result := True
  else if FindWindowByClassName('ScannerService.TrayApp') > 0 then
    Result := True;
end;

// Kill the app if running
function KillApp(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('taskkill.exe', '/F /IM ' + AppName + '.exe', '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode);
end;

// Check if the app is already installed
function IsUpgrade(): Boolean;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppId + '_is1';
  Result := RegKeyExists(HKCU, UninstallKey);
end;

// Get the uninstaller executable path
function GetUninstallString(): String;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppId + '_is1';
  if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', Result) then
  begin
    // Remove quotes and /silent= parameters if present
    Result := RemoveQuotes(Result);
    Result := RemoveFileExt(Result);
    Result := Result + '.exe';
  end;
end;

// Get the installed version
function GetInstalledVersion(): String;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppId + '_is1';
  RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Result);
end;

// Get the installation directory
function GetInstallDir(): String;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppId + '_is1';
  RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', Result);
end;

// Launch uninstaller
function LaunchUninstall(): Boolean;
var
  UninstallExe: String;
  ResultCode: Integer;
begin
  Result := False;
  UninstallExe := GetUninstallString();
  if UninstallExe <> '' then
  begin
    Result := Exec(UninstallExe, '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;

// Show custom upgrade/modify/repair/uninstall dialog
function ShowUpgradeDialog(): Integer;
var
  InstalledVersion, InstallPath: String;
begin
  Result := 0;  // Default: new install

  InstalledVersion := GetInstalledVersion();
  InstallPath := GetInstallDir();

  // Create and show custom dialog
  if MsgBox('Resaa Scanner Service is already installed.' + #13#10#13#10 +
             'Installed Version: ' + InstalledVersion + #13#10 +
             'Installation Path: ' + InstallPath + #13#10#13#10 +
             'Click OK to upgrade/repair, or Cancel to exit.' + #13#10#13#10 +
             'To uninstall, please use "Programs and Features" or the Uninstall shortcut.',
             mbConfirmation, MB_OKCANCEL) = IDOK then
  begin
    // User chose to proceed (upgrade/repair)
    Result := 1;  // Modify/Repair mode
  end
  else
  begin
    // User cancelled - ask if they want to uninstall
    if MsgBox('Do you want to uninstall Resaa Scanner Service?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      if LaunchUninstall() then
      begin
        Result := 3;  // Uninstall was initiated
        // Exit this installer since uninstaller is now running
        Result := -1;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  // Check if app is already installed
  if IsUpgrade() then
  begin
    // Close app if running before install/upgrade
    if IsAppRunning() then
    begin
      if MsgBox('Resaa Scanner Service is currently running. Close it and continue?',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        if not KillApp() then
        begin
          MsgBox('Failed to close Scanner Service. Please close it manually and try again.',
                 mbError, MB_OK);
          Result := False;
          Exit;
        end;
        Sleep(1500); // Give it time to close
      end
      else
      begin
        Result := False;
        Exit;
      end;
    end;

    // Show upgrade dialog
    if ShowUpgradeDialog() = -1 then
    begin
      // User chose to uninstall, exit this installer
      Result := False;
      Exit;
    end;

    // Set wizard images to indicate upgrade/repair
    // Use existing installation directory
  end
  else
  begin
    // New installation - check if app is running from another location
    if IsAppRunning() then
    begin
      MsgBox('Scanner Service appears to be running from another location.' + #13#10 +
             'Please close it before continuing with installation.',
             mbInformation, MB_OK);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;

  // Close app if running before uninstall
  if IsAppRunning() then
  begin
    if MsgBox('Scanner Service is currently running.' + #13#10#13#10 +
              'Close it and continue with uninstall?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      if KillApp() then
      begin
        Sleep(1500); // Give it time to close
      end
      else
      begin
        MsgBox('Failed to close Scanner Service. Please close it manually and try again.',
               mbError, MB_OK);
        Result := False;
      end;
    end
    else
      Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  case CurUninstallStep of
    usUninstall:
    begin
      // Before uninstall - can do additional checks
    end;
    usDone:
    begin
      // After uninstall - offer to remove user data
      if MsgBox('Do you want to remove all user data (scans, database, logs)?' + #13#10#13#10 +
                'Click Yes to remove everything, or No to keep your data.',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        // Remove entire app directory including user data
        DelTree(ExpandConstant('{app}'), True, True, True);
      end
      else
      begin
        // Remove only log files (keep database and config)
        DelTree(ExpandConstant('{app}\logs'), True, True, True);
      end;
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  // Before copying files, ensure app is not running
  if (CurPageID = wpReady) and IsUpgrade() then
  begin
    if IsAppRunning() then
    begin
      MsgBox('Scanner Service is still running. Please close it first.',
             mbError, MB_OK);
      Result := False;
    end;
  end;
end;
