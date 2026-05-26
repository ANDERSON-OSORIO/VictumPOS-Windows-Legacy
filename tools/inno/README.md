# VictumPOS WPF Installer

Este instalador esta pensado para Windows 7 SP1 en adelante usando .NET Framework 4.7.2 y CefSharp empacado dentro de la aplicacion.

## Prerrequisitos incluidos

Coloca este archivo en `tools\inno\prerequisites` antes de compilar el instalador:

- `ndp472-kb4054530-x86-x64-allos-enu.exe`
  - Instalador offline de .NET Framework 4.7.2.
- `vc_redist.x86.exe`
  - Microsoft Visual C++ Redistributable x86 requerido por CefSharp.

CefSharp se copia desde `VictumPOS\bin\Release` con la aplicacion. Ya no se descarga ni instala WebView2.

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

## Descargar prerrequisitos

Puedes descargar automaticamente los instaladores requeridos con:

```powershell
.\tools\inno\Download-Prerequisites.ps1
```

Para Windows 7, si el `vc_redist.x86.exe` mas reciente no instala, reemplazalo por un instalador Microsoft Visual C++ 2015-2019 x86 compatible con Windows 7 y conserva el mismo nombre.
