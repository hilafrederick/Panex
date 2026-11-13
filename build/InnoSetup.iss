; -- InnoSetup.iss --
; Inno Setup release installer script for Panex

#ifndef AppName
  #define AppName "Panex"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef AppPublisher
  #define AppPublisher "Panex Development"
#endif

#ifndef AppExeName
  #define AppExeName "Panex.exe"
#endif

; Target the published staging directory
#ifndef SourcePath
  #define SourcePath "..\dist\staging"
#endif

; Output folder for final deliverables
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

; Output executable base filename (without .exe)
#ifndef OutputBaseName
  #define OutputBaseName "Panex-v" + AppVersion + "-win-x64-Setup"
#endif

#define AppId "{{91F873F5-87BD-4DD0-B1A1-9929378905B8}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Panex\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}

[Tasks]
; Checkbox for creating desktop shortcut (checked by default)
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Primary application executable
Source: "{#SourcePath}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Supporting binaries, libraries, and data folders
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "{#AppExeName}"

[Icons]
; Start Menu shortcut
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

; Desktop shortcut
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Option to launch the application immediately after installation
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
