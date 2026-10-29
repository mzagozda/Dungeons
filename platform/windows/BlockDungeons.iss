; Build-WindowsArtifacts.ps1 supplies a clean release payload and validated metadata.
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef ArtifactStem
  #error ArtifactStem is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppPublisher
  #error AppPublisher is required
#endif
#ifndef AppSupportUrl
  #error AppSupportUrl is required
#endif
#ifndef AppCopyright
  #error AppCopyright is required
#endif

[Setup]
AppId={{8F2F3411-E009-4A65-AE08-195AB82B3FA6}
AppName=Block Dungeons
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppSupportUrl}
AppSupportURL={#AppSupportUrl}
AppCopyright={#AppCopyright}
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\Taskscape\Block Dungeons
DefaultGroupName=Block Dungeons
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename={#ArtifactStem}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\BlockDungeons.exe
CloseApplications=yes
RestartApplications=no
#ifdef SignedBuild
SignTool=TaskscapeRelease
SignedUninstaller=yes
SignedUninstallerDir={#UninstallerDir}
#endif

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Icons]
Name: "{group}\Block Dungeons"; Filename: "{app}\BlockDungeons.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Block Dungeons"; Filename: "{app}\BlockDungeons.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\BlockDungeons.exe"; Description: "Launch Block Dungeons"; Flags: nowait postinstall skipifsilent
