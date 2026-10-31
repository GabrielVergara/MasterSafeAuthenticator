<#
.SYNOPSIS
  Builds the Windows installer for MasterSafe Authenticator.

.DESCRIPTION
  1. Publishes a self-contained win-x64 build (no .NET install needed on the user's PC).
  2. Compiles installer\windows\MasterSafe.iss with Inno Setup into artifacts\.
  3. Writes a SHA-256 checksum file and, when a signing key is given, an ML-DSA-65 signature.

.PARAMETER Version
  Version shown in the installer and file name.

.PARAMETER SigningKey
  Path to the ML-DSA private key file made with: MasterSafe.Cli keygen <name>
  Keep this file out of the repository.

.EXAMPLE
  .\installer\build-windows.ps1 -Version 1.2.0 -SigningKey C:\keys\masterSafe-release.key
#>
param(
    [string]$Version = "1.1.0",
    [string]$SigningKey = ""
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $root "artifacts\win-x64"
$artifacts = Join-Path $root "artifacts"

Write-Host "Publishing MasterSafe Authenticator $Version (win-x64, self-contained)..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root "src\MasterSafe.App\MasterSafe.App.csproj") `
    -c Release -r win-x64 --self-contained `
    -p:Version=$Version -p:PublishReadyToRun=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup 6 was not found. Install it (winget install JRSoftware.InnoSetup) and run this script again."
    Write-Host "The published files are in $publishDir"
    exit 1
}

& $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot "windows\MasterSafe.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$installer = Join-Path $artifacts "MasterSafe-Authenticator-$Version-win-x64-setup.exe"
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $installer -Leaf)" | Set-Content -Path "$installer.sha256" -Encoding ascii
Write-Host "SHA-256: $hash"

if ($SigningKey) {
    dotnet run --project (Join-Path $root "src\MasterSafe.Cli\MasterSafe.Cli.csproj") -c Release -- sign $installer $SigningKey
    if ($LASTEXITCODE -ne 0) { throw "Signing failed" }
} else {
    Write-Warning "No signing key given. The installer is not signed with ML-DSA."
}

Write-Host "Done: $installer"
