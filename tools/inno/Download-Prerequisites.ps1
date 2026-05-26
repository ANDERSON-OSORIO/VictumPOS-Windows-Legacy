$ErrorActionPreference = "Stop"

$target = Join-Path $PSScriptRoot "prerequisites"
New-Item -ItemType Directory -Force $target | Out-Null

$downloads = @(
    @{
        Name = "ndp472-kb4054530-x86-x64-allos-enu.exe"
        Url = "https://go.microsoft.com/fwlink/?linkid=863265"
    },
    @{
        Name = "vc_redist.x86.exe"
        Url = "https://aka.ms/vs/17/release/vc_redist.x86.exe"
    }
)

foreach ($item in $downloads) {
    $destination = Join-Path $target $item.Name
    $download = "$destination.download"
    if (Test-Path $destination) {
        Write-Host "Ya existe $($item.Name), se omite descarga."
        continue
    }

    Write-Host "Descargando $($item.Name)..."
    Remove-Item -Force $download -ErrorAction SilentlyContinue
    Invoke-WebRequest -Uri $item.Url -OutFile $download
    Move-Item -Force $download $destination
}

Write-Host ""
Write-Host "Descargas terminadas en $target"
Write-Host "CefSharp queda empacado con la aplicacion; ya no se descarga ni instala WebView2."
Write-Host "Si el VC++ Redistributable mas reciente no instala en Windows 7, usa un vc_redist.x86.exe 2015-2019 compatible y conserva ese mismo nombre."
