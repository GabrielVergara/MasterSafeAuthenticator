#!/usr/bin/env python3
# File:    announce.py
# Project: MasterSafe
# Author:  Gabriel Vergara Ezcurdia
# License: See LICENSE
#
# Announces a published release on Mastodon and Bluesky. Used by .github/workflows/announce.yml; see docs/Promotion.md.
#   announce.py <version> [--dry-run]
# A service is skipped when its secrets are missing. Secrets (environment): MASTODON_URL (for example https://mastodon.social),
# MASTODON_TOKEN (scope write:statuses), BLUESKY_HANDLE, BLUESKY_APP_PASSWORD (an app password, never the account password).
import json
import os
import sys
import urllib.request

REPO = "GabrielVergara/MasterSafeAuthenticator"


def message(version):
    url = f"https://github.com/{REPO}/releases/tag/v{version}"
    text = (f"MasterSafe Authenticator {version} is out: a private, offline authenticator and password vault with "
            f"post-quantum encryption, for Windows and Linux. Source-available, every release signed.\n\n{url}\n\n"
            "#2FA #privacy #passwordmanager")
    return text, url


def post(url, payload, headers):
    request = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json", **headers})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def mastodon(text, dry):
    base, token = os.environ.get("MASTODON_URL", "").rstrip("/"), os.environ.get("MASTODON_TOKEN", "")
    if not base or not token:
        print("Mastodon: MASTODON_URL or MASTODON_TOKEN is not set, skipped.")
        return
    if dry:
        print(f"Mastodon (dry run) -> {base}/api/v1/statuses\n{text}\n")
        return
    result = post(f"{base}/api/v1/statuses", {"status": text, "visibility": "public", "language": "en"},
                  {"Authorization": f"Bearer {token}"})
    print("Mastodon: posted", result.get("url"))


def facets(text):
    """Makes the link and the hashtags clickable on Bluesky. Positions are UTF-8 byte offsets."""
    raw, found = text.encode(), []
    for word in text.replace("\n", " ").split(" "):
        kind = "link" if word.startswith("https://") else "tag" if word.startswith("#") else None
        if not kind:
            continue
        start = raw.index(word.encode())
        feature = ({"$type": "app.bsky.richtext.facet#link", "uri": word} if kind == "link"
                   else {"$type": "app.bsky.richtext.facet#tag", "tag": word[1:]})
        found.append({"index": {"byteStart": start, "byteEnd": start + len(word.encode())}, "features": [feature]})
    return found


def bluesky(text, dry):
    handle, password = os.environ.get("BLUESKY_HANDLE", ""), os.environ.get("BLUESKY_APP_PASSWORD", "")
    if not handle or not password:
        print("Bluesky: BLUESKY_HANDLE or BLUESKY_APP_PASSWORD is not set, skipped.")
        return
    record = {"$type": "app.bsky.feed.post", "text": text, "facets": facets(text), "langs": ["en"],
              "createdAt": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")}
    if dry:
        print(f"Bluesky (dry run) as {handle}\n{json.dumps(record, indent=1)}\n")
        return
    session = post("https://bsky.social/xrpc/com.atproto.server.createSession", {"identifier": handle, "password": password}, {})
    result = post("https://bsky.social/xrpc/com.atproto.repo.createRecord",
                  {"repo": session["did"], "collection": "app.bsky.feed.post", "record": record},
                  {"Authorization": f"Bearer {session['accessJwt']}"})
    print("Bluesky: posted", result.get("uri"))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        sys.exit("usage: announce.py <version> [--dry-run]")
    version, dry = args[0].lstrip("v"), "--dry-run" in sys.argv
    text, _ = message(version)
    mastodon(text, dry)
    bluesky(text, dry)


if __name__ == "__main__":
    main()
