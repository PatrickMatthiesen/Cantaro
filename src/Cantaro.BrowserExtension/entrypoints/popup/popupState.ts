import { useCallback, useEffect, useRef, useState } from 'react';
import { configureMediaApi } from '@cantaro/client-shared/media';
import {
  getVerifiedExtensionUser,
  resolveAuthenticatedExtensionConfig,
} from '../../lib/cantaroAuthSession';
import {
  DEFAULT_API_BASE_URL,
  emptyExtensionConfig,
  normalizeApiBaseUrl,
  readExtensionConfig,
  type ExtensionConfig,
} from '../../lib/extensionRuntimeConfig';
import { readEpisodeTrackingTimeout, type EpisodeTrackingTimeoutState } from '../../lib/episodeTrackingTimeout';
import type { SubmitMediaObservationResponse } from '../../lib/mediaObservation';
import { readLatestMediaResolution } from '../../lib/mediaResolutionStorage';
import { readLastPopupTab, rememberPopupTab, type PopupTab } from '../../lib/popupTabPreference';

type StatusType = 'success' | 'error';
type MediaRoute = { kind: 'library' } | { kind: 'entry'; id: string };
type AppliedProfilePreferences = { blurEmailAddress: boolean; theme: 'light' | 'dark' };
type ProfilePreferencesResponse = {
  preferences?: { theme?: 'system' | 'light' | 'dark'; blurEmailAddress?: boolean };
};
export type ShowStatus = (message: string, type: StatusType) => void;

export function isMediaConfigured(config: ExtensionConfig): boolean {
  return Boolean(config.apiBaseUrl.trim() && (config.accessToken.trim() || config.refreshToken.trim()));
}

async function loadProfilePreferences(config: ExtensionConfig): Promise<AppliedProfilePreferences> {
  const authenticated = await resolveAuthenticatedExtensionConfig(config.apiBaseUrl.trim());
  const response = await fetch(`${authenticated.apiBaseUrl}/api/profile`, {
    headers: { Authorization: `Bearer ${authenticated.accessToken}` },
  });
  if (!response.ok) throw new Error('Could not load profile preferences.');
  const profile = await response.json() as ProfilePreferencesResponse;
  return {
    blurEmailAddress: profile.preferences?.blurEmailAddress ?? true,
    theme: profileThemePreference(profile),
  };
}

function profileThemePreference(profile: ProfilePreferencesResponse): 'light' | 'dark' {
  const preference = profile.preferences?.theme ?? 'system';
  if (preference !== 'system') return preference;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

async function applyProfilePreferences(preferences: AppliedProfilePreferences): Promise<void> {
  await browser.storage.local.set({
    blurEmailAddress: preferences.blurEmailAddress,
    cantaroTheme: preferences.theme,
  });
  document.documentElement.dataset.theme = preferences.theme;
}

async function readStoredBlurPreference(): Promise<boolean> {
  const stored = await browser.storage.local.get('blurEmailAddress');
  return typeof stored.blurEmailAddress === 'boolean' ? stored.blurEmailAddress : true;
}

export function createConfigUpdate(
  savedConfig: ExtensionConfig,
  draftApiBaseUrl: string,
  draftWebBaseUrl: string,
  injectLyricsOnYouTube: boolean,
  verboseLogging: boolean,
): { config: ExtensionConfig; apiBaseUrlChanged: boolean } {
  const apiBaseUrl = normalizeApiBaseUrl(draftApiBaseUrl) || DEFAULT_API_BASE_URL;
  const config = {
    ...savedConfig,
    apiBaseUrl,
    webBaseUrl: normalizeApiBaseUrl(draftWebBaseUrl) || emptyExtensionConfig.webBaseUrl,
    injectLyricsOnYouTube,
    verboseLogging,
  } satisfies ExtensionConfig;
  const apiBaseUrlChanged = apiBaseUrl !== normalizeApiBaseUrl(savedConfig.apiBaseUrl);
  if (!apiBaseUrlChanged) return { config, apiBaseUrlChanged };
  return {
    apiBaseUrlChanged,
    config: {
      ...config,
      accessToken: '',
      refreshToken: '',
      accessTokenExpiresAt: '',
      sessionEmail: '',
    },
  };
}

export function usePopupStatus(): { status: { message: string; type: StatusType } | null; showStatus: ShowStatus } {
  const [status, setStatus] = useState<{ message: string; type: StatusType } | null>(null);
  const statusTimeout = useRef<number | null>(null);
  const showStatus = useCallback((message: string, type: StatusType) => {
    setStatus({ message, type });
    if (statusTimeout.current) window.clearTimeout(statusTimeout.current);
    statusTimeout.current = window.setTimeout(() => setStatus(null), 3200);
  }, []);
  useEffect(() => () => {
    if (statusTimeout.current) window.clearTimeout(statusTimeout.current);
  }, []);
  return { status, showStatus };
}

export function useInitialPopupSettings(
  hasSelectedTab: { current: boolean },
  setActiveTab: (tab: PopupTab) => void,
  applyConfig: (config: ExtensionConfig) => void,
  setLoading: (loading: boolean) => void,
  showStatus: ShowStatus,
) {
  useEffect(() => {
    let mounted = true;
    void readLastPopupTab().then((tab) => {
      if (mounted && !hasSelectedTab.current) setActiveTab(tab);
    });
    readExtensionConfig()
      .then((config) => {
        if (mounted) applyConfig(config);
      })
      .catch((error) => {
        console.error('Error loading settings:', error);
        showStatus('Failed to load extension settings', 'error');
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => { mounted = false; };
  }, [applyConfig, hasSelectedTab, setActiveTab, setLoading, showStatus]);
}

export function useInitialEpisodeState(
  setLatestResolution: (resolution: SubmitMediaObservationResponse | null) => void,
  setTrackingTimeout: (timeout: EpisodeTrackingTimeoutState) => void,
) {
  useEffect(() => {
    let mounted = true;
    Promise.all([readLatestMediaResolution(), readEpisodeTrackingTimeout()])
      .then(([resolution, timeout]) => {
        if (!mounted) return;
        setLatestResolution(resolution);
        setTrackingTimeout(timeout);
      })
      .catch((error) => console.error('Failed to load extension episode state:', error));
    return () => { mounted = false; };
  }, [setLatestResolution, setTrackingTimeout]);
}

export function useMediaApiConfiguration(apiBaseUrl: string) {
  useEffect(() => {
    configureMediaApi(async () => {
      const config = await resolveAuthenticatedExtensionConfig(apiBaseUrl.trim());
      return {
        apiBaseUrl: config.apiBaseUrl.trim(),
        accessToken: config.accessToken.trim() || undefined,
        includeCredentials: false,
      };
    });
  }, [apiBaseUrl]);
}

export function useProfilePreferenceState(savedConfig: ExtensionConfig, setBlurEmailAddress: (blur: boolean) => void) {
  useEffect(() => {
    if (!isMediaConfigured(savedConfig)) return;
    let cancelled = false;
    void loadProfilePreferences(savedConfig).then((preferences) => {
      if (cancelled) return;
      setBlurEmailAddress(preferences.blurEmailAddress);
      void applyProfilePreferences(preferences);
    }).catch(() => {
      void readStoredBlurPreference().then((blurEmail) => {
        if (!cancelled) setBlurEmailAddress(blurEmail);
      });
    });
    return () => { cancelled = true; };
  }, [savedConfig, setBlurEmailAddress]);
}

function hasStoredSession(config: ExtensionConfig): boolean {
  return Boolean(config.accessToken.trim() || config.refreshToken.trim());
}

async function verifyAndRefreshSession(config: ExtensionConfig): Promise<{ email: string | null; config: ExtensionConfig }> {
  let email: string | null = null;
  try {
    email = (await getVerifiedExtensionUser(config.apiBaseUrl))?.email ?? null;
  } catch {
    email = null;
  }
  return { email, config: await readExtensionConfig() };
}

export function useVerifiedSession(
  savedConfig: ExtensionConfig,
  setSavedConfig: (config: ExtensionConfig) => void,
  setSessionEmail: (email: string | null) => void,
  setIsCheckingSession: (checking: boolean) => void,
) {
  useEffect(() => {
    let cancelled = false;
    if (!hasStoredSession(savedConfig)) {
      setSessionEmail(null);
      setIsCheckingSession(false);
      return () => { cancelled = true; };
    }
    setIsCheckingSession(true);
    void verifyAndRefreshSession(savedConfig)
      .then((result) => {
        if (cancelled) return;
        setSessionEmail(result.email);
        setSavedConfig(result.config);
      })
      .finally(() => {
        if (!cancelled) setIsCheckingSession(false);
      });
    return () => { cancelled = true; };
  }, [savedConfig.accessToken, savedConfig.refreshToken, savedConfig.apiBaseUrl, setIsCheckingSession, setSavedConfig, setSessionEmail]);
}

export function usePopupTabState() {
  const [activeTab, setActiveTab] = useState<PopupTab>('music');
  const hasSelectedTab = useRef(false);
  const selectTab = useCallback((tab: PopupTab) => {
    hasSelectedTab.current = true;
    setActiveTab(tab);
    void rememberPopupTab(tab);
  }, []);
  return { activeTab, setActiveTab, hasSelectedTab, selectTab };
}

export function usePopupConfigState() {
  const [savedConfig, setSavedConfig] = useState<ExtensionConfig>(emptyExtensionConfig);
  const [draftApiBaseUrl, setDraftApiBaseUrl] = useState(DEFAULT_API_BASE_URL);
  const [draftWebBaseUrl, setDraftWebBaseUrl] = useState(emptyExtensionConfig.webBaseUrl);
  const [draftInjectLyricsOnYouTube, setDraftInjectLyricsOnYouTube] = useState(false);
  const [draftVerboseLogging, setDraftVerboseLogging] = useState(false);
  const [sessionEmail, setSessionEmail] = useState<string | null>(null);
  const [blurEmailAddress, setBlurEmailAddress] = useState(true);
  const [loading, setLoading] = useState(true);
  const [isSigningIn, setIsSigningIn] = useState(false);
  const [isCheckingSession, setIsCheckingSession] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const applyLoadedConfig = useCallback((config: ExtensionConfig) => {
    setSavedConfig(config);
    setDraftApiBaseUrl(config.apiBaseUrl);
    setDraftWebBaseUrl(config.webBaseUrl);
    setDraftInjectLyricsOnYouTube(config.injectLyricsOnYouTube);
    setDraftVerboseLogging(config.verboseLogging);
    setSessionEmail(config.sessionEmail || null);
  }, []);
  return {
    savedConfig, setSavedConfig,
    draftApiBaseUrl, setDraftApiBaseUrl,
    draftWebBaseUrl, setDraftWebBaseUrl,
    draftInjectLyricsOnYouTube, setDraftInjectLyricsOnYouTube,
    draftVerboseLogging, setDraftVerboseLogging,
    sessionEmail, setSessionEmail,
    blurEmailAddress, setBlurEmailAddress,
    loading, setLoading,
    isSigningIn, setIsSigningIn,
    isCheckingSession, setIsCheckingSession,
    isDisconnecting, setIsDisconnecting,
    settingsOpen, setSettingsOpen,
    applyLoadedConfig,
  };
}

export function usePopupMediaState() {
  const [mediaRoute, setMediaRoute] = useState<MediaRoute>({ kind: 'library' });
  const [trackingControlsOpen, setTrackingControlsOpen] = useState(false);
  const [mediaSessionKey, setMediaSessionKey] = useState(0);
  const [latestResolution, setLatestResolution] = useState<SubmitMediaObservationResponse | null>(null);
  const [resolutionError, setResolutionError] = useState<string | null>(null);
  const [isResolving, setIsResolving] = useState(false);
  const [trackingTimeout, setTrackingTimeout] = useState<EpisodeTrackingTimeoutState>({});
  return {
    mediaRoute, setMediaRoute,
    trackingControlsOpen, setTrackingControlsOpen,
    mediaSessionKey, setMediaSessionKey,
    latestResolution, setLatestResolution,
    resolutionError, setResolutionError,
    isResolving, setIsResolving,
    trackingTimeout, setTrackingTimeout,
  };
}
