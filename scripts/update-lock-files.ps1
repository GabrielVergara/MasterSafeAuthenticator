# File:    update-lock-files.ps1
# Project: MasterSafe
# Author:  Gabriel Vergara Ezcurdia
# License: See LICENSE
#
# Refreshes packages.windows.lock.json and packages.linux.lock.json. Run after changing a package and commit the result.

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

foreach ($os in 'windows', 'linux') {
    dotnet restore MasterSafe.slnx --force-evaluate -p:MasterSafeLockFiles=true "-p:MasterSafeTargetOs=$os"
    if ($LASTEXITCODE -ne 0) { throw "Restore for $os failed." }
}

# Leaves the normal build files in place for this computer.
dotnet restore MasterSafe.slnx
