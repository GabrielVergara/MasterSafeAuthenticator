<#
.SYNOPSIS
  Signs a draft release offline and publishes it.

.DESCRIPTION
  The release workflow builds the files and records where they came from, but it never holds the signing key. This script
  runs on the maintainer's own computer:

    1. downloads the files of the draft release,
    2. checks that GitHub's build attestation says they were built by this repository's release workflow
       (a file that was swapped after the build fails this check),
    3. checks every file against its .sha256,
    4. signs each installer, package and command-line tool with the offline ML-DSA-65 key,
    5. verifies all signatures against signing/masterSafe-release.pub,
    6. uploads the .sig files and, with -Publish, publishes the release.

  Nothing is signed unless every check passes. See docs/ReleaseProcess.md.

.PARAMETER Tag
  The release tag, for example v1.5.0. The draft release must exist (it is created when the tag is pushed).

.PARAMETER Key
  The private signing key file. Default: $HOME\.masterSafe-signing\masterSafe-release.key

.PARAMETER Publish
  Also publish the release (and mark it as the latest).

.PARAMETER NotesFile
  Release notes to use when publishing. Default: the section of CHANGELOG.md for this version.

.EXAMPLE
  .\scripts\sign-release.ps1 -Tag v1.5.0 -Publish
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Key = (Join-Path $HOME ".masterSafe-signing\masterSafe-release.key"),
    [switch]$Publish,
    [string]$NotesFile = ""
)

$ErrorActionPreference = "Stop"
$repo = "GabrielVergara/MasterSafeAuthenticator"
$root = Split-Path -Parent $PSScriptRoot
$pub = Join-Path $root "signing\masterSafe-release.pub"
$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
$gh = if ($ghCommand) { $ghCommand.Source } else { "C:\Program Files\GitHub CLI\gh.exe" }
if (-not (Test-Path $gh)) { throw "GitHub CLI (gh) was not found. Install it and run: gh auth login" }
if (-not (Test-Path $Key)) { throw "The signing key was not found: $Key" }
if (-not (Test-Path $pub)) { throw "The public key was not found: $pub" }

$cli = @("run", "--project", (Join-Path $root "src\MasterSafe.Cli"), "-c", "Release", "--")
function Invoke-Cli { & dotnet @cli @args; if ($LASTEXITCODE -ne 0) { throw "MasterSafe.Cli failed: $args" } }

$work = Join-Path ([IO.Path]::GetTempPath()) ("ms-sign-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Write-Host "1. Downloading the files of release $Tag ..."
    & $gh release download $Tag -R $repo -D $work
    if ($LASTEXITCODE -ne 0) { throw "Could not download the release $Tag. Does the draft exist?" }

    $files = Get-ChildItem $work -File | Where-Object { $_.Name -notmatch '\.(sig|sha256|pub|json)$' }
    if (-not $files) { throw "The release has no files to sign." }
    Write-Host ("   {0} files to sign." -f $files.Count)

    Write-Host "2. Checking the build attestation of each file (built by this repository's release workflow) ..."
    foreach ($f in $files) {
        & $gh attestation verify $f.FullName -R $repo --signer-workflow "$repo/.github/workflows/release.yml" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "No valid build attestation for $($f.Name). It may have been replaced. NOT signing." }
        Write-Host "   OK  $($f.Name)"
    }

    Write-Host "3. Checking the checksums ..."
    foreach ($f in $files) {
        $shaFile = "$($f.FullName).sha256"
        if (-not (Test-Path $shaFile)) { throw "No .sha256 for $($f.Name)." }
        $recorded = ((Get-Content $shaFile -Raw).Trim() -split '\s+')[0]
        $actual = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
        if ($recorded -ne $actual) { throw "The checksum of $($f.Name) does not match. NOT signing." }
    }

    Write-Host "4. Signing with the offline key ..."
    foreach ($f in $files) { Invoke-Cli sign $f.FullName $Key | Out-Null; Write-Host "   signed $($f.Name)" }

    Write-Host "5. Verifying every signature against signing\masterSafe-release.pub ..."
    Copy-Item $pub $work -Force
    Invoke-Cli verify-release $work $pub

    Write-Host "6. Uploading the signatures ..."
    $sigs = Get-ChildItem $work -File -Filter *.sig | ForEach-Object { $_.FullName }
    & $gh release upload $Tag @sigs -R $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "Uploading the signatures failed." }

    if ($Publish) {
        $version = $Tag.TrimStart('v')
        if (-not $NotesFile) {
            $changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw
            $match = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($version)) .*?\r?\n(.*?)(?=^## |\z)")
            if (-not $match.Success) { throw "No section for $version in CHANGELOG.md." }
            $NotesFile = Join-Path $work "notes.md"
            Set-Content $NotesFile $match.Groups[1].Value.Trim() -Encoding utf8
        }
        & $gh release edit $Tag -R $repo --draft=false --latest --title "MasterSafe Authenticator $version" --notes-file $NotesFile
        if ($LASTEXITCODE -ne 0) { throw "Publishing failed." }
        Write-Host "Published: https://github.com/$repo/releases/tag/$Tag"
    }
    else {
        Write-Host "Signed. Review the draft, then publish it (or run this script again with -Publish)."
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
