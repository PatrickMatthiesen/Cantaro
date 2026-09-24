import { suggestedSongTitle } from './searchHints';
import { createLyricsSearchForm, type LyricsSearch } from './drawerSearch';
import { displayLyricsText, type LyricsCandidate, type LyricsResult } from '../../../app/music/musicLyrics';
import type { YouTubeLyricsResponse } from './youtubeLyrics';

type LyricsView =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'unmatched' }
  | { kind: 'matched'; response: YouTubeLyricsResponse; choices: LyricsCandidate[]; selectedIndex: number };

interface DrawerOptions {
  document: Document;
  /** Open mode lets the in-app preview and component tests inspect the shipped drawer. */
  shadowMode?: ShadowRootMode;
  getCurrentVideoId: () => string | null;
  getSearchHints?: () => LyricsSearch;
  requestLyrics: (videoId: string, search?: LyricsSearch) => Promise<YouTubeLyricsResponse>;
}

const styles = `
:host{all:initial;color-scheme:dark;font-family:Roboto,Arial,sans-serif;position:fixed;z-index:2147483646;right:16px;bottom:16px;--bg:var(--yt-spec-base-background,#0f0f0f);--text:var(--yt-spec-text-primary,#f1f1f1);--muted:var(--yt-spec-text-secondary,#aaa);--line:var(--yt-spec-10-percent-layer,rgba(255,255,255,.1));--hover:var(--yt-spec-badge-chip-background,rgba(255,255,255,.1));color:var(--text);font-size:14px;line-height:1.45}
*{box-sizing:border-box}
button{font:inherit;color:inherit}
button:focus-visible,select:focus-visible,input:focus-visible,.heading:focus-visible{outline:2px solid var(--text);outline-offset:2px}
.launcher{border:0;background:var(--yt-spec-raised-background,#212121);border-radius:18px;padding:9px 16px;cursor:pointer;font-weight:500}
.launcher:hover{background:color-mix(in srgb,var(--bg) 85%,var(--text))}
.launcher:focus-visible{outline:1px solid var(--muted);outline-offset:-3px}
.icon-button:hover,.resize:hover,.size-controls button:hover{background:var(--hover)}
.panel{display:none;width:380px;height:520px;max-width:calc(100vw - 32px);max-height:calc(100vh - 32px);min-width:min(280px,calc(100vw - 32px));min-height:min(260px,calc(100vh - 32px));overflow:hidden;border:1px solid var(--line);border-radius:12px;background:var(--bg)}
.panel[open]{display:flex;flex-direction:column}
.header{cursor:grab;touch-action:none;user-select:none;display:flex;align-items:center;gap:8px;padding:12px;border-bottom:1px solid var(--line)}
.resize,.icon-button,.size-controls button{display:inline-flex;align-items:center;justify-content:center;flex:none;width:36px;height:36px;padding:0;border:0;border-radius:50%;background:transparent;cursor:pointer}
button svg{display:block;flex:none;width:20px;height:20px;pointer-events:none}
.resize{cursor:nwse-resize;touch-action:none}
.heading{min-width:0;flex:1}.title{margin:0;font-size:16px;font-weight:500}.subtitle{margin:2px 0 0;color:var(--muted);font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.body{min-height:0;flex:1;overflow:auto;padding:16px;scrollbar-width:thin;scrollbar-color:var(--muted) transparent}
.body p{margin:0}.notice-title{font-weight:500}.notice-detail{margin-top:6px!important;color:var(--muted);font-size:13px}.attribution{margin-top:18px!important;color:var(--muted);font-size:11px}
.lyrics{margin:0;white-space:pre-wrap;overflow-wrap:anywhere;font-family:inherit;font-size:14px;font-weight:400;line-height:1.8;color:var(--text)}
.version-picker{display:flex;flex-direction:column;gap:6px;margin-bottom:16px;color:var(--muted);font-size:12px;font-weight:400}
.version-picker select{width:100%;border:1px solid var(--line);border-radius:8px;background:var(--bg);color:var(--text);padding:8px;font:inherit;font-size:13px}
.retry{margin-top:14px;border:0;border-radius:18px;background:var(--hover);padding:8px 16px;cursor:pointer;font-weight:500}
.footer{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:6px 12px 6px 16px;border-top:1px solid var(--line);color:var(--muted);font-size:11px}
.size-controls{display:flex;gap:4px}.size-controls button{width:32px;height:32px;color:var(--text)}
.search-form{padding:16px;border-bottom:1px solid var(--line);display:grid;grid-template-columns:minmax(0,1fr) auto;gap:14px 12px;align-items:end}
.search-form[hidden]{display:none}
.search-form label:first-child{grid-column:1/-1}
.search-form label{display:flex;min-width:0;flex-direction:column;gap:7px;color:var(--muted);font-size:12px;line-height:1.4}
.search-form label:focus-within{color:var(--text)}
.search-form input{width:100%;min-width:0;height:40px;border:1px solid var(--line);border-radius:8px;background:var(--bg);color:var(--text);padding:0 12px;font:inherit;font-size:14px;transition:border-color .12s}
.search-form input:hover{border-color:var(--muted)}
.search-form input:focus-visible{border-color:var(--text);outline:1px solid var(--text);outline-offset:0}
.search-actions{display:flex;justify-content:flex-end}
.search-actions .retry{margin:0;min-height:40px;padding:8px 16px;font-size:13px}
.search-actions .retry:hover{background:color-mix(in srgb,var(--bg) 80%,var(--text))}
.search-form button:disabled{opacity:.5;cursor:default}

.visually-hidden{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}
`;

export interface YouTubeLyricsDrawer {
  refreshForCurrentVideo(): void;
  dispose(): void;
}

function toLyricsView(response: YouTubeLyricsResponse): LyricsView {
  const result = response.lyrics;
  if (!response.song || !result) {
    return { kind: 'unmatched' };
  }
  if (result.state === 'provider_error') {
    return { kind: 'error', message: result.explanation || 'The lyrics provider could not be reached.' };
  }
  const rootChoice: LyricsCandidate = { ...result, label: 'Best match' };
  const candidates = result.candidates?.length ? [...result.candidates] : [rootChoice];
  const selectedIndex = candidates.findIndex(candidate => sameLyricsCandidate(candidate, result));
  if (selectedIndex < 0) {
    candidates.unshift(rootChoice);
    return { kind: 'matched', response, choices: candidates, selectedIndex: 0 };
  }
  const selectedCandidate = candidates[selectedIndex];
  if (!selectedCandidate) return { kind: 'matched', response, choices: [rootChoice], selectedIndex: 0 };
  candidates[selectedIndex] = { ...selectedCandidate, ...result, label: selectedCandidate.label };
  return { kind: 'matched', response, choices: candidates, selectedIndex };
}

function sameLyricsCandidate(candidate: LyricsResult, result: LyricsResult): boolean {
  if (candidate.providerRecordId && result.providerRecordId) {
    return candidate.provider === result.provider && candidate.providerRecordId === result.providerRecordId;
  }
  return candidate.provider === result.provider
    && candidate.state === result.state
    && candidate.matchStatus === result.matchStatus
    && candidate.plainLyrics === result.plainLyrics
    && candidate.syncedLyrics === result.syncedLyrics;
}

function toErrorView(error: unknown): LyricsView {
  return {
    kind: 'error',
    message: error instanceof Error ? error.message : 'Cantaro could not load lyrics.',
  };
}

function appendNotice(doc: Document, body: HTMLElement, message: string, detail: string, attribution?: string) {
  const messageElement = doc.createElement('p');
  messageElement.className = 'notice-title';
  messageElement.textContent = message;
  body.append(messageElement);
  if (detail) {
    const detailElement = doc.createElement('p');
    detailElement.className = 'notice-detail';
    detailElement.textContent = detail;
    body.append(detailElement);
  }
  if (attribution) appendAttribution(doc, body, attribution);
}

function appendAttribution(doc: Document, body: HTMLElement, value: string) {
  const attribution = doc.createElement('p');
  attribution.className = 'attribution';
  attribution.textContent = value;
  body.append(attribution);
}

function renderLyricsView(
  doc: Document,
  body: HTMLElement,
  title: HTMLElement,
  subtitle: HTMLElement,
  view: LyricsView,
  retry: () => void,
  selectChoice: (index: number) => void,
) {
  switch (view.kind) {
    case 'idle': return;
    case 'loading':
      appendNotice(doc, body, 'Loading lyrics', '');
      return;
    case 'error': {
      appendNotice(doc, body, 'Lyrics unavailable', view.message);
      const retryButton = doc.createElement('button');
      retryButton.type = 'button';
      retryButton.className = 'retry';
      retryButton.textContent = 'Retry';
      retryButton.addEventListener('click', retry);
      body.append(retryButton);
      return;
    }
    case 'unmatched':
      appendNotice(doc, body, 'No lyrics found', 'This video is not linked to a song in your Cantaro library. Search by song title and artist.');
      return;
    case 'matched': {
      renderMatchedLyrics(doc, body, title, subtitle, view, selectChoice);
      return;
    }
  }
}

function renderMatchedLyrics(
  doc: Document,
  body: HTMLElement,
  title: HTMLElement,
  subtitle: HTMLElement,
  view: Extract<LyricsView, { kind: 'matched' }>,
  selectChoice: (index: number) => void,
) {
  title.textContent = view.response.song?.title || 'Lyrics';
  subtitle.textContent = view.response.song?.artist || '';
  appendLyricsVersionSelector(doc, body, view, selectChoice);
  const selectedResult = view.choices[view.selectedIndex] ?? view.choices[0];
  if (selectedResult) renderSelectedLyrics(doc, body, selectedResult);
}

function appendLyricsVersionSelector(
  doc: Document,
  body: HTMLElement,
  view: Extract<LyricsView, { kind: 'matched' }>,
  selectChoice: (index: number) => void,
) {
  if (view.choices.length < 2) return;
  const label = doc.createElement('label');
  label.className = 'version-picker';
  label.textContent = 'Lyrics version';
  const select = doc.createElement('select');
  select.setAttribute('aria-label', 'Lyrics version');
  for (const [index, choice] of view.choices.entries()) {
    const option = doc.createElement('option');
    option.value = String(index);
    option.textContent = choice.label;
    if (index === view.selectedIndex) option.setAttribute('selected', '');
    select.append(option);
  }
  select.addEventListener('change', () => selectChoice(Number(select.value)));
  label.append(select);
  body.append(label);
}

function renderSelectedLyrics(doc: Document, body: HTMLElement, result: LyricsResult) {
  if (result.state === 'instrumental') {
    appendNotice(doc, body, 'Instrumental track', result.explanation || 'This track has no lyrics.', result.attribution);
    return;
  }
  if (result.state === 'ambiguous') {
    appendNotice(doc, body, 'Lyrics need review', result.explanation || 'Cantaro found more than one possible lyrics match.', result.attribution);
    return;
  }
  const selected = displayLyricsText(result);
  if (!selected) {
    appendNotice(doc, body, 'Lyrics unavailable', result.explanation || 'No lyrics were found for this song.', result.attribution);
    return;
  }
  const lyrics = doc.createElement('pre');
  lyrics.className = 'lyrics';
  lyrics.textContent = selected.text;
  body.append(lyrics);
  appendAttribution(doc, body, result.attribution);
}

function drawerIcon(doc: Document, path: string): SVGSVGElement {
  const icon = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
  icon.setAttribute('viewBox', '0 0 20 20');
  icon.setAttribute('aria-hidden', 'true');
  icon.setAttribute('fill', 'none');
  icon.setAttribute('stroke', 'currentColor');
  icon.setAttribute('stroke-width', '1.5');
  icon.setAttribute('stroke-linecap', 'round');
  icon.setAttribute('stroke-linejoin', 'round');
  const shape = doc.createElementNS('http://www.w3.org/2000/svg', 'path');
  shape.setAttribute('d', path);
  icon.append(shape);
  return icon;
}

export function mountYouTubeLyricsDrawer(options: DrawerOptions): YouTubeLyricsDrawer {
  const { document: doc } = options;
  const host = doc.createElement('div');
  host.setAttribute('data-cantaro-youtube-lyrics', '');
  const root = host.attachShadow({ mode: options.shadowMode ?? 'closed' });
  const style = doc.createElement('style');
  style.textContent = styles;
  root.append(style);

  const launcher = doc.createElement('button');
  launcher.type = 'button';
  launcher.className = 'launcher';
  launcher.textContent = 'Cantaro lyrics';
  launcher.setAttribute('aria-label', 'Cantaro lyrics');
  launcher.setAttribute('aria-expanded', 'false');
  root.append(launcher);

  const panel = doc.createElement('section');
  panel.className = 'panel';
  panel.setAttribute('aria-label', 'Cantaro lyrics');
  panel.setAttribute('role', 'region');

  const header = doc.createElement('header');
  header.className = 'header';
  const resize = doc.createElement('button');
  resize.type = 'button';
  resize.className = 'resize';
  resize.append(drawerIcon(doc, 'M4 10V4h6M4 4l12 12M10 16h6v-6'));
  resize.setAttribute('aria-label', 'Resize lyrics drawer. Use arrow keys to change its size.');

  const heading = doc.createElement('div');
  heading.className = 'heading';
  heading.tabIndex = 0;
  heading.setAttribute('role', 'button');
  heading.setAttribute('aria-label', 'Move lyrics drawer. Use arrow keys to change its position.');
  const title = doc.createElement('h2');
  title.className = 'title';
  title.textContent = 'Lyrics';
  const subtitle = doc.createElement('p');
  subtitle.className = 'subtitle';
  heading.append(title, subtitle);
  const close = doc.createElement('button');
  close.type = 'button';
  close.className = 'icon-button';
  close.append(drawerIcon(doc, 'M5 5l10 10M15 5L5 15'));
  close.setAttribute('aria-label', 'Close lyrics');
  const searchToggle = doc.createElement('button');
  searchToggle.type = 'button';
  searchToggle.className = 'icon-button';
  searchToggle.setAttribute('aria-label', 'Search lyrics');
  searchToggle.append(drawerIcon(doc, 'M13 13l4 4M14 8.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0'));
  const searchForm = createLyricsSearchForm(doc, query => { void loadForCurrentVideo(query); });
  searchToggle.addEventListener('click', () => {
    searchForm.form.hidden = !searchForm.form.hidden;
    searchToggle.setAttribute('aria-expanded', String(!searchForm.form.hidden));
    if (!searchForm.form.hidden) {
      fillSearchHints();
      searchForm.title.focus();
    }
  });
  header.append(resize, heading, searchToggle, close);

  const body = doc.createElement('div');
  body.className = 'body';
  body.setAttribute('aria-live', 'polite');
  const footer = doc.createElement('footer');
  footer.className = 'footer';
  const footerLabel = doc.createElement('span');
  footerLabel.textContent = 'Cantaro';
  const sizeControls = doc.createElement('div');
  sizeControls.className = 'size-controls';
  const smaller = doc.createElement('button');
  smaller.type = 'button';
  smaller.append(drawerIcon(doc, 'M5 10h10'));
  smaller.setAttribute('aria-label', 'Make lyrics drawer smaller');
  const larger = doc.createElement('button');
  larger.type = 'button';
  larger.append(drawerIcon(doc, 'M5 10h10M10 5v10'));
  larger.setAttribute('aria-label', 'Make lyrics drawer larger');
  sizeControls.append(smaller, larger);
  footer.append(footerLabel, sizeControls);
  panel.append(header, searchForm.form, body, footer);
  root.append(panel);

  doc.documentElement.append(host);

  let open = false;
  let disposed = false;
  let activeRequest = 0;
  let lastVideoId: string | null = null;
  let lastSearch: LyricsSearch | undefined;
  let searchHintsApplied = false;
  let view: LyricsView = { kind: 'idle' };
  let panelWidth = 380;
  let panelHeight = 520;
  let right = 16;
  let bottom = 16;
  let move: { x: number; y: number; right: number; bottom: number } | null = null;
  const setPosition = (nextRight: number, nextBottom: number) => {
    const width = doc.defaultView?.innerWidth ?? 1024;
    const height = doc.defaultView?.innerHeight ?? 768;
    right = Math.max(16, Math.min(nextRight, width - panelWidth - 16));
    bottom = Math.max(16, Math.min(nextBottom, height - panelHeight - 16));
    host.style.right = `${right}px`;
    host.style.bottom = `${bottom}px`;
  };
  let drag: { x: number; y: number; width: number; height: number } | null = null;

  const setSize = (width: number, height: number) => {
    const viewportWidth = doc.defaultView?.innerWidth ?? 1024;
    const viewportHeight = doc.defaultView?.innerHeight ?? 768;
    panelWidth = Math.max(Math.min(280, viewportWidth - 32), Math.min(width, viewportWidth - 32));
    panelHeight = Math.max(Math.min(260, viewportHeight - 32), Math.min(height, viewportHeight - 32));
    panel.style.width = `${panelWidth}px`;
    panel.style.height = `${panelHeight}px`;
    setPosition(right, bottom);
  };

  const fillSearchHints = () => {
    if (searchHintsApplied) return;
    const hints = view.kind === 'matched' && view.response.song
      ? { title: suggestedSongTitle(view.response.song.title), artist: view.response.song.artist ?? '' }
      : options.getSearchHints?.();
    if (!hints) return;
    if (!searchForm.title.value) searchForm.title.value = hints.title;
    if (!searchForm.artist.value) searchForm.artist.value = hints.artist;
    searchHintsApplied = true;
  };

  const render = () => {
    launcher.setAttribute('aria-expanded', String(open));
    launcher.hidden = open;
    panel.toggleAttribute('open', open);
    title.textContent = 'Lyrics';
    body.replaceChildren();
    subtitle.textContent = '';
    if (view.kind === 'unmatched') {
      searchForm.form.hidden = false;
      fillSearchHints();
    }
    searchToggle.setAttribute('aria-expanded', String(!searchForm.form.hidden));
    searchForm.button.disabled = view.kind === 'loading';
    searchForm.button.textContent = view.kind === 'loading' ? 'Searching…' : 'Search LRCLIB';
    searchForm.form.setAttribute('aria-busy', String(view.kind === 'loading'));
    renderLyricsView(doc, body, title, subtitle, view, () => { void loadForCurrentVideo(lastSearch); }, index => {
      if (view.kind !== 'matched' || index < 0 || index >= view.choices.length) return;
      view = { ...view, selectedIndex: index };
      render();
    });
  };

  async function loadForCurrentVideo(search?: LyricsSearch) {
    if (!open || disposed) return;
    const videoId = options.getCurrentVideoId();
    lastVideoId = videoId;
    lastSearch = search;
    const requestId = ++activeRequest;
    if (!videoId) {
      view = { kind: 'unmatched' };
      render();
      return;
    }
    view = { kind: 'loading' };
    render();
    try {
      const response = await options.requestLyrics(videoId, search);
      if (disposed || !open || requestId !== activeRequest) return;
      view = toLyricsView(response);
      render();
    } catch (error) {
      if (disposed || !open || requestId !== activeRequest) return;
      view = toErrorView(error);
      render();
    }
  }

  const openDrawer = () => {
    if (disposed) return;
    open = true;
    view = { kind: 'loading' };
    render();
    void loadForCurrentVideo(lastSearch);
    close.focus();
  };
  const closeDrawer = () => {
    if (!open) return;
    open = false;
    drag = null;
    move = null;
    header.style.cursor = '';
    ++activeRequest;
    render();
    launcher.focus();
  };
  const handleEscape = (event: KeyboardEvent) => {
    if (event.key === 'Escape' && open) {
      event.preventDefault();
      closeDrawer();
    }
  };
  const changeSize = (widthDelta: number, heightDelta = widthDelta) => setSize(panelWidth + widthDelta, panelHeight + heightDelta);
  const onPointerDown = (event: PointerEvent) => {
    if (event.button !== 0) return;
    drag = { x: event.clientX, y: event.clientY, width: panelWidth, height: panelHeight };
    event.preventDefault();
  };
  const onMoveStart = (event: PointerEvent) => {
    if (event.button !== 0 || (event.target as Element).closest('button')) return;
    move = { x: event.clientX, y: event.clientY, right, bottom };
    header.style.cursor = 'grabbing';
    event.preventDefault();
  };
  const onMoveKey = (event: KeyboardEvent) => {
    if (event.target !== heading) return;
    const amount = event.shiftKey ? 80 : 24;
    const increments: Record<string, [number, number]> = {
      ArrowLeft: [amount, 0], ArrowRight: [-amount, 0], ArrowUp: [0, amount], ArrowDown: [0, -amount],
    };
    const delta = increments[event.key];
    if (!delta) return;
    event.preventDefault();
    event.stopPropagation();
    setPosition(right + delta[0], bottom + delta[1]);
  };
  const onPointerMove = (event: PointerEvent) => {
    if (move) setPosition(move.right + move.x - event.clientX, move.bottom + move.y - event.clientY);
    if (drag) changeDragSize(event.clientX, event.clientY);
  };
  const changeDragSize = (x: number, y: number) => {
    if (!drag) return;
    const start = drag;
    setSize(start.width + start.x - x, start.height + start.y - y);
  };
  const onPointerUp = () => { drag = null; move = null; header.style.cursor = ''; };
  const onResizeKey = (event: KeyboardEvent) => {
    const amount = event.shiftKey ? 80 : 32;
    const increments: Record<string, [number, number]> = {
      ArrowLeft: [amount, 0], ArrowUp: [0, amount], ArrowRight: [-amount, 0], ArrowDown: [0, -amount],
    };
    const increment = increments[event.key];
    if (!increment) return;
    event.preventDefault();
    changeSize(...increment);
  };
  const onViewportResize = () => setSize(panelWidth, panelHeight);

  launcher.addEventListener('click', openDrawer);
  close.addEventListener('click', closeDrawer);
  panel.addEventListener('keydown', handleEscape);
  header.addEventListener('pointerdown', onMoveStart);
  heading.addEventListener('keydown', onMoveKey);
  resize.addEventListener('pointerdown', onPointerDown);
  resize.addEventListener('keydown', onResizeKey);
  doc.defaultView?.addEventListener('pointermove', onPointerMove);
  doc.defaultView?.addEventListener('pointerup', onPointerUp);
  doc.defaultView?.addEventListener('pointercancel', onPointerUp);
  doc.defaultView?.addEventListener('resize', onViewportResize);
  smaller.addEventListener('click', () => changeSize(-40));
  larger.addEventListener('click', () => changeSize(40));
  setSize(panelWidth, panelHeight);
  render();

  return {
    refreshForCurrentVideo() {
      if (disposed) return;
      const nextVideoId = options.getCurrentVideoId();
      if (nextVideoId === lastVideoId) return;
      lastVideoId = nextVideoId;
      lastSearch = undefined;
      searchHintsApplied = false;
      searchForm.title.value = '';
      searchForm.artist.value = '';
      searchForm.form.hidden = true;
      ++activeRequest;
      view = open ? { kind: 'loading' } : { kind: 'idle' };
      render();
      if (open) void loadForCurrentVideo();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      ++activeRequest;
      launcher.removeEventListener('click', openDrawer);
      close.removeEventListener('click', closeDrawer);
      panel.removeEventListener('keydown', handleEscape);
      header.removeEventListener('pointerdown', onMoveStart);
      heading.removeEventListener('keydown', onMoveKey);
      resize.removeEventListener('pointerdown', onPointerDown);
      resize.removeEventListener('keydown', onResizeKey);
      doc.defaultView?.removeEventListener('pointermove', onPointerMove);
      doc.defaultView?.removeEventListener('pointerup', onPointerUp);
      doc.defaultView?.removeEventListener('pointercancel', onPointerUp);
      doc.defaultView?.removeEventListener('resize', onViewportResize);
      host.remove();
    },
  };
}
