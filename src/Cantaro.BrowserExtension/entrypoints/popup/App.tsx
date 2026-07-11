import type { FormEvent } from 'react';
import { startTransition, useEffect, useRef, useState } from 'react';
import {
  MediaEntryDetailPage,
  MediaLibraryPage,
  configureMediaApi,
} from '@cantaro/client-shared/media';
import { GlassCard, GradientButton } from '@cantaro/client-shared/ui';
import { MediaResolutionPicker } from '../../lib/MediaResolutionPicker';
import {
  beginInteractiveSignIn,
  getVerifiedExtensionUser,
  resolveAuthenticatedExtensionConfig,
  revokeExtensionSession,
} from '../../lib/cantaroAuthSession';
import { MusicLibrary } from './components/MusicLibrary';
import { SettingsPanel } from './components/SettingsPanel';
import { SetupCard } from './components/SetupCard';
import {
  ensureApiBaseUrlPermission,
  DEFAULT_API_BASE_URL,
  emptyExtensionConfig,
  normalizeApiBaseUrl,
  readExtensionConfig,
  saveExtensionConfig,
  type ExtensionConfig,
} from '../../lib/extensionRuntimeConfig';
import {
  clearEpisodeTrackingTimeout,
  readEpisodeTrackingTimeout,
  setEpisodeTrackingTimeout,
  type EpisodeTrackingTimeoutPreset,
  type EpisodeTrackingTimeoutState,
} from '../../lib/episodeTrackingTimeout';
import type {
  MediaObservationDto,
  MediaObservationMessage,
  ResolveMediaObservationRequest,
  SubmitMediaObservationResponse,
} from '../../lib/mediaObservation';
import { readLatestMediaResolution } from '../../lib/mediaResolutionStorage';
import { readActiveTabMusicContext } from '../../lib/musicContext';
import { recognizeActiveYouTube } from '../../lib/musicLibrary';

type StatusType = 'success' | 'error';
type PopupTab = 'music' | 'media';
type MediaRoute = { kind: 'library' } | { kind: 'entry'; id: string };

const suppressEmbeddedHeading = () => undefined;

function isMediaConfigured(config: ExtensionConfig): boolean {
  return Boolean(config.apiBaseUrl.trim() && (config.accessToken.trim() || config.refreshToken.trim()));
}

// fallow-ignore-next-line complexity
function App() {
  const [activeTab, setActiveTab] = useState<PopupTab>('media');
  const [mediaRoute, setMediaRoute] = useState<MediaRoute>({ kind: 'library' });
  const [savedConfig, setSavedConfig] = useState<ExtensionConfig>(emptyExtensionConfig);
  const [draftApiBaseUrl, setDraftApiBaseUrl] = useState(DEFAULT_API_BASE_URL);
  const [draftWebBaseUrl, setDraftWebBaseUrl] = useState(emptyExtensionConfig.webBaseUrl);
  const [draftInjectLyricsOnYouTube, setDraftInjectLyricsOnYouTube] = useState(false);
  const [sessionEmail, setSessionEmail] = useState<string | null>(null);
  const [blurEmailAddress, setBlurEmailAddress] = useState(true);
  const [status, setStatus] = useState<{ message: string; type: StatusType } | null>(null);
  const [loading, setLoading] = useState(true);
  const [isSigningIn, setIsSigningIn] = useState(false);
  const [isCheckingSession, setIsCheckingSession] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [trackingControlsOpen, setTrackingControlsOpen] = useState(false);
  const [mediaSessionKey, setMediaSessionKey] = useState(0);
  const [latestResolution, setLatestResolution] = useState<SubmitMediaObservationResponse | null>(null);
  const [resolutionError, setResolutionError] = useState<string | null>(null);
  const [isResolving, setIsResolving] = useState(false);
  const [trackingTimeout, setTrackingTimeout] = useState<EpisodeTrackingTimeoutState>({});
  const statusTimeout = useRef<number | null>(null);

  const showStatus = (message: string, type: StatusType) => {
    setStatus({ message, type });
    if (statusTimeout.current) {
      window.clearTimeout(statusTimeout.current);
    }
    statusTimeout.current = window.setTimeout(() => setStatus(null), 3200);
  };

  useEffect(() => {
    let mounted = true;

    void readActiveTabMusicContext()
      .then((context) => context ? recognizeActiveYouTube(context) : null)
      .then((recognition) => {
        if (mounted && recognition?.classification === 'music') setActiveTab('music');
      })
      .catch(() => undefined);

    readExtensionConfig()
      .then((config) => {
        if (!mounted) {
          return;
        }

        setSavedConfig(config);
        setDraftApiBaseUrl(config.apiBaseUrl);
        setDraftWebBaseUrl(config.webBaseUrl);
        setDraftInjectLyricsOnYouTube(config.injectLyricsOnYouTube);
        setSessionEmail(config.sessionEmail || null);
      })
      .catch((error) => {
        console.error('Error loading settings:', error);
        showStatus('Failed to load extension settings', 'error');
      })
      .finally(() => {
        if (mounted) {
          setLoading(false);
        }
      });

    return () => {
      mounted = false;
      if (statusTimeout.current) {
        window.clearTimeout(statusTimeout.current);
      }
    };
  }, []);

  useEffect(() => {
    let mounted = true;

    Promise.all([readLatestMediaResolution(), readEpisodeTrackingTimeout()])
      .then(([resolution, timeout]) => {
        if (!mounted) return;
        setLatestResolution(resolution);
        setTrackingTimeout(timeout);
      })
      .catch((error) => {
        console.error('Failed to load extension episode state:', error);
      });

    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    configureMediaApi(async () => {
      const config = await resolveAuthenticatedExtensionConfig(savedConfig.apiBaseUrl.trim());
      return {
        apiBaseUrl: config.apiBaseUrl.trim(),
        accessToken: config.accessToken.trim() || undefined,
        includeCredentials: false,
      };
    });
  }, [savedConfig.apiBaseUrl]);

  useEffect(() => {
    if (!isMediaConfigured(savedConfig)) return;
    let cancelled = false;
    void resolveAuthenticatedExtensionConfig(savedConfig.apiBaseUrl.trim()).then(async (config) => {
      const response = await fetch(`${config.apiBaseUrl}/api/profile`, { headers: { Authorization: `Bearer ${config.accessToken}` } });
      if (!response.ok) return;
      const profile = await response.json() as { preferences?: { theme?: 'system' | 'light' | 'dark'; blurEmailAddress?: boolean } };
      if (cancelled) return;
      const blur = profile.preferences?.blurEmailAddress ?? true;
      setBlurEmailAddress(blur);
      const preference = profile.preferences?.theme ?? 'system';
      const theme = preference === 'system' ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : preference;
      await browser.storage.local.set({ blurEmailAddress: blur, cantaroTheme: theme });
      document.documentElement.dataset.theme = theme;
    }).catch(async () => {
      const stored = await browser.storage.local.get('blurEmailAddress');
      setBlurEmailAddress(typeof stored.blurEmailAddress === 'boolean' ? stored.blurEmailAddress : true);
    });
    return () => { cancelled = true; };
  }, [savedConfig]);

  useEffect(() => {
    let cancelled = false;

    if (!savedConfig.accessToken.trim() && !savedConfig.refreshToken.trim()) {
      setSessionEmail(null);
      setIsCheckingSession(false);
      return () => {
        cancelled = true;
      };
    }

    setIsCheckingSession(true);

    void getVerifiedExtensionUser(savedConfig.apiBaseUrl)
      .then((user) => {
        if (!cancelled) {
          setSessionEmail(user?.email ?? null);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setSessionEmail(null);
        }
      })
      .finally(() => {
        if (!cancelled) {
          void readExtensionConfig().then((config) => {
            if (!cancelled) {
              setSavedConfig(config);
            }
          });
          setIsCheckingSession(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [savedConfig.accessToken, savedConfig.refreshToken, savedConfig.apiBaseUrl]);

  const persistConfig = async (nextConfig: ExtensionConfig, successMessage: string) => {
    const persistedConfig = await saveExtensionConfig(nextConfig);

    setSavedConfig(persistedConfig);
    setDraftApiBaseUrl(persistedConfig.apiBaseUrl);
    setDraftWebBaseUrl(persistedConfig.webBaseUrl);
    setDraftInjectLyricsOnYouTube(persistedConfig.injectLyricsOnYouTube);
    setSessionEmail(persistedConfig.sessionEmail || null);
    setMediaSessionKey((current) => current + 1);
    setSettingsOpen(false);
    startTransition(() => setMediaRoute({ kind: 'library' }));
    showStatus(successMessage, 'success');
  };

  // fallow-ignore-next-line complexity
  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    try {
      const nextApiBaseUrl = normalizeApiBaseUrl(draftApiBaseUrl) || DEFAULT_API_BASE_URL;
      await ensureApiBaseUrlPermission(nextApiBaseUrl);
      const apiBaseUrlChanged = nextApiBaseUrl !== normalizeApiBaseUrl(savedConfig.apiBaseUrl);
      const nextConfig = {
        ...savedConfig,
        apiBaseUrl: nextApiBaseUrl,
        webBaseUrl: normalizeApiBaseUrl(draftWebBaseUrl) || emptyExtensionConfig.webBaseUrl,
        injectLyricsOnYouTube: draftInjectLyricsOnYouTube,
        accessToken: apiBaseUrlChanged ? '' : savedConfig.accessToken,
        refreshToken: apiBaseUrlChanged ? '' : savedConfig.refreshToken,
        accessTokenExpiresAt: apiBaseUrlChanged ? '' : savedConfig.accessTokenExpiresAt,
        sessionEmail: apiBaseUrlChanged ? '' : savedConfig.sessionEmail,
      } satisfies ExtensionConfig;

      await persistConfig(
        nextConfig,
        apiBaseUrlChanged
          ? 'API origin updated. Stored Cantaro session was cleared.'
          : 'Extension settings saved',
      );
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
    const next = await setEpisodeTrackingTimeout(preset);
    setTrackingTimeout(next);
    showStatus('Episode tracking paused', 'success');
  };

  const resumeTracking = async () => {
    await clearEpisodeTrackingTimeout();
    setTrackingTimeout({});
    showStatus('Episode tracking resumed', 'success');
  };

  const hasUnsavedChanges = draftApiBaseUrl !== savedConfig.apiBaseUrl
    || draftWebBaseUrl !== savedConfig.webBaseUrl
    || draftInjectLyricsOnYouTube !== savedConfig.injectLyricsOnYouTube;
  const mediaConfigured = isMediaConfigured(savedConfig);
  const trackingPaused = Boolean(
    trackingTimeout.disabledUntil && new Date(trackingTimeout.disabledUntil) > new Date(),
  );

  return (
    <div className="h-screen overflow-hidden bg-[#f7f5ff] text-slate-900">
      <div className="flex h-full flex-col p-3">
        <header className="flex min-h-12 items-center gap-2 rounded-xl border border-[#e7e2f7] bg-white p-1.5">
          <nav className="flex rounded-xl bg-slate-950 p-0.5" aria-label="Popup section">
            {(['music', 'media'] as const).map((tab) => {
              const active = tab === activeTab;
              return (
                <button
                  key={tab}
                  type="button"
                  onClick={() => setActiveTab(tab)}
                  className={`min-h-9 rounded-[0.625rem] px-3 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600 ${active
                    ? 'bg-white text-slate-950'
                    : 'text-slate-300 hover:bg-white/10 hover:text-white'}`}
                  aria-current={active ? 'page' : undefined}
                >
                  {tab === 'music' ? 'Music' : 'Media'}
                </button>
              );
            })}
          </nav>

          <span className="min-w-0 flex-1 truncate pl-1 text-sm font-semibold text-slate-700">
            Cantaro
          </span>

          {mediaConfigured ? (
            <button
              type="button"
              className={`inline-flex min-h-9 items-center gap-1.5 rounded-xl px-2.5 text-xs font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600 ${trackingControlsOpen
                ? 'bg-violet-100 text-violet-800'
                : 'text-slate-700 hover:bg-slate-100'}`}
              aria-expanded={trackingControlsOpen}
              aria-controls="episode-tracking-controls"
              onClick={() => setTrackingControlsOpen((current) => !current)}
            >
              <span className={`h-2 w-2 rounded-full ${trackingPaused ? 'bg-amber-500' : 'bg-emerald-500'}`} aria-hidden />
              {trackingPaused ? 'Paused' : 'Tracking'}
            </button>
          ) : (
            <span className="rounded-lg bg-amber-100 px-2 py-1 text-xs font-semibold text-amber-800">
              Setup needed
            </span>
          )}

          <button
            type="button"
            className="inline-flex size-9 items-center justify-center rounded-xl text-slate-700 transition-colors hover:bg-slate-100 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600"
            onClick={() => setSettingsOpen(true)}
            aria-label="Open extension settings"
            title="Settings"
          >
            <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9.6 3.7 10 2h4l.4 1.7a8.6 8.6 0 0 1 1.5.9l1.7-.5 2 3.5-1.3 1.2c.1.5.2 1.1.2 1.7s-.1 1.2-.2 1.7l1.3 1.2-2 3.5-1.7-.5a8.6 8.6 0 0 1-1.5.9L14 19h-4l-.4-1.7a8.6 8.6 0 0 1-1.5-.9l-1.7.5-2-3.5 1.3-1.2a7.8 7.8 0 0 1 0-3.4L4.4 7.6l2-3.5 1.7.5a8.6 8.6 0 0 1 1.5-.9Z" />
              <circle cx="12" cy="10.5" r="2.5" />
            </svg>
          </button>
        </header>

        {mediaConfigured && trackingControlsOpen ? (
          <div id="episode-tracking-controls" className="mt-2">
            <EpisodeTrackingTimeoutPanel
              timeout={trackingTimeout}
              onPause={applyTrackingTimeout}
              onResume={resumeTracking}
            />
          </div>
        ) : null}

        {mediaConfigured && latestResolution ? (
          <div className="mt-2">
            <MediaResolutionPicker
              response={latestResolution}
              surface="popup"
              resolving={isResolving}
              error={resolutionError}
              onResolve={resolveObservation}
            />
          </div>
        ) : null}

        <main className="relative mt-2 min-h-0 flex-1 overflow-y-auto overflow-x-hidden rounded-xl bg-white/55" aria-label={`${activeTab === 'music' ? 'Music' : 'Media'} content`}>
          {settingsOpen ? (
            <SettingsPanel
              apiBaseUrl={draftApiBaseUrl} loading={loading} isSigningIn={isSigningIn} isDisconnecting={isDisconnecting}
              webBaseUrl={draftWebBaseUrl}
              isCheckingSession={isCheckingSession} hasUnsavedChanges={hasUnsavedChanges} sessionEmail={sessionEmail}
              blurEmailAddress={blurEmailAddress}
              injectLyricsOnYouTube={draftInjectLyricsOnYouTube}
              defaultApiBaseUrl={DEFAULT_API_BASE_URL} onClose={() => setSettingsOpen(false)} onSubmit={handleSubmit}
              onSignIn={handleSignIn} onDisconnect={handleDisconnect} onApiBaseUrlChange={setDraftApiBaseUrl} onWebBaseUrlChange={setDraftWebBaseUrl}
              onInjectLyricsOnYouTubeChange={setDraftInjectLyricsOnYouTube}
            />
          ) : activeTab === 'music' ? (
            <div className="h-full py-1">
              <MusicLibrary configured={mediaConfigured} onSignIn={handleSignIn} isSigningIn={isSigningIn} />
            </div>
          ) : mediaConfigured ? (
            mediaRoute.kind === 'library' ? (
              <MediaLibraryPage
                key={`library-${mediaSessionKey}`}
                embedded
                density="compact"
                onHeadingChange={suppressEmbeddedHeading}
                onNavigateEntry={(id) => startTransition(() => setMediaRoute({ kind: 'entry', id }))}
              />
            ) : (
              <MediaEntryDetailPage
                key={`entry-${mediaSessionKey}-${mediaRoute.id}`}
                libraryEntryId={mediaRoute.id}
                embedded
                onNavigateBack={() => startTransition(() => setMediaRoute({ kind: 'library' }))}
              />
            )
          ) : (
            <div className="h-full py-1">
              <SetupCard onOpenSettings={() => setSettingsOpen(true)} onSignIn={handleSignIn} isSigningIn={isSigningIn} />
            </div>
          )}
        </main>
      </div>

      {status ? (
        <div className="pointer-events-none absolute right-4 bottom-4 z-50">
          <div
            className={`rounded-2xl border px-4 py-3 text-sm font-medium shadow-xl ${status.type === 'success'
              ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
              : 'border-rose-200 bg-rose-50 text-rose-700'}`}
            role="status"
            aria-live="polite"
          >
            {status.message}
          </div>
        </div>
      ) : null}
    </div>
  );
}

export default App;

function EpisodeTrackingTimeoutPanel({
  timeout,
  onPause,
  onResume,
}: {
  timeout: EpisodeTrackingTimeoutState;
  onPause: (preset: EpisodeTrackingTimeoutPreset) => void | Promise<void>;
  onResume: () => void | Promise<void>;
}) {
  const disabledUntil = timeout.disabledUntil ? new Date(timeout.disabledUntil) : null;
  const active = disabledUntil !== null && disabledUntil > new Date();

  return (
    <GlassCard className="p-2.5">
      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-slate-800">Episode tracking</p>
          <p className="truncate text-xs text-slate-600">
            {active ? `Paused until ${disabledUntil.toLocaleString()}` : 'Active · pause observations for'}
          </p>
        </div>
        <button className="min-h-9 rounded-lg bg-slate-100 px-2.5 text-xs font-semibold text-slate-800 transition-colors hover:bg-slate-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600" type="button" onClick={() => onPause('30m')}>
          30 min
        </button>
        <button className="min-h-9 rounded-lg bg-slate-100 px-2.5 text-xs font-semibold text-slate-800 transition-colors hover:bg-slate-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600" type="button" onClick={() => onPause('2h')}>
          2 hours
        </button>
        <button className="min-h-9 rounded-lg bg-slate-100 px-2.5 text-xs font-semibold text-slate-800 transition-colors hover:bg-slate-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600" type="button" onClick={() => onPause('tomorrow')}>
          Tomorrow
        </button>
        {active ? (
          <GradientButton tone="soft" className="min-h-9 rounded-lg px-2.5 py-1 text-xs" onClick={onResume}>Resume</GradientButton>
        ) : null}
      </div>
    </GlassCard>
  );
}
