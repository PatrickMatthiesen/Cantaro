# Cantaro - Infrastructure Overview (v0.1)

Cantaro is an open-source, self-hostable “music identity and playlist brain”
that unifies your playlists and track mappings across multiple streaming
services.

It acts as the canonical vessel that holds your music graph and pours it into
Spotify, YouTube/YouTube Music, and other platforms as synchronized playlists,
while keeping your preferences, mappings, and tags under your control.

## Goals

- Provide a single, canonical representation of tracks and playlists.
- Sync playlists across multiple music platforms with minimal friction.
- Keep the system open-source, self-hostable, and privacy-respecting.
- Be robust against third-party changes by:
  - Using multiple metadata sources.
  - Maintaining our own stable internal identifiers.

## High-Level Architecture

- Core backend service:
  - Technology: ASP.NET Core 10+ (C#).
  - Responsibilities:
    - Expose a JSON HTTP API for the web app, extension, and future clients.
    - Handle OAuth flows with third-party music services.
    - Store encrypted OAuth refresh tokens for background sync (where allowed).
    - Maintain canonical entities:
      - Users and connected service accounts.
      - Tracks with an internal TrackID.
      - Mappings to external IDs (Spotify, YouTube, etc.).
      - Mappings to open metadata sources (MusicBrainz, others).
      - Playlists and playlist membership.
    - Run background jobs for:
      - Cross-service sync.
      - Track mapping resolution.
      - Retry/backoff handling and monitoring.
    - Implement mapping logic using:
      - MBIDs, ISRCs, and other strong identifiers where possible.
      - Structured fuzzy matching with clear fallbacks where necessary.

- Web frontend:
  - Technology: React + TypeScript + Vite.
  - Responsibilities:
    - User registration/login for Cantaro.
    - Connect and disconnect third-party services.
    - Manage unified playlists (create, import, edit).
    - Display mapping and availability state per track and service.
    - Provide manual “Sync now” actions and conflict resolution tools.
  - Communicates with the backend over HTTPS using the public API.
  - Lives in the same repository as the backend; built and optionally served
    by it in production.

- Browser extension:
  - Technology: TypeScript + WebExtension APIs
    (optional React/Preact for popup UI).
  - Responsibilities:
    - Detect playlist-related events on supported web UIs
      (e.g. track added to a playlist).
    - Send events to Cantaro’s backend using the user’s Cantaro auth.
    - Accelerate near-real-time sync while the user is active.
  - Design:
    - Optional but recommended.
    - Never the single source of truth.
    - Does not store long-lived third-party refresh tokens; those remain on the
      backend only.

- Data storage:
  - Primary DB: PostgreSQL.
  - Stores:
    - Cantaro user accounts and auth data.
    - ConnectedServiceAccounts and encrypted OAuth tokens.
    - Tracks and identifier mappings.
    - Playlists and playlist entries.
    - ServicePlaylistMappings and sync logs.
    - Rate-limiting and backoff metadata.
  - Future option:
    - Run a local MusicBrainz mirror or cache to improve resilience,
      performance, and independence.

- Metadata and external data sources:
  - MusicBrainz:
    - Primary open metadata and ID backbone when available.
    - Respect licensing (core data CC0, others CC BY-NC-SA) and project
      guidelines.
    - Use either public API (with care) or a self-hosted mirror.
  - Additional open sources (optional, pluggable):
    - Discogs, ListenBrainz, etc., as read-only metadata inputs.
  - Streaming platform APIs:
    - Spotify Web API:
      - Use Authorization Code with PKCE / modern recommended flows.
      - Store refresh tokens encrypted; use them for background sync.
    - YouTube Data API:
      - Use standard OAuth 2.0 flows.
    - Additional platforms later (Apple Music, Deezer, etc.) using the same
      adapter pattern.

- Security and privacy:
  - All external communication over HTTPS in real deployments.
  - OAuth tokens:
    - Stored only on the Cantaro backend.
    - Encrypted at rest using ASP.NET Core Data Protection or a dedicated KMS.
    - Scoped to the minimum required permissions.
    - Revocation:
      - Users can disconnect a service; Cantaro deletes stored tokens.
  - Cantaro auth:
    - Standard, battle-tested mechanism (e.g. cookies or JWT).
  - No DRM-breaking or ToS-violating behavior by design.

- Deployment and operations:
  - Initial target:
    - Single-node deployment (e.g. home server).
    - Components:
      - Cantaro API (ASP.NET Core).
      - React frontend static assets.
      - PostgreSQL.
      - Reverse proxy (nginx/Caddy/Traefik).
  - Production-ready path:
    - Containerized deployment on any major cloud.
    - Separate services for API and background workers.
    - Secrets and configuration externalized from code.
    - Horizontal scaling for stateless components when needed.

## Core Domain Entities (v0.1)

- User
  - Internal Cantaro user account.
  - References to connected third-party service accounts.

- ConnectedServiceAccount
  - user_id
  - service (spotify, youtube, etc.)
  - encrypted_refresh_token (and any required metadata)
  - scopes and expiration
  - created_at / updated_at

- Track
  - id (internal TrackID, canonical within Cantaro)
  - canonical metadata snapshot (artist, title, duration, etc.)
  - mbid_recording (nullable)
  - isrc (nullable)
  - created_at / updated_at

- TrackSourceId
  - track_id
  - source_type (spotify, youtube, musicbrainz, discogs, etc.)
  - external_id
  - confidence / origin metadata
  - last_verified_at

- Playlist
  - id
  - user_id
  - name
  - description
  - metadata (tags, visibility, custom fields)
  - created_at / updated_at

- PlaylistEntry
  - id
  - playlist_id
  - track_id
  - position
  - added_at
  - source_service (optional: where it was first observed/added)

- ServicePlaylistMapping
  - playlist_id
  - service
  - service_playlist_id
  - sync_mode (from_cantaro, bidirectional, import_only)
  - last_synced_at
  - last_sync_status

## Sync Model (Summary)

- Cantaro is the source of truth for unified playlists.
- External playlists are:
  - Imported into Cantaro.
  - Or created/managed by Cantaro and mirrored out.

Sync flow (high level):

- Detection:
  - For Cantaro-managed playlists:
    - Backend knows intended state; pushes changes to connected services.
  - For playlists linked to external sources:
    - Poll provider APIs at controlled intervals using:
      - snapshot_id / ETag / updated timestamps where supported.
    - Optionally augment with browser extension events for faster detection.
- Normalization:
  - New/changed items from any source are:
    - Normalized into TrackIDs using strong identifiers (MBID/ISRC) where
      possible.
    - Fallback matched via structured heuristics when needed.
- Propagation:
  - For each connected service:
    - Resolve TrackID -> service-specific track ID.
    - If found, update that service’s playlist to match Cantaro’s canonical
      playlist.
    - If not found, mark as unavailable for that service.
- Resilience:
  - Handle rate limits and transient failures with retry/backoff.
  - Detect invalid/expired tokens and prompt user for re-auth.
  - Log discrepancies and expose them through the UI for debugging.

Cantaro does not guarantee every track exists everywhere. It guarantees that:

- The canonical definition of your playlist is preserved.
- Any feasible, ToS-compliant sync to connected platforms is attempted.
- Gaps and conflicts are visible and reviewable, not hidden.