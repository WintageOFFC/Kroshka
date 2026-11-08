#define AppName "Крошка"
#ifndef AppVersion
#error AppVersion is required
#endif
[Setup]
AppId=WintageOFFC.Kroshka
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WintageOFFC
DefaultDirName={localappdata}\Programs\Kroshka
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=Kroshka-Setup-{#AppVersion}
SetupIconFile=assets\kroshka.ico
Compression=lzma2/fast
WizardStyle=modern
UninstallDisplayIcon={app}\Kroshka.exe
[Files]
Source: "release-app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "assets\kroshka.ico"; DestDir: "{app}"; DestName: "kroshka-1.1.ico"
[Icons]
Name: "{autoprograms}\Крошка — история диска"; Filename: "{app}\Kroshka.exe"
[Run]
Filename: "{app}\Kroshka.exe"; Description: "Открыть Крошку"; Flags: postinstall nowait skipifsilent
