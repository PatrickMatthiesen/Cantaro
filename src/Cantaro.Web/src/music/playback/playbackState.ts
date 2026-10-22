export interface PlaybackTrack {
  id: string;
  title: string;
  artist?: string;
  artworkUrl?: string;
  durationSeconds?: number;
  sources: PlaybackSource[];
}

export interface PlaybackSource {
  id: string;
  providerId: string;
  label: string;
  capability:
    | { kind: 'embed'; mediaId: string; externalUrl?: string }
    | { kind: 'external'; url: string; reason: string }
    | { kind: 'unavailable'; reason: string; externalUrl?: string };
}

export interface PlaybackState {
  queue: PlaybackTrack[];
  currentIndex: number | null;
  selectedSourceId: string | null;
  // This is session intent. The media adapter added by #99 will execute it.
  status: 'stopped' | 'paused' | 'playing';
}

export type PlaybackAction =
  | { type: 'replaceQueue'; tracks: PlaybackTrack[] }
  | { type: 'enqueue'; track: PlaybackTrack }
  | { type: 'select'; index: number; play?: boolean }
  | { type: 'play' }
  | { type: 'pause' }
  | { type: 'stop' }
  | { type: 'next' }
  | { type: 'previous' }
  | { type: 'selectSource'; sourceId: string }
  | { type: 'clearQueue' };

export const initialPlaybackState: PlaybackState = {
  queue: [],
  currentIndex: null,
  selectedSourceId: null,
  status: 'stopped',
};

export function currentPlaybackTrack(state: PlaybackState): PlaybackTrack | null {
  return state.currentIndex === null ? null : state.queue[state.currentIndex] ?? null;
}

export function selectedPlaybackSource(state: PlaybackState): PlaybackSource | null {
  return currentPlaybackTrack(state)?.sources.find((source) => source.id === state.selectedSourceId) ?? null;
}

export function isPlayerVisible(state: PlaybackState): boolean {
  return currentPlaybackTrack(state) !== null;
}

export interface PlaybackAvailability {
  canPlayInApp: boolean;
  reason?: string;
  externalUrl?: string;
  externalLabel?: string;
}

export function playbackAvailability(state: PlaybackState): PlaybackAvailability {
  const track = currentPlaybackTrack(state);
  if (!track) return { canPlayInApp: false, reason: 'Select a track to play.' };

  const source = selectedPlaybackSource(state);
  const fallback = track.sources.find((candidate) => candidate.capability.kind === 'external' || candidate.capability.externalUrl);
  const externalUrl = source?.capability.kind === 'external'
    ? source.capability.url
    : source?.capability.externalUrl ?? (fallback?.capability.kind === 'external' ? fallback.capability.url : fallback?.capability.externalUrl);
  const externalLabel = externalUrl ? (source?.capability.kind === 'external' || source?.capability.externalUrl ? source.label : fallback?.label) : undefined;

  if (source?.capability.kind === 'embed') return { canPlayInApp: true, externalUrl, externalLabel };
  return {
    canPlayInApp: false,
    reason: source?.capability.reason ?? 'No playback source is available for this track.',
    externalUrl,
    externalLabel,
  };
}

function preferredSource(track: PlaybackTrack): PlaybackSource | undefined {
  return track.sources.find((source) => source.capability.kind === 'embed')
    ?? track.sources.find((source) => source.capability.kind === 'external')
    ?? track.sources[0];
}

function selectIndex(state: PlaybackState, index: number, play: boolean): PlaybackState {
  if (!Number.isInteger(index) || index < 0 || index >= state.queue.length) return state;
  const source = preferredSource(state.queue[index]);
  return {
    ...state,
    currentIndex: index,
    selectedSourceId: source?.id ?? null,
    status: play && source?.capability.kind === 'embed' ? 'playing' : 'paused',
  };
}

function moveIndex(state: PlaybackState, offset: number): PlaybackState {
  return state.currentIndex === null
    ? state
    : selectIndex(state, state.currentIndex + offset, state.status === 'playing');
}

function selectSource(state: PlaybackState, sourceId: string): PlaybackState {
  const source = currentPlaybackTrack(state)?.sources.find((candidate) => candidate.id === sourceId);
  return source ? { ...state, selectedSourceId: source.id, status: 'paused' } : state;
}

function play(state: PlaybackState): PlaybackState {
  return selectedPlaybackSource(state)?.capability.kind === 'embed' ? { ...state, status: 'playing' } : state;
}

function pause(state: PlaybackState): PlaybackState {
  return state.status === 'playing' ? { ...state, status: 'paused' } : state;
}

function stop(state: PlaybackState): PlaybackState {
  return state.currentIndex === null ? state : { ...state, status: 'stopped' };
}

export function playbackReducer(state: PlaybackState, action: PlaybackAction): PlaybackState {
  switch (action.type) {
    case 'replaceQueue':
      return { ...initialPlaybackState, queue: [...action.tracks] };
    case 'enqueue':
      return { ...state, queue: [...state.queue, action.track] };
    case 'select':
      return selectIndex(state, action.index, action.play ?? false);
    case 'play':
      return play(state);
    case 'pause':
      return pause(state);
    case 'stop':
      return stop(state);
    case 'next':
      return moveIndex(state, 1);
    case 'previous':
      return moveIndex(state, -1);
    case 'selectSource':
      return selectSource(state, action.sourceId);
    case 'clearQueue':
      return initialPlaybackState;
  }
}
