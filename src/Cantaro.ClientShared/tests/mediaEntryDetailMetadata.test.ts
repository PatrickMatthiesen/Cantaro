import { expect, test } from 'bun:test';
import type { ProviderAvailabilityMap } from '../src/media/components/media-entry-detail/providerAvailability';
import type { MediaEntryDetailModel, MediaTitleDetailDto } from '../src/media/services/mediaApi';
import {
  getAioStreamsTargets,
  getStremioTargets,
  mergeTitleMetadata,
} from '../src/media/pages/media-entry-detail/mediaEntryDetailModel';
import { findStremioDetailUrl } from '../src/media/services/playbackLinks';

function createTitle(id: string, providerLinks: MediaTitleDetailDto['providerLinks']): MediaTitleDetailDto {
  return {
    id,
    canonicalTitle: `Title ${id}`,
    synonyms: [],
    mediaKind: 'anime',
    primaryProgressDimension: 'episode',
    releaseStatusDimension: 'airing',
    updatedAt: '2026-10-15T12:00:00Z',
    providerLinks,
  };
}

function createEntry(title: MediaTitleDetailDto): MediaEntryDetailModel {
  return {
    id: 'viewer-entry',
    mediaTitleId: title.id,
    viewerStateStatus: 'loaded',
    isInLibrary: true,
    title,
    provider: 'anilist',
    providerMediaId: '123',
    status: 'watching',
    score: 8,
    providerListNames: [],
    progressEpisodes: 3,
    progressChapters: 0,
    progressVolumes: 0,
    isConnected: true,
    updatedAt: '2026-10-15T11:00:00Z',
    providerLinks: title.providerLinks,
  };
}

test('delayed title hydration merges into the latest viewer state and drafts', async () => {
  const oldLink = { id: 'anilist-link', provider: 'anilist', externalId: '123', linkSource: 'imported' };
  const newLink = { id: 'simkl-link', provider: 'simkl', externalId: 'anime:456', linkSource: 'imported' };
  const fetchedTitle = createTitle('media-1', [oldLink, newLink]);
  let resolveFetch!: (title: MediaTitleDetailDto) => void;
  const fetchTitle = new Promise<MediaTitleDetailDto>((resolve) => { resolveFetch = resolve; });
  let current: MediaEntryDetailModel | null = createEntry(createTitle('media-1', [oldLink]));
  const draft = { selectedStatus: 'completed', progressEpisodes: 12 };
  const hydrate = async () => {
    const title = await fetchTitle;
    current = mergeTitleMetadata(current, 'media-1', 'media-1', title);
  };

  const pendingHydration = hydrate();
  current = {
    ...current!,
    status: 'completed',
    progressEpisodes: 12,
    updatedAt: '2026-10-15T12:01:00Z',
  };
  resolveFetch(fetchedTitle);
  await pendingHydration;

  expect(current?.providerLinks).toEqual([oldLink, newLink]);
  expect(current?.title).toBe(fetchedTitle);
  expect(current?.status).toBe('completed');
  expect(current?.progressEpisodes).toBe(12);
  expect(current?.updatedAt).toBe('2026-10-15T12:01:00Z');
  expect(draft).toEqual({ selectedStatus: 'completed', progressEpisodes: 12 });
});

test('title hydration ignores a response after navigation to another title', () => {
  const current = createEntry(createTitle('media-2', []));
  const fetchedTitle = createTitle('media-1', [
    { id: 'simkl-link', provider: 'simkl', externalId: 'anime:456', linkSource: 'imported' },
  ]);

  expect(mergeTitleMetadata(current, 'media-1', 'media-2', fetchedTitle)).toBe(current);
});

test.each([
  { mediaKind: 'movie', format: 'movie', type: 'movie' as const },
  { mediaKind: 'series', format: 'tv', type: 'series' as const },
  { mediaKind: 'anime', format: 'movie', type: 'movie' as const },
])('AIOStreams uses verified canonical IMDb links for $mediaKind $format', ({ mediaKind, format, type }) => {
  const lastVerifiedAt = '2026-10-15T12:00:00Z';
  const simkl = { id: 'simkl-link', provider: 'simkl', externalId: '456', linkSource: 'imported', lastVerifiedAt };
  const imdb = { id: 'imdb-link', provider: 'imdb', externalId: 'tt1234567', linkSource: 'imported' };
  const availabilityByProviderLink: ProviderAvailabilityMap = {
    'simkl:456': {
      status: 'loaded',
      links: [],
      characters: [],
      stremioTargets: [{ type, id: 'tt7654321' }],
    },
  };
  const title = { ...createTitle('media-1', [simkl, imdb]), mediaKind, format };
  const verifiedTitle = { ...title, providerLinks: [simkl, { ...imdb, lastVerifiedAt }] };

  expect(getAioStreamsTargets({ entry: createEntry(title), availabilityByProviderLink })).toEqual([]);
  expect(getAioStreamsTargets({ entry: createEntry(verifiedTitle), availabilityByProviderLink }))
    .toEqual([{ type, id: 'tt1234567' }]);
  expect(findStremioDetailUrl(getStremioTargets(availabilityByProviderLink['simkl:456']))).toContain('tt7654321');
});

test('AIOStreams retains Kitsu targets only when provider evidence agrees with verified IMDb', () => {
  const link = {
    id: 'simkl-link', provider: 'simkl', externalId: 'anime:456', linkSource: 'imported',
    lastVerifiedAt: '2026-10-15T12:00:00Z',
  };
  const target = { type: 'series' as const, id: 'kitsu:1', episodeMapping: { seasonNumber: null, episodeOffset: 0 } };
  const availabilityByProviderLink: ProviderAvailabilityMap = {
    'simkl:anime:456': {
      status: 'loaded',
      links: [],
      characters: [],
      stremioTargets: [{ type: 'movie', id: 'tt7654321' }, target],
    },
  };
  const title = { ...createTitle('media-1', [link]), format: 'tv' };

  expect(getAioStreamsTargets({ entry: createEntry(title), availabilityByProviderLink })).toEqual([target]);
  const imdb = { ...link, id: 'imdb-link', provider: 'imdb', externalId: 'tt1234567' };
  const linkedTitle = { ...title, providerLinks: [link, imdb] };
  expect(getAioStreamsTargets({ entry: createEntry(linkedTitle), availabilityByProviderLink })).toEqual([]);
});

test('a conflict from a verified provider blocks AIOStreams across providers and keeps Stremio targets', () => {
  const lastVerifiedAt = '2026-10-15T12:00:00Z';
  const simkl = { id: 'simkl-link', provider: 'simkl', externalId: 'anime:456', linkSource: 'imported' };
  const anilist = { id: 'anilist-link', provider: 'anilist', externalId: '123', linkSource: 'imported', lastVerifiedAt };
  const fallback = { type: 'series' as const, id: 'kitsu:1', episodeMapping: { seasonNumber: null, episodeOffset: 0 } };
  const alternative = { ...fallback, id: 'kitsu:2' };
  const availabilityByProviderLink: ProviderAvailabilityMap = {
    'simkl:anime:456': {
      status: 'loaded', links: [], characters: [], stremioTargets: [fallback], aioStreamsIdentityConflict: true,
    },
    'anilist:123': { status: 'loaded', links: [], characters: [], stremioTargets: [alternative] },
  };
  const verifiedEntry = createEntry(createTitle('media-1', [anilist, { ...simkl, lastVerifiedAt }]));
  const unverifiedEntry = createEntry(createTitle('media-1', [anilist, simkl]));

  expect(getAioStreamsTargets({ entry: verifiedEntry, availabilityByProviderLink })).toEqual([]);
  expect(getAioStreamsTargets({ entry: unverifiedEntry, availabilityByProviderLink })).toEqual([alternative]);
  expect(getStremioTargets(availabilityByProviderLink['simkl:anime:456'])).toEqual([fallback]);
});
