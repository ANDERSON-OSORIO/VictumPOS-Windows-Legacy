#define MyAppName "VictumPOS"
#define MyAppVersion "1.1.67"
#define MyAppPublisher "VictumPOS"
#define MyAppExeName "VictumPOS.exe"
#define MyAppId "71C1A403-B050-4C45-BD97-A82AC0E7D4C9"
#define StageDir "..\..\artifacts\VictumPOS-win-installer"
#define InstallerOutputDir "..\..\artifacts\installer"

[Setup]
AppId={{{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VictumPOS
DefaultGroupName=VictumPOS
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousLanguage=yes
UsePreviousPrivileges=yes
UsePreviousSetupType=yes
UsePreviousTasks=no
OutputDir={#InstallerOutputDir}
OutputBaseFilename=VictumPOS-Windows-Setup
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\VictumPOS.exe
SetupIconFile=..\..\VictumPOS\favicon.ico
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
MinVersion=6.1sp1

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce
Name: "autostart"; Description: "Abrir VictumPOS al iniciar Windows"; GroupDescription: "Inicio de Windows:"; Flags: checkedonce
Name: "bridgenone"; Description: "No iniciar ni instalar Print Bridge"; GroupDescription: "Print Bridge:"; Flags: exclusive
Name: "bridgeuser"; Description: "Iniciar Print Bridge en modo usuario"; GroupDescription: "Print Bridge:"; Flags: unchecked exclusive
Name: "bridgeservice"; Description: "Instalar Print Bridge como servicio Windows"; GroupDescription: "Print Bridge:"; Flags: unchecked exclusive

[Dirs]
Name: "{commonappdata}\VictumPOS"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Prerequisites"
Name: "{app}\PrintBridge"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Prerequisites\*,*.pdb,PrintBridge\*.pdb"
Source: "{#StageDir}\Prerequisites\*"; DestDir: "{app}\Prerequisites"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\VictumPOS"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Configurar Print Bridge"; Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "console --settings ""{commonappdata}\VictumPOS\settings.json"""; WorkingDir: "{app}\PrintBridge"
Name: "{commondesktop}\VictumPOS"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VictumPOS"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Prerequisites\ndp472-kb4054530-x86-x64-allos-enu.exe"; Parameters: "/q /norestart"; StatusMsg: "Instalando .NET Framework 4.7.2..."; Check: NeedsDotNet472 and FileExists(ExpandConstant('{app}\Prerequisites\ndp472-kb4054530-x86-x64-allos-enu.exe')); Flags: waituntilterminated
Filename: "{app}\Prerequisites\vc_redist.x86.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Instalando Microsoft Visual C++ Runtime x86..."; Check: NeedsVcRuntimeX86 and FileExists(ExpandConstant('{app}\Prerequisites\vc_redist.x86.exe')); Flags: waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "stop"; StatusMsg: "Deteniendo Print Bridge..."; Check: ShouldRefreshBridgeService; Flags: runhidden waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "uninstall"; StatusMsg: "Actualizando Print Bridge como servicio..."; Check: ShouldRefreshBridgeService; Flags: runhidden waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "install --port 9123 --settings ""{commonappdata}\VictumPOS\settings.json"""; StatusMsg: "Instalando Print Bridge como servicio..."; Check: ShouldInstallBridgeService; Flags: runhidden waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "start"; StatusMsg: "Iniciando Print Bridge..."; Check: ShouldInstallBridgeService; Flags: runhidden waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "user --port 9123 --settings ""{commonappdata}\VictumPOS\settings.json"""; StatusMsg: "Iniciando Print Bridge local..."; Check: ShouldStartBridgeUser; Flags: nowait runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir VictumPOS"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "stop"; Flags: runhidden waituntilterminated; RunOnceId: "StopPrintBridge"
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallPrintBridge"

[InstallDelete]
; Solo limpia binarios antiguos de la instalacion, nunca datos de {commonappdata}\VictumPOS.
Type: files; Name: "{app}\*.old"

[Code]
const
  SW_RESTORE = 9;
  HWND_TOPMOST = -1;
  HWND_NOTOPMOST = -2;
  SWP_NOSIZE = $0001;
  SWP_NOMOVE = $0002;
  SWP_SHOWWINDOW = $0040;

function SetForegroundWindow(hWnd: Longint): Boolean;
  external 'SetForegroundWindow@user32.dll stdcall';
function ShowWindow(hWnd: Longint; nCmdShow: Integer): Boolean;
  external 'ShowWindow@user32.dll stdcall';
function SetWindowPos(hWnd: Longint; hWndInsertAfter: Longint; X: Integer; Y: Integer; cx: Integer; cy: Integer; uFlags: Integer): Boolean;
  external 'SetWindowPos@user32.dll stdcall';

function IsUpgradeInstall: Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#MyAppId}}_is1') or
    RegKeyExists(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#MyAppId}}_is1');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsUpgradeInstall and
    ((PageID = wpSelectDir) or
     (PageID = wpSelectProgramGroup));
end;

procedure BringInstallerToFront;
begin
  try
    WizardForm.Position := poScreenCenter;
    BringToFrontAndRestore();
    ShowWindow(WizardForm.Handle, SW_RESTORE);
    SetWindowPos(WizardForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_SHOWWINDOW);
    WizardForm.BringToFront();
    SetForegroundWindow(WizardForm.Handle);
    Sleep(150);
    SetWindowPos(WizardForm.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_SHOWWINDOW);
    SetForegroundWindow(WizardForm.Handle);
  except
  end;
end;

procedure InitializeWizard;
begin
  BringInstallerToFront();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  BringInstallerToFront();
end;

function ServiceExists(ServiceName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(ExpandConstant('{sys}\sc.exe'), 'query "' + ServiceName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
    (ResultCode = 0);
end;

function ShouldInstallBridgeService: Boolean;
begin
  Result := WizardIsTaskSelected('bridgeservice');
end;

function ShouldRefreshBridgeService: Boolean;
begin
  Result := WizardIsTaskSelected('bridgeservice') and ServiceExists('VictumPOSPrintBridge');
end;

function ShouldStartBridgeUser: Boolean;
begin
  Result := WizardIsTaskSelected('bridgeuser');
end;

function NeedsDotNet472: Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := Release < 461808;
end;

function NeedsVcRuntimeX86: Boolean;
var
  Installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86', 'Installed', Installed) then
    Result := Installed <> 1;
end;
