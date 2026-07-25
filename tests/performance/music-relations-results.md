# Music relation query baseline

Measured on 2026-07-26 with PostgreSQL 18.3 in the local Aspire environment.
The repeatable harness is `music-relations.sql`.

Synthetic data:

- 10,000 Songs
- 100,000 Tracks and SongTrack memberships
- 100,000 provider source IDs
- 100,000 artist credits
- 10,000 playlist entries
- one representative Track belonging to two Songs

## Plans

| Query | Plan | Execution |
| --- | --- | ---: |
| Exact `(SourceType, ExternalId)` provider lookup | unique index scan | 0.025 ms |
| Track with one credit and one provider source | nested indexed joins | 0.042 ms |
| Ten versions for one Song | primary-key bitmap index scan | 0.027 ms |
| Two Songs for one Track | reverse index-only scan | 0.037 ms |
| First 50 entries of a 10,000-entry playlist | indexed nested joins | 0.429 ms |

## Repeated membership lookups

Each scenario ran 500 times after seeding and analyzing the temporary tables.
The timings include the PL/pgSQL `PERFORM` statement overhead.

| Scenario | Median | p95 |
| --- | ---: | ---: |
| Exact provider lookup | 0.0040 ms | 0.0070 ms |
| Enumerate Song versions | 0.0030 ms | 0.0071 ms |
| Resolve Songs for a Track | 0.0020 ms | 0.0050 ms |

These are local synthetic baselines, not production latency promises. They show
that the narrow `SongTrack` edge and reverse composite index do not make the
dominant identity and one-hop relation lookups expensive. Production telemetry
should still watch larger credit/source fan-out and real cache behavior.
