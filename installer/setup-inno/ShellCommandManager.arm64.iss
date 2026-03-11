; Inno Setup script for ShellCommandManager

#define MyAppName "Shell Command Manager"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "F7s"
#define MyAppExeName "ShellCommandManager.exe"
#define MyAppId "{{6D1E1B20-9D89-4F37-8E4E-2D2AE3E4153F}"
#define BuildRoot "..\\..\\App1\\bin\\Release\\net8.0-windows10.0.19041.0\\win-arm64\\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ShellCommandManager
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\out
OutputBaseFilename=ShellCommandManager-setup-arm64
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

