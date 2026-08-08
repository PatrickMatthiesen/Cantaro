export interface YouTubePageContext {
  site: 'youtube' | 'youtube_music';
  externalId: string;
  title?: string;
  artist?: string;
  url: string;
  observedAt: string;
}

export function readYouTubePageContext(
  doc: Document,
  pageUrl: string,
): YouTubePageContext | null {
  const visibleTitle = doc.querySelector<HTMLElement>(
    'ytmusic-player-bar .title, h1.ytd-watch-metadata',
  )?.innerText.trim();
  const visibleArtist = doc.querySelector<HTMLElement>(
    'ytmusic-player-bar .byline, #owner #channel-name',
  )?.innerText.trim();

  return parseYouTubePageContext(
    pageUrl,
    visibleTitle || doc.querySelector<HTMLMetaElement>('meta[name="title"]')?.content,
    visibleArtist || doc.querySelector<HTMLMetaElement>('meta[itemprop="author"]')?.content,
  );
}

function parseYouTubePageContext(
  urlValue: string,
  title?: string,
  artist?: string,
): YouTubePageContext | null {
  const url = new URL(urlValue);
  if (!isYouTubeHost(url.hostname)) return null;
  const externalId = validVideoId(url.searchParams.get('v'));
  if (!externalId) return null;
  return {
    site: url.hostname === 'music.youtube.com' ? 'youtube_music' : 'youtube',
    externalId,
    title: normalizedText(title),
    artist: normalizedText(artist),
    url: url.href,
    observedAt: new Date().toISOString(),
  };
}

function validVideoId(value: string | null): string | undefined {
  const id = value?.trim();
  return id && /^[-_A-Za-z0-9]{6,}$/.test(id) ? id : undefined;
}

function normalizedText(value: string | undefined): string | undefined {
  return value?.trim() || undefined;
}

function isYouTubeHost(hostname: string): boolean {
  return hostname === 'www.youtube.com'
    || hostname === 'youtube.com'
    || hostname === 'music.youtube.com';
}

export function youtubeContextKey(context: YouTubePageContext): string {
  return `${context.site}:${context.externalId}:${context.title ?? ''}:${context.artist ?? ''}`;
}

export function youtubeSongLabel(
  context: YouTubePageContext,
  title?: string,
  artist?: string,
): string {
  return [title || context.title, artist || context.artist]
    .filter(Boolean)
    .join(' · ') || 'Current song';
}
