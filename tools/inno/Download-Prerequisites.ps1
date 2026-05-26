$ErrorActionPreference = "Stop"

$target = Join-Path $PSScriptRoot "prerequisites"
New-Item -ItemType Directory -Force $target | Out-Null

$downloads = @(
    @{
        Name = "ndp472-kb4054530-x86-x64-allos-enu.exe"
        Url = "https://go.microsoft.com/fwlink/?linkid=863265"
    }
)

foreach ($item in $downloads) {
    $destination = Join-Path $target $item.Name
    Write-Host "Descargando $($item.Name)..."
    Invoke-WebRequest -Uri $item.Url -OutFile $destination
}

Write-Host ""
Write-Host "Descargas terminadas en $target"
Write-Host "La variante WebView2 para Windows 10+ no empaca ni instala WebView2 Runtime."
Write-Host "WebView2 Runtime debe estar instalado previamente en el equipo."
