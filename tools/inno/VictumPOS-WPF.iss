#define MyAppName "VictumPOS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "VictumPOS"
#define MyAppExeName "VictumPOS.exe"
#define StageDir "..\..\artifacts\VictumPOS-win-installer"
#define InstallerOutputDir "..\..\artifacts\installer"

[Setup]
AppId={{71C1A403-B050-4C45-BD97-A82AC0E7D4C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VictumPOS
DefaultGroupName=VictumPOS
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=VictumPOS-Windows-Setup
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
Source: "{#StageDir}\WebView2FixedRuntime\*"; DestDir: "{app}\WebView2FixedRuntime"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\VictumPOS"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Configurar Print Bridge"; Filename: "{app}\PrintBridge\VictumPOS.PrintBridge.Service.exe"; Parameters: "console --settings ""{commonappdata}\VictumPOS\settings.json"""; WorkingDir: "{app}\PrintBridge"
Name: "{commondesktop}\VictumPOS"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VictumPOS"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Prerequisites\ndp472-kb4054530-x86-x64-allos-enu.exe"; Parameters: "/q /norestart"; StatusMsg: "Instalando .NET Framework 4.7.2..."; Check: NeedsDotNet472 and FileExists(ExpandConstant('{app}\Prerequisites\ndp472-kb4054530-x86-x64-allos-enu.exe')); Flags: waituntilterminated
Filename: "{app}\Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando WebView2 Runtime..."; Check: NeedsEvergreenWebView2 and Is64BitInstallMode and FileExists(ExpandConstant('{app}\Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe')); Flags: waituntilterminated
Filename: "{app}\Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX86.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando WebView2 Runtime..."; Check: NeedsEvergreenWebView2 and not Is64BitInstallMode and FileExists(ExpandConstant('{app}\Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX86.exe')); Flags: waituntilterminated
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
function IsWindows7Or81: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 6) and ((Version.Minor = 1) or (Version.Minor = 2) or (Version.Minor = 3));
end;

function NeedsDotNet472: Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := Release < 461808;
end;

function NeedsEvergreenWebView2: Boolean;
var
  Version: String;
begin
  if IsWindows7Or81 then
  begin
    Result := False;
    Exit;
  end;

  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7F5D7-06F0-4D8E-9852-5B6F2D6F16F1}', 'pv', Version) then
    if Version <> '' then
      Result := False;
  if Result and RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7F5D7-06F0-4D8E-9852-5B6F2D6F16F1}', 'pv', Version) then
    if Version <> '' then
      Result := False;
end;
