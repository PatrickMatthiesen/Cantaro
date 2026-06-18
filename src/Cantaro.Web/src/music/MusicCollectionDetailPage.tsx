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
  durationSeconds?: number;
  addedLabel?: string;
  platformIds?: PlatformId[];
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

function CollectionArtwork({ artworkUrl, title }: { artworkUrl?: string; title: string }) {
  if (artworkUrl) {
    return (
      <img
        src={artworkUrl}
        alt=""
        className="aspect-square w-full rounded-[1.75rem] object-cover shadow-[0_24px_60px_rgba(17,24,39,0.18)]"
      />
    );
  }

  return (
    <div
      className="flex aspect-square w-full items-center justify-center rounded-[1.75rem] bg-[radial-gradient(circle_at_25%_20%,#a78bfa,transparent_30%),linear-gradient(135deg,#111827,#4338ca_48%,#f472b6)] text-5xl font-black text-white shadow-[0_24px_60px_rgba(17,24,39,0.18)]"
      aria-hidden
    >
      {title.charAt(0)}
    </div>
  );
}

function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full bg-white/62 px-3 py-1 text-xs font-black text-slate-700 shadow-[inset_0_1px_0_rgba(255,255,255,0.74)]">
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
        className="inline-flex h-11 items-center gap-2 rounded-full bg-slate-950 px-6 text-sm font-black text-white shadow-[0_16px_34px_rgba(15,23,42,0.18)] transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-45"
        disabled={!hasTracks || !onPlayAll}
        onClick={onPlayAll}
      >
        <PlayIcon />
        Play
      </button>
      <button
        type="button"
        className="inline-flex h-11 items-center gap-2 rounded-full bg-white/76 px-5 text-sm font-black text-slate-800 shadow-[0_14px_34px_rgba(88,74,150,0.09)] transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-45"
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
    return <img src={track.artworkUrl} alt="" className="h-10 w-10 rounded-xl object-cover" />;
  }

  return (
    <div
      className="h-10 w-10 rounded-xl bg-[linear-gradient(135deg,#172554,#7c3aed_52%,#fb7185)]"
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
          className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-slate-950 text-white shadow-[0_8px_18px_rgba(15,23,42,0.18)] transition hover:bg-slate-800"
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
      <span className={`font-mono text-xs transition group-focus-within:opacity-0 group-hover:opacity-0 ${track.isPlaying ? 'font-black text-violet-600' : 'text-slate-500'}`}>
        {track.isPlaying ? '||' : index + 1}
      </span>
      {hasTrackControls ? <TrackHoverControls track={track} onPlayTrack={onPlayTrack} /> : null}
    </span>
  );
}

function NowPlayingBadge({ isPlaying }: { isPlaying?: boolean }) {
  if (!isPlaying) return null;

  return (
    <span className="ml-2 rounded-full bg-violet-100 px-2 py-0.5 text-[0.62rem] font-black text-violet-700">
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
      className="inline-flex h-8 w-8 items-center justify-center rounded-full text-slate-500 transition hover:bg-white hover:text-slate-950"
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
      <span className="min-w-0">
        <span className="block truncate font-black text-slate-950">
          {track.title}
          <NowPlayingBadge isPlaying={track.isPlaying} />
        </span>
        <span className="block truncate text-xs font-semibold text-slate-500 xl:hidden">{artist}</span>
      </span>
    </>
  );

  if (!track.detailSongId) {
    return <div className="flex min-w-0 items-center gap-3">{content}</div>;
  }

  return (
    <Link
      to="/music/songs/$songId"
      params={{ songId: track.detailSongId }}
      className="group/identity flex min-w-0 items-center gap-3 rounded-xl focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
    >
      {content}
    </Link>
  );
}

function TrackPlatforms({ platformIds = [] }: { platformIds?: PlatformId[] }) {
  if (platformIds.length === 0) {
    return <span className="text-xs font-semibold text-slate-400">-</span>;
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
    ? 'grid-cols-[72px_minmax(0,2fr)_minmax(0,1fr)_minmax(0,1.1fr)_110px_82px_54px]'
    : 'grid-cols-[72px_minmax(0,2fr)_minmax(0,1.1fr)_110px_82px_54px]';
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
  const rowStateClassName = track.isPlaying ? 'bg-[#eeeaff]/75' : 'hover:bg-white/56';

  return (
    <li
      className={`group grid items-center gap-3 px-5 py-3 text-sm text-slate-700 max-xl:grid-cols-[72px_minmax(0,1fr)_86px_44px] ${trackGridClassName(showAlbums)} ${rowStateClassName}`}
    >
      <TrackNumber track={track} index={index} onPlayTrack={onPlayTrack} />
      <TrackIdentity track={track} index={index} artist={artist} />
      <span className="truncate font-medium max-xl:hidden">{artist}</span>
      {showAlbums ? <span className="truncate font-medium max-xl:hidden" title={albums}>{albums || '-'}</span> : null}
      <span className="max-xl:hidden"><TrackPlatforms platformIds={track.platformIds} /></span>
      <span className="font-mono text-xs text-slate-600">{formatDuration(track.durationSeconds)}</span>
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
      <section className="rounded-[1.75rem] border border-white/70 bg-white/58 p-6 text-sm font-semibold text-slate-500 shadow-[0_20px_70px_rgba(88,74,150,0.08)]">
        Loading tracks...
      </section>
    );
  }

  if (tracks.length === 0) {
    return (
      <section className="rounded-[1.75rem] border border-dashed border-[#dcd5f7] bg-white/48 p-6">
        <p className="font-black text-slate-950">{emptyTrackLabel}</p>
        <p className="mt-1 text-sm font-medium text-slate-500">{emptyTrackDetail}</p>
      </section>
    );
  }

  const showAlbums = tracks.some((track) => (track.albums?.length ?? 0) > 0);

  return (
    <section className="overflow-hidden rounded-[1.75rem] border border-white/60 bg-white/42 shadow-[0_20px_70px_rgba(88,74,150,0.07)] backdrop-blur">
      <div className={`grid gap-3 border-b border-[#e8e3fa] px-5 py-3 text-[0.68rem] font-black tracking-[0.14em] text-slate-500 uppercase max-xl:grid-cols-[72px_minmax(0,1fr)_86px_44px] ${trackGridClassName(showAlbums)}`}>
        <span>#</span>
        <span>Title</span>
        <span className="max-xl:hidden">Artist</span>
        {showAlbums ? <span className="max-xl:hidden">Albums</span> : null}
        <span className="max-xl:hidden">Platforms</span>
        <span>Time</span>
        <span />
      </div>
      <ol className="divide-y divide-[#eeeafa]">
        {tracks.map((track, index) => (
          <TrackRow key={track.id} track={track} index={index} showAlbums={showAlbums} onPlayTrack={onPlayTrack} onQueueTrack={onQueueTrack} />
        ))}
      </ol>
    </section>
  );
}

function SuggestionThumb({ suggestion }: { suggestion: MusicCollectionSuggestion }) {
  if (suggestion.artworkUrl) {
    return <img src={suggestion.artworkUrl} alt="" className="h-12 w-12 rounded-xl object-cover" />;
  }

  return <div className="h-12 w-12 rounded-xl bg-[#eeeaff]" aria-hidden />;
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
        <p className="truncate text-sm font-black text-slate-950">{suggestion.title}</p>
        <p className="truncate text-xs font-semibold text-slate-500">{suggestion.detail}</p>
      </div>
    </>
  );
}

function SuggestionsPanel({ suggestions, className = '' }: { suggestions?: MusicCollectionSuggestion[]; className?: string }) {
  if (!suggestions || suggestions.length === 0) return null;

  return (
    <section className={`rounded-[1.75rem] border border-white/62 bg-white/48 p-5 shadow-[0_18px_60px_rgba(88,74,150,0.07)] backdrop-blur ${className}`}>
      <div className="flex items-center justify-between gap-3">
        <h2 className="font-black text-slate-950">More like this</h2>
      </div>
      <div className="mt-4 space-y-3">
        {suggestions.slice(0, 4).map((suggestion) => (
          <SuggestionLink
            key={suggestion.id}
            suggestion={suggestion}
            className="flex items-center gap-3 rounded-2xl p-2 transition hover:bg-white/64 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
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
      <MusicUpNextPanel
        activeTrack={activeTrack}
        queuedTracks={queuedTracks}
        title={rightRailTitle}
        onClearQueue={onClearQueue}
      />
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

function CollectionHero({
  eyebrow,
  title,
  description,
  artworkUrl,
  ownerLabel,
  updatedAt,
  songsLabel,
  durationLabel,
  chips,
  tracks,
  actionSlot,
  onPlayAll,
  onShuffle,
}: Omit<MusicCollectionDetailPageProps, 'backTo' | 'backParams' | 'backLabel' | 'emptyTrackLabel' | 'onPlayTrack' | 'onQueueTrack'>) {
  const computedDurationLabel = getCollectionDurationLabel(tracks, durationLabel);

  return (
    <section className="relative overflow-hidden rounded-[2rem] bg-[#ece9ff] px-5 py-5 shadow-[0_28px_90px_rgba(88,74,150,0.12)] md:px-7">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_18%_14%,rgba(255,255,255,0.95),transparent_32%),radial-gradient(circle_at_82%_26%,rgba(199,210,254,0.9),transparent_34%),linear-gradient(135deg,rgba(255,255,255,0.64),rgba(221,214,254,0.74))]" />
      <div className="relative grid gap-6 md:grid-cols-[minmax(180px,270px)_1fr] md:items-end">
        <CollectionArtwork artworkUrl={artworkUrl} title={title} />
        <div className="min-w-0 space-y-5">
          <div>
            <p className="text-xs font-black tracking-[0.18em] text-slate-600 uppercase">{eyebrow}</p>
            <h1 className="mt-2 text-4xl leading-tight font-black text-slate-950 sm:text-5xl">{title}</h1>
            {description ? <p className="mt-3 max-w-2xl text-sm leading-6 font-semibold text-slate-600">{description}</p> : null}
          </div>
          <div className="flex flex-wrap gap-2">
            {chips.length > 0 ? chips.map((chip) => <Pill key={chip}>{chip}</Pill>) : <Pill>Cantaro</Pill>}
          </div>
          <div className="flex flex-wrap items-center justify-between gap-5">
            <div className="space-y-3">
              <p className="text-sm font-black text-slate-700">
                {songsLabel}
                {computedDurationLabel ? <span className="font-semibold text-slate-500"> · {computedDurationLabel}</span> : null}
              </p>
              <p className="text-sm font-semibold text-slate-600">
                {ownerLabel}
                {updatedAt ? <span className="block text-xs text-slate-500">Updated {formatTimestamp(updatedAt) ?? updatedAt}</span> : null}
              </p>
            </div>
            <HeaderActions actionSlot={actionSlot} hasTracks={tracks.length > 0} onPlayAll={onPlayAll} onShuffle={onShuffle} />
          </div>
        </div>
      </div>
    </section>
  );
}

export function MusicCollectionDetailPage({
  eyebrow,
  title,
  description,
  artworkUrl,
  backTo,
  backParams,
  backLabel,
  ownerLabel,
  updatedAt,
  songsLabel,
  durationLabel,
  chips,
  tracks,
  isLoadingTracks,
  emptyTrackLabel,
  activeTrack,
  queuedTracks,
  rightRailTitle,
  suggestions,
  actionSlot,
  onPlayAll,
  onShuffle,
  onPlayTrack,
  onQueueTrack,
  onClearQueue,
}: MusicCollectionDetailPageProps) {
  return (
    <div className="space-y-5">
      <Link
        to={backTo}
        params={backParams as never}
        className="inline-flex items-center gap-2 text-sm font-black text-violet-600 transition hover:text-violet-500"
      >
        <span aria-hidden>‹</span>
        {backLabel}
      </Link>

      <div className="grid gap-6 2xl:grid-cols-[minmax(0,1fr)_330px]">
        <div className="min-w-0 space-y-5">
          <CollectionHero
            eyebrow={eyebrow}
            title={title}
            description={description}
            artworkUrl={artworkUrl}
            ownerLabel={ownerLabel}
            updatedAt={updatedAt}
            songsLabel={songsLabel}
            durationLabel={durationLabel}
            chips={chips}
            tracks={tracks}
            actionSlot={actionSlot}
            onPlayAll={onPlayAll}
            onShuffle={onShuffle}
          />

          <MusicTrackTable
            tracks={tracks}
            isLoadingTracks={isLoadingTracks}
            emptyTrackLabel={emptyTrackLabel}
            onPlayTrack={onPlayTrack}
            onQueueTrack={onQueueTrack}
          />
        </div>

        <RightRail
          activeTrack={activeTrack}
          queuedTracks={queuedTracks}
          rightRailTitle={rightRailTitle}
          suggestions={suggestions}
          onClearQueue={onClearQueue}
        />
      </div>

      {suggestions && suggestions.length > 0 ? (
        <section className="2xl:hidden">
          <h2 className="mb-3 text-lg font-black text-slate-950">More like this</h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {suggestions.slice(0, 4).map((suggestion) => (
              <SuggestionLink
                key={suggestion.id}
                suggestion={suggestion}
                className="overflow-hidden rounded-2xl bg-white/60 shadow-[0_16px_45px_rgba(88,74,150,0.08)] transition hover:-translate-y-0.5 hover:bg-white focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
              >
                {suggestion.artworkUrl ? <img src={suggestion.artworkUrl} alt="" className="h-24 w-full object-cover" /> : null}
                <div className="p-3">
                  <p className="truncate text-sm font-black text-slate-950">{suggestion.title}</p>
                  <p className="truncate text-xs font-semibold text-slate-500">{suggestion.detail}</p>
                </div>
              </SuggestionLink>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}
