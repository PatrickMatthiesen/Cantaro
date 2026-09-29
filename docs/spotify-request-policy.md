# Spotify requests and retries

Reviewed against Spotify's official documentation on 26 September 2026.

Spotify's [rate-limit guidance](https://developer.spotify.com/documentation/web-api/concepts/rate-limits)
describes an app-wide rolling 30-second window and a `Retry-After` header in seconds.
The window is not the cooldown duration. Cantaro must honor the full supplied delay,
including a delay of several hours. Spotify does not publish a fixed request budget
that makes any particular batch size safe.

[Development quotas](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)
are separate from rate limits. Endpoint groups share quota buckets, and a quota response
has `error.reason = "QUOTA_EXCEEDED"`. Spotify's
[July 2026 update](https://developer.spotify.com/blog/2026-07-23-web-api-quota-updates)
says development apps under the same developer account share a quota even if their
client IDs differ. Cantaro preserves this reason instead of classifying every 429 as
an ordinary rate limit.

## Request policy

- Every Spotify Web API read and mutation checks a database-backed gate before sending.
  The gate shares request pacing and cooldown deadlines across API instances using the
  same Cantaro database. A restart does not reset either deadline.
- A response can extend a cooldown but cannot shorten a later deadline already recorded.
  Active cooldowns defer work immediately; HTTP requests do not sleep for hours.
- The initial upgrade carries forward the latest existing Spotify playlist cooldown.
  It cannot recover the original quota reason if the previous code discarded it.
- Requests start at least one second apart by default. This configurable interval is a
  conservative Cantaro setting, not a published Spotify allowance. Requests already in
  flight when a 429 arrives cannot be recalled.
- Missing, invalid, zero or expired retry headers use increasing delays. Ordinary limits
  start at 30 seconds; quota limits start at five minutes. Fallbacks double up to one hour.
  The cap applies only to Cantaro's fallback, never to Spotify's supplied delay.
- A successful request after the cooldown resets the ordinary fallback streak. Quota
  streaks survive unrelated successful requests because Spotify does not expose which
  endpoint buckets share the quota. A 24-hour quiet period resets that streak. These
  fallback intervals and the quiet period are local recovery choices, not Spotify quota
  sizes or reset schedules.
- Playlist jobs persist their retry time and matching progress. Quota and rate-limit
  deferrals do not consume the ordinary failure budget, and manual sync cannot bypass
  a provider cooldown.
- Logs record a received 429's reason, retry header, effective delay and deadline.
  They do not include access tokens, authorization headers, search queries or response bodies.
- Automatic HTTP resilience retries are disabled for this client. Playlist mutation
  recovery starts by reading the remote state, so an ambiguous response does not cause
  a blind duplicate append.

Spotify [recommends exponential backoff and respecting Retry-After](https://developer.spotify.com/documentation/web-api/tutorials/building-with-ai).
The limits above control requests, not the number of tracks in a matching batch. A track
can need several search queries. Matching batches limit job duration and retain progress;
they are not a substitute for request pacing.

OAuth token requests use `accounts.spotify.com`, separately from the Web API. They retain
bounded token retries with exponential fallback and honor seconds or HTTP-date retry
headers. A Web API cooldown does not prevent reconnecting an account.

## Avoiding repeated requests

- Destination matching validates the linked account locally. It no longer requests
  `/me` for every track. Playlist operations reuse a profile validation within their
  service scope only while the user, account, external identity and token stay the same.
- Catalogue search evaluates candidates after each query and stops once the shared
  matching engine accepts a recording and the source interpretation is unambiguous.
  Conflicting artist/title interpretations still require their alternative searches.
- Successful application-token catalogue queries are stored in the database by exact
  query and result limit. Workers and playlists reuse those results for seven days,
  or one day for an empty result. Known accepted track identities remain durable and
  are checked before searching. User-token searches are excluded because their results
  can depend on the user's market.
- Cache hits need neither a token request nor a Spotify Web API request. Failed HTTP
  requests and quota responses are never stored as empty results. Each successful query
  is saved separately, including when a later query fails or the job is cancelled after
  its response. Expired cache rows are pruned on cache misses at most hourly per process.
- Concurrent identical queries are coalesced within an API process. Completed results
  are shared through the database across processes; simultaneous first requests from
  separate processes can still both reach Spotify. The global request gate still applies.
- Unchanged playlists reuse their final freshness read. A write or rename still requires
  a separate verification read.

The settings bind from `Spotify:RequestGate`. The defaults are:

```json
{
  "RequestInterval": "00:00:01",
  "MissingRetryAfterBaseDelay": "00:00:30",
  "QuotaMissingRetryAfterBaseDelay": "00:05:00",
  "MissingRetryAfterMaxDelay": "01:00:00",
  "BackoffResetQuietPeriod": "1.00:00:00"
}
```

## Deployment boundary

The database gate coordinates instances sharing one database. Separate development and
production databases cannot observe each other's cooldowns, even if Spotify counts their
traffic against the same developer quota. Use a shared coordination store before running
independent installations against a shared quota with an expectation of global pacing.
