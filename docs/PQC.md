# Post-Quantum Cryptography (PQC)

## Goal

Prepare MasterSafe for a hybrid classical + post-quantum scheme based on **ML-KEM-768**
(FIPS 203) without shipping fake cryptography.

## Current state

`MasterSafe.PostQuantum` defines `IPostQuantumEncryptionProvider` (key generation,
encapsulation, decapsulation). The only shipped implementation, `UnavailableMlKemProvider`, reports
`IsAvailable == false` and **throws** `NotSupportedException` rather than returning insecure output.

## Integration path

.NET 10 exposes `System.Security.Cryptography.MLKem` where the platform cryptographic backend
(OpenSSL 3.5+ / Windows CNG) supports it. To enable real PQC:

1. Add a `MLKemProvider : IPostQuantumEncryptionProvider` backed by `MLKem.GenerateKey(MLKemAlgorithm.MLKem768)`.
2. Set `IsAvailable` based on `MLKem.IsSupported`.
3. Register it ahead of the placeholder in `PostQuantumProviderFactory`.
4. Use it in a **hybrid** construction (ML-KEM shared secret X/HKDF-combined with the AES-256 KEK).

## Why hybrid

Until ML-KEM is broadly vetted in deployment, combine it with the existing AES-256-GCM scheme so the
system is never weaker than today's classical baseline.
