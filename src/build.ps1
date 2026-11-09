# Compila src\*.cs em bin\ScreenPets.exe usando o compilador C# que já vem no Windows (.NET Framework 4).
# Os personagens (personagens\*.json), a fonte e o ícone vão embutidos no .exe, para ele também funcionar
# sozinho como instalador. Uma cópia com o nome de distribuição fica em dist\ScreenPets-Setup.exe.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'Compilador C# do .NET Framework 4 nao encontrado.' }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
Get-Process ScreenPets -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$sources = (Get-ChildItem (Join-Path $PSScriptRoot '*.cs')).FullName
$res = Join-Path $PSScriptRoot 'recursos'
$resources = @("/resource:$(Join-Path $res 'PixelifySans.ttf'),recursos/PixelifySans.ttf")
foreach ($f in Get-ChildItem (Join-Path $root 'personagens\*.json')) {
    $resources += "/resource:$($f.FullName),personagens/$($f.Name)"
}
$out = Join-Path $bin 'ScreenPets.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out" "/win32icon:$(Join-Path $res 'icone.ico')" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll $resources $sources
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $out (Join-Path $dist 'ScreenPets-Setup.exe') -Force
# o site oferece o instalador para baixar (site\index.html aponta para ScreenPets-Setup.exe ao lado)
$site = Join-Path $root 'site'
if (Test-Path $site) { Copy-Item $out (Join-Path $site 'ScreenPets-Setup.exe') -Force }
Write-Host 'Compilado: bin\ScreenPets.exe (e dist\ScreenPets-Setup.exe para distribuir)'
