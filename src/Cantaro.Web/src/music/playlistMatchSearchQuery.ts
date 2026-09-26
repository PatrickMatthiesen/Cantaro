export function playlistMatchSearchQuery(input: string, suggestedQuery: string | null | undefined): string | undefined {
  const query = input.trim();
  if (!query) return undefined;
  return suggestedQuery?.trim() === query ? undefined : query;
}
