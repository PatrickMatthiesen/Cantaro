# API rate limiting

Cantaro applies API rate limiting in the API process. The limiter protects account abuse and expensive work. It is not a replacement for an edge rate limit or distributed coordination between multiple API replicas.

The current limits are:

| Request group | Limit | Partition key |
| --- | --- | --- |
| Identity registration, login, recovery, confirmation, and Google authentication | 10 requests per minute | Direct client IP |
| Browser extension authorization and token endpoints | 30 requests per minute | Direct client IP |
| General `/api` requests | 120 requests per minute | Authenticated user ID, otherwise direct client IP |
| POST sync, matching, provider import, and initial-sync operations | 20 requests per minute and 2 concurrent requests | Authenticated user ID, otherwise direct client IP |

Rejected requests receive HTTP 429 and a `Retry-After` header. GET provider import-event streams stay in the general API group so a long-lived stream does not consume an expensive-operation concurrency permit.

The API does not trust forwarded client IP headers by default. When the API is behind a local reverse proxy or tunnel that forwards `X-Forwarded-For`, configure each proxy address explicitly with `RateLimiting:TrustedProxies:0`, `RateLimiting:TrustedProxies:1`, and so on. The middleware accepts one forwarded hop and ignores headers from any peer that is not in that list. Do not populate this setting with broad networks or user-controlled values.

For multiple API replicas, configure a shared edge limiter as well. These in-process counters are independent on each replica and reset when the API process restarts.

For Aspire and GitHub deployment, `TrustedProxyIp` / the Production environment secret `CANTARO_TRUSTED_PROXY_IP` accepts a comma-separated list of explicit IPv4 or IPv6 addresses. For example: `192.168.1.151,192.168.1.152`. Replace the second address with the actual tunnel connector source address seen by the API. A single address still works. Whitespace and empty entries are ignored, duplicates are removed, and invalid addresses or CIDR ranges fail startup. An empty value disables forwarded-header trust. Each indexed backend configuration entry also accepts this format.
