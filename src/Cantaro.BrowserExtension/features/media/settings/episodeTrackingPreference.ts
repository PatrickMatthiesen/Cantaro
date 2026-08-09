export interface EpisodeTrackingPause {
  disabledUntil?: string;
}

export type EpisodeTrackingPausePreset = '30m' | '2h' | 'tomorrow';

const EPISODE_TRACKING_PAUSE_KEY = 'cantaro.media.trackingPause.v1';

function parsePause(value: unknown): EpisodeTrackingPause {
  if (!value || typeof value !== 'object') return {};
  const disabledUntil = (value as { disabledUntil?: unknown }).disabledUntil;
  return typeof disabledUntil === 'string' ? { disabledUntil } : {};
}

export async function readEpisodeTrackingPause(): Promise<EpisodeTrackingPause> {
  const stored = await browser.storage.local.get(EPISODE_TRACKING_PAUSE_KEY);
  return parsePause(stored[EPISODE_TRACKING_PAUSE_KEY]);
}

export async function isEpisodeTrackingPaused(now = new Date()): Promise<boolean> {
  const pause = await readEpisodeTrackingPause();
  if (!pause.disabledUntil) return false;
  const disabledUntil = new Date(pause.disabledUntil);
  if (!Number.isNaN(disabledUntil.getTime()) && disabledUntil > now) return true;
  await clearEpisodeTrackingPause();
  return false;
}

export async function setEpisodeTrackingPause(
  preset: EpisodeTrackingPausePreset,
  now = new Date(),
): Promise<EpisodeTrackingPause> {
  const disabledUntil = computeDisabledUntil(preset, now).toISOString();
  const pause = { disabledUntil };
  await browser.storage.local.set({ [EPISODE_TRACKING_PAUSE_KEY]: pause });
  return pause;
}

export async function clearEpisodeTrackingPause(): Promise<void> {
  await browser.storage.local.remove(EPISODE_TRACKING_PAUSE_KEY);
}

export function computeDisabledUntil(
  preset: EpisodeTrackingPausePreset,
  now: Date,
): Date {
  if (preset === '30m') return new Date(now.getTime() + 30 * 60 * 1000);
  if (preset === '2h') return new Date(now.getTime() + 2 * 60 * 60 * 1000);
  const tomorrow = new Date(now);
  tomorrow.setDate(now.getDate() + 1);
  tomorrow.setHours(8, 0, 0, 0);
  return tomorrow;
}
