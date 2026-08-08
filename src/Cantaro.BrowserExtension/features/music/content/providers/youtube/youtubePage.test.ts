import { parseHTML } from 'linkedom';
import { describe, expect, it } from 'vitest';
import { readYouTubePageContext } from './youtubePage';

describe('readYouTubePageContext', () => {
  it('reads a tab-local YouTube Music track context', () => {
    const { document } = parseHTML('<meta name="title" content="Example Song"><meta itemprop="author" content="Example Artist">');
    const context = readYouTubePageContext(
      document,
      'https://music.youtube.com/watch?v=ABC123xyz',
    );
    expect(context).toMatchObject({
      site: 'youtube_music',
      externalId: 'ABC123xyz',
      title: 'Example Song',
      artist: 'Example Artist',
    });
  });

  it('returns null when the page has no valid video identity', () => {
    const { document } = parseHTML('<title>YouTube</title>');
    expect(readYouTubePageContext(document, 'https://www.youtube.com/feed/subscriptions'))
      .toBeNull();
  });
});
