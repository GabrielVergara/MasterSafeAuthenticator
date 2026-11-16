# Promotion without money

What is automatic and what is done by hand. Nothing here costs money, and nothing is posted under a false name.

## Automatic

- **Release announcements.** `.github/workflows/announce.yml` runs when a release is published and posts a short, fixed text
  with the release link on Bluesky and Mastodon (`scripts/announce.py`). A service is skipped when its secrets are missing.
  Secrets (repository Settings, Secrets and variables, Actions, or `gh secret set NAME -R GabrielVergara/MasterSafeAuthenticator`,
  which asks for the value without echoing it):

  | Secret | Value |
  | --- | --- |
  | `BLUESKY_HANDLE` | The account, for example `mastersafe.bsky.social` |
  | `BLUESKY_APP_PASSWORD` | An app password (Bluesky: Settings, Privacy and security, App passwords), never the account password |
  | `MASTODON_URL` | The instance, for example `https://mastodon.social` |
  | `MASTODON_TOKEN` | A token with only the `write:statuses` scope (Preferences, Development, New application) |

  Try it without posting: Actions, announce, Run workflow, a version, "Only print the posts" ticked. Untick it to post for real.
- **winget** (see [Distribution.md](Distribution.md)): once accepted, `winget install mastersafe` and a daily check that proposes new
  releases.
- **Search engines.** The repository has a description and topics, and the web site has articles about the app.

## By hand, once

- Upload `docs/social-preview.png` in the repository Settings, "Social preview", so links to the repository show a card.
- Add the app to AlternativeTo and Product Hunt, with your own account. Say plainly that it is source-available (not open source)
  and that much of it was written with an AI assistant.
- A post on Hacker News ("Show HN") or Reddit (r/privacy, r/selfhosted) only if it follows each community's rules and says the
  same. Many lists (Fosshub, awesome-privacy, Privacy Guides) only take open-source software, so MasterSafe does not qualify.

## Not done

Fake accounts, comments in other people's threads, asking for stars. They are dishonest and they are noticed.
