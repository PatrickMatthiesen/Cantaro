import type { FormEvent } from 'react';
import { startTransition, useEffect, useRef, useState } from 'react';
import { GlassCard, GradientButton } from '../../../Cantaro.Web/src/components/ui/GlassComponents';
import { MediaEntryDetailPage } from '../../../Cantaro.Web/src/pages/MediaEntryDetailPage';
import { MediaLibraryPage } from '../../../Cantaro.Web/src/pages/MediaLibraryPage';
import { configureMediaApi } from '../../../Cantaro.Web/src/services/mediaApi';
import {
  beginInteractiveSignIn,
  getVerifiedExtensionUser,
  resolveAuthenticatedExtensionConfig,
  revokeExtensionSession,
} from '../../lib/cantaroAuthSession';
import { MusicPlaceholder } from './components/MusicPlaceholder';
import { SettingsPanel } from './components/SettingsPanel';
import { SetupCard } from './components/SetupCard';
import {
  DEFAULT_API_BASE_URL,
  emptyExtensionConfig,
  normalizeApiBaseUrl,
  readExtensionConfig,
  saveExtensionConfig,
  type ExtensionConfig,
} from '../../lib/extensionRuntimeConfig';

type StatusType = 'success' | 'error';
type PopupTab = 'music' | 'media';
type MediaRoute = { kind: 'library' } | { kind: 'entry'; id: string };

function isMediaConfigured(config: ExtensionConfig): boolean {
  return Boolean(config.apiBaseUrl.trim() && (config.accessToken.trim() || config.refreshToken.trim()));
}

function App() {
  const [activeTab, setActiveTab] = useState<PopupTab>('media');
  const [mediaRoute, setMediaRoute] = useState<MediaRoute>({ kind: 'library' });
  const [savedConfig, setSavedConfig] = useState<ExtensionConfig>(emptyExtensionConfig);
  const [draftApiBaseUrl, setDraftApiBaseUrl] = useState(DEFAULT_API_BASE_URL);
  const [sessionEmail, setSessionEmail] = useState<string | null>(null);
  const [status, setStatus] = useState<{ message: string; type: StatusType } | null>(null);
  const [loading, setLoading] = useState(true);
  const [isSigningIn, setIsSigningIn] = useState(false);
  const [isCheckingSession, setIsCheckingSession] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [mediaSessionKey, setMediaSessionKey] = useState(0);
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

    readExtensionConfig()
      .then((config) => {
        if (!mounted) {
          return;
        }

        setSavedConfig(config);
        setDraftApiBaseUrl(config.apiBaseUrl);
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
    setSessionEmail(persistedConfig.sessionEmail || null);
    setMediaSessionKey((current) => current + 1);
    setSettingsOpen(false);
    startTransition(() => setMediaRoute({ kind: 'library' }));
    showStatus(successMessage, 'success');
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    try {
      const nextApiBaseUrl = normalizeApiBaseUrl(draftApiBaseUrl) || DEFAULT_API_BASE_URL;
      const apiBaseUrlChanged = nextApiBaseUrl !== normalizeApiBaseUrl(savedConfig.apiBaseUrl);
      const nextConfig = {
        ...savedConfig,
        apiBaseUrl: nextApiBaseUrl,
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
      const nextConfig = await beginInteractiveSignIn(apiBaseUrl);
      await persistConfig(nextConfig, 'Signed in to Cantaro');
      setSessionEmail(nextConfig.sessionEmail || null);
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

  const hasUnsavedChanges = draftApiBaseUrl !== savedConfig.apiBaseUrl;
  const mediaConfigured = isMediaConfigured(savedConfig);

  return (
    <div className="relative min-h-screen overflow-hidden text-gray-900">
      <div className="absolute -left-20 top-0 h-72 w-72 rounded-full bg-cyan-400/20 blur-3xl" aria-hidden />
      <div className="absolute right-0 top-8 h-80 w-80 rounded-full bg-fuchsia-400/16 blur-3xl" aria-hidden />

      <div className="relative z-10 flex min-h-screen flex-col p-4">
        <GlassCard className="p-2">
          <div className="flex items-center gap-3">
            <div className="rounded-full bg-slate-950/78 p-1">
              <div className="flex items-center gap-1">
                {(['music', 'media'] as const).map((tab) => {
                  const active = tab === activeTab;
                  return (
                    <button
                      key={tab}
                      type="button"
                      onClick={() => setActiveTab(tab)}
                      className={`rounded-full px-4 py-2 text-sm font-semibold transition ${active
                        ? 'bg-white text-slate-950 shadow-sm'
                        : 'text-white/72 hover:bg-white/10 hover:text-white'}`}
                    >
                      {tab === 'music' ? 'Music' : 'Media'}
                    </button>
                  );
                })}
              </div>
            </div>

            <div>
              <p className="text-xs tracking-[0.28em] text-gray-500 uppercase">Cantaro popup</p>
              <p className="text-sm font-medium text-gray-700">
                {mediaConfigured ? 'Shared media library UI loaded' : 'Media tab needs setup'}
              </p>
            </div>

            <div className="ml-auto flex items-center gap-2">
              <span className={`rounded-full px-3 py-1 text-xs font-semibold ${mediaConfigured
                ? 'bg-emerald-100 text-emerald-700'
                : 'bg-amber-100 text-amber-700'}`}>
                {mediaConfigured ? 'Ready' : 'Setup needed'}
              </span>
              <GradientButton tone="soft" onClick={() => setSettingsOpen(true)}>Settings</GradientButton>
            </div>
          </div>
        </GlassCard>

        <div className="relative mt-4 flex-1 overflow-hidden rounded-4xl">
          {activeTab === 'music' ? (
            <div className="h-full p-2">
              <MusicPlaceholder />
            </div>
          ) : mediaConfigured ? (
            mediaRoute.kind === 'library' ? (
              <MediaLibraryPage
                key={`library-${mediaSessionKey}`}
                embedded
                onNavigateEntry={(id) => startTransition(() => setMediaRoute({ kind: 'entry', id }))}
              />
            ) : (
              <MediaEntryDetailPage
                key={`entry-${mediaSessionKey}-${mediaRoute.id}`}
                libraryEntryId={mediaRoute.id}
                onNavigateBack={() => startTransition(() => setMediaRoute({ kind: 'library' }))}
              />
            )
          ) : (
            <div className="h-full p-2">
              <SetupCard onOpenSettings={() => setSettingsOpen(true)} onSignIn={handleSignIn} isSigningIn={isSigningIn} />
            </div>
          )}
        </div>
      </div>

      {settingsOpen ? (
        <SettingsPanel
          apiBaseUrl={draftApiBaseUrl}
          loading={loading}
          isSigningIn={isSigningIn}
          isDisconnecting={isDisconnecting}
          isCheckingSession={isCheckingSession}
          hasUnsavedChanges={hasUnsavedChanges}
          sessionEmail={sessionEmail}
          defaultApiBaseUrl={DEFAULT_API_BASE_URL}
          onClose={() => setSettingsOpen(false)}
          onSubmit={handleSubmit}
          onSignIn={handleSignIn}
          onDisconnect={handleDisconnect}
          onApiBaseUrlChange={setDraftApiBaseUrl}
        />
      ) : null}

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
