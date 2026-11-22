# File:    update-scoop.ps1
# Project: MasterSafe
# Author:  Gabriel Vergara Ezcurdia
# License: See LICENSE
#
# Points bucket/mastersafe.json at a published release: the version, the installer URL and its SHA-256, read from the
# release's .sha256 file. Run it after publishing a release, then commit the file:
#   .\scripts\update-scoop.ps1 -Version 1.7.3
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)

$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '..\bucket\mastersafe.json'
$base = "https://github.com/GabrielVergara/MasterSafeAuthenticator/releases/download/v$Version"
$name = "MasterSafe-Authenticator-$Version-win-x64-setup.exe"

$line = (Invoke-RestMethod "$base/$name.sha256").ToString().Trim()
if ($line -notmatch '^([0-9a-fA-F]{64})\s') { throw "Unexpected checksum file: $line" }
$hash = $Matches[1].ToLowerInvariant()

$text = Get-Content $path -Raw -Encoding UTF8
# Only the three values change, so the file keeps its formatting.
$text = $text -replace '(?m)^(\s*"version":\s*")[^"]*(")', "`${1}$Version`$2"
# The file may have CRLF line ends (Git on Windows), and .NET's $ matches only before \n, so allow the \r.
$text = $text -replace '(?m)^(\s*"url":\s*")https://github\.com/[^"]*/releases/download/v\d[^"\r\n]*(",?)(?=\r?$)', "`${1}$base/$name`$2"
$text = $text -replace '(?m)^(\s*"hash":\s*")[0-9a-f]{64}(")', "`${1}$hash`$2"
[IO.File]::WriteAllText((Resolve-Path $path), $text, (New-Object Text.UTF8Encoding($false)))
Write-Host "bucket/mastersafe.json now offers $Version ($hash)."
