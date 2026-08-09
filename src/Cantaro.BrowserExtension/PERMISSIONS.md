# Browser extension permissions

Cantaro requests only the browser capabilities needed for the enabled integrations.

| Permission | Why it is needed |
| --- | --- |
| `storage` | Stores versioned settings, the extension OAuth session, temporary tracking preferences, and the bounded offline-delivery queue. |
| `tabs` | Lets the popup identify the active tab and ask that tab's content controller for its local context. Cantaro does not broadcast context requests to every tab. |
| `identity` | Runs the browser extension OAuth/PKCE sign-in flow against the configured Cantaro server. |

Host access is limited to the configured Cantaro API plus the provider pages where the extension runs:

- `www.crunchyroll.com` for rendered series episode URLs and watch progress.
- `youtube.com`, `www.youtube.com`, and `music.youtube.com` for music context and optional lyrics.
- `open.spotify.com` for the isolated Spotify integration placeholder.

The broad HTTP/HTTPS patterns are optional permissions. They are used only when the user configures a Cantaro API origin that was not included at build time; the settings UI asks the browser for that exact origin.

Cantaro does not use Crunchyroll credentials or cookies, call private Crunchyroll APIs, crawl hidden seasons, download media, or reload provider tabs. Content scripts only observe the rendered page in the tab where they run.
