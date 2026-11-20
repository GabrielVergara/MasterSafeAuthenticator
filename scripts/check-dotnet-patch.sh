#!/usr/bin/env bash
# Fails when the .NET 10 runtime that this machine would put in a self-contained build is older than the newest .NET 10
# release. Run in the release workflow, so no release ships with a runtime that misses a security fix.
set -euo pipefail

channel="${1:-10.0}"
latest=$(curl -fsS --retry 3 "https://builds.dotnet.microsoft.com/dotnet/release-metadata/${channel}/releases.json" \
  | grep -o '"latest-release": *"[0-9.]*"' | head -1 | grep -o '[0-9][0-9.]*' || true)
if [ -z "$latest" ]; then
  echo "::error::Could not read the newest .NET ${channel} release from Microsoft's release list."
  exit 1
fi

# The runtime bundled with the SDK in use (the one dotnet publish --self-contained takes).
installed=$(dotnet --list-runtimes | awk -v c="Microsoft.NETCore.App ${channel}." 'index($0, c) == 1 {print $2}' | sort -V | tail -1)

echo "Newest .NET ${channel} release: ${latest}. Runtime on this machine: ${installed:-none}."
if [ -z "$installed" ] || [ "$(printf '%s\n%s\n' "$installed" "$latest" | sort -V | head -1)" != "$latest" ]; then
  echo "::error::The .NET runtime ${installed:-none} is older than ${latest}. Update the SDK before building a release."
  exit 1
fi
