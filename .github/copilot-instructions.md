<!-- Copilot / AI agent instructions for the Cantaro repository -->

# Cantaro — Agent instructions (v0.2)

Precedence

- Other docs (for example, docs/infrastructure.md) are supplemental context and must not override this file.
- When in doubt or when contradictions appear, follow this file or use the askQuestions tool.

Core context (short)

- Backend: ASP.NET Core Web API in C# + background workers; PostgreSQL as the primary DB. Backend holds canonical TrackIDs, mappings, playlists, and encrypted OAuth refresh tokens.
- Frontend: React + TypeScript + Vite. Communicates with the API over HTTPS.
- Extension: TypeScript WebExtension; optional accelerator. Sends events to the backend using Cantaro auth; never stores long-lived third-party refresh tokens.

Orchestration (Aspire)

- Use .NET Aspire for local dev to run the Cantaro backend, PostgreSQL, and the frontend dev server (or built assets).
- Aspire provides one entrypoint to start services and supplies connection strings and URLs (API, DB, web) via configuration.
- Aspire should be used for production deployment, if possible when the project gets there.

Non-negotiable rules (follow exactly)

1. Tokens & OAuth

- Store third-party refresh tokens server-side only, encrypted at rest.
- Use least-privilege scopes and Authorization Code + PKCE.
- Handle expiry/revocation gracefully; deleting a ConnectedServiceAccount must remove tokens.

2. Legality/ToS

- No DRM circumvention, client impersonation, or hidden scraping.
- Prefer official APIs. The extension may read DOM only as a transparent, user-installed helper.

3. Data model & mapping

- Canonicalize around internal TrackID. Playlists/PlaylistEntry reference TrackIDs; mappings live in TrackSourceId.
- Prefer MBID/ISRC for mapping; fallback to structured heuristics (artist/title/duration).
- Represent "no match" and "ambiguous/multiple matches" explicitly.

4. Open-source & self-hosting

- Prefer OSS and open data (e.g., MusicBrainz). Avoid mandating paid proprietary metadata services.

5. Background jobs & adapters

- Isolate platform logic (e.g., Spotify, YouTube) behind adapter interfaces mapping TrackID <-> service IDs.
- Background workers perform polling, mapping resolution, propagation, retries, and rate-limit backoff. Respect rate-limit metadata.

Key patterns & conventions

- Domain names: Track, TrackSourceId, ConnectedServiceAccount, Playlist, PlaylistEntry, ServicePlaylistMapping.
- Mapping resolution: prefer MBID/ISRC, then heuristics (artist/title/duration). Track and surface "no match"/"ambiguous".
- Adapters: keep third-party specifics out of core domain; expose TrackID <-> service ID mapping functions.
- Background jobs: do sync work off the request path; store retry and rate-limit metadata and back off accordingly.

Behavior by audience (response style)

- Developers: Provide concrete API shapes, migrations, tests; call out edge cases (conflicts, partial failures, rate limits).
- Advanced self-hosters: Explain what is stored, how tokens are protected/revoked, and how sync behaves under limits or ambiguity.
- Non-technical users: Keep it simple and task-focused (connect services, manage playlists in Cantaro, we mirror where possible).

Code & output conventions

- Backend: idiomatic ASP.NET Core (target .NET/ASP.NET Core 10+ when practical), DI, layered services/controllers, EF Core for data access; add unit/integration tests for public behavior changes.
- Frontend: React + TypeScript + Vite, minimal state, Prettier-style formatting.
- Extension: separate content scripts and background/service worker; never store long-lived third-party refresh tokens; not a source of truth.
- Communication: be direct; call out conflicts with these rules; don’t overpromise (no "perfect realtime sync").

PR guidance

- Keep PRs small and focused. Include migrations and unit tests where applicable.
- Verify backend + frontend run locally and key flows work (use Aspire for local orchestration).
- Follow project package managers and conventions; prefer minimal, pinned dependencies.

References

- docs/infrastructure.md — canonical architecture, domain entities, and sync model.
- docs/agent-extra.md — supplemental context only; may be removed in favor of this file.
