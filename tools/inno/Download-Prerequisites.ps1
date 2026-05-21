$ErrorActionPreference = "Stop"

$target = Join-Path $PSScriptRoot "prerequisites"
New-Item -ItemType Directory -Force $target | Out-Null

$downloads = @(
    @{
        Name = "ndp472-kb4054530-x86-x64-allos-enu.exe"
        Url = "https://go.microsoft.com/fwlink/?linkid=863265"
    },
    @{
        Name = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
        Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
    }
)

foreach ($item in $downloads) {
    $destination = Join-Path $target $item.Name
    Write-Host "Descargando $($item.Name)..."
    Invoke-WebRequest -Uri $item.Url -OutFile $destination
}

Write-Host ""
Write-Host "Descargas terminadas en $target"
Write-Host "Para Windows 7/8/8.1 agrega manualmente WebView2 Fixed Runtime 109 en:"
Write-Host (Join-Path $target "WebView2FixedRuntime")
