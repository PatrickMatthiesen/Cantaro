import { GradientButton } from '@cantaro/client-shared/ui';
import type { EpisodeTrackingPausePreset } from '../../features/media/settings/episodeTrackingPreference';

interface EpisodeTrackingControlsProps {
  paused: boolean;
  pausedUntil: Date | null;
  onPause: (preset: EpisodeTrackingPausePreset) => Promise<void>;
  onResume: () => Promise<void>;
}

export function EpisodeTrackingControls({ paused, pausedUntil, onPause, onResume }: EpisodeTrackingControlsProps) {
  return (
    <details className="border-b border-border-subtle bg-surface px-3 py-2">
      <summary className="flex min-h-9 cursor-pointer list-none items-center gap-2 px-2 text-sm font-semibold text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus">
        <span className={`size-2 rounded-full ${paused ? 'bg-amber-500' : 'bg-emerald-500'}`} aria-hidden />
        <span>{paused ? 'Episode tracking paused' : 'Episode tracking active'}</span>
        <span className="ml-auto text-xs font-normal text-content-muted">Manage</span>
      </summary>
      <div className="flex items-center gap-2 px-2 pt-2 pb-1">
        <p className="min-w-0 flex-1 truncate text-xs text-content-muted">
          {pausedUntil && paused ? `Paused until ${pausedUntil.toLocaleString()}` : 'Pause new watch observations for:'}
        </p>
        {(['30m', '2h', 'tomorrow'] as const).map((preset) => (
          <TrackingAction key={preset} preset={preset} onPause={onPause} />
        ))}
        {paused ? (
          <GradientButton tone="soft" className="min-h-9 px-2.5 py-1 text-xs" onClick={() => void onResume()}>
            Resume
          </GradientButton>
        ) : null}
      </div>
    </details>
  );
}

function TrackingAction({ preset, onPause }: {
  preset: EpisodeTrackingPausePreset;
  onPause: (preset: EpisodeTrackingPausePreset) => Promise<void>;
}) {
  const label = preset === '30m' ? '30 min' : preset === '2h' ? '2 hours' : 'Tomorrow';
  return (
    <button
      className="min-h-9 border border-border-subtle bg-surface-subtle px-2.5 text-xs font-semibold text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      type="button"
      onClick={() => void onPause(preset)}
    >
      {label}
    </button>
  );
}
