; Inno Setup script for the single-file Cloud-SSD installer (per-user; asks for administrator rights only to install WinFsp).
; Built by build-release.ps1:
#ifndef AppVersion
  #define AppVersion "0.7.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\Cloud-SSD-" + AppVersion + "-win-x64"
#endif
#ifndef WinFspMsi
  #define WinFspMsi "..\tools\winfsp\winfsp-2.1.25156.msi"
#endif

[Setup]
AppId={{6A0C9C2E-6E7B-4E5E-9D2B-4C1F0A7E1D01}
AppName=Cloud-SSD
AppVersion={#AppVersion}
AppVerName=Cloud-SSD {#AppVersion}
AppPublisher=Cloud-SSD
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Cloud-SSD
VersionInfoDescription=Cloud-SSD Setup
DefaultDirName={localappdata}\Programs\Cloud-SSD
DefaultGroupName=Cloud-SSD
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=Cloud-SSD-{#AppVersion}-setup
SetupIconFile=..\src\CloudPlayX.Desktop\Assets\cloudplayx.ico
UninstallDisplayIcon={app}\Cloud-SSD.exe
UninstallDisplayName=Cloud-SSD
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The desktop app holds this mutex while running; Setup and the uninstaller ask the user to close it first.
AppMutex=CloudSSD.Desktop.SingleInstance
; Never let Windows force-close rclone: a mounted drive may still be uploading. PrepareToInstall checks instead.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "Install.cmd,Install-CloudPlayX.ps1,Uninstall-CloudPlayX.ps1,Install-CloudSSD.ps1,Uninstall-CloudSSD.ps1"
; Official, signed WinFsp installer (Navimatics LLC, GPLv3 with FLOSS exception). Only extracted when WinFsp is missing.
Source: "{#WinFspMsi}"; DestName: "winfsp.msi"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Cloud-SSD"; Filename: "{app}\Cloud-SSD.exe"; Comment: "Cloud-to-SSD virtual drive and storage engine"
Name: "{autodesktop}\Cloud-SSD"; Filename: "{app}\Cloud-SSD.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Cloud-SSD.exe"; Description: "Start Cloud-SSD"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings, saved logins and caches are kept on purpose
Type: files; Name: "{app}\*.log"

[Code]
function WinFspInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf32}\WinFsp\bin\winfsp-x64.dll')) or
            FileExists(ExpandConstant('{commonpf64}\WinFsp\bin\winfsp-x64.dll'));
end;

{ Name of a Cloud-SSD process (app, cpx CLI or a mounted drive's rclone) running from Dir, or '' if none. }
function RunningFrom(const Dir: String): String;
var
  Locator, Service, Procs, P: Variant;
  I: Integer;
  Path, Prefix: String;
begin
  Result := '';
  Prefix := Lowercase(AddBackslash(Dir));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process WHERE Name = ''rclone.exe'' OR Name = ''cpx.exe'' OR Name = ''CloudPlayX.exe'' OR Name = ''Cloud-SSD.exe''');
    for I := 0 to Procs.Count - 1 do
    begin
      P := Procs.ItemIndex(I);
      if VarIsNull(P.ExecutablePath) then Path := '' else Path := P.ExecutablePath;
      Path := Lowercase(Path);
      if (Path <> '') and (Pos(Prefix, Path) = 1) then
      begin
        Result := P.Name;
        Exit;
      end;
    end;
  except
    { WMI unavailable: fall back to the AppMutex check only. }
  end;
end;

function BusyMessage(const Name: String): String;
begin
  if Lowercase(Name) = 'rclone.exe' then
    Result := 'A Cloud-SSD cloud drive is still mounted.' + #13#10#13#10 +
              'Open Cloud-SSD, close its window and choose "No" so the drives are unmounted safely ' +
              '(pending uploads finish first). Then try again.'
  else
    Result := 'Cloud-SSD (' + Name + ') is running. Close it and try again.';
end;

procedure StopProcesses(const Dir: String);
var
  Locator, Service, Procs, P: Variant;
  I: Integer;
  Path, Prefix: String;
begin
  Prefix := Lowercase(AddBackslash(Dir));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''rclone.exe'' OR Name = ''cpx.exe'' OR Name = ''CloudPlayX.exe'' OR Name = ''Cloud-SSD.exe''');
    for I := 0 to Procs.Count - 1 do
    begin
      P := Procs.ItemIndex(I);
      if VarIsNull(P.ExecutablePath) then Path := '' else Path := P.ExecutablePath;
      Path := Lowercase(Path);
        if (Path <> '') and (Pos(Prefix, Path) = 1) then
        begin
          try
            P.Terminate();
          except
          end;
        end;
    end;
  except
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Busy: String;
  Answer: Integer;
begin
  Result := '';
  Busy := RunningFrom(ExpandConstant('{app}'));
  if Busy = '' then Busy := RunningFrom(ExpandConstant('{localappdata}\Programs\CloudPlayX'));
  if Busy <> '' then
  begin
    Answer := MsgBox('A Cloud-SSD process (' + Busy + ') or virtual drive is currently active.' + #13#10#13#10 +
                     'Would you like Setup to automatically close it and continue the installation?',
                     mbConfirmation, MB_YESNO);
    if Answer = IDYES then
    begin
      StopProcesses(ExpandConstant('{app}'));
      StopProcesses(ExpandConstant('{localappdata}\Programs\CloudPlayX'));
      Sleep(1500);
      Busy := RunningFrom(ExpandConstant('{app}'));
      if Busy = '' then Busy := RunningFrom(ExpandConstant('{localappdata}\Programs\CloudPlayX'));
      if Busy <> '' then Result := BusyMessage(Busy);
    end
    else
      Result := BusyMessage(Busy);
  end;
end;

procedure InstallWinFsp();
var
  Code: Integer;
  Msi: String;
begin
  WizardForm.StatusLabel.Caption := 'Installing WinFsp (needed for the virtual drive) - please approve the Windows prompt...';
  ExtractTemporaryFile('winfsp.msi');
  Msi := ExpandConstant('{tmp}\winfsp.msi');
  if not ShellExec('runas', 'msiexec.exe', '/i "' + Msi + '" /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
    Code := -1;
  if (Code <> 0) and (Code <> 3010) and not WinFspInstalled() then
    MsgBox('WinFsp was not installed (it needs administrator approval).' + #13#10#13#10 +
           'Cloud-SSD is installed, but cloud drives cannot be mounted until WinFsp is installed. ' +
           'Run this setup again, or install WinFsp from https://winfsp.dev/rel/', mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WinFspInstalled() then
    InstallWinFsp();
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then Result := Result + MemoTasksInfo + NewLine + NewLine;
  if WinFspInstalled() then
    Result := Result + 'WinFsp (virtual drive driver): already installed'
  else
    Result := Result + 'WinFsp (virtual drive driver): will be installed - Windows asks for permission once';
end;

function InitializeUninstall(): Boolean;
var
  Busy: String;
begin
  Result := True;
  Busy := RunningFrom(ExpandConstant('{app}'));
  if Busy <> '' then
  begin
    MsgBox(BusyMessage(Busy), mbInformation, MB_OK);
    Result := False;
  end;
end;
