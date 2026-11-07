; Restage installer (Inno Setup 6)
; Per-user setup: no admin prompt, installs to %LOCALAPPDATA%\Programs\Restage —
; the same pattern as VS Code's user setup, matching the app's HKCU-only
; footprint (settings, Run entry, restage: URI). Build with:  ISCC.exe Restage.iss
; Expects the published exes at ..\publish\Restage.exe and ..\publish\restage.exe
; (see README "Building").

#define MyAppName "Restage"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Restage"
#define MyAppURL "https://github.com/roosmsg/Restage-Windows"
#define MyAppExeName "Restage.exe"
#define MyCliExeName "restage.exe"

[Setup]
; Never change AppId between versions — it is how upgrades find the install.
; (Distinct from Rewake's, so both can be installed side by side.)
AppId={{A7C3E1F2-5D64-4B8E-9F10-3E2B7C9D1A55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; The app holds this mutex while running (tray included): setup asks the user
; to exit Restage rather than clobbering a running copy.
AppMutex=Local\Restage.SingleInstance
OutputDir=Output
OutputBaseFilename=Restage-Setup-v{#MyAppVersion}
SetupIconFile=..\src\Restage.App\Assets\Restage.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} when I sign in (runs in the tray)"
Name: "addpath"; Description: "Add the restage command to my PATH"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\{#MyCliExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Same key/value the app's own "Run at startup" toggle manages.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "Restage"; \
  ValueData: """{app}\{#MyAppExeName}"" --minimized"; \
  Flags: uninsdeletevalue; Tasks: startup
; The restage: URI scheme the app registers on every start; written here too so
; it exists before the first launch and is removed on uninstall.
Root: HKCU; Subkey: "Software\Classes\restage"; ValueType: string; ValueName: ""; ValueData: "URL:Restage workspace"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\restage"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\restage\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
; User PATH entry for the command line (added only when not already present).
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
  ValueData: "{olddata};{app}"; Tasks: addpath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  // Look for the path with leading and trailing semicolons, case-insensitive.
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

// Uninstall: the Run entry may exist even without the startup task (the app's
// own toggle writes it), and the user's workspaces live outside {app} — clean
// the first silently, ask about the second.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'Restage');
    DataDir := ExpandConstant('{localappdata}\Restage');
    if DirExists(DataDir) then
      if MsgBox('Also delete your workspaces and settings?' + #13#10 +
                DataDir, mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
