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
