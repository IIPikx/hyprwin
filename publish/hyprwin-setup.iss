; ╔══════════════════════════════════════════════════════════════╗
; ║              HyprWin — Inno Setup Installer Script          ║
; ║    Builds a standard Windows installer (.exe) suitable      ║
; ║    for winget, GitHub Releases, or manual distribution.     ║
; ╚══════════════════════════════════════════════════════════════╝
;
; Prerequisites:
;   1. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
;   2. Publish: dotnet publish src\HyprWin.App\HyprWin.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\bin
;   3. Compile this script with ISCC.exe or the Inno Setup GUI.
;
; Output: installer\HyprWin-Setup-{version}.exe

#define MyAppName "HyprWin"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HyprWin"
#define MyAppURL "https://github.com/hyprwin/hyprwin"
#define MyAppExeName "HyprWin.App.exe"
#define MyAppDescription "Hyprland-inspired tiling window manager for Windows 11"

[Setup]
AppId={{B8A5E7F2-3C4D-4E6A-9F1B-2D8C7E0A5B3F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Require admin for installation (matches app manifest requireAdministrator)
PrivilegesRequired=admin
OutputDir=..\installer
OutputBaseFilename=HyprWin-Setup-{#MyAppVersion}
SetupIconFile=..\src\HyprWin.App\hyprwin.ico
Compression=lzma2/ultra64
SolidCompression=yes
; Modern installer UI
WizardStyle=modern
; Minimum Windows 10 version 21H2 (build 22000)
MinVersion=10.0.22000
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppDescription}
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon";  Description: "{cm:CreateDesktopIcon}";  GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart";    Description: "Start {#MyAppName} automatically with Windows"; GroupDescription: "System Integration:"; Flags: checked

[Files]
; Single-file publish output — just the one .exe (and .pdb if present)
Source: "..\publish\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\bin\*.pdb";           DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Default config template
Source: "..\publish\hyprwin.toml";        DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}";               Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";          Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Autostart: write HKCU\...\Run entry when user selects the task
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Kill HyprWin before uninstalling so files aren't locked
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillHyprWin"

[UninstallDelete]
; Clean up log files
Type: filesandordirs; Name: "{userappdata}\HyprWin\logs"

[Code]
// Kill any running HyprWin instance before install/upgrade
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
