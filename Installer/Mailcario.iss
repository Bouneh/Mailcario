#ifndef AppVersion
  #error AppVersion must be provided by build-installer.ps1
#endif
#ifndef StageDir
  #error StageDir must be provided by build-installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by build-installer.ps1
#endif

[Setup]
AppId={{2EE0D87F-A8F9-4DD0-B7DE-6EAB3B94FC17}
AppName=Mailcario
AppVersion={#AppVersion}
AppPublisher=Mailcario Contributors
DefaultDirName={localappdata}\Programs\Mailcario
DefaultGroupName=Mailcario
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=Mailcario-Setup-{#AppVersion}
SetupIconFile=..\Assets\Mailcario.ico
UninstallDisplayIcon={app}\Mailcario.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
ArchitecturesAllowed=x64compatible

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "deps\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{group}\Mailcario"; Filename: "{app}\Mailcario.exe"
Name: "{autodesktop}\Mailcario"; Filename: "{app}\Mailcario.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; Flags: runhidden; Check: NeedsWebView2
Filename: "{app}\Mailcario.exe"; Description: "{cm:LaunchProgram,Mailcario}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsWebView2: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := True;
  if RegQueryStringValue(HKCU, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False;
  if RegQueryStringValue(HKLM32, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False;
end;
