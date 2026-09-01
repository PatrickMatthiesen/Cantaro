# Browser extension permissions

Cantaro requests only the browser capabilities needed for the enabled integrations.

| Permission | Why it is needed |
| --- | --- |
| `storage` | Stores versioned settings, the persistent refresh session, session-only access credentials, and temporary tracking preferences. Local storage is restricted to trusted extension contexts. |
| `identity` | Runs the browser extension OAuth/PKCE sign-in flow against the configured Cantaro server. |
| `scripting` | Notifies an already-open Cantaro web page after the extension updates media progress so the page can refresh the user's visible library state. |

Host access is limited to the configured Cantaro API plus the provider pages where the extension runs:

- `www.crunchyroll.com` for rendered series episode URLs and watch progress.

The broad HTTPS pattern is optional permission scope, not install-time access. It lets the settings UI ask the browser for one exact self-hosted Cantaro origin entered by the user. Cantaro removes the previous optional origin when the configured instance changes. Runtime-configured instances must use HTTPS; local development origins are included directly by the development build.

Cantaro does not use Crunchyroll credentials or cookies, call private Crunchyroll APIs, crawl hidden seasons, download media, or reload provider tabs. Content scripts only observe the rendered page in the tab where they run.
