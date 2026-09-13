# Jellyfin Public Media Links

Share a single library item with someone who does not have a Jellyfin account, using a link
that expires on its own.

Jellyfin's built-in "Copy media URL" embeds your `api_key`. Anyone you send it to holds a
credential for your whole library until you rotate it. This plugin issues a different kind of
URL: a signed token that names exactly one item, carries its own expiry, and grants nothing
else.

## How the token works

A share token is `base64url(payload ‖ mac)`:

| Field   | Size     | Purpose                                    |
|---------|----------|--------------------------------------------|
| version | 1 byte   | Format versioning                          |
| item id | 16 bytes | The single item the link unlocks           |
| expiry  | 8 bytes  | Unix seconds, **inside** the signed payload |
| nonce   | 8 bytes  | Identifies the link so it can be revoked   |
| mac     | 16 bytes | HMAC-SHA256 over the payload, truncated    |

Properties that fall out of this:

- **The expiry cannot be extended by the holder.** It is covered by the signature, and the
  signature is verified with a constant-time comparison.
- **Nothing secret is stored on disk.** Only the nonce is persisted. The token is a pure
  function of `(signing key, item, expiry, nonce)`, so the dashboard re-derives the URL when
  it lists links rather than keeping a copy.
- **Revocation is an allow list, not a block list.** A token is honoured only while its entry
  exists in the plugin config, so deleting the entry kills that one link immediately.
  "Revoke every link" rotates the signing key, which breaks every token ever issued.
- **Failures are indistinguishable.** Invalid, expired, revoked, and never-existed all return
  the same page, so a link cannot be probed for information about your library.

## Endpoints

Anonymous, token-authorised:

| Route                         | Purpose                                            |
|-------------------------------|----------------------------------------------------|
| `GET /PublicMediaLinks/w/{token}` | Minimal player page — this is the shareable link |
| `GET /PublicMediaLinks/s/{token}` | The raw stream, with range requests for seeking  |
| `GET /PublicMediaLinks/d/{token}` | The original file as a download (can be disabled) |

Administrator-only (`RequiresElevation`): `GET`/`POST /PublicMediaLinks/Links`,
`DELETE /PublicMediaLinks/Links/{nonce}`, `POST /PublicMediaLinks/RevokeAll`.

## Usage

Dashboard → Plugins → **Public Media Links**. Search for an item, set a lifetime (default 3
hours), and create the link. Copy the share URL from the Active links list.

Set **Public base URL** to your externally reachable address (for example
`https://jellyfin.example.com`) so generated links work outside your LAN. Left blank, links
are built from whatever address you loaded the dashboard on.

## Context menu integration

Adds **Copy Public Share Link** to the item dropdown, right under "Copy media URL". Clicking
it issues a link and copies it.

This needs the [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
plugin (v3.0.0.0 or newer ships a Jellyfin 12 build). Install that first; this plugin detects
it by reflection at startup. **Everything else works without it** — if it is missing you just
get a log line and the dashboard page instead.

Jellyfin has no first-party way for a server plugin to extend the web client. File
Transformation rewrites the served jellyfin-web response in memory, so nothing on disk is
modified, server updates do not undo it, and several plugins can stack transforms on one file.

The injected script drives the **rendered action sheet DOM**, not the minified web bundle.
Patching the bundle by regex (as some plugins do) is pinned to one exact build of
jellyfin-web and breaks on every update; the DOM structure is comparatively stable.

- Only shown to administrators, since issuing a link requires elevation.
- Only shown where "Copy media URL" already is, i.e. downloadable file-backed items.
- Uses the plugin's configured default lifetime. Edit `CONFIG.ttlHours` in
  `Jellyfin.Plugin.PublicMediaLinks/Web/contextMenu.js` to override.
- Falls back to a dialog showing the URL if the browser blocks clipboard access.

Because this rides on jellyfin-web internals, `tests/web/` runs the real script against a
jsdom replica of the action sheet, covering injection, admin gating and the item-id edge
cases:

```sh
cd tests/web && npm install && npm test
```

## Direct play, not transcoding

The stream endpoint serves the original file byte-for-byte with HTTP range support. There is
no transcoding, so playback in a browser depends on the recipient's browser being able to
decode the container — MP4/H.264 generally works, MKV generally does not. The player page
shows the stream URL so the recipient can paste it into VLC or mpv, which play anything.

## Building

Requires the .NET 10 SDK (Jellyfin 12 targets `net10.0`).

```sh
dotnet test
dotnet build -c Release
```

If the .NET 10 SDK is not on your PATH, prefix with its install directory (for example
`export PATH="$HOME/.dotnet:$PATH"`).

Copy `Jellyfin.Plugin.PublicMediaLinks/bin/Release/net10.0/Jellyfin.Plugin.PublicMediaLinks.dll`
into `<jellyfin-config>/plugins/Public Media Links/` and restart the server.

## Caveats

- A share link is a bearer credential for that one item. Anyone it is forwarded to can use it
  until it expires. Keep lifetimes short.
- Links are served by Jellyfin itself, so the recipient's IP reaches your server directly.
- Plugin configuration holds the signing key. It lives in your Jellyfin config directory with
  the rest of your server's secrets; back it up accordingly.
