# Software Bill of Materials (SBOM)

SBOMs are generated in CI by `.github/workflows/sbom.yml` and stored under `/sbom`.

## Formats

- **CycloneDX** (`sbom/sbom.cyclonedx.json`) via the `CycloneDX` dotnet tool.
- **SPDX** (`sbom/sbom.spdx.json`) via `anchore/sbom-action`.

## Regenerate locally

```bash
dotnet tool install --global CycloneDX
dotnet CycloneDX MasterSafe.slnx -o sbom -f sbom.cyclonedx.json --json
```

Release builds are self-contained, so each installer also contains the .NET runtime and the native libraries of
Avalonia and SkiaSharp. The SBOM above lists the NuGet packages; the runtime version is the one the build used
(`dotnet --version`).

## Direct dependencies

| Component | Version | License |
| --- | --- | --- |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 11.2.3 | MIT |
| CommunityToolkit.Mvvm | 8.3.2 | MIT |
| ZXing.Net, SkiaSharp | see `MasterSafe.QR.csproj` | Apache-2.0 / MIT |
| Microsoft.EntityFrameworkCore.Sqlite | see `MasterSafe.Data.csproj` | MIT |
| xunit | 2.9.2 | Apache-2.0 |
| FluentAssertions | 6.12.1 | Apache-2.0 (check the terms of newer 8.x versions before upgrading) |
| Moq | 4.20.72 | BSD-3-Clause |
| coverlet.collector | 6.0.2 | MIT |

ML-KEM and ML-DSA come from the .NET 10 runtime and the operating system's cryptography provider, not from a
NuGet package. Windows Hello uses the Windows 10 APIs projected by the .NET SDK (Windows builds only).

## Advisories

CI runs `dotnet list package --vulnerable --include-transitive` on Ubuntu and Windows. At the time of writing nothing is
reported. `Tmds.DBus.Protocol` (used by Avalonia on Linux) was affected by GHSA-xrw6-gwf8-vvr9 (CVE-2026-39959);
the app pins the patched 0.21.3 as a direct reference in `MasterSafe.App.csproj`. Remove that pin once Avalonia
depends on a fixed version. The pinned version has not been run on a Linux desktop yet, so test the Linux tray and
notifications after each upgrade.
