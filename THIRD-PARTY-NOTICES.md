# Third-party notices

MasterSafe Authenticator uses the components below. Each keeps its own license. The full list of packages, with
versions, is in [docs/SBOM.md](docs/SBOM.md).

## Folder icons

The icons that can be given to password folders come from **Material Design Icons** by the
[Pictogrammers](https://pictogrammers.com/) icon group (<https://github.com/Templarian/MaterialDesign>).

> Pictogrammers Free License. This icon collection is released as free, open source, and GPL friendly by the
> Pictogrammers icon group. You may use it for commercial projects, open source projects, or anything really.
>
> Icons: Apache 2.0 (<https://www.apache.org/licenses/LICENSE-2.0>). Some of the icons are redistributed under the
> Apache 2.0 license. All other icons are either redistributed under their respective licenses or are distributed
> under the Apache 2.0 license.

Only a small set of generic icons is used (folder, email, bank, briefcase and similar). No brand or product logos are
included, and nothing is taken from KeePass, KeePassXC or any other password manager.

Icons used: folder, email, bank, briefcase, cart, home, cloud, controller, key-variant, lock, shield-check, web,
server, wifi, credit-card, school, heart, star, music-note, camera, code-braces, account, phone, airplane, paperclip.

## Services the app can contact (only when you use them)

- **Pwned Passwords** by Troy Hunt (Have I Been Pwned, <https://haveibeenpwned.com/Passwords>): the optional breach check
  sends the first 5 characters of a password's SHA-1 hash to api.pwnedpasswords.com and compares the answer locally. The
  Pwned Passwords data is made available under the Creative Commons Attribution 4.0 licence; this notice is the
  attribution.
- **GitHub** (github.com): the optional update check asks for the project's public release information and, when you
  choose "Download and verify", downloads a release file.

## Other components

Avalonia (MIT), CommunityToolkit.Mvvm (MIT), SkiaSharp (MIT), ZXing.Net (Apache-2.0), Inter font (SIL OFL 1.1),
Konscious.Security.Cryptography (MIT), Microsoft.Win32.SystemEvents (MIT), Microsoft.Windows.CsWin32 (MIT) and the .NET runtime (MIT). See
[docs/SBOM.md](docs/SBOM.md) for the exact list.

## Trademarks and independence

KeePass, KeePassXC, Windows, Windows Hello, Microsoft, Google and other names are trademarks of their owners. They are
used here only to say what MasterSafe Authenticator can import from or work with. MasterSafe Authenticator is an
independent project and is not affiliated with, endorsed by or sponsored by any of them.

MasterSafe Authenticator contains no code, icons or other assets from KeePass or KeePassXC. Their export formats
(KeePass XML 2.x and CSV) are read by code written for this project. The vault format is the project's own.
