; Restage installer (Inno Setup 6)
; Per-user setup: no admin prompt, installs to %LOCALAPPDATA%\Programs\Restage —
; same pattern as VS Code's user setup, and it matches the app's HKCU-only
; footprint (settings, Run entry). Build with:  ISCC.exe Restage.iss
; Expects the published exe at ..\publish\Restage.exe (see README "Build & publish").

#define MyAppName "Restage"
#define MyAppVersion "1.0.0-beta.2"
#define MyAppPublisher "Restage"
#define MyAppURL "https://github.com/Anurag-patil-26/Restage-Windows"
#define MyAppExeName "Restage.exe"

[Setup]
; Never change AppId between versions — it is how upgrades find the install.
AppId={{7B7E4E86-2D8A-4B62-9C1E-52A6E1B0FE12}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
; Per-user: {autopf} resolves to {localappdata}\Programs under lowest privileges.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; The app holds this mutex while running (tray included): setup asks the user
; to exit Restage rather than clobbering a running copy.
AppMutex=Local\Restage.SingleInstance
OutputDir=Output
OutputBaseFilename=Restage-Setup-v{#MyAppVersion}
SetupIconFile=..\Assets\Restage.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The exe is win-x64 self-contained.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Checked by default: an app that rescues you after a power cut only helps if
; it is already running when the power comes back.
Name: "startup"; Description: "Start {#MyAppName} when I sign in (runs minimized in the tray)"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Same key/value the app's own "Run at startup" toggle manages, so the two
; stay interchangeable. uninsdeletevalue removes it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "Restage"; \
  ValueData: """{app}\{#MyAppExeName}"" --minimized"; \
  Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: nowait postinstall skipifsilent

[Code]
// Uninstall: the Run entry may exist even without the startup task (the app's
// own toggle writes it), and the user's captures live outside {app} — clean
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
      if MsgBox('Also delete your captures and settings?' + #13#10 +
                DataDir, mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
