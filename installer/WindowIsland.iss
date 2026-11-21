; Window Island installer (Inno Setup 6)
; Per-user install: no admin rights / UAC prompt needed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{6F2C1A7E-9B3D-4C55-8E21-5D7A0B3C9E11}
AppName=Window Island
AppVersion={#AppVersion}
AppVerName=Window Island {#AppVersion}
AppPublisher=Window Island
AppPublisherURL=https://github.com/akmalhakim-96/WINDOW-ISLAND
AppSupportURL=https://github.com/akmalhakim-96/WINDOW-ISLAND/issues
DefaultDirName={localappdata}\Programs\Window Island
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName=Window Island
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=WindowIsland-Setup-{#AppVersion}
SetupIconFile=..\app\WindowIsland\icon.ico
UninstallDisplayIcon={app}\WindowIsland.exe
UninstallDisplayName=Window Island
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\WindowIsland.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Window Island"; Filename: "{app}\WindowIsland.exe"
Name: "{autodesktop}\Window Island"; Filename: "{app}\WindowIsland.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WindowIsland.exe"; Description: "Launch Window Island"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\WindowIsland.exe"; Parameters: "--uninstall"; Flags: waituntilterminated skipifdoesntexist; RunOnceId: "QuitApp"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Close a running copy gracefully before upgrading (restores the taskbar properly).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  rc: Integer;
  exe: String;
begin
  exe := ExpandConstant('{app}\WindowIsland.exe');
  if FileExists(exe) then
  begin
    Exec(exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Sleep(700);
  end;
  Result := '';
end;
