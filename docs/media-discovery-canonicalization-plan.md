# Media discovery canonicalization plan

## Goal

Save provider search results in the shared Cantaro catalog, even when nobody opens
them. Return one result when provider identifiers establish that multiple results
describe the same work. Do not add discovered titles to a personal library.

## Implementation

1. Preserve public search metadata and cross-provider references in search results
   and the search cache. Request MyAnimeList IDs in the existing AniList search
   query and retain IDs returned by SIMKL.
2. Resolve identities in a batch, using existing provider links and cross-provider
   references. Reuse or create a `MediaTitle` and persist safe `MediaProviderLink`
   records. Keep conflicting works separate and preserve richer stored metadata.
3. Canonicalize discovered results before returning them. Return Cantaro IDs and
   routes and overlay only the current user's library state. Local search must
   find saved results without requiring a detail-page visit.
4. Reuse recent provider search results, including compatible cached result limits.
   Do not treat failed or truncated searches as complete catalog coverage.
5. Keep full detail, episode, and availability requests outside the search request.
   Reload displayed canonical metadata after successful detail-page hydration,
   preserving unsaved viewer edits and preventing reload loops. Do not merge
   results on title and year alone.
6. Test identity conflicts, provider order, transitive references, repeat searches,
   cache reuse, summary metadata, personal-state isolation, and concurrent writes.
   Run relevant backend tests with Aspire stopped and run the required frontend
   checks. Report measured performance evidence without inventing timings.

## Boundaries

Reuse the existing catalog schema unless review identifies a necessary addition.
Do not automatically merge existing titles with histories. Preserve unrelated
AppHost changes, experiments, and test artifacts. Open a scoped pull request.

## Review

Astra reviewed the plan before implementation. The implementation incorporates
these findings:

- Explicit discovery must reach the provider cache or provider even when local
  results fill the page. Persist every result returned by the bounded provider
  search contract before display truncation.
- Validate complete identity components, including all links on existing titles.
  Preserve exact mappings and leave ambiguous references unassigned.
- Use isolated database contexts and bounded retries for identity constraint races.
  Preserve local results if catalog persistence fails.
- Preserve populated metadata and progress semantics. A richer lower-priority
  detail response may fill missing metadata without replacing authoritative data.
- Reuse cached coverage without assuming that a short response is exhaustive.
  Coalesce fetches without capturing another request's database context.
- Defer background enrichment. Missing identifiers cannot establish equivalence,
  and a lookup alone cannot safely merge existing titles with histories.

The existing schema supports this change. The reported AniList/SIMKL pair needs
live identity evidence before it can be claimed as verified; synthetic regression
coverage must remain distinct from a production-data repair.

Astra also reviewed the implementation. Its findings led to conditional summary
updates through an isolated EF concurrency model, catalog-only detail hydration,
and per-key cache gates. Astra rechecked those fixes and found no unresolved issue
within that review scope.

## Verification

- The full backend suite passed all 1,179 tests with the local PostgreSQL test
  database enabled. PostgreSQL acceptance tests cover concurrent identity claims,
  interleaved detail updates, complete local synonym search, and batch command
  counts.
- Twenty new summaries used two database commands. Revisiting those summaries
  used one read and no writes. These measurements exclude provider requests.
- Web and shared-client checks passed, including delayed hydration regression
  tests. Web Fallow passed. Shared-client Fallow reports existing duplication and
  complexity findings; those broader refactors are outside this change.
