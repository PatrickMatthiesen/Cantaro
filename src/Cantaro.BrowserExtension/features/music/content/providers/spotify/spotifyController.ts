import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import type { MusicTabContext } from '../../../contracts/musicTabContext';
import { registerTabContext } from '../../../../../platform/messaging/tabContext';

export interface SpotifyController {
  getSnapshot(): MusicTabContext;
  dispose(): void;
}

export function createSpotifyController(ctx: ContentScriptContext): SpotifyController {
  const getSnapshot = (): MusicTabContext => ({
    feature: 'music',
    provider: 'spotify',
    pageKind: 'placeholder',
    pageUrl: location.href,
    status: 'unsupported',
    message: 'Spotify page integration is not implemented yet.',
  });
  registerTabContext(ctx, getSnapshot);
  return { getSnapshot, dispose: () => ctx.abort() };
}
