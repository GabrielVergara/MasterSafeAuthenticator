; MasterSafe Authenticator - Inno Setup script
; Build with installer\build-windows.ps1, which publishes the app and passes the defines below.

#ifndef AppVersion
  #define AppVersion "1.2.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#define AppName "MasterSafe Authenticator"
#define AppExe "MasterSafe.App.exe"
#define AppUrl "https://github.com/GabrielVergara/MasterSafeAuthenticator"

[Setup]
AppId={{5B0F6F0E-3A57-4C69-9D54-7C1C7E5A2D41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Gabriel Vergara Ezcurdia
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\src\MasterSafe.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#OutputDir}
OutputBaseFilename=MasterSafe-Authenticator-{#AppVersion}-win-x64-setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installs for the current user by default (no administrator rights needed); the user can choose all users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The vault (%LocalAppData%\MasterSafe) is the user's data and is never removed by the uninstaller.
[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
    MsgBox('Your vault is not removed. It stays in %LocalAppData%\MasterSafe so you can reinstall later.' + #13#10 +
           'Delete that folder yourself if you want to remove all of your data.', mbInformation, MB_OK);
end;
