#!/usr/bin/env bash
# Fails when any direct or transitive NuGet package has a known vulnerability.
# Used by CI (build, security, release workflows) and can be run locally before a release.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

dotnet restore MasterSafe.slnx > /dev/null

report="$(dotnet list MasterSafe.slnx package --vulnerable --include-transitive 2>&1)"
echo "$report"

echo
echo "Deprecated packages (information only):"
dotnet list MasterSafe.slnx package --deprecated 2>&1 | grep -E ">|has the following deprecated" || echo "none"

if echo "$report" | grep -q "has the following vulnerable packages"; then
  echo "::error::Known vulnerable packages found. Update them (see Dependabot pull requests) before building a release."
  exit 1
fi

echo
echo "No known vulnerable packages."
