import { useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { Bell, Check, ChevronRight, Database, Download, ImagePlus, LockKeyhole, Palette, Plug, RefreshCw, Shield, SlidersHorizontal, Trash2, UserRound } from 'lucide-react';
import { authApi, type ProfilePreferences, type ThemePreference, type User } from '@cantaro/client-shared/auth';
import { MusicPlatformIcon, platformCatalog, platformManager, type PlatformAccountStatus, type PlatformId } from '@cantaro/client-shared/music';
import { MediaProviderIcon, mediaApi, mediaProviderCatalog, type MediaProviderAccountStatusDto } from '@cantaro/client-shared/media';
import { PageShell } from '../components/PageShell';
import { useAuth } from '../contexts/AuthContext';
import { AvatarCropDialog } from '../components/AvatarCropDialog';

type SaveState = 'idle' | 'saving' | 'saved';
type SectionId = 'profile' | 'connections' | 'notifications' | 'appearance' | 'sync' | 'security' | 'data';
interface AvatarCropSource { file: File; imageUrl: string }

const sections: Array<{ id: SectionId; label: string; icon: ReactNode }> = [
  { id: 'profile', label: 'Profile', icon: <UserRound className="h-4 w-4" /> },
  { id: 'connections', label: 'Connections', icon: <Plug className="h-4 w-4" /> },
  { id: 'notifications', label: 'Notifications', icon: <Bell className="h-4 w-4" /> },
  { id: 'appearance', label: 'Appearance', icon: <Palette className="h-4 w-4" /> },
  { id: 'sync', label: 'Sync defaults', icon: <SlidersHorizontal className="h-4 w-4" /> },
  { id: 'security', label: 'Security', icon: <Shield className="h-4 w-4" /> },
  { id: 'data', label: 'Data & privacy', icon: <Database className="h-4 w-4" /> },
];

function SettingsSidebar() {
  return (
    <aside className="settings-sidebar sticky top-0 h-screen overflow-y-auto border-r px-5 py-6">
      <Link to="/music/songs" className="flex items-center gap-3">
        <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-violet-600 text-lg font-black text-white shadow-lg shadow-violet-500/25">C</span>
        <span><strong className="block tracking-widest">CANTARO</strong><small className="font-bold tracking-[.28em] text-slate-500 uppercase">Settings</small></span>
      </Link>
      <nav className="mt-10 space-y-1" aria-label="Settings sections">
        {sections.map((section) => (
          <a key={section.id} href={`#${section.id}`} className="settings-nav-link flex items-center gap-3 rounded-2xl px-3 py-3 text-sm font-bold text-slate-600 transition hover:bg-white/70 hover:text-violet-700">
            <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-white/70 text-violet-600 shadow-sm">{section.icon}</span>
            {section.label}
          </a>
        ))}
      </nav>
      <div className="mt-10 rounded-3xl border border-violet-100 bg-white/65 p-5 text-sm leading-6 text-slate-600 shadow-sm">
        <LockKeyhole className="mb-3 h-5 w-5 text-violet-600" />
        Service credentials stay encrypted on the Cantaro server and are never returned to this page.
      </div>
    </aside>
  );
}

function SettingsSection({ id, eyebrow, title, description, children }: { id: SectionId; eyebrow: string; title: string; description: string; children: ReactNode }) {
  return (
    <section id={id} className="settings-card scroll-mt-28 overflow-hidden rounded-[2rem] border p-6 shadow-[0_24px_80px_rgba(69,55,120,.08)] sm:p-8">
      <header className="mb-6 max-w-2xl">
        <p className="text-[11px] font-black tracking-[.28em] text-violet-600 uppercase">{eyebrow}</p>
        <h2 className="mt-2 text-2xl font-black tracking-tight text-slate-950">{title}</h2>
        <p className="mt-2 text-sm leading-6 text-slate-500">{description}</p>
      </header>
      {children}
    </section>
  );
}

function SaveButton({ state, children = 'Save changes', type = 'submit', onClick }: { state: SaveState; children?: ReactNode; type?: 'submit' | 'button'; onClick?: () => void }) {
  return (
    <button type={type} onClick={onClick} disabled={state === 'saving'} className="inline-flex min-w-36 items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:-translate-y-0.5 hover:bg-violet-700 disabled:opacity-60">
      {state === 'saving' ? <RefreshCw className="h-4 w-4 animate-spin" /> : state === 'saved' ? <Check className="h-4 w-4" /> : null}
      {state === 'saving' ? 'Saving…' : state === 'saved' ? 'Saved' : children}
    </button>
  );
}

function Toggle({ checked, onChange, title, detail }: { checked: boolean; onChange: (checked: boolean) => void; title: string; detail: string }) {
  return (
    <label className="settings-toggle-row flex cursor-pointer items-center justify-between gap-5 border-b py-4 last:border-0">
      <span><strong className="block text-sm text-slate-900">{title}</strong><span className="mt-1 block text-xs leading-5 text-slate-500">{detail}</span></span>
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} className="peer sr-only" />
      <span className="relative h-7 w-12 shrink-0 rounded-full bg-slate-200 transition peer-checked:bg-violet-600 peer-focus-visible:ring-2 peer-focus-visible:ring-violet-400 peer-focus-visible:ring-offset-2 after:absolute after:top-1 after:left-1 after:h-5 after:w-5 after:rounded-full after:bg-white after:shadow after:transition-transform peer-checked:after:translate-x-5" aria-hidden />
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
          <div className="h-32 w-32 overflow-hidden rounded-[2rem] bg-linear-to-br from-violet-500 via-fuchsia-500 to-slate-950 text-white shadow-2xl shadow-violet-500/20">
            {profile.avatarUrl ? <img src={profile.avatarUrl} alt={`${profile.displayName} avatar`} className="h-full w-full object-cover" /> : <span className="flex h-full items-center justify-center text-4xl font-black">{profile.displayName.charAt(0).toUpperCase()}</span>}
          </div>
          <button type="button" onClick={() => inputRef.current?.click()} className="absolute -right-3 -bottom-3 flex h-11 w-11 items-center justify-center rounded-2xl bg-slate-950 text-white shadow-lg transition hover:scale-105" aria-label="Choose a new avatar"><ImagePlus className="h-5 w-5" /></button>
          <input ref={inputRef} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={uploadAvatar} />
        </div>
        <form onSubmit={save} className="space-y-4">
          <label className="block"><span className="text-xs font-black tracking-wider text-slate-500 uppercase">Display name</span><input value={displayName} maxLength={100} required onChange={(event) => setDisplayName(event.target.value)} className="settings-input mt-2 w-full rounded-2xl border px-4 py-3 text-sm font-bold transition outline-none focus:border-violet-400 focus:ring-4 focus:ring-violet-100" /></label>
          <label className="block"><span className="text-xs font-black tracking-wider text-slate-500 uppercase">Email</span><input value={profile.email} readOnly className="settings-input mt-2 w-full rounded-2xl border px-4 py-3 text-sm text-slate-500 opacity-75" /></label>
          {error ? <p className="text-sm font-semibold text-rose-600" role="alert">{error}</p> : null}
          <div className="flex flex-wrap gap-3"><SaveButton state={state} />{profile.avatarUrl ? <button type="button" onClick={async () => onProfile(await authApi.deleteAvatar())} className="rounded-2xl px-4 py-3 text-sm font-bold text-slate-600 hover:bg-rose-50 hover:text-rose-700">Remove photo</button> : null}</div>
        </form>
      </div>
      </SettingsSection>
      {cropSource ? <AvatarCropDialog file={cropSource.file} imageUrl={cropSource.imageUrl} isSaving={isUploading} onCancel={closeCropper} onConfirm={confirmAvatar} /> : null}
    </>
  );
}

function ConnectionCard({ name, detail, icon, implemented, connected, busy, onAction }: { name: string; detail: string; icon: ReactNode; implemented: boolean; connected: boolean; busy: boolean; onAction: () => void }) {
  return (
    <article className="settings-surface-row flex items-center gap-4 rounded-3xl border p-4">
      <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-white shadow-sm">{icon}</span>
      <span className="min-w-0 flex-1"><strong className="block text-sm text-slate-950">{name}</strong><span className="block truncate text-xs text-slate-500">{implemented ? detail : 'Coming later'}</span></span>
      {implemented ? <button type="button" disabled={busy} onClick={onAction} className={`rounded-xl px-4 py-2 text-xs font-black transition ${connected ? 'bg-emerald-50 text-emerald-700 hover:bg-rose-50 hover:text-rose-700' : 'bg-violet-600 text-white hover:bg-violet-700'}`}>{busy ? 'Working…' : connected ? 'Connected' : 'Connect'}</button> : <span className="rounded-full bg-slate-100 px-3 py-1.5 text-[10px] font-black tracking-wider text-slate-500 uppercase">Coming Soon</span>}
    </article>
  );
}

function MusicConnectionCard({ item, status, busyKey, onAction }: { item: (typeof platformCatalog)[number]; status?: PlatformAccountStatus; busyKey: string | null; onAction: () => void }) {
  const connected = Boolean(status?.isConnected);
  const detail = status?.displayName || (connected ? 'Account connected' : 'Music platform');
  return <ConnectionCard name={item.name} detail={detail} icon={<MusicPlatformIcon platformId={item.id} className="h-9 w-9" />} implemented={item.implemented} connected={connected} busy={busyKey === `music-${item.id}`} onAction={onAction} />;
}

function MediaConnectionCard({ item, status, busyKey, onAction }: { item: (typeof mediaProviderCatalog)[number]; status?: MediaProviderAccountStatusDto; busyKey: string | null; onAction: () => void }) {
  const connected = Boolean(status?.isConnected);
  const detail = status?.displayName || (connected ? 'Account connected' : 'Media provider');
  return <ConnectionCard name={item.name} detail={detail} icon={<MediaProviderIcon providerId={item.iconId} className="h-9 w-9" aria-hidden />} implemented={item.implemented} connected={connected} busy={busyKey === `media-${item.id}`} onAction={onAction} />;
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
      <div className="grid gap-3 lg:grid-cols-2">
        {platformCatalog.map(item => <MusicConnectionCard key={item.id} item={item} status={music[item.id]} busyKey={busy} onAction={() => void handleMusicAction(item)} />)}
        {mediaProviderCatalog.map(item => <MediaConnectionCard key={item.id} item={item} status={media[item.id]} busyKey={busy} onAction={() => void handleMediaAction(item)} />)}
      </div>
    </SettingsSection>
  );
}

function PreferencesSections({ profile, onProfile }: { profile: User; onProfile: (profile: User) => void }) {
  const [preferences, setPreferences] = useState(profile.preferences);
  const [state, setState] = useState<SaveState>('idle');
  useEffect(() => setPreferences(profile.preferences), [profile.preferences]);
  useEffect(() => {
    const theme = preferences.theme;
    const resolved = theme === 'system' ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : theme;
    document.documentElement.dataset.theme = resolved;
    document.documentElement.style.colorScheme = resolved;
  }, [preferences.theme]);
  const change = <K extends keyof ProfilePreferences>(key: K, value: ProfilePreferences[K]) => setPreferences(current => ({ ...current, [key]: value }));
  const previewTheme = (theme: ThemePreference) => {
    change('theme', theme);
  };
  const save = async () => { setState('saving'); try { onProfile(await authApi.updatePreferences(preferences)); setState('saved'); window.setTimeout(() => setState('idle'), 1600); } catch { setState('idle'); } };
  const saveFooter = <div className="mt-5 flex justify-end"><SaveButton state={state} type="button" onClick={save} /></div>;

  return <>
    <SettingsSection id="notifications" eyebrow="Signal, not noise" title="Notification defaults" description="Choose which events should become in-app notices as Cantaro's notification center comes online.">
      <div className="settings-toggle-group overflow-hidden rounded-3xl border px-5"><Toggle checked={preferences.notifyOnSyncSuccess} onChange={value => change('notifyOnSyncSuccess', value)} title="Successful syncs" detail="Let me know when a playlist finishes syncing." /><Toggle checked={preferences.notifyOnSyncFailure} onChange={value => change('notifyOnSyncFailure', value)} title="Sync failures" detail="Surface provider errors and syncs that need attention." /><Toggle checked={preferences.notifyOnMediaReview} onChange={value => change('notifyOnMediaReview', value)} title="Media review queue" detail="Flag new observations that need a match decision." /></div>{saveFooter}
    </SettingsSection>
    <SettingsSection id="appearance" eyebrow="Atmosphere" title="Choose your listening room" description="Follow your device or set a consistent Cantaro appearance everywhere you sign in.">
      <div className="grid gap-3 sm:grid-cols-3">{(['system','light','dark'] as ThemePreference[]).map(theme => <button key={theme} type="button" onClick={() => previewTheme(theme)} className={`rounded-3xl border p-5 text-left transition ${preferences.theme === theme ? 'border-violet-500 bg-violet-50 ring-2 ring-violet-200' : 'hover:-translate-y-0.5 hover:border-violet-200'}`}><span className={`mb-4 block h-20 rounded-2xl ${theme === 'dark' ? 'bg-slate-950' : theme === 'light' ? 'bg-white shadow-inner' : 'bg-linear-to-r from-white to-slate-950'}`} /><strong className="text-slate-950 capitalize">{theme}</strong></button>)}</div>{saveFooter}
    </SettingsSection>
    <SettingsSection id="sync" eyebrow="Set it once" title="Playlist sync defaults" description="New playlist sync sessions begin with these rules. You can still change them for an individual run.">
      <div className="settings-toggle-group overflow-hidden rounded-3xl border px-5"><Toggle checked={preferences.keepPlaylistOrder} onChange={value => change('keepPlaylistOrder', value)} title="Preserve song order" detail="Keep the source playlist sequence intact." /><Toggle checked={preferences.keepPlaylistMetadata} onChange={value => change('keepPlaylistMetadata', value)} title="Preserve playlist metadata" detail="Carry title, description, and artwork where providers allow it." /><Toggle checked={preferences.hideUnavailableTracks} onChange={value => change('hideUnavailableTracks', value)} title="Hide unavailable tracks" detail="Keep missing or region-blocked tracks out of sync previews." /><Toggle checked={preferences.scheduledSync} onChange={value => change('scheduledSync', value)} title="Scheduled sync by default" detail="Prepare imported playlists for future automatic sync runs." /></div>{saveFooter}
    </SettingsSection>
  </>;
}

function SecuritySection() {
  const [currentPassword, setCurrentPassword] = useState(''); const [newPassword, setNewPassword] = useState(''); const [message, setMessage] = useState<string | null>(null); const [state, setState] = useState<SaveState>('idle');
  return <SettingsSection id="security" eyebrow="Account security" title="Change your password" description="Updating your password refreshes your current Cantaro session without exposing credentials to connected services."><form onSubmit={async event => { event.preventDefault(); setState('saving'); setMessage(null); try { await authApi.changePassword(currentPassword, newPassword); setCurrentPassword(''); setNewPassword(''); setMessage('Password changed.'); setState('saved'); } catch (reason) { setMessage(reason instanceof Error ? reason.message : 'Password change failed'); setState('idle'); } }} className="grid gap-4 md:grid-cols-2"><input type="password" autoComplete="current-password" required placeholder="Current password" value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} className="settings-input rounded-2xl border px-4 py-3 text-sm outline-none focus:border-violet-400" /><input type="password" autoComplete="new-password" minLength={6} required placeholder="New password" value={newPassword} onChange={event => setNewPassword(event.target.value)} className="settings-input rounded-2xl border px-4 py-3 text-sm outline-none focus:border-violet-400" /><div className="flex items-center gap-4 md:col-span-2"><SaveButton state={state}>Change password</SaveButton>{message ? <p className="text-sm text-slate-600" role="status">{message}</p> : null}</div></form></SettingsSection>;
}

function DataSection() {
  const [password, setPassword] = useState(''); const [error, setError] = useState<string | null>(null);
  return <SettingsSection id="data" eyebrow="You own the archive" title="Data and privacy" description="Take a portable copy of your Cantaro data, or permanently remove your account and server-held provider credentials."><div className="grid gap-4 md:grid-cols-2"><a href="/api/profile/export" className="settings-surface-row group flex items-center gap-4 rounded-3xl border p-5 transition hover:-translate-y-0.5"><span className="flex h-11 w-11 items-center justify-center rounded-2xl bg-violet-100 text-violet-700"><Download className="h-5 w-5" /></span><span className="flex-1"><strong className="block text-sm text-slate-950">Export my data</strong><span className="text-xs text-slate-500">Download JSON and your avatar as a ZIP.</span></span><ChevronRight className="h-4 w-4 text-slate-400 transition group-hover:translate-x-1" /></a><div className="rounded-3xl border border-rose-200 bg-rose-50/60 p-5"><strong className="text-sm text-rose-900">Delete account</strong><p className="mt-1 text-xs leading-5 text-rose-700">This permanently removes your profile, libraries, connections, and settings.</p><input type="password" value={password} onChange={event => setPassword(event.target.value)} placeholder="Current password" className="mt-3 w-full rounded-xl border border-rose-200 bg-white px-3 py-2 text-sm outline-none" /><button type="button" disabled={!password} onClick={async () => { if (!window.confirm('Permanently delete your Cantaro account? This cannot be undone.')) return; setError(null); try { await authApi.deleteAccount(password); window.location.assign('/'); } catch (reason) { setError(reason instanceof Error ? reason.message : 'Account deletion failed'); } }} className="mt-3 inline-flex items-center gap-2 rounded-xl bg-rose-700 px-4 py-2 text-xs font-black text-white disabled:opacity-50"><Trash2 className="h-4 w-4" />Delete permanently</button>{error ? <p className="mt-2 text-xs font-bold text-rose-700">{error}</p> : null}</div></div></SettingsSection>;
}

export function SettingsPage() {
  const { user, setProfile } = useAuth();
  const profile = useMemo(() => user, [user]);
  if (!profile) return null;
  return <PageShell sidebar={<SettingsSidebar />} searchPlaceholder="Search settings…" contentClassName="settings-content"><div className="mx-auto max-w-5xl space-y-6"><header className="mb-10"><p className="text-xs font-black tracking-[.3em] text-violet-600 uppercase">Personal control room</p><h1 className="mt-3 text-4xl font-black tracking-[-.04em] text-slate-950 sm:text-5xl">Make Cantaro yours.</h1><p className="mt-3 max-w-2xl text-base leading-7 text-slate-500">Tune the defaults behind every playlist, provider, and late-night library session.</p></header><ProfileSection profile={profile} onProfile={setProfile} /><ConnectionsSection /><PreferencesSections profile={profile} onProfile={setProfile} /><SecuritySection /><DataSection /></div></PageShell>;
}
