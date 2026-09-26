import type { PlaylistCreatePreview } from '@cantaro/client-shared/music';

export class PlaylistCreateRequestGate {
  private generation = 0;
  private pending = false;

  begin() {
    if (this.pending) return null;
    this.pending = true;
    return ++this.generation;
  }

  isCurrent(request: number) { return request === this.generation; }

  cancel() {
    this.generation++;
    this.pending = false;
  }
}

export async function previewAndCreatePlaylist(
  gate: PlaylistCreateRequestGate,
  request: number,
  preview: () => Promise<PlaylistCreatePreview>,
  create: (token: string) => Promise<boolean>,
  onCreating: () => void,
): Promise<{ kind: 'canceled' } | { kind: 'candidates'; preview: PlaylistCreatePreview } | { kind: 'created'; succeeded: boolean }> {
  const result = await preview();
  if (!gate.isCurrent(request)) return { kind: 'canceled' };
  if (result.candidates.length) return { kind: 'candidates', preview: result };
  onCreating();
  const succeeded = await create(result.previewToken);
  return gate.isCurrent(request) ? { kind: 'created', succeeded } : { kind: 'canceled' };
}
