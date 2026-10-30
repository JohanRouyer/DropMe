# Publishes DropMe as a self-contained app and builds dist\DropMe-Setup-<version>.exe.
# Needs the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$version = ([xml](Get-Content "$root\DropMe.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $root 'bin\publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\DropMe.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $publish
if ($LASTEXITCODE) { throw "dotnet publish a échoué" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" "$PSScriptRoot\DropMe.iss"
if ($LASTEXITCODE) { throw "La compilation de l'installeur a échoué" }

Get-Item "$root\dist\DropMe-Setup-$version.exe"
