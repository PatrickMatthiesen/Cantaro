import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  clearEpisodeTrackingPause,
  readEpisodeTrackingPause,
  setEpisodeTrackingPause,
  type EpisodeTrackingPausePreset,
  type EpisodeTrackingPause,
} from '../../features/media/settings/episodeTrackingPreference';

export function useEpisodeTracking(onUpdated: (message: string) => void) {
  const [timeout, setTimeoutState] = useState<EpisodeTrackingPause>({});

  useEffect(() => {
    let active = true;
    void readEpisodeTrackingPause().then((stored) => {
      if (active) setTimeoutState(stored);
    });
    return () => { active = false; };
  }, []);

  const pause = useCallback(async (preset: EpisodeTrackingPausePreset) => {
    setTimeoutState(await setEpisodeTrackingPause(preset));
    onUpdated('Episode tracking paused');
  }, [onUpdated]);

  const resume = useCallback(async () => {
    await clearEpisodeTrackingPause();
    setTimeoutState({});
    onUpdated('Episode tracking resumed');
  }, [onUpdated]);

  const pausedUntil = timeout.disabledUntil ? new Date(timeout.disabledUntil) : null;
  const paused = pausedUntil !== null && pausedUntil > new Date();

  return useMemo(() => ({ timeout, pausedUntil, paused, pause, resume }), [pause, paused, pausedUntil, resume, timeout]);
}
