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
  .\installer\build-windows.ps1 -Version 1.3.1 -SigningKey C:\keys\masterSafe-release.key
#>
param(
    [string]$Version = "1.3.1",
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

# Debug symbols are for developers. SkiaSharp 4 ships two native ones of over 110 MB that users never need.
Get-ChildItem $publishDir -Recurse -Filter *.pdb | Remove-Item -Force

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

# A standalone copy of the command-line tool, so anyone can verify the download without installing .NET.
$cliProject = Join-Path $root "src\MasterSafe.Cli\MasterSafe.Cli.csproj"
$cliDir = Join-Path $artifacts "cli-win-x64"
dotnet publish $cliProject -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version -o $cliDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the command-line tool failed" }
$cli = Join-Path $artifacts "MasterSafe-Cli-$Version-win-x64.exe"
Copy-Item (Join-Path $cliDir "MasterSafe.Cli.exe") $cli -Force
$cliHash = (Get-FileHash $cli -Algorithm SHA256).Hash.ToLowerInvariant()
"$cliHash  $(Split-Path $cli -Leaf)" | Set-Content -Path "$cli.sha256" -Encoding ascii

if ($SigningKey) {
    foreach ($file in @($installer, $cli)) {
        dotnet run --project $cliProject -c Release -- sign $file $SigningKey
        if ($LASTEXITCODE -ne 0) { throw "Signing $file failed" }
    }
} else {
    Write-Warning "No signing key given. The installer and the command-line tool are not signed with ML-DSA."
}

Write-Host "Done: $installer"
Write-Host "Also built: $cli"
