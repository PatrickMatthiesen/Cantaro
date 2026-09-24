# Browser extension permissions

Cantaro requests only the browser capabilities needed for the enabled integrations.

| Permission | Why it is needed |
| --- | --- |
| `storage` | Stores versioned settings, local consent choices, the persistent refresh session, session-only access credentials, and temporary tracking preferences. Local storage is restricted to trusted extension contexts. |
| `identity` | Runs the browser extension OAuth/PKCE sign-in flow against the configured Cantaro server. |

Host access is limited to the configured Cantaro API plus the provider pages where the extension runs:

- `www.crunchyroll.com` for rendered series episode URLs and watch progress.
- `www.youtube.com` and `music.youtube.com` for the opt-in lyrics feature, which reads the active video's ID.
- `lrclib.net` for lyrics searches using the matched song's title and artist. Album and duration stay in the extension and help rank results.

The broad HTTPS pattern is optional permission scope, not install-time access. It lets the settings UI ask the browser for one exact self-hosted Cantaro origin entered by the user. Cantaro removes the previous optional origin when the configured instance changes. Runtime-configured instances must use HTTPS; local development origins are included directly by the development build.

Cantaro does not use Crunchyroll credentials or cookies, call private Crunchyroll APIs, crawl hidden seasons, download media, or reload provider tabs. Content scripts only inspect the rendered page in the tab where they run, and only after the user has accepted the relevant consent choice and is signed in. Before that gate, after sign-out, or after revocation, they remain inactive and do not capture, queue, or transmit page data.

## Collection controls

The first-use consent screen explains the page data read, the purpose for
reading it, the configured Cantaro server that receives it, and links to the
[Privacy Policy](../../PRIVACY.md). Collection is off until the user makes an
affirmative choice. Media watch tracking and catalog collection are separate
choices. A versioned consent record is stored locally so a material change can
require renewed consent. Revoking a choice stops new collection immediately.

Music library and playlist features use the signed-in popup and account
integration. Opening a library song's lyrics sends its title and artist
directly to LRCLIB. Album and duration stay in the extension and help rank
results. The separate YouTube lyrics option reads
the active video's ID after the user enables it and signs in, then matches
that ID against the user's existing Cantaro library. The extension sends the
matched song's title and artist directly to LRCLIB. It does
not automatically send YouTube page titles, descriptions, ad text, full URLs, or browsing
history to Cantaro or LRCLIB. YouTube access is limited to those two video
sites.

After lyrics consent, the YouTube page title can prefill editable manual search fields locally. The user can search by title alone or add an artist. Entered terms go directly to LRCLIB only when the user submits the search; they do not go to Cantaro.
