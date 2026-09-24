import type { LyricsSearch } from './drawerSearch';

export function lyricsSearchHints(pageTitle: string): LyricsSearch {
  const title = pageTitle.trim().replace(/^\(\d+\)\s+/, '').replace(/ - YouTube(?: Music)?$/i, '').trim();
  const parts = title.split(' - ');
  if (parts.length !== 2) return { title: suggestedSongTitle(title), artist: '' };
  return { artist: parts[0]!.trim().slice(0, 200), title: suggestedSongTitle(parts[1]!) };
}

export function suggestedSongTitle(title: string): string {
  let cleaned = title;
  // Peel balanced inner groups first so nested qualifiers are removed too.
  while (/\([^()]*\)/.test(cleaned)) cleaned = cleaned.replace(/\([^()]*\)/g, ' ');
  return cleaned.replace(/\s+/g, ' ').trim().slice(0, 200);
}
