export interface EpisodeTrackingTimeoutState {
  disabledUntil?: string;
}

export type EpisodeTrackingTimeoutPreset = '30m' | '2h' | 'tomorrow';

const EPISODE_TRACKING_TIMEOUT_KEY = 'episodeTrackingTimeout';

export async function readEpisodeTrackingTimeout(): Promise<EpisodeTrackingTimeoutState> {
  const stored = await browser.storage.local.get(EPISODE_TRACKING_TIMEOUT_KEY);
  return (stored[EPISODE_TRACKING_TIMEOUT_KEY] as EpisodeTrackingTimeoutState | undefined) ?? {};
}

export async function isEpisodeTrackingTimedOut(now = new Date()): Promise<boolean> {
  const timeout = await readEpisodeTrackingTimeout();
  if (!timeout.disabledUntil) return false;

  const disabledUntil = new Date(timeout.disabledUntil);
  if (Number.isNaN(disabledUntil.getTime())) {
    await clearEpisodeTrackingTimeout();
    return false;
  }

  if (disabledUntil <= now) {
    await clearEpisodeTrackingTimeout();
    return false;
  }

  return true;
}

export async function setEpisodeTrackingTimeout(preset: EpisodeTrackingTimeoutPreset): Promise<EpisodeTrackingTimeoutState> {
  const disabledUntil = computeDisabledUntil(preset).toISOString();
  const state = { disabledUntil } satisfies EpisodeTrackingTimeoutState;
  await browser.storage.local.set({ [EPISODE_TRACKING_TIMEOUT_KEY]: state });
  return state;
}

export async function clearEpisodeTrackingTimeout(): Promise<void> {
  await browser.storage.local.remove(EPISODE_TRACKING_TIMEOUT_KEY);
}

function computeDisabledUntil(preset: EpisodeTrackingTimeoutPreset): Date {
  const now = new Date();
  if (preset === '30m') return new Date(now.getTime() + 30 * 60 * 1000);
  if (preset === '2h') return new Date(now.getTime() + 2 * 60 * 60 * 1000);

  const tomorrow = new Date(now);
  tomorrow.setDate(now.getDate() + 1);
  tomorrow.setHours(8, 0, 0, 0);
  return tomorrow;
}
