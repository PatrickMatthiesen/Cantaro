# Version-aware song model

## Purpose

Cantaro currently treats each canonical `Track` as an isolated song. In the
version-aware model, a `Song` is the underlying musical work as Cantaro
understands it, while a `Track` is a particular musical version or recording of
that song. A provider item remains a `TrackSourceId`: it is one provider's
presentation of that version.

The model deliberately separates two decisions:

1. Which musical version does this item represent?
2. Which provider presentation should be used for that version?

This prevents a YouTube lyric video, visualizer, or music video from being
mistaken for an acoustic, live, instrumental, remix, or other musical version.

## Invariants

- Cantaro IDs are authoritative. MusicBrainz Work/Recording IDs, ISRCs,
  provider metadata, parsers, and user decisions are evidence; none is a
  mandatory identity.
- A `Song` may temporarily have no `Track` versions. Database cardinality cannot
  prevent removal of the final Track, so later domain operations must either
  remove/tombstone the empty Song or retain it deliberately for audit history.
  During rollout, `Track.SongId` remains nullable so old and new application
  versions can coexist safely.
- A `Track` belongs to at most one `Song`.
- A `Track` may carry multiple controlled version traits. Traits are rows, not
  one exclusive enum, because combinations such as live + acoustic are valid.
- Every inferred trait or grouping retains confidence and provenance. Low or
  conflicting confidence creates a review suggestion; it never silently
  rewrites canonical identity.
- `TrackSourceId` remains the unique provider/external-ID lookup and the
  provider-link collection for a `Track`. Presentation metadata enriches that
  entity rather than introducing a parallel provider hierarchy.
- Playlist entries continue to reference exact `Track` versions. Song-level
  substitution is a sync-time policy and must not rewrite the user's canonical
  playlist entry.
- No lineage edge is added until a concrete consumer needs it. Remix-of,
  edit-of, samples, and mashup relationships can later use a flexible
  `TrackRelation`; `DerivedFromTrackId` is intentionally excluded.

## Target entities

### Song

`Song` is a Cantaro-owned grouping identity. The first phase gives it only an
ID and timestamps. Canonical title/credits and external-work evidence are
deferred until grouping workflows define how conflicts, aliases, and manual
overrides are represented; copying a title from an arbitrary version during
migration would turn uncertain evidence into canonical data.

### Track

`Track` keeps its current recording-level metadata, MusicBrainz Recording ID,
ISRC, artist credits, provider links, observations, and playlist entries. It
gains nullable `SongId` plus a navigation to `Song`.

### TrackVersionTrait (later phase)

Each assertion associates a controlled trait key with a `Track`. Its eventual
shape includes `Id`, `TrackId`, `TraitKey`, confidence constrained to `[0, 1]`,
evidence source/method, model or rule version, optional evidence reference,
`CreatedAt`, and `RevokedAt` or `SupersededBy`. A partial unique index prevents
duplicate active assertions from the same evidence source and method. Initial
controlled keys should cover at least `original`,
`acoustic`, `orchestral`, `live`, `instrumental`, `a-cappella`, `remix`,
`cover`, `edit`, and `demo`. The schema must permit several distinct traits per
track. The service layer, not a database enum, owns vocabulary evolution and
explicit compatibility/conflict rules. `original` is an affirmative claim and
must never be inferred merely from the absence of other markers.

### TrackSourceId presentation (later phase)

Presentation metadata is nullable and provider-neutral where possible. Its
orthogonal dimensions are stored separately: YouTube presentation kind can be
music video, lyric video, cover-art audio, visualizer, or live video, while
uploader authority can be official or user. This permits an official lyric
video rather than forcing one exclusive label. Each inferred dimension retains
confidence and provenance. Spotify usually remains audio. Existing uniqueness
on `(SourceType, ExternalId)` and its indexed lookup path are preserved.

### Membership and compatibility

`Song` is composition/work-level in Cantaro's own graph. Covers, remixes, and
edits may be grouped with the composition when accepted evidence supports that
membership, but membership alone never means two Tracks are substitutable.
Version traits plus explicit user policy determine compatibility. Mashups are
separate Songs and can later be connected through `TrackRelation` when a real
consumer requires lineage.

## Reviewable delivery phases

1. **Identity foundation (this PR).** Add `Song`, nullable `Track.SongId`, an
   index and restrictive foreign key, and an idempotent migration backfill that
   creates exactly one deterministic Song for every existing unassigned Track.
   It never groups two Tracks. Keep APIs, matching, playlists, recognition, and
   sync behavior unchanged.
2. **Trait evidence.** Add controlled `TrackVersionTrait` assertions with
   confidence and provenance, parsing/classification services, and review-safe
   read models. Do not group Tracks automatically.
3. **Provider presentation.** Add nullable presentation classification and
   provenance to `TrackSourceId`, retaining provider/external-ID uniqueness and
   lookup indexes. Classify YouTube presentations without changing sync choice.
4. **Grouping suggestions and review.** Build candidate Songs using
   MusicBrainz Work/relationships, normalized title and artist credits, version
   markers, ISRC, provider metadata, and manual evidence. Expose accept/reject
   review and auditability before any automatic grouping.
5. **Trustworthy grouping.** Apply accepted suggestions transactionally. A
   merge keeps one immutable Song ID, moves Tracks, records redirects/tombstones
   for retired IDs, and retains the decision evidence. A split creates a new
   Song and records reversible reassignment provenance. Define this policy
   before Song IDs become public contracts. Make `Track.SongId` required only
   after production telemetry confirms no unassigned Tracks and all writers
   assign Songs.
6. **Version-aware sync.** Resolve in order: exact version with preferred
   presentation; exact version with another presentation; preferred compatible
   version within the Song; substitution only when the user's settings allow
   it. Preserve the playlist's exact canonical `TrackId`.
7. **Preferences and UI.** Add exact-version/substitution settings and YouTube
   presentation preferences, then expose Song groups and their versions without
   overloading today's track-shaped `MusicLibrarySongDto` contracts.
8. **Optional relationships.** Add `TrackRelation` only when remix/edit/sample
   lineage is required by sync or UI behavior.

## Phase 1 migration and compatibility

The schema change is additive. The migration creates `Songs`, adds nullable
`Tracks.SongId`, and backfills only rows where `SongId` is null in the
migration's transaction snapshot. A namespaced,
deterministic UUID derived from each Track ID makes the data operation safe to
repeat without creating duplicate Songs. Song timestamps are copied from the
Track so the migration does not invent lifecycle history.

The foreign key uses `RESTRICT`: deleting a Song must be an explicit domain
operation and must not cascade-delete recorded versions. Deleting a Track does
not delete its Song, because later phases may group several versions under one
Song. An index on `Tracks.SongId` supports version enumeration.

New or old writers remain allowed to create a Track without a Song during the
transition; applying an EF migration again does not sweep those later rows. A
separately rerunnable repair/backfill operation is therefore required before
the column becomes non-nullable. That phase will first centralize Track creation
and assign a Song in application code.

## Verification gates

- Model tests prove a Song can own multiple Track versions and deletion is
  restrictive.
- A PostgreSQL upgrade test migrates from the previous schema, seeds Tracks,
  applies phase 1, executes the set-based backfill again, and verifies
  deterministic one-per-Track IDs, timestamps, idempotence, 1:N cardinality,
  and restrictive deletion. SQLite model tests supplement but do not replace
  this provider-specific test.
- The complete backend test suite runs with Aspire stopped.
- Frontend `bun run check` and final `bun run check:fallow` remain clean even
  though phase 1 intentionally changes no client contract.
- Matching, playlist uniqueness, recognition, and source-ID lookup tests remain
  unchanged and passing.
