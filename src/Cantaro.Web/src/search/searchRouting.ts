import type { SearchResultItem } from './searchApi';

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function hasControlCharacter(value: string): boolean {
  return Array.from(value).some((character) => {
    const codePoint = character.codePointAt(0) ?? 0;
    return codePoint < 32 || codePoint === 127;
  });
}

function decodeRouteSegment(segment: string): string | null {
  if (!segment || segment.includes('\\') || /%(?:2f|5c)/i.test(segment)) return null;

  try {
    const decoded = decodeURIComponent(segment);
    if (
      !decoded
      || decoded === '.'
      || decoded === '..'
      || decoded.includes('/')
      || decoded.includes('\\')
      || /%(?:2e|2f|5c)/i.test(decoded)
      || hasControlCharacter(decoded)
    ) {
      return null;
    }
    return decoded;
  } catch {
    return null;
  }
}

function isSongId(value: string): boolean {
  if (guidPattern.test(value)) return true;

  const separator = value.indexOf(':');
  if (separator < 0) return false;
  const kind = value.slice(0, separator);
  const id = value.slice(separator + 1);
  return (
    ((kind === 'track' || kind === 'observation') && guidPattern.test(id))
    || (kind === 'entry' && guidPattern.test(id))
  );
}

export function trustedCanonicalRoute(route: string): string | null {
  if (!route.startsWith('/') || route.startsWith('//') || route.includes('?') || route.includes('#')) return null;

  const segments = route.split('/');
  const decoded = segments.slice(1).map(decodeRouteSegment);
  if (decoded.some((segment) => segment === null)) return null;

  return isTrustedDecodedRoute(segments.length, decoded) ? route : null;
}

function isTrustedDecodedRoute(segmentCount: number, decoded: Array<string | null>): boolean {
  if (segmentCount === 5) return decoded[0] === 'media' && decoded[1] === 'catalog';
  if (segmentCount !== 4) return false;

  const [area, collection, id] = decoded;
  if (area === 'music' && collection === 'songs') return isSongId(id!);
  if (area === 'music' && collection === 'playlists') return guidPattern.test(id!);
  return area === 'media' && collection === 'library' && guidPattern.test(id!);
}

export function searchResultDomId(item: SearchResultItem): string {
  return `search-option-${item.entityType}-${encodeURIComponent(item.id).replaceAll('%', '-')}`;
}
