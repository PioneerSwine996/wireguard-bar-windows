; Inno Setup 6 script for WireGuardBar-Setup.exe. Built by scripts\package.ps1:
;   ISCC.exe /DAppVersion=0.1.0 installer\WireGuardBar.iss
; One UAC prompt installs the app and the wgbar-helper service; offers to fetch WireGuard for Windows.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6F1E1C3A-3B7D-4E43-9C55-2B6C1B0F7A21}
AppName=WireGuard Bar
AppVersion={#AppVersion}
AppPublisher=PioneerSwine996
AppPublisherURL=https://gitlab.com/PioneerSwine996/wireguard-bar-windows
DefaultDirName={autopf}\WireGuardBar
; the helper keeps configs in Data\ next to itself, so the folder stays fixed
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=WireGuardBar-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\WireGuardBar.exe
UninstallDisplayName=WireGuard Bar
CloseApplications=yes

[Files]
Source: "..\dist\app\WireGuardBar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\app\wgbar-helper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\WireGuard Bar"; Filename: "{app}\WireGuardBar.exe"

[Registry]
; start at sign-in with just the tray icon
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WireGuard Bar"; \
  ValueData: """{app}\WireGuardBar.exe"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\wgbar-helper.exe"; Parameters: "install"; StatusMsg: "Installing the helper service..."; Flags: runhidden waituntilterminated
Filename: "{app}\WireGuardBar.exe"; Description: "Open WireGuard Bar"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM WireGuardBar.exe /F"; Flags: runhidden; RunOnceId: "CloseApp"
Filename: "{app}\wgbar-helper.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveHelper"

[Code]
const
  WireGuardInstaller = 'https://download.wireguard.com/windows-client/wireguard-installer.exe';

var
  DownloadPage: TDownloadWizardPage;

function WireGuardInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf64}\WireGuard\wireguard.exe'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 'Downloading WireGuard for Windows...', nil);
end;

{ WireGuard Bar runs tunnels through WireGuard for Windows. Offer its official installer when it's missing. }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or WireGuardInstalled or WizardSilent then Exit;
  if MsgBox('WireGuard Bar needs WireGuard for Windows, which isn''t installed. Download and install it now from wireguard.com?',
            mbConfirmation, MB_YESNO) <> IDYES then Exit;
  DownloadPage.Clear;
  DownloadPage.Add(WireGuardInstaller, 'wireguard-installer.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Exec(ExpandConstant('{tmp}\wireguard-installer.exe'), '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
    except
      MsgBox('Couldn''t download WireGuard for Windows: ' + GetExceptionMessage + #13#10 +
             'Install it later from https://www.wireguard.com/install/', mbError, MB_OK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

{ Upgrades: stop the running helper so its exe can be replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\net.exe'), 'stop wgbar-helper', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

{ Configs (with private keys) are kept on uninstall unless the user asks to delete them. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DirExists(ExpandConstant('{app}\Data')) then
    if MsgBox('Also delete your tunnel configurations (including their private keys)?',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{app}'), True, True, True);
end;
