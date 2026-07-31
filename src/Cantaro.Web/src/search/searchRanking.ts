import type {
  SearchResponse,
  SearchResultGroupId,
  SearchResultItem,
} from './searchApi';

export interface RankedSearchResult {
  groupId: SearchResultGroupId;
  item: SearchResultItem;
}

function searchableText(value?: string): string {
  return (value ?? '')
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase()
    .trim();
}

function textMatchRank(value: string, query: string): number {
  if (!value) return 5;
  if (value === query) return 0;
  if (value.startsWith(query)) return 1;
  if (value.split(/\s+/).some((word) => word.startsWith(query))) return 2;
  if (value.includes(query)) return 3;
  return 5;
}

function itemMatchRank(item: SearchResultItem, query: string): number {
  const titleRank = textMatchRank(searchableText(item.title), query);
  if (titleRank < 5) return titleRank;

  const subtitleRank = textMatchRank(searchableText(item.subtitle), query);
  if (subtitleRank < 5) return subtitleRank + 4;

  const detailRank = textMatchRank(searchableText(item.detail), query);
  return detailRank < 5 ? detailRank + 8 : 13;
}

export function rankSearchResults(
  response: SearchResponse | null,
  groupIds: SearchResultGroupId[],
  query: string,
): RankedSearchResult[] {
  if (!response) return [];

  const normalizedQuery = searchableText(query);
  return groupIds
    .flatMap((groupId, groupIndex) => response.groups[groupId].items.map((item, itemIndex) => ({
      groupId,
      groupIndex,
      item,
      itemIndex,
      matchRank: itemMatchRank(item, normalizedQuery),
    })))
    .sort((left, right) => (
      left.matchRank - right.matchRank
      || left.itemIndex - right.itemIndex
      || left.groupIndex - right.groupIndex
      || left.item.title.localeCompare(right.item.title)
    ))
    .map(({ groupId, item }) => ({ groupId, item }));
}
