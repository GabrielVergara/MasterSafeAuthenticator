# How MasterSafe compares

This page helps you decide whether MasterSafe Authenticator suits you. It shows where other apps are ahead as well as where
MasterSafe is. Checked on 2026-11-01 from each project's own pages where possible. Products change, so check the sources
below before you rely on a detail. If something here is wrong, please [open an issue](https://github.com/GabrielVergara/MasterSafeAuthenticator/issues).

**In short:** MasterSafe keeps passwords and authenticator codes together in one encrypted file on your own computer, with
no account and no cloud. If you need autofill in the browser, a phone app or automatic sync, or you want software that has
had an independent security audit, one of the other apps suits you better today.

In the tables, **?** means we could not confirm it from the sources we used. It does not mean the app lacks the feature.

## The basics

| | MasterSafe | KeePassXC | Bitwarden | 1Password | Proton Pass | Ente Auth | Aegis |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Stores | Passwords and codes | Passwords and codes | Passwords, and codes on Premium | Passwords and codes | Passwords, and codes on Plus | Codes only | Codes only |
| Computers | Windows, Linux | Windows, macOS, Linux | All major systems | All major systems | All major systems | Windows, macOS, Linux | None |
| Phones | No | Through other apps | Yes | Yes | Yes | Yes | Android |
| Works with no account | Yes | Yes | No | No | No | Yes | Yes |
| Where your data lives | A file on your computer | A file you control | Their cloud, or your own server | Their cloud | Their cloud | On the device, or in their cloud if you choose | On the device |
| Price | Free for personal, non-commercial use | Free | Free, Premium about $20 a year | Paid | Free, Plus is paid | Free | Free |
| Open source | No, the source can be read for review | Yes (GPLv3) | Yes (AGPL and Bitwarden License) | No | Yes | Yes | Yes (GPLv3) |

## Security

| | MasterSafe | KeePassXC | Bitwarden | 1Password | Proton Pass | Ente Auth | Aegis |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Independent security audit | **None yet** | Yes: 2023 audit, French ANSSI security visa in 2025 | Yes: several every year | Yes: lists a 2020 review, and ongoing penetration tests | Yes: Cure53, 2023 | Yes: Cure53, sponsored by CERN | None found |
| Post-quantum protection | Recovery backups (ECDH-P384 with ML-KEM-768) and release signatures (ML-DSA-65). The vault itself uses AES-256 | ? | Was still evaluating, on an older page | Web app connections (2026), passkeys being added | None found for Pass | ? | ? |
| Hardware key | Unlocks the vault with a FIDO2 key (Windows only) | YubiKey or OnlyKey strengthens the database key | Protects the account login | ? | ? | ? | ? |
| Fingerprint or face | Windows Hello | ? | ? | ? | ? | ? | Yes |
| Emergency access for someone you trust | No, recovery key instead | ? | Yes, on Premium | ? | Yes, on Plus | Not applicable | Not applicable |

## Everyday features

| | MasterSafe | KeePassXC | Bitwarden | 1Password | Proton Pass | Ente Auth | Aegis |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Browser autofill | No | Yes, by extension | Yes | Yes | Yes | Not applicable | Not applicable |
| Passkeys | No | Yes | Yes | Yes | Yes | Not applicable | Not applicable |
| Sync between devices | No, backups to a folder or WebDAV | Through a cloud folder of your choice | Built in | Built in | Built in | Built in, optional | No, backups to a place you choose |
| Weak and reused password checks | Yes | ? | Yes, on Premium | Yes (Watchtower) | Yes | Not applicable | Not applicable |
| Breach check | Yes, off until you turn it on | ? | Yes, on Premium | Yes (Watchtower) | Yes, on Plus | Not applicable | Not applicable |
| File attachments | Yes, up to 5 MiB each | ? | Yes, on Premium | ? | Yes, on Plus | Not applicable | Not applicable |
| Auto-type | No | Yes | ? | ? | ? | Not applicable | Not applicable |
| SSH agent | No | Yes | ? | Yes | ? | Not applicable | Not applicable |
| Tap to reveal codes | Planned for 1.6 | ? | ? | ? | ? | ? | Yes |

## Where each kind of app fits

- **MasterSafe:** you want passwords and codes in one place, offline, on Windows or Linux, with no account, and with hardware
  or Windows Hello unlock.
- **KeePassXC:** you want a mature offline password manager that has been audited, with browser support and an SSH agent.
- **Bitwarden, 1Password, Proton Pass:** you want everything on every device, and are happy to have an account.
- **Ente Auth, Aegis:** you only need authenticator codes, above all on a phone.

## What MasterSafe does not claim

MasterSafe is written to be as safe as it can be, but **no independent firm has reviewed it**, and it is the work of one
person. A self-assessment ([OWASP checklist](OwaspChecklist.md)) and an AI-assisted review are not the same as an audit. No
password manager can fully protect you from malware running on your computer while the vault is unlocked. See the
[threat model](ThreatModel.md).

## Sources

- KeePassXC: [documentation](https://keepassxc.org/docs/), [ANSSI visa](https://www.privacyguides.org/news/2025/11/25/keepassxc-awarded-anssi-security-visa/), [2023 audit](https://keepassxc.org/blog/2023-04-15-audit-report/), [passkeys](https://www.ghacks.net/?p=205232)
- Bitwarden: [audits](https://bitwarden.com/help/is-bitwarden-audited/), [plans](https://bitwarden.com/pricing/), [encryption](https://bitwarden.com/help/what-encryption-is-used/), [emergency access](https://bitwarden.com/help/emergency-access/)
- 1Password: [assessments](https://support.1password.com/security-assessments/), [post-quantum web app](https://1password.com/blog/post-quantum-cryptography), [post-quantum passkeys](https://1password.com/blog/post-quantum-passkeys), [Watchtower](https://support.1password.com/watchtower/)
- Proton Pass: [plans](https://proton.me/pass/pricing), [audit](https://proton.me/blog/pass-open-source-security-audit)
- Ente Auth: [FAQ](https://ente.com/help/auth/faq/), [audit](https://ente.com/blog/cern-audit)
- Aegis: [README](https://github.com/beemdevelopment/Aegis)

Product names are the trademarks of their owners and are used here only to identify them.
