const PAUSE_KEY = 'cantaro.media.trackingPause.v1';

export function watchTrackingPauseChanges(listener: () => void): () => void {
  const onChanged = (
    changes: Record<string, { newValue?: unknown; oldValue?: unknown }>,
    areaName: string,
  ) => {
    if (areaName !== 'local') return;
    if (changes[PAUSE_KEY]) listener();
  };
  browser.storage.onChanged.addListener(onChanged);
  return () => browser.storage.onChanged.removeListener(onChanged);
}
