import type { LyricsResult } from './lyrics';

export type LyricsPanelSite = 'youtube' | 'youtube_music';

export interface LyricsPanelMount {
  target: HTMLElement;
  layout: 'embedded' | 'floating';
}

const YOUTUBE_SIDEBAR_SELECTORS = ['#secondary-inner', '#secondary'];
const YOUTUBE_MUSIC_PANEL_SELECTORS = [
  'ytmusic-player-page #main-panel',
  'ytmusic-player-page',
  'ytmusic-app',
];

type QueryRoot = Pick<ParentNode, 'querySelector'>;

function firstElement(root: QueryRoot, selectors: string[]): HTMLElement | null {
  for (const selector of selectors) {
    const candidate = root.querySelector(selector);
    if (candidate) return candidate as HTMLElement;
  }
  return null;
}

/** Kept separate from panel rendering so evolving YouTube DOMs can be fixture-tested. */
export function findLyricsPanelMount(root: QueryRoot, site: LyricsPanelSite): LyricsPanelMount | null {
  const target = firstElement(root, site === 'youtube' ? YOUTUBE_SIDEBAR_SELECTORS : YOUTUBE_MUSIC_PANEL_SELECTORS);
  return target ? { target, layout: 'embedded' } : null;
}

export function lyricsText(result: LyricsResult): string | null {
  return result.plainLyrics?.trim() || result.syncedLyrics?.replace(/^\[[^\]]+\]\s*/gm, '').trim() || null;
}

export function shouldRenderLyricsPanel(enabled: boolean, trackId?: string, classification?: string): boolean {
  return enabled && classification === 'music' && Boolean(trackId);
}

/** A new YouTube page context must never inherit text rendered for the prior track. */
export function shouldResetLyricsPanel(previousContextKey: string, nextContextKey: string): boolean {
  return previousContextKey !== nextContextKey;
}

const panelStyles = `
  :host { all: initial; display: block; color-scheme: light dark; font-family: Inter, ui-sans-serif, system-ui, sans-serif; }
  *, *::before, *::after { box-sizing: border-box; }
  .panel { color: #e9edf8; background: #111827; border: 1px solid #303a50; border-radius: 12px; overflow: hidden; box-shadow: 0 6px 8px rgba(15, 23, 42, .24); }
  .panel.floating { position: fixed; right: 20px; bottom: 20px; width: min(360px, calc(100vw - 40px)); max-height: min(64vh, 620px); z-index: 2147483000; }
  .header { display: flex; gap: 10px; align-items: flex-start; padding: 14px 14px 10px; border-bottom: 1px solid #303a50; }
  .heading { min-width: 0; flex: 1; }
  h2 { margin: 0; font-size: 15px; line-height: 20px; font-weight: 700; }
  .track { overflow: hidden; margin: 3px 0 0; color: #aebbd2; font-size: 12px; line-height: 17px; text-overflow: ellipsis; white-space: nowrap; }
  button { appearance: none; border: 0; border-radius: 8px; padding: 5px 8px; color: #e9edf8; background: transparent; font: inherit; font-size: 12px; font-weight: 650; cursor: pointer; }
  button:hover { background: #263148; }
  button:focus-visible { outline: 2px solid #a78bfa; outline-offset: 2px; }
  .body { max-height: 410px; overflow-y: auto; padding: 13px 14px 15px; }
  .lyrics { margin: 0; color: #edf1fa; white-space: pre-wrap; font-size: 14px; line-height: 1.7; }
  .status { margin: 0; color: #c2ccdd; font-size: 13px; line-height: 1.5; }
  .footer { padding: 9px 14px 12px; color: #93a4bf; font-size: 11px; line-height: 1.4; }
  .hidden { display: none; }
  @media (prefers-color-scheme: light) { .panel { color: #172033; background: #ffffff; border-color: #dce3ef; } .header { border-color: #e4eaf3; } .track, .status, .footer { color: #56657e; } .lyrics { color: #172033; } button { color: #293650; } button:hover { background: #eef2f8; } }
  .panel.theme-light { color: #172033; background: #ffffff; border-color: #dce3ef; } .panel.theme-light .header { border-color: #e4eaf3; } .panel.theme-light .track, .panel.theme-light .status, .panel.theme-light .footer { color: #56657e; } .panel.theme-light .lyrics { color: #172033; } .panel.theme-light button { color: #293650; } .panel.theme-light button:hover { background: #eef2f8; }
  .panel.theme-dark { color: #e9edf8; background: #111827; border-color: #303a50; } .panel.theme-dark .header { border-color: #303a50; } .panel.theme-dark .track, .panel.theme-dark .status, .panel.theme-dark .footer { color: #aebbd2; } .panel.theme-dark .lyrics { color: #edf1fa; } .panel.theme-dark button { color: #e9edf8; } .panel.theme-dark button:hover { background: #263148; }
  @media (prefers-reduced-motion: no-preference) { .panel { animation: cantaro-lyrics-enter 180ms cubic-bezier(.16, 1, .3, 1); } @keyframes cantaro-lyrics-enter { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: translateY(0); } } }
`;

interface RenderableLyricsPanel {
  mount: (mount: LyricsPanelMount | null) => void;
  render: (input: LyricsPanelRenderInput) => void;
  dismiss: () => void;
  destroy: () => void;
}

export interface LyricsPanelRenderInput {
  trackLabel: string;
  theme?: 'light' | 'dark';
  result?: LyricsResult;
  loading?: boolean;
  error?: string;
}

export function createLyricsPanel(documentRoot: Document): RenderableLyricsPanel {
  const host = documentRoot.createElement('aside');
  host.setAttribute('data-cantaro-lyrics-panel', '');
  host.setAttribute('aria-label', 'Cantaro lyrics');
  const shadow = host.attachShadow({ mode: 'closed' });
  const style = documentRoot.createElement('style');
  style.textContent = panelStyles;
  const container = documentRoot.createElement('div');
  shadow.append(style, container);
  let currentMount: LyricsPanelMount | null = null;
  let dismissed = false;
  let destroyed = false;

  const mount = (nextMount: LyricsPanelMount | null) => {
    if (dismissed || destroyed) return;
    currentMount = nextMount;
    const parent = nextMount?.target ?? documentRoot.body;
    if (host.parentElement !== parent) parent.append(host);
  };

  const render = ({ trackLabel, theme, result, loading, error }: LyricsPanelRenderInput) => {
    if (dismissed || destroyed) return;
    container.innerHTML = panelMarkup(trackLabel, theme, result, loading, error, currentMount?.layout);
    container.querySelector<HTMLButtonElement>('[data-dismiss]')?.addEventListener('click', dismiss);
  };

  const dismiss = () => {
    dismissed = true;
    host.remove();
  };

  return {
    mount,
    render,
    dismiss,
    destroy: () => {
      destroyed = true;
      host.remove();
    },
  };
}

function panelMarkup(trackLabel: string, theme: 'light' | 'dark' | undefined, result: LyricsResult | undefined, loading: boolean | undefined, error: string | undefined, layout: LyricsPanelMount['layout'] | undefined): string {
  return `<section class="panel ${layout === 'embedded' ? '' : 'floating'} ${theme ? `theme-${theme}` : ''}" aria-live="polite">
    <div class="header"><div class="heading"><h2>Lyrics</h2><p class="track" title="${escapeAttribute(trackLabel)}">${escapeHtml(trackLabel)}</p></div><button type="button" data-dismiss aria-label="Hide Cantaro lyrics">Hide</button></div>
    <div class="body">${panelBody(result, loading, error)}</div>${panelAttribution(result)}
  </section>`;
}

function panelBody(result: LyricsResult | undefined, loading: boolean | undefined, error: string | undefined): string {
  const text = result ? lyricsText(result) : null;
  if (text) return `<p class="lyrics">${escapeHtml(text)}</p>`;
  const status = loading
    ? 'Loading lyrics from Cantaro…'
    : error ?? result?.explanation ?? (result?.state === 'instrumental' ? 'This track is marked as instrumental.' : 'Lyrics are not available for this song.');
  return `<p class="status">${escapeHtml(status)}</p>`;
}

function panelAttribution(result: LyricsResult | undefined): string {
  return result?.attribution ? `<footer class="footer">${escapeHtml(result.attribution)}</footer>` : '';
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character] ?? character);
}

function escapeAttribute(value: string): string {
  return escapeHtml(value).replace(/`/g, '&#96;');
}
