## What this changes

<!-- What does it fix or add, and why? Link the issue, for example "Fixes #12". -->

## How it was tested

<!-- Tests you added, and what you tried by hand. Never test against your real vault. -->

## Checklist

- [ ] `dotnet build MasterSafe.slnx -c Release` and `dotnet test MasterSafe.slnx -c Release` pass, with no new warnings
- [ ] A test fails without this change and passes with it
- [ ] No secret, vault, backup or personal data is included, and nothing new is sent over the network or written to logs
- [ ] Old vaults and backups still open (if a file format changed)
- [ ] New text on screen is in the Spanish and Finnish files and the Help, and works with a screen reader and at 200% text size
- [ ] CHANGELOG and the affected docs are updated
- [ ] I agree that this contribution may be distributed under the MasterSafe licence, as described in [CONTRIBUTING.md](../CONTRIBUTING.md)
