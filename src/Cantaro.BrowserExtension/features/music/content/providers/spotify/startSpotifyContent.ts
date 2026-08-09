import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import { createSpotifyController } from './spotifyController';

export function startSpotifyContent(ctx: ContentScriptContext): void {
  createSpotifyController(ctx);
}
