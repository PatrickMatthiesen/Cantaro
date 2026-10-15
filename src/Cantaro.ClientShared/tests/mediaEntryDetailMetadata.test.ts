import { expect, test } from 'bun:test';
import type { MediaEntryDetailModel, MediaTitleDetailDto } from '../src/media/services/mediaApi';
import { mergeTitleMetadata } from '../src/media/pages/media-entry-detail/mediaEntryDetailModel';

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
