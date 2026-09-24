import { describe, expect, it } from 'vitest';
import { isYouTubeLyricsRequest, youtubeVideoId } from './youtubeLyrics';

describe('YouTube video identity', () => {
  it('uses only the watch video ID, ignoring playlist and tracking parameters', () => {
    expect(youtubeVideoId('https://www.youtube.com/watch?v=qmSiJxEOVGs&list=other')).toBe('qmSiJxEOVGs');
    expect(youtubeVideoId('https://music.youtube.com/watch?v=qmSiJxEOVGs')).toBe('qmSiJxEOVGs');
  });

  it.each([
    'https://www.youtube.com.evil.test/watch?v=qmSiJxEOVGs',
    'http://www.youtube.com/watch?v=qmSiJxEOVGs',
    'https://www.youtube.com/results?v=qmSiJxEOVGs',
    'https://www.youtube.com/watch?v=invalid',
    'not a URL',
  ])('rejects unsupported identity %s', url => expect(youtubeVideoId(url)).toBeNull());

  it('rejects malformed background messages', () => {
    expect(isYouTubeLyricsRequest({ type: 'music.youtube.lyrics', correlationId: 'test', payload: { videoId: '../other' } })).toBe(false);
    expect(isYouTubeLyricsRequest(null)).toBe(false);
  });

  it('accepts bounded manual search terms alongside the YouTube video ID', () => {
    expect(isYouTubeLyricsRequest({ type: 'music.youtube.lyrics', correlationId: 'test', payload: {
      videoId: 'qmSiJxEOVGs', search: { title: ' Maybe IDK ', artist: ' Jon Bellion ' },
    } })).toBe(true);
  });

  it('accepts title-only manual search', () => {
    expect(isYouTubeLyricsRequest({ type: 'music.youtube.lyrics', correlationId: 'test', payload: { videoId: 'qmSiJxEOVGs', search: { title: 'Song', artist: '' } } })).toBe(true);
  });

  it.each([
    { title: '', artist: 'Jon Bellion' },

    { title: 'x'.repeat(201), artist: 'Jon Bellion' },
    { title: 'Maybe IDK', artist: 'x'.repeat(201) },
    { title: 'Maybe IDK' },
  ])('rejects invalid manual search data %#', search => {
    expect(isYouTubeLyricsRequest({ type: 'music.youtube.lyrics', correlationId: 'test', payload: {
      videoId: 'qmSiJxEOVGs', search,
    } })).toBe(false);
  });
});
