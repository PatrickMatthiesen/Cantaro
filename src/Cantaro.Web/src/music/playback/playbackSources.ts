import type { MusicLibrarySong, MusicLibrarySongPlatformLink, MusicLibrarySongSourceIdentity } from '@cantaro/client-shared/music';
import type { PlaybackSource, PlaybackTrack } from './playbackState';

export type SourceResolver = (
  identity: MusicLibrarySongSourceIdentity,
  link: MusicLibrarySongPlatformLink | undefined,
) => PlaybackSource;

const sourceResolvers: Record<string, SourceResolver> = {
  youtube: (identity, link) => ({
    id: `youtube:${identity.externalId}`,
    providerId: 'youtube',
    label: 'YouTube',
    capability: { kind: 'embed', mediaId: identity.externalId, externalUrl: link?.url ?? `https://www.youtube.com/watch?v=${encodeURIComponent(identity.externalId)}` },
  }),
  spotify: (identity, link) => ({
    id: `spotify:${identity.externalId}`,
    providerId: 'spotify',
    label: 'Spotify',
    capability: {
      kind: 'external',
      url: link?.url ?? `https://open.spotify.com/track/${encodeURIComponent(identity.externalId)}`,
      reason: 'Spotify tracks open in Spotify. Cantaro cannot play this source in the app.',
    },
  }),
};

function sourceFromIdentity(
  identity: MusicLibrarySongSourceIdentity,
  link: MusicLibrarySongPlatformLink | undefined,
  handlers: Record<string, SourceResolver>,
): PlaybackSource {
  const resolver = handlers[identity.source];
  if (resolver) return resolver(identity, link);
  return {
    id: `${identity.source}:${identity.externalId}`,
    providerId: identity.source,
    label: identity.source,
    capability: link
      ? { kind: 'external', url: link.url, reason: `${identity.source} opens outside Cantaro.` }
      : { kind: 'unavailable', reason: `Cantaro cannot play this ${identity.source} source yet.` },
  };
}

function sourceFromLink(link: MusicLibrarySongPlatformLink): PlaybackSource {
  return {
    id: `${link.platform}:${link.url}`,
    providerId: link.platform,
    label: link.label,
    capability: { kind: 'external', url: link.url, reason: `${link.label} opens outside Cantaro.` },
  };
}

function linkForIdentity(
  identity: MusicLibrarySongSourceIdentity,
  links: MusicLibrarySongPlatformLink[],
): MusicLibrarySongPlatformLink | undefined {
  const encodedId = encodeURIComponent(identity.externalId);
  return links.find((link) => link.platform === identity.source && link.url.split(/[/?=&#]/).includes(encodedId));
}

/** Resolve source identities without tying session state to any provider's API shape. */
export function playbackTrackFromSong(song: MusicLibrarySong, resolvers: Record<string, SourceResolver> = {}): PlaybackTrack {
  const handlers = { ...sourceResolvers, ...resolvers };
  const sources: PlaybackSource[] = [];
  const usedLinks = new Set<string>();

  for (const identity of song.sourceIdentities) {
    if (!identity.source || !identity.externalId || identity.source === 'isrc' || identity.source === 'musicbrainz') continue;
    const link = linkForIdentity(identity, song.platformLinks);
    if (link) usedLinks.add(link.url);
    sources.push(sourceFromIdentity(identity, link, handlers));
  }

  for (const link of song.platformLinks) {
    if (usedLinks.has(link.url) || link.platform === 'musicbrainz') continue;
    sources.push(sourceFromLink(link));
  }

  return {
    id: song.id,
    title: song.title,
    artist: song.artist,
    artworkUrl: song.thumbnailUrl,
    durationSeconds: song.durationSeconds,
    sources,
  };
}
