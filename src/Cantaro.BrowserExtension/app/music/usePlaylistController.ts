import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { useMemo, useState } from 'react';
import { addSongToPlaylist, removeSongFromPlaylist } from './musicService';

type Playlist = MusicLibraryResponse['playlists'][number];
export type PlaylistMembership = MusicLibrarySong['playlists'][number];

function selectedYouTubeVersion(youtubeIds: string[], selectedId: string) {
  if (youtubeIds.length <= 1) return selectedId || undefined;
  if (selectedId) return selectedId;
  throw new Error('Choose which YouTube version to sync.');
}

function membershipName(memberships: PlaylistMembership[], playlistId: string) {
  return memberships.find((item) => item.playlistId === playlistId)?.playlistName ?? 'playlist';
}

export function usePlaylistController(song: MusicLibrarySong, playlists: MusicLibraryResponse['playlists']) {
  const [memberships, setMemberships] = useState(song.playlists);
  const [busy, setBusy] = useState<string | null>(null);
  const [showPicker, setShowPicker] = useState(false);
  const [confirmingRemoval, setConfirmingRemoval] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const youtubeIds = useMemo(() => song.sourceIdentities
    .filter((identity) => identity.source === 'youtube')
    .map((identity) => identity.externalId), [song.sourceIdentities]);
  const [selectedYouTubeId, setSelectedYouTubeId] = useState(
    youtubeIds.length === 1 ? youtubeIds[0]! : '',
  );
  const trackId = song.id.startsWith('track:') ? song.id.slice(6) : null;
  const availablePlaylists = playlists.filter((playlist) =>
    !memberships.some((membership) => membership.playlistId === playlist.id));

  const addToPlaylist = async (playlist: Playlist) => {
    if (!trackId) return;
    setBusy(playlist.id);
    setMessage(null);
    try {
      await addSongToPlaylist(trackId, playlist.id, selectedYouTubeVersion(youtubeIds, selectedYouTubeId));
      setMemberships((current) => [...current, {
        playlistId: playlist.id,
        playlistName: playlist.name,
        position: playlist.entryCount,
      }]);
      setMessage(`Added to ${playlist.name} and synced.`);
      setShowPicker(false);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not add song.');
    } finally {
      setBusy(null);
    }
  };

  const removeFromPlaylist = async (playlistId: string) => {
    if (!trackId) return;
    const playlistName = membershipName(memberships, playlistId);
    setBusy(playlistId);
    setMessage(null);
    try {
      await removeSongFromPlaylist(trackId, playlistId, selectedYouTubeId || undefined);
      setMemberships((current) => current.filter((item) => item.playlistId !== playlistId));
      setMessage(`Removed from ${playlistName} and synced.`);
      setConfirmingRemoval(null);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not remove song.');
    } finally {
      setBusy(null);
    }
  };

  return {
    memberships,
    busy,
    showPicker,
    setShowPicker,
    confirmingRemoval,
    setConfirmingRemoval,
    message,
    youtubeIds,
    selectedYouTubeId,
    setSelectedYouTubeId,
    trackId,
    availablePlaylists,
    addToPlaylist,
    removeFromPlaylist,
  };
}

export type PlaylistController = ReturnType<typeof usePlaylistController>;
