# Music identity invariants

The database model and constraints are the implementation reference. This note
only records product meanings that cannot be inferred from the schema.

- A `Song` is the underlying composition or work.
- A `Track` is one exact recording or performance of one or more Songs.
- A cover is a separate Track for the same Song, with different performance
  credits and the `Cover` version flag.
- A mashup or medley can belong to multiple Songs. `SongTrack` membership means
  that the recording realizes the composition; it does not imply that every
  member is a safe synchronization substitute.
- Samples use `TrackRelation.Samples`; sampling does not create Song membership.
- Version flags describe a Track even when the exact source recording is
  unknown. A `TrackRelation` is added only when that exact lineage is known.
- A provider item belongs to the same Track only when it contains materially
  the same audio. Cover-art, lyrics, and music-video presentations can share a
  Track; acoustic, live, remix, or otherwise changed audio cannot.
- A playlist entry references the user's exact Track. Substituting another
  Track from the same Song is an explicit synchronization policy decision.
- MusicBrainz and other provider identities are evidence and external links,
  not Cantaro's canonical identity.

The graph-shaped model remains in PostgreSQL while its hot paths are indexed
one-hop lookups and transactional playlist updates. The BenchmarkDotNet suite
under `benchmarks/Cantaro.Benchmarks` measures those paths; consider another
database only if measured traversal requirements outgrow this representation.
