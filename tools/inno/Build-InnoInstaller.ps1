param(
    [string]$Configuration = "Release",
    [string]$MsBuildPath = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
    [string]$InnoCompilerPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$solution = Join-Path $root "VictumPOS.WPF.slnx"
$stage = Join-Path $root "artifacts\VictumPOS-win-installer"
$output = Join-Path $root "artifacts\installer"
$appBin = Join-Path $root "VictumPOS\bin\$Configuration"
$bridgeBin = Join-Path $root "VictumPOS.PrintBridge.Service\bin\$Configuration"
$prereq = Join-Path $PSScriptRoot "prerequisites"

if (!(Test-Path $MsBuildPath)) {
    throw "MSBuild no encontrado: $MsBuildPath"
}

& $MsBuildPath $solution /restore /t:Build /p:Configuration=$Configuration
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild fallo con codigo $LASTEXITCODE"
}

Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage, $output | Out-Null

Copy-Item -Recurse -Force (Join-Path $appBin "*") $stage

$bridgeStage = Join-Path $stage "PrintBridge"
New-Item -ItemType Directory -Force $bridgeStage | Out-Null
Copy-Item -Recurse -Force (Join-Path $bridgeBin "*") $bridgeStage

$prereqStage = Join-Path $stage "Prerequisites"
New-Item -ItemType Directory -Force $prereqStage | Out-Null

foreach ($file in @(
    "ndp472-kb4054530-x86-x64-allos-enu.exe"
)) {
    $source = Join-Path $prereq $file
    if (Test-Path $source) {
        Copy-Item -Force $source $prereqStage
    }
}

Get-ChildItem -Path $stage -Recurse -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

if (!(Test-Path $InnoCompilerPath)) {
    throw "ISCC.exe no encontrado. Instala Inno Setup 6 o pasa -InnoCompilerPath."
}

& $InnoCompilerPath (Join-Path $PSScriptRoot "VictumPOS-WPF.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup fallo con codigo $LASTEXITCODE"
}

Write-Host "Instalador generado en $output"
