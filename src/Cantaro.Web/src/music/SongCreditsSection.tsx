import { ChevronDown } from 'lucide-react';
import type { MusicLibrarySong, MusicLibrarySongArtistCredit } from '@cantaro/client-shared/music';
import { platformName } from './musicPresentation';

const creditRoles: Record<MusicLibrarySongArtistCredit['role'], string> = {
  primary: 'Artist',
  featured: 'Featured artist',
  producer: 'Producer',
  remixer: 'Remixer',
};

function SongIdentifiers({ song }: { song: MusicLibrarySong }) {
  const identifiers = [
    {
      label: song.id.startsWith('observation:') ? 'Observation ID' : 'Cantaro recording ID',
      value: song.id.replace(/^(track|observation):/, ''),
    },
    ...song.sourceIdentities.map((identity) => ({
      label: identity.source === 'musicbrainz' ? 'MusicBrainz recording ID'
        : identity.source === 'isrc' ? 'ISRC' : `${platformName(identity.source)} ID`,
      value: identity.externalId,
    })),
  ];

  return (
    <details className="group mt-7">
      <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-3 text-sm font-semibold text-content-muted hover:text-content focus-visible:outline-2 focus-visible:outline-focus [&::-webkit-details-marker]:hidden">
        Identifiers
        <ChevronDown className="size-4 shrink-0 group-open:rotate-180" aria-hidden />
      </summary>
      <dl className="mt-3 space-y-4 pb-2">
        {identifiers.map((identifier) => (
          <div key={`${identifier.label}:${identifier.value}`}>
            <dt className="text-xs text-content-muted">{identifier.label}</dt>
            <dd className="mt-1 break-all font-mono text-xs leading-5 text-content">{identifier.value}</dd>
          </div>
        ))}
      </dl>
    </details>
  );
}

export function SongCreditsSection({ song }: { song: MusicLibrarySong }) {
  const credits = [...song.artistCredits].sort((left, right) => left.position - right.position);

  return (
    <section className="py-9" aria-labelledby="song-credits-heading">
      <h2 id="song-credits-heading" className="text-xl font-bold text-content">Credits</h2>
      {credits.length > 0 ? (
        <ul className="mt-5 flex flex-wrap gap-x-8 gap-y-5">
          {credits.map((credit) => (
            <li key={`${credit.artistId}:${credit.role}:${credit.position}`} className="min-w-0 max-w-full">
              <p className="break-words text-base font-semibold text-content">{credit.creditedName || credit.name}</p>
              <p className="mt-1 text-sm text-content-muted">{creditRoles[credit.role]}</p>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-5 text-base font-semibold text-content">{song.artist || 'No artist credits yet.'}</p>
      )}
      {song.albums.length > 0 ? (
        <div className="mt-7">
          <h3 className="text-sm text-content-muted">{song.albums.length === 1 ? 'Album' : 'Albums'}</h3>
          <ul className="mt-2 space-y-1">
            {song.albums.map((album) => <li key={album} className="break-words text-base font-semibold text-content">{album}</li>)}
          </ul>
        </div>
      ) : null}
      <SongIdentifiers key={song.id} song={song} />
    </section>
  );
}
