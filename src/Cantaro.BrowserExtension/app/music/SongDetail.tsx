import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { SongLyrics } from './SongLyrics';
import { SongSection } from './SongSection';
import {
  type PlaylistController,
  type PlaylistMembership,
  usePlaylistController,
} from './usePlaylistController';

export function SongDetail({ song, playlists, onBack }: {
  song: MusicLibrarySong;
  playlists: MusicLibraryResponse['playlists'];
  onBack: () => void;
}) {
  const controller = usePlaylistController(song, playlists);

  return (
    <article className="p-1">
      <button type="button" onClick={onBack} className="min-h-9 px-2 text-xs font-bold text-content-muted hover:bg-surface-hover hover:text-content">← Back to music</button>
      <SongSummary song={song} />
      <PlatformLinks song={song} />
      <SongLyrics trackId={controller.trackId} />
      <Identifiers song={song} />
      <YouTubeVersion controller={controller} />
      <Playlists controller={controller} />
    </article>
  );
}

function SongSummary({ song }: { song: MusicLibrarySong }) {
  const duration = song.durationSeconds
    ? ` · ${Math.floor(song.durationSeconds / 60)}:${String(song.durationSeconds % 60).padStart(2, '0')}`
    : '';
  return (
    <div className="relative isolate mt-1 overflow-hidden border-y border-border-subtle bg-surface-subtle px-3 py-4">
      {song.thumbnailUrl ? <img src={song.thumbnailUrl} alt="" className="absolute inset-0 -z-20 size-full scale-110 object-cover opacity-15 blur-xl" /> : null}
      <div className="absolute inset-0 -z-10 bg-linear-to-r from-canvas via-canvas/90 to-canvas/60" aria-hidden />
      <div className="flex gap-3">
        {song.thumbnailUrl ? <img src={song.thumbnailUrl} alt="" className="size-20 object-cover" /> : <div className="size-20 bg-surface" />}
        <div className="min-w-0">
        <h1 className="text-lg font-bold text-content">{song.title}</h1>
        <p className="text-sm text-content-muted">{song.artist || 'Unknown artist'}</p>
        <p className="mt-1 text-xs text-content-muted">{song.albums.join(', ') || 'No album'}{duration}</p>
        </div>
      </div>
    </div>
  );
}

function PlatformLinks({ song }: { song: MusicLibrarySong }) {
  if (song.platformLinks.length === 0) return null;
  return (
    <SongSection title="Listen">
      <div className="flex flex-wrap gap-2">
        {song.platformLinks.map((link) => (
          <a key={`${link.platform}-${link.url}`} href={link.url} target="_blank" rel="noreferrer" className="min-h-9 border border-border-strong bg-surface px-3 py-2 text-xs font-bold text-content hover:bg-surface-hover hover:text-personal-accent-strong">
            {link.label}
          </a>
        ))}
      </div>
    </SongSection>
  );
}

function Identifiers({ song }: { song: MusicLibrarySong }) {
  if (song.sourceIdentities.length === 0) return null;
  return (
    <SongSection title="Identifiers">
      {song.sourceIdentities.map((identity) => (
        <p key={`${identity.source}-${identity.externalId}`} className="truncate text-xs text-content-muted">
          <b>{identity.source}:</b> {identity.externalId}
        </p>
      ))}
    </SongSection>
  );
}

function YouTubeVersion({ controller }: { controller: PlaylistController }) {
  if (controller.youtubeIds.length <= 1) return null;
  return (
    <SongSection title="YouTube version">
      <select
        value={controller.selectedYouTubeId}
        onChange={(event) => controller.setSelectedYouTubeId(event.target.value)}
        className="min-h-10 w-full border border-border-strong bg-surface px-2 text-xs text-content"
      >
        <option value="">Choose a version to sync…</option>
        {controller.youtubeIds.map((id) => <option key={id} value={id}>{id}</option>)}
      </select>
    </SongSection>
  );
}

function Playlists({ controller }: { controller: PlaylistController }) {
  return (
    <SongSection title="Playlists">
      <PlaylistHeader controller={controller} />
      <MembershipList controller={controller} />
      <PlaylistPicker controller={controller} />
      {controller.message ? <p className="mt-2 text-xs text-content-muted" role="status">{controller.message}</p> : null}
    </SongSection>
  );
}

function PlaylistHeader({ controller }: { controller: PlaylistController }) {
  const canAdd = Boolean(controller.trackId && controller.availablePlaylists.length);
  return (
    <div className="mb-2 flex items-center justify-between gap-3">
      <p className="text-xs text-content-muted">Tracked playlist memberships</p>
      {canAdd ? (
        <button
          type="button"
          onClick={() => controller.setShowPicker((value) => !value)}
          className="flex size-8 items-center justify-center border border-border-strong bg-surface text-lg font-bold text-personal-accent-strong hover:bg-surface-hover"
          aria-label="Add to another playlist"
        >+</button>
      ) : null}
    </div>
  );
}

function MembershipList({ controller }: { controller: PlaylistController }) {
  if (controller.memberships.length === 0) return <p className="text-xs text-content-muted">Not currently in a playlist.</p>;
  return (
    <div className="divide-y divide-border-subtle border-y border-border-subtle">
      {controller.memberships.map((membership) => (
        <MembershipRow key={membership.playlistId} membership={membership} controller={controller} />
      ))}
    </div>
  );
}

function MembershipRow({ membership, controller }: {
  membership: PlaylistMembership;
  controller: PlaylistController;
}) {
  const confirming = controller.confirmingRemoval === membership.playlistId;
  return (
    <div className="group flex min-h-10 items-center gap-2 px-2 text-sm text-content hover:bg-danger-surface">
      <span className="min-w-0 flex-1 truncate">{membership.playlistName} <span className="text-xs text-content-subtle">#{membership.position + 1}</span></span>
      {confirming ? <RemovalConfirmation membership={membership} controller={controller} /> : (
        <button
          type="button"
          onClick={() => controller.setConfirmingRemoval(membership.playlistId)}
          className="flex size-7 items-center justify-center text-danger-content opacity-0 transition-opacity hover:bg-danger-surface group-hover:opacity-100 focus:opacity-100"
          aria-label={`Remove from ${membership.playlistName}`}
        >×</button>
      )}
    </div>
  );
}

function RemovalConfirmation({ membership, controller }: {
  membership: PlaylistMembership;
  controller: PlaylistController;
}) {
  return (
    <div className="flex items-center gap-1">
      <span className="text-[11px] font-semibold text-danger-content">Remove?</span>
      <button type="button" disabled={controller.busy !== null} onClick={() => void controller.removeFromPlaylist(membership.playlistId)} className="bg-danger-action px-2 py-1 text-[11px] font-bold text-danger-action-content hover:bg-danger-action-hover">Confirm</button>
      <button type="button" onClick={() => controller.setConfirmingRemoval(null)} className="px-2 py-1 text-[11px] font-bold text-content-muted">Cancel</button>
    </div>
  );
}

function PlaylistPicker({ controller }: { controller: PlaylistController }) {
  if (!controller.showPicker) return null;
  return (
    <div className="mt-3 flex flex-wrap gap-2 border-t border-border-subtle pt-3">
      {controller.availablePlaylists.map((playlist) => (
        <button
          key={playlist.id}
          type="button"
          disabled={controller.busy !== null}
          onClick={() => void controller.addToPlaylist(playlist)}
          className="min-h-9 border border-border-strong bg-surface px-3 text-xs font-bold text-content hover:bg-surface-hover hover:text-personal-accent-strong disabled:opacity-50"
        >
          {controller.busy === playlist.id ? 'Adding…' : playlist.name}
        </button>
      ))}
    </div>
  );
}
