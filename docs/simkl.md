# SIMKL

SIMKL connects to the existing media provider system for TV series, movies, and anime.
Anime and manga remain supported through AniList and MyAnimeList. Providers only
participate in initial synchronization for media kinds they support.

## Configuration

Register an AUTH V2 **Server apps & services** application in
[SIMKL developer settings](https://simkl.com/settings/developer/).
Use browser redirect with PKCE (S256). For local Aspire development, register:

```text
https://cantaro.dev.localhost:5173/api/media/providers/simkl/callback
```

Store `Parameters:SimklClientId` and `Parameters:SimklClientSecret` in the
AppHost's local user-secrets. Aspire passes them to the API as `Simkl:ClientId`
and `Simkl:ClientSecret`. Both are required Aspire secret parameters,
matching the other media providers.

Production uses its own SIMKL app and public HTTPS callback. Configure
`CANTARO_SIMKL_CLIENT_ID` and `CANTARO_SIMKL_CLIENT_SECRET` in the deployment
workflow's GitHub secrets. Never commit the secret or user tokens.

Cantaro requests `media:read media:write`, exchanges authorization codes on the
backend, and encrypts user tokens using the existing token encryption service.
Users connect SIMKL from the media providers page.

## Progress and synchronization

The first import reads the library and episode history. Later imports check
SIMKL activity timestamps and merge changed items into an account-owned database
snapshot. When SIMKL reports removals, Cantaro reconciles the snapshot against
the remaining provider IDs. Removing an item from SIMKL does not delete the
Cantaro-owned library entry.

Conflict resolution uses each item's own update timestamps. Account-wide
activity timestamps only control incremental fetching.

The first import from a newly connected provider adds new titles and links
existing titles without replacing their Cantaro history or sending changes to
other providers. Use the explicit synchronization preview to reconcile existing
titles. Later changes from an established provider binding can update Cantaro
and the other connected providers. Queued imports retain provider provenance
and are discarded if newer canonical state has made them obsolete.

Status, score, and progress edits save the Cantaro entry and queued provider
operations together. The API returns after that database save; the background
worker sends the changes to providers and retries transient failures. Provider
responses update synchronization metadata without replacing the saved Cantaro
values. Library reads and local edits have separate rate limits from provider
metadata requests.

TV progress counts regular episodes in season/episode order. For example, after
a ten-episode first season, progress 12 means season 2, episode 2. Imported
episode history retains watched episodes beyond a gap and specials. The displayed
progress stops at the first unwatched regular episode.

For TV titles with a verified episode catalog, the episode list groups episodes
by season and shows the series-wide regular-episode number in a tooltip. The
season selector also scopes the watched-through progress controls. Selecting a
season changes only the view; saving a progress edit maps the chosen episode
back to Cantaro's overall watched-through count. Titles without a reliable
season mapping retain overall numbering.

Specials appear in a separate list without an overall episode number. They do
not change the regular watched-through count. Catalog refreshes update episode
metadata only and preserve the last cached mapping when SIMKL is unavailable.

Status and rating edits do not replace episode history. Automatic reconciliation
does not flatten a history with gaps into an episode count. Explicit progress
edits change the watched-through range while retaining separately watched episodes
and specials. An interface for selecting arbitrary watched/unwatched episodes is
deferred.

When SIMKL normalizes a requested status, Cantaro records the accepted status.
Initial synchronization recognizes that result instead of repeatedly requesting
the same normalization. Later imports preserve Cantaro's requested status when
SIMKL returns that accepted status, while still importing rating and progress
changes. A different remote status ends that normalization match.
Ratings use SIMKL's integer 1-10 scale, mapped to
Cantaro's 1-100 scale.

The browser extension still observes Crunchyroll. Netflix observation support is
a separate follow-up.

## API references

- [Authentication](https://api.simkl.org/authentication)
- [Incremental synchronization](https://api.simkl.org/guides/sync)
- [Watch history](https://api.simkl.org/guides/mark-as-watched)
