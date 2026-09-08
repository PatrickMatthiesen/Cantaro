# Browser extension architecture

The extension is organized by feature. WXT entrypoints only start a feature; they do not own business logic or shared state.

```text
entrypoints/                 WXT bootstraps only
app/                         popup shell, pages, and settings UI
features/media/
  contracts/                 typed catalog and watch messages
  content/providers/         provider DOM observation and tab-local controllers
  background/                authenticated media delivery
features/music/
  contracts/                 typed tab context
  content/providers/         provider page integration
  popup/                     music library operations used by the popup
platform/
  api/                       authenticated Cantaro HTTP client
  auth/                      extension OAuth session lifecycle
  background/                runtime message router
  diagnostics/               structured logging
  messaging/                 shared envelopes and active-tab protocol
  settings/                  versioned settings storage and host permissions
  storage/                   restricted extension storage access
```

## Runtime flow

Each supported page has its own content controller and snapshot. Nothing copies the "latest tab" into global storage.

1. A content controller first checks the local, versioned consent gate and the authenticated session. Until the relevant media purpose is enabled and the user is signed in, it does not read, capture, queue, or transmit supported-page data. Once enabled, it reads only its current document and keeps its extraction/tracking status in that tab.
2. When the popup opens, it asks the active tab for `tab.context.get`. That tab's content controller replies directly.
3. Catalog evidence uses `media.catalog.submit`; watch progress uses `media.watch.submit`; manual watch matching uses `media.watch.resolve`.
4. The background worker owns refresh-token exchange and forwards media messages. Popup and content clients request an access token from it, so separate extension contexts cannot rotate the same refresh token concurrently. It does not own a current tab, current media item, or resolution overlay.
5. The background worker forwards only authenticated, consented media messages. A signed-out or disabled feature never creates a delivery. Failed media deliveries are discarded rather than retained for offline replay; signing in again does not retroactively collect pages that were visited while signed out.
6. The configured Cantaro API owns observations, matching, episode identities, destination trust, and deduplication.

Consent is a local extension concern. The first-use flow explains the data read,
each media purpose, the configured server destination, and the Privacy Policy
before enabling collection. Watch progress and catalog collection have separate
choices. A material collection change increments the consent version and pauses
the affected purpose until it is accepted again. Revocation clears pending
delivery state and takes effect for newly observed pages immediately.

Catalog collection and watch progress intentionally have separate backend endpoints. A catalog batch that is still waiting for a media-title match is a successful `pending_match` delivery; it must not open the watch-resolution UI or advance progress.

If an accepted watch observation needs manual matching, the API remains the
durable owner of that unresolved observation. The extension does not replay
page observations from local storage or store a global "latest resolution",
because that would let one provider tab overwrite or display another tab's
state. Pending-review recovery belongs in a backend-backed review surface
rather than tab-agnostic extension storage.

## State ownership

| State | Owner |
| --- | --- |
| Rendered DOM, extracted provider IDs, tracker status | Content controller in that tab |
| Current popup page and transient notices | Popup React tree |
| API/web origins and feature preferences | `cantaro.settings.v1` |
| Consent version and media collection choices | Versioned local settings |
| Refresh token and signed-in email | `cantaro.refresh-session.v2` in trusted local storage |
| Short-lived access token | `cantaro.access-session.v2` in browser session storage |
| Observations, matching, destinations, seen counts | Cantaro API/database |

The background service worker can stop at any time under Manifest V3. Durable
state therefore lives in extension storage or the backend, while transient
controller state remains reconstructible from its page. Media observations are
not an offline queue: if collection is disabled, the user is signed out, or a
delivery fails, the page data is not retained for later replay.

## Adding a provider feature

1. Add the smallest WXT entrypoint and provider-specific controller under the relevant feature.
2. Keep DOM selectors and parsing inside that provider folder.
3. Expose a typed tab snapshot through `tab.context.get`.
4. Add a feature contract before adding a new background message.
5. Route server calls through the authenticated API client; do not fetch from popup/content modules directly.
6. Add focused parser/controller tests and update [PERMISSIONS.md](./PERMISSIONS.md) when a host or browser permission changes.
