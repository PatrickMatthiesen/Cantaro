import { useEffect, useRef, useState } from 'react';
import type { ConsentStatus } from '../../platform/consent/consentService';
import { getRuntimeConsentStatus, subscribeRuntimeConsent } from '../../platform/consent/runtimeConsentClient';
import { requestYouTubeLyrics, youtubeVideoId } from '../../features/music/youtube/youtubeLyrics';
import type { LyricsResult } from './musicLyrics';

export type CurrentYouTubeState =
  | { kind: 'disabled' }
  | { kind: 'checking' }
  | { kind: 'unsupported' }
  | { kind: 'loading'; videoId: string }
  | { kind: 'unmatched'; videoId: string }
  | { kind: 'song'; videoId: string; song: { id: string; title: string; artist?: string };
      lyrics: LyricsResult | null }
  | { kind: 'error'; message: string };

const initialState: CurrentYouTubeState = { kind: 'checking' };

function permitsLyrics(status: ConsentStatus): boolean {
  return status.authenticated && status.musicLyricsAllowed;
}

function lookupFailure(error: unknown): CurrentYouTubeState {
  return { kind: 'error', message: error instanceof Error
    ? error.message : 'Cantaro could not load the current song.' };
}

async function loadVideo(videoId: string): Promise<CurrentYouTubeState> {
  try {
    const result = await requestYouTubeLyrics(videoId);
    return result.song
      ? { kind: 'song', videoId, song: result.song, lyrics: result.lyrics }
      : { kind: 'unmatched', videoId };
  } catch (error) {
    return lookupFailure(error);
  }
}

export function useYouTubeLyrics(enabled: boolean): CurrentYouTubeState {
  const [state, setState] = useState<CurrentYouTubeState>(initialState);
  const requestRevision = useRef(0);
  const consentAllowed = useRef(false);

  useEffect(() => {
    if (!enabled) {
      consentAllowed.current = false;
      requestRevision.current++;
      setState({ kind: 'disabled' });
      return;
    }

    let active = true;
    let activeTabId: number | undefined;
    let consentRevision = 0;
    const isCurrent = (revision: number) => active && revision === requestRevision.current
      && consentAllowed.current;

    const refresh = async () => {
      const revision = ++requestRevision.current;
      setState({ kind: 'checking' });
      try {
        const [tab] = await browser.tabs.query({ active: true, currentWindow: true });
        if (!isCurrent(revision)) return;
        activeTabId = tab?.id;
        const videoId = tab?.url ? youtubeVideoId(tab.url) : null;
        if (!videoId) {
          setState({ kind: 'unsupported' });
          return;
        }
        setState({ kind: 'loading', videoId });
        const result = await loadVideo(videoId);
        if (isCurrent(revision)) setState(result);
      } catch (error) {
        if (isCurrent(revision)) setState(lookupFailure(error));
      }
    };

    const handleActivated = () => {
      activeTabId = undefined;
      requestRevision.current++;
      setState({ kind: 'checking' });
      if (consentAllowed.current) void refresh();
    };
    const handleUpdated = (tabId: number, change: { status?: string; url?: string }) => {
      if (activeTabId !== tabId || (change.status !== 'complete' && !change.url)) return;
      requestRevision.current++;
      setState({ kind: 'checking' });
      if (consentAllowed.current) void refresh();
    };

    const stopConsent = subscribeRuntimeConsent(status => {
      consentRevision++;
      if (!permitsLyrics(status)) {
        consentAllowed.current = false;
        requestRevision.current++;
        setState({ kind: 'disabled' });
        return;
      }
      consentAllowed.current = true;
      void refresh();
    });
    browser.tabs.onActivated.addListener(handleActivated);
    browser.tabs.onUpdated.addListener(handleUpdated);

    const currentConsentRevision = consentRevision;
    void getRuntimeConsentStatus().then(status => {
      if (!active || consentRevision !== currentConsentRevision) return;
      consentAllowed.current = permitsLyrics(status);
      if (consentAllowed.current) void refresh();
      else setState({ kind: 'disabled' });
    }).catch(() => {
      if (active && consentRevision === currentConsentRevision) {
        consentAllowed.current = false;
        requestRevision.current++;
        setState({ kind: 'disabled' });
      }
    });

    return () => {
      active = false;
      consentAllowed.current = false;
      requestRevision.current++;
      stopConsent();
      browser.tabs.onActivated.removeListener(handleActivated);
      browser.tabs.onUpdated.removeListener(handleUpdated);
    };
  }, [enabled]);

  return state;
}
