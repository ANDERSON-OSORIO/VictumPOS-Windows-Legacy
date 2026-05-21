# VictumPOS WPF Installer

Este instalador esta pensado para Windows 7 SP1 en adelante usando .NET Framework 4.7.2.

## Prerrequisitos opcionales incluidos

Coloca estos archivos en `tools\inno\prerequisites` antes de compilar el instalador:

- `ndp472-kb4054530-x86-x64-allos-enu.exe`
  - Instalador offline de .NET Framework 4.7.2.
- `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`
  - Evergreen WebView2 Runtime para Windows 10+.
- `WebView2FixedRuntime\msedgewebview2.exe`
  - Runtime fijo WebView2 109 para Windows 7/8/8.1.

El instalador no borra configuraciones. Los datos permanecen en:

```text
C:\ProgramData\VictumPOS
```

Si esa carpeta no existe, la app usa:

```text
%LOCALAPPDATA%\VictumPOS
```

## Compilar

Instala Inno Setup 6 y ejecuta:

```powershell
.\tools\inno\Build-InnoInstaller.ps1
```

El resultado queda en:

```text
artifacts\installer
```

## Descargar complementos

Puedes descargar automaticamente los instaladores disponibles con:

```powershell
.\tools\inno\Download-Prerequisites.ps1
```

El runtime fijo de WebView2 109 para Windows 7/8/8.1 debe agregarse manualmente como carpeta:

```text
tools\inno\prerequisites\WebView2FixedRuntime\msedgewebview2.exe
```
