import type { FormEvent } from 'react';
import { startTransition } from 'react';
import {
  beginInteractiveSignIn,
  revokeExtensionSession,
} from '../../lib/cantaroAuthSession';
import {
  ensureApiBaseUrlPermission,
  DEFAULT_API_BASE_URL,
  normalizeApiBaseUrl,
  readExtensionConfig,
  saveExtensionConfig,
  type ExtensionConfig,
} from '../../lib/extensionRuntimeConfig';
import {
  clearEpisodeTrackingTimeout,
  setEpisodeTrackingTimeout,
  type EpisodeTrackingTimeoutPreset,
} from '../../lib/episodeTrackingTimeout';
import type {
  MediaObservationDto,
  MediaObservationMessage,
  ResolveMediaObservationRequest,
} from '../../lib/mediaObservation';
import { PopupView } from './components/PopupView';
import {
  createConfigUpdate,
  isMediaConfigured,
  useInitialEpisodeState,
  useInitialPopupSettings,
  useMediaApiConfiguration,
  usePopupConfigState,
  usePopupMediaState,
  usePopupStatus,
  usePopupTabState,
  useProfilePreferenceState,
  useVerifiedSession,
} from './popupState';

function App() {
  const { activeTab, setActiveTab, hasSelectedTab, selectTab } = usePopupTabState();
  const {
    savedConfig, setSavedConfig,
    sessionEmail, setSessionEmail,
    blurEmailAddress, setBlurEmailAddress,
    loading, setLoading,
    settingsOpen, setSettingsOpen,
    draftApiBaseUrl, setDraftApiBaseUrl,
    draftWebBaseUrl, setDraftWebBaseUrl,
    draftInjectLyricsOnYouTube, setDraftInjectLyricsOnYouTube,
    draftVerboseLogging, setDraftVerboseLogging,
    isSigningIn, setIsSigningIn,
    isCheckingSession, setIsCheckingSession,
    isDisconnecting, setIsDisconnecting,
    applyLoadedConfig,
  } = usePopupConfigState();
  const {
    mediaRoute, setMediaRoute,
    trackingControlsOpen, setTrackingControlsOpen,
    mediaSessionKey, setMediaSessionKey,
    latestResolution, setLatestResolution,
    resolutionError, setResolutionError,
    isResolving, setIsResolving,
    trackingTimeout, setTrackingTimeout,
  } = usePopupMediaState();
  const { status, showStatus } = usePopupStatus();

  useInitialPopupSettings(hasSelectedTab, setActiveTab, applyLoadedConfig, setLoading, showStatus);
  useInitialEpisodeState(setLatestResolution, setTrackingTimeout);
  useMediaApiConfiguration(savedConfig.apiBaseUrl);
  useProfilePreferenceState(savedConfig, setBlurEmailAddress);
  useVerifiedSession(savedConfig, setSavedConfig, setSessionEmail, setIsCheckingSession);

  const persistConfig = async (nextConfig: ExtensionConfig, successMessage: string) => {
    const persistedConfig = await saveExtensionConfig(nextConfig);
    setSavedConfig(persistedConfig);
    setDraftApiBaseUrl(persistedConfig.apiBaseUrl);
    setDraftWebBaseUrl(persistedConfig.webBaseUrl);
    setDraftInjectLyricsOnYouTube(persistedConfig.injectLyricsOnYouTube);
    setDraftVerboseLogging(persistedConfig.verboseLogging);
    setSessionEmail(persistedConfig.sessionEmail || null);
    setMediaSessionKey((current) => current + 1);
    setSettingsOpen(false);
    startTransition(() => setMediaRoute({ kind: 'library' }));
    showStatus(successMessage, 'success');
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    try {
      const update = createConfigUpdate(
        savedConfig,
        draftApiBaseUrl,
        draftWebBaseUrl,
        draftInjectLyricsOnYouTube,
        draftVerboseLogging,
      );
      await ensureApiBaseUrlPermission(update.config.apiBaseUrl);
      await persistConfig(update.config, update.apiBaseUrlChanged
        ? 'API origin updated. Stored Cantaro session was cleared.'
        : 'Extension settings saved');
    } catch (error) {
      console.error('Error saving settings:', error);
      showStatus('Failed to save extension settings', 'error');
    }
  };

  const handleSignIn = async () => {
    const apiBaseUrl = normalizeApiBaseUrl(draftApiBaseUrl) || DEFAULT_API_BASE_URL;
    setIsSigningIn(true);
    try {
      await ensureApiBaseUrlPermission(apiBaseUrl);
      const nextConfig = await beginInteractiveSignIn(apiBaseUrl);
      await persistConfig(nextConfig, 'Signed in to Cantaro');
      setSessionEmail(nextConfig.sessionEmail || null);
      await browser.runtime.sendMessage({ type: 'DRAIN_MEDIA_OBSERVATION_QUEUE' }).catch(() => null);
    } catch (error) {
      console.error('Extension sign-in failed:', error);
      showStatus(error instanceof Error ? error.message : 'Sign-in failed.', 'error');
    } finally {
      setIsSigningIn(false);
    }
  };

  const handleDisconnect = async () => {
    const apiBaseUrl = normalizeApiBaseUrl(savedConfig.apiBaseUrl) || DEFAULT_API_BASE_URL;
    setIsDisconnecting(true);
    try {
      await revokeExtensionSession(apiBaseUrl);
      const clearedConfig = await readExtensionConfig();
      setSavedConfig(clearedConfig);
      setDraftApiBaseUrl(clearedConfig.apiBaseUrl);
      setSessionEmail(null);
      setMediaSessionKey((current) => current + 1);
      showStatus('Cantaro session cleared', 'success');
    } catch (error) {
      console.error('Extension sign-out failed:', error);
      showStatus('Failed to clear the stored Cantaro session.', 'error');
    } finally {
      setIsDisconnecting(false);
    }
  };

  const resolveObservation = async (observationId: string, request: ResolveMediaObservationRequest) => {
    setIsResolving(true);
    setResolutionError(null);
    try {
      const response = await browser.runtime.sendMessage({
        type: 'RESOLVE_MEDIA_OBSERVATION',
        payload: { observationId, request },
      } satisfies MediaObservationMessage);
      const resolved = (response as { payload?: MediaObservationDto }).payload;
      setLatestResolution(null);
      setMediaSessionKey((current) => current + 1);
      showStatus(`Resolved ${resolved?.observedTitle ?? 'episode'}`, 'success');
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to resolve episode.';
      setResolutionError(message);
      showStatus(message, 'error');
    } finally {
      setIsResolving(false);
    }
  };

  const applyTrackingTimeout = async (preset: EpisodeTrackingTimeoutPreset) => {
    setTrackingTimeout(await setEpisodeTrackingTimeout(preset));
    showStatus('Episode tracking paused', 'success');
  };

  const resumeTracking = async () => {
    await clearEpisodeTrackingTimeout();
    setTrackingTimeout({});
    showStatus('Episode tracking resumed', 'success');
  };

  const hasUnsavedChanges = draftApiBaseUrl !== savedConfig.apiBaseUrl
    || draftWebBaseUrl !== savedConfig.webBaseUrl
    || draftInjectLyricsOnYouTube !== savedConfig.injectLyricsOnYouTube
    || draftVerboseLogging !== savedConfig.verboseLogging;
  const mediaConfigured = isMediaConfigured(savedConfig);
  const trackingPaused = Boolean(trackingTimeout.disabledUntil && new Date(trackingTimeout.disabledUntil) > new Date());

  return <PopupView
    activeTab={activeTab}
    onSelectTab={selectTab}
    mediaConfigured={mediaConfigured}
    trackingControlsOpen={trackingControlsOpen}
    onToggleTrackingControls={() => setTrackingControlsOpen((current) => !current)}
    trackingPaused={trackingPaused}
    trackingTimeout={trackingTimeout}
    onPauseTracking={applyTrackingTimeout}
    onResumeTracking={resumeTracking}
    latestResolution={latestResolution}
    isResolving={isResolving}
    resolutionError={resolutionError}
    onResolveObservation={resolveObservation}
    settingsOpen={settingsOpen}
    onOpenSettings={() => setSettingsOpen(true)}
    settingsPanelProps={{
      apiBaseUrl: draftApiBaseUrl,
      webBaseUrl: draftWebBaseUrl,
      loading,
      isSigningIn,
      isDisconnecting,
      isCheckingSession,
      hasUnsavedChanges,
      sessionEmail,
      blurEmailAddress,
      injectLyricsOnYouTube: draftInjectLyricsOnYouTube,
      verboseLogging: draftVerboseLogging,
      defaultApiBaseUrl: DEFAULT_API_BASE_URL,
      onClose: () => setSettingsOpen(false),
      onSubmit: handleSubmit,
      onSignIn: handleSignIn,
      onDisconnect: handleDisconnect,
      onApiBaseUrlChange: setDraftApiBaseUrl,
      onWebBaseUrlChange: setDraftWebBaseUrl,
      onInjectLyricsOnYouTubeChange: setDraftInjectLyricsOnYouTube,
      onVerboseLoggingChange: setDraftVerboseLogging,
    }}
    musicLibraryProps={{ configured: mediaConfigured, onSignIn: handleSignIn, isSigningIn }}
    setupCardProps={{ onOpenSettings: () => setSettingsOpen(true), onSignIn: handleSignIn, isSigningIn }}
    mediaRoute={mediaRoute}
    mediaSessionKey={mediaSessionKey}
    onNavigateEntry={(id) => startTransition(() => setMediaRoute({ kind: 'entry', id }))}
    onNavigateLibrary={() => startTransition(() => setMediaRoute({ kind: 'library' }))}
    status={status}
  />;
}

export default App;
