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
  selectionVersion: number;
  // Transport intent for the current selection and queue navigation.
  status: 'stopped' | 'paused' | 'playing';
}

export type PlaybackAction =
  | { type: 'replaceQueue'; tracks: PlaybackTrack[]; play?: boolean }
  | { type: 'enqueue'; track: PlaybackTrack; play?: boolean }
  | { type: 'select'; index: number; play?: boolean }
  | { type: 'play' | 'pause'; selectionVersion?: number }
  | { type: 'stop' }
  | { type: 'next' }
  | { type: 'previous' }
  | { type: 'ended'; selectionVersion: number }
  | { type: 'selectSource'; sourceId: string }
  // Clears pending tracks. replaceQueue with [] resets the whole session.
  | { type: 'clearQueue' };

export const initialPlaybackState: PlaybackState = {
  queue: [],
  currentIndex: null,
  selectedSourceId: null,
  selectionVersion: 0,
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

function selectIndex(state: PlaybackState, index: number, play = false): PlaybackState {
  if (!Number.isInteger(index) || index < 0 || index >= state.queue.length) return state;
  const source = preferredSource(state.queue[index]);
  return {
    ...state,
    currentIndex: index,
    selectedSourceId: source?.id ?? null,
    selectionVersion: state.selectionVersion + 1,
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
  return source
    ? { ...state, selectedSourceId: source.id, selectionVersion: state.selectionVersion + 1, status: 'paused' }
    : state;
}

function play(state: PlaybackState): PlaybackState {
  return state.status !== 'playing' && selectedPlaybackSource(state)?.capability.kind === 'embed'
    ? { ...state, status: 'playing' }
    : state;
}

function pause(state: PlaybackState): PlaybackState {
  return state.status === 'playing' ? { ...state, status: 'paused' } : state;
}

function applyTransportIntent(state: PlaybackState, action: Extract<PlaybackAction, { type: 'play' | 'pause' }>) {
  if (action.selectionVersion !== undefined && action.selectionVersion !== state.selectionVersion) return state;
  return action.type === 'play' ? play(state) : pause(state);
}

function replaceQueue(state: PlaybackState, { tracks, play: start }: Extract<PlaybackAction, { type: 'replaceQueue' }>) {
  const replacement = { ...initialPlaybackState, queue: [...tracks] };
  return start ? selectIndex({ ...replacement, selectionVersion: state.selectionVersion }, 0, true) : replacement;
}

function playNow(state: PlaybackState, track: PlaybackTrack): PlaybackState {
  const index = (state.currentIndex ?? -1) + 1;
  const queue = [...state.queue];
  queue.splice(index, 0, track);
  return selectIndex({ ...state, queue }, index, true);
}

function enqueue(state: PlaybackState, { track, play: start }: Extract<PlaybackAction, { type: 'enqueue' }>) {
  return start ? playNow(state, track) : { ...state, queue: [...state.queue, track] };
}

function ended(state: PlaybackState, action: Extract<PlaybackAction, { type: 'ended' }>): PlaybackState {
  if (state.currentIndex === null || state.selectionVersion !== action.selectionVersion) return state;
  return state.currentIndex + 1 < state.queue.length
    ? selectIndex(state, state.currentIndex + 1, true)
    : pause(state);
}

function clearQueue(state: PlaybackState): PlaybackState {
  if (state.currentIndex === null) return initialPlaybackState;
  if (state.currentIndex === state.queue.length - 1) return state;
  return { ...state, queue: state.queue.slice(0, state.currentIndex + 1) };
}

export function playbackReducer(state: PlaybackState, action: PlaybackAction): PlaybackState {
  switch (action.type) {
    case 'replaceQueue':
      return replaceQueue(state, action);
    case 'enqueue':
      return enqueue(state, action);
    case 'select':
      return selectIndex(state, action.index, action.play);
    case 'play':
    case 'pause':
      return applyTransportIntent(state, action);
    case 'stop':
      return initialPlaybackState;
    case 'next':
      return moveIndex(state, 1);
    case 'previous':
      return moveIndex(state, -1);
    case 'ended':
      return ended(state, action);
    case 'selectSource':
      return selectSource(state, action.sourceId);
    case 'clearQueue':
      return clearQueue(state);
  }
}
