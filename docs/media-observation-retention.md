# Media observation data lifecycle

Issue #153 replaces serialized browser requests with structured matching evidence.
Both the watch and catalog endpoints store `MediaObservation` rows in this branch;
there is no separate `MediaCatalogObservation` table. Provider `RawMetadata` and
music `TrackObservation` data are separate concerns.

## Consumer inventory

| Consumer | Evidence required | Reason |
| --- | --- | --- |
| `MediaObservationMatchingService` | Series and season titles, provider series/season IDs, season and episode numbers, observed episode numbers | Search canonical titles, parse season episode ranges, apply provider season mappings, check identity compatibility, and infer cumulative episode offsets. |
| `MediaEpisodeIdentityService` | Provider IDs, episode title/number/sequence, season identifiers, release track, next episode identity/destination, observed episode destinations and language codes | Resolve playable variants to canonical episodes, preserve next-episode links and language availability, and detect conflicting assignments. |
| `MediaObservationsController` | Series title, season/series identifiers and season number | Build provider-search queries and apply stored/manual episode offsets. |
| Catalog ingestion | Explicit catalog discriminator and normalized episode evidence | Reprocess unmatched catalog evidence without advancing watch progress. |
| Observation responses and export | Existing observation identity, status, progress, timestamps and structured evidence | Show and export the user's retained observation and resolution state. |

The main observation already stores its site, URL, site ID, observed title,
progress hint, observation timestamp and extension version. These values are not
duplicated in another request body. Series, season and episode titles retain
their distinct roles in matching and canonical episode naming. Playback position,
duration, watch percentage and unused
request fields do not need durable storage after ingestion. A bounded child
collection stores episode evidence instead of a second serialized request.

Provider choices and resolution history are generated workflow state, not raw
extension requests. They remain attached to the observation and expire with it.
The server does not replay a stored HTTP request; matching and user resolution
operate directly on structured evidence.

## URLs and diagnostics

Observation page, series, next-episode and episode destination URLs lose their
query string and fragment before extension delivery and again at server
ingestion. Canonical playable destinations continue to use the provider allowlist
and provider-relative paths. Production observation diagnostics use bounded
identifiers and status information rather than raw request bodies or exception
messages containing upstream URLs. Hosting access logs are controlled separately
by the server operator.

## Retention, export and deletion

Media observations and their candidate, episode-evidence and resolution data
expire after 30 days without a server-side update. Cleanup runs at startup and
hourly. The server timestamp is used so a client cannot extend retention by
submitting a future observation timestamp. Resubmitting an observation refreshes
its evidence and retention window. Legacy rows with an unset server update use
their creation timestamp for the same cutoff. Once evidence expires, matching
requires a fresh observation; the extension has no offline replay queue.

Account export includes the requesting user's retained structured evidence and
current resolution state. Generated search choices, matching candidates and the
internal resolution audit JSON are omitted from the archive. Account
deletion cascades to their observations and dependent evidence immediately in the
live database. Library state and per-user episode offsets remain user-scoped and
are deleted with the account; observation expiry alone does not erase watch
progress or those settings.

Canonical titles, provider title/season mappings, canonical episodes, playable
provider identities and language availability are shared catalog data. They have
no observation owner and remain until explicitly maintained or removed, including
after source observations expire or an account is deleted. They are not a user's
viewing history and are not exported as private observations. No raw request body
is retained to establish their provenance. Backups and operational logs follow
the operator's separate retention policy.

Untrusted inferred episode identities can be corrected by later matching.
Automatic evidence cannot move a trusted or user-confirmed identity: a
disagreement marks the identity conflicted and the observation ambiguous, keeps
it in the matching review queue, and prevents automatic watch-progress updates.
Explicit resolution in that queue may confirm the old assignment or accept a
new assignment and clear the conflict.

## Migration and verification

The migration adds structured storage, backfills the values consumed above from
both historical request shapes, sanitizes stored URLs, and only then drops
`RawPayload`. Missing or malformed legacy JSON contributes no structured evidence,
as it did when runtime deserialization failed. Existing canonical matches,
episode identities, trust/conflict flags, library progress and resolution state
are preserved. The migration is transactional; a failed backfill must not leave
the raw column removed without its replacement data.

Deploy the migration and API together with ingestion stopped during the upgrade;
the old API cannot write to a schema without `RawPayload`. Rollback cannot restore
discarded request fields. Keep operator-managed backups according to their
retention policy, and validate the upgrade on a disposable database before
deployment.

Run `./scripts/Test-MediaObservationMigration.ps1` with Docker available and the
Cantaro AppHost stopped. It creates a disposable PostgreSQL 17 instance, applies
the complete preceding migration chain, seeds watch/catalog/malformed legacy
requests plus a trusted canonical episode identity, applies the new migration,
and verifies structured evidence, sanitized URLs, match preservation and account
deletion cascades. It then removes only its own disposable database container.
