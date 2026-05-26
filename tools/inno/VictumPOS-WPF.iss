#define MyAppName "VictumPOS"
#define MyAppVersion "1.1.67"
#define MyAppPublisher "VictumPOS"
#define MyAppExeName "VictumPOS.exe"
#define StageDir "..\..\artifacts\VictumPOS-win-installer"
#define InstallerOutputDir "..\..\artifacts\installer"

[Setup]
AppId={{71C1A403-B050-4C45-BD97-A82AC0E7D4C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} WebView2
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VictumPOS
DefaultGroupName=VictumPOS
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=VictumPOS-Windows10-WebView2-Setup
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
MinVersion=10.0

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce
Name: "autostart"; Description: "Abrir VictumPOS al iniciar Windows"; GroupDescription: "Inicio de Windows:"; Flags: checkedonce
Name: "bridgenone"; Description: "No iniciar ni instalar Print Bridge"; GroupDescription: "Print Bridge:"; Flags: checkedonce exclusive
Name: "bridgeuser"; Description: "Iniciar Print Bridge en modo usuario"; GroupDescription: "Print Bridge:"; Flags: unchecked exclusive
Name: "bridgeservice"; Description: "Instalar Print Bridge como servicio Windows"; GroupDescription: "Print Bridge:"; Flags: unchecked exclusive

[Dirs]
Name: "{commonappdata}\VictumPOS"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Prerequisites"
Name: "{app}\PrintBridge"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Prerequisites\*,WebView2FixedRuntime\*,*.pdb,PrintBridge\*.pdb"
Source: "{#StageDir}\Prerequisites\*"; DestDir: "{app}\Prerequisites"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\VictumPOS"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Configurar Print Bridge"; Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "console --settings ""{commonappdata}\VictumPOS\settings.json"""; WorkingDir: "{app}\PrintBridge"
Name: "{commondesktop}\VictumPOS"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VictumPOS"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Prerequisites\ndp472-kb4054530-x86-x64-allos-enu.exe"; Parameters: "/q /norestart"; StatusMsg: "Instalando .NET Framework 4.7.2..."; Check: NeedsDotNet472 and FileExists(ExpandConstant('{app}\Prerequisites\ndp472-kb4054530-x86-x64-allos-enu.exe')); Flags: waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "install --port 9123 --settings ""{commonappdata}\VictumPOS\settings.json"""; StatusMsg: "Instalando Print Bridge como servicio..."; Tasks: bridgeservice; Flags: runhidden waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "start"; StatusMsg: "Iniciando Print Bridge..."; Tasks: bridgeservice; Flags: runhidden waituntilterminated
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "user --port 9123 --settings ""{commonappdata}\VictumPOS\settings.json"""; StatusMsg: "Iniciando Print Bridge local..."; Tasks: bridgeuser; Flags: nowait runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir VictumPOS"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "stop"; Flags: runhidden waituntilterminated; RunOnceId: "StopPrintBridge"
Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallPrintBridge"

[InstallDelete]
; Solo limpia binarios antiguos de la instalacion, nunca datos de {commonappdata}\VictumPOS.
Type: files; Name: "{app}\*.old"

[Code]
function NeedsDotNet472: Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := Release < 461808;
end;

function IsWebView2Installed: Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7F5D7-06F0-4D8E-9852-5B6F2D6F16F1}', 'pv', Version) then
    if Version <> '' then
      Result := True;
  if (not Result) and RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7F5D7-06F0-4D8E-9852-5B6F2D6F16F1}', 'pv', Version) then
    if Version <> '' then
      Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsWebView2Installed then
  begin
    MsgBox(
      'VictumPOS WebView2 requiere Microsoft Edge WebView2 Runtime instalado en Windows 10 o superior.' #13#10 #13#10 +
      'Instala WebView2 Runtime de Microsoft y vuelve a ejecutar este instalador.',
      mbError,
      MB_OK);
    Result := False;
  end;
end;
