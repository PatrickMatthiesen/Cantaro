import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react';
import { useRouterState } from '@tanstack/react-router';
import { Bell, Check, ChevronRight, Database, Download, ImagePlus, Palette, Plug, RefreshCw, Shield, SlidersHorizontal, Trash2, UserRound } from 'lucide-react';
import { authApi, BlurredEmail, type ProfilePreferences, type ThemePreference, type User } from '@cantaro/client-shared/auth';
import { MusicPlatformIcon, platformCatalog, platformManager, type PlatformAccountStatus, type PlatformId } from '@cantaro/client-shared/music';
import { MediaProviderIcon, mediaApi, mediaProviderCatalog, type MediaProviderAccountStatusDto } from '@cantaro/client-shared/media';
import { PageShell } from '../components/PageShell';
import { PageSideNavigation, type PageNavigationSection } from '../components/PageNavigation';
import { useAuth } from '../contexts/AuthContext';
import { AvatarCropDialog } from '../components/AvatarCropDialog';

type SaveState = 'idle' | 'saving' | 'saved';
type SectionId = 'profile' | 'connections' | 'notifications' | 'appearance' | 'sync' | 'security' | 'data';
interface AvatarCropSource { file: File; imageUrl: string }

const settingsNavigationSections: PageNavigationSection[] = [
  {
    title: 'Settings',
    items: [
      { label: 'Profile', to: '/settings', hash: 'profile', icon: <UserRound className="h-4 w-4" />, tone: 'violet' },
      { label: 'Connections', to: '/settings', hash: 'connections', icon: <Plug className="h-4 w-4" />, tone: 'indigo' },
      { label: 'Notifications', to: '/settings', hash: 'notifications', icon: <Bell className="h-4 w-4" />, tone: 'sky' },
      { label: 'Appearance', to: '/settings', hash: 'appearance', icon: <Palette className="h-4 w-4" />, tone: 'pink' },
      { label: 'Sync defaults', to: '/settings', hash: 'sync', icon: <SlidersHorizontal className="h-4 w-4" />, tone: 'emerald' },
      { label: 'Security', to: '/settings', hash: 'security', icon: <Shield className="h-4 w-4" />, tone: 'violet' },
      { label: 'Data & privacy', to: '/settings', hash: 'data', icon: <Database className="h-4 w-4" />, tone: 'slate' },
    ],
  },
];

function SettingsSidebar() {
  const location = useRouterState({ select: (state) => state.location });
  const activeHash = location.hash || 'profile';

  return (
    <PageSideNavigation
      activeHash={activeHash}
      activePathname={location.pathname}
      sections={settingsNavigationSections}
      subtitle="Settings"
      footer={(
        <div className="border-t border-border-subtle pt-5 text-sm leading-6 text-content-muted">
          <Shield className="mb-3 h-5 w-5 text-personal-accent-strong" />
        Service credentials stay encrypted on the Cantaro server and are never returned to this page.
        </div>
      )}
    />
  );
}

function SettingsSection({ id, title, description, children }: { id: SectionId; eyebrow: string; title: string; description: string; children: ReactNode }) {
  return (
    <section id={id} className="scroll-mt-28 border-b border-border-subtle py-8 sm:py-10">
      <header className="mb-6 max-w-2xl">
        <h2 className="text-2xl font-black tracking-tight text-content">{title}</h2>
        <p className="mt-2 text-sm leading-6 text-content-muted">{description}</p>
      </header>
      {children}
    </section>
  );
}

function SaveButton({ state, children = 'Save changes', type = 'submit', onClick }: { state: SaveState; children?: ReactNode; type?: 'submit' | 'button'; onClick?: () => void }) {
  return (
    <button type={type} onClick={onClick} disabled={state === 'saving'} className="inline-flex min-h-11 min-w-36 items-center justify-center gap-2 bg-personal-accent px-5 text-sm font-bold text-personal-accent-content transition-colors hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:opacity-60">
      {state === 'saving' ? <RefreshCw className="h-4 w-4 animate-spin" /> : state === 'saved' ? <Check className="h-4 w-4" /> : null}
      {state === 'saving' ? 'Saving…' : state === 'saved' ? 'Saved' : children}
    </button>
  );
}

function Toggle({
  checked,
  disabled = false,
  onChange,
  title,
  detail,
}: {
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
  title: string;
  detail: string;
}) {
  return (
    <label className={`flex items-center justify-between gap-5 border-b border-border-subtle py-4 last:border-0 ${disabled ? 'cursor-not-allowed opacity-65' : 'cursor-pointer'}`}>
      <span><strong className="block text-sm text-content">{title}</strong><span className="mt-1 block text-xs leading-5 text-content-muted">{detail}</span></span>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} className="peer sr-only" />
      <span className="relative h-7 w-12 shrink-0 rounded-full bg-surface-subtle transition peer-checked:bg-accent peer-focus-visible:ring-2 peer-focus-visible:ring-focus peer-focus-visible:ring-offset-2 peer-disabled:bg-surface-subtle after:absolute after:top-1 after:left-1 after:h-5 after:w-5 after:rounded-full after:bg-surface after:shadow after:transition-transform peer-checked:after:translate-x-5 peer-disabled:after:bg-surface-raised" aria-hidden />
    </label>
  );
}

function ProfileSection({ profile, onProfile }: { profile: User; onProfile: (profile: User) => void }) {
  const [displayName, setDisplayName] = useState(profile.displayName);
  const [state, setState] = useState<SaveState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [cropSource, setCropSource] = useState<AvatarCropSource | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const uploadAvatar = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;
    setError(null);
    setCropSource({ file, imageUrl: URL.createObjectURL(file) });
    event.target.value = '';
  };

  const closeCropper = () => {
    if (cropSource) URL.revokeObjectURL(cropSource.imageUrl);
    setCropSource(null);
  };

  const confirmAvatar = async (avatar: Blob) => {
    setIsUploading(true);
    setError(null);
    try {
      onProfile(await authApi.uploadAvatar(avatar));
      closeCropper();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Failed to upload avatar');
      throw reason;
    } finally {
      setIsUploading(false);
    }
  };

  const save = async (event: FormEvent) => {
    event.preventDefault(); setState('saving'); setError(null);
    try { onProfile(await authApi.updateProfile(displayName)); setState('saved'); window.setTimeout(() => setState('idle'), 1600); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Failed to update profile'); setState('idle'); }
  };

  return (
    <>
      <SettingsSection id="profile" eyebrow="Identity" title="A face for your library" description="This is how Cantaro addresses you across music, media, and shared activity.">
      <div className="grid gap-7 md:grid-cols-[auto_1fr] md:items-center">
        <div className="relative mx-auto md:mx-0">
          <div className="h-32 w-32 overflow-hidden bg-personal-accent text-personal-accent-content">
            {profile.avatarUrl ? <img src={profile.avatarUrl} alt={`${profile.displayName} avatar`} className="h-full w-full object-cover" /> : <span className="flex h-full items-center justify-center text-4xl font-black">{profile.displayName.charAt(0).toUpperCase()}</span>}
          </div>
          <button type="button" onClick={() => inputRef.current?.click()} className="absolute -right-3 -bottom-3 flex h-11 w-11 items-center justify-center border border-border-strong bg-surface text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus" aria-label="Choose a new avatar"><ImagePlus className="h-5 w-5" /></button>
          <input ref={inputRef} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={uploadAvatar} />
        </div>
        <form onSubmit={save} className="space-y-4">
          <label className="block"><span className="text-xs font-bold text-content-muted">Display name</span><input value={displayName} maxLength={100} required onChange={(event) => setDisplayName(event.target.value)} className="mt-2 min-h-11 w-full border border-border-strong bg-surface px-4 text-sm font-bold text-content outline-none transition-colors focus:border-focus focus-visible:outline-2 focus-visible:outline-focus" /></label>
          <label className="block">
            <span className="text-xs font-bold text-content-muted">Email</span>
            <div className="mt-2 min-h-11 w-full border border-border-subtle bg-surface-subtle px-4 py-3 text-sm text-content-muted">
              <BlurredEmail email={profile.email} blur={profile.preferences.blurEmailAddress} />
            </div>
          </label>
          {error ? <p className="text-sm font-semibold text-rose-600" role="alert">{error}</p> : null}
          <div className="flex flex-wrap gap-3"><SaveButton state={state} />{profile.avatarUrl ? <button type="button" onClick={async () => onProfile(await authApi.deleteAvatar())} className="min-h-11 px-4 text-sm font-bold text-content-muted hover:bg-danger-surface hover:text-danger-content">Remove photo</button> : null}</div>
        </form>
      </div>
      </SettingsSection>
      {cropSource ? <AvatarCropDialog file={cropSource.file} imageUrl={cropSource.imageUrl} isSaving={isUploading} onCancel={closeCropper} onConfirm={confirmAvatar} /> : null}
    </>
  );
}

function ConnectionCard({ name, detail, icon, implemented, connected, busy, onAction }: { name: string; detail: string; icon: ReactNode; implemented: boolean; connected: boolean; busy: boolean; onAction: () => void }) {
  return (
    <article className="flex items-center gap-4 border-t border-border-subtle py-4">
      {icon}
      <span className="min-w-0 flex-1"><strong className="block text-sm text-content">{name}</strong><span className="block truncate text-xs text-content-muted">{implemented ? detail : 'Coming later'}</span></span>
      {implemented ? <button type="button" disabled={busy} onClick={onAction} className={`min-h-9 px-4 text-xs font-bold transition-colors ${connected ? 'bg-success-surface text-success-content hover:bg-danger-surface hover:text-danger-content' : 'bg-personal-accent text-personal-accent-content hover:bg-personal-accent-hover'}`}>{busy ? 'Working…' : connected ? 'Connected' : 'Connect'}</button> : <span className="bg-surface-subtle px-3 py-1.5 text-[10px] font-bold text-content-muted">Coming soon</span>}
    </article>
  );
}

function MusicConnectionCard({ item, status, busyKey, onAction }: { item: (typeof platformCatalog)[number]; status?: PlatformAccountStatus; busyKey: string | null; onAction: () => void }) {
  const connected = Boolean(status?.isConnected);
  const detail = status?.displayName || (connected ? 'Account connected' : 'Music platform');
  return <ConnectionCard name={item.name} detail={detail} icon={<MusicPlatformIcon platformId={item.id} className="h-10 w-10 shrink-0" />} implemented={item.implemented} connected={connected} busy={busyKey === `music-${item.id}`} onAction={onAction} />;
}

function MediaConnectionCard({ item, status, busyKey, onAction }: { item: (typeof mediaProviderCatalog)[number]; status?: MediaProviderAccountStatusDto; busyKey: string | null; onAction: () => void }) {
  const connected = Boolean(status?.isConnected);
  const detail = status?.displayName || (connected ? 'Account connected' : 'Media provider');
  return <ConnectionCard name={item.name} detail={detail} icon={<MediaProviderIcon providerId={item.iconId} className="h-10 w-10 shrink-0" aria-hidden />} implemented={item.implemented} connected={connected} busy={busyKey === `media-${item.id}`} onAction={onAction} />;
}

function ConnectionsSection() {
  const [music, setMusic] = useState<Partial<Record<PlatformId, PlatformAccountStatus>>>({});
  const [media, setMedia] = useState<Record<string, MediaProviderAccountStatusDto>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const load = async () => {
    const musicEntries = await Promise.all(platformCatalog.filter(item => item.implemented).map(async item => [item.id, await platformManager.status(item.id)] as const));
    setMusic(Object.fromEntries(musicEntries));
    const mediaEntries = await Promise.all(mediaProviderCatalog.filter(item => item.implemented).map(async item => [item.id, await mediaApi.getProviderStatus(item.id)] as const));
    setMedia(Object.fromEntries(mediaEntries));
  };
  useEffect(() => { void load(); }, []);

  const handleMusicAction = async (item: (typeof platformCatalog)[number]) => {
    if (!music[item.id]?.isConnected) {
      await platformManager.connect(item.id, { route: '/settings#connections', trigger: 'settings' });
      return;
    }
    if (!window.confirm(`Disconnect ${item.name}?`)) return;
    setBusy(`music-${item.id}`);
    try {
      await platformManager.disconnect(item.id);
      await load();
    } finally {
      setBusy(null);
    }
  };

  const handleMediaAction = async (item: (typeof mediaProviderCatalog)[number]) => {
    if (!media[item.id]?.isConnected) {
      await mediaApi.connectProvider(item.id, { route: '/settings', trigger: 'settings' });
      return;
    }
    if (!window.confirm(`Disconnect ${item.name}?`)) return;
    setBusy(`media-${item.id}`);
    try {
      await mediaApi.disconnectProvider(item.id);
      await load();
    } finally {
      setBusy(null);
    }
  };

  return (
    <SettingsSection id="connections" eyebrow="Your constellation" title="Connected services" description="Cantaro keeps provider credentials server-side while this page gives you one calm place to manage them.">
      <div className="grid gap-x-8 lg:grid-cols-2">
        {platformCatalog.map(item => <MusicConnectionCard key={item.id} item={item} status={music[item.id]} busyKey={busy} onAction={() => void handleMusicAction(item)} />)}
        {mediaProviderCatalog.map(item => <MediaConnectionCard key={item.id} item={item} status={media[item.id]} busyKey={busy} onAction={() => void handleMediaAction(item)} />)}
      </div>
    </SettingsSection>
  );
}

function useThemeAutosave(
  preferences: ProfilePreferences,
  setPreferences: (preferences: ProfilePreferences) => void,
  onProfile: (profile: User) => void,
) {
  const [themeSaveState, setThemeSaveState] = useState<SaveState>('idle');
  const themeSaveTimerRef = useRef<number | null>(null);
  const themeSaveResetTimerRef = useRef<number | null>(null);
  const pendingThemePreferencesRef = useRef<ProfilePreferences | null>(null);
  const themeSaveVersionRef = useRef(0);

  const clearThemeSaveTimer = useCallback(() => {
    if (themeSaveTimerRef.current === null) return;
    window.clearTimeout(themeSaveTimerRef.current);
    themeSaveTimerRef.current = null;
  }, []);

  const clearThemeSaveResetTimer = useCallback(() => {
    if (themeSaveResetTimerRef.current === null) return;
    window.clearTimeout(themeSaveResetTimerRef.current);
    themeSaveResetTimerRef.current = null;
  }, []);

  const commitThemePreferences = useCallback(async (nextPreferences: ProfilePreferences, version: number) => {
    setThemeSaveState('saving');
    clearThemeSaveResetTimer();

    try {
      const updatedProfile = await authApi.updatePreferences(nextPreferences);
      if (version !== themeSaveVersionRef.current) return;

      pendingThemePreferencesRef.current = null;
      onProfile(updatedProfile);
      setThemeSaveState('saved');
      themeSaveResetTimerRef.current = window.setTimeout(() => setThemeSaveState('idle'), 1400);
    } catch {
      if (version === themeSaveVersionRef.current) setThemeSaveState('idle');
    }
  }, [clearThemeSaveResetTimer, onProfile]);

  const flushThemeSave = useCallback(() => {
    const pendingPreferences = pendingThemePreferencesRef.current;
    if (!pendingPreferences) return;

    clearThemeSaveTimer();
    pendingThemePreferencesRef.current = null;
    const version = ++themeSaveVersionRef.current;
    void commitThemePreferences(pendingPreferences, version);
  }, [clearThemeSaveTimer, commitThemePreferences]);

  useEffect(() => {
    const flushOnPageHide = () => flushThemeSave();
    window.addEventListener('pagehide', flushOnPageHide);

    return () => {
      window.removeEventListener('pagehide', flushOnPageHide);
      flushThemeSave();
      clearThemeSaveResetTimer();
    };
  }, [clearThemeSaveResetTimer, flushThemeSave]);

  useEffect(() => {
    const theme = preferences.theme;
    const resolved = theme === 'system' ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : theme;
    document.documentElement.dataset.theme = resolved;
    document.documentElement.style.colorScheme = resolved;
  }, [preferences.theme]);

  const previewTheme = useCallback((theme: ThemePreference) => {
    if (theme === preferences.theme) return;

    const nextPreferences = { ...preferences, theme };
    setPreferences(nextPreferences);
    pendingThemePreferencesRef.current = nextPreferences;
    const version = ++themeSaveVersionRef.current;
    clearThemeSaveTimer();
    clearThemeSaveResetTimer();
    setThemeSaveState('saving');
    themeSaveTimerRef.current = window.setTimeout(() => {
      pendingThemePreferencesRef.current = null;
      themeSaveTimerRef.current = null;
      void commitThemePreferences(nextPreferences, version);
    }, 700);
  }, [clearThemeSaveResetTimer, clearThemeSaveTimer, commitThemePreferences, preferences, setPreferences]);

  return { previewTheme, themeSaveState };
}

function PreferencesSections({ profile, onProfile }: { profile: User; onProfile: (profile: User) => void }) {
  const [preferences, setPreferences] = useState(profile.preferences);
  const [state, setState] = useState<SaveState>('idle');
  const { previewTheme, themeSaveState } = useThemeAutosave(preferences, setPreferences, onProfile);
  useEffect(() => setPreferences(profile.preferences), [profile.preferences]);
  const change = <K extends keyof ProfilePreferences>(key: K, value: ProfilePreferences[K]) => setPreferences(current => ({ ...current, [key]: value }));
  const save = async () => { setState('saving'); try { onProfile(await authApi.updatePreferences(preferences)); setState('saved'); window.setTimeout(() => setState('idle'), 1600); } catch { setState('idle'); } };
  const saveFooter = <div className="mt-5 flex justify-end"><SaveButton state={state} type="button" onClick={save} /></div>;

  return <>
    <SettingsSection id="notifications" eyebrow="Signal, not noise" title="Notification defaults" description="Choose which events should become in-app notices as Cantaro's notification center comes online.">
      <div className="overflow-hidden border-y border-border-subtle px-1"><Toggle checked={preferences.notifyOnSyncSuccess} onChange={value => change('notifyOnSyncSuccess', value)} title="Successful syncs" detail="Let me know when a playlist finishes syncing." /><Toggle checked={preferences.notifyOnSyncFailure} onChange={value => change('notifyOnSyncFailure', value)} title="Sync failures" detail="Surface provider errors and syncs that need attention." /><Toggle checked={preferences.notifyOnMediaReview} onChange={value => change('notifyOnMediaReview', value)} title="Media review queue" detail="Flag new observations that need a match decision." /></div>{saveFooter}
    </SettingsSection>
    <SettingsSection id="appearance" eyebrow="Atmosphere" title="Choose your listening room" description="Follow your device or set a consistent Cantaro appearance everywhere you sign in.">
      <div className="grid gap-3 sm:grid-cols-3">{(['system','light','dark'] as ThemePreference[]).map(theme => <button key={theme} type="button" onClick={() => previewTheme(theme)} className={`border p-4 text-left transition-colors focus-visible:outline-2 focus-visible:outline-focus ${preferences.theme === theme ? 'border-personal-accent bg-surface-hover' : 'border-border-subtle hover:border-border-strong hover:bg-surface-hover'}`}><span className={`mb-4 block h-16 border border-border-subtle ${theme === 'dark' ? 'bg-slate-950' : theme === 'light' ? 'bg-white' : 'bg-linear-to-r from-white to-slate-950'}`} /><strong className="text-content capitalize">{theme}</strong></button>)}</div>
      <p className="mt-4 text-right text-xs font-bold text-content-muted" role="status">
        {themeSaveState === 'saving' ? 'Saving appearance…' : themeSaveState === 'saved' ? 'Appearance saved' : 'Appearance saves automatically'}
      </p>
    </SettingsSection>
    <SettingsSection id="sync" eyebrow="Set it once" title="Sync and media defaults" description="New playlist sync sessions begin with these rules. Your media preference controls which released episodes Cantaro highlights.">
      <div className="overflow-hidden border-y border-border-subtle px-1"><Toggle checked={preferences.keepPlaylistOrder} onChange={value => change('keepPlaylistOrder', value)} title="Preserve song order" detail="Keep the source playlist sequence intact." /><Toggle checked={preferences.keepPlaylistMetadata} onChange={value => change('keepPlaylistMetadata', value)} title="Preserve playlist metadata" detail="Carry title, description, and artwork where providers allow it." /><Toggle checked={preferences.hideUnavailableTracks} onChange={value => change('hideUnavailableTracks', value)} title="Hide unavailable tracks" detail="Keep missing or region-blocked tracks out of sync previews." /><Toggle checked={preferences.scheduledSync} onChange={value => change('scheduledSync', value)} title="Scheduled sync by default" detail="Prepare imported playlists for future automatic sync runs." /></div>
      <label className="mt-5 block max-w-md">
        <span className="text-xs font-black tracking-wider text-content-muted uppercase">Prefer watching</span>
        <select
          value={preferences.preferredMediaReleaseTrack}
          onChange={event => change('preferredMediaReleaseTrack', event.target.value)}
          className="mt-2 min-h-11 w-full border border-border-strong bg-surface px-4 text-sm font-bold text-content outline-none focus:border-focus focus-visible:outline-2 focus-visible:outline-focus"
        >
          <option value="sub:en">English subtitles</option>
          <option value="dub:en">English dub</option>
        </select>
        <span className="mt-2 block text-xs leading-5 text-content-muted">
          The red episode-progress segment follows this choice, with English subtitles used when no English dub is known. Release schedules are provided by{' '}
          <a href="https://animeschedule.net" target="_blank" rel="noopener noreferrer" className="underline hover:text-content">AnimeSchedule.net</a>.
        </span>
      </label>
      {saveFooter}
    </SettingsSection>
  </>;
}

function SecuritySection() {
  const [currentPassword, setCurrentPassword] = useState(''); const [newPassword, setNewPassword] = useState(''); const [message, setMessage] = useState<string | null>(null); const [state, setState] = useState<SaveState>('idle');
  return <SettingsSection id="security" eyebrow="Account security" title="Change your password" description="Updating your password refreshes your current Cantaro session without exposing credentials to connected services."><form onSubmit={async event => { event.preventDefault(); setState('saving'); setMessage(null); try { await authApi.changePassword(currentPassword, newPassword); setCurrentPassword(''); setNewPassword(''); setMessage('Password changed.'); setState('saved'); } catch (reason) { setMessage(reason instanceof Error ? reason.message : 'Password change failed'); setState('idle'); } }} className="grid gap-4 md:grid-cols-2"><input type="password" autoComplete="current-password" required placeholder="Current password" value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} className="min-h-11 border border-border-strong bg-surface px-4 text-sm text-content outline-none focus:border-focus focus-visible:outline-2 focus-visible:outline-focus" /><input type="password" autoComplete="new-password" minLength={6} required placeholder="New password" value={newPassword} onChange={event => setNewPassword(event.target.value)} className="min-h-11 border border-border-strong bg-surface px-4 text-sm text-content outline-none focus:border-focus focus-visible:outline-2 focus-visible:outline-focus" /><div className="flex items-center gap-4 md:col-span-2"><SaveButton state={state}>Change password</SaveButton>{message ? <p className="text-sm text-content-muted" role="status">{message}</p> : null}</div></form></SettingsSection>;
}

function withBlurEmailAddress(profile: User, blurEmailAddress: boolean) {
  return {
    ...profile,
    preferences: {
      ...profile.preferences,
      blurEmailAddress,
    },
  };
}

async function recoverBlurEmailAddressPreference(profile: User, fallbackBlurEmailAddress: boolean) {
  try {
    const currentProfile = await authApi.getCurrentUser();
    const blurEmailAddress = typeof currentProfile.preferences.blurEmailAddress === 'boolean'
      ? currentProfile.preferences.blurEmailAddress
      : fallbackBlurEmailAddress;

    return {
      blurEmailAddress,
      profile: withBlurEmailAddress(currentProfile, blurEmailAddress),
    };
  } catch {
    return {
      blurEmailAddress: fallbackBlurEmailAddress,
      profile: withBlurEmailAddress(profile, fallbackBlurEmailAddress),
    };
  }
}

function useBlurEmailAddressPreference(profile: User, onProfile: (profile: User) => void) {
  const [privacyState, setPrivacyState] = useState<SaveState>('idle');
  const [privacyError, setPrivacyError] = useState<string | null>(null);
  const [blurEmailAddress, setBlurEmailAddressValue] = useState(profile.preferences.blurEmailAddress);
  const privacyStateRef = useRef(privacyState);
  useEffect(() => {
    privacyStateRef.current = privacyState;
  }, [privacyState]);
  useEffect(() => {
    if (privacyStateRef.current === 'saving') return;
    setBlurEmailAddressValue(profile.preferences.blurEmailAddress);
  }, [profile.preferences.blurEmailAddress]);

  const setBlurEmailAddress = async (nextBlurEmailAddress: boolean) => {
    if (privacyState === 'saving') {
      return;
    }

    const previousBlurEmailAddress = !nextBlurEmailAddress;
    setBlurEmailAddressValue(nextBlurEmailAddress);
    setPrivacyState('saving');
    setPrivacyError(null);
    try {
      const updatedProfile = await authApi.updatePreferences({ ...profile.preferences, blurEmailAddress: nextBlurEmailAddress });
      if (updatedProfile.preferences.blurEmailAddress !== nextBlurEmailAddress) {
        throw new Error('We could not save that privacy setting. Please try again in a moment.');
      }

      onProfile(updatedProfile);
      setBlurEmailAddressValue(updatedProfile.preferences.blurEmailAddress);
      setPrivacyState('saved');
      window.setTimeout(() => setPrivacyState('idle'), 1600);
    } catch (reason) {
      const recovered = await recoverBlurEmailAddressPreference(profile, previousBlurEmailAddress);
      onProfile(recovered.profile);
      setBlurEmailAddressValue(recovered.blurEmailAddress);
      setPrivacyError(reason instanceof Error ? reason.message : 'Failed to save privacy setting.');
      setPrivacyState('idle');
    }
  };

  return { blurEmailAddress, privacyError, privacyState, setBlurEmailAddress };
}

function DataSection({ profile, onProfile }: { profile: User; onProfile: (profile: User) => void }) {
  const [password, setPassword] = useState(''); const [error, setError] = useState<string | null>(null);
  const { blurEmailAddress, privacyError, privacyState, setBlurEmailAddress } = useBlurEmailAddressPreference(profile, onProfile);

  return <SettingsSection id="data" eyebrow="You own the archive" title="Data and privacy" description="Take a portable copy of your Cantaro data, reduce stream-visible account details, or permanently remove your account."><div className="mb-5 overflow-hidden border-y border-border-subtle px-1"><Toggle checked={blurEmailAddress} disabled={privacyState === 'saving'} onChange={value => void setBlurEmailAddress(value)} title="Blur account email on screen" detail="Obscure your email anywhere Cantaro shows it, useful while streaming or sharing your screen." />{privacyState !== 'idle' ? <p className="pb-4 text-right text-xs font-bold text-content-muted" role="status">{privacyState === 'saving' ? 'Saving privacy setting…' : 'Privacy setting saved'}</p> : null}{privacyError ? <p className="pb-4 text-right text-xs font-bold text-danger-content" role="alert">{privacyError}</p> : null}</div><div className="grid gap-5 md:grid-cols-2"><a href="/api/profile/export" className="group flex items-center gap-4 border-y border-border-subtle py-5 transition-colors hover:bg-surface-hover"><span className="flex h-11 w-11 items-center justify-center bg-info-surface text-info-content"><Download className="h-5 w-5" /></span><span className="flex-1"><strong className="block text-sm text-content">Export my data</strong><span className="text-xs text-content-muted">Download JSON and your avatar as a ZIP.</span></span><ChevronRight className="h-4 w-4 text-content-subtle" /></a><div className="border border-danger-border bg-danger-surface p-5"><strong className="text-sm text-danger-content">Delete account</strong><p className="mt-1 text-xs leading-5 text-danger-content">This permanently removes your profile, libraries, connections, and settings.</p><input type="password" value={password} onChange={event => setPassword(event.target.value)} placeholder="Current password" className="mt-3 min-h-10 w-full border border-danger-border bg-surface px-3 text-sm text-content outline-none" /><button type="button" disabled={!password} onClick={async () => { if (!window.confirm('Permanently delete your Cantaro account? This cannot be undone.')) return; setError(null); try { await authApi.deleteAccount(password); window.location.assign('/'); } catch (reason) { setError(reason instanceof Error ? reason.message : 'Account deletion failed'); } }} className="mt-3 inline-flex min-h-10 items-center gap-2 bg-danger-action px-4 text-xs font-bold text-danger-action-content transition-colors hover:bg-danger-action-hover disabled:opacity-50"><Trash2 className="h-4 w-4" />Delete permanently</button>{error ? <p className="mt-2 text-xs font-bold text-danger-content">{error}</p> : null}</div></div></SettingsSection>;
}

export function SettingsPage() {
  const { user, setProfile } = useAuth();
  const profile = useMemo(() => user, [user]);
  if (!profile) return null;
  return <PageShell sidebar={<SettingsSidebar />}><div className="mx-auto max-w-5xl"><header className="border-b border-border-subtle pb-7"><h1 className="text-3xl font-black tracking-[-.03em] text-content sm:text-4xl">Settings</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-content-muted">Your profile, connected services, sync defaults, security, and archive ownership.</p></header><ProfileSection profile={profile} onProfile={setProfile} /><ConnectionsSection /><PreferencesSections profile={profile} onProfile={setProfile} /><SecuritySection /><DataSection profile={profile} onProfile={setProfile} /></div></PageShell>;
}
