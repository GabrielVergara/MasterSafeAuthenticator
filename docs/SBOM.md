# Software Bill of Materials (SBOM)

SBOMs are generated in CI by `.github/workflows/sbom.yml` and stored under `/sbom`.

## Formats

- **CycloneDX** (`sbom/sbom.cyclonedx.json`) via the `CycloneDX` dotnet tool.
- **SPDX** (`sbom/sbom.spdx.json`) via `anchore/sbom-action`.

## Regenerate locally

```bash
dotnet tool install --global CycloneDX
dotnet CycloneDX SafeAuthenticator.slnx -o sbom -f sbom.cyclonedx.json --json
```

## Direct dependencies

| Component | Version | License |
| --- | --- | --- |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT |
| System.Security.Cryptography.ProtectedData | 9.0.0 | MIT |
| xunit | 2.9.2 | Apache-2.0 |
| FluentAssertions | 6.12.1 | Apache-2.0 |
| Moq | 4.20.72 | BSD-3-Clause |
