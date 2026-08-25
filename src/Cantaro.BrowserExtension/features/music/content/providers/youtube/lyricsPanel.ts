import type { LyricsResult } from './youtubeService';
import { currentExtensionNamespace } from '../../../../../platform/diagnostics/extensionIdentity';

export type LyricsPanelSite = 'youtube' | 'youtube_music';

export interface LyricsPanelMount {
  target: HTMLElement;
  layout: 'embedded' | 'floating';
}

export interface LyricsPanelRenderInput {
  trackLabel: string;
  theme?: 'light' | 'dark';
  result?: LyricsResult;
  error?: string;
}

export interface LyricsPanelController {
  mount(mount: LyricsPanelMount | null): void;
  render(input: LyricsPanelRenderInput): void;
  destroy(): void;
}

const YOUTUBE_SIDEBAR_SELECTORS = ['#secondary-inner', '#secondary'];
const YOUTUBE_MUSIC_PANEL_SELECTORS = [
  'ytmusic-player-page #main-panel',
  'ytmusic-player-page',
  'ytmusic-app',
];

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
  @media (prefers-color-scheme: light) { .panel { color: #172033; background: #ffffff; border-color: #dce3ef; } .header { border-color: #e4eaf3; } .track, .status, .footer { color: #56657e; } .lyrics { color: #172033; } button { color: #293650; } button:hover { background: #eef2f8; } }
  .panel.theme-light { color: #172033; background: #ffffff; border-color: #dce3ef; } .panel.theme-light .header { border-color: #e4eaf3; } .panel.theme-light .track, .panel.theme-light .status, .panel.theme-light .footer { color: #56657e; } .panel.theme-light .lyrics { color: #172033; } .panel.theme-light button { color: #293650; } .panel.theme-light button:hover { background: #eef2f8; }
  .panel.theme-dark { color: #e9edf8; background: #111827; border-color: #303a50; } .panel.theme-dark .header { border-color: #303a50; } .panel.theme-dark .track, .panel.theme-dark .status, .panel.theme-dark .footer { color: #aebbd2; } .panel.theme-dark .lyrics { color: #edf1fa; } .panel.theme-dark button { color: #e9edf8; } .panel.theme-dark button:hover { background: #263148; }
`;

export function findLyricsPanelMount(
  root: Pick<ParentNode, 'querySelector'>,
  site: LyricsPanelSite,
): LyricsPanelMount | null {
  const selectors = site === 'youtube' ? YOUTUBE_SIDEBAR_SELECTORS : YOUTUBE_MUSIC_PANEL_SELECTORS;
  const target = selectors
    .map(selector => root.querySelector<HTMLElement>(selector))
    .find((element): element is HTMLElement => Boolean(element));
  return target ? { target, layout: 'embedded' } : null;
}

function lyricsText(result: LyricsResult): string | null {
  const plain = result.plainLyrics?.trim();
  if (plain) return plain;
  return result.syncedLyrics?.replace(/^\[[^\]]+\]\s*/gm, '').trim() || null;
}

export function shouldRenderLyricsPanel(
  enabled: boolean,
  trackId?: string,
  classification?: string,
): boolean {
  return enabled && classification === 'music' && Boolean(trackId);
}

export function shouldResetLyricsPanel(previousContextKey: string, nextContextKey: string): boolean {
  return previousContextKey !== nextContextKey;
}

export function createLyricsPanel(documentRoot: Document): LyricsPanelController {
  const host = documentRoot.createElement('aside');
  const owner = currentExtensionNamespace();
  host.id = `${owner}-lyrics-panel`;
  host.setAttribute('data-cantaro-lyrics-panel', owner);
  host.setAttribute('data-cantaro-owner', owner);
  host.setAttribute('aria-label', 'Cantaro lyrics');
  const shadow = host.attachShadow({ mode: 'closed' });
  const style = documentRoot.createElement('style');
  style.textContent = panelStyles;
  const container = documentRoot.createElement('div');
  shadow.append(style, container);
  let currentMount: LyricsPanelMount | null = null;
  let destroyed = false;

  const destroy = () => {
    destroyed = true;
    host.remove();
  };
  return {
    mount(nextMount) {
      if (destroyed) return;
      currentMount = nextMount;
      const parent = nextMount?.target ?? documentRoot.body;
      if (host.parentElement !== parent) parent.append(host);
    },
    render(input) {
      if (destroyed) return;
      container.innerHTML = panelMarkup(input, currentMount?.layout);
      container.querySelector<HTMLButtonElement>('[data-dismiss]')
        ?.addEventListener('click', destroy);
    },
    destroy,
  };
}

function panelMarkup(
  input: LyricsPanelRenderInput,
  layout: LyricsPanelMount['layout'] | undefined,
): string {
  const theme = input.theme ? `theme-${input.theme}` : '';
  const layoutClass = layout === 'embedded' ? '' : 'floating';
  return `<section class="panel ${layoutClass} ${theme}" aria-live="polite">
    <div class="header"><div class="heading"><h2>Lyrics</h2><p class="track" title="${escapeHtml(input.trackLabel)}">${escapeHtml(input.trackLabel)}</p></div><button type="button" data-dismiss aria-label="Hide Cantaro lyrics">Hide</button></div>
    <div class="body">${panelBody(input)}</div>${panelAttribution(input.result)}
  </section>`;
}

function panelBody(input: LyricsPanelRenderInput): string {
  const text = input.result ? lyricsText(input.result) : null;
  if (text) return `<p class="lyrics">${escapeHtml(text)}</p>`;
  return `<p class="status">${escapeHtml(emptyLyricsMessage(input))}</p>`;
}

function emptyLyricsMessage(input: LyricsPanelRenderInput): string {
  if (input.error) return input.error;
  if (input.result?.explanation) return input.result.explanation;
  if (input.result?.state === 'instrumental') return 'This track is marked as instrumental.';
  return 'Lyrics are not available for this song.';
}

function panelAttribution(result: LyricsResult | undefined): string {
  return result?.attribution
    ? `<footer class="footer">${escapeHtml(result.attribution)}</footer>`
    : '';
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, character => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
  })[character] ?? character);
}
