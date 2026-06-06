import type { SubmitMediaObservationResponse } from './mediaObservation';

const LATEST_MEDIA_RESOLUTION_KEY = 'latestMediaResolution';

export async function saveLatestMediaResolution(response: SubmitMediaObservationResponse): Promise<void> {
  await browser.storage.local.set({ [LATEST_MEDIA_RESOLUTION_KEY]: response });
}

export async function readLatestMediaResolution(): Promise<SubmitMediaObservationResponse | null> {
  const stored = await browser.storage.local.get(LATEST_MEDIA_RESOLUTION_KEY);
  return (stored[LATEST_MEDIA_RESOLUTION_KEY] as SubmitMediaObservationResponse | undefined) ?? null;
}

export async function clearLatestMediaResolution(observationId?: string): Promise<void> {
  if (!observationId) {
    await browser.storage.local.remove(LATEST_MEDIA_RESOLUTION_KEY);
    return;
  }

  const current = await readLatestMediaResolution();
  if (current?.observationId === observationId) {
    await browser.storage.local.remove(LATEST_MEDIA_RESOLUTION_KEY);
  }
}
