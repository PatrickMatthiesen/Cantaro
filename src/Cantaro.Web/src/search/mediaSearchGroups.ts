import type { SearchResultItem } from './searchApi';

export function mediaSearchCategory(item: SearchResultItem): 'watch' | 'read' | 'other' {
  switch (item.mediaKind?.toLowerCase()) {
    case 'anime':
    case 'movie':
    case 'series':
      return 'watch';
    case 'manga':
      return 'read';
    default:
      return 'other';
  }
}
