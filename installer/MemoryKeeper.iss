[Setup]
AppId={{7E7F88A1-2F6B-4A85-9D5D-5E4D9029D3C1}
AppName=MemoryKeeper
AppVersion=2.0.1
AppPublisher=MemoryKeeper
DefaultDirName={autopf}\MemoryKeeper
DefaultGroupName=MemoryKeeper
OutputDir=..\artifacts\installer
OutputBaseFilename=MemoryKeeper-v2.0.1-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName=MemoryKeeper
SetupIconFile=..\MemoryKeeper.App\Assets\MemoryKeeper.ico
UninstallDisplayIcon={app}\MemoryKeeper.exe

[Files]
Source: "..\artifacts\MemoryKeeper-v2.0.1-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MemoryKeeper"; Filename: "{app}\MemoryKeeper.exe"
Name: "{autodesktop}\MemoryKeeper"; Filename: "{app}\MemoryKeeper.exe"

[Run]
Filename: "{app}\MemoryKeeper.exe"; Description: "MemoryKeeper 실행"; Flags: nowait postinstall skipifsilent
