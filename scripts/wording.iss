#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef IconFile
  #define IconFile SourceDir + "\..\..\src\Wording.Desktop\Assets\Wording.ico"
#endif

[Setup]
AppId={{FC92DC0A-522D-41CD-88A7-267A81C00AC7}
AppName=Wording
AppVersion={#AppVersion}
AppPublisher=Wording
AppPublisherURL=https://github.com/ming0071/Wording
AppSupportURL=https://github.com/ming0071/Wording/issues
AppUpdatesURL=https://github.com/ming0071/Wording/releases
DefaultDirName={localappdata}\Programs\Wording
DefaultGroupName=Wording
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=Wording-{#AppVersion}-win-x64-Setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\Wording.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=yes
AppMutex=Local\Wording.Installation
SetupMutex=Local\Wording.Setup
CloseApplications=no
RestartApplications=no
AllowNoIcons=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "traditionalchinese"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Wording"; Filename: "{app}\Wording.exe"; AppUserModelID: "Ming.Wording"
Name: "{userdesktop}\Wording"; Filename: "{app}\Wording.exe"; Tasks: desktopicon; AppUserModelID: "Ming.Wording"

[Run]
Filename: "{app}\Wording.exe"; Description: "開啟 Wording"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: not IsUpdate
Filename: "{app}\Wording.exe"; Parameters: "{code:GetRestartArguments}"; Flags: nowait runasoriginaluser; Check: ShouldRestart

[Code]
function OpenProcess(Access: LongWord; Inherit: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:WORDINGUPDATE|0}') = '1';
end;

function GetRestartArguments(Param: String): String;
var DataDirectory, Trailing: String;
begin
  DataDirectory := ExpandConstant('{param:WORDINGDATADIR|}');
  Result := '';
  if DataDirectory <> '' then begin
    Trailing := '';
    while (Length(DataDirectory) > 0) and (DataDirectory[Length(DataDirectory)] = '\') do begin
      Trailing := Trailing + '\';
      SetLength(DataDirectory, Length(DataDirectory) - 1);
    end;
    Result := '--data-dir "' + DataDirectory + Trailing + Trailing + '"';
  end;
end;

function ShouldRestart: Boolean;
begin
  Result := IsUpdate and (ExpandConstant('{param:WORDINGRELAUNCH|1}') = '1');
end;

function InitializeSetup: Boolean;
var ParentId: Integer; ParentHandle: THandle;
begin
  Result := True;
  ParentId := StrToIntDef(ExpandConstant('{param:WORDINGPARENTPID|0}'), 0);
  if IsUpdate and (ParentId > 0) then begin
    ParentHandle := OpenProcess($00100000, False, ParentId);
    if ParentHandle <> 0 then begin
      Result := WaitForSingleObject(ParentHandle, 30000) = 0;
      CloseHandle(ParentHandle);
      if not Result then MsgBox('Wording 尚未關閉，原版本未被更新。請稍後重試。', mbError, MB_OK);
    end;
  end;
end;
