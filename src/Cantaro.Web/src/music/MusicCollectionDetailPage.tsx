import { Link } from '@tanstack/react-router';
import { type ReactNode } from 'react';
import { MusicPlatformIcon, type PlatformId } from '@cantaro/client-shared/music';
import { MusicUpNextPanel } from './MusicUpNextPanel';
import { formatDuration, formatTimestamp, platformName } from './musicPresentation';

export interface MusicCollectionTrack {
  id: string;
  detailSongId?: string;
  title: string;
  artist?: string;
  albums?: string[];
  artworkUrl?: string;
  artworkExternalUrl?: string;
  preserveArtworkAspectRatio?: boolean;
  durationSeconds?: number;
  addedLabel?: string;
  platformIds?: PlatformId[];
  externalUrl?: string;
  albumLinks?: Array<{ name: string; url: string }>;
  isPlaying?: boolean;
}

export interface MusicCollectionSuggestion {
  id: string;
  title: string;
  detail: string;
  artworkUrl?: string;
  route:
    | { type: 'libraryPlaylist' }
    | { type: 'platformPlaylist'; platformId: string };
}

interface MusicCollectionDetailPageProps {
  eyebrow: string;
  title: string;
  description?: string;
  artworkUrl?: string;
  artworkExternalUrl?: string;
  preserveArtworkAspectRatio?: boolean;
  backTo: '/music/playlists' | '/music/platforms/$platformId';
  backParams?: Record<string, string>;
  backLabel: string;
  ownerLabel: string;
  updatedAt?: string;
  songsLabel: string;
  durationLabel?: string;
  chips: string[];
  tracks: MusicCollectionTrack[];
  isLoadingTracks?: boolean;
  emptyTrackLabel: string;
  activeTrack?: MusicCollectionTrack;
  queuedTracks?: MusicCollectionTrack[];
  rightRailTitle?: string;
  suggestions?: MusicCollectionSuggestion[];
  actionSlot?: ReactNode;
  onPlayAll?: () => void;
  onShuffle?: () => void;
  onPlayTrack?: (track: MusicCollectionTrack) => void;
  onQueueTrack?: (track: MusicCollectionTrack) => void;
  onClearQueue?: () => void;
}

function CollectionArtwork({
  artworkUrl,
  artworkExternalUrl,
  preserveArtworkAspectRatio,
  title,
}: {
  artworkUrl?: string;
  artworkExternalUrl?: string;
  preserveArtworkAspectRatio?: boolean;
  title: string;
}) {
  if (artworkUrl) {
    const artwork = (
      <img
        src={artworkUrl}
        alt=""
        className={`aspect-square w-full bg-surface ${
          preserveArtworkAspectRatio ? 'object-contain' : 'object-cover'
        }`}
      />
    );

    return artworkExternalUrl ? (
      <a
        href={artworkExternalUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        aria-label={`Open ${title} on its music platform`}
      >
        {artwork}
      </a>
    ) : artwork;
  }

  return (
    <div
      className="flex aspect-square w-full items-center justify-center bg-surface-subtle text-5xl font-black text-personal-accent-strong"
      aria-hidden
    >
      {title.charAt(0)}
    </div>
  );
}

function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="border-l border-border-strong pl-2 text-xs font-bold text-content-muted first:border-0 first:pl-0">
      {children}
    </span>
  );
}

function PlayIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
      <path d="M8 5.8v12.4c0 .8.9 1.3 1.6.9l9.7-6.2a1.1 1.1 0 0 0 0-1.8L9.6 4.9C8.9 4.5 8 5 8 5.8Z" />
    </svg>
  );
}

function ShuffleIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" aria-hidden>
      <path d="M16 3h5v5M4 18h2.8c2 0 3.2-1 4.3-2.6l1.8-2.8C14 11 15.2 10 17.2 10H21M4 6h2.8c2 0 3.2 1 4.3 2.6l.4.7M16 21h5v-5" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" />
    </svg>
  );
}

function QueueIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" aria-hidden>
      <path d="M4 7h10M4 12h8M4 17h10M18 9v8M14 13h8" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" />
    </svg>
  );
}

function HeaderActions({
  actionSlot,
  hasTracks,
  onPlayAll,
  onShuffle,
}: {
  actionSlot?: ReactNode;
  hasTracks: boolean;
  onPlayAll?: () => void;
  onShuffle?: () => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <button
        type="button"
        className="inline-flex h-10 items-center gap-2 bg-personal-accent px-4 text-sm font-black text-personal-accent-contrast transition hover:bg-personal-accent-hover disabled:cursor-not-allowed disabled:opacity-45 sm:h-11 sm:px-6"
        disabled={!hasTracks || !onPlayAll}
        onClick={onPlayAll}
      >
        <PlayIcon />
        Play
      </button>
      <button
        type="button"
        className="inline-flex h-10 items-center gap-2 border border-border-strong bg-surface px-4 text-sm font-black text-content transition hover:bg-surface-hover disabled:cursor-not-allowed disabled:opacity-45 sm:h-11 sm:px-5"
        disabled={!hasTracks || !onShuffle}
        onClick={onShuffle}
      >
        <ShuffleIcon />
        Shuffle
      </button>
      {actionSlot}
    </div>
  );
}

function TrackArtwork({ track, index }: { track: MusicCollectionTrack; index: number }) {
  if (track.artworkUrl) {
    const artwork = (
      <img
        src={track.artworkUrl}
        alt=""
        className={`h-10 w-10 shrink-0 bg-surface ${track.preserveArtworkAspectRatio ? 'object-contain' : 'object-cover'}`}
      />
    );

    return track.artworkExternalUrl ? (
      <a
        href={track.artworkExternalUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="shrink-0 focus-visible:outline-2 focus-visible:outline-focus"
        aria-label={`Open artwork for ${track.title}`}
      >
        {artwork}
      </a>
    ) : artwork;
  }

  return (
    <div
      className="h-10 w-10 shrink-0 bg-surface-subtle"
      aria-label={`Track ${index + 1}`}
    />
  );
}

function TrackHoverControls({
  track,
  onPlayTrack,
}: {
  track: MusicCollectionTrack;
  onPlayTrack?: (track: MusicCollectionTrack) => void;
}) {
  return (
    <span className="absolute left-0 flex items-center gap-1 opacity-0 transition group-focus-within:opacity-100 group-hover:opacity-100">
      {onPlayTrack ? (
        <button
          type="button"
          className="inline-flex h-7 w-7 items-center justify-center bg-personal-accent text-personal-accent-content transition-colors hover:bg-personal-accent-hover"
          aria-label={`Play ${track.title}`}
          onClick={() => onPlayTrack(track)}
        >
          <PlayIcon />
        </button>
      ) : null}
    </span>
  );
}

function TrackNumber({
  track,
  index,
  onPlayTrack,
}: {
  track: MusicCollectionTrack;
  index: number;
  onPlayTrack?: (track: MusicCollectionTrack) => void;
}) {
  const hasTrackControls = Boolean(onPlayTrack);

  return (
    <span className="relative flex h-9 items-center">
      <span className={`font-mono text-xs transition group-focus-within:opacity-0 group-hover:opacity-0 ${track.isPlaying ? 'font-black text-accent' : 'text-content-muted'}`}>
        {track.isPlaying ? '||' : index + 1}
      </span>
      {hasTrackControls ? <TrackHoverControls track={track} onPlayTrack={onPlayTrack} /> : null}
    </span>
  );
}

function NowPlayingBadge({ isPlaying }: { isPlaying?: boolean }) {
  if (!isPlaying) return null;

  return (
    <span className="ml-2 bg-info-surface px-2 py-0.5 text-[0.62rem] font-bold text-info-content">
      Now playing
    </span>
  );
}

function MoreTrackActions({
  title,
  track,
  onQueueTrack,
}: {
  title: string;
  track: MusicCollectionTrack;
  onQueueTrack?: (track: MusicCollectionTrack) => void;
}) {
  if (!onQueueTrack) return null;

  return (
    <button
      type="button"
      className="inline-flex h-8 w-8 items-center justify-center text-content-muted transition-colors hover:bg-surface-hover hover:text-content"
      aria-label={`Add ${title} up next`}
      onClick={() => onQueueTrack(track)}
    >
      <QueueIcon />
    </button>
  );
}

function TrackIdentity({ track, index, artist }: { track: MusicCollectionTrack; index: number; artist: string }) {
  const content = (
    <>
      <TrackArtwork track={track} index={index} />
      <span className="min-w-0 flex-1">
        {track.externalUrl && !track.detailSongId ? (
          <a
            href={track.externalUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="block truncate font-black text-content hover:text-accent-strong focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none"
          >
            {track.title}
            <span className="sr-only"> — open on music platform</span>
            <NowPlayingBadge isPlaying={track.isPlaying} />
          </a>
        ) : (
          <span className="block truncate font-black text-content">
            {track.title}
            <NowPlayingBadge isPlaying={track.isPlaying} />
          </span>
        )}
        <span className="block truncate text-xs font-semibold text-content-muted xl:hidden">{artist}</span>
      </span>
    </>
  );

  if (!track.detailSongId) {
    return <div className="flex min-w-0 items-center gap-2.5 sm:gap-3">{content}</div>;
  }

  return (
    <Link
      to="/music/songs/$songId"
      params={{ songId: track.detailSongId }}
      className="group/identity flex min-w-0 items-center gap-2.5 focus-visible:outline-2 focus-visible:outline-focus sm:gap-3"
    >
      {content}
    </Link>
  );
}

function TrackPlatforms({ platformIds = [] }: { platformIds?: PlatformId[] }) {
  if (platformIds.length === 0) {
    return <span className="text-xs font-semibold text-content-subtle">-</span>;
  }

  return (
    <span className="flex items-center gap-2">
      {platformIds.map((platformId) => (
        <span key={platformId} className="inline-flex items-center justify-center" title={platformName(platformId)}>
          <MusicPlatformIcon platformId={platformId} className="h-5 w-5" title={platformName(platformId)} />
        </span>
      ))}
    </span>
  );
}

function trackGridClassName(showAlbums: boolean) {
  return showAlbums
    ? 'xl:grid-cols-[72px_minmax(0,2fr)_minmax(0,1fr)_minmax(0,1.1fr)_110px_82px_54px]'
    : 'xl:grid-cols-[72px_minmax(0,2fr)_minmax(0,1.1fr)_110px_82px_54px]';
}

function TrackAlbums({ track, albums }: { track: MusicCollectionTrack; albums: string }) {
  if (!track.albumLinks?.length) {
    return <>{albums || '-'}</>;
  }

  return track.albumLinks.map((album, albumIndex) => (
    <span key={`${album.url}-${album.name}`}>
      {albumIndex > 0 ? ', ' : null}
      <a
        href={album.url}
        target="_blank"
        rel="noopener noreferrer"
        className="hover:text-accent-strong hover:underline focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none"
      >
        {album.name}
      </a>
    </span>
  ));
}

function TrackRow({
  track,
  index,
  showAlbums,
  onPlayTrack,
  onQueueTrack,
}: {
  track: MusicCollectionTrack;
  index: number;
  showAlbums: boolean;
  onPlayTrack?: (track: MusicCollectionTrack) => void;
  onQueueTrack?: (track: MusicCollectionTrack) => void;
}) {
  const artist = track.artist ?? 'Unknown artist';
  const albums = track.albums?.join(', ') ?? '';
  const rowStateClassName = track.isPlaying ? 'bg-info-surface' : 'hover:bg-surface-hover';

  return (
    <li
      className={`group grid grid-cols-[2rem_minmax(0,1fr)_3.25rem_2rem] items-center gap-2 px-3 py-3 text-sm text-content transition-colors sm:grid-cols-[56px_minmax(0,1fr)_72px_40px] sm:gap-3 sm:px-5 ${trackGridClassName(showAlbums)} ${rowStateClassName}`}
    >
      <TrackNumber track={track} index={index} onPlayTrack={onPlayTrack} />
      <TrackIdentity track={track} index={index} artist={artist} />
      <span className="truncate font-medium max-xl:hidden">{artist}</span>
      {showAlbums ? (
        <span className="truncate font-medium max-xl:hidden" title={albums}>
          <TrackAlbums track={track} albums={albums} />
        </span>
      ) : null}
      <span className="max-xl:hidden"><TrackPlatforms platformIds={track.platformIds} /></span>
      <span className="justify-self-start font-mono text-xs text-content-muted sm:justify-self-auto">{formatDuration(track.durationSeconds)}</span>
      <MoreTrackActions title={track.title} track={track} onQueueTrack={onQueueTrack} />
    </li>
  );
}

export function MusicTrackTable({
  tracks,
  isLoadingTracks,
  emptyTrackLabel,
  emptyTrackDetail = 'Tracks will appear here after Cantaro has song data for this collection.',
  onPlayTrack,
  onQueueTrack,
}: {
  tracks: MusicCollectionTrack[];
  isLoadingTracks?: boolean;
  emptyTrackLabel: string;
  emptyTrackDetail?: string;
  onPlayTrack?: (track: MusicCollectionTrack) => void;
  onQueueTrack?: (track: MusicCollectionTrack) => void;
}) {
  if (isLoadingTracks) {
    return (
      <section className="border-y border-border-subtle bg-surface py-6 text-sm font-semibold text-content-muted">
        Loading tracks...
      </section>
    );
  }

  if (tracks.length === 0) {
    return (
      <section className="border-y border-border-subtle bg-surface py-6">
        <p className="font-black text-content">{emptyTrackLabel}</p>
        <p className="mt-1 text-sm font-medium text-content-muted">{emptyTrackDetail}</p>
      </section>
    );
  }

  const showAlbums = tracks.some((track) => (track.albums?.length ?? 0) > 0);

  return (
    <section className="overflow-hidden border-y border-border-subtle bg-surface">
      <div className={`grid grid-cols-[2rem_minmax(0,1fr)_3.25rem_2rem] gap-2 border-b border-border-subtle px-3 py-3 text-[0.68rem] font-bold tracking-[0.12em] text-content-muted uppercase sm:grid-cols-[56px_minmax(0,1fr)_72px_40px] sm:gap-3 sm:px-5 ${trackGridClassName(showAlbums)}`}>
        <span>#</span>
        <span>Title</span>
        <span className="max-xl:hidden">Artist</span>
        {showAlbums ? <span className="max-xl:hidden">Albums</span> : null}
        <span className="max-xl:hidden">Platforms</span>
        <span>Time</span>
        <span />
      </div>
      <ol className="divide-y divide-border-subtle">
        {tracks.map((track, index) => (
          <TrackRow key={track.id} track={track} index={index} showAlbums={showAlbums} onPlayTrack={onPlayTrack} onQueueTrack={onQueueTrack} />
        ))}
      </ol>
    </section>
  );
}

function SuggestionThumb({ suggestion }: { suggestion: MusicCollectionSuggestion }) {
  if (suggestion.artworkUrl) {
    return <img src={suggestion.artworkUrl} alt="" className="h-12 w-12 object-cover" />;
  }

  return <div className="h-12 w-12 bg-surface-subtle" aria-hidden />;
}

function SuggestionLink({
  suggestion,
  children,
  className,
}: {
  suggestion: MusicCollectionSuggestion;
  children: ReactNode;
  className: string;
}) {
  if (suggestion.route.type === 'platformPlaylist') {
    return (
      <Link
        to="/music/platforms/$platformId/playlists/$playlistId"
        params={{ platformId: suggestion.route.platformId, playlistId: suggestion.id }}
        className={className}
      >
        {children}
      </Link>
    );
  }

  return (
    <Link
      to="/music/playlists/$playlistId"
      params={{ playlistId: suggestion.id }}
      className={className}
    >
      {children}
    </Link>
  );
}

function SuggestionContent({ suggestion }: { suggestion: MusicCollectionSuggestion }) {
  return (
    <>
      <SuggestionThumb suggestion={suggestion} />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-black text-content">{suggestion.title}</p>
        <p className="truncate text-xs font-semibold text-content-muted">{suggestion.detail}</p>
      </div>
    </>
  );
}

function SuggestionsPanel({ suggestions, className = '' }: { suggestions?: MusicCollectionSuggestion[]; className?: string }) {
  if (!suggestions || suggestions.length === 0) return null;

  return (
    <section className={`border-y border-border-subtle py-5 ${className}`}>
      <div className="flex items-center justify-between gap-3">
        <h2 className="font-black text-content">More like this</h2>
      </div>
      <div className="mt-4 space-y-3">
        {suggestions.slice(0, 4).map((suggestion) => (
          <SuggestionLink
            key={suggestion.id}
            suggestion={suggestion}
            className="flex items-center gap-3 border-t border-border-subtle p-2 transition first:border-0 hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus"
          >
            <SuggestionContent suggestion={suggestion} />
          </SuggestionLink>
        ))}
      </div>
    </section>
  );
}

function RightRail({
  activeTrack,
  queuedTracks = [],
  rightRailTitle,
  suggestions,
  onClearQueue,
}: {
  activeTrack?: MusicCollectionTrack;
  queuedTracks?: MusicCollectionTrack[];
  rightRailTitle?: string;
  suggestions?: MusicCollectionSuggestion[];
  onClearQueue?: () => void;
}) {
  return (
    <aside className="space-y-4">
      {activeTrack || queuedTracks.length > 0 || onClearQueue ? (
        <MusicUpNextPanel
          activeTrack={activeTrack}
          queuedTracks={queuedTracks}
          title={rightRailTitle}
          onClearQueue={onClearQueue}
        />
      ) : null}
      <SuggestionsPanel suggestions={suggestions} className="hidden 2xl:block" />
    </aside>
  );
}

function getCollectionDurationLabel(tracks: MusicCollectionTrack[], fallback?: string) {
  if (fallback) return fallback;

  const totalSeconds = tracks.reduce((total, track) => total + (track.durationSeconds ?? 0), 0);
  if (totalSeconds <= 0) return undefined;

  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.round((totalSeconds % 3600) / 60);
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
}

type CollectionHeroProps = Omit<MusicCollectionDetailPageProps, 'backTo' | 'backParams' | 'backLabel' | 'emptyTrackLabel' | 'onPlayTrack' | 'onQueueTrack'>;

function CollectionHeroTitle({
  eyebrow,
  title,
  description,
}: Pick<CollectionHeroProps, 'eyebrow' | 'title' | 'description'>) {
  return (
    <div>
      <p className="text-xs font-black tracking-[0.18em] text-content-muted uppercase">{eyebrow}</p>
      <h1 className="mt-1.5 text-3xl leading-tight font-black text-content sm:mt-2 sm:text-4xl md:text-5xl">{title}</h1>
      {description ? <p className="mt-3 max-w-2xl text-sm leading-6 font-semibold text-content-muted">{description}</p> : null}
    </div>
  );
}

function CollectionHeroChips({ chips }: Pick<CollectionHeroProps, 'chips'>) {
  return (
    <div className="flex flex-wrap gap-2">
      {chips.length > 0 ? chips.map((chip) => <Pill key={chip}>{chip}</Pill>) : <Pill>Cantaro</Pill>}
    </div>
  );
}

function CollectionHeroMeta({
  ownerLabel,
  updatedAt,
  songsLabel,
  durationLabel,
  tracks,
  actionSlot,
  onPlayAll,
  onShuffle,
}: Pick<CollectionHeroProps, 'ownerLabel' | 'updatedAt' | 'songsLabel' | 'durationLabel' | 'tracks' | 'actionSlot' | 'onPlayAll' | 'onShuffle'>) {
  const computedDurationLabel = getCollectionDurationLabel(tracks, durationLabel);

  return (
    <div className="col-span-2 flex flex-wrap items-center justify-between gap-4 sm:col-span-1 sm:gap-5">
      <div className="space-y-3">
        <p className="text-sm font-black text-content">
          {songsLabel}
          {computedDurationLabel ? <span className="font-semibold text-content-muted"> · {computedDurationLabel}</span> : null}
        </p>
        <p className="text-sm font-semibold text-content-muted">
          {ownerLabel}
          {updatedAt ? <span className="block text-xs text-content-muted">Updated {formatTimestamp(updatedAt) ?? updatedAt}</span> : null}
        </p>
      </div>
      <HeaderActions actionSlot={actionSlot} hasTracks={tracks.length > 0} onPlayAll={onPlayAll} onShuffle={onShuffle} />
    </div>
  );
}

function CollectionHero(props: CollectionHeroProps) {
  return (
    <section className="relative isolate overflow-hidden border-y border-border-subtle bg-surface-subtle px-4 py-5 sm:px-5 md:px-7">
      {props.artworkUrl ? <img src={props.artworkUrl} alt="" className="absolute inset-0 -z-20 size-full scale-110 object-cover opacity-15 blur-3xl" /> : null}
      <div className="absolute inset-0 -z-10 bg-linear-to-r from-canvas via-canvas/90 to-canvas/55" aria-hidden />
      <div className="relative grid grid-cols-[76px_minmax(0,1fr)] items-start gap-4 sm:grid-cols-[minmax(132px,180px)_1fr] sm:gap-5 md:grid-cols-[minmax(180px,270px)_1fr] md:items-end md:gap-6">
        <CollectionArtwork
          artworkUrl={props.artworkUrl}
          artworkExternalUrl={props.artworkExternalUrl}
          preserveArtworkAspectRatio={props.preserveArtworkAspectRatio}
          title={props.title}
        />
        <div className="min-w-0 space-y-4 sm:space-y-5">
          <CollectionHeroTitle {...props} />
          <CollectionHeroChips chips={props.chips} />
          <CollectionHeroMeta {...props} />
        </div>
      </div>
    </section>
  );
}

function MobileSuggestions({ suggestions }: Pick<MusicCollectionDetailPageProps, 'suggestions'>) {
  if (!suggestions || suggestions.length === 0) return null;

  return (
    <section className="2xl:hidden">
      <h2 className="mb-3 text-lg font-black text-content">More like this</h2>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {suggestions.slice(0, 4).map((suggestion) => (
          <SuggestionLink
            key={suggestion.id}
            suggestion={suggestion}
            className="group relative overflow-hidden bg-surface-subtle transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus"
          >
            {suggestion.artworkUrl ? <img src={suggestion.artworkUrl} alt="" className="h-24 w-full object-cover" /> : null}
            <div className="p-3">
              <p className="truncate text-sm font-black text-content">{suggestion.title}</p>
              <p className="truncate text-xs font-semibold text-content-muted">{suggestion.detail}</p>
            </div>
          </SuggestionLink>
        ))}
      </div>
    </section>
  );
}

export function MusicCollectionDetailPage(props: MusicCollectionDetailPageProps) {
  return (
    <div className="space-y-5">
      <Link
        to={props.backTo}
        params={props.backParams as never}
        className="inline-flex min-h-10 items-center gap-2 text-sm font-black text-content-muted transition hover:text-personal-accent-strong"
      >
        <span aria-hidden>‹</span>
        {props.backLabel}
      </Link>

      <div className="grid gap-6 2xl:grid-cols-[minmax(0,1fr)_330px]">
        <div className="min-w-0 space-y-5">
          <CollectionHero
            eyebrow={props.eyebrow}
            title={props.title}
            description={props.description}
            artworkUrl={props.artworkUrl}
            artworkExternalUrl={props.artworkExternalUrl}
            preserveArtworkAspectRatio={props.preserveArtworkAspectRatio}
            ownerLabel={props.ownerLabel}
            updatedAt={props.updatedAt}
            songsLabel={props.songsLabel}
            durationLabel={props.durationLabel}
            chips={props.chips}
            tracks={props.tracks}
            actionSlot={props.actionSlot}
            onPlayAll={props.onPlayAll}
            onShuffle={props.onShuffle}
          />

          <MusicTrackTable
            tracks={props.tracks}
            isLoadingTracks={props.isLoadingTracks}
            emptyTrackLabel={props.emptyTrackLabel}
            onPlayTrack={props.onPlayTrack}
            onQueueTrack={props.onQueueTrack}
          />
        </div>

        <RightRail
          activeTrack={props.activeTrack}
          queuedTracks={props.queuedTracks}
          rightRailTitle={props.rightRailTitle}
          suggestions={props.suggestions}
          onClearQueue={props.onClearQueue}
        />
      </div>

      <MobileSuggestions suggestions={props.suggestions} />
    </div>
  );
}
