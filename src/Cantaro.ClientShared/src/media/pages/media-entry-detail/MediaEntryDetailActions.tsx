import {
  CheckCircle2,
  ChevronDown,
  Clock3,
  Minus,
  MoreVertical,
  Play,
  Plus,
  RotateCw,
  Save,
  Star,
} from 'lucide-react';
import { formatNextReleaseDisplay } from '../../services/mediaFormatting';
import type { MediaContinueWatchingDto } from '../../services/mediaApi';
import type { StreamingDestination } from '../../services/streamingDestinations';
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from '../../services/streamingServices';
import { StreamingServiceIcon } from '../../components/StreamingServiceIcon';
import {
  clampProgressValue,
  getEntryStatusChanged,
  getProgressCapabilities,
  getPrimaryProgressSummary,
  getStatusSaveLabel,
  isStatusRefreshDisabled,
} from './mediaEntryDetailModel';
import { getContinueLinkActions, type ContinueLinkAction } from './continueWatchingAction';
import type {
  ContinueWatchingState,
  MediaEntryDetailContentProps,
} from './mediaEntryDetailTypes';

const NORMALIZED_STATUSES = [
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'planned', label: 'Planning' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
  { value: 'repeating', label: 'Rewatching / Rereading' },
];

function ProgressStepper({
  label,
  value,
  max,
  onChange,
}: {
  label: string;
  value: number | undefined;
  max?: number;
  onChange: (value: number) => void;
}) {
  const currentValue = clampProgressValue(value ?? 0, max);
  const sliderMax = Math.max(max ?? 100, currentValue, 1);
  const canDecrease = currentValue > 0;
  const canIncrease = max ? currentValue < max : true;
  const setProgressValue = (nextValue: number) => onChange(clampProgressValue(nextValue, max));

  return (
    <div className="media-detail-stepper">
      <div className="media-detail-stepper-row">
        <button
          type="button"
          onClick={() => setProgressValue(currentValue - 1)}
          disabled={!canDecrease}
          aria-label={`Decrease ${label.toLowerCase()}`}
        >
          <Minus aria-hidden />
        </button>
        <input
          type="range"
          min={0}
          max={sliderMax}
          step={1}
          value={currentValue}
          onChange={(event) => setProgressValue(Number(event.target.value))}
          aria-label={`${label} progress`}
        />
        <button
          type="button"
          onClick={() => setProgressValue(currentValue + 1)}
          disabled={!canIncrease}
          aria-label={`Increase ${label.toLowerCase()}`}
        >
          <Plus aria-hidden />
        </button>
      </div>
    </div>
  );
}

type ProgressCockpitProps = Pick<
  MediaEntryDetailContentProps,
  'entry'
  | 'selectedStatus'
  | 'progressEpisodes'
  | 'progressChapters'
  | 'progressVolumes'
  | 'isRefreshingProgress'
  | 'isSavingStatus'
  | 'isAddingToLibrary'
  | 'onSetSelectedStatus'
  | 'onSetProgressEpisodes'
  | 'onSetProgressChapters'
  | 'onSetProgressVolumes'
  | 'onRefreshProgress'
  | 'onSaveStatus'
  | 'onAddToLibrary'
>;

function ProgressSyncStatus({ hasStatusChanged }: { hasStatusChanged: boolean }) {
  return (
    <span className={`media-detail-sync-chip ${hasStatusChanged ? 'media-detail-sync-chip--pending' : 'media-detail-sync-chip--ok'}`}>
      {hasStatusChanged ? <Clock3 aria-hidden /> : <CheckCircle2 aria-hidden />}
      {hasStatusChanged ? 'Unsaved' : 'Synced'}
    </span>
  );
}

function ProgressControls({
  props,
  capabilities,
}: {
  props: ProgressCockpitProps;
  capabilities: ReturnType<typeof getProgressCapabilities>;
}) {
  return (
    <>
      {capabilities.supportsEpisodes ? (
        <ProgressStepper label="Episodes" value={props.progressEpisodes} max={props.entry.title.episodeCount} onChange={props.onSetProgressEpisodes} />
      ) : null}
      {capabilities.supportsChapters ? (
        <ProgressStepper label="Chapters" value={props.progressChapters} max={props.entry.title.chapterCount} onChange={props.onSetProgressChapters} />
      ) : null}
      {capabilities.supportsVolumes ? (
        <ProgressStepper label="Volumes" value={props.progressVolumes} max={props.entry.title.volumeCount} onChange={props.onSetProgressVolumes} />
      ) : null}
    </>
  );
}

function StatusSelect({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <select name="status" value={value} onChange={(event) => onChange(event.target.value)}>
      {NORMALIZED_STATUSES.map((status) => (
        <option key={status.value} value={status.value}>{status.label}</option>
      ))}
    </select>
  );
}

function ProgressScoreRow({ props }: { props: ProgressCockpitProps }) {
  return (
    <div className="media-detail-score-row">
      <div className="media-detail-status-control">
        <StatusSelect value={props.selectedStatus} onChange={props.onSetSelectedStatus} />
        <button
          type="button"
          className="media-detail-refresh-status"
          onClick={props.onRefreshProgress}
          disabled={isStatusRefreshDisabled(props.entry.isConnected, props.isSavingStatus, props.isRefreshingProgress)}
          aria-busy={props.isRefreshingProgress}
          aria-label="Refresh progress from provider"
          title="Refresh progress from provider"
        >
          <RotateCw className={props.isRefreshingProgress ? 'media-detail-spin' : ''} aria-hidden />
        </button>
      </div>
      <div className="media-detail-score">
        <p>Your score</p>
        <div aria-label="User score unavailable">
          {[1, 2, 3, 4, 5].map((star) => <Star key={star} aria-hidden />)}
        </div>
      </div>
    </div>
  );
}

export function ProgressCockpit(props: ProgressCockpitProps) {
  const capabilities = getProgressCapabilities(props.entry.title);
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const hasStatusChanged = getEntryStatusChanged(props);

  if (props.entry.viewerStateStatus !== 'loaded') {
    return (
      <section className="media-detail-progress-card media-detail-add-card" aria-busy={props.entry.viewerStateStatus === 'loading'}>
        <div>
          <p className="media-detail-add-kicker">Your library</p>
          <h2>{props.entry.viewerStateStatus === 'loading' ? 'Loading your progress…' : "Couldn't load your progress"}</h2>
          <p>The media details are available independently while Cantaro loads your private library state.</p>
        </div>
      </section>
    );
  }

  if (!props.entry.isInLibrary) {
    return (
      <section className="media-detail-progress-card media-detail-add-card">
        <div>
          <p className="media-detail-add-kicker">Not in your library</p>
          <h2>Track this title</h2>
          <p>The title and its provider links are already part of Cantaro. Adding it only creates your personal progress state.</p>
        </div>
        <StatusSelect value={props.selectedStatus} onChange={props.onSetSelectedStatus} />
        <button
          type="button"
          className="media-detail-primary-action"
          onClick={props.onAddToLibrary}
          disabled={props.isAddingToLibrary}
          aria-busy={props.isAddingToLibrary}
        >
          <Plus aria-hidden />
          <span>{props.isAddingToLibrary ? 'Adding…' : 'Add to your library'}</span>
        </button>
      </section>
    );
  }

  return (
    <section className="media-detail-progress-card">
      <div className="media-detail-progress-next">
        <div className="media-detail-progress-next-head">
          <div>
            <p>{progressSummary.progressLabel}</p>
          </div>
          <ProgressSyncStatus hasStatusChanged={hasStatusChanged} />
        </div>
        <ProgressControls props={props} capabilities={capabilities} />
      </div>
      <ProgressScoreRow props={props} />
    </section>
  );
}

function SaveProgressAction({
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
}: {
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
}) {
  return (
    <button
      type="button"
      className="media-detail-primary-action"
      onClick={onSaveStatus}
      disabled={isSavingStatus || isRefreshingProgress}
      aria-busy={isSavingStatus}
    >
      <Save aria-hidden />
      <span>{getStatusSaveLabel(isSavingStatus)}</span>
    </button>
  );
}

function getContinueWatchingLabel(state: ContinueWatchingState): string {
  if (state.status === 'loading') return 'Finding episode...';
  if (state.status === 'error') return "Couldn't load streaming links";

  const labels: Record<Exclude<MediaContinueWatchingDto['outcome'], 'direct'>, string> = {
    series_fallback: 'Open streaming service',
    completed: 'Completed',
    conflict: 'Episode link needs review',
    unavailable: 'Streaming link not available',
  };
  return state.value.outcome === 'direct'
    ? `Continue episode ${state.value.episodeNumber ?? ''}`.trim()
    : labels[state.value.outcome];
}

function ContinueDestinationLink({
  action,
  className,
  onSelect,
  menuItem = false,
}: {
  action: ContinueLinkAction;
  className?: string;
  onSelect: (serviceId: StreamingServiceId) => void;
  menuItem?: boolean;
}) {
  return (
    <a
      className={className}
      href={action.url}
      target="_blank"
      rel="noopener noreferrer"
      style={menuItem ? { color: STREAMING_SERVICES[action.serviceId].buttonColor } : undefined}
      onClick={(event) => {
        onSelect(action.serviceId);
        if (menuItem) event.currentTarget.closest('details')?.removeAttribute('open');
      }}
    >
      <StreamingServiceIcon
        serviceId={action.serviceId}
        style={{ color: 'currentColor', fill: 'currentColor' }}
        aria-hidden
      />
      <span>{action.label}</span>
    </a>
  );
}

function ContinueDestinationMenu({
  actions,
  onSelect,
}: {
  actions: ContinueLinkAction[];
  onSelect: (serviceId: StreamingServiceId) => void;
}) {
  const primary = actions[0];
  if (!primary) return null;
  return (
    <div className="media-detail-streaming-action">
      <ContinueDestinationLink
        action={primary}
        className="media-detail-primary-action media-detail-streaming-primary"
        onSelect={onSelect}
      />
      {actions.length > 1 ? (
        <details className="media-detail-streaming-menu">
          <summary aria-label="Choose streaming service">
            <ChevronDown aria-hidden />
          </summary>
          <div>
            {actions.map(action => (
              <ContinueDestinationLink
                key={`${action.serviceId}:${action.kind}`}
                action={action}
                onSelect={onSelect}
                menuItem
              />
            ))}
          </div>
        </details>
      ) : null}
    </div>
  );
}

function ContinueUnavailableAction({ state }: { state: ContinueWatchingState }) {
  return (
    <button type="button" className="media-detail-primary-action" disabled>
      <Play aria-hidden />
      <span>{getContinueWatchingLabel(state)}</span>
    </button>
  );
}

const UPCOMING_RELEASE_OUTCOMES = new Set<MediaContinueWatchingDto['outcome']>([
  'series_fallback',
  'unavailable',
]);

function isFutureRelease(timestamp?: string): timestamp is string {
  return Boolean(timestamp && Date.parse(timestamp) > Date.now());
}

function readReleaseEpisodeNumber(label?: string): number | null {
  const value = /\b(?:episode|ep|e)\s*(\d+)\b/i.exec(label ?? '')?.[1];
  return value ? Number(value) : null;
}

function getUpcomingRelease(
  state: ContinueWatchingState,
  nextReleaseAt?: string,
  nextReleaseLabel?: string,
) {
  if (state.status !== 'loaded') return null;
  if (!UPCOMING_RELEASE_OUTCOMES.has(state.value.outcome)) return null;
  if (!isFutureRelease(nextReleaseAt)) return null;

  const releaseEpisodeNumber = readReleaseEpisodeNumber(nextReleaseLabel);
  if (releaseEpisodeNumber !== null && releaseEpisodeNumber !== state.value.episodeNumber) return null;

  return formatNextReleaseDisplay(nextReleaseAt);
}

function ContinueWatchingAction({
  state,
  seriesDestinations,
  episodeDestinations,
  preferredServiceId,
  onSelectStreamingService,
  canonicalTitle,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  state: ContinueWatchingState;
  seriesDestinations: readonly StreamingDestination[];
  episodeDestinations: readonly StreamingDestination[];
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  canonicalTitle: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  const upcomingRelease = getUpcomingRelease(state, nextReleaseAt, nextReleaseLabel);
  if (upcomingRelease) {
    return (
      <button
        type="button"
        className="media-detail-primary-action"
        disabled
        title={`${nextReleaseLabel ?? 'Next episode'} expected ${upcomingRelease.absolute}`}
      >
        <Clock3 aria-hidden />
        <span>Come back {upcomingRelease.relative}</span>
      </button>
    );
  }

  const linkActions = getContinueLinkActions(
    state,
    seriesDestinations,
    episodeDestinations,
    preferredServiceId,
    canonicalTitle,
  );
  if (linkActions.length > 0) {
    return <ContinueDestinationMenu actions={linkActions} onSelect={onSelectStreamingService} />;
  }

  return <ContinueUnavailableAction state={state} />;
}

export function ActionRail({
  hasStatusChanged,
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
  continueWatching,
  seriesDestinations,
  episodeDestinations,
  preferredServiceId,
  onSelectStreamingService,
  canonicalTitle,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
  continueWatching: ContinueWatchingState;
  seriesDestinations: readonly StreamingDestination[];
  episodeDestinations: readonly StreamingDestination[];
  preferredServiceId: StreamingServiceId | null;
  onSelectStreamingService: (serviceId: StreamingServiceId) => void;
  canonicalTitle: string;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  return (
    <div className="media-detail-action-rail">
      {hasStatusChanged ? (
        <SaveProgressAction
          isSavingStatus={isSavingStatus}
          isRefreshingProgress={isRefreshingProgress}
          onSaveStatus={onSaveStatus}
        />
      ) : (
        <ContinueWatchingAction
          state={continueWatching}
          seriesDestinations={seriesDestinations}
          episodeDestinations={episodeDestinations}
          preferredServiceId={preferredServiceId}
          onSelectStreamingService={onSelectStreamingService}
          canonicalTitle={canonicalTitle}
          nextReleaseAt={nextReleaseAt}
          nextReleaseLabel={nextReleaseLabel}
        />
      )}
      <button type="button" className="media-detail-more-action" aria-label="More actions">
        <MoreVertical aria-hidden />
      </button>
    </div>
  );
}
