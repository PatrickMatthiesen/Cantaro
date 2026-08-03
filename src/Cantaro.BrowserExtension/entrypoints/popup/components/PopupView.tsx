import type { ComponentProps } from 'react';
import {
  MediaEntryDetailPage,
  MediaLibraryPage,
} from '@cantaro/client-shared/media';
import { GlassCard, GradientButton } from '@cantaro/client-shared/ui';
import { MediaResolutionPicker } from '../../../lib/MediaResolutionPicker';
import type {
  ResolveMediaObservationRequest,
  SubmitMediaObservationResponse,
} from '../../../lib/mediaObservation';
import type {
  EpisodeTrackingTimeoutPreset,
  EpisodeTrackingTimeoutState,
} from '../../../lib/episodeTrackingTimeout';
import type { PopupTab } from '../../../lib/popupTabPreference';
import { MusicLibrary } from './MusicLibrary';
import { SettingsPanel } from './SettingsPanel';
import { SetupCard } from './SetupCard';

type MediaRoute = { kind: 'library' } | { kind: 'entry'; id: string };
type PopupStatus = { message: string; type: 'success' | 'error' } | null;

interface PopupViewProps {
  activeTab: PopupTab;
  onSelectTab: (tab: PopupTab) => void;
  mediaConfigured: boolean;
  trackingControlsOpen: boolean;
  onToggleTrackingControls: () => void;
  trackingPaused: boolean;
  trackingTimeout: EpisodeTrackingTimeoutState;
  onPauseTracking: (preset: EpisodeTrackingTimeoutPreset) => void | Promise<void>;
  onResumeTracking: () => void | Promise<void>;
  latestResolution: SubmitMediaObservationResponse | null;
  isResolving: boolean;
  resolutionError: string | null;
  onResolveObservation: (observationId: string, request: ResolveMediaObservationRequest) => void | Promise<void>;
  settingsOpen: boolean;
  onOpenSettings: () => void;
  settingsPanelProps: ComponentProps<typeof SettingsPanel>;
  musicLibraryProps: ComponentProps<typeof MusicLibrary>;
  setupCardProps: ComponentProps<typeof SetupCard>;
  mediaRoute: MediaRoute;
  mediaSessionKey: number;
  onNavigateEntry: (id: string) => void;
  onNavigateLibrary: () => void;
  status: PopupStatus;
}

const suppressEmbeddedHeading = () => undefined;

export function PopupView(props: PopupViewProps) {
  return (
    <div className="h-screen overflow-hidden bg-canvas text-content">
      <div className="flex h-full flex-col p-3">
        <PopupHeader {...props} />
        <TrackingControls {...props} />
        <ResolutionPicker {...props} />
        <PopupContent {...props} />
      </div>
      <StatusToast status={props.status} />
    </div>
  );
}

function PopupHeader(props: Pick<PopupViewProps,
  | 'activeTab'
  | 'onSelectTab'
  | 'mediaConfigured'
  | 'trackingControlsOpen'
  | 'onToggleTrackingControls'
  | 'trackingPaused'
  | 'onOpenSettings'
>) {
  return (
    <header className="flex min-h-12 items-center gap-2 rounded-xl border border-border-subtle bg-surface p-1.5">
      <PopupTabNavigation activeTab={props.activeTab} onSelectTab={props.onSelectTab} />
      <span className="min-w-0 flex-1 truncate pl-1 text-sm font-semibold text-content">Cantaro</span>
      <TrackingStatus {...props} />
      <button
        type="button"
        className="inline-flex size-9 items-center justify-center rounded-xl text-content-muted transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        onClick={props.onOpenSettings}
        aria-label="Open extension settings"
        title="Settings"
      >
        <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
          <path strokeLinecap="round" strokeLinejoin="round" d="M9.6 3.7 10 2h4l.4 1.7a8.6 8.6 0 0 1 1.5.9l1.7-.5 2 3.5-1.3 1.2c.1.5.2 1.1.2 1.7s-.1 1.2-.2 1.7l1.3 1.2-2 3.5-1.7-.5a8.6 8.6 0 0 1-1.5.9L14 19h-4l-.4-1.7a8.6 8.6 0 0 1-1.5-.9l-1.7.5-2-3.5 1.3-1.2a7.8 7.8 0 0 1 0-3.4L4.4 7.6l2-3.5 1.7.5a8.6 8.6 0 0 1 1.5-.9Z" />
          <circle cx="12" cy="10.5" r="2.5" />
        </svg>
      </button>
    </header>
  );
}

function PopupTabNavigation({ activeTab, onSelectTab }: Pick<PopupViewProps, 'activeTab' | 'onSelectTab'>) {
  return (
    <nav className="flex rounded-xl bg-slate-950 p-0.5" aria-label="Popup section">
      {(['music', 'media'] as const).map((tab) => (
        <button
          key={tab}
          type="button"
          onClick={() => onSelectTab(tab)}
          className={`min-h-9 rounded-[0.625rem] px-3 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${tab === activeTab
            ? 'bg-surface text-content'
            : 'text-slate-300 hover:bg-white/10 hover:text-white'}`}
          aria-current={tab === activeTab ? 'page' : undefined}
        >
          {tab === 'music' ? 'Music' : 'Media'}
        </button>
      ))}
    </nav>
  );
}

function TrackingStatus(props: Pick<PopupViewProps,
  | 'mediaConfigured'
  | 'trackingControlsOpen'
  | 'onToggleTrackingControls'
  | 'trackingPaused'
>) {
  if (!props.mediaConfigured) {
    return <span className="rounded-lg bg-warning-surface px-2 py-1 text-xs font-semibold text-warning-content">Setup needed</span>;
  }

  return (
    <button
      type="button"
      className={`inline-flex min-h-9 items-center gap-1.5 rounded-xl px-2.5 text-xs font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${props.trackingControlsOpen
        ? 'bg-accent-soft text-accent-strong'
        : 'text-content-muted hover:bg-surface-hover'}`}
      aria-expanded={props.trackingControlsOpen}
      aria-controls="episode-tracking-controls"
      onClick={props.onToggleTrackingControls}
    >
      <span className={`h-2 w-2 rounded-full ${props.trackingPaused ? 'bg-amber-500' : 'bg-emerald-500'}`} aria-hidden />
      {props.trackingPaused ? 'Paused' : 'Tracking'}
    </button>
  );
}

function TrackingControls(props: Pick<PopupViewProps,
  | 'mediaConfigured'
  | 'trackingControlsOpen'
  | 'trackingTimeout'
  | 'onPauseTracking'
  | 'onResumeTracking'
>) {
  if (!props.mediaConfigured || !props.trackingControlsOpen) return null;
  return <div id="episode-tracking-controls" className="mt-2"><EpisodeTrackingTimeoutPanel timeout={props.trackingTimeout} onPause={props.onPauseTracking} onResume={props.onResumeTracking} /></div>;
}

function ResolutionPicker(props: Pick<PopupViewProps,
  | 'mediaConfigured'
  | 'latestResolution'
  | 'isResolving'
  | 'resolutionError'
  | 'onResolveObservation'
>) {
  if (!props.mediaConfigured || !props.latestResolution) return null;
  return <div className="mt-2"><MediaResolutionPicker response={props.latestResolution} surface="popup" resolving={props.isResolving} error={props.resolutionError} onResolve={props.onResolveObservation} /></div>;
}

function PopupContent(props: Pick<PopupViewProps,
  | 'activeTab'
  | 'mediaConfigured'
  | 'settingsOpen'
  | 'settingsPanelProps'
  | 'musicLibraryProps'
  | 'setupCardProps'
  | 'mediaRoute'
  | 'mediaSessionKey'
  | 'onNavigateEntry'
  | 'onNavigateLibrary'
>) {
  return (
    <main className="relative mt-2 min-h-0 flex-1 overflow-y-auto overflow-x-hidden rounded-xl bg-surface-translucent" aria-label={`${props.activeTab === 'music' ? 'Music' : 'Media'} content`}>
      <PopupContentBody {...props} />
    </main>
  );
}

function PopupContentBody(props: Parameters<typeof PopupContent>[0]) {
  if (props.settingsOpen) return <SettingsPanel {...props.settingsPanelProps} />;
  if (props.activeTab === 'music') return <div className="h-full py-1"><MusicLibrary {...props.musicLibraryProps} /></div>;
  if (!props.mediaConfigured) return <div className="h-full py-1"><SetupCard {...props.setupCardProps} /></div>;
  if (props.mediaRoute.kind === 'library') {
    return <MediaLibraryPage key={`library-${props.mediaSessionKey}`} embedded density="compact" onHeadingChange={suppressEmbeddedHeading} onNavigateEntry={props.onNavigateEntry} />;
  }
  return <MediaEntryDetailPage key={`entry-${props.mediaSessionKey}-${props.mediaRoute.id}`} libraryEntryId={props.mediaRoute.id} embedded onNavigateBack={props.onNavigateLibrary} />;
}

function StatusToast({ status }: { status: PopupStatus }) {
  if (!status) return null;
  return (
    <div className="pointer-events-none absolute right-4 bottom-4 z-50">
      <div
        className={`rounded-2xl border px-4 py-3 text-sm font-medium shadow-xl ${status.type === 'success'
          ? 'border-success-border bg-success-surface text-success-content'
          : 'border-danger-border bg-danger-surface text-danger-content'}`}
        role="status"
        aria-live="polite"
      >
        {status.message}
      </div>
    </div>
  );
}

function EpisodeTrackingTimeoutPanel({ timeout, onPause, onResume }: {
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
          <p className="text-sm font-semibold text-content">Episode tracking</p>
          <p className="truncate text-xs text-content-muted">{active ? `Paused until ${disabledUntil.toLocaleString()}` : 'Active · pause observations for'}</p>
        </div>
        <button className="min-h-9 rounded-lg bg-surface-subtle px-2.5 text-xs font-semibold text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus" type="button" onClick={() => onPause('30m')}>30 min</button>
        <button className="min-h-9 rounded-lg bg-surface-subtle px-2.5 text-xs font-semibold text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus" type="button" onClick={() => onPause('2h')}>2 hours</button>
        <button className="min-h-9 rounded-lg bg-surface-subtle px-2.5 text-xs font-semibold text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus" type="button" onClick={() => onPause('tomorrow')}>Tomorrow</button>
        {active ? <GradientButton tone="soft" className="min-h-9 rounded-lg px-2.5 py-1 text-xs" onClick={onResume}>Resume</GradientButton> : null}
      </div>
    </GlassCard>
  );
}
