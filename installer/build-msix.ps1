<#
.SYNOPSIS
  Builds the Microsoft Store package (MSIX) of MasterSafe Authenticator.

.DESCRIPTION
  Packs a program folder with installer\windows\msix\AppxManifest.xml into
  artifacts\MasterSafe-Authenticator-<version>-win-x64.msix. The package is not signed: the Microsoft Store signs it
  when it is submitted. To try it on this computer first, sign it with a test certificate (docs\Distribution.md).

  Use the portable zip from the signed GitHub release as the source, so the Store gets exactly the published files.
  Check the zip first: MasterSafe-Cli verify-release <folder with the release files>

.PARAMETER Version
  The release version, for example 1.8.1. The package version gets a fourth part of 0, as the Store requires.

.PARAMETER Source
  The portable zip (MasterSafe-Authenticator-<version>-win-x64-portable.zip) or a program folder. Defaults to
  artifacts\win-x64, the folder build-windows.ps1 publishes.

.EXAMPLE
  .\installer\build-msix.ps1 -Version 1.8.1 -Source C:\release\MasterSafe-Authenticator-1.8.1-win-x64-portable.zip
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Source = ""
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version must look like 1.8.1" }
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifacts = Join-Path $root "artifacts"
if (-not $Source) { $Source = Join-Path $artifacts "win-x64" }
$Source = (Resolve-Path $Source).Path

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Directory.Parent.Name } -ErrorAction SilentlyContinue | Select-Object -Last 1
if (-not $makeappx) { throw "makeappx.exe was not found. Install the Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100)." }

$stage = Join-Path $artifacts "msix-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null

if ($Source.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $unzipped = Join-Path $artifacts "msix-unzip"
    if (Test-Path $unzipped) { Remove-Item $unzipped -Recurse -Force }
    [IO.Compression.ZipFile]::ExtractToDirectory($Source, $unzipped)
    # The zip holds one top folder, MasterSafe-Authenticator-<version>-win-x64.
    $Source = (Get-ChildItem $unzipped -Directory | Select-Object -First 1).FullName
}

if (-not (Test-Path (Join-Path $Source "MasterSafe.App.exe"))) { throw "$Source does not hold MasterSafe.App.exe" }
if (-not (Test-Path (Join-Path $Source "installed-files.txt"))) { throw "$Source has no installed-files.txt" }
$listHeader = Get-Content (Join-Path $Source "installed-files.txt") -TotalCount 1
if ($listHeader -notmatch " $([regex]::Escape($Version)) win-x64$") { throw "The program folder is not version $Version ($listHeader)" }

Copy-Item (Join-Path $Source "*") $stage -Recurse
Copy-Item (Join-Path $root "LICENSE") $stage -Force
Copy-Item (Join-Path $PSScriptRoot "windows\msix\Assets") (Join-Path $stage "Assets") -Recurse
$manifest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "windows\msix\AppxManifest.xml")).Replace('$VERSION$', "$Version.0")
[IO.File]::WriteAllText((Join-Path $stage "AppxManifest.xml"), $manifest, (New-Object Text.UTF8Encoding $false))

$package = Join-Path $artifacts "MasterSafe-Authenticator-$Version-win-x64.msix"
& $makeappx.FullName pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
Remove-Item $stage -Recurse -Force
if ($unzipped) { Remove-Item $unzipped -Recurse -Force }

Write-Host "Store package: $package"
Write-Host "Upload it in Partner Center (MasterSafe Authenticator > Packages). The Store signs it."
