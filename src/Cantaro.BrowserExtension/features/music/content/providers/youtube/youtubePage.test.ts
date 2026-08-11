import { parseHTML } from 'linkedom';
import { describe, expect, it } from 'vitest';
import youtubeMusicWatchHtml from './fixtures/youtube-music-watch.html?raw';
import youtubeWatchHtml from './fixtures/youtube-watch.html?raw';
import { readYouTubePageContext } from './youtubePage';

describe('readYouTubePageContext', () => {
  it('reads clean title and channel fields from the sanitized YouTube watch DOM contract', () => {
    const { document } = parseHTML(youtubeWatchHtml);

    expect(readYouTubePageContext(
      document,
      'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
    )).toMatchObject({
      site: 'youtube',
      externalId: 'dQw4w9WgXcQ',
      title: 'Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)',
      artist: 'Rick Astley',
    });
  });

  it('excludes engagement metadata from the sanitized YouTube Music watch DOM contract', () => {
    const { document } = parseHTML(youtubeMusicWatchHtml);

    expect(readYouTubePageContext(
      document,
      'https://music.youtube.com/watch?v=dQw4w9WgXcQ',
    )).toMatchObject({
      site: 'youtube_music',
      externalId: 'dQw4w9WgXcQ',
      title: 'Never Gonna Give You Up',
      artist: 'Rick Astley',
    });
  });

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
