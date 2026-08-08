import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import {
  createLyricsPanel,
  findLyricsPanelMount,
  shouldRenderLyricsPanel,
  shouldResetLyricsPanel,
  type LyricsPanelRenderInput,
} from './lyricsPanel';
import type { MusicTabContext } from '../../../contracts/musicTabContext';
import { registerTabContext } from '../../../../../platform/messaging/tabContext';
import {
  readYouTubePageContext,
  youtubeContextKey,
  youtubeSongLabel,
  type YouTubePageContext,
} from './youtubePage';
import { loadTrackLyrics, recognizeYouTubeTrack } from './youtubeService';

const LYRICS_REFRESH_DELAY_MS = 180;
const PANEL_RESTORE_DELAY_MS = 600;

export interface YouTubeSettings {
  injectLyrics: boolean;
  theme?: 'light' | 'dark';
}

export interface YouTubeControllerDependencies {
  readSettings(): Promise<YouTubeSettings>;
  watchSettings(listener: () => void): () => void;
}

export interface YouTubeMusicController {
  getSnapshot(): MusicTabContext;
  refreshContext(): Promise<void>;
  dispose(): void;
}

interface LyricsState {
  contextKey: string;
  request: number;
  abort: AbortController | null;
  render: LyricsPanelRenderInput | null;
  panel: ReturnType<typeof createLyricsPanel>;
}

export function createYouTubeMusicController(
  ctx: ContentScriptContext,
  dependencies: YouTubeControllerDependencies,
): YouTubeMusicController {
  let previousContextKey = '';
  let refreshTimer: number | null = null;
  let restoreTimer: number | null = null;
  let snapshot = createSnapshot(null);
  let notifyContextChanged: () => void = () => undefined;
  const lyrics: LyricsState = {
    contextKey: '',
    request: 0,
    abort: null,
    render: null,
    panel: createLyricsPanel(document),
  };

  const refreshContext = async () => {
    const context = readYouTubePageContext(document, location.href);
    const nextKey = context
      ? youtubeContextKey(context)
      : `none:${location.href}`;
    if (nextKey === previousContextKey) return;

    if (shouldResetLyricsPanel(previousContextKey, nextKey)) {
      resetLyricsPanel(lyrics);
    }
    previousContextKey = nextKey;
    snapshot = createSnapshot(context);
    notifyContextChanged();
    queueLyricsRefresh();
  };

  const refreshLyrics = async () => {
    const context = readYouTubePageContext(document, location.href);
    const settings = await dependencies.readSettings();
    if (!settings.injectLyrics || !context) {
      resetLyricsPanel(lyrics);
      return;
    }
    if (renderCachedLyrics(lyrics, context)) return;
    await requestLyrics(lyrics, context, settings.theme);
  };

  const requestLyrics = async (
    state: LyricsState,
    context: YouTubePageContext,
    theme?: 'light' | 'dark',
  ) => {
    const request = beginLyricsRequest(state, youtubeContextKey(context));
    try {
      const render = await loadLyricsRender(context, request.controller.signal, theme);
      if (isStaleLyricsRequest(state, request)) return;
      if (!render) {
        resetLyricsPanel(state);
        return;
      }
      renderLyrics(state, context, render);
    } catch (error) {
      if (isStaleLyricsRequest(state, request)) return;
      renderLyrics(state, context, {
        trackLabel: youtubeSongLabel(context),
        error: error instanceof Error ? error.message : 'Lyrics could not be loaded right now.',
      });
    }
  };

  const queueLyricsRefresh = () => {
    if (refreshTimer !== null) clearTimeout(refreshTimer);
    refreshTimer = ctx.setTimeout(() => {
      refreshTimer = null;
      void refreshLyrics();
    }, LYRICS_REFRESH_DELAY_MS);
  };

  const restoreLyricsPanel = () => {
    if (!lyrics.render) return;
    const context = readYouTubePageContext(document, location.href);
    if (context) lyrics.panel.mount(findLyricsPanelMount(document, context.site));
  };

  const queuePanelRestore = () => {
    if (restoreTimer !== null) return;
    restoreTimer = ctx.setTimeout(() => {
      restoreTimer = null;
      restoreLyricsPanel();
    }, PANEL_RESTORE_DELAY_MS);
  };

  const contextObserver = new MutationObserver(() => void refreshContext());
  contextObserver.observe(document.documentElement, { childList: true, subtree: true });
  const panelObserver = new MutationObserver(queuePanelRestore);
  panelObserver.observe(document.documentElement, { childList: true, subtree: true });
  const stopWatchingSettings = dependencies.watchSettings(queueLyricsRefresh);
  notifyContextChanged = registerTabContext(ctx, () => snapshot);
  ctx.addEventListener(window, 'yt-navigate-finish', () => void refreshContext());
  ctx.addEventListener(window, 'popstate', () => void refreshContext());
  ctx.addEventListener(window, 'wxt:locationchange', () => void refreshContext());
  ctx.onInvalidated(() => {
    contextObserver.disconnect();
    panelObserver.disconnect();
    stopWatchingSettings();
    lyrics.abort?.abort();
    lyrics.panel.destroy();
    if (refreshTimer !== null) clearTimeout(refreshTimer);
    if (restoreTimer !== null) clearTimeout(restoreTimer);
  });

  void refreshContext();
  return {
    getSnapshot: () => snapshot,
    refreshContext,
    dispose: () => ctx.abort(),
  };
}

function createSnapshot(context: YouTubePageContext | null): MusicTabContext {
  if (!context) {
    return {
      feature: 'music',
      provider: location.hostname === 'music.youtube.com' ? 'youtube_music' : 'youtube',
      pageKind: 'track',
      pageUrl: location.href,
      status: 'unsupported',
      message: 'No playable YouTube track was found in this tab.',
    };
  }

  return {
    feature: 'music',
    provider: context.site,
    pageKind: 'track',
    pageUrl: context.url,
    status: 'ready',
    externalId: context.externalId,
    title: context.title,
    artist: context.artist,
  };
}

function resetLyricsPanel(state: LyricsState): void {
  state.abort?.abort();
  state.abort = null;
  state.request += 1;
  state.contextKey = '';
  state.render = null;
  state.panel.destroy();
  state.panel = createLyricsPanel(document);
}

function beginLyricsRequest(
  state: LyricsState,
  contextKey: string,
): { id: number; controller: AbortController } {
  state.abort?.abort();
  const controller = new AbortController();
  state.abort = controller;
  state.request += 1;
  state.contextKey = contextKey;
  return { id: state.request, controller };
}

function renderCachedLyrics(
  state: LyricsState,
  context: YouTubePageContext,
): boolean {
  if (youtubeContextKey(context) !== state.contextKey) return false;
  if (!state.render) return false;
  state.panel.mount(findLyricsPanelMount(document, context.site));
  state.panel.render(state.render);
  return true;
}

function isStaleLyricsRequest(
  state: LyricsState,
  request: { id: number; controller: AbortController },
): boolean {
  if (request.controller.signal.aborted) return true;
  return request.id !== state.request;
}

async function loadLyricsRender(
  context: YouTubePageContext,
  signal: AbortSignal,
  theme?: 'light' | 'dark',
): Promise<LyricsPanelRenderInput | null> {
  const recognition = await recognizeYouTubeTrack(context, signal);
  if (!shouldRenderLyricsPanel(true, recognition?.trackId, recognition?.classification)) return null;
  if (!recognition?.trackId) return null;
  const trackLabel = youtubeSongLabel(context, recognition.title, recognition.artist);
  const result = await loadTrackLyrics(recognition.trackId, signal);
  return { trackLabel, theme, result };
}

function renderLyrics(
  state: LyricsState,
  context: YouTubePageContext,
  render: LyricsPanelRenderInput,
): void {
  state.render = render;
  state.panel.mount(findLyricsPanelMount(document, context.site));
  state.panel.render(render);
}
